using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Mini quiz: el de los capítulos lo activa ReadingSession al detectar el final de la página; el quiz "pagina_1"
/// lo inicia el onboarding al terminar las reglas (con las imágenes onboarding_quiz_1 y _2).
/// - Pregunta con imagen: se proyecta la imagen (pregunta y alternativas ya dibujadas) en vez del panel de texto,
///   las alternativas no se mezclan y la elegida se ilumina encima de su píldora.
/// - Muestra las preguntas una por una en un panel a la derecha del libro, con alternativas A, B y C.
/// - Las alternativas se MEZCLAN en cada sesión, para que la correcta no esté siempre en el mismo lugar.
/// - El agente "Principito quiz" de ElevenLabs lee la pregunta y sus alternativas (first message) y escucha la
///   respuesta. Cuando el niño responde, llama a la herramienta de cliente "elegir_opcion" (letra):
///   Unity ilumina la opción, dice si es correcta y el agente responde según el resultado.
/// - Mientras dura el quiz, la lectura en vivo se pausa y no se abren dudas.
/// - Guarda los resultados (sin datos personales) en resultados_quiz.csv (Application.persistentDataPath).
/// Quizzes por capítulo: banco en StreamingAssets/quiz_preguntas.json; se sortea 1 pregunta literal + 1 inferencial.
/// Prueba sin ElevenLabs: Q (quiz de la página 1), 1 / 2 (quiz del capítulo 1 / 2) y A, B, C (responde).
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
        [Tooltip("Imagen con la pregunta y las alternativas ya dibujadas (opcional). Con imagen no se muestra el panel de texto ni se mezclan las alternativas")]
        public Sprite imagen;
        [Tooltip("Zonas de las píldoras A, B y C dentro de la imagen (0–1, origen arriba a la izquierda: x, y, ancho, alto)")]
        public Rect[] zonas = new Rect[3];
    }

    [Serializable]
    public class Quiz
    {
        public string id = "pagina_1";
        [Tooltip("Lo que dice el Principito antes de la primera pregunta")]
        [TextArea(1, 3)] public string intro = "¡Muy bien! Terminaste esta página. Ahora, un mini quiz.";
        public List<Pregunta> preguntas = new List<Pregunta>();
        [Tooltip("Márcalo en el quiz del último capítulo: al terminar, el Principito felicita por acabar el libro en vez de invitar a pasar la página")]
        public bool finDelLibro;
        [Tooltip("Lo que debe decir el Principito al terminar la última pregunta (vacío = invitar a pasar la página y seguir leyendo)")]
        [TextArea(1, 3)] public string cierre;
        [NonSerialized] public bool hecho;
    }

    // Banco de preguntas por capítulo (StreamingAssets/quiz_preguntas.json). En cada sesión se sortea
    // 1 pregunta literal + 1 inferencial, en orden aleatorio, para que a cada niño le toque un par distinto.
    [Serializable]
    public class BancoQuiz
    {
        public string id;
        public string intro;
        public bool finDelLibro;
        public List<Pregunta> literales = new List<Pregunta>();
        public List<Pregunta> inferenciales = new List<Pregunta>();
    }

    [Serializable]
    class Banco { public List<BancoQuiz> quizzes = new List<BancoQuiz>(); }

    [Header("Preguntas")]
    [Tooltip("Quizzes con preguntas fijas (p. ej. pagina_1). Si un id también existe en el banco, se usa el banco")]
    public List<Quiz> quizzes = new List<Quiz>();
    [Tooltip("Banco de preguntas por capítulo, en StreamingAssets")]
    public string bancoArchivo = "quiz_preguntas.json";
    [Tooltip("Ids de quiz con los que termina el libro (no se invita a pasar la página)")]
    public string[] idsFinDelLibro = { "capitulo_2" };

    [Header("Nombre del niño (lo pregunta el onboarding)")]
    [Tooltip("Si el onboarding guardó el nombre, el quiz empieza diciéndolo")]
    public bool usarNombre = true;
    [Tooltip("Inicio del quiz con nombre. {nombre} = nombre del niño; {intro} = intro del banco sin \"¡Muy bien!\" ni \"Ahora, un mini quiz.\"")]
    public string introConNombre = "¡Muy bien, {nombre}! {intro} Ahora responde estas preguntas del mini quiz.";
    readonly HashSet<string> bancosHechos = new HashSet<string>();
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
    [Tooltip("Donde se proyecta la imagen de las preguntas con imagen (SpriteRenderer + ProjectedCard)")]
    public ProjectedCard imagenPregunta;
    [Tooltip("Centro de la imagen de la pregunta (a la derecha del libro)")]
    public Vector2 posImagen = new Vector2(5.0f, 0.1f);
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
    /// ¿El agente habló en el último quiz? (si no, se respondió con el teclado de prueba)
    public bool UsoAgente { get; private set; }

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
    bool modoImagen;
    struct Original { public Vector3 pos; public Color color; public float fillAlpha; public Vector2 fill, border; }
    Original[] originales;

    void Awake()
    {
        if (principito) principitoSr = principito.GetComponent<SpriteRenderer>();
        foreach (var c in Cards()) if (c && c.label) { c.label.enableAutoSizing = true; c.label.fontSizeMin = c.label.fontSize * 0.55f; c.label.fontSizeMax = c.label.fontSize; }
        // Alternativas en una sola línea: se achica la letra si el texto es largo
        if (opciones != null) foreach (var c in opciones) if (c && c.label) c.label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        // Cómo están las alternativas en el panel de texto, para volver a él después de una pregunta con imagen
        originales = new Original[opciones.Length];
        for (int i = 0; i < opciones.Length; i++)
        {
            var c = opciones[i]; if (!c) continue;
            originales[i] = new Original { pos = c.transform.position, color = c.color, fillAlpha = c.fillAlpha,
                fill = c.fill ? c.fill.size : Vector2.zero, border = c.border ? c.border.size : Vector2.zero };
        }
    }

    void OnEnable() { if (agent) agent.RegisterTool(toolName, OnElegir); }
    void OnDisable() { if (agent) agent.RegisterTool(toolName, null); }

    void Start() { HideAll(true); }

    /// Inicia el quiz con ese id (lo llama ReadingSession al detectar el final de la página).
    public void IniciarQuiz(string id)
    {
        if (IsRunning) return;

        // 1) ¿Hay un banco de preguntas para este id? Se sortean 2 preguntas (una literal y una inferencial)
        var delBanco = ArmarDesdeBanco(id);
        if (delBanco != null)
        {
            if (!bancosHechos.Add(id)) return;   // ya se hizo en esta sesión de juego
            StartCoroutine(Run(delBanco));
            return;
        }

        // 2) Si no, el quiz fijo de la escena
        var q = quizzes.Find(x => x.id == id);
        if (q == null) { Debug.LogWarning($"[Quiz] No existe el quiz \"{id}\"."); return; }
        if (q.hecho) return;
        StartCoroutine(Run(q));
    }

    /// Lo vuelve a hacer aunque ya se haya hecho en esta sesión (el onboarding lo usa con cada niño nuevo).
    public void IniciarQuizDeNuevo(string id)
    {
        var q = quizzes.Find(x => x.id == id); if (q != null) q.hecho = false;
        bancosHechos.Remove(id);
        IniciarQuiz(id);
    }

    [ContextMenu("Iniciar quiz de la página 1")]
    void IniciarPrimero() { if (quizzes.Count > 0) { quizzes[0].hecho = false; IniciarQuiz(quizzes[0].id); } }

    [ContextMenu("Probar quiz del capítulo 1 (sorteo)")]
    void ProbarCapitulo1() { IniciarCapituloPrueba("capitulo_1"); }

    [ContextMenu("Probar quiz del capítulo 2 (sorteo)")]
    void ProbarCapitulo2() { IniciarCapituloPrueba("capitulo_2"); }

    void IniciarCapituloPrueba(string id) { bancosHechos.Remove(id); IniciarQuiz(id); }

    // ───────────── Banco de preguntas ─────────────
    Banco CargarBanco()
    {
        if (string.IsNullOrEmpty(bancoArchivo)) return null;
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, bancoArchivo);
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<Banco>(File.ReadAllText(path));
        }
        catch (Exception e) { Debug.LogWarning("[Quiz] No se pudo leer el banco de preguntas: " + e.Message); return null; }
    }

    static bool Valida(Pregunta p) =>
        p != null && !string.IsNullOrWhiteSpace(p.texto) && p.opciones != null && p.opciones.Length == 3
        && p.correcta >= 0 && p.correcta < 3;

    static Pregunta Sortear(List<Pregunta> lista, Pregunta evitar = null)
    {
        var validas = lista?.FindAll(p => Valida(p) && p != evitar);
        return validas == null || validas.Count == 0 ? null : validas[UnityEngine.Random.Range(0, validas.Count)];
    }

    /// Arma un quiz con 1 pregunta literal y 1 inferencial al azar (en orden aleatorio). Null si el id no tiene banco.
    Quiz ArmarDesdeBanco(string id)
    {
        var b = CargarBanco()?.quizzes?.Find(x => x.id == id);
        if (b == null) return null;

        var lit = Sortear(b.literales);
        var inf = Sortear(b.inferenciales);
        var elegidas = new List<Pregunta>();
        if (lit != null) elegidas.Add(lit);
        if (inf != null) elegidas.Add(inf);
        // Si falta un tipo, se completa con otra pregunta del que sí hay
        if (elegidas.Count == 1)
        {
            var otra = Sortear(lit != null ? b.literales : b.inferenciales, elegidas[0]);
            if (otra != null) elegidas.Add(otra);
        }
        if (elegidas.Count == 0) { Debug.LogWarning($"[Quiz] El banco \"{id}\" no tiene preguntas válidas."); return null; }
        if (elegidas.Count == 2 && UnityEngine.Random.value < 0.5f) (elegidas[0], elegidas[1]) = (elegidas[1], elegidas[0]);

        var q = new Quiz { id = id, preguntas = elegidas, finDelLibro = b.finDelLibro };
        if (!string.IsNullOrWhiteSpace(b.intro)) q.intro = b.intro;
        Debug.Log($"[Quiz] Sorteo \"{id}\": " + string.Join(" | ", elegidas.ConvertAll(p => p.texto)));
        return q;
    }

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

        UsoAgente = false;
        StartCoroutine(Fade(1f));
        Anim(animQuiz);
        bool todasConImagen = imagenPregunta && q.preguntas.TrueForAll(x => x.imagen);
        if (!todasConImagen) { Show(panel); Show(titulo); }

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
                { "intro", i == 0 ? IntroInicial(q.intro) : "¡Vamos con la siguiente!" },
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
                if (conectado) UsoAgente = true;
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
        Debug.Log(FinDelLibro() ? "[Quiz] Terminado: fin del libro." : "[Quiz] Terminado: sigue la lectura.");
        quizActual = null;
        preguntaActual = null;
        IsRunning = false;
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
        modoImagen = p.imagen && imagenPregunta;
        if (mezclarOpciones && !modoImagen)   // con imagen, las letras ya están dibujadas
            for (int i = orden.Length - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (orden[i], orden[j]) = (orden[j], orden[i]); }
        posCorrecta = Array.IndexOf(orden, p.correcta);

        if (modoImagen) { PrepararImagen(p); Debug.Log($"[Quiz] Pregunta {numeroPregunta} (imagen): correcta en la posición {Letras[posCorrecta]}."); return; }

        if (pregunta && pregunta.label) pregunta.label.text = $"<b>{p.texto}</b>";
        pregunta?.ResetState();
        Show(pregunta);
        for (int i = 0; i < opciones.Length; i++)
        {
            var c = opciones[i];
            if (!c) continue;
            RestaurarTarjeta(i);
            c.ResetState();
            if (c.label) c.label.text = $"<b>{Letras[i]}</b>   {p.opciones[orden[i]]}";
            c.Show();
        }
        Debug.Log($"[Quiz] Pregunta {numeroPregunta}: correcta en la posición {Letras[posCorrecta]}.");
    }

    /// Proyecta la imagen de la pregunta y coloca las tarjetas de las alternativas sobre sus píldoras (ocultas hasta que se elige una).
    void PrepararImagen(Pregunta p)
    {
        var sr = imagenPregunta.fill ? imagenPregunta.fill : imagenPregunta.GetComponent<SpriteRenderer>();
        if (sr) sr.sprite = p.imagen;
        imagenPregunta.transform.position = new Vector3(posImagen.x, posImagen.y, 0f);
        imagenPregunta.ResetState();
        imagenPregunta.Show();
        Vector2 tam = p.imagen.bounds.size;
        for (int i = 0; i < opciones.Length; i++)
        {
            var c = opciones[i]; if (!c) continue;
            c.ResetState(); c.Hide(true);
            if (c.label) c.label.text = "";
            if (p.zonas == null || i >= p.zonas.Length) continue;
            var z = p.zonas[i];
            var centro = posImagen + new Vector2((z.center.x - 0.5f) * tam.x, (0.5f - z.center.y) * tam.y);
            var medida = new Vector2(z.width * tam.x, z.height * tam.y) + new Vector2(0.08f, 0.08f);
            c.transform.position = new Vector3(centro.x, centro.y, 0f);
            if (c.fill) c.fill.size = medida;
            if (c.border) c.border.size = medida;
            c.color = Color.white;
            c.fillAlpha = 0f;   // solo el borde brillante; el relleno aparece al brillar
        }
    }

    void RestaurarTarjeta(int i)
    {
        var c = opciones[i]; if (!c || originales == null || i >= originales.Length) return;
        var o = originales[i];
        c.transform.position = o.pos; c.color = o.color; c.fillAlpha = o.fillAlpha;
        if (c.fill) c.fill.size = o.fill;
        if (c.border) c.border.size = o.border;
    }

    /// Alternativa descartada: en el panel de texto se atenúa; sobre la imagen se oscurece su píldora.
    void Apagar(int i)
    {
        var c = (i >= 0 && i < opciones.Length) ? opciones[i] : null; if (!c) return;
        if (!modoImagen) { c.Dim(); return; }
        c.SetGlow(false); c.color = Color.black; c.fillAlpha = 0.55f; c.Show();
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
                ? "correcta, y era la última pregunta del quiz. Felicítalo con alegría. " + CierreFinal()
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
                       ? "Era la última pregunta. " + CierreFinal()
                       : "No te despidas ni hables de seguir leyendo: todavía falta otra pregunta. Luego termina la conversación con end_call.");
        }
        StartCoroutine(MostrarIncorrecta(pos, false));
        return "incorrecta. Dile con cariño que no es esa y que lo intente otra vez. No des pistas. " +
               "Puedes repetir las alternativas que quedan.";
    }

    bool Ultima() => quizActual != null && numeroPregunta >= quizActual.preguntas.Count;

    /// Intro de la primera pregunta: con el nombre del niño si el onboarding lo guardó.
    /// "¡Muy bien! Terminaste el capítulo uno. Ahora, un mini quiz." →
    /// "¡Muy bien, Camila! Terminaste el capítulo uno. Ahora responde estas preguntas del mini quiz."
    string IntroInicial(string intro)
    {
        intro = intro ?? "";
        if (!usarNombre || !SesionLector.TieneNombre || string.IsNullOrWhiteSpace(introConNombre)) return intro;
        string medio = intro.Trim();
        if (medio.StartsWith("¡Muy bien!")) medio = medio.Substring("¡Muy bien!".Length).Trim();
        if (medio.EndsWith("Ahora, un mini quiz.")) medio = medio.Substring(0, medio.Length - "Ahora, un mini quiz.".Length).Trim();
        string r = introConNombre.Replace("{nombre}", SesionLector.Nombre).Replace("{intro}", medio);
        while (r.Contains("  ")) r = r.Replace("  ", " ");
        return r.Trim();
    }

    /// ¿Con este quiz termina el libro? (casilla Fin Del Libro, "finDelLibro" en el banco o id en idsFinDelLibro)
    bool FinDelLibro() => quizActual != null &&
        (quizActual.finDelLibro || (idsFinDelLibro != null && Array.IndexOf(idsFinDelLibro, quizActual.id) >= 0));

    /// Lo que debe decir el agente al terminar la última pregunta.
    string CierreFinal() => quizActual != null && !string.IsNullOrWhiteSpace(quizActual.cierre)
        ? quizActual.cierre.Trim() + " Luego termina la conversación con end_call."
        : FinDelLibro()
        ? "Con este quiz TERMINÓ EL LIBRO: NO lo invites a pasar la página ni a seguir leyendo. Dile que terminó el mini quiz y también todo el libro, y felicítalo con mucho cariño por haber leído toda la historia. Luego termina la conversación con end_call."
        : "Dile que terminó el mini quiz e invítalo a pasar la página para seguir leyendo la historia. Luego termina la conversación con end_call.";

    IEnumerator MostrarCorrecta(int pos)
    {
        Anim(animThink);
        Show(opciones[pos]);
        Glow(pos, true);
        yield return new WaitForSeconds(0.8f);
        Glow(pos, false);
        opciones[pos].SetGrow(true);
        for (int i = 0; i < opciones.Length; i++) if (i != pos && opciones[i]) Apagar(i);
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
        Show(opciones[pos]);
        Glow(pos, true);
        yield return new WaitForSeconds(0.8f);
        Glow(pos, false);
        Apagar(pos);
        if (revelar)
        {
            Show(opciones[posCorrecta]);
            opciones[posCorrecta].SetGlow(true);
            for (int i = 0; i < opciones.Length; i++) if (i != posCorrecta && opciones[i]) Apagar(i);
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
        if (k.digit1Key.wasPressedThisFrame) ProbarCapitulo1();
        if (k.digit2Key.wasPressedThisFrame) ProbarCapitulo2();
        if (IsRunning && (agent == null || !agent.IsRunning))
        {
            if (k.aKey.wasPressedThisFrame) Debug.Log("[Quiz] Prueba A → " + OnElegir(new ElevenLabsAgentClient.ToolArgs { letra = "A" }));
            if (k.bKey.wasPressedThisFrame) Debug.Log("[Quiz] Prueba B → " + OnElegir(new ElevenLabsAgentClient.ToolArgs { letra = "B" }));
            if (k.cKey.wasPressedThisFrame) Debug.Log("[Quiz] Prueba C → " + OnElegir(new ElevenLabsAgentClient.ToolArgs { letra = "C" }));
        }
#else
        if (Input.GetKeyDown(KeyCode.Q)) IniciarPrimero();
        if (Input.GetKeyDown(KeyCode.Alpha1)) ProbarCapitulo1();
        if (Input.GetKeyDown(KeyCode.Alpha2)) ProbarCapitulo2();
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
        Hide(imagenPregunta);
        foreach (var c in opciones) Hide(c);
        yield return new WaitForSeconds(0.8f);
        if (modoImagen) for (int i = 0; i < opciones.Length; i++) RestaurarTarjeta(i);
        modoImagen = false;
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
        yield return imagenPregunta;
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
