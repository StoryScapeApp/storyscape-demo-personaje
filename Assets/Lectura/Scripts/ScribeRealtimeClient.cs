using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Transcripción en tiempo real con ElevenLabs Scribe v2 Realtime (Speech to Text por WebSocket).
/// A diferencia del agente, entrega texto PARCIAL mientras el niño habla (partial_transcript) y el texto
/// final cuando hace una pausa (committed_transcript).
/// - Graba el micrófono, lo convierte a PCM 16 kHz y lo envía en bloques cortos.
/// - La API key se lee de StreamingAssets/elevenlabs.local.json (campo "apiKey", no se sube a GitHub).
/// - Se cobra todo el audio enviado mientras está conectado: ReadingSession lo conecta solo durante la lectura.
/// </summary>
public class ScribeRealtimeClient : MonoBehaviour
{
    [Header("Configuración")]
    public string configFileName = "elevenlabs.local.json";
    [Tooltip("Idioma (ISO 639-1). Vacío = detección automática")]
    public string languageCode = "es";
    [Tooltip("Segundos de silencio para cerrar una frase (texto final). El texto parcial llega igual mientras habla")]
    public float vadSilenceSecs = 0.8f;
    [Tooltip("Términos clave para mejorar la transcripción (opcional)")]
    public string[] keyterms = new string[0];
    [Tooltip("Milisegundos de audio por envío (menos = menor latencia)")]
    [Range(100, 1000)] public int chunkMs = 250;

    [Header("Micrófono")]
    [Tooltip("Nombre exacto del micrófono. Vacío o inexistente = predeterminado de Windows")]
    public string microphoneDevice = "";
    [Range(1f, 30f)] public float micGain = 1f;
    [Tooltip("Segundos de audio que se guardan mientras se conecta (se envían apenas la conexión está lista, así no se pierde lo que se dijo al inicio)")]
    public float bufferConexion = 8f;

    /// (texto, final): final = false es texto parcial.
    public event Action<string, bool> TextReceived;

    public bool IsRunning => running;
    /// En pausa: la conexión sigue lista pero se envía silencio en vez del micrófono (p. ej. mientras suena una grabación del Principito).
    public bool Pausado { get; set; }
    public bool HasApiKey() => !string.IsNullOrEmpty(LoadApiKey());

    const string Endpoint = "wss://api.elevenlabs.io/v1/speech-to-text/realtime";
    const int SampleRate = 16000;

    [Serializable] class LocalConfig { public string apiKey; }
    [Serializable] class ServerMsg { public string message_type; public string text; public string error; public string message; }

    ClientWebSocket ws;
    CancellationTokenSource cts;
    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    readonly ConcurrentQueue<KeyValuePair<bool, string>> incoming = new ConcurrentQueue<KeyValuePair<bool, string>>();
    volatile bool running, sessionStarted;

    AudioClip micClip;
    string micDevice;
    int micRate, micReadPos;
    double resamplePos;
    readonly List<float> micRaw = new List<float>();
    readonly List<float> pending = new List<float>();

    // ───────────── API ─────────────
    public void StartStreaming()
    {
        if (running) return;
        string key = LoadApiKey();
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning($"[Scribe] Falta \"apiKey\" en {Path.Combine(Application.streamingAssetsPath, configFileName)}.");
            return;
        }
        _ = RunAsync(key);
    }

    public void StopStreaming()
    {
        StopMic();   // libera el micrófono al instante (lo usará el agente de dudas o del quiz)
        try { cts?.Cancel(); } catch { }
    }

    // ───────────── Conexión ─────────────
    async Task RunAsync(string apiKey)
    {
        running = true;
        sessionStarted = false;
        cts = new CancellationTokenSource();
        ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("xi-api-key", apiKey);
        try
        {
            StartMic();   // graba desde ya: lo que se diga mientras se conecta se guarda y se envía después
            await ws.ConnectAsync(new Uri(BuildUrl()), cts.Token);
            await ReceiveLoop();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            // Detalle completo: la causa real suele estar en la excepción interna (DNS, TLS, código HTTP del servidor…)
            var sb = new StringBuilder("[Scribe] ").Append(e.Message);
            for (var ie = e.InnerException; ie != null; ie = ie.InnerException) sb.Append(" → ").Append(ie.GetType().Name).Append(": ").Append(ie.Message);
            sb.Append(" (estado del WebSocket: ").Append(ws != null ? ws.State.ToString() : "-").Append(')');
            Debug.LogWarning(sb.ToString());
        }
        finally
        {
            StopMic();
            try
            {
                if (ws != null && ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "fin", CancellationToken.None);
            }
            catch { }
            ws?.Dispose();
            ws = null;
            running = false;
            sessionStarted = false;
            Pausado = false;
        }
    }

    string BuildUrl()
    {
        var sb = new StringBuilder(Endpoint);
        sb.Append("?model_id=scribe_v2_realtime&audio_format=pcm_16000&commit_strategy=vad");
        sb.Append("&vad_silence_threshold_secs=").Append(vadSilenceSecs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(languageCode)) sb.Append("&language_code=").Append(Uri.EscapeDataString(languageCode));
        if (keyterms != null)
            foreach (var k in keyterms)
                if (!string.IsNullOrWhiteSpace(k)) sb.Append("&keyterms=").Append(Uri.EscapeDataString(k.Trim()));
        return sb.ToString();
    }

    async Task ReceiveLoop()
    {
        var buffer = new byte[32 * 1024];
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
                    Debug.Log($"[Scribe] Conexión cerrada por el servidor: {r.CloseStatus} {r.CloseStatusDescription}");
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
        try { m = JsonUtility.FromJson<ServerMsg>(json); } catch { return; }
        if (m == null || m.message_type == null) return;
        switch (m.message_type)
        {
            case "session_started":
                sessionStarted = true;
                Debug.Log("[Scribe] Transcripción en tiempo real conectada.");
                break;
            case "partial_transcript":
                if (!string.IsNullOrWhiteSpace(m.text)) incoming.Enqueue(new KeyValuePair<bool, string>(false, m.text));
                break;
            case "committed_transcript":
                if (!string.IsNullOrWhiteSpace(m.text)) incoming.Enqueue(new KeyValuePair<bool, string>(true, m.text));
                break;
            case "committed_transcript_with_timestamps":
                break;
            default:
                // input_error, auth_error, quota_exceeded, etc.
                Debug.LogWarning("[Scribe] " + m.message_type + ": " + (m.error ?? m.message ?? json));
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
        catch (Exception e) { if (!(e is OperationCanceledException)) Debug.LogWarning("[Scribe] Error al enviar: " + e.Message); }
        finally { sendLock.Release(); }
    }

    // ───────────── Micrófono ─────────────
    void StartMic()
    {
        if (Microphone.devices.Length == 0) { Debug.LogError("[Scribe] No se detectó ningún micrófono."); return; }
        micDevice = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
        if (micDevice != null && Array.IndexOf(Microphone.devices, micDevice) < 0)
        {
            Debug.LogWarning($"[Scribe] El micrófono \"{micDevice}\" no existe en este equipo. Se usa el predeterminado.");
            micDevice = null;
        }
        Microphone.GetDeviceCaps(micDevice, out int minFreq, out int maxFreq);
        if (minFreq == 0 && maxFreq == 0) micRate = SampleRate;
        else if (SampleRate >= minFreq && SampleRate <= maxFreq) micRate = SampleRate;
        else micRate = maxFreq;
        micClip = Microphone.Start(micDevice, true, 1, micRate);
        micReadPos = 0;
        resamplePos = 0;
        micRaw.Clear();
        pending.Clear();
        Debug.Log($"[Scribe] Grabando con \"{(micDevice ?? "micrófono predeterminado")}\" a {micRate} Hz → {SampleRate} Hz");
    }

    void StopMic()
    {
        if (micClip != null) Microphone.End(micDevice);
        micClip = null;
    }

    void Update()
    {
        // Texto recibido (se entrega en el hilo principal)
        while (incoming.TryDequeue(out var item)) TextReceived?.Invoke(item.Value, item.Key);

        if (micClip == null) return;
        int pos = Microphone.GetPosition(micDevice);
        if (pos < 0 || pos == micReadPos) return;

        int total = micClip.samples;
        int count = pos > micReadPos ? pos - micReadPos : total - micReadPos + pos;
        var data = new float[count];
        if (pos > micReadPos) micClip.GetData(data, micReadPos);
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
        if (Pausado) Array.Clear(data, 0, data.Length);   // en pausa se envía silencio: mantiene viva la conexión sin transcribir lo que suena
        for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i] * micGain, -1f, 1f);
        micRaw.AddRange(data);
        Resample();
        // Todavía conectando: se guarda (como máximo bufferConexion s) y se envía cuando la sesión esté lista
        int max = Mathf.Max(1, (int)(SampleRate * bufferConexion));
        if (pending.Count > max) pending.RemoveRange(0, pending.Count - max);
        if (ws == null || ws.State != WebSocketState.Open || !sessionStarted) return;

        int chunk = SampleRate * chunkMs / 1000;
        while (pending.Count >= chunk)
        {
            var bytes = new byte[chunk * 2];
            for (int i = 0; i < chunk; i++)
            {
                short s = (short)Mathf.Clamp(pending[i] * 32767f, short.MinValue, short.MaxValue);
                bytes[i * 2] = (byte)(s & 0xff);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
            }
            pending.RemoveRange(0, chunk);
            _ = SendText("{\"message_type\":\"input_audio_chunk\",\"audio_base_64\":\"" + Convert.ToBase64String(bytes) +
                         "\",\"commit\":false,\"sample_rate\":" + SampleRate + "}");
        }
    }

    void Resample()
    {
        if (micRate == SampleRate) { pending.AddRange(micRaw); micRaw.Clear(); return; }
        double step = (double)micRate / SampleRate;
        while (resamplePos + step <= micRaw.Count)
        {
            int start = (int)resamplePos;
            int end = Mathf.Max(start + 1, (int)(resamplePos + step));
            float acc = 0f;
            for (int i = start; i < end; i++) acc += micRaw[i];
            pending.Add(acc / (end - start));
            resamplePos += step;
        }
        int consumed = (int)resamplePos;
        if (consumed > 0) { micRaw.RemoveRange(0, consumed); resamplePos -= consumed; }
    }

    string LoadApiKey()
    {
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var key = JsonUtility.FromJson<LocalConfig>(File.ReadAllText(path))?.apiKey?.Trim();
            return string.IsNullOrEmpty(key) || key.StartsWith("PEGA_") ? null : key;
        }
        catch { return null; }
    }

    void OnDestroy() { StopStreaming(); StopMic(); }
}
