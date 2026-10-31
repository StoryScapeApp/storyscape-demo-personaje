using System.Collections;
using UnityEngine;

/// <summary>
/// Espera la respuesta hablada del niño sin enviarla a ningún servicio (gratis y local):
/// detecta cuando empieza a hablar y continúa cuando se queda en silencio.
/// Se calibra solo: mide el ruido del ambiente al empezar a escuchar y fija el umbral de voz
/// por encima de ese ruido. Si el niño no responde en "No Response Timeout" segundos, continúa igual.
/// </summary>
public class VoiceActivityWaiter : MonoBehaviour
{
    [Header("Micrófono")]
    [Tooltip("Nombre exacto del micrófono (se listan en la consola al dar Play). Vacío = predeterminado de Windows")]
    public string microphoneDevice = "";
    [Range(1f, 30f)] public float micGain = 8f;

    [Header("Detección de voz")]
    [Tooltip("Umbral mínimo de voz (RMS, ya amplificado). El umbral real se ajusta según el ruido del ambiente")]
    public float minSpeechThreshold = 0.012f;
    [Tooltip("La voz debe superar el ruido del ambiente por este factor")]
    public float noiseMultiplier = 2.5f;
    [Tooltip("Segundos que se mide el ruido del ambiente antes de escuchar")]
    public float calibrationTime = 0.4f;
    [Tooltip("Segundos mínimos de voz para contar como respuesta (ignora ruidos cortos)")]
    public float minSpeechTime = 0.25f;
    [Tooltip("Segundos de silencio después de hablar para dar la respuesta por terminada")]
    public float endSilence = 0.5f;
    [Tooltip("Si el niño no habla en este tiempo, se continúa")]
    public float noResponseTimeout = 7f;
    [Tooltip("Tiempo máximo de espera aunque siga hablando")]
    public float maxWait = 12f;

    [Header("Depuración")]
    [Tooltip("Muestra un medidor en pantalla mientras escucha")]
    public bool showLevel = false;

    public bool IsListening { get; private set; }
    public bool LastResponded { get; private set; }
    public float Level { get; private set; }
    public float Threshold { get; private set; }

    AudioClip clip;
    string device;
    int readPos;

    void Start()
    {
        string dev = string.IsNullOrEmpty(microphoneDevice) ? "predeterminado de Windows" : microphoneDevice;
        if (!string.IsNullOrEmpty(microphoneDevice) && System.Array.IndexOf(Microphone.devices, microphoneDevice) < 0)
            Debug.LogWarning($"[Onboarding] El micrófono \"{microphoneDevice}\" no existe. Disponibles: {string.Join(" | ", Microphone.devices)}");
        else
            Debug.Log($"[Onboarding] Micrófono: {dev} (ganancia x{micGain:0.#})");
    }

    public IEnumerator WaitForResponse()
    {
        LastResponded = false;
        if (!StartMic())
        {
            yield return new WaitForSeconds(3f);
            yield break;
        }

        IsListening = true;

        // Calibración: ruido del ambiente
        float noiseSum = 0f; int noiseN = 0;
        for (float t = 0f; t < calibrationTime; t += Time.deltaTime)
        {
            Level = ReadLevel();
            noiseSum += Level; noiseN++;
            yield return null;
        }
        float noise = noiseN > 0 ? noiseSum / noiseN : 0f;
        Threshold = Mathf.Max(minSpeechThreshold, noise * noiseMultiplier);

        float elapsed = 0f, speech = 0f, silence = 0f, peak = 0f;
        bool spoke = false;
        while (true)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            Level = ReadLevel();
            peak = Mathf.Max(peak, Level);

            if (Level >= Threshold)
            {
                speech += dt;
                silence = 0f;
                if (speech >= minSpeechTime) spoke = true;
            }
            else
            {
                silence += dt;
            }

            if (spoke && silence >= endSilence) break;
            if (!spoke && elapsed >= noResponseTimeout) break;
            if (elapsed >= maxWait) break;
            yield return null;
        }

        IsListening = false;
        LastResponded = spoke;
        StopMic();
        Debug.Log((spoke ? "[Onboarding] El niño respondió." : "[Onboarding] Sin respuesta; se continúa.") +
                  $" (ruido {noise:0.000}, umbral {Threshold:0.000}, pico {peak:0.000})");
    }

    bool StartMic()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[Onboarding] No hay micrófono: se continúa sin esperar respuesta.");
            return false;
        }
        device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
        Microphone.GetDeviceCaps(device, out int min, out int max);
        int rate = (min == 0 && max == 0) ? 16000 : Mathf.Clamp(16000, min, max);
        clip = Microphone.Start(device, true, 1, rate);
        readPos = 0;
        return clip != null;
    }

    void StopMic()
    {
        if (clip != null) Microphone.End(device);
        clip = null;
        Level = 0f;
    }

    float ReadLevel()
    {
        if (clip == null) return 0f;
        int pos = Microphone.GetPosition(device);
        if (pos < 0 || pos == readPos) return Level * 0.9f;

        int total = clip.samples;
        int count = pos > readPos ? pos - readPos : total - readPos + pos;

        float sum = 0f;
        if (pos > readPos)
        {
            var data = new float[count];
            clip.GetData(data, readPos);
            for (int i = 0; i < count; i++) { float v = data[i] * micGain; sum += v * v; }
        }
        else
        {
            var tail = new float[total - readPos];
            clip.GetData(tail, readPos);
            for (int i = 0; i < tail.Length; i++) { float v = tail[i] * micGain; sum += v * v; }
            if (pos > 0)
            {
                var head = new float[pos];
                clip.GetData(head, 0);
                for (int i = 0; i < head.Length; i++) { float v = head[i] * micGain; sum += v * v; }
            }
        }
        readPos = pos;
        float rms = Mathf.Sqrt(sum / Mathf.Max(1, count));
        return Mathf.Lerp(Level, rms, 0.6f);
    }

    void OnGUI()
    {
        if (!showLevel || !IsListening) return;
        GUI.Label(new Rect(16, 10, 400, 20), $"Escuchando respuesta…  (umbral {Threshold:0.000})");
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(new Rect(16, 32, 160, 8), Texture2D.whiteTexture);
        GUI.color = Level >= Threshold ? new Color(0.3f, 1f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
        GUI.DrawTexture(new Rect(16, 32, 160 * Mathf.Clamp01(Level * 8f), 8), Texture2D.whiteTexture);
        // marca del umbral
        GUI.color = new Color(1f, 0.8f, 0.2f);
        GUI.DrawTexture(new Rect(16 + 160 * Mathf.Clamp01(Threshold * 8f), 30, 2, 12), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void OnDisable() => StopMic();
}
