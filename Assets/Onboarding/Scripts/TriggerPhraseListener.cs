using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

/// <summary>
/// Escucha la frase "Principito, tengo una duda" con el reconocedor de voz de Windows
/// (sin internet y sin gastar minutos de ElevenLabs) y dispara onPhrase.
/// - Se pausa mientras la conversación con el agente está abierta (el micrófono lo usa el agente).
/// - Usa el micrófono PREDETERMINADO de Windows y el idioma de voz de Windows (debe estar en español).
/// - Solo funciona en Windows (Editor y .exe).
/// </summary>
public class TriggerPhraseListener : MonoBehaviour
{
    [Tooltip("Frases que abren las dudas (se reconocen sin importar comas ni mayúsculas)")]
    public string[] phrases =
    {
        "Principito tengo una duda",
        "Principito tengo duda",
        "Principito una duda",
        "Principito tengo una pregunta",
    };
    [Tooltip("Baja = acepta pronunciaciones menos claras (niños). Alta = menos activaciones por error")]
    public Confianza minConfidence = Confianza.Baja;

    [Tooltip("Mientras este agente esté conversando, no se escucha la frase")]
    public ElevenLabsAgentClient agent;
    [Tooltip("Segundos de espera después de cerrar una conversación antes de volver a escuchar")]
    public float resumeDelay = 1.5f;

    public UnityEvent onPhrase = new UnityEvent();

    public enum Confianza { Baja, Media, Alta }

    public bool IsListening { get; private set; }

    float idleSince;
    bool phraseHeard;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    KeywordRecognizer recognizer;

    void Start()
    {
        if (!PhraseRecognitionSystem.isSupported)
        {
            Debug.LogWarning("[Frase] El reconocimiento de voz de Windows no está disponible en este equipo.");
            enabled = false;
            return;
        }
        var level = minConfidence == Confianza.Alta ? ConfidenceLevel.High
                  : minConfidence == Confianza.Media ? ConfidenceLevel.Medium : ConfidenceLevel.Low;
        var list = new List<string>();
        foreach (var p in phrases) if (!string.IsNullOrWhiteSpace(p)) list.Add(p.Trim());
        try
        {
            recognizer = new KeywordRecognizer(list.ToArray(), level);
            recognizer.OnPhraseRecognized += OnRecognized;
            PhraseRecognitionSystem.OnError += e => Debug.LogWarning("[Frase] Error del reconocedor de Windows: " + e);
            Debug.Log("[Frase] Di \"Principito, tengo una duda\" para abrir las dudas (micrófono predeterminado de Windows).");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Frase] No se pudo iniciar el reconocedor de voz de Windows: " + e.Message +
                           ". Revisa que el idioma de voz de Windows esté en español.");
            enabled = false;
        }
    }

    void OnRecognized(PhraseRecognizedEventArgs args)
    {
        Debug.Log($"[Frase] Escuché: \"{args.text}\" (confianza {args.confidence})");
        phraseHeard = true;   // se procesa en Update
    }

    void Update()
    {
        if (recognizer == null) return;

        if (phraseHeard)
        {
            phraseHeard = false;
            Pause();
            onPhrase?.Invoke();
            idleSince = Time.unscaledTime;
            return;
        }

        bool busy = agent != null && agent.IsRunning;
        if (busy)
        {
            if (IsListening) Pause();
            idleSince = Time.unscaledTime;
        }
        else if (!IsListening && Time.unscaledTime - idleSince >= resumeDelay)
        {
            Resume();
        }
    }

    void Resume()
    {
        try { recognizer.Start(); IsListening = true; }
        catch (System.Exception e) { Debug.LogWarning("[Frase] No se pudo reanudar: " + e.Message); }
    }

    void Pause()
    {
        if (recognizer != null && recognizer.IsRunning) recognizer.Stop();
        IsListening = false;
    }

    void OnDisable() => Pause();

    void OnDestroy()
    {
        if (recognizer == null) return;
        Pause();
        recognizer.OnPhraseRecognized -= OnRecognized;
        recognizer.Dispose();
        recognizer = null;
    }
#else
    void Start()
    {
        Debug.LogWarning("[Frase] El reconocimiento de la frase solo está disponible en Windows. Usa la tecla D o el mensaje UDP {\"tipo\":\"duda\"}.");
        enabled = false;
    }
#endif
}
