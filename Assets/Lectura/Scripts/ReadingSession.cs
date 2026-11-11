using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Agente IA en vivo durante la lectura (empieza al terminar el onboarding).
/// - Mantiene abierta una conversación con el agente "Principito lector" de ElevenLabs, que solo escucha
///   (su voz está silenciada) mientras el niño lee en voz alta.
/// - Cuando el niño lee una frase resaltada del libro, se activa su animación. Se detecta de dos formas:
///   1) Unity compara lo que el agente transcribe con las frases de la lista (rápido y seguro).
///   2) El agente llama a la herramienta de cliente "activar_animacion" (respaldo si el niño lee distinto).
/// - Si el niño dice "Principito, tengo una duda", se cierra la lectura, se abre el agente de dudas y,
///   al terminar la duda, la lectura se reconecta sola.
/// - Al cambiar de página (ArUco), le avisa al agente con el texto de la nueva página.
/// Prueba sin ElevenLabs: tecla V (reproduce la animación de la página actual).
/// </summary>
public class ReadingSession : MonoBehaviour
{
    [Serializable]
    public class SceneAnimation
    {
        [Tooltip("Identificador que usa el agente en activar_animacion")]
        public string id = "principito_volando";
        [Tooltip("Página del libro (marker + 1). 0 = cualquier página")]
        public int pagina = 1;
        [Tooltip("Frase del libro. Puedes poner alternativas separadas por | (basta con que coincida una)")]
        [TextArea(2, 3)] public string frase;
        [Tooltip("Proporción de palabras de la frase que deben escucharse para activarla")]
        [Range(0.3f, 1f)] public float coincidencia = 0.7f;
        public bool soloUnaVez = true;
        public UnityEvent alActivar = new UnityEvent();
        [NonSerialized] public bool activada;
    }

    [Header("Agentes")]
    [Tooltip("Agente que escucha la lectura (configKey = agentIdLectura, voz silenciada)")]
    public ElevenLabsAgentClient agent;
    [Tooltip("Agente de dudas: mientras conversa, la lectura se pausa")]
    public ElevenLabsAgentClient doubtAgent;
    public DoubtSession doubts;
    [Tooltip("Mientras el quiz está activo, la lectura se pausa")]
    public QuizSession quiz;
    public BookPageTracker tracker;

    [Header("Animaciones del libro")]
    public List<SceneAnimation> animaciones = new List<SceneAnimation>();
    public string toolName = "activar_animacion";
    [Tooltip("Solo activa las animaciones de la página en la que está el niño")]
    public bool soloPaginaActual = true;

    [Header("Detección")]
    [Tooltip("Segundos de lectura que se juntan para buscar una frase (el niño puede hacer pausas)")]
    public float ventanaSegundos = 20f;
    public float reconectarTras = 2f;
    [Tooltip("Abre las dudas cuando la transcripción contiene \"Principito\" y alguna de estas palabras")]
    public string[] palabrasDuda = { "duda", "pregunta" };

    [Header("Prueba")]
    public bool tecladoPrueba = true;

    public bool Activa { get; private set; }

    readonly List<KeyValuePair<float, string>> heard = new List<KeyValuePair<float, string>>();
    float retryAt;
    int lastPage = -1;

    void OnEnable()
    {
        if (!agent) return;
        agent.UserTranscript += OnTranscript;
        agent.RegisterTool(toolName, OnTool);
    }

    void OnDisable()
    {
        if (!agent) return;
        agent.UserTranscript -= OnTranscript;
        agent.RegisterTool(toolName, null);
    }

    /// Empieza a escuchar la lectura (lo llama el onboarding al terminar).
    [ContextMenu("Iniciar lectura en vivo")]
    public void Iniciar()
    {
        Activa = true;
        retryAt = Time.unscaledTime;
        Debug.Log("[Lectura] Agente en vivo escuchando la lectura.");
    }

    [ContextMenu("Detener lectura en vivo")]
    public void Detener()
    {
        Activa = false;
        if (agent && agent.IsRunning) agent.EndConversation();
    }

    [ContextMenu("Reiniciar animaciones")]
    public void ReiniciarAnimaciones()
    {
        foreach (var a in animaciones) a.activada = false;
    }

    void Update()
    {
        if (tecladoPrueba && KeyTest())
        {
            var a = animaciones.Find(x => PageOk(x)) ?? (animaciones.Count > 0 ? animaciones[0] : null);
            if (a != null) Activar(a, "tecla V", true);
        }

        int page = tracker ? tracker.CurrentPage : 1;
        if (page != lastPage)
        {
            bool first = lastPage < 0;
            lastPage = page;
            heard.Clear();
            if (!first && agent && agent.IsRunning)
                agent.SendContextualUpdate($"El niño pasó a la página {page}. Texto de la página {page}:\n{TextoPagina(page)}");
        }

        if (!Activa || !agent) return;
        bool doubtBusy = (doubtAgent && doubtAgent.IsRunning) || (quiz && quiz.IsRunning);
        if (doubtBusy)
        {
            if (agent.IsRunning) { agent.EndConversation(); Debug.Log("[Lectura] Pausa: duda o quiz en curso."); }
            retryAt = Time.unscaledTime + reconectarTras;
            return;
        }
        if (!agent.IsRunning && Time.unscaledTime >= retryAt)
        {
            retryAt = Time.unscaledTime + reconectarTras + 3f;
            agent.StartConversation(BuildVariables());
        }
    }

    Dictionary<string, string> BuildVariables()
    {
        int page = tracker ? tracker.CurrentPage : 1;
        var sb = new StringBuilder();
        foreach (var a in animaciones)
            sb.Append("- ").Append(a.id).Append(" (página ").Append(a.pagina).Append("): \"").Append(a.frase).Append("\"\n");
        return new Dictionary<string, string>
        {
            { "pagina_actual", page.ToString() },
            { "texto_pagina", TextoPagina(page) },
            { "animaciones", sb.ToString().Trim() },
        };
    }

    string TextoPagina(int page) => tracker ? tracker.GetTextoPagina(page) : "";

    // ───────────── Detección ─────────────
    void OnTranscript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        float now = Time.unscaledTime;
        heard.Add(new KeyValuePair<float, string>(now, text));
        heard.RemoveAll(h => now - h.Key > ventanaSegundos);

        var last = Tokens(text);
        if (last.Contains("principito") && Array.Exists(palabrasDuda, w => last.Contains(Norm(w))))
        {
            Debug.Log("[Lectura] \"Principito, tengo una duda\" → abriendo dudas.");
            if (doubts) doubts.AbrirDudas();
            return;
        }

        var sb = new StringBuilder();
        foreach (var h in heard) sb.Append(h.Value).Append(' ');
        var window = Tokens(sb.ToString());
        foreach (var a in animaciones)
        {
            if (!PageOk(a) || (a.soloUnaVez && a.activada)) continue;
            float score = Score(a.frase, window);
            if (score >= a.coincidencia) Activar(a, $"lectura ({score:P0})", false);
        }
    }

    string OnTool(ElevenLabsAgentClient.ToolArgs args)
    {
        string id = (args.animacion ?? "").Trim();
        var a = animaciones.Find(x => string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase));
        if (a == null) return $"No existe la animación \"{id}\".";
        if (a.soloUnaVez && a.activada) return "Esa animación ya se mostró. Sigue escuchando en silencio.";
        Activar(a, "agente", false);
        return "Animación activada. Sigue escuchando en silencio.";
    }

    bool PageOk(SceneAnimation a)
    {
        if (!soloPaginaActual || a.pagina <= 0 || tracker == null) return true;
        return a.pagina == tracker.CurrentPage;
    }

    void Activar(SceneAnimation a, string origen, bool force)
    {
        if (!force && a.soloUnaVez && a.activada) return;
        a.activada = true;
        Debug.Log($"[Lectura] Animación \"{a.id}\" activada por {origen}.");
        a.alActivar?.Invoke();
    }

    /// Proporción de palabras de la frase (de 3 letras o más) que aparecen en lo escuchado.
    static float Score(string phrase, HashSet<string> heardTokens)
    {
        float best = 0f;
        foreach (var alt in (phrase ?? "").Split('|'))
            best = Mathf.Max(best, ScoreOne(alt, heardTokens));
        return best;
    }

    static float ScoreOne(string phrase, HashSet<string> heardTokens)
    {
        int total = 0, found = 0;
        foreach (var w in Tokens(phrase))
        {
            if (w.Length < 3) continue;
            total++;
            foreach (var h in heardTokens)
                if (Similar(w, h)) { found++; break; }
        }
        return total == 0 ? 0f : (float)found / total;
    }

    static bool Similar(string a, string b)
    {
        if (a == b) return true;
        if (a.Length < 4 || b.Length < 4) return false;
        string s = a.Length <= b.Length ? a : b, l = a.Length <= b.Length ? b : a;
        return l.StartsWith(s) && l.Length - s.Length <= 3;
    }

    static HashSet<string> Tokens(string text)
    {
        var set = new HashSet<string>();
        var sb = new StringBuilder();
        foreach (char ch in (text ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0) { set.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) set.Add(sb.ToString());
        return set;
    }

    static string Norm(string w)
    {
        foreach (var t in Tokens(w)) return t;
        return "";
    }

    static bool KeyTest()
    {
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        return k != null && k.vKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.V);
#endif
    }
}
