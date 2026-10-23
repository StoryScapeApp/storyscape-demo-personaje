using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Conversación por voz en tiempo real con un agente de ElevenLabs (WebSocket).
/// - Envía el micrófono en PCM 16 kHz y reproduce la voz del agente (PCM 16 kHz).
/// - Pasa {{termino}} y {{significado}} como variables dinámicas del agente.
/// - El Agent ID se lee de StreamingAssets/elevenlabs.local.json (no se sube a GitHub).
/// - Modo simulado: muestra un diálogo de prueba sin conectarse (no gasta minutos).
/// - Expone IsAgentTalking y OutputLevel para animar la boca del personaje.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class ElevenLabsAgentClient : MonoBehaviour
{
    [Header("Configuración")]
    [Tooltip("Archivo en StreamingAssets con { \"agentId\": \"...\" }")]
    public string configFileName = "elevenlabs.local.json";
    [Tooltip("No se conecta a ElevenLabs: muestra un diálogo simulado y no gasta minutos")]
    public bool simulate = true;
    [Tooltip("Debe coincidir con el formato PCM configurado en el agente")]
    public int sampleRate = 16000;

    [Header("Micrófono")]
    [Tooltip("Vacío = micrófono predeterminado de Windows")]
    public string microphoneDevice = "";
    [Tooltip("Envía silencio mientras habla el personaje, para que no se escuche a sí mismo por los parlantes. " +
             "Desactívalo si usas audífonos y quieres probar las interrupciones.")]
    public bool muteMicWhileAgentSpeaks = true;

    [Header("Subtítulos")]
    public bool showSubtitles = true;
    public int subtitleFontSize = 22;

    [Header("Eventos")]
    public UnityEvent onConversationStarted;
    public UnityEvent onConversationEnded;

    public bool IsRunning => running;
    public bool AgentSpeaking { get { lock (playLock) return playQueue.Count > 0; } }
    /// <summary>El personaje está hablando (voz real o diálogo simulado).</summary>
    public bool IsAgentTalking => AgentSpeaking || SimulatedTalking;
    /// <summary>True mientras el modo simulado "dice" una línea.</summary>
    public bool SimulatedTalking { get; private set; }
    /// <summary>Volumen actual de la voz del agente (RMS, aprox. 0 a 0.3).</summary>
    public float OutputLevel => outputLevel;

    const string Endpoint = "wss://api.elevenlabs.io/v1/convai/conversation?agent_id=";
    const int ChunkSamples = 1600; // 100 ms a 16 kHz

    ClientWebSocket ws;
    CancellationTokenSource cts;
    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    readonly Queue<float> playQueue = new Queue<float>();
    readonly object playLock = new object();
    readonly List<float> micPending = new List<float>();
    volatile float outputLevel;

    AudioSource source;
    AudioClip micClip;
    string micDevice;
    int micReadPos;
    bool running;
    string subtitleAgent = "", subtitleUser = "";
    GUIStyle agentStyle, userStyle;

    // ───────────── Mensajes del servidor (solo los campos que usamos) ─────────────
    [Serializable] class ServerMsg
    {
        public string type;
        public AudioEvt audio_event;
        public PingEvt ping_event;
        public UserTranscriptEvt user_transcription_event;
        public AgentResponseEvt agent_response_event;
        public InitMetaEvt conversation_initiation_metadata_event;
    }
    [Serializable] class AudioEvt { public string audio_base_64; public int event_id; }
    [Serializable] class PingEvt { public int event_id; public int ping_ms; }
    [Serializable] class UserTranscriptEvt { public string user_transcript; }
    [Serializable] class AgentResponseEvt { public string agent_response; }
    [Serializable] class InitMetaEvt { public string conversation_id; public string agent_output_audio_format; public string user_input_audio_format; }
    [Serializable] class LocalConfig { public string agentId; }

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.clip = AudioClip.Create("VozAgente", sampleRate * 2, 1, sampleRate, true, OnAudioRead);
        source.Play();
    }

    // ───────────── API pública ─────────────
    public void StartConversation(string termino, string significado)
    {
        if (running) return;

        if (simulate)
        {
            StartCoroutine(Simulate(termino));
            return;
        }
        if (!TryLoadAgentId(out string agentId))
        {
            Debug.LogWarning("[ElevenLabs] No se encontró un agentId en StreamingAssets/" + configFileName +
                             ". Se usa el modo simulado.");
            StartCoroutine(Simulate(termino));
            return;
        }
        _ = RunAsync(agentId, termino, significado);
    }

    public void EndConversation()
    {
        try { cts?.Cancel(); } catch { }
    }

    // ───────────── Conexión ─────────────
    async Task RunAsync(string agentId, string termino, string significado)
    {
        running = true;
        subtitleAgent = subtitleUser = "";
        cts = new CancellationTokenSource();
        ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri(Endpoint + agentId), cts.Token);

            string init = "{\"type\":\"conversation_initiation_client_data\",\"dynamic_variables\":{" +
                          "\"termino\":" + Json(termino) + ",\"significado\":" + Json(significado) + "}}";
            await SendText(init);

            StartMic();
            onConversationStarted?.Invoke();
            await ReceiveLoop();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError("[ElevenLabs] " + e.Message);
        }
        finally
        {
            StopMic();
            ClearPlayback();
            try
            {
                if (ws != null && ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "fin", CancellationToken.None);
            }
            catch { }
            ws?.Dispose();
            ws = null;
            running = false;
            subtitleAgent = subtitleUser = "";
            onConversationEnded?.Invoke();
        }
    }

    async Task ReceiveLoop()
    {
        var buffer = new byte[64 * 1024];
        var ms = new MemoryStream();
        while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
        {
            ms.SetLength(0);
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    Debug.Log($"[ElevenLabs] Conversación cerrada por el servidor: {r.CloseStatus} {r.CloseStatusDescription}");
                    return;
                }
                ms.Write(buffer, 0, r.Count);
            } while (!r.EndOfMessage);

            Handle(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
        }
    }

    void Handle(string json)
    {
        ServerMsg m;
        try { m = JsonUtility.FromJson<ServerMsg>(json); }
        catch { return; }
        if (m == null || m.type == null) return;

        switch (m.type)
        {
            case "conversation_initiation_metadata":
                var meta = m.conversation_initiation_metadata_event;
                if (meta != null)
                {
                    Debug.Log($"[ElevenLabs] Conectado. Conversación {meta.conversation_id} | salida {meta.agent_output_audio_format} | entrada {meta.user_input_audio_format}");
                    if (!string.IsNullOrEmpty(meta.agent_output_audio_format) && !meta.agent_output_audio_format.StartsWith("pcm_" + sampleRate))
                        Debug.LogWarning("[ElevenLabs] El agente no está en PCM " + sampleRate + " Hz. Cámbialo en la pestaña Voice del agente.");
                }
                break;

            case "audio":
                if (m.audio_event != null && !string.IsNullOrEmpty(m.audio_event.audio_base_64))
                    EnqueuePcm(Convert.FromBase64String(m.audio_event.audio_base_64));
                break;

            case "agent_response":
                if (m.agent_response_event != null) subtitleAgent = m.agent_response_event.agent_response;
                break;

            case "user_transcript":
                if (m.user_transcription_event != null) subtitleUser = m.user_transcription_event.user_transcript;
                break;

            case "interruption":
                ClearPlayback();
                break;

            case "ping":
                if (m.ping_event != null)
                    _ = SendText("{\"type\":\"pong\",\"event_id\":" + m.ping_event.event_id + "}");
                break;
        }
    }

    async Task SendText(string text)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;
        await sendLock.WaitAsync();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
        catch (Exception e)
        {
            if (!(e is OperationCanceledException)) Debug.LogWarning("[ElevenLabs] Error al enviar: " + e.Message);
        }
        finally { sendLock.Release(); }
    }

    // ───────────── Micrófono ─────────────
    void StartMic()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[ElevenLabs] No se detectó ningún micrófono.");
            return;
        }
        micDevice = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
        micClip = Microphone.Start(micDevice, true, 1, sampleRate);
        micReadPos = 0;
        micPending.Clear();
    }

    void StopMic()
    {
        if (micClip != null) Microphone.End(micDevice);
        micClip = null;
    }

    void Update()
    {
        if (micClip == null || ws == null || ws.State != WebSocketState.Open) return;

        int pos = Microphone.GetPosition(micDevice);
        if (pos < 0 || pos == micReadPos) return;

        int total = micClip.samples;
        int count = pos > micReadPos ? pos - micReadPos : total - micReadPos + pos;
        var data = new float[count];
        if (pos > micReadPos)
        {
            micClip.GetData(data, micReadPos);
        }
        else
        {
            var tail = new float[total - micReadPos];
            micClip.GetData(tail, micReadPos);
            Array.Copy(tail, data, tail.Length);
            if (pos > 0)
            {
                var head = new float[pos];
                micClip.GetData(head, 0);
                Array.Copy(head, 0, data, tail.Length, pos);
            }
        }
        micReadPos = pos;

        bool silence = muteMicWhileAgentSpeaks && AgentSpeaking;
        for (int i = 0; i < data.Length; i++) micPending.Add(silence ? 0f : data[i]);

        while (micPending.Count >= ChunkSamples)
        {
            var bytes = new byte[ChunkSamples * 2];
            for (int i = 0; i < ChunkSamples; i++)
            {
                short s = (short)Mathf.Clamp(micPending[i] * 32767f, short.MinValue, short.MaxValue);
                bytes[i * 2] = (byte)(s & 0xff);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
            }
            micPending.RemoveRange(0, ChunkSamples);
            _ = SendText("{\"user_audio_chunk\":\"" + Convert.ToBase64String(bytes) + "\"}");
        }
    }

    // ───────────── Reproducción ─────────────
    void EnqueuePcm(byte[] pcm)
    {
        lock (playLock)
        {
            for (int i = 0; i + 1 < pcm.Length; i += 2)
            {
                short s = (short)(pcm[i] | (pcm[i + 1] << 8));
                playQueue.Enqueue(s / 32768f);
            }
        }
    }

    void ClearPlayback()
    {
        lock (playLock) playQueue.Clear();
        outputLevel = 0f;
    }

    void OnAudioRead(float[] data)
    {
        float sum = 0f;
        int n = 0;
        lock (playLock)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (playQueue.Count > 0)
                {
                    float v = playQueue.Dequeue();
                    data[i] = v;
                    sum += v * v;
                    n++;
                }
                else data[i] = 0f;
            }
        }
        outputLevel = n > 0 ? Mathf.Sqrt(sum / data.Length) : 0f;
    }

    // ───────────── Modo simulado ─────────────
    IEnumerator Simulate(string termino)
    {
        running = true;
        onConversationStarted?.Invoke();
        subtitleUser = "";

        subtitleAgent = $"¡Hola! En esta página está resaltado \"{termino}\". ¿Me ayudas a descubrir qué significa?";
        yield return SimSay(subtitleAgent);
        yield return new WaitForSeconds(1f);

        subtitleUser = "(simulación) Respuesta del niño…";
        yield return new WaitForSeconds(3f);

        subtitleUser = "";
        subtitleAgent = "¡Muy bien! Esa es la idea. Ya puedes seguir leyendo la siguiente página.";
        yield return SimSay(subtitleAgent);
        yield return new WaitForSeconds(1.5f);

        subtitleAgent = "";
        running = false;
        onConversationEnded?.Invoke();
    }

    IEnumerator SimSay(string line)
    {
        SimulatedTalking = true;
        yield return new WaitForSeconds(Mathf.Clamp(line.Length * 0.055f, 1.5f, 6f));
        SimulatedTalking = false;
    }

    // ───────────── Utilidades ─────────────
    bool TryLoadAgentId(out string agentId)
    {
        agentId = null;
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);
        if (!File.Exists(path)) return false;
        try
        {
            var cfg = JsonUtility.FromJson<LocalConfig>(File.ReadAllText(path));
            agentId = cfg?.agentId?.Trim();
        }
        catch { return false; }
        return !string.IsNullOrEmpty(agentId) && !agentId.StartsWith("PEGA_");
    }

    static string Json(string s)
    {
        if (s == null) return "\"\"";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }

    void OnGUI()
    {
        if (!showSubtitles || (string.IsNullOrEmpty(subtitleAgent) && string.IsNullOrEmpty(subtitleUser))) return;
        if (agentStyle == null)
        {
            agentStyle = new GUIStyle(GUI.skin.label) { fontSize = subtitleFontSize, wordWrap = true, alignment = TextAnchor.LowerCenter };
            agentStyle.normal.textColor = Color.white;
            userStyle = new GUIStyle(agentStyle) { fontStyle = FontStyle.Italic };
            userStyle.normal.textColor = new Color(0.6f, 0.85f, 1f);
        }
        float w = Screen.width * 0.8f, x = (Screen.width - w) / 2f;
        if (!string.IsNullOrEmpty(subtitleUser))
            GUI.Label(new Rect(x, Screen.height - 150, w, 40), subtitleUser, userStyle);
        if (!string.IsNullOrEmpty(subtitleAgent))
            GUI.Label(new Rect(x, Screen.height - 110, w, 90), subtitleAgent, agentStyle);
    }

    void OnDestroy()
    {
        EndConversation();
        StopMic();
    }
}
