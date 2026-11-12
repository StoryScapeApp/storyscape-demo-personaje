using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Mini quiz al terminar una página (lo activa ReadingSession al detectar el final de la página).
/// - Muestra las preguntas una por una en un panel a la derecha del libro, con alternativas A, B y C.
/// - Las alternativas se MEZCLAN en cada sesión, para que la correcta no esté siempre en el mismo lugar.
/// - El agente "Principito quiz" de ElevenLabs lee la pregunta y sus alternativas (first message) y escucha la
///   respuesta. Cuando el niño responde, llama a la herramienta de cliente "elegir_opcion" (letra):
///   Unity ilumina la opción, dice si es correcta y el agente responde según el resultado.
/// - Mientras dura el quiz, la lectura en vivo se pausa y no se abren dudas.
/// - Guarda los resultados (sin datos personales) en resultados_quiz.csv (Application.persistentDataPath).
/// Prueba sin ElevenLabs: tecla Q (inicia el quiz) y teclas A, B, C (responde).
/// </summary>
public class QuizSession : MonoBehaviour
{
    [Serializable]
    public class Pregunta
    {
        [TextArea(1, 3)] public string texto;
        [Tooltip("Alternativas en su orden original (se mezclan al mostrarlas)")]
        public string[] opciones = new string[3];
        [Tooltip("Índice de la alternativa correcta en el orden original (0 = primera)")]
        public int correcta;
    }

    [Serializable]
    public class Quiz
    {
        public string id = "pagina_1";
        [Tooltip("Lo que dice el Principito antes de la primera pregunta")]
        [TextArea(1, 3)] public string intro = "¡Muy bien! Terminaste esta página. Ahora, un mini quiz.";
        public List<Pregunta> preguntas = new List<Pregunta>();
        [NonSerialized] public bool hecho;
    }

    [Header("Preguntas")]
    public List<Quiz> quizzes = new List<Quiz>();
    public bool mezclarOpciones = true;
    [Tooltip("Intentos por pregunta; al agotarlos, el Principito dice cuál era la correcta")]
    public int maxIntentos = 2;

    [Header("Agente")]
    [Tooltip("Agente \"Principito quiz\" (configKey = agentIdQuiz)")]
    public ElevenLabsAgentClient agent;
    public string toolName = "elegir_opcion";
    [Tooltip("Mientras conversa el agente de dudas, el quiz espera")]
    public ElevenLabsAgentClient doubtAgent;
    [Tooltip("Agente de lectura: el quiz espera a que se cierre antes de conectarse (evita dos conversaciones a la vez)")]
    public ElevenLabsAgentClient readingAgent;
    public DoubtSession doubts;

    [Header("Proyección")]
    public SpriteAnimator2D principito;
    public ProjectedCard panel;
    public ProjectedCard titulo;
    public ProjectedCard pregunta;
    [Tooltip("Tarjetas de las alternativas en pantalla: posición A, B y C")]
    public ProjectedCard[] opciones = new ProjectedCard[3];
    public StarBurst estrellas;
    [Tooltip("Animaciones que deben terminar antes de empezar el quiz (p. ej. el Principito volando)")]
    public PrincipitoVolando[] esperarAnimaciones;

    [Header("Animaciones del Principito")]
    public string animQuiz = "Quiz";
    public string animRest = "Reposo";
    public string animTalk = "Hablando";
    public string animThink = "Pensando";
    public string animRight = "Correcto";
    public float fadeTime = 0.6f;
    [Tooltip("Segundos que el Principito celebra una respuesta correcta antes de volver a hablar")]
    public float celebrar = 2.5f;

    [Header("Resultados")]
    public bool guardarResultados = true;
    public string archivoResultados = "resultados_quiz.csv";

    [Header("Prueba")]
    public bool tecladoPrueba = true;

    public bool IsRunning { get; private set; }

    static readonly string[] Letras = { "A", "B", "C" };

    // Estado de la pregunta actual
    Quiz quizActual;
    Pregunta preguntaActual;
    int numeroPregunta;
    int[] orden = { 0, 1, 2 };   // posición en pantalla → índice original
    int posCorrecta;
    int intentos;
    bool resuelta;
    bool acierto;
    readonly HashSet<int> descartadas = new HashSet<int>();
    SpriteRenderer principitoSr;

    void Awake()
    {
        if (principito) principitoSr = principito.GetComponent<SpriteRenderer>();
        foreach (var c in Cards()) if (c && c.label) { c.label.enableAutoSizing = true; c.label.fontSizeMin = c.label.fontSize * 0.55f; c.label.fontSizeMax = c.label.fontSize; }
        // Alternativas en una sola línea: se achica la letra si el texto es largo
        if (opciones != null) foreach (var c in opciones) if (c && c.label) c.label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
    }

    void OnEnable() { if (agent) agent.RegisterTool(toolName, OnElegir); }
    void OnDisable() { if (agent) agent.RegisterTool(toolName, null); }

    void Start() { HideAll(true); }

    /// Inicia el quiz con ese id (lo llama ReadingSession al detectar el final de la página).
    public void IniciarQuiz(string id)
    {
        var q = quizzes.Find(x => x.id == id);
        if (q == null) { Debug.LogWarning($"[Quiz] No existe el quiz \"{id}\"."); return; }
        if (q.hecho || IsRunning) return;
        StartCoroutine(Run(q));
    }

    [ContextMenu("Iniciar quiz de la página 1")]
    void IniciarPrimero() { if (quizzes.Count > 0) { quizzes[0].hecho = false; IniciarQuiz(quizzes[0].id); } }

    IEnumerator Run(Quiz q)
    {
        IsRunning = true;
        q.hecho = true;
        quizActual = q;
        if (doubts) doubts.bloqueada = true;

        // Espera a que termine una animación o una duda en curso, y a que se cierre la lectura
        while (Busy()) yield return null;
        for (float t = 0f; t < 4f && readingAgent && readingAgent.IsRunning; t += Time.deltaTime) yield return null;
        if (agent && !agent.HasAgentId())
            Debug.LogWarning("[Quiz] Falta el Agent ID del quiz (agentIdQuiz) en " + System.IO.Path.Combine(Application.streamingAssetsPath, "elevenlabs.local.json"));
        Debug.Log($"[Quiz] Empieza el quiz \"{q.id}\" ({q.preguntas.Count} preguntas).");

        StartCoroutine(Fade(1f));
        Anim(animQuiz);
        Show(panel);
        Show(titulo);

        for (int i = 0; i < q.preguntas.Count; i++)
        {
            numeroPregunta = i + 1;
            PrepararPregunta(q.preguntas[i]);
            if (i > 0) Anim(animQuiz);   // nueva pregunta: vuelve al cartel "?"
            if (titulo && titulo.label) titulo.label.text = $"<b>Mini quiz</b>\n<size=70%>Pregunta {numeroPregunta} de {q.preguntas.Count}</size>";
            yield return new WaitForSeconds(0.6f);

            // El agente lee la pregunta y escucha la respuesta
            var vars = new Dictionary<string, string>
            {
                { "intro", i == 0 ? q.intro : "¡Vamos con la siguiente!" },
                { "numero_pregunta", numeroPregunta.ToString() },
                { "total_preguntas", q.preguntas.Count.ToString() },
                { "pregunta", preguntaActual.texto },
                { "opcion_a", preguntaActual.opciones[orden[0]] },
                { "opcion_b", preguntaActual.opciones[orden[1]] },
                { "opcion_c", preguntaActual.opciones[orden[2]] },
            };
            bool conectado = false;
            if (agent)
            {
                agent.StartConversation(vars);
                for (float t = 0f; t < 6f && !agent.IsRunning; t += Time.deltaTime) yield return null;
                conectado = agent.IsRunning;
                while (agent.IsRunning) yield return null;
            }
            if (!conectado)
            {
                Debug.LogWarning("[Quiz] El agente del quiz no se conectó (revisa agentIdQuiz). Responde con las teclas A, B o C.");
                for (float t = 0f; t < 20f && !resuelta; t += Time.deltaTime) yield return null;
                yield return new WaitForSeconds(1.5f);
            }

            Guardar(q, preguntaActual);
            yield return HidePregunta();
        }

        HideAll(false);
        Anim(animRest);
        yield return Fade(0f);
        if (doubts) doubts.bloqueada = false;
        quizActual = null;
        preguntaActual = null;
        IsRunning = false;
        Debug.Log("[Quiz] Terminado: sigue la lectura.");
    }

    bool Busy()
    {
        if (doubtAgent && doubtAgent.IsRunning) return true;
        if (esperarAnimaciones != null) foreach (var a in esperarAnimaciones) if (a && a.IsPlaying) return true;
        return false;
    }

    void PrepararPregunta(Pregunta p)
    {
        preguntaActual = p;
        intentos = 0;
        resuelta = false;
        acierto = false;
        descartadas.Clear();
        orden = new[] { 0, 1, 2 };
        if (mezclarOpciones)
            for (int i = orden.Length - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (orden[i], orden[j]) = (orden[j], orden[i]); }
        posCorrecta = Array.IndexOf(orden, p.correcta);

        if (pregunta && pregunta.label) pregunta.label.text = $"<b>{p.texto}</b>";
        pregunta?.ResetState();
        Show(pregunta);
        for (int i = 0; i < opciones.Length; i++)
        {
            var c = opciones[i];
            if (!c) continue;
            c.ResetState();
            if (c.label) c.label.text = $"<b>{Letras[i]}</b>   {p.opciones[orden[i]]}";
            c.Show();
        }
        Debug.Log($"[Quiz] Pregunta {numeroPregunta}: correcta en la posición {Letras[posCorrecta]}.");
    }

    // ───────────── Herramienta del agente: elegir_opcion(letra) ─────────────
    string OnElegir(ElevenLabsAgentClient.ToolArgs args)
    {
        if (preguntaActual == null) return "No hay ninguna pregunta activa.";
        if (resuelta) return "Esta pregunta ya terminó. Despídete con una frase corta y termina la conversación con end_call.";

        string l = (args.letra ?? "").Trim().ToUpperInvariant();
        int pos = l.Length > 0 ? Array.IndexOf(Letras, l.Substring(0, 1)) : -1;
        if (pos < 0) return "Letra no válida. Pídele con cariño que responda diciendo A, B o C.";
        if (descartadas.Contains(pos)) return $"La opción {Letras[pos]} ya la intentó y no era. Pídele que elija otra.";

        intentos++;
        if (pos == posCorrecta)
        {
            resuelta = true;
            acierto = true;
            StartCoroutine(MostrarCorrecta(pos));
            return Ultima()
                ? "correcta, y era la última pregunta del quiz. Felicítalo con alegría, dile que terminó el mini quiz e invítalo a pasar la página para seguir leyendo la historia. Luego termina la conversación con end_call."
                : "correcta. Felicítalo con UNA frase corta y alegre. No te despidas ni hables de seguir leyendo: todavía falta otra pregunta. Luego termina la conversación con end_call.";
        }

        descartadas.Add(pos);
        if (intentos >= maxIntentos)
        {
            resuelta = true;
            StartCoroutine(MostrarIncorrecta(pos, true));
            string ok = preguntaActual.opciones[orden[posCorrecta]];
            return $"incorrecta, y ya no quedan intentos. Dile con cariño que la respuesta correcta era la {Letras[posCorrecta]}: {ok}. " +
                   (Ultima()
                       ? "Era la última pregunta: dile que terminó el mini quiz e invítalo a pasar la página para seguir leyendo la historia. Luego termina la conversación con end_call."
                       : "No te despidas ni hables de seguir leyendo: todavía falta otra pregunta. Luego termina la conversación con end_call.");
        }
        StartCoroutine(MostrarIncorrecta(pos, false));
        return "incorrecta. Dile con cariño que no es esa y que lo intente otra vez. No des pistas. " +
               "Puedes repetir las alternativas que quedan.";
    }

    bool Ultima() => quizActual != null && numeroPregunta >= quizActual.preguntas.Count;

    IEnumerator MostrarCorrecta(int pos)
    {
        Anim(animThink);
        Glow(pos, true);
        yield return new WaitForSeconds(0.8f);
        Glow(pos, false);
        opciones[pos].SetGrow(true);
        for (int i = 0; i < opciones.Length; i++) if (i != pos && opciones[i]) opciones[i].Dim();
        if (estrellas) estrellas.Burst(opciones[pos].transform.position);
        Anim(animRight);
        // Celebra un momento y vuelve a hablar/reposo (Correcto está en bucle)
        yield return new WaitForSeconds(celebrar);
        if (principito && principito.CurrentClip == animRight)
            Anim(agent && agent.IsAgentTalking ? animTalk : animRest);
    }

    IEnumerator MostrarIncorrecta(int pos, bool revelar)
    {
        Anim(animThink);
        Glow(pos, true);
        yield return new WaitForSeconds(0.8f);
        Glow(pos, false);
        opciones[pos].Dim();
        if (revelar)
        {
            opciones[posCorrecta].SetGlow(true);
            for (int i = 0; i < opciones.Length; i++) if (i != posCorrecta && opciones[i]) opciones[i].Dim();
        }
        Anim(animTalk);
    }

    // ───────────── Animación del Principito ─────────────
    void Update()
    {
        if (tecladoPrueba) TecladoPrueba();
        if (!IsRunning || !principito || !agent || !agent.IsRunning) return;
        string cur = principito.CurrentClip;
        if (agent.IsAgentTalking && cur == animRest) principito.Play(animTalk);
        else if (!agent.IsAgentTalking && cur == animTalk) principito.Play(animRest);
    }

    void TecladoPrueba()
    {
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        if (k == null) return;
        if (k.qKey.wasPressedThisFrame) IniciarPrimero();
        if (IsRunning && (agent == null || !agent.IsRunning))
        {
            if (k.aKey.wasPressedThisFrame) Debug.Log("[Quiz] Prueba A → " + OnElegir(new ElevenLabsAgentClient.ToolArgs { letra = "A" }));
            if (k.bKey.wasPressedThisFrame) Debug.Log("[Quiz] Prueba B → " + OnElegir(new ElevenLabsAgentClient.ToolArgs { letra = "B" }));
            if (k.cKey.wasPressedThisFrame) Debug.Log("[Quiz] Prueba C → " + OnElegir(new ElevenLabsAgentClient.ToolArgs { letra = "C" }));
        }
#else
        if (Input.GetKeyDown(KeyCode.Q)) IniciarPrimero();
        if (IsRunning && (agent == null || !agent.IsRunning))
        {
            if (Input.GetKeyDown(KeyCode.A)) Debug.Log("[Quiz] Prueba A → " + OnElegir(new ElevenLabsAgentClient.ToolArgs { letra = "A" }));
            if (Input.GetKeyDown(KeyCode.B)) Debug.Log("[Quiz] Prueba B → " + OnElegir(new ElevenLabsAgentClient.ToolArgs { letra = "B" }));
            if (Input.GetKeyDown(KeyCode.C)) Debug.Log("[Quiz] Prueba C → " + OnElegir(new ElevenLabsAgentClient.ToolArgs { letra = "C" }));
        }
#endif
    }

    IEnumerator Fade(float to)
    {
        if (!principitoSr) yield break;
        float from = principitoSr.color.a;
        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            var c = principitoSr.color; c.a = Mathf.Lerp(from, to, t / fadeTime); principitoSr.color = c;
            yield return null;
        }
        var e = principitoSr.color; e.a = to; principitoSr.color = e;
    }

    // ───────────── Ayudas ─────────────
    IEnumerator HidePregunta()
    {
        Hide(pregunta);
        foreach (var c in opciones) Hide(c);
        yield return new WaitForSeconds(0.8f);
    }

    void HideAll(bool instant)
    {
        foreach (var c in Cards()) if (c) { c.ResetState(); c.Hide(instant); }
    }

    IEnumerable<ProjectedCard> Cards()
    {
        yield return panel;
        yield return titulo;
        yield return pregunta;
        if (opciones != null) foreach (var c in opciones) yield return c;
    }

    void Glow(int pos, bool on) { if (pos >= 0 && pos < opciones.Length && opciones[pos]) opciones[pos].SetGlow(on); }
    void Anim(string clip) { if (principito && !string.IsNullOrEmpty(clip)) principito.Play(clip); }
    static void Show(ProjectedCard c) { if (c) c.Show(); }
    static void Hide(ProjectedCard c) { if (c) c.Hide(); }

    void Guardar(Quiz q, Pregunta p)
    {
        if (!guardarResultados || p == null) return;
        try
        {
            string path = Path.Combine(Application.persistentDataPath, archivoResultados);
            bool nuevo = !File.Exists(path);
            string ordenTxt = string.Join("/", Array.ConvertAll(orden, i => p.opciones[i]));
            string linea = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss};{q.id};{numeroPregunta};\"{p.texto}\";\"{ordenTxt}\";{Letras[posCorrecta]};{intentos};{(acierto ? "acierto" : intentos == 0 ? "sin_respuesta" : "no")}";
            File.AppendAllText(path, (nuevo ? "fecha;quiz;pregunta;texto;orden_mostrado;posicion_correcta;intentos;resultado\n" : "") + linea + "\n");
            Debug.Log("[Quiz] Resultado guardado en " + path);
        }
        catch (Exception e) { Debug.LogWarning("[Quiz] No se pudo guardar el resultado: " + e.Message); }
    }

}
