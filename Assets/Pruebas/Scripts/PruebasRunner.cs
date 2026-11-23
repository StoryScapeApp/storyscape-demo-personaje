using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

/// <summary>
/// Ejecutor de las pruebas de voz y de los agentes (todas menos la detección de páginas, que tiene PruebaDeteccionPaginas).
/// Lee un plan CSV de Pruebas/planes, corre cada ensayo, registra los eventos con su tiempo
/// (Pruebas/resultados/eventos_*.csv) y una fila de resultado por ensayo (Pruebas/resultados/&lt;modo&gt;_*.csv).
/// Las métricas se calculan con Pruebas/scripts/analizar.py.
///
/// Las voces son sintéticas (Azure, generadas con Pruebas/scripts/generar_audios.py) y se reproducen POR LOS PARLANTES:
/// el sistema las escucha con su micrófono, igual que a un niño.
///
/// Modos:
///  - Fragmentos: un pasaje leído en una página → animaciones activadas (aciertos) y activaciones falsas.
///  - LatenciaAparicion: la frase activadora de una animación de lectura → ms desde el fin de la voz hasta que se ve.
///  - Transcripcion: lectura de una página → texto de Scribe para calcular la tasa de error de palabras (WER).
///  - Alternativas: respuesta a una pregunta del quiz → letra que aceptó el sistema y latencia de turno.
///  - Adelantos / Guardrails: el "niño" le dice algo al Principito del quiz → respuesta del personaje.
///
/// Teclas: N = siguiente ensayo · ESPACIO = terminé de leer (lectura en vivo) · R = repetir el último · S = saltar · P = automático sí/no.
/// No guarda datos de niños: las pruebas se hacen con voces sintéticas o con la voz de los autores.
/// </summary>
[DefaultExecutionOrder(-500)]   // antes que PruebaDeteccionPaginas y el onboarding
public class PruebasRunner : MonoBehaviour
{
    public enum Modo { Fragmentos, LatenciaAparicion, Transcripcion, Alternativas, Adelantos, Guardrails }

    [Header("Prueba")]
    public Modo modo = Modo.Fragmentos;
    [Tooltip("Vacío = Pruebas/planes/<modo>.csv")]
    public string archivoPlan = "";
    [Tooltip("Pasa al siguiente ensayo solo (con audios, simulación o mensajes por texto)")]
    public bool automatico = true;
    [Tooltip("Gratis: inyecta el texto del plan como si fuera Scribe (comprueba el flujo sin voz ni ElevenLabs). Solo lectura")]
    public bool simularSinVoz = false;
    [Tooltip("Empieza en este ensayo (1 = el primero). Sirve para continuar un plan largo otro día")]
    public int empezarEn = 1;

    [Header("Escena (se buscan solas)")]
    public ReadingSession lectura;
    public ScribeRealtimeClient scribe;
    public BookPageTracker tracker;
    public QuizSession quiz;
    public OnboardingSequence onboarding;
    public ArucoUdpReceiver receptor;
    public FondosPorPagina fondos;

    [Header("Audio (por los parlantes)")]
    public AudioSource altavoz;
    public AudioSource ruido;
    [Tooltip("Volumen de las voces de prueba")]
    [Range(0f, 1f)] public float volumenVoz = 1f;
    [Tooltip("Ruido de aula en Pruebas/audios (vacío = sin ruido)")]
    public string archivoRuido = "ruido_aula.wav";
    [Tooltip("Calíbralo una vez: 55–60 dB(A) a la altura del micrófono")]
    [Range(0f, 1f)] public float volumenRuido = 0.35f;

    [Header("Tiempos")]
    [Tooltip("Segundos que se sigue escuchando después de terminar la lectura")]
    public float esperaTrasLectura = 5f;
    public float timeoutRespuesta = 20f;
    [Tooltip("Velocidad de la lectura simulada (palabras por segundo)")]
    public float palabrasPorSegundo = 2.2f;
    [Tooltip("Pausa entre ensayos en modo automático")]
    public float pausaEntreEnsayos = 3f;

    [Header("Quiz y agentes")]
    [Tooltip("Usa agentIdQuizPrueba (copia del agente quiz sin first message) si está en elevenlabs.local.json: ahorra tiempo y minutos")]
    public bool usarAgenteQuizPrueba = true;
    [Tooltip("Si un ensayo no tiene audio (o no existe), envía la frase como texto")]
    public bool respuestaPorTexto = false;
    [Tooltip("En los ensayos por texto no reproduce la voz del agente")]
    public bool silenciarAgenteEnTexto = true;

    [Header("Panel")]
    public bool mostrarPanel = true;
    public int tamanoLetra = 22;

    // ───────────── Estado ─────────────
    static readonly Stopwatch Reloj = Stopwatch.StartNew();
    static double Ms => Reloj.Elapsed.TotalMilliseconds;

    readonly List<Dictionary<string, string>> plan = new List<Dictionary<string, string>>();
    int siguiente;            // índice del próximo ensayo
    int actual = -1;          // índice del ensayo en curso o último
    bool enEnsayo, finManual, saltarPedido, listo;
    string estado = "";
    string archivoResultados, archivoEventos;
    bool cabeceraEscrita;
    readonly Dictionary<string, AudioClip> audios = new Dictionary<string, AudioClip>();
    AudioClip clipRuido;

    // Eventos del ensayo
    readonly List<KeyValuePair<double, string>> activaciones = new List<KeyValuePair<double, string>>();
    readonly Dictionary<string, double> visibles = new Dictionary<string, double>();
    readonly List<KeyValuePair<double, string>> textos = new List<KeyValuePair<double, string>>();   // (t, texto) de Scribe
    readonly List<string> finales = new List<string>();
    string ultimoParcial = "";
    readonly List<KeyValuePair<double, string>> respuestas = new List<KeyValuePair<double, string>>();
    readonly List<double> vozInicios = new List<double>();
    readonly List<string> intentadas = new List<string>();
    string letraElegida, transcripcionAgente;
    bool correctaEnQuiz;
    double tLetra = -1;
    bool hablandoAntes;
    ElevenLabsAgentClient agenteActivo;

    // Valores originales (se restauran al salir)
    bool usarScribeOriginal, mezclarOriginal, guardarOriginal, validarOriginal, startOnPlayOriginal;
    string claveQuizOriginal;
    readonly List<(UnityEventBase ev, int i, UnityEventCallState antes)> eventosApagados = new List<(UnityEventBase, int, UnityEventCallState)>();

    static string Base => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Pruebas");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    bool EsLectura => modo == Modo.Fragmentos || modo == Modo.LatenciaAparicion || modo == Modo.Transcripcion;
    bool EsAgente => modo == Modo.Adelantos || modo == Modo.Guardrails;

    // ───────────── Inicio ─────────────
    void Awake()
    {
        if (!lectura) lectura = FindFirstObjectByType<ReadingSession>(FindObjectsInactive.Include);
        if (!scribe) scribe = lectura && lectura.scribe ? lectura.scribe : FindFirstObjectByType<ScribeRealtimeClient>(FindObjectsInactive.Include);
        if (!tracker) tracker = FindFirstObjectByType<BookPageTracker>(FindObjectsInactive.Include);
        if (!quiz) quiz = FindFirstObjectByType<QuizSession>(FindObjectsInactive.Include);
        if (!onboarding) onboarding = FindFirstObjectByType<OnboardingSequence>(FindObjectsInactive.Include);
        if (!receptor) receptor = FindFirstObjectByType<ArucoUdpReceiver>(FindObjectsInactive.Include);
        if (!fondos) fondos = FindFirstObjectByType<FondosPorPagina>(FindObjectsInactive.Include);
        if (!altavoz) altavoz = gameObject.AddComponent<AudioSource>();
        if (!ruido) ruido = gameObject.AddComponent<AudioSource>();
        altavoz.playOnAwake = ruido.playOnAwake = false;
        altavoz.spatialBlend = ruido.spatialBlend = 0f;

        // Las dos pruebas no pueden correr a la vez
        var deteccion = FindFirstObjectByType<PruebaDeteccionPaginas>(FindObjectsInactive.Exclude);
        if (deteccion && deteccion.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("[Pruebas] PruebaDeteccion estaba activo: lo desactivo mientras corren estas pruebas.");
            deteccion.gameObject.SetActive(false);
        }

        // El onboarding no debe empezar (ni solo ni al ver el ArUco de la página 1)
        if (onboarding)
        {
            startOnPlayOriginal = onboarding.startOnPlay;
            onboarding.startOnPlay = false;
        }
        if (receptor && receptor.actions != null)
            foreach (var a in receptor.actions) if (a != null) Apagar(a.onDetected);

        // Las teclas de respaldo de los comandos de voz (P, R, N…) chocan con las de la prueba
        comandos = FindFirstObjectByType<VoiceCommands>(FindObjectsInactive.Include);
        if (comandos) { teclasComandosOriginal = comandos.teclasRespaldo; comandos.teclasRespaldo = false; }
    }

    VoiceCommands comandos;
    bool teclasComandosOriginal;

    void OnEnable()
    {
        if (lectura) lectura.AnimacionActivada += OnAnimacion;
        if (scribe) scribe.TextReceived += OnScribe;
        if (quiz) { quiz.OpcionElegida += OnOpcion; quiz.OpcionIntentada += OnIntento; }
        if (quiz && quiz.agent) { quiz.agent.UserTranscript += OnTranscripcionAgente; quiz.agent.AgentResponse += OnRespuesta; }
    }

    void OnDisable()
    {
        if (lectura) lectura.AnimacionActivada -= OnAnimacion;
        if (scribe) scribe.TextReceived -= OnScribe;
        if (quiz) { quiz.OpcionElegida -= OnOpcion; quiz.OpcionIntentada -= OnIntento; }
        if (quiz && quiz.agent) { quiz.agent.UserTranscript -= OnTranscripcionAgente; quiz.agent.AgentResponse -= OnRespuesta; }
        Restaurar();
    }

    IEnumerator Start()
    {
        yield return null;   // después del Start de los demás
        PrepararEscena();
        if (!CargarPlan()) yield break;
        siguiente = Mathf.Clamp(empezarEn - 1, 0, plan.Count - 1);
        if (!string.IsNullOrEmpty(archivoRuido) && volumenRuido > 0f)
            yield return CargarAudio(archivoRuido, c => clipRuido = c);
        string sello = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(Path.Combine(Base, "resultados"));
        string nombre = NombreModo();
        archivoResultados = Path.Combine(Base, "resultados", $"{nombre}_{sello}.csv");
        archivoEventos = Path.Combine(Base, "resultados", $"eventos_{nombre}_{sello}.csv");
        File.WriteAllText(archivoEventos, "t_ms;hora;ensayo;evento;detalle\n", new UTF8Encoding(true));
        int faltan = ContarAudiosFaltantes();
        estado = $"Listo: {plan.Count} ensayos" + (siguiente > 0 ? $" (empieza en el {siguiente + 1})" : "") + ". Presiona N para empezar" +
                 (automatico ? " (luego sigue solo)." : ".") +
                 (faltan > 0 ? $"\n¡Faltan {faltan} audios en Pruebas/audios! Ejecuta generar_audios.py." : "");
        listo = true;
        Debug.Log($"[Pruebas] {modo}: {plan.Count} ensayos. Resultados en {archivoResultados}" + (faltan > 0 ? $" · faltan {faltan} audios" : ""));
    }

    string NombreModo() => modo switch
    {
        Modo.Fragmentos => "fragmentos",
        Modo.LatenciaAparicion => "latencia_aparicion",
        Modo.Transcripcion => "transcripcion",
        Modo.Alternativas => "alternativas",
        Modo.Adelantos => "adelantos",
        _ => "guardrails",
    };

    /// Deja la mesa limpia: sin onboarding, con el fondo de la página y la lectura o el quiz listos para la prueba.
    void PrepararEscena()
    {
        if (onboarding && onboarding.IsRunning) onboarding.Cancelar();
        var vc = VoiceCommands.Instancia ? VoiceCommands.Instancia : FindFirstObjectByType<VoiceCommands>(FindObjectsInactive.Include);
        if (vc && vc != comandos) { comandos = vc; teclasComandosOriginal = vc.teclasRespaldo; }
        if (comandos) comandos.teclasRespaldo = false;
        if (fondos && fondos.fondoBase) { fondos.fondoBase.ResetState(); fondos.fondoBase.Show(); }

        if (lectura) usarScribeOriginal = lectura.usarScribe;
        if (quiz)
        {
            mezclarOriginal = quiz.mezclarOpciones; guardarOriginal = quiz.guardarResultados; validarOriginal = quiz.validarRespuesta;
            if (quiz.agent) claveQuizOriginal = quiz.agent.configKey;
        }

        if (lectura)
        {
            lectura.modoPrueba = true;
            if (EsLectura)
            {
                if (simularSinVoz) lectura.usarScribe = false;   // simulación: sin costo
                lectura.Iniciar();
            }
            else lectura.Detener();
        }
        if (quiz)
        {
            quiz.mezclarOpciones = false;   // la letra esperada del plan corresponde al orden original
            quiz.guardarResultados = false; // no mezcla las pruebas con resultados_quiz.csv
            bool conQuiz = modo == Modo.Alternativas || EsAgente;
            if (conQuiz && usarAgenteQuizPrueba && quiz.agent && TieneClave("agentIdQuizPrueba"))
            {
                quiz.agent.configKey = "agentIdQuizPrueba";
                Debug.Log("[Pruebas] Uso el agente agentIdQuizPrueba (sin first message).");
            }
        }
    }

    void Restaurar()
    {
        if (lectura) { lectura.modoPrueba = false; lectura.usarScribe = usarScribeOriginal; }
        if (quiz)
        {
            quiz.mezclarOpciones = mezclarOriginal; quiz.guardarResultados = guardarOriginal; quiz.validarRespuesta = validarOriginal;
            if (quiz.agent)
            {
                if (!string.IsNullOrEmpty(claveQuizOriginal)) quiz.agent.configKey = claveQuizOriginal;
                quiz.agent.silenciarMic = false;
            }
        }
        if (onboarding) onboarding.startOnPlay = startOnPlayOriginal;
        if (comandos) comandos.teclasRespaldo = teclasComandosOriginal;
        foreach (var e in eventosApagados) e.ev.SetPersistentListenerState(e.i, e.antes);
        eventosApagados.Clear();
    }

    void Apagar(UnityEventBase ev)
    {
        if (ev == null) return;
        for (int i = 0; i < ev.GetPersistentEventCount(); i++)
        {
            eventosApagados.Add((ev, i, ev.GetPersistentListenerState(i)));
            ev.SetPersistentListenerState(i, UnityEventCallState.Off);
        }
    }

    // ───────────── Plan ─────────────
    bool CargarPlan()
    {
        string ruta = string.IsNullOrEmpty(archivoPlan) ? Path.Combine(Base, "planes", NombreModo() + ".csv")
                    : Path.IsPathRooted(archivoPlan) ? archivoPlan : Path.Combine(Base, "planes", archivoPlan);
        if (!File.Exists(ruta)) { estado = "No encuentro el plan: " + ruta + "\nEjecuta Pruebas/scripts/generar_planes.py."; Debug.LogError("[Pruebas] " + estado); return false; }
        var filas = LeerCsv(File.ReadAllText(ruta, Encoding.UTF8));
        if (filas.Count < 2) { estado = "El plan está vacío: " + ruta; return false; }
        var cab = filas[0];
        for (int i = 1; i < filas.Count; i++)
        {
            if (filas[i].Count == 0 || filas[i].TrueForAll(string.IsNullOrWhiteSpace)) continue;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < cab.Count; j++) d[cab[j].Trim()] = j < filas[i].Count ? filas[i][j].Trim() : "";
            plan.Add(d);
        }
        return plan.Count > 0;
    }

    int ContarAudiosFaltantes()
    {
        int n = 0;
        foreach (var f in plan)
        {
            string a = Col(f, "audio");
            if (a.Length > 0 && !File.Exists(Path.IsPathRooted(a) ? a : Path.Combine(Base, "audios", a))) n++;
        }
        return n;
    }

    static string Col(Dictionary<string, string> f, string k) => f != null && f.TryGetValue(k, out var v) ? v : "";
    static int ColInt(Dictionary<string, string> f, string k, int def) => int.TryParse(Col(f, k), out int v) ? v : def;

    // ───────────── Control ─────────────
    void Update()
    {
        if (!listo) return;
        if (Tecla("n") && !enEnsayo) Lanzar(siguiente);
        if (Tecla("r") && !enEnsayo && actual >= 0) Lanzar(actual);
        if (Tecla("s")) { if (enEnsayo) saltarPedido = true; else if (siguiente < plan.Count) { siguiente++; estado = $"Saltado. Siguiente: ensayo {siguiente + 1}."; } }
        if (Tecla("space") && enEnsayo) finManual = true;
        if (Tecla("p")) { automatico = !automatico; estado = automatico ? "Automático activado." : "Automático desactivado (N para el siguiente)."; }

        // Voz del agente activo (inicio de habla = latencia de turno)
        if (agenteActivo)
        {
            bool hablando = agenteActivo.IsAgentTalking;
            if (hablando && !hablandoAntes) { vozInicios.Add(Ms); Evento("agente_voz_inicio", ""); }
            if (!hablando && hablandoAntes) Evento("agente_voz_fin", "");
            hablandoAntes = hablando;
        }
    }

    void Lanzar(int i)
    {
        if (i >= plan.Count) { estado = "Plan terminado. Ejecuta Pruebas/scripts/analizar.py para ver las métricas."; return; }
        actual = i;
        siguiente = i + 1;
        StartCoroutine(Ensayo(plan[i]));
    }

    IEnumerator Ensayo(Dictionary<string, string> fila)
    {
        enEnsayo = true;
        finManual = saltarPedido = false;
        Limpiar();
        Evento("ensayo_inicio", Col(fila, "id"));
        IEnumerator co = EsLectura ? EnsayoLectura(fila) : modo == Modo.Alternativas ? EnsayoAlternativa(fila) : EnsayoAgente(fila);
        yield return co;
        Evento("ensayo_fin", Col(fila, "id"));
        agenteActivo = null;
        enEnsayo = false;

        if (siguiente >= plan.Count) { estado = $"Plan terminado ({plan.Count} ensayos). Ejecuta Pruebas/scripts/analizar.py."; Debug.Log("[Pruebas] " + estado); yield break; }
        if (automatico && PuedeAutomatico())
        {
            estado += $"  ·  Siguiente en {pausaEntreEnsayos:0} s (P para pausar).";
            float t = 0f;
            while (t < pausaEntreEnsayos) { if (!automatico) yield break; t += Time.unscaledDeltaTime; yield return null; }
            if (automatico && !enEnsayo) Lanzar(siguiente);
        }
    }

    /// En lectura en vivo (sin audio) el automático espera a que el lector presione N.
    bool PuedeAutomatico()
    {
        if (simularSinVoz || respuestaPorTexto) return true;
        return siguiente < plan.Count && !string.IsNullOrEmpty(Col(plan[siguiente], "audio"));
    }

    void Limpiar()
    {
        activaciones.Clear(); visibles.Clear(); textos.Clear(); finales.Clear(); respuestas.Clear(); vozInicios.Clear(); intentadas.Clear();
        ultimoParcial = ""; letraElegida = null; transcripcionAgente = ""; correctaEnQuiz = false; tLetra = -1;
    }

    /// Esconde lo que proyectó el ensayo anterior (personajes, serpiente, avión) para empezar limpio.
    void OcultarAnimaciones()
    {
        foreach (var p in FindObjectsByType<PersonajeEscena>(FindObjectsSortMode.None)) if (p.Visible) p.Ocultar();
        foreach (var s in FindObjectsByType<SerpienteRecorrido>(FindObjectsSortMode.None)) if (s.Visible) s.Ocultar();
        foreach (var a in FindObjectsByType<AvionVueltaMundo>(FindObjectsSortMode.None)) if (a.Visible) a.Ocultar();
    }

    // ───────────── Lectura: fragmentos, latencia y transcripción ─────────────
    IEnumerator EnsayoLectura(Dictionary<string, string> fila)
    {
        int pagina = ColInt(fila, "pagina", 1);
        string audio = Col(fila, "audio");
        string texto = Col(fila, "texto");
        OcultarAnimaciones();
        if (tracker) tracker.SetPage(pagina);
        lectura.ReiniciarAnimaciones();
        agenteActivo = null;
        yield return new WaitForSecondsRealtime(1.0f);   // se acomoda el fondo de la página y desaparece lo anterior
        lectura.LimpiarEscuchado();

        // Espera a que Scribe esté conectado
        if (!simularSinVoz && scribe && lectura.usarScribe)
        {
            estado = "Conectando Scribe…";
            for (float t = 0f; t < 12f && !scribe.IsRunning; t += Time.unscaledDeltaTime) yield return null;
            if (!scribe.IsRunning) { estado = "Scribe no se conectó (revisa apiKey). Ensayo no válido."; Fila(fila, "error", -1, -1, "scribe_desconectado"); yield break; }
            yield return new WaitForSecondsRealtime(0.8f);
        }

        IniciarRuido();
        double tInicio = Ms, tFin;
        string entrada;
        Evento("lectura_inicio", $"pagina {pagina}");

        AudioClip clip = null;
        if (!string.IsNullOrEmpty(audio) && !simularSinVoz) yield return CargarAudio(audio, c => clip = c);
        if (clip)
        {
            entrada = "audio";
            estado = $"Reproduciendo {audio}…";
            yield return Reproducir(clip, (a, b) => { tInicio = a; }, () => saltarPedido);
            tFin = ultimoFinVoz;
        }
        else if (simularSinVoz || (!string.IsNullOrEmpty(audio) && respuestaPorTexto))
        {
            entrada = "simulado";
            estado = "Lectura simulada…";
            yield return SimularLectura(string.IsNullOrEmpty(texto) && tracker ? tracker.GetTextoPagina(pagina) : texto);
            tFin = Ms;
        }
        else if (!string.IsNullOrEmpty(audio))
        {
            estado = "No encuentro el audio " + audio + " (ejecuta generar_audios.py).";
            DetenerRuido(); Fila(fila, "audio", -1, -1, "audio_no_encontrado"); yield break;
        }
        else
        {
            entrada = "en_vivo";
            estado = $"LEE AHORA · página {pagina}" + (string.IsNullOrEmpty(Col(fila, "version")) ? "" : $" · versión {Col(fila, "version")}") +
                     "\nPresiona ESPACIO apenas termines.";
            while (!finManual && !saltarPedido) yield return null;
            tFin = Ms;
        }
        Evento("lectura_fin", entrada);
        if (saltarPedido) { altavoz.Stop(); DetenerRuido(); Fila(fila, entrada, tInicio, tFin, "saltado"); yield break; }

        estado = $"Escuchando {esperaTrasLectura:0} s más…";
        yield return new WaitForSecondsRealtime(esperaTrasLectura);
        DetenerRuido();
        Fila(fila, entrada, tInicio, tFin, "ok");
    }

    IEnumerator SimularLectura(string texto)
    {
        var parcial = new StringBuilder();
        foreach (var palabra in (texto ?? "").Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (saltarPedido) yield break;
            if (palabra == "…") { yield return new WaitForSecondsRealtime(1.2f); continue; }
            parcial.Append(palabra.Replace("…", "")).Append(' ');
            bool fin = palabra.EndsWith(".") || palabra.EndsWith("?") || palabra.EndsWith("!");
            string t = parcial.ToString().Trim();
            OnScribe(t, fin);             // registra como evento de transcripción
            lectura.InyectarTexto(t, fin);
            if (fin) parcial.Clear();
            yield return new WaitForSecondsRealtime(1f / Mathf.Max(0.5f, palabrasPorSegundo));
        }
        if (parcial.Length > 0) { string t = parcial.ToString().Trim(); OnScribe(t, true); lectura.InyectarTexto(t, true); }
    }

    // ───────────── Quiz: identificación de la alternativa y latencia de turno ─────────────
    IEnumerator EnsayoAlternativa(Dictionary<string, string> fila)
    {
        var p = quiz ? quiz.BuscarPregunta(Col(fila, "quiz"), Col(fila, "pregunta")) : null;
        if (p == null) { estado = $"No existe la pregunta {Col(fila, "quiz")} {Col(fila, "pregunta")}."; Fila(fila, "error", -1, -1, "pregunta_no_encontrada"); yield break; }
        var ag = quiz.agent;
        yield return AbrirPregunta(fila, p, ag);
        if (!ag.LastConnected) { estado = "El agente del quiz no se conectó."; ag.EndConversation(); Fila(fila, "error", -1, -1, "agente_desconectado"); yield return EsperarQuiz(); yield break; }

        IniciarRuido();
        string respuesta = Col(fila, "texto");
        double tInicio = -1, tFin = -1;
        string entrada = "";
        int vocesAntes = vozInicios.Count, respuestasAntes = respuestas.Count;
        yield return DecirAlAgente(fila, ag, respuesta, (e, a, b) => { entrada = e; tInicio = a; tFin = b; });
        if (entrada == "error") { ag.EndConversation(); DetenerRuido(); Fila(fila, "audio", -1, -1, "audio_no_encontrado"); yield return EsperarQuiz(); yield break; }
        vozInicios.RemoveRange(0, Math.Min(vocesAntes, vozInicios.Count));
        respuestas.RemoveRange(0, Math.Min(respuestasAntes, respuestas.Count));   // sin la lectura de la pregunta

        estado = "Esperando al Principito…";
        for (float t = 0f; t < timeoutRespuesta && ag.IsRunning && vozInicios.Count == 0 && respuestas.Count == 0; t += Time.unscaledDeltaTime) yield return null;
        yield return new WaitForSecondsRealtime(0.5f);
        yield return EsperarSilencio(ag, 1.2f, timeoutRespuesta);
        DetenerRuido();
        if (ag.IsRunning) ag.EndConversation();
        yield return EsperarQuiz();
        quiz.validarRespuesta = validarOriginal;
        Fila(fila, entrada, tInicio, tFin, "ok");
    }

    /// Abre una conversación del quiz con esa pregunta y espera a que el Principito termine de leerla.
    IEnumerator AbrirPregunta(Dictionary<string, string> fila, QuizSession.Pregunta p, ElevenLabsAgentClient ag)
    {
        agenteActivo = ag;
        hablandoAntes = false;
        OcultarAnimaciones();
        int pagina = ColInt(fila, "pagina", 0);
        if (tracker && pagina > 0) tracker.SetPage(pagina);   // el fondo de la página donde ocurre ese quiz
        bool conFirstMessage = ag.configKey != "agentIdQuizPrueba";

        estado = $"Conectando el agente del quiz ({Col(fila, "quiz")} {Col(fila, "pregunta")})…";
        quiz.IniciarPreguntaUnica(p);
        for (float t = 0f; t < 15f && !(ag.IsRunning && ag.LastConnected); t += Time.unscaledDeltaTime) yield return null;
        if (!ag.LastConnected) yield break;

        if (conFirstMessage)
        {
            estado = "El Principito lee la pregunta…";
            for (float t = 0f; t < 15f && vozInicios.Count == 0 && respuestas.Count == 0; t += Time.unscaledDeltaTime) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return EsperarSilencio(ag, 1.0f, 25f);
        }
        else yield return new WaitForSecondsRealtime(1.2f);
        yield return new WaitForSecondsRealtime(0.4f);   // termina la cola de silencio del micrófono
    }

    /// El "niño" habla: audio por los parlantes (lo escucha el micrófono), texto o en vivo.
    IEnumerator DecirAlAgente(Dictionary<string, string> fila, ElevenLabsAgentClient ag, string frase, Action<string, double, double> fin)
    {
        string audio = Col(fila, "audio");
        AudioClip clip = null;
        if (!string.IsNullOrEmpty(audio)) yield return CargarAudio(audio, c => clip = c);
        if (clip)
        {
            estado = $"Audio: \"{frase}\"";
            double a = -1;
            yield return Reproducir(clip, (x, y) => a = x, null);
            Evento("respuesta_fin", "audio");
            fin("audio", a, ultimoFinVoz);
        }
        else if (respuestaPorTexto || string.IsNullOrEmpty(audio))
        {
            if (!respuestaPorTexto && string.IsNullOrEmpty(audio))
            {
                estado = $"DI: \"{frase}\"  y presiona ESPACIO al terminar.";
                double a = Ms;
                while (!finManual) yield return null;
                Evento("respuesta_fin", "en_vivo");
                fin("en_vivo", a, Ms);
                yield break;
            }
            estado = $"Texto: \"{frase}\"";
            quiz.validarRespuesta = false;   // por texto no hay transcripción que validar
            bool muteAntes = ag.muteOutput;
            ag.silenciarMic = true;
            if (silenciarAgenteEnTexto) ag.muteOutput = true;
            double t = Ms;
            ag.SendUserMessage(frase);
            Evento("mensaje_enviado", frase);
            fin("texto", t, t);
            // se restaura al terminar el ensayo
            StartCoroutine(RestaurarAgenteAlTerminar(ag, muteAntes));
        }
        else
        {
            estado = "No encuentro el audio " + audio + " (ejecuta generar_audios.py).";
            fin("error", -1, -1);
        }
    }

    IEnumerator RestaurarAgenteAlTerminar(ElevenLabsAgentClient ag, bool muteAntes)
    {
        while (enEnsayo) yield return null;
        ag.silenciarMic = false;
        ag.muteOutput = muteAntes;
    }

    IEnumerator EsperarQuiz()
    {
        for (float t = 0f; t < 10f && quiz.IsRunning; t += Time.unscaledDeltaTime) yield return null;
    }

    /// Espera a que el agente deje de hablar durante "silencio" segundos (o se cierre la conversación).
    static IEnumerator EsperarSilencio(ElevenLabsAgentClient ag, float silencio, float maximo)
    {
        float callado = 0f;
        for (float t = 0f; t < maximo && ag.IsRunning; t += Time.unscaledDeltaTime)
        {
            callado = ag.IsAgentTalking ? 0f : callado + Time.unscaledDeltaTime;
            if (callado >= silencio) yield break;
            yield return null;
        }
    }

    // ───────────── Adelantos y guardrails ─────────────
    /// Abre una pregunta del quiz (el momento en que el niño conversa con el Principito), el "niño" dice la frase
    /// del plan y se guarda lo que responde el personaje.
    IEnumerator EnsayoAgente(Dictionary<string, string> fila)
    {
        var ag = quiz ? quiz.agent : null;
        if (!ag) { Fila(fila, "error", -1, -1, "sin_agente"); yield break; }
        string qid = Col(fila, "quiz"), clave = Col(fila, "pregunta");
        var p = quiz.BuscarPregunta(string.IsNullOrEmpty(qid) ? "pagina_1" : qid, string.IsNullOrEmpty(clave) ? "1" : clave);
        if (p == null) { Fila(fila, "error", -1, -1, "pregunta_no_encontrada"); yield break; }

        yield return AbrirPregunta(fila, p, ag);
        if (!ag.LastConnected) { estado = "El agente no se conectó."; ag.EndConversation(); Fila(fila, "error", -1, -1, "agente_desconectado"); yield return EsperarQuiz(); yield break; }
        respuestas.Clear();
        vozInicios.Clear();
        intentadas.Clear();

        IniciarRuido();
        string mensaje = Col(fila, "texto");
        double tInicio = -1, tFin = -1;
        string entrada = "";
        yield return DecirAlAgente(fila, ag, mensaje, (e, a, b) => { entrada = e; tInicio = a; tFin = b; });
        if (entrada == "error") { ag.EndConversation(); DetenerRuido(); Fila(fila, "audio", -1, -1, "audio_no_encontrado"); yield return EsperarQuiz(); yield break; }

        estado = "Esperando al Principito…";
        for (float t = 0f; t < timeoutRespuesta && ag.IsRunning && respuestas.Count == 0; t += Time.unscaledDeltaTime) yield return null;
        // Junta las respuestas hasta 2,5 s sin nada nuevo y con el personaje callado
        int n = -1; float quieto = 0f;
        for (float t = 0f; t < timeoutRespuesta && ag.IsRunning; t += Time.unscaledDeltaTime)
        {
            if (respuestas.Count != n) { n = respuestas.Count; quieto = 0f; }
            else quieto += Time.unscaledDeltaTime;
            if (quieto >= 2.5f && !ag.IsAgentTalking) break;
            yield return null;
        }
        DetenerRuido();
        if (ag.IsRunning) ag.EndConversation();
        yield return EsperarQuiz();
        quiz.validarRespuesta = validarOriginal;
        Fila(fila, entrada, tInicio, tFin, "ok");
    }

    // ───────────── Eventos ─────────────
    void OnAnimacion(string id, string origen)
    {
        if (!enEnsayo) return;
        activaciones.Add(new KeyValuePair<double, string>(Ms, id));
        Evento("animacion", id + " · " + origen);
        StartCoroutine(MarcarVisible(id));
    }

    /// La animación se considera visible al terminar de dibujarse el cuadro en que se activó.
    IEnumerator MarcarVisible(string id)
    {
        yield return new WaitForEndOfFrame();
        if (!visibles.ContainsKey(id)) { visibles[id] = Ms; Evento("animacion_visible", id); }
    }

    void OnScribe(string texto, bool final)
    {
        if (!enEnsayo) return;
        textos.Add(new KeyValuePair<double, string>(Ms, texto));
        if (final) { finales.Add(texto); ultimoParcial = ""; } else ultimoParcial = texto;
        Evento(final ? "scribe_final" : "scribe_parcial", texto);
    }

    void OnOpcion(string letra, bool correcta)
    {
        if (!enEnsayo || letraElegida != null) return;
        letraElegida = letra; correctaEnQuiz = correcta; tLetra = Ms;
        Evento("elegir_opcion", letra + (correcta ? " (correcta)" : " (incorrecta)"));
    }

    void OnIntento(string letra)
    {
        if (!enEnsayo) return;
        intentadas.Add(letra);
        Evento("elegir_opcion_llamada", letra);
    }

    void OnTranscripcionAgente(string t) { if (enEnsayo) { transcripcionAgente += (transcripcionAgente.Length > 0 ? " | " : "") + t; Evento("agente_escucho", t); } }
    void OnRespuesta(string t) { if (enEnsayo) { respuestas.Add(new KeyValuePair<double, string>(Ms, t)); Evento("agente_respondio", t); } }

    // ───────────── Resultados ─────────────
    void Fila(Dictionary<string, string> fila, string entrada, double tInicio, double tFin, string estadoEnsayo)
    {
        var c = new List<string> { (actual + 1).ToString(), Col(fila, "id"), estadoEnsayo, entrada };
        string cab;
        string Seg(double a, double b, string f) => a >= 0 && b >= 0 ? ((a - b) / 1000.0).ToString(f, Inv) : "";
        switch (modo)
        {
            case Modo.Fragmentos:
            case Modo.LatenciaAparicion:
            {
                var esperados = Lista(Col(fila, "esperado"));
                var activadas = new List<string>();
                foreach (var a in activaciones) if (!activadas.Contains(a.Value)) activadas.Add(a.Value);
                int aciertos = esperados.FindAll(activadas.Contains).Count;
                var falsas = activadas.FindAll(a => !esperados.Contains(a));
                var lat = new List<string>();
                var latTexto = new List<string>();
                foreach (var e in esperados)
                {
                    if (visibles.TryGetValue(e, out double tv) && tFin >= 0) lat.Add(e + ":" + (tv - tFin).ToString("0", Inv));
                    double tw = TiempoUltimaPalabra(e);
                    if (tw >= 0 && tFin >= 0) latTexto.Add(e + ":" + (tw - tFin).ToString("0", Inv));
                }
                cab = "ensayo;id;estado;entrada;tipo;pagina;version;voz;esperado;activadas;aciertos;omitidas;falsas;duracion_lectura_s;latencia_aparicion_ms;latencia_transcripcion_ms;transcripcion";
                c.AddRange(new[] { Col(fila, "tipo"), Col(fila, "pagina"), Col(fila, "version"), Col(fila, "voz"),
                    string.Join("|", esperados), string.Join("|", activadas), aciertos.ToString(), (esperados.Count - aciertos).ToString(),
                    string.Join("|", falsas), Seg(tFin, tInicio, "0.0"), string.Join("|", lat), string.Join("|", latTexto), Transcripcion() });
                break;
            }
            case Modo.Transcripcion:
            {
                int pagina = ColInt(fila, "pagina", 1);
                string referencia = string.IsNullOrEmpty(Col(fila, "referencia")) && tracker ? tracker.GetTextoPagina(pagina) : Col(fila, "referencia");
                cab = "ensayo;id;estado;entrada;pagina;lector;duracion_lectura_s;referencia;hipotesis";
                c.AddRange(new[] { pagina.ToString(), Col(fila, "lector"), Seg(tFin, tInicio, "0.0"), referencia, Transcripcion() });
                break;
            }
            case Modo.Alternativas:
            {
                string elegida = letraElegida ?? "ninguna";
                string esperado = Col(fila, "esperado");
                bool acierto = string.Equals(elegida, esperado, StringComparison.OrdinalIgnoreCase);
                double tVoz = vozInicios.Count > 0 ? vozInicios[0] : -1;
                cab = "ensayo;id;estado;entrada;quiz;pregunta;forma;voz;respuesta_dicha;esperado;elegida;acierto;correcta_en_quiz;llamadas_elegir_opcion;latencia_turno_s;latencia_herramienta_s;transcripcion_agente;respuesta_personaje";
                c.AddRange(new[] { Col(fila, "quiz"), Col(fila, "pregunta"), Col(fila, "forma"), Col(fila, "voz"), Col(fila, "texto"), esperado, elegida,
                    acierto ? "1" : "0", letraElegida == null ? "" : (correctaEnQuiz ? "1" : "0"), string.Join("|", intentadas),
                    Seg(tVoz, tFin, "0.000"), Seg(tLetra, tFin, "0.000"), transcripcionAgente, Respuestas() });
                break;
            }
            default:
            {
                double tResp = respuestas.Count > 0 ? respuestas[0].Key : -1;
                double tVoz = vozInicios.Count > 0 ? vozInicios[0] : -1;
                cab = "ensayo;id;estado;entrada;quiz;pregunta;pagina;categoria;voz;mensaje;transcripcion_agente;respuesta;eligio_opcion;latencia_respuesta_s;latencia_voz_s;esperado;evaluacion_manual";
                c.AddRange(new[] { Col(fila, "quiz"), Col(fila, "pregunta"), Col(fila, "pagina"), Col(fila, "categoria"), Col(fila, "voz"),
                    Col(fila, "texto"), transcripcionAgente, Respuestas(), string.Join("|", intentadas),
                    Seg(tResp, tFin, "0.00"), Seg(tVoz, tFin, "0.00"), Col(fila, "esperado"), "" });
                break;
            }
        }
        var sb = new StringBuilder();
        if (!cabeceraEscrita) sb.Append(cab).Append('\n');
        sb.Append(string.Join(";", c.ConvertAll(Csv))).Append('\n');
        if (!cabeceraEscrita) { File.WriteAllText(archivoResultados, sb.ToString(), new UTF8Encoding(true)); cabeceraEscrita = true; }
        else File.AppendAllText(archivoResultados, sb.ToString(), new UTF8Encoding(false));
        estado = $"Ensayo {actual + 1}/{plan.Count} ({Col(fila, "id")}) guardado: {Resumen(fila)}";
        Debug.Log("[Pruebas] " + estado);
    }

    string Resumen(Dictionary<string, string> fila)
    {
        switch (modo)
        {
            case Modo.Fragmentos:
            case Modo.LatenciaAparicion:
                var act = new List<string>(); foreach (var a in activaciones) if (!act.Contains(a.Value)) act.Add(a.Value);
                return (act.Count == 0 ? "ninguna animación" : "activó " + string.Join(", ", act)) + $" (esperado: {Col(fila, "esperado")})";
            case Modo.Transcripcion: return Transcripcion().Length + " caracteres transcritos";
            case Modo.Alternativas: return $"eligió {letraElegida ?? "ninguna"} (esperado {Col(fila, "esperado")})";
            default: return respuestas.Count == 0 ? "sin respuesta" : "«" + Respuestas() + "»";
        }
    }

    string Transcripcion()
    {
        var sb = new StringBuilder();
        foreach (var f in finales) sb.Append(f).Append(' ');
        sb.Append(ultimoParcial);
        return sb.ToString().Trim();
    }

    string Respuestas()
    {
        var sb = new StringBuilder();
        foreach (var r in respuestas) sb.Append(r.Value).Append(' ');
        return sb.ToString().Trim();
    }

    /// Momento en que la última palabra de la frase de esa animación aparece por primera vez en la transcripción.
    double TiempoUltimaPalabra(string id)
    {
        var a = lectura.animaciones.Find(x => x.id == id);
        if (a == null || string.IsNullOrEmpty(a.frase)) return -1;
        string ultima = null;
        foreach (var w in Palabras(a.frase.Split('|')[0])) if (w.Length >= 3) ultima = w;
        if (ultima == null) return -1;
        foreach (var t in textos) if (Palabras(t.Value).Contains(ultima)) return t.Key;
        return -1;
    }

    static List<string> Lista(string s)
    {
        var l = new List<string>();
        foreach (var p in (s ?? "").Split('|')) { var t = p.Trim(); if (t.Length > 0 && t != "ninguno" && t != "ninguna") l.Add(t); }
        return l;
    }

    static List<string> Palabras(string texto)
    {
        var l = new List<string>(); var sb = new StringBuilder();
        foreach (char ch in (texto ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0) { l.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) l.Add(sb.ToString());
        return l;
    }

    void Evento(string tipo, string detalle)
    {
        if (string.IsNullOrEmpty(archivoEventos)) return;
        string fila = string.Join(";", new[] { Ms.ToString("0.0", Inv), DateTime.Now.ToString("HH:mm:ss.fff"),
            actual >= 0 && actual < plan.Count ? Col(plan[actual], "id") : "", tipo, Csv(detalle) });
        try { File.AppendAllText(archivoEventos, fila + "\n", new UTF8Encoding(false)); } catch { }
    }

    // ───────────── Audio ─────────────
    double ultimoFinVoz;

    /// Reproduce un audio por los parlantes. inicio(tInicioVoz, tFinVoz) se llama al empezar; deja el fin de la voz en ultimoFinVoz.
    IEnumerator Reproducir(AudioClip clip, Action<double, double> inicio, Func<bool> cortar)
    {
        altavoz.clip = clip;
        altavoz.volume = volumenVoz;
        altavoz.Play();
        double tPlay = Ms;
        double a = tPlay + InicioDeVoz(clip) * 1000.0;
        ultimoFinVoz = tPlay + FinDeVoz(clip) * 1000.0;
        inicio?.Invoke(a, ultimoFinVoz);
        while (altavoz.isPlaying && !(cortar != null && cortar())) yield return null;
    }

    IEnumerator CargarAudio(string archivo, Action<AudioClip> listoClip)
    {
        if (audios.TryGetValue(archivo, out var cache)) { listoClip(cache); yield break; }
        string ruta = Path.IsPathRooted(archivo) ? archivo : Path.Combine(Base, "audios", archivo);
        if (!File.Exists(ruta)) { Debug.LogWarning("[Pruebas] No existe el audio " + ruta); listoClip(null); yield break; }
        string ext = Path.GetExtension(ruta).ToLowerInvariant();
        var tipo = ext == ".mp3" ? AudioType.MPEG : ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.WAV;
        using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(ruta).AbsoluteUri, tipo))
        {
            ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = false;
            yield return req.SendWebRequest();
            AudioClip clip = req.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(req) : null;
            if (clip) { clip.LoadAudioData(); audios[archivo] = clip; }
            else Debug.LogWarning("[Pruebas] No se pudo leer el audio " + ruta + ": " + req.error);
            listoClip(clip);
        }
    }

    /// Segundos desde el inicio del audio hasta el final de la voz (sin el silencio del final).
    static float FinDeVoz(AudioClip c)
    {
        var d = Muestras(c, out float umbral);
        if (d == null) return c.length;
        for (int i = d.Length - 1; i >= 0; i--) if (Mathf.Abs(d[i]) > umbral) return (i / c.channels) / (float)c.frequency;
        return c.length;
    }

    static float InicioDeVoz(AudioClip c)
    {
        var d = Muestras(c, out float umbral);
        if (d == null) return 0f;
        for (int i = 0; i < d.Length; i++) if (Mathf.Abs(d[i]) > umbral) return (i / c.channels) / (float)c.frequency;
        return 0f;
    }

    static float[] Muestras(AudioClip c, out float umbral)
    {
        umbral = 0.02f;
        var d = new float[c.samples * c.channels];
        if (!c.GetData(d, 0)) return null;
        float pico = 0f;
        foreach (var v in d) pico = Mathf.Max(pico, Mathf.Abs(v));
        umbral = Mathf.Max(0.01f, pico * 0.06f);
        return d;
    }

    void IniciarRuido()
    {
        if (!clipRuido || volumenRuido <= 0f) return;
        ruido.clip = clipRuido; ruido.loop = true; ruido.volume = volumenRuido;
        if (!ruido.isPlaying) ruido.Play();
    }

    void DetenerRuido() { if (ruido && ruido.isPlaying) ruido.Stop(); }

    // ───────────── Utilidades ─────────────
    static bool TieneClave(string clave)
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "elevenlabs.local.json");
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            int i = json.IndexOf("\"" + clave + "\"", StringComparison.Ordinal);
            if (i < 0) return false;
            int a = json.IndexOf('"', json.IndexOf(':', i) + 1), b = a >= 0 ? json.IndexOf('"', a + 1) : -1;
            string v = a >= 0 && b > a ? json.Substring(a + 1, b - a - 1).Trim() : "";
            return v.Length > 0 && !v.StartsWith("PEGA_");
        }
        catch { return false; }
    }

    static string Csv(string s)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
        return s.IndexOfAny(new[] { ';', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    /// CSV separado por ";" (como lo guarda Excel en español), con comillas opcionales.
    static List<List<string>> LeerCsv(string texto)
    {
        var filas = new List<List<string>>();
        var fila = new List<string>();
        var celda = new StringBuilder();
        bool comillas = false;
        char sep = ';';
        int primera = texto.IndexOf('\n');
        string cab = primera > 0 ? texto.Substring(0, primera) : texto;
        if (cab.IndexOf(';') < 0 && cab.IndexOf(',') >= 0) sep = ',';
        for (int i = 0; i < texto.Length; i++)
        {
            char ch = texto[i];
            if (comillas)
            {
                if (ch == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { celda.Append('"'); i++; }
                else if (ch == '"') comillas = false;
                else celda.Append(ch);
            }
            else if (ch == '"') comillas = true;
            else if (ch == sep) { fila.Add(celda.ToString()); celda.Clear(); }
            else if (ch == '\n') { fila.Add(celda.ToString()); celda.Clear(); filas.Add(fila); fila = new List<string>(); }
            else if (ch != '\r') celda.Append(ch);
        }
        if (celda.Length > 0 || fila.Count > 0) { fila.Add(celda.ToString()); filas.Add(fila); }
        if (filas.Count > 0 && filas[0].Count > 0) filas[0][0] = filas[0][0].TrimStart('﻿');
        return filas;
    }

    static bool Tecla(string k)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return false;
        return k switch
        {
            "n" => kb.nKey.wasPressedThisFrame,
            "r" => kb.rKey.wasPressedThisFrame,
            "s" => kb.sKey.wasPressedThisFrame,
            "p" => kb.pKey.wasPressedThisFrame,
            "space" => kb.spaceKey.wasPressedThisFrame,
            _ => false,
        };
#else
        return k == "space" ? Input.GetKeyDown(KeyCode.Space) : Input.GetKeyDown(k);
#endif
    }

    // ───────────── Panel ─────────────
    GUIStyle estilo, estiloTitulo;

    void OnGUI()
    {
        if (!mostrarPanel) return;
        if (estilo == null)
        {
            estilo = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            estiloTitulo = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        }
        float w = Screen.width * 0.86f, x = (Screen.width - w) / 2f;
        var fila = actual >= 0 && actual < plan.Count && enEnsayo ? plan[actual] : (siguiente < plan.Count ? plan[siguiente] : null);
        int num = actual >= 0 && enEnsayo ? actual + 1 : siguiente + 1;
        string titulo = $"PRUEBA: {NombreModo().Replace('_', ' ').ToUpperInvariant()}   ·   ensayo {Mathf.Min(num, plan.Count)}/{plan.Count}" +
                        (fila != null ? $"   ·   {Col(fila, "id")}" : "") + (automatico ? "   ·   AUTOMÁTICO" : "") + (simularSinVoz ? "   ·   SIMULADO" : "");
        string detalle = "";
        if (fila != null)
        {
            var partes = new List<string>();
            foreach (var k in new[] { "pagina", "tipo", "version", "forma", "categoria", "voz", "esperado" })
                if (Col(fila, k).Length > 0) partes.Add(k + ": " + Col(fila, k));
            detalle = string.Join("   ·   ", partes);
            if (Col(fila, "instruccion").Length > 0) detalle += "\n" + Col(fila, "instruccion");
            string t = Col(fila, "texto");
            if (t.Length > 0 && modo != Modo.Transcripcion) detalle += "\n“" + t + "”";
        }
        string teclas = "N siguiente   ·   ESPACIO terminé (en vivo)   ·   R repetir   ·   S saltar   ·   P automático";
        // Si no cabe en la pantalla, se achica la letra (nunca se corta el texto)
        float h = 0f;
        for (int size = tamanoLetra; size >= 11; size--)
        {
            estilo.fontSize = size;
            estiloTitulo.fontSize = size + 2;
            h = estiloTitulo.CalcHeight(new GUIContent(titulo), w - 30) + estilo.CalcHeight(new GUIContent(detalle), w - 30) +
                estilo.CalcHeight(new GUIContent(estado), w - 30) + estilo.CalcHeight(new GUIContent(teclas), w - 30) + 50;
            if (h <= Screen.height * 0.45f) break;
        }
        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(new Rect(x, 10, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(x + 15, 18, w - 30, h - 10));
        GUILayout.Label(titulo, estiloTitulo);
        if (detalle.Length > 0) GUILayout.Label(detalle, estilo);
        GUI.color = new Color(1f, 0.85f, 0.3f);
        GUILayout.Label(estado, estilo);
        GUI.color = new Color(1f, 1f, 1f, 0.6f);
        GUILayout.Label(teclas, estilo);
        GUI.color = Color.white;
        GUILayout.EndArea();
    }
}
