using System.Collections;
using UnityEngine;

/// <summary>
/// Espera la respuesta hablada del niño sin enviarla a ningún servicio (gratis y local):
/// detecta cuando empieza a hablar y continúa cuando se queda en silencio.
/// Si no responde en "No Response Timeout" segundos, continúa igual.
/// </summary>
public class VoiceActivityWaiter : MonoBehaviour
{
    [Header("Micrófono")]
    [Tooltip("Nombre exacto del micrófono. Vacío = predeterminado de Windows")]
    public string microphoneDevice = "";
    [Range(1f, 30f)] public float micGain = 8f;

    [Header("Detección de voz")]
    [Tooltip("Volumen (RMS, ya amplificado) a partir del cual se considera que el niño habla")]
    public float speechThreshold = 0.04f;
    [Tooltip("Segundos mínimos de voz para contar como respuesta (ignora ruidos cortos)")]
    public float minSpeechTime = 0.3f;
    [Tooltip("Segundos de silencio después de hablar para dar la respuesta por terminada")]
    public float endSilence = 1.0f;
    [Tooltip("Si el niño no habla en este tiempo, se continúa")]
    public float noResponseTimeout = 7f;
    [Tooltip("Tiempo máximo de espera aunque siga hablando")]
    public float maxWait = 12f;

    [Header("Depuración")]
    public bool showLevel = false;

    public bool IsListening { get; private set; }
    public bool LastResponded { get; private set; }
    public float Level { get; private set; }

    AudioClip clip;
    string device;
    int readPos;
    float[] buffer = new float[0];

    public IEnumerator WaitForResponse()
    {
        LastResponded = false;
        if (!StartMic())
        {
            yield return new WaitForSeconds(3f);
            yield break;
        }

        IsListening = true;
        float elapsed = 0f, speech = 0f, silence = 0f;
        bool spoke = false;

        while (true)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            Level = ReadLevel();

            if (Level >= speechThreshold)
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
        Debug.Log(spoke ? "[Onboarding] El niño respondió." : "[Onboarding] Sin respuesta; se continúa.");
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
        if (buffer.Length < total) buffer = new float[total];

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
        GUI.Label(new Rect(16, 10, 300, 20), "Escuchando respuesta…");
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(new Rect(16, 32, 160, 8), Texture2D.whiteTexture);
        GUI.color = Level >= speechThreshold ? new Color(0.3f, 1f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
        GUI.DrawTexture(new Rect(16, 32, 160 * Mathf.Clamp01(Level * 8f), 8), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void OnDisable() => StopMic();
}
