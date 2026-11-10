using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Conversación por voz en tiempo real con un agente de ElevenLabs (WebSocket).
/// - Graba el micrófono a su frecuencia nativa, lo amplifica (Mic Gain), lo convierte a PCM 16 kHz y lo envía.
/// - Reproduce la voz del agente (PCM 16 kHz).
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
    [Tooltip("Campo del archivo con el ID de este agente: agentId (dudas) o agentIdBienvenida (onboarding)")]
    public string configKey = "agentId";
    [Tooltip("No se conecta a ElevenLabs: muestra un diálogo simulado y no gasta minutos")]
    public bool simulate = true;
    [Tooltip("Modo simulado: palabra que \"pregunta\" el niño (prueba el resaltado sin gastar minutos)")]
    public string simulatedWord = "excusa";
    [TextArea] public string simulatedMeaning = "Razón que das para explicar por qué hiciste algo.";
    [Tooltip("Debe coincidir con el formato PCM configurado en el agente")]
    public int sampleRate = 16000;

    [Header("Micrófono")]
    [Tooltip("Nombre exacto del micrófono (ver consola al dar Play). Vacío = predeterminado de Windows")]
    public string microphoneDevice = "";
    [Tooltip("Amplifica el micrófono. Ajústalo durante el Play: al hablar con voz normal la barra verde " +
             "debería llegar más o menos a la mitad, sin quedarse llena todo el tiempo.")]
    [Range(1f, 30f)] public float micGain = 1f;
    [Tooltip("Envía silencio mientras habla el personaje, para que no se escuche a sí mismo por los parlantes. " +
             "Desactívalo si usas audífonos y quieres probar las interrupciones.")]
    public bool muteMicWhileAgentSpeaks = true;
    [Tooltip("Muestra un medidor del micrófono en pantalla durante la conversación")]
    public bool showMicLevel = true;
    [Tooltip("No reproduce la voz del agente (agente que solo escucha, p. ej. durante la lectura)")]
    public bool muteOutput = false;

    [Header("Subtítulos")]
    public bool showSubtitles = true;
    public int subtitleFontSize = 22;

    [Header("Eventos")]
    public UnityEvent onConversationStarted;
    public UnityEvent onConversationEnded;

    public bool IsRunning => running;
    /// <summary>True si la última conversación llegó a conectarse con ElevenLabs.</summary>
    public bool LastConnected { get; private set; }
    /// <summary>True si hay un Agent ID configurado (archivo local) para este agente.</summary>
    public bool HasAgentId() => TryLoadAgentId(out _);
    public bool AgentSpeaking { get { lock (playLock) return playQueue.Count > 0; } }
    /// <summary>El personaje está hablando (voz real o diálogo simulado).</summary>
    public bool IsAgentTalking => AgentSpeaking || SimulatedTalking;
    /// <summary>True mientras el modo simulado "dice" una línea.</summary>
    public bool SimulatedTalking { get; private set; }
    /// <summary>Volumen actual de la voz del agente (RMS, aprox. 0 a 0.3).</summary>
    public float OutputLevel => outputLevel;
    /// <summary>Volumen actual del micrófono ya amplificado (RMS).</summary>
    public float MicLevel => micLevel;

    /// <summary>
    /// Herramientas de cliente (Client tools) del agente, p. ej. "resaltar_palabra".
    /// Recibe (nombre, parámetros) y devuelve el texto que se responde al agente, o null si no la reconoce.
    /// </summary>
    public Func<string, ToolArgs, string> ClientToolHandler;

    /// <summary>Texto que el agente transcribió de lo que dijo el niño (cada turno).</summary>
    public event Action<string> UserTranscript;

    readonly Dictionary<string, Func<ToolArgs, string>> tools = new Dictionary<string, Func<ToolArgs, string>>();
    /// Registra una herramienta de cliente por nombre (tiene prioridad sobre ClientToolHandler).
    public void RegisterTool(string name, Func<ToolArgs, string> handler) { if (handler == null) tools.Remove(name); else tools[name] = handler; }

    /// Envía información al agente sin interrumpirlo (p. ej. el niño cambió de página).
    public void SendContextualUpdate(string text)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;
        _ = SendText("{\"type\":\"contextual_update\",\"text\":" + Json(text) + "}");
    }

    const string Endpoint = "wss://api.elevenlabs.io/v1/convai/conversation?agent_id=";
    const int ChunkSamples = 1600; // 100 ms a 16 kHz

    ClientWebSocket ws;
    CancellationTokenSource cts;
    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    readonly Queue<float> playQueue = new Queue<float>();
    readonly object playLock = new object();
    readonly List<float> micRaw = new List<float>();     // a la frecuencia del micrófono
    readonly List<float> micPending = new List<float>(); // ya convertido a sampleRate
    volatile float outputLevel;
    float micLevel;
    bool micMuted;

    AudioSource source;
    AudioClip micClip;
    string micDevice;
    int micRate;
    int micReadPos;
    double resamplePos;
    bool running;
    string subtitleAgent = "", subtitleUser = "";
    GUIStyle agentStyle, userStyle, meterStyle;
    Texture2D whiteTex;

    static readonly Regex AudioTags = new Regex(@"\[[^\]]*\]\s*", RegexOptions.Compiled);

    // ───────────── Mensajes del servidor (solo los campos que usamos) ─────────────
    [Serializable] class ServerMsg
    {
        public string type;
        public AudioEvt audio_event;
        public PingEvt ping_event;
        public UserTranscriptEvt user_transcription_event;
        public AgentResponseEvt agent_response_event;
        public InitMetaEvt conversation_initiation_metadata_event;
        public ClientToolCallEvt client_tool_call;
    }
    [Serializable] class AudioEvt { public string audio_base_64; public int event_id; }
    [Serializable] class PingEvt { public int event_id; public int ping_ms; }
    [Serializable] class UserTranscriptEvt { public string user_transcript; }
    [Serializable] class AgentResponseEvt { public string agent_response; }
    [Serializable] class InitMetaEvt { public string conversation_id; public string agent_output_audio_format; public string user_input_audio_format; }
    [Serializable] class LocalConfig { public string agentId; public string agentIdBienvenida; public string agentIdLectura; }
    [Serializable] class ClientToolCallEvt { public string tool_name; public string tool_call_id; public ToolArgs parameters; }
    /// Parámetros de las herramientas de cliente del agente (los que una herramienta no usa llegan vacíos).
    [Serializable] public class ToolArgs { public string palabra; public string significado; public string animacion; }

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.clip = AudioClip.Create("VozAgente", sampleRate * 2, 1, sampleRate, true, OnAudioRead);
        source.Play();
    }

    void Start()
    {
        var devs = Microphone.devices;
        Debug.Log("[ElevenLabs] Micrófonos disponibles: " + (devs.Length == 0 ? "(ninguno)" : string.Join(" | ", devs)));
    }

    // ───────────── API pública ─────────────
    /// Inicia la conversación con cualquier conjunto de variables dinámicas del agente
    /// (p. ej. pagina_actual y texto_leido para el modo "Principito, tengo una duda").
    public void StartConversation(Dictionary<string, string> variables)
    {
        if (running) return;
        string summary = variables != null && variables.TryGetValue("pagina_actual", out var pg) ? "página " + pg : "";
        if (simulate) { StartCoroutine(Simulate(summary)); return; }
        if (!TryLoadAgentId(out string agentId))
        {
            Debug.LogWarning("[ElevenLabs] No se encontró un agentId en StreamingAssets/" + configFileName + ". Se usa el modo simulado.");
            StartCoroutine(Simulate(summary));
            return;
        }
        _ = RunAsync(agentId, variables ?? new Dictionary<string, string>());
    }

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
        _ = RunAsync(agentId, new Dictionary<string, string> { { "termino", termino }, { "significado", significado } });
    }

    public void EndConversation()
    {
        try { cts?.Cancel(); } catch { }
    }

    // ───────────── Conexión ─────────────
    async Task RunAsync(string agentId, Dictionary<string, string> variables)
    {
        running = true;
        LastConnected = false;
        subtitleAgent = subtitleUser = "";
        cts = new CancellationTokenSource();
        ws = new ClientWebSocket();
        EnsurePlaying();
        try
        {
            await ws.ConnectAsync(new Uri(Endpoint + agentId), cts.Token);

            var sbVars = new StringBuilder();
            foreach (var kv in variables)
            {
                if (sbVars.Length > 0) sbVars.Append(',');
                sbVars.Append(Json(kv.Key)).Append(':').Append(Json(kv.Value));
            }
            string init = "{\"type\":\"conversation_initiation_client_data\",\"dynamic_variables\":{" + sbVars + "}}";
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
            if (cts != null && !cts.IsCancellationRequested)
            {
                for (int i = 0; i < 300 && AgentSpeaking; i++) await Task.Delay(50);   // máx. 15 s
            }
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
                    LastConnected = true;
                    Debug.Log($"[ElevenLabs] Conectado. Conversación {meta.conversation_id} | salida {meta.agent_output_audio_format} | entrada {meta.user_input_audio_format}");
                    if (!string.IsNullOrEmpty(meta.agent_output_audio_format) && !meta.agent_output_audio_format.StartsWith("pcm_" + sampleRate))
                        Debug.LogWarning("[ElevenLabs] El agente no está en PCM " + sampleRate + " Hz. Cámbialo en la pestaña Voice del agente.");
                }
                break;

            case "audio":
                if (!muteOutput && m.audio_event != null && !string.IsNullOrEmpty(m.audio_event.audio_base_64))
                {
                    EnqueuePcm(Convert.FromBase64String(m.audio_event.audio_base_64));
                    EnsurePlaying();
                }
                break;

            case "agent_response":
                if (m.agent_response_event != null)
                {
                    subtitleAgent = StripTags(m.agent_response_event.agent_response);
                    Debug.Log("[ElevenLabs] Personaje: " + subtitleAgent);
                }
                break;

            case "user_transcript":
                if (m.user_transcription_event != null)
                {
                    subtitleUser = m.user_transcription_event.user_transcript;
                    Debug.Log("[ElevenLabs] Niño: " + subtitleUser);
                    UserTranscript?.Invoke(subtitleUser);
                }
                break;

            case "interruption":
                ClearPlayback();
                break;

            case "client_tool_call":
                HandleClientTool(m.client_tool_call);
                break;

            case "ping":
                if (m.ping_event != null)
                    _ = SendText("{\"type\":\"pong\",\"event_id\":" + m.ping_event.event_id + "}");
                break;
        }
    }

    void HandleClientTool(ClientToolCallEvt call)
    {
        if (call == null || string.IsNullOrEmpty(call.tool_call_id)) return;
        Debug.Log($"[ElevenLabs] Herramienta: {call.tool_name} (palabra: \"{call.parameters?.palabra}\")");
        string result;
        bool error = false;
        try
        {
            var args = call.parameters ?? new ToolArgs();
            result = tools.TryGetValue(call.tool_name ?? "", out var fn) ? fn(args) : ClientToolHandler?.Invoke(call.tool_name, args);
            if (result == null) { result = "Herramienta no disponible: " + call.tool_name; error = true; }
        }
        catch (Exception e) { result = "Error: " + e.Message; error = true; Debug.LogException(e); }
        _ = SendText("{\"type\":\"client_tool_result\",\"tool_call_id\":" + Json(call.tool_call_id) +
                     ",\"result\":" + Json(result) + ",\"is_error\":" + (error ? "true" : "false") + "}");
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

        // Graba a la frecuencia que soporta el micrófono; luego se convierte a sampleRate.
        Microphone.GetDeviceCaps(micDevice, out int minFreq, out int maxFreq);
        if (minFreq == 0 && maxFreq == 0) micRate = sampleRate;             // acepta cualquier frecuencia
        else if (sampleRate >= minFreq && sampleRate <= maxFreq) micRate = sampleRate;
        else micRate = maxFreq;

        micClip = Microphone.Start(micDevice, true, 1, micRate);
        micReadPos = 0;
        resamplePos = 0;
        micRaw.Clear();
        micPending.Clear();
        Debug.Log($"[ElevenLabs] Grabando con \"{(micDevice ?? "micrófono predeterminado")}\" a {micRate} Hz → envío a {sampleRate} Hz (ganancia x{micGain:0.#})");
    }

    void StopMic()
    {
        if (micClip != null) Microphone.End(micDevice);
        micClip = null;
        micLevel = 0f;
    }

    void Update()
    {
        // La voz del personaje se reproduce en un AudioSource "infinito". Si Unity lo detiene
        // (p. ej. al perder el foco), se reanuda; si no, el audio quedaría acumulado y el micrófono en pausa.
        if (running || AgentSpeaking) EnsurePlaying();

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

        // Ganancia + nivel del micrófono (para el medidor en pantalla)
        float sum = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float v = Mathf.Clamp(data[i] * micGain, -1f, 1f);
            data[i] = v;
            sum += v * v;
        }
        micLevel = Mathf.Lerp(micLevel, Mathf.Sqrt(sum / Mathf.Max(1, data.Length)), 0.5f);

        // Solo se silencia si la voz del personaje realmente se está reproduciendo.
        micMuted = muteMicWhileAgentSpeaks && AgentSpeaking && source.isPlaying;
        micRaw.AddRange(data);
        Resample();

        while (micPending.Count >= ChunkSamples)
        {
            var bytes = new byte[ChunkSamples * 2];
            for (int i = 0; i < ChunkSamples; i++)
            {
                float v = micMuted ? 0f : micPending[i];
                short s = (short)Mathf.Clamp(v * 32767f, short.MinValue, short.MaxValue);
                bytes[i * 2] = (byte)(s & 0xff);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
            }
            micPending.RemoveRange(0, ChunkSamples);
            _ = SendText("{\"user_audio_chunk\":\"" + Convert.ToBase64String(bytes) + "\"}");
        }
    }

    /// Convierte micRaw (micRate) a micPending (sampleRate) promediando cada ventana (evita distorsión).
    void Resample()
    {
        if (micRate == sampleRate)
        {
            micPending.AddRange(micRaw);
            micRaw.Clear();
            return;
        }

        double step = (double)micRate / sampleRate; // p. ej. 48000 / 16000 = 3
        while (resamplePos + step <= micRaw.Count)
        {
            int start = (int)resamplePos;
            int end = Mathf.Max(start + 1, (int)(resamplePos + step));
            float acc = 0f;
            for (int i = start; i < end; i++) acc += micRaw[i];
            micPending.Add(acc / (end - start));
            resamplePos += step;
        }

        int consumed = (int)resamplePos;
        if (consumed > 0)
        {
            micRaw.RemoveRange(0, consumed);
            resamplePos -= consumed;
        }
    }

    // ───────────── Reproducción ─────────────
    void EnsurePlaying()
    {
        if (source != null && source.enabled && !source.isPlaying) source.Play();
    }

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

        if (ClientToolHandler != null && !string.IsNullOrEmpty(simulatedWord))
        {
            ClientToolHandler("resaltar_palabra", new ToolArgs { palabra = simulatedWord, significado = simulatedMeaning });
            yield return SimSay("Simulación: explicando " + simulatedWord + ". " + simulatedMeaning);
            yield return new WaitForSeconds(2f);
        }

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
            agentId = (configKey == "agentIdBienvenida" ? cfg?.agentIdBienvenida
                     : configKey == "agentIdLectura" ? cfg?.agentIdLectura : cfg?.agentId)?.Trim();
        }
        catch { return false; }
        return !string.IsNullOrEmpty(agentId) && !agentId.StartsWith("PEGA_");
    }

    /// Quita etiquetas de estilo de voz como [slow] o [laughs] de los subtítulos.
    static string StripTags(string s) => string.IsNullOrEmpty(s) ? s : AudioTags.Replace(s, "").Trim();

    static string Json(string s)
    {
        if (s == null) return "\"\"";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ") + "\"";
    }

    void OnGUI()
    {
        if (agentStyle == null)
        {
            agentStyle = new GUIStyle(GUI.skin.label) { fontSize = subtitleFontSize, wordWrap = true, alignment = TextAnchor.LowerCenter };
            agentStyle.normal.textColor = Color.white;
            userStyle = new GUIStyle(agentStyle) { fontStyle = FontStyle.Italic };
            userStyle.normal.textColor = new Color(0.6f, 0.85f, 1f);
            meterStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            meterStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            whiteTex = Texture2D.whiteTexture;
        }

        // Medidor del micrófono (esquina superior izquierda)
        if (showMicLevel && running && !simulate && micClip != null)
        {
            float lvl = Mathf.Clamp01(micLevel * 8f);
            string label = micMuted ? "Mic: en pausa (habla el personaje)" : $"Mic: escuchando  (ganancia x{micGain:0.#})";
            GUI.Label(new Rect(16, 10, 360, 20), label, meterStyle);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(new Rect(16, 32, 160, 8), whiteTex);
            GUI.color = micMuted ? new Color(1f, 0.6f, 0.2f) : (lvl > 0.95f ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 1f, 0.4f));
            GUI.DrawTexture(new Rect(16, 32, 160 * lvl, 8), whiteTex);
            GUI.color = Color.white;
        }

        if (!showSubtitles || (string.IsNullOrEmpty(subtitleAgent) && string.IsNullOrEmpty(subtitleUser))) return;
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
