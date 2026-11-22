using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Onboarding de StoryScape (se activa al escanear el ArUco de la primera página: marker_0).
/// Principito a la izquierda del libro, tarjetas arriba, ejemplos a la derecha; la zona de manos no se proyecta.
/// Guion: ONBOARDING_AUDIOS.md
///
/// 1. 01_intro     Bienvenida: el Principito aparece, saluda y anuncia las reglas.
/// 2. 02_libro     Tarjeta 1 "En el libro".
/// 3. 03_voz_alta  Tarjeta 2 "Lee en voz alta": en "Así podrás ver animaciones…" cruza una estrella fugaz.
/// 4. 04_quiz      Tarjeta 3 "Quiz": aparece el panel; en "Responde diciendo la letra…" el Principito elige
///                 una opción y en "Si eliges una respuesta que no es…" esa opción se apaga (sin rojo ni error).
/// 5. 05_final     "Y cuando aciertes, ¡lo celebramos juntos!": la opción correcta brilla, crece, salen estrellas;
///                 "¿Estás listo?" y luego todo se desvanece (mesa limpia).
///
/// La parte "Principito, tengo una duda" está desactivada por ahora (Incluir Dudas).
/// Los momentos (segundos) están medidos en los audios; si una línea no tiene audio, se espera un tiempo
/// según la longitud del texto (útil para probar sin audios).
/// </summary>
public class OnboardingSequence : MonoBehaviour
{
    [Serializable]
    public class Line
    {
        public string id;
        [TextArea(2, 4)] public string text;
        public AudioClip clip;
    }

    [Header("Inicio")]
    [Tooltip("Inicia al dar Play (para probar sin el ArUco)")]
    public bool startOnPlay = false;
    public float startDelay = 1f;

    [Header("Nombre del niño")]
    [Tooltip("Al inicio pregunta el nombre (01_saludo_nombre), lo escucha con Scribe y responde (02_nombre). " +
             "Se guarda en SesionLector para que el quiz lo diga. Desmarcado = saludo de siempre (01_intro)")]
    public bool pedirNombre = true;
    [Tooltip("Transcripción en tiempo real (el mismo Scribe de la lectura en vivo)")]
    public ScribeRealtimeClient scribe;
    [Tooltip("Segundos que espera a que el niño diga su nombre")]
    public float esperaNombre = 7f;
    [Tooltip("Agente \"Principito bienvenida\" (agentIdBienvenida): dice en vivo, con la voz del Principito, la frase que le manda Unity " +
             "(bienvenida, \"¡Qué lindo nombre, …!\" y \"¿Estás listo?\"). Sin configurar, suenan los audios grabados")]
    public ElevenLabsAgentClient agenteNombre;
    [Tooltip("Segundos máximos para que el agente empiece a hablar; si no, se usa el audio grabado")]
    public float esperaAgenteNombre = 5f;

    [Header("Voz en vivo (agente de bienvenida)")]
    [Tooltip("La bienvenida (\"¡Hola! ¡Bienvenido…!\") la dice en vivo el agente de bienvenida. Si no responde, suena el audio grabado")]
    public bool bienvenidaEnVivo = false;
    [Tooltip("\"Y cuando aciertes, ¡lo celebramos juntos! ¿Estás listo?\" lo dice en vivo el agente. Si no responde, suena 05_final")]
    public bool listoEnVivo = true;
    [Tooltip("Lo que dice el agente después de escuchar el nombre. {nombre} = nombre del niño")]
    public string respuestaNombre = "¡Qué lindo nombre, {nombre}! Antes de empezar nuestra aventura, te voy a contar tres reglas muy fáciles.";
    [Tooltip("Después de \"¿Estás listo?\" el agente se queda escuchando: oye la respuesta del niño, contesta con una frase corta y cierra la conversación (end_call). " +
             "Desmarcado = Unity solo espera a que suene la voz del niño (Voice Activity Waiter)")]
    public bool agenteEscuchaListo = true;
    [Tooltip("Segundos máximos que el agente espera la respuesta a \"¿Estás listo?\" antes de que Unity siga igual")]
    public float esperaRespuestaListo = 12f;
    [Tooltip("Variable {{tarea}} del agente en las frases que solo dice (bienvenida, nombre)")]
    [TextArea(2, 4)] public string tareaSoloDecir = "Nada más. Si el niño dice algo, responde con UNA frase corta y amable (por ejemplo: \"¡Escucha bien!\") y termina la conversación con end_call.";
    [Tooltip("Variable {{tarea}} del agente después de \"¿Estás listo?\"")]
    [TextArea(3, 6)] public string tareaListo = "Acabas de preguntarle al niño si está listo. Escucha su respuesta y contesta con UNA frase corta y alegre, sin hacer preguntas (por ejemplo: \"¡Genial! ¡Vamos!\"). Si dice que no o que tiene miedo, anímalo con cariño en una frase corta. No hables del quiz ni del cuento. Luego termina la conversación con end_call. Si no responde, di \"¡Vamos!\" y termina la conversación con end_call.";

    [Header("Personaje y voz")]
    public SpriteAnimator2D principito;
    public AudioSource voice;
    [Tooltip("Solo se usa si \"Esperar respuesta final\" está activo")]
    public VoiceActivityWaiter listener;
    public float principitoFade = 0.8f;

    [Header("Encabezado")]
    public ProjectedCard title;
    public ProjectedCard subtitle;

    [Header("Arte acuarela")]
    public ProjectedCard fondo;
    [Tooltip("Rectángulo negro sobre el libro: evita proyectar el fondo encima del libro real")]
    public ProjectedCard mascaraLibro;
    [Tooltip("Fondo que queda proyectado al terminar el onboarding (prólogo / lectura). Vacío = mesa en negro")]
    public ProjectedCard fondoFinal;

    [Header("Aterrizaje del Principito (al inicio)")]
    public SpriteRenderer aterrizaje;
    public Sprite[] framesAterrizaje;
    public float alturaCaida = 5.5f;
    public float duracionCaida = 1.1f;
    public float fpsAterrizaje = 9f;

    [Header("Zorro (a la derecha del libro)")]
    public SpriteAnimator2D zorro;
    public Vector3 zorroInicio = new Vector3(8.6f, -1.45f, 0f);
    public Vector3 zorroDestino = new Vector3(5.1f, -1.45f, 0f);
    public float zorroEspera = 1.2f;
    public float zorroCaminar = 2.6f;
    [Tooltip("El zorro aparece desde detrás del planeta de la derecha (sube por su borde) en vez de entrar caminando desde fuera de la proyección")]
    public bool zorroDesdePlaneta = true;
    [Tooltip("Esconde la parte del zorro que todavía está detrás del planeta (MascaraZorro)")]
    public SpriteMask mascaraPlaneta;
    [Tooltip("Donde empieza, escondido detrás del planeta")]
    public Vector3 zorroEscondido = new Vector3(6.05f, -3.1f, 0f);
    [Tooltip("Donde termina de asomarse, sobre el borde del planeta")]
    public Vector3 zorroBorde = new Vector3(5.6f, -1.28f, 0f);
    [Tooltip("Segundos que tarda en subir desde detrás del planeta (el resto de Zorro Caminar camina hasta su lugar)")]
    public float zorroSubir = 1.4f;

    [Header("1. En el libro")]
    public ProjectedCard cardBook;

    [Header("2. Lee en voz alta")]
    public ProjectedCard cardRead;
    [Tooltip("Estrella fugaz que cruza la proyección durante la regla 2 (objeto EstrellaFugaz)")]
    [UnityEngine.Serialization.FormerlySerializedAs("meteor")] public MeteorFlight estrellaFugaz;
    [UnityEngine.Serialization.FormerlySerializedAs("mostrarMeteoro")] public bool mostrarEstrellaFugaz = true;
    [Tooltip("Segundo del audio 03_voz_alta en que sale la estrella fugaz (\"Así podrás ver animaciones…\")")]
    [UnityEngine.Serialization.FormerlySerializedAs("meteorStartTime")] public float estrellaFugazInicio = 2.94f;

    [Header("3. Quiz (solo mirar)")]
    public ProjectedCard cardQuiz;
    public ProjectedCard quizPanel;
    public ProjectedCard quizTitle;
    public ProjectedCard question;
    [Tooltip("0 = Planeta #4FB3E8, 1 = Rosa #F2727F, 2 = Estrella #F7C948")]
    public ProjectedCard[] options = new ProjectedCard[3];
    public int wrongOption = 0;
    public int rightOption = 1;
    public StarBurst stars;
    [Tooltip("Segundo de 04_quiz en que aparece el panel (\"…al terminar cada capítulo…\")")]
    public float quizShowTime = 0f;
    [Tooltip("Segundo de 04_quiz en que el Principito elige la opción incorrecta (\"Responde diciendo la letra…\")")]
    public float quizTryTime = 5.64f;
    [Tooltip("Segundo de 04_quiz en que la opción incorrecta se apaga (\"Si eliges una respuesta que no es…\")")]
    public float quizWrongTime = 8.36f;
    [Tooltip("Segundo de 05_final en que se celebra la correcta (\"¡lo celebramos juntos!\")")]
    public float celebrateTime = 1.40f;

    [Header("Dudas (desactivado por ahora)")]
    public bool incluirDudas = false;
    public ProjectedCard cardDoubt;
    public ProjectedCard wordHighlight;
    public ProjectedCard definition;

    [Header("Animaciones del Principito (nombres de clips)")]
    public string animRest = "Reposo";
    public string animGreet = "Saludo";
    [Tooltip("Regla 1: \"no tapes el cuadrado\" (dedo levantado)")]
    public string animBook = "Incorrecto";
    public string animDoubt = "Duda";
    public string animQuiz = "Quiz";
    [Tooltip("Mientras habla en las líneas que no tienen animación propia (vacío = reposo)")]
    public string animTalk = "Hablando";
    [Tooltip("Regla 2: pide leer en voz alta")]
    public string animRead = "VozAlta";
    [Tooltip("En el quiz, mientras una opción brilla antes de saber si es correcta")]
    public string animThink = "Pensando";
    public string animWrong = "Incorrecto";
    public string animRight = "Correcto";

    [Header("Guion (audios en Assets/Onboarding/Audio con el mismo id)")]
    [Tooltip("Saludo que pregunta el nombre (si Pedir Nombre está activo)")]
    public Line saludoNombre = new Line { id = "01_saludo_nombre", text = "¡Hola! ¡Bienvenido al libro El Principito! ¿Cómo te llamas?" };
    [Tooltip("Respuesta después de que el niño dice su nombre (si Pedir Nombre está activo)")]
    public Line nameReply = new Line { id = "02_nombre", text = "¡Qué lindo nombre! Antes de empezar nuestra aventura, te voy a contar tres reglas muy fáciles." };
    [Tooltip("Saludo sin preguntar el nombre (si Pedir Nombre está desactivado)")]
    public Line intro = new Line { id = "01_intro", text = "¡Hola! ¡Bienvenido al libro El Principito! Antes de empezar nuestra aventura, te voy a comentar unas reglas muy fáciles." };
    public Line bookLine = new Line { id = "02_libro", text = "Primero: no tapes el cuadrado de barras con tus manos. Así sé en qué página estás." };
    public Line readLine = new Line { id = "03_voz_alta", text = "Segundo: lee en voz alta. Así podrás ver animaciones durante la historia." };
    public Line quizLine = new Line { id = "04_quiz", text = "Tercero: al terminar cada capítulo, te haré dos preguntas sobre lo que leíste. Responde diciendo la letra de la opción. Si eliges una respuesta que no es, no pasa nada. ¡Puedes intentarlo otra vez!" };
    public Line finalLine = new Line { id = "05_final", text = "Y cuando aciertes, ¡lo celebramos juntos! ¿Estás listo?" };
    [Tooltip("Respuesta cuando el niño contesta a \"¿Estás listo?\" (luego empieza la lectura en vivo)")]
    public Line readyLine = new Line { id = "06_listo", text = "¡Genial! Pasa la página y empieza a leer en voz alta." };
    [Tooltip("Solo si Incluir Dudas está activo")]
    public Line doubtLine = new Line { id = "04_duda", text = "Si tienes una duda, llámame diciendo: Principito, tengo una duda." };
    public Line doubtExample = new Line { id = "04b_duda_ejemplo", text = "Por ejemplo, si no entiendes la palabra dedicatoria, te la marco en el libro y te cuento lo que significa." };

    [Header("Quiz al terminar el onboarding")]
    [Tooltip("Después de \"¿Estás listo?\" empieza solo este quiz (con el fondo de acuarela todavía puesto); al terminar, se cambia al fondo del prólogo")]
    public bool hacerQuizFinal = true;
    public QuizSession quizFinal;
    [Tooltip("Id del quiz (en Lectura > Quiz Session > Quizzes)")]
    public string quizFinalId = "pagina_1";

    [Header("Tiempos")]
    public float pauseBetweenLines = 0.35f;
    [Tooltip("Espera tras \"¿Estás listo?\" antes de que todo se desvanezca")]
    public float finalHold = 1.2f;
    [Tooltip("Espera que el niño responda a \"¿Estás listo?\" (micrófono) antes de desvanecer")]
    public bool esperarRespuestaFinal = true;
    public float cleanupFade = 1.2f;

    [Header("Eventos")]
    public UnityEvent onStarted = new UnityEvent();
    public UnityEvent onFinished = new UnityEvent();

    [Tooltip("Seguimiento del libro: si tiene marcado 'Calibrar antes del onboarding', el onboarding espera a la calibración")]
    public BookPoseFollower calibracion;

    public bool IsRunning { get; private set; }

    SpriteRenderer principitoSr;

    void Start()
    {
        if (principito)
        {
            principitoSr = principito.GetComponent<SpriteRenderer>();
            principito.NextOverride = (finished, next) => next == animTalk && !speaking ? animRest : next;
        }
        HideAll(true);
        SetPrincipitoAlpha(0f);
        SetSrAlpha(aterrizaje, 0f);
        if (zorro) SetSrAlpha(zorro.GetComponent<SpriteRenderer>(), 0f);
        if (startOnPlay) Invoke(nameof(IniciarOnboarding), startDelay);
    }

    /// Punto de entrada: lo llama la detección del ArUco de la primera página (marker_0).
    [ContextMenu("Iniciar onboarding")]
    public void IniciarOnboarding()
    {
        if (IsRunning) return;
        if (calibracion && calibracion.BloqueaOnboarding)
        {
            calibracion.SolicitarOnboarding();
            Debug.Log("[Onboarding] Espera: primero se calibra la proyección.");
            return;
        }
        StartCoroutine(Run());
    }

    /// Nuevo alumno: corta el onboarding en curso y deja todo listo para empezar de nuevo con el ArUco de la página 1.
    public void Cancelar()
    {
        StopAllCoroutines();
        CancelInvoke();
        if (voice) voice.Stop();
        if (agenteNombre && agenteNombre.IsRunning) agenteNombre.EndConversation();
        if (scribeNombre && scribe) { scribe.StopStreaming(); scribeNombre = false; }
        speaking = false;
        IsRunning = false;
        HideAll(true);
        SetPrincipitoAlpha(0f);
        SetSrAlpha(aterrizaje, 0f);
        if (zorro) SetSrAlpha(zorro.GetComponent<SpriteRenderer>(), 0f);
        Debug.Log("[Onboarding] Cancelado (nuevo alumno).");
    }

    IEnumerator Run()
    {
        IsRunning = true;
        onStarted?.Invoke();
        HideAll(true);

        // 1. Bienvenida: fondo y título; el zorro entra caminando y el Principito aterriza en su colina
        Show(fondo);
        Show(mascaraLibro);
        Show(title);
        Show(subtitle);
        if (zorro) StartCoroutine(ZorroEntra());
        if (aterrizaje && framesAterrizaje != null && framesAterrizaje.Length >= 4) yield return Aterrizar();
        Anim(animGreet);
        yield return FadePrincipito(1f);
        SesionLector.Reiniciar();   // nuevo niño: se olvida el nombre anterior
        if (pedirNombre)
        {
            // Scribe se conecta mientras suena el saludo (en pausa), para escuchar apenas termina la pregunta.
            // Si la bienvenida la dice el agente en vivo, no: ambos usarían el micrófono a la vez.
            if (!(bienvenidaEnVivo && AgenteEnVivo)) PrepararScribe();
            yield return Decir(saludoNombre, bienvenidaEnVivo);   // "…¿Cómo te llamas?"
            Anim(animRest);
            yield return EscucharNombre();
            Anim(animGreet);
            bool dichoEnVivo = false;
            if (SesionLector.TieneNombre && AgenteEnVivo)
                yield return EnVivo(respuestaNombre.Replace("{nombre}", SesionLector.Nombre), ok => dichoEnVivo = ok);   // "¡Qué lindo nombre, Camila!…"
            if (!dichoEnVivo) yield return Say(nameReply);           // "¡Qué lindo nombre!…" (grabado)
        }
        else yield return Decir(intro, bienvenidaEnVivo);
        Anim(animRest);

        // 2. Tarjeta 1: no tapar el ArUco
        Show(cardBook);
        Anim(animBook);
        yield return Say(bookLine);
        Anim(animRest);

        // 3. Tarjeta 2: lee en voz alta → estrella fugaz en "Así podrás ver animaciones…"
        Show(cardRead);
        Anim(animRead);
        bool estrellaEnVuelo = mostrarEstrellaFugaz && estrellaFugaz != null;
        if (estrellaEnVuelo)
            StartCoroutine(At(readLine, estrellaFugazInicio, () => StartCoroutine(VolarEstrella(() => estrellaEnVuelo = false))));
        yield return Say(readLine);
        while (estrellaEnVuelo) yield return null;
        Anim(animRest);

        // (Opcional, desactivado) ¿Una duda?
        if (incluirDudas)
        {
            Show(cardDoubt);
            Anim(animDoubt);
            yield return Say(doubtLine);
            Show(wordHighlight);
            if (wordHighlight) wordHighlight.SetGlow(true);
            Show(definition);
            Anim(animTalk);
            yield return Say(doubtExample);
            Anim(animRest);
            Hide(wordHighlight);
            Hide(definition);
        }

        // 4. Tarjeta 3: quiz (solo mirar). Todo en un solo audio, con momentos medidos.
        Show(cardQuiz);
        Anim(animQuiz);
        StartCoroutine(At(quizLine, quizShowTime, () =>
        {
            Show(quizPanel);
            Show(quizTitle);
            Show(question);
            foreach (var o in options) Show(o);
        }));
        StartCoroutine(At(quizLine, quizTryTime, () =>      // "Responde diciendo la letra…": elige la A
        {
            Anim(animThink);
            Glow(wrongOption, true);
        }));
        StartCoroutine(At(quizLine, quizWrongTime, () =>    // "Si eliges una respuesta que no es…": se apaga
        {
            Glow(wrongOption, false);
            Dim(wrongOption);
            Anim(animTalk);   // explica que no pasa nada: habla
        }));
        yield return Say(quizLine);

        // 5. Correcta y cierre: brilla → "¡lo celebramos juntos!" crece + estrellas → "¿Estás listo?"
        Anim(animThink);
        Glow(rightOption, true);
        Action celebrar = () =>
        {
            Glow(rightOption, false);
            Grow(rightOption);
            for (int i = 0; i < options.Length; i++) if (i != rightOption) Dim(i);
            if (stars && Valid(rightOption)) stars.Burst(options[rightOption].transform.position);
            Anim(animRight);
        };
        bool finalEnVivo = false;
        bool agenteEscucha = listoEnVivo && agenteEscuchaListo;
        if (listoEnVivo && AgenteEnVivo)   // en vivo: se celebra a los celebrateTime s de que empieza a hablar
            yield return EnVivo(finalLine.text, ok => finalEnVivo = ok, () => StartCoroutine(Despues(celebrateTime, celebrar)), agenteEscucha);
        if (!finalEnVivo)
        {
            StartCoroutine(At(finalLine, celebrateTime, celebrar));
            yield return Say(finalLine);
        }
        if (finalEnVivo && agenteEscucha)
        {
            // El agente ya escuchó la respuesta y contestó
            Anim(animRest);
            if (!HayQuizFinal) yield return Say(readyLine);
        }
        else if (esperarRespuestaFinal && listener)
        {
            // Espera el "¡Sí, estoy listo!" (o cualquier respuesta; si no responde, continúa igual)
            Anim(animRest);
            yield return listener.WaitForResponse();
            Debug.Log(listener.LastResponded ? "[Onboarding] El niño respondió." : "[Onboarding] Sin respuesta: se continúa.");
            if (!HayQuizFinal) yield return Say(readyLine);   // con quiz, invita a leer el Principito al cerrar el quiz
        }
        else yield return new WaitForSeconds(finalHold);

        // Quiz automático, todavía con el fondo de acuarela
        if (HayQuizFinal) yield return QuizFinal();

        Anim(animRest);
        HideAll(false);
        Show(fondoFinal);   // el fondo del prólogo aparece mientras se desvanece el de acuarela
        if (zorro) StartCoroutine(FadeSr(zorro.GetComponent<SpriteRenderer>(), 0f, cleanupFade));
        yield return FadePrincipito(0f, cleanupFade);

        IsRunning = false;
        onFinished?.Invoke();
        Debug.Log(fondoFinal ? "[Onboarding] Terminado: queda el fondo del prólogo." : "[Onboarding] Terminado: mesa limpia.");
    }

    /// Escucha la respuesta a "¿Cómo te llamas?" con Scribe y guarda el nombre en SesionLector.
    /// Se conecta solo estos segundos (Scribe cobra el audio enviado). Si no se entiende, sigue sin nombre.
    IEnumerator EscucharNombre()
    {
        if (!scribe) scribe = FindFirstObjectByType<ScribeRealtimeClient>();
        if (!scribe || !scribe.isActiveAndEnabled || !scribe.HasApiKey())
        {
            Debug.LogWarning("[Onboarding] No se puede escuchar el nombre (falta Scribe o su apiKey). Se sigue sin nombre.");
            yield return new WaitForSeconds(2f);   // da tiempo a que el niño responda igual
            yield break;
        }
        string final = null, parcial = null;
        Action<string, bool> alTexto = (texto, esFinal) =>
        {
            if (string.IsNullOrWhiteSpace(texto)) return;
            if (esFinal) final = final == null ? texto : final + " " + texto; else parcial = texto;
        };
        bool yaCorria = scribe.IsRunning && !scribeNombre;   // ya lo usaba otro (la lectura): no se cierra al final
        scribe.TextReceived += alTexto;
        if (!scribe.IsRunning) scribe.StartStreaming();
        scribe.Pausado = false;   // terminó la pregunta: desde aquí sí escucha
        bool reintento = false;
        for (float t = 0f; t < esperaNombre && final == null; t += Time.deltaTime)
        {
            // Si la conexión con Scribe falló (red), se reintenta una vez en vez de esperar sin escuchar
            if (!yaCorria && !reintento && t > 0.5f && !scribe.IsRunning && parcial == null)
            {
                reintento = true;
                Debug.LogWarning("[Onboarding] Scribe no se conectó; se reintenta para escuchar el nombre.");
                scribe.StartStreaming();
            }
            yield return null;
        }
        // Empezó a hablar justo al final: un poco más para la frase completa
        if (final == null && parcial != null) for (float t = 0f; t < 1.5f && final == null; t += Time.deltaTime) yield return null;
        scribe.TextReceived -= alTexto;
        if (!yaCorria) scribe.StopStreaming();
        scribeNombre = false;

        string escuchado = final ?? parcial ?? "";
        string nombre = SesionLector.ExtraerNombre(escuchado);
        Debug.Log($"[Onboarding] Respuesta al nombre: \"{escuchado}\" → {(nombre.Length > 0 ? nombre : "(no se reconoció un nombre)")}");
        if (nombre.Length > 0) SesionLector.SetNombre(nombre);
    }

    bool scribeNombre;   // Scribe lo abrió el onboarding para escuchar el nombre

    /// Conecta Scribe en pausa mientras suena "¿Cómo te llamas?" (la conexión tarda 1–2 s).
    void PrepararScribe()
    {
        if (!scribe) scribe = FindFirstObjectByType<ScribeRealtimeClient>();
        if (!scribe || !scribe.isActiveAndEnabled || !scribe.HasApiKey() || scribe.IsRunning) return;
        scribe.Pausado = true;
        scribe.StartStreaming();
        scribeNombre = true;
    }

    bool HayQuizFinal => hacerQuizFinal && quizFinal && quizFinal.isActiveAndEnabled && !string.IsNullOrEmpty(quizFinalId);

    /// Deja solo el fondo de acuarela y al Principito (el quiz ocupa la derecha), hace el quiz y espera a que termine.
    IEnumerator QuizFinal()
    {
        Anim(animRest);
        foreach (var c in new[] { mascaraLibro, title, subtitle, cardBook, cardRead, cardQuiz, cardDoubt, wordHighlight, definition, quizPanel, quizTitle, question })
            if (c) c.Hide();
        if (options != null) foreach (var o in options) if (o) o.Hide();
        if (zorro) StartCoroutine(FadeSr(zorro.GetComponent<SpriteRenderer>(), 0f, cleanupFade));
        yield return new WaitForSeconds(cleanupFade);
        Debug.Log($"[Onboarding] Empieza el quiz \"{quizFinalId}\".");
        quizFinal.IniciarQuizDeNuevo(quizFinalId);
        yield return null;
        while (quizFinal.IsRunning) yield return null;
        // Sin agente (respuestas con el teclado de prueba) nadie invitó a empezar a leer
        if (!quizFinal.UsoAgente)
        {
            yield return FadePrincipito(1f);
            yield return Say(readyLine);
        }
    }

    bool AgenteEnVivo => agenteNombre && !agenteNombre.simulate && agenteNombre.HasAgentId();

    /// Dice una línea en vivo con el agente de bienvenida (si está activo y responde) o con su audio grabado.
    IEnumerator Decir(Line line, bool enVivo)
    {
        bool ok = false;
        if (enVivo && AgenteEnVivo && line != null) yield return EnVivo(line.text, r => ok = r);
        if (!ok) yield return Say(line);
    }

    IEnumerator Despues(float segundos, Action accion) { yield return new WaitForSeconds(segundos); accion?.Invoke(); }

    /// El agente de bienvenida dice en vivo la frase que le manda Unity (su first message es {{frase}}).
    /// Unity cuelga en cuanto termina de hablar, para que no se quede escuchando ni responda otra cosa.
    /// alEmpezar se llama cuando su voz empieza a sonar (para sincronizar animaciones).
    /// escuchar = después de la frase no cuelga: el agente escucha al niño, responde y cierra con end_call (máx. esperaRespuestaListo s).
    IEnumerator EnVivo(string frase, Action<bool> listo, Action alEmpezar = null, bool escuchar = false)
    {
        var a = agenteNombre;
        a.StartConversation(new System.Collections.Generic.Dictionary<string, string>
        {
            { "frase", frase ?? "" },
            { "nombre", SesionLector.TieneNombre ? SesionLector.Nombre : "" },
            { "tarea", (escuchar ? tareaListo : tareaSoloDecir) ?? "" },
        });
        float t = 0f;
        while (a.IsRunning && !a.AgentSpeaking && t < esperaAgenteNombre) { t += Time.deltaTime; yield return null; }
        if (!a.AgentSpeaking)
        {
            if (a.IsRunning) a.EndConversation();
            while (a.IsRunning) yield return null;
            Debug.LogWarning("[Onboarding] El agente de bienvenida no respondió a tiempo: se usa el audio grabado.");
            listo(false);
            yield break;
        }
        alEmpezar?.Invoke();
        speaking = true;
        float silencio = 0f;
        for (float total = 0f; a.IsRunning && total < 20f; total += Time.deltaTime)
        {
            if (a.AgentSpeaking) { silencio = 0f; TalkIfResting(); }
            else if ((silencio += Time.deltaTime) > 0.9f) break;   // terminó la frase
            yield return null;
        }
        speaking = false;
        StopTalking();
        if (escuchar && a.IsRunning)
        {
            // Escucha la respuesta: el agente contesta y cierra con end_call (Unity espera a que termine de sonar su voz)
            string respuesta = null;
            Action<string> oir = s => { if (!string.IsNullOrWhiteSpace(s) && s.Trim() != "...") respuesta = s; };
            a.UserTranscript += oir;
            Anim(animRest);
            for (float te = 0f; a.IsRunning && te < esperaRespuestaListo; te += Time.deltaTime)
            {
                if (a.AgentSpeaking) { speaking = true; TalkIfResting(); }
                else if (speaking) { speaking = false; StopTalking(); }
                yield return null;
            }
            a.UserTranscript -= oir;
            speaking = false;
            StopTalking();
            Debug.Log(respuesta != null ? $"[Onboarding] Respuesta a \"¿Estás listo?\": \"{respuesta}\"" : "[Onboarding] Sin respuesta a \"¿Estás listo?\": se continúa.");
        }
        if (a.IsRunning) a.EndConversation();
        while (a.IsRunning) yield return null;
        yield return new WaitForSeconds(pauseBetweenLines);
        listo(true);
    }

    /// Ejecuta una acción en el segundo indicado de una línea que empieza ahora.
    /// Sin audio, el momento se ajusta proporcionalmente a la duración estimada del texto.
    IEnumerator At(Line line, float seconds, Action action)
    {
        float delay = seconds;
        if (line == null || line.clip == null)
        {
            float est = EstimatedDuration(line);
            delay = Mathf.Min(seconds, est * 0.9f);
        }
        else delay = Mathf.Clamp(seconds, 0f, line.clip.length);
        yield return new WaitForSeconds(delay);
        action?.Invoke();
    }

    /// El Principito cae desde arriba con los brazos abiertos, aterriza agachado y se pone de pie.
    IEnumerator Aterrizar()
    {
        Vector3 end = principito ? principito.transform.position : aterrizaje.transform.position;
        Vector3 start = end + Vector3.up * alturaCaida;
        aterrizaje.transform.position = start;
        SetSrAlpha(aterrizaje, 1f);
        for (float t = 0f; t < duracionCaida; t += Time.deltaTime)
        {
            float k = t / duracionCaida;
            aterrizaje.transform.position = Vector3.Lerp(start, end, k * k);          // acelera al caer
            aterrizaje.sprite = framesAterrizaje[Mathf.Min(2, (int)(k * 3f))];       // cuadros 1–3: en el aire
            yield return null;
        }
        aterrizaje.transform.position = end;
        for (int i = 3; i < framesAterrizaje.Length; i++)                           // cuadros 4–8: aterriza y se levanta
        {
            aterrizaje.sprite = framesAterrizaje[i];
            yield return new WaitForSeconds(1f / Mathf.Max(1f, fpsAterrizaje));
        }
        SetPrincipitoAlpha(1f);
        SetSrAlpha(aterrizaje, 0f);
    }

    /// El zorro aparece desde detrás del planeta de la derecha (o, si está desactivado, entra caminando por la derecha)
    /// y se sienta junto al libro.
    IEnumerator ZorroEntra()
    {
        var sr = zorro.GetComponent<SpriteRenderer>();
        if (zorroDesdePlaneta && mascaraPlaneta) { yield return ZorroDesdePlaneta(sr); yield break; }
        zorro.transform.position = zorroInicio;
        yield return new WaitForSeconds(zorroEspera);
        zorro.Play("Caminar", true);
        SetSrAlpha(sr, 1f);
        for (float t = 0f; t < zorroCaminar; t += Time.deltaTime)
        {
            zorro.transform.position = Vector3.Lerp(zorroInicio, zorroDestino, t / zorroCaminar);
            yield return null;
        }
        zorro.transform.position = zorroDestino;
        zorro.Play("Sentarse", true);
    }

    /// Sube caminando por el otro lado del planeta: la máscara oculta lo que todavía está detrás del borde.
    IEnumerator ZorroDesdePlaneta(SpriteRenderer sr)
    {
        mascaraPlaneta.enabled = true;
        sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        zorro.transform.position = zorroEscondido;
        yield return new WaitForSeconds(zorroEspera);
        zorro.Play("Caminar", true);
        SetSrAlpha(sr, 1f);
        // Curva: sube casi vertical y al final avanza hacia el libro, como si subiera la ladera del planeta
        Vector3 control = new Vector3(zorroEscondido.x - 0.05f, zorroBorde.y + 0.1f, 0f);
        float subir = Mathf.Max(0.2f, zorroSubir);
        for (float t = 0f; t < subir; t += Time.deltaTime)
        {
            float k = t / subir; float u = k * (2f - k);   // frena al llegar arriba
            float m = 1f - u;
            zorro.transform.position = m * m * zorroEscondido + 2f * m * u * control + u * u * zorroBorde;
            yield return null;
        }
        zorro.transform.position = zorroBorde;
        sr.maskInteraction = SpriteMaskInteraction.None;   // ya está entero sobre el planeta
        float caminar = Mathf.Max(0.4f, zorroCaminar - subir);
        for (float t = 0f; t < caminar; t += Time.deltaTime)
        {
            zorro.transform.position = Vector3.Lerp(zorroBorde, zorroDestino, t / caminar);
            yield return null;
        }
        zorro.transform.position = zorroDestino;
        zorro.Play("Sentarse", true);
    }

    IEnumerator FadeSr(SpriteRenderer sr, float to, float duration)
    {
        if (!sr) yield break;
        float from = sr.color.a;
        for (float t = 0f; t < duration; t += Time.deltaTime) { SetSrAlpha(sr, Mathf.Lerp(from, to, t / duration)); yield return null; }
        SetSrAlpha(sr, to);
    }

    static void SetSrAlpha(SpriteRenderer sr, float a)
    {
        if (!sr) return;
        var c = sr.color; c.a = a; sr.color = c;
    }

    IEnumerator VolarEstrella(Action done)
    {
        yield return estrellaFugaz.Play();
        done?.Invoke();
    }

    // ───────────── Ayudas ─────────────
    bool speaking;

    static float EstimatedDuration(Line line) =>
        line == null || string.IsNullOrEmpty(line.text) ? 1.5f : Mathf.Clamp(line.text.Length * 0.065f, 1.5f, 15f);

    IEnumerator Say(Line line)
    {
        if (line == null) yield break;
        speaking = true;
        if (line.clip != null && voice != null)
        {
            voice.clip = line.clip;
            voice.Play();
            yield return null;
            float limit = line.clip.length + 1.5f, t = 0f;   // seguro: nunca esperar de más
            // Tiempo escalado: en pausa (timeScale 0) no se agota el límite de seguridad
            while (voice.isPlaying && t < limit) { t += Time.deltaTime; TalkIfResting(); yield return null; }
        }
        else
        {
            Debug.Log($"[Onboarding] (sin audio {line.id}) {line.text}");
            float dur = EstimatedDuration(line);
            for (float t = 0f; t < dur; t += Time.deltaTime) { TalkIfResting(); yield return null; }
        }
        speaking = false;
        StopTalking();
        yield return new WaitForSeconds(pauseBetweenLines);
    }

    IEnumerator FadePrincipito(float to, float duration = -1f)
    {
        if (principitoSr == null) yield break;
        if (duration < 0f) duration = principitoFade;
        float from = principitoSr.color.a;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            SetPrincipitoAlpha(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetPrincipitoAlpha(to);
    }

    void SetPrincipitoAlpha(float a)
    {
        if (principitoSr == null) return;
        var c = principitoSr.color; c.a = a; principitoSr.color = c;
    }

    void TalkIfResting()
    {
        if (principito && !string.IsNullOrEmpty(animTalk) && principito.CurrentClip == animRest) principito.Play(animTalk);
    }

    void StopTalking()
    {
        if (principito && !string.IsNullOrEmpty(animTalk) && principito.CurrentClip == animTalk) principito.Play(animRest);
    }

    void Anim(string clip) { if (principito && !string.IsNullOrEmpty(clip)) principito.Play(clip); }

    bool Valid(int i) => options != null && i >= 0 && i < options.Length && options[i] != null;
    void Glow(int i, bool on) { if (Valid(i)) options[i].SetGlow(on); }
    void Dim(int i) { if (Valid(i)) options[i].Dim(); }
    void Grow(int i) { if (Valid(i)) options[i].SetGrow(true); }

    static void Show(ProjectedCard c) { if (c) c.Show(); }
    static void Hide(ProjectedCard c) { if (c) c.Hide(); }

    void HideAll(bool instant)
    {
        foreach (var c in new[] { fondo, fondoFinal, mascaraLibro, title, subtitle, cardBook, cardRead, cardQuiz, cardDoubt, wordHighlight, definition, quizPanel, quizTitle, question })
            if (c) { c.ResetState(); c.Hide(instant); }
        if (options != null) foreach (var o in options) if (o) { o.ResetState(); o.Hide(instant); }
    }

    /// Lista de líneas (para asignar audios desde el editor).
    public Line[] AllLines() => new[] { saludoNombre, nameReply, intro, bookLine, readLine, quizLine, finalLine, readyLine, doubtLine, doubtExample };
}
