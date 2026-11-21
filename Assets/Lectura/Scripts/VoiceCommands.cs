using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Comandos de voz para controlar la presentación. Todos empiezan con "Atención Principito, …".
///
///   pausa / espera ........... congela audio, animaciones y lectura en vivo
///   continúa / sigue ......... reanuda donde quedó
///   repite esta página ....... vuelve a armar las animaciones y la frase de la página actual (el ArUco no cambia)
///   repite el capítulo ....... lo mismo para todas las páginas del capítulo actual
///   repite el quiz ........... nuevo sorteo de preguntas
///   repite la pregunta ....... el Principito vuelve a leer la pregunta
///   ayuda .................... dice qué comandos hay
///   nuevo alumno ............. reinicia todo para el siguiente niño (pide confirmación)
///
/// Después de cada comando el Principito confirma con una frase corta (audio en Resources/Comandos/ + texto en pantalla).
/// Teclas de respaldo por si falla la voz: P pausa/continúa · R página · G capítulo · U quiz · Y pregunta · H ayuda · N nuevo alumno (dos veces).
///
/// La voz llega por Scribe mientras se lee (y mientras está en pausa) y por el agente del quiz mientras dura el quiz.
/// No hace falta ponerlo en la escena: se crea solo al empezar.
/// </summary>
public class VoiceCommands : MonoBehaviour
{
    public static VoiceCommands Instancia { get; private set; }
    public bool Pausado { get; private set; }

    [Header("Referencias (si se dejan vacías se buscan solas)")]
    public ReadingSession lectura;
    public QuizSession quiz;
    public OnboardingSequence onboarding;
    public BookPageTracker tracker;
    public ArucoUdpReceiver receptor;
    public ScribeRealtimeClient scribe;

    [Header("Ajustes")]
    [Tooltip("Segundos que se ignora lo que se oye después de un comando (para no repetirlo con el texto parcial)")]
    public float enfriamiento = 3.5f;
    [Tooltip("Segundos para confirmar \"nuevo alumno\"")]
    public float esperaConfirmacion = 8f;
    [Tooltip("Muestra la confirmación escrita en pantalla")]
    public bool mostrarTexto = true;
    public bool teclasRespaldo = true;
    [Range(0f, 1f)] public float volumen = 1f;

    enum Cmd { Ninguno, Pausa, Continua, RepitePagina, RepiteCapitulo, RepiteQuiz, RepitePregunta, Ayuda, NuevoAlumno, Confirma }

    // Texto y audio de cada confirmación (audio: Resources/Comandos/<clave>.mp3)
    static readonly Dictionary<string, string> Frases = new Dictionary<string, string>
    {
        { "pausa",              "Listo, hago una pausa." },
        { "continua",           "Muy bien, sigamos." },
        { "repite_pagina",      "De acuerdo, repetimos esta página." },
        { "repite_capitulo",    "De acuerdo, repetimos el capítulo." },
        { "repite_quiz",        "Claro, otras preguntas." },
        { "repite_pregunta",    "Te la repito." },
        { "ayuda",              "Puedes pedirme: pausa, continúa, repite esta página, repite el quiz o nuevo alumno." },
        { "nuevo_alumno_pide",  "¿Seguro? Dime: sí, nuevo alumno." },
        { "nuevo_alumno",       "Empezamos con un nuevo lector." },
        { "no_entendi",         "No te entendí, ¿me lo repites?" },
        { "no_hay_quiz",        "Todavía no hay un quiz que repetir." },
    };

    AudioSource voz;
    float ignorarHasta;
    float confirmaHasta = -1f;
    string textoPantalla = "";
    float textoHasta;

    // ───────────── Arranque ─────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Crear()
    {
        if (FindFirstObjectByType<VoiceCommands>() != null) return;
        if (FindFirstObjectByType<ReadingSession>() == null) return;   // solo en la escena del libro
        new GameObject("ComandosDeVoz").AddComponent<VoiceCommands>();
    }

    void Awake()
    {
        if (Instancia != null && Instancia != this) { Destroy(gameObject); return; }
        Instancia = this;
        voz = gameObject.AddComponent<AudioSource>();
        voz.playOnAwake = false;
        voz.ignoreListenerPause = true;   // la confirmación debe oírse aunque todo esté en pausa
        voz.spatialBlend = 0f;
    }

    void Start()
    {
        if (!lectura) lectura = FindFirstObjectByType<ReadingSession>();
        if (!quiz) quiz = FindFirstObjectByType<QuizSession>();
        if (!onboarding) onboarding = FindFirstObjectByType<OnboardingSequence>();
        if (!tracker) tracker = FindFirstObjectByType<BookPageTracker>();
        if (!receptor) receptor = FindFirstObjectByType<ArucoUdpReceiver>();
        if (!scribe) scribe = FindFirstObjectByType<ScribeRealtimeClient>();

        if (lectura) lectura.TextoEnVivo += OnTexto;
        if (quiz && quiz.agent) quiz.agent.UserTranscript += OnTextoQuiz;
        Debug.Log("[Comandos] Listos. Di \"Atención Principito, pausa\" (o usa las teclas P R G U Y H N).");
    }

    void OnDestroy()
    {
        if (lectura) lectura.TextoEnVivo -= OnTexto;
        if (quiz && quiz.agent) quiz.agent.UserTranscript -= OnTextoQuiz;
        if (Instancia == this) Instancia = null;
        if (Pausado) { Time.timeScale = 1f; AudioListener.pause = false; }
    }

    // ───────────── Entrada de voz ─────────────
    void OnTexto(string texto, bool final) => Procesar(texto, "Scribe", final);
    void OnTextoQuiz(string texto) => Procesar(texto, "quiz", true);

    void Procesar(string texto, string origen, bool final)
    {
        if (Time.unscaledTime < ignorarHasta || string.IsNullOrWhiteSpace(texto)) return;
        var tk = Tokens(texto);

        // Confirmación pendiente de "nuevo alumno": basta decir "sí" / "confirmo", o repetir el comando
        if (confirmaHasta > 0f)
        {
            if (Time.unscaledTime > confirmaHasta) confirmaHasta = -1f;
            else if (tk.Contains("si") || tk.Contains("confirmo") || tk.Contains("confirma") || (tk.Contains("nuevo") && tk.Contains("alumno")))
            {
                confirmaHasta = -1f;
                Registrar(origen, texto, Cmd.Confirma);
                Ejecutar(Cmd.NuevoAlumno, true);
                return;
            }
        }

        int fin = FinActivacion(tk);
        if (fin < 0) return;                       // no empezó con "Atención Principito"
        var resto = tk.GetRange(fin, tk.Count - fin);
        var cmd = Interpretar(resto);
        if (cmd == Cmd.Ninguno)
        {
            // Dijo la frase de activación pero no se entiende la orden: solo se avisa con la frase completa (no con texto parcial).
            if (final && resto.Count >= 1) { ignorarHasta = Time.unscaledTime + enfriamiento; Decir("no_entendi"); }
            return;
        }
        Registrar(origen, texto, cmd);
        Ejecutar(cmd, false);
    }

    void Registrar(string origen, string texto, Cmd cmd) =>
        Debug.Log($"[Comandos] ({origen}) \"{texto.Trim()}\" → {cmd}");

    /// ¿Este texto contiene la frase de activación? (la lectura lo usa para no confundirlo con "Principito, tengo una duda")
    public static bool ContieneActivacion(string texto) => FinActivacion(Tokens(texto)) >= 0;

    /// Índice del primer token después de "atención principito" (Scribe puede meter una palabra en medio). -1 si no está.
    static int FinActivacion(List<string> tk)
    {
        for (int i = 0; i < tk.Count; i++)
        {
            if (!EsAtencion(tk[i])) continue;
            for (int j = i + 1; j <= Math.Min(i + 2, tk.Count - 1); j++)
                if (EsPrincipito(tk[j])) return j + 1;
        }
        return -1;
    }

    static bool EsAtencion(string t) => t == "atencion" || t == "atento" || t == "atenta";
    static bool EsPrincipito(string t) => t == "principito" || t == "principe" || t == "principitos";

    static Cmd Interpretar(List<string> r)
    {
        bool Tiene(params string[] ws) { foreach (var w in ws) if (r.Contains(w)) return true; return false; }

        if (Tiene("nuevo", "nueva", "otro") && Tiene("alumno", "alumna", "lector", "lectora", "estudiante", "sesion")) return Cmd.NuevoAlumno;
        if (Tiene("repite", "repetir", "repetimos", "repita", "otra", "otras", "vuelve", "volver"))
        {
            if (Tiene("pregunta", "preguntas")) return Cmd.RepitePregunta;
            if (Tiene("quiz", "cuestionario", "preguntitas")) return Cmd.RepiteQuiz;
            if (Tiene("capitulo")) return Cmd.RepiteCapitulo;
            if (Tiene("pagina", "esta", "hoja")) return Cmd.RepitePagina;
            return Cmd.Ninguno;
        }
        if (Tiene("ayuda", "ayudame", "comandos", "opciones")) return Cmd.Ayuda;
        if (Tiene("continua", "continuar", "sigue", "seguir", "reanuda", "reanudar", "sigamos")) return Cmd.Continua;
        if (Tiene("pausa", "pausar", "espera", "esperar", "detente", "detener")) return Cmd.Pausa;
        return Cmd.Ninguno;
    }

    // ───────────── Acciones ─────────────
    void Ejecutar(Cmd cmd, bool confirmado)
    {
        ignorarHasta = Time.unscaledTime + enfriamiento;
        switch (cmd)
        {
            case Cmd.Pausa: Pausar(); break;
            case Cmd.Continua: Reanudar(); break;
            case Cmd.RepitePagina: RepetirPagina(); break;
            case Cmd.RepiteCapitulo: RepetirCapitulo(); break;
            case Cmd.RepiteQuiz: RepetirQuiz(); break;
            case Cmd.RepitePregunta: RepetirPregunta(); break;
            case Cmd.Ayuda: Decir("ayuda"); break;
            case Cmd.NuevoAlumno:
                if (!confirmado)
                {
                    confirmaHasta = Time.unscaledTime + esperaConfirmacion;
                    Decir("nuevo_alumno_pide");
                }
                else NuevoAlumno();
                break;
        }
    }

    public void Pausar()
    {
        if (Pausado) return;
        Pausado = true;
        if (lectura) lectura.Pausada = true;
        if (quiz) quiz.Pausar();
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Decir("pausa");
    }

    public void Reanudar()
    {
        if (!Pausado) { Decir("continua"); return; }
        Pausado = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (lectura) lectura.Pausada = false;
        if (quiz) quiz.Reanudar();
        Decir("continua");
    }

    void RepetirPagina()
    {
        if (Pausado) Reanudar();
        int p = tracker ? tracker.CurrentPage : 1;
        if (lectura) lectura.RearmarPaginas(p, p);
        if (quiz) quiz.RearmarQuizzes();
        Debug.Log($"[Comandos] Página {p} rearmada.");
        Decir("repite_pagina");
    }

    void RepetirCapitulo()
    {
        if (Pausado) Reanudar();
        int p = tracker ? tracker.CurrentPage : 1;
        int ini = p, fin = p;
        if (lectura) lectura.RangoCapitulo(p, out ini, out fin);
        if (lectura) lectura.RearmarPaginas(ini, fin);
        if (quiz) quiz.RearmarQuizzes();
        Debug.Log($"[Comandos] Capítulo rearmado (páginas {ini} a {fin}).");
        Decir("repite_capitulo");
    }

    void RepetirQuiz()
    {
        if (Pausado) Reanudar();
        if (quiz && quiz.ReiniciarQuiz()) Decir("repite_quiz");
        else Decir("no_hay_quiz");
    }

    void RepetirPregunta()
    {
        if (Pausado) Reanudar();
        if (quiz && quiz.RepetirPregunta()) Decir("repite_pregunta");
        else Decir("no_hay_quiz");
    }

    /// Reinicia todo para el siguiente niño: se olvida nombre, página, animaciones y quizzes, y vuelve a esperar el ArUco de la página 1.
    public void NuevoAlumno()
    {
        if (Pausado) { Pausado = false; Time.timeScale = 1f; AudioListener.pause = false; if (lectura) lectura.Pausada = false; }
        if (quiz) { quiz.Cancelar(); quiz.RearmarQuizzes(); }
        if (onboarding) onboarding.Cancelar();
        if (lectura) { lectura.Detener(); lectura.RearmarPaginas(0, 0); }
        if (tracker) tracker.ReiniciarPagina();
        if (receptor) receptor.ResetActions();   // el ArUco de la página 1 vuelve a iniciar el onboarding
        SesionLector.Reiniciar();
        Debug.Log("[Comandos] Nuevo alumno: todo reiniciado. Muestra el ArUco de la página 1 para empezar.");
        Decir("nuevo_alumno");
    }

    // ───────────── Confirmación del Principito ─────────────
    void Decir(string clave)
    {
        string texto = Frases.TryGetValue(clave, out var f) ? f : clave;
        textoPantalla = texto;
        textoHasta = Time.unscaledTime + 4f;
        var clip = Resources.Load<AudioClip>("Comandos/" + clave);
        if (clip && voz)
        {
            voz.Stop();
            voz.volume = volumen;
            voz.PlayOneShot(clip);
        }
        else Debug.Log($"[Comandos] (sin audio \"{clave}\") {texto}");
    }

    void OnGUI()
    {
        if (!mostrarTexto) return;
        if (Pausado)
        {
            var estilo = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
            GUI.color = new Color(1f, 0.85f, 0.3f);
            GUI.Label(new Rect(Screen.width - 380, 12, 360, 36), "⏸ En pausa", estilo);
            GUI.color = Color.white;
        }
        if (Time.unscaledTime < textoHasta && !string.IsNullOrEmpty(textoPantalla))
        {
            var estilo = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
            var r = new Rect(Screen.width * 0.15f, Screen.height - 90, Screen.width * 0.7f, 60);
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, textoPantalla, estilo);
        }
    }

    // ───────────── Teclas de respaldo ─────────────
    void Update()
    {
        if (!teclasRespaldo) return;
        if (confirmaHasta > 0f && Time.unscaledTime > confirmaHasta) confirmaHasta = -1f;
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        if (k == null) return;
        if (k.pKey.wasPressedThisFrame) { if (Pausado) Reanudar(); else Pausar(); }
        if (k.rKey.wasPressedThisFrame) RepetirPagina();
        if (k.gKey.wasPressedThisFrame) RepetirCapitulo();
        if (k.uKey.wasPressedThisFrame) RepetirQuiz();
        if (k.yKey.wasPressedThisFrame) RepetirPregunta();
        if (k.hKey.wasPressedThisFrame) Decir("ayuda");
        if (k.nKey.wasPressedThisFrame) TeclaNuevoAlumno();
#else
        if (Input.GetKeyDown(KeyCode.P)) { if (Pausado) Reanudar(); else Pausar(); }
        if (Input.GetKeyDown(KeyCode.R)) RepetirPagina();
        if (Input.GetKeyDown(KeyCode.G)) RepetirCapitulo();
        if (Input.GetKeyDown(KeyCode.U)) RepetirQuiz();
        if (Input.GetKeyDown(KeyCode.Y)) RepetirPregunta();
        if (Input.GetKeyDown(KeyCode.H)) Decir("ayuda");
        if (Input.GetKeyDown(KeyCode.N)) TeclaNuevoAlumno();
#endif
    }

    void TeclaNuevoAlumno()
    {
        if (confirmaHasta > 0f) { confirmaHasta = -1f; NuevoAlumno(); }
        else { confirmaHasta = Time.unscaledTime + esperaConfirmacion; Decir("nuevo_alumno_pide"); }
    }

    // ───────────── Texto ─────────────
    /// Palabras en minúsculas, sin tildes ni signos, en el orden en que se dijeron.
    static List<string> Tokens(string text)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        foreach (char ch in (text ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list;
    }
}
