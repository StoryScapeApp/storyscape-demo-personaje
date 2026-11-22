using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Quiz: el de los capítulos lo activa ReadingSession al detectar el final de la página; el quiz "pagina_1"
/// lo inicia el onboarding al terminar las reglas (mismo panel de texto que los quizzes de capítulo).
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
        [TextArea(1, 3)] public string intro = "¡Muy bien! Terminaste esta página. Ahora, un quiz.";
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
    [Tooltip("Inicio del quiz con nombre. {nombre} = nombre del niño; {intro} = intro del banco sin \"¡Muy bien!\" ni \"Ahora, un quiz.\"")]
    public string introConNombre = "¡Muy bien, {nombre}! {intro} Ahora responde estas preguntas del quiz.";
    readonly HashSet<string> bancosHechos = new HashSet<string>();
    public bool mezclarOpciones = true;
    [Tooltip("Intentos por pregunta; al agotarlos, el Principito dice cuál era la correcta")]
    public int maxIntentos = 2;
    [Tooltip("Solo acepta la letra si lo que se transcribió del niño la contiene (\"la A\", \"B\", \"la tercera\") o nombra esa alternativa. " +
             "Evita que el ruido de fondo o una conversación cercana cuenten como respuesta")]
    public bool validarRespuesta = true;
    [Tooltip("Una letra suelta solo cuenta si la frase tiene como mucho estas palabras (\"a\", \"se\" o \"ve\" aparecen en cualquier frase de fondo)")]
    public int maxPalabrasLetraSuelta = 3;

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
    [Header("Diseño del panel")]
    [Tooltip("Estrella con rayitos sobre el borde superior del panel")]
    public ProjectedCard estrella;
    [Tooltip("Línea curva debajo de la pregunta")]
    public ProjectedCard subrayado;
    [Tooltip("Fuente de la pregunta (título)")]
    public TMPro.TMP_FontAsset fuenteTitulo;
    [Tooltip("Fuente de las alternativas y del contador de preguntas")]
    public TMPro.TMP_FontAsset fuenteOpciones;
    public float tamPregunta = 2.5f;
    [Tooltip("Mismo tamaño de letra para todas las alternativas; si el texto no cabe, pasa a dos renglones y la píldora crece")]
    public float tamOpciones = 1.9f;
    public float altoMinOpcion = 0.55f;
    public float separacionOpciones = 0.13f;
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
    /// Pausa por comando de voz: el agente se cuelga y, al continuar, se vuelve a leer la misma pregunta.
    public bool Pausado { get; private set; }
    /// Id del último quiz que se inició (para "repite el quiz").
    public string UltimoId { get; private set; }
    bool repetirPregunta, esRepeticion;
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
    string ultimoTranscript;   // lo último que el agente transcribió del niño en esta pregunta
    SpriteRenderer principitoSr;
    bool modoImagen;
    struct Original { public Vector3 pos; public Color color; public float fillAlpha; public Vector2 fill, border; public bool rellenoPropio; }
    Original[] originales;
    float panelTop, panelBottom, panelX;   // borde superior/inferior del panel en la escena (crece hacia abajo si hace falta)
    Vector2 panelSize;

    void Awake()
    {
        if (principito) principitoSr = principito.GetComponent<SpriteRenderer>();
        // Contador "Quiz · Pregunta 1 de 2": pequeño, una línea
        if (titulo && titulo.label)
        {
            if (fuenteOpciones) titulo.label.font = fuenteOpciones;
            titulo.label.enableAutoSizing = false; titulo.label.fontSize = 1.45f; titulo.label.fontStyle = TMPro.FontStyles.Normal;
            titulo.label.color = new Color(0.35f, 0.38f, 0.56f);
        }
        // Pregunta = título en Bold (hasta 2–3 renglones)
        if (pregunta && pregunta.label)
        {
            if (fuenteTitulo) pregunta.label.font = fuenteTitulo;
            pregunta.label.enableAutoSizing = false; pregunta.label.fontSize = tamPregunta;
            pregunta.label.textWrappingMode = TMPro.TextWrappingModes.Normal; pregunta.label.lineSpacing = -8f;
        }
        // Alternativas: mismo tamaño para todas; si no cabe, dos renglones
        if (opciones != null) foreach (var c in opciones) if (c && c.label)
        {
            if (fuenteOpciones) c.label.font = fuenteOpciones;
            c.label.enableAutoSizing = false; c.label.fontSize = tamOpciones;
            c.label.textWrappingMode = TMPro.TextWrappingModes.Normal; c.label.lineSpacing = -10f;
        }
        if (panel && panel.fill)
        {
            panelSize = panel.fill.size; panelX = panel.transform.position.x;
            panelTop = panel.transform.position.y + panelSize.y * 0.5f; panelBottom = panel.transform.position.y - panelSize.y * 0.5f;
        }
        // Cómo están las alternativas en el panel de texto, para volver a él después de una pregunta con imagen
        originales = new Original[opciones.Length];
        for (int i = 0; i < opciones.Length; i++)
        {
            var c = opciones[i]; if (!c) continue;
            originales[i] = new Original { pos = c.transform.position, color = c.color, fillAlpha = c.fillAlpha,
                fill = c.fill ? c.fill.size : Vector2.zero, border = c.border ? c.border.size : Vector2.zero, rellenoPropio = c.rellenoPropio };
        }
    }

    void OnEnable() { if (agent) { agent.RegisterTool(toolName, OnElegir); agent.UserTranscript += OnTranscript; } }
    void OnDisable() { if (agent) { agent.RegisterTool(toolName, null); agent.UserTranscript -= OnTranscript; } }
    void OnTranscript(string s) { if (IsRunning) ultimoTranscript = s; }

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

    // ───────────── Comandos de voz ─────────────
    /// Vuelve a permitir todos los quizzes (nuevo sorteo): los chapters se disparan otra vez al leer su frase.
    public void RearmarQuizzes()
    {
        bancosHechos.Clear();
        foreach (var q in quizzes) q.hecho = false;
    }

    /// Corta el quiz en curso sin guardar nada y deja la mesa limpia.
    public void Cancelar(bool desvanecer = true)
    {
        bool estaba = IsRunning;
        StopAllCoroutines();
        if (agent && agent.IsRunning) agent.EndConversation();
        Pausado = false;
        repetirPregunta = esRepeticion = false;
        IsRunning = false;
        quizActual = null;
        preguntaActual = null;
        if (doubts) doubts.bloqueada = false;
        HideAll(true);
        if (estaba)
        {
            Anim(animRest);
            if (desvanecer) StartCoroutine(Fade(0f));
            Debug.Log("[Quiz] Cancelado por comando.");
        }
    }

    /// "Repite el quiz": nuevo sorteo (si hay uno en curso, lo corta y empieza otro; si ya terminó, repite el último).
    public bool ReiniciarQuiz()
    {
        string id = IsRunning && quizActual != null ? quizActual.id : UltimoId;
        if (string.IsNullOrEmpty(id)) return false;
        Cancelar(false);   // sin desvanecer: el quiz nuevo empieza enseguida
        IniciarQuizDeNuevo(id);
        return true;
    }

    /// "Repite la pregunta": el agente vuelve a leerla (mismas alternativas).
    public bool RepetirPregunta()
    {
        if (!IsRunning || preguntaActual == null) return false;
        repetirPregunta = true;
        if (agent && agent.IsRunning) agent.EndConversation();
        return true;
    }

    public void Pausar()
    {
        if (!IsRunning) return;
        Pausado = true;
        if (agent && agent.IsRunning) agent.EndConversation();   // el micrófono queda libre para oír "continúa"
    }

    public void Reanudar() { Pausado = false; }

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
        UltimoId = q.id;
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
        if (!todasConImagen) { Show(panel); Show(titulo); Show(estrella); }

        for (int i = 0; i < q.preguntas.Count; i++)
        {
            bool repite = esRepeticion;   // "repite la pregunta" / volvió de una pausa: misma pregunta, mismas alternativas
            esRepeticion = false;
            numeroPregunta = i + 1;
            if (!repite) PrepararPregunta(q.preguntas[i]);
            if (i > 0 || repite) Anim(animQuiz);   // nueva pregunta: vuelve al cartel "?"
            if (titulo && titulo.label) titulo.label.text = $"Quiz · Pregunta {numeroPregunta} de {q.preguntas.Count}";
            if (!modoImagen) Maquetar();
            yield return new WaitForSeconds(0.6f);

            // El agente lee la pregunta y escucha la respuesta
            var vars = new Dictionary<string, string>
            {
                { "intro", repite ? "Te la repito." : i == 0 ? IntroInicial(q.intro) : "¡Vamos con la siguiente!" },
                { "numero_pregunta", numeroPregunta.ToString() },
                { "total_preguntas", q.preguntas.Count.ToString() },
                { "pregunta", preguntaActual.texto },
                { "opcion_a", preguntaActual.opciones[orden[0]] },
                { "opcion_b", preguntaActual.opciones[orden[1]] },
                { "opcion_c", preguntaActual.opciones[orden[2]] },
            };
            bool conectado = false;
            while (Pausado) yield return null;
            if (agent)
            {
                agent.StartConversation(vars);
                for (float t = 0f; t < 6f && !agent.IsRunning; t += Time.deltaTime) yield return null;
                conectado = agent.IsRunning;
                if (conectado) UsoAgente = true;
                while (agent.IsRunning) yield return null;

                // Comando de voz: se colgó el agente para repetir la pregunta o por una pausa
                if (!resuelta && (repetirPregunta || Pausado))
                {
                    repetirPregunta = false;
                    while (Pausado) yield return null;
                    esRepeticion = true;
                    i--;
                    continue;
                }
                repetirPregunta = false;
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
        ultimoTranscript = null;
        orden = new[] { 0, 1, 2 };
        modoImagen = p.imagen && imagenPregunta;
        if (mezclarOpciones && !modoImagen)   // con imagen, las letras ya están dibujadas
            for (int i = orden.Length - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (orden[i], orden[j]) = (orden[j], orden[i]); }
        posCorrecta = Array.IndexOf(orden, p.correcta);

        if (modoImagen) { PrepararImagen(p); Debug.Log($"[Quiz] Pregunta {numeroPregunta} (imagen): correcta en la posición {Letras[posCorrecta]}."); return; }

        if (pregunta && pregunta.label) pregunta.label.text = p.texto;   // la fuente del título ya es Bold
        pregunta?.ResetState();
        Show(pregunta);
        for (int i = 0; i < opciones.Length; i++)
        {
            var c = opciones[i];
            if (!c) continue;
            RestaurarTarjeta(i);
            c.ResetState();
            if (c.label) c.label.text = $"<b>{Letras[i]}</b><indent=13%>{p.opciones[orden[i]]}</indent>";   // el 2.º renglón queda alineado con el texto
            c.Show();
        }
        subrayado?.ResetState();
        Show(subrayado);
        Debug.Log($"[Quiz] Pregunta {numeroPregunta}: correcta en la posición {Letras[posCorrecta]}.");
    }

    /// Acomoda el panel de arriba hacia abajo según el largo de los textos: contador, pregunta (1–3 renglones),
    /// línea curva y alternativas (cada píldora crece si su texto ocupa dos renglones). El panel crece hacia abajo si hace falta.
    void Maquetar()
    {
        if (!panel || !panel.fill || panelSize == Vector2.zero) return;
        float x = panelX, y = panelTop - 0.40f;   // espacio para la estrella
        if (estrella) estrella.transform.position = new Vector3(x, panelTop + 0.04f, 0f);
        if (titulo) { titulo.transform.position = new Vector3(x, y - 0.16f, 0f); y -= 0.36f; }
        if (pregunta && pregunta.label)
        {
            var pl = pregunta.label; float w = pl.rectTransform.sizeDelta.x;
            float h = Mathf.Max(0.35f, pl.GetPreferredValues(pl.text, w, 0f).y);
            pl.rectTransform.sizeDelta = new Vector2(w, h);
            pregunta.transform.position = new Vector3(x, y - h * 0.5f, 0f);
            y -= h;
        }
        if (subrayado) { subrayado.transform.position = new Vector3(x, y - 0.09f, 0f); y -= 0.28f; } else y -= 0.15f;
        for (int i = 0; i < opciones.Length; i++)
        {
            var c = opciones[i]; if (!c || !c.label) continue;
            float w = c.label.rectTransform.sizeDelta.x;
            float h = Mathf.Max(altoMinOpcion, c.label.GetPreferredValues(c.label.text, w, 0f).y + 0.2f);
            float ancho = originales != null && i < originales.Length && originales[i].fill != Vector2.zero ? originales[i].fill.x : (c.fill ? c.fill.size.x : 2.6f);
            if (c.fill) c.fill.size = new Vector2(ancho, h);
            if (c.border) c.border.size = new Vector2(ancho, h);
            c.label.rectTransform.sizeDelta = new Vector2(w, h);
            c.transform.position = new Vector3(x, y - h * 0.5f, 0f);
            y -= h + separacionOpciones;
        }
        y += separacionOpciones;
        float bottom = Mathf.Min(panelBottom, y - 0.22f);
        float alto = panelTop - bottom;
        panel.fill.size = new Vector2(panelSize.x, alto);
        if (panel.border) panel.border.size = new Vector2(panelSize.x, alto);
        panel.transform.position = new Vector3(panelX, panelTop - alto * 0.5f, panel.transform.position.z);
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
            c.rellenoPropio = false;
            c.color = Color.white;
            c.fillAlpha = 0f;   // solo el borde brillante; el relleno aparece al brillar
        }
    }

    void RestaurarTarjeta(int i)
    {
        var c = opciones[i]; if (!c || originales == null || i >= originales.Length) return;
        var o = originales[i];
        c.transform.position = o.pos; c.color = o.color; c.fillAlpha = o.fillAlpha; c.rellenoPropio = o.rellenoPropio;
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
    string OnElegir(ElevenLabsAgentClient.ToolArgs args) => Elegir(args, validarRespuesta);

    string Elegir(ElevenLabsAgentClient.ToolArgs args, bool validar)
    {
        if (preguntaActual == null) return "No hay ninguna pregunta activa.";
        if (resuelta) return "Esta pregunta ya terminó. Despídete con una frase corta y termina la conversación con end_call.";

        string l = (args.letra ?? "").Trim().ToUpperInvariant();
        int pos = l.Length > 0 ? Array.IndexOf(Letras, l.Substring(0, 1)) : -1;
        if (pos < 0) return "Letra no válida. Pídele con cariño que responda diciendo A, B o C.";
        if (validar)
        {
            string oido = ultimoTranscript;
            ultimoTranscript = null;   // cada respuesta se usa una sola vez
            int dijo = LetraDicha(oido);
            // Con la puerta de ruido activa: si no pasó ninguna voz real, el texto lo inventó el reconocedor a partir del ruido
            if (agent && agent.PuertaActiva && agent.VozUltimoTurno < 2) dijo = -1;   // menos de 0,2 s de voz real
            if (dijo < 0)
            {
                Debug.Log($"[Quiz] Se ignoró la letra {l}: no se escuchó una respuesta clara (\"{oido}\"). Puede ser ruido de fondo.");
                return "NO se registró ninguna respuesta: lo que se escuchó no fue una respuesta clara (probablemente ruido o alguien hablando cerca). " +
                       "No digas si está bien o mal. Pídele con cariño, en una frase corta, que diga su respuesta un poco más fuerte: la A, la B o la C.";
            }
            if (dijo != pos)
            {
                Debug.Log($"[Quiz] Se ignoró la letra {l}: lo que se escuchó (\"{oido}\") apunta a la {Letras[dijo]}.");
                return $"NO se registró ninguna respuesta: lo que se escuchó (\"{oido}\") no coincide con la {l}. " +
                       "No digas si está bien o mal. Pregúntale con cariño cuál eligió: la A, la B o la C.";
            }
            Debug.Log($"[Quiz] Respuesta {l} (se escuchó \"{oido}\").");
        }
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

    // ───────────── ¿Qué alternativa dijo el niño? ─────────────
    static readonly string[][] TokensLetra = { new[] { "a" }, new[] { "b", "be", "ve", "be." }, new[] { "c", "ce", "se" } };
    static readonly string[][] Ordinales = { new[] { "primera", "primero", "uno", "1" }, new[] { "segunda", "segundo", "dos", "2" }, new[] { "tercera", "tercero", "tres", "3" } };
    static readonly HashSet<string> Marcadores = new HashSet<string> { "la", "letra", "opcion", "alternativa", "es", "elijo", "respuesta" };
    static readonly HashSet<string> Vacias = new HashSet<string> { "para", "porque", "como", "cuando", "donde", "pero", "esta", "este", "esto", "eso", "esos", "esas", "unos", "unas", "todo", "todos", "muy", "tiene", "tenia", "habia", "estaba", "sobre", "desde", "hasta", "entre" };

    /// Índice de la alternativa (0 = A) que se escucha en la transcripción, o -1 si no hay una respuesta clara.
    int LetraDicha(string s)
    {
        var w = Palabras(s);
        if (w.Count == 0) return -1;
        var votos = new int[3];
        for (int i = 0; i < w.Count; i++)
        {
            for (int k = 0; k < 3; k++)
            {
                bool letra = Array.IndexOf(TokensLetra[k], w[i]) >= 0;
                // Una letra cuenta si la frase es corta (\"la B\") o viene después de \"la / letra / opción / es\"
                if (letra && (w.Count <= maxPalabrasLetraSuelta || (i > 0 && Marcadores.Contains(w[i - 1])))) votos[k] += 3;
                if (Array.IndexOf(Ordinales[k], w[i]) >= 0 && (w.Count <= maxPalabrasLetraSuelta || (i > 0 && Marcadores.Contains(w[i - 1])))) votos[k] += 3;
            }
        }
        // También vale nombrar la alternativa (\"una boa que se come un elefante\")
        if (preguntaActual != null && preguntaActual.opciones != null)
            for (int k = 0; k < 3 && k < orden.Length; k++)
            {
                var op = Palabras(preguntaActual.opciones[orden[k]]);
                foreach (var x in op) if (x.Length >= 4 && !Vacias.Contains(x) && w.Contains(x)) votos[k] += 1;
            }
        int mejor = -1, max = 0; bool empate = false;
        for (int k = 0; k < 3; k++) { if (votos[k] > max) { max = votos[k]; mejor = k; empate = false; } else if (votos[k] == max && max > 0) empate = true; }
        return empate ? -1 : mejor;
    }

    static List<string> Palabras(string s)
    {
        var r = new List<string>();
        if (string.IsNullOrWhiteSpace(s)) return r;
        var sb = new System.Text.StringBuilder();
        foreach (char ch in s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;   // quita tildes
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        foreach (var x in sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (x != "ah" && x != "eh" && x != "mm" && x != "mmm" && x != "um" && x != "oh" && x != "aa") r.Add(x);   // muletillas / ruido
        return r;
    }

    bool Ultima() => quizActual != null && numeroPregunta >= quizActual.preguntas.Count;

    /// Intro de la primera pregunta: con el nombre del niño si el onboarding lo guardó.
    /// "¡Muy bien! Terminaste el capítulo uno. Ahora, un quiz." →
    /// "¡Muy bien, Camila! Terminaste el capítulo uno. Ahora responde estas preguntas del quiz."
    string IntroInicial(string intro)
    {
        intro = intro ?? "";
        if (!usarNombre || !SesionLector.TieneNombre || string.IsNullOrWhiteSpace(introConNombre)) return intro;
        string medio = intro.Trim();
        if (medio.StartsWith("¡Muy bien!")) medio = medio.Substring("¡Muy bien!".Length).Trim();
        foreach (var fin in new[] { "Ahora, un quiz.", "Ahora, un mini quiz." })
            if (medio.EndsWith(fin)) medio = medio.Substring(0, medio.Length - fin.Length).Trim();
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
        ? "Con este quiz TERMINÓ EL LIBRO: NO lo invites a pasar la página ni a seguir leyendo. Dile que terminó el quiz y también todo el libro, y felicítalo con mucho cariño por haber leído toda la historia. Luego termina la conversación con end_call."
        : "Dile que terminó el quiz e invítalo a pasar la página para seguir leyendo la historia. Luego termina la conversación con end_call.";

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
            if (k.aKey.wasPressedThisFrame) Debug.Log("[Quiz] Prueba A → " + Elegir(new ElevenLabsAgentClient.ToolArgs { letra = "A" }, false));
            if (k.bKey.wasPressedThisFrame) Debug.Log("[Quiz] Prueba B → " + Elegir(new ElevenLabsAgentClient.ToolArgs { letra = "B" }, false));
            if (k.cKey.wasPressedThisFrame) Debug.Log("[Quiz] Prueba C → " + Elegir(new ElevenLabsAgentClient.ToolArgs { letra = "C" }, false));
        }
#else
        if (Input.GetKeyDown(KeyCode.Q)) IniciarPrimero();
        if (Input.GetKeyDown(KeyCode.Alpha1)) ProbarCapitulo1();
        if (Input.GetKeyDown(KeyCode.Alpha2)) ProbarCapitulo2();
        if (IsRunning && (agent == null || !agent.IsRunning))
        {
            if (Input.GetKeyDown(KeyCode.A)) Debug.Log("[Quiz] Prueba A → " + Elegir(new ElevenLabsAgentClient.ToolArgs { letra = "A" }, false));
            if (Input.GetKeyDown(KeyCode.B)) Debug.Log("[Quiz] Prueba B → " + Elegir(new ElevenLabsAgentClient.ToolArgs { letra = "B" }, false));
            if (Input.GetKeyDown(KeyCode.C)) Debug.Log("[Quiz] Prueba C → " + Elegir(new ElevenLabsAgentClient.ToolArgs { letra = "C" }, false));
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
        Hide(subrayado);
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
        yield return estrella;
        yield return subrayado;
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
