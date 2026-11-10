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
/// 2. 02_libro     Tarjeta 1 "En el libro": se iluminan los bordes del área del marcador.
/// 3. 03_voz_alta  Tarjeta 2 "Lee en voz alta": en "Así podrás ver animaciones…" un meteoro cruza hacia el planeta.
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

    [Header("Personaje y voz")]
    public SpriteAnimator2D principito;
    public AudioSource voice;
    [Tooltip("Solo se usa si \"Esperar respuesta final\" está activo")]
    public VoiceActivityWaiter listener;
    public float principitoFade = 0.8f;

    [Header("Encabezado")]
    public ProjectedCard title;
    public ProjectedCard subtitle;

    [Header("1. En el libro")]
    public ProjectedCard cardBook;
    public ProjectedCard markerFrame;

    [Header("2. Lee en voz alta")]
    public ProjectedCard cardRead;
    public MeteorFlight meteor;
    public ProjectedCard planet;
    [Tooltip("Muestra el meteoro y el planeta durante la regla 2")]
    public bool mostrarMeteoro = true;
    [Tooltip("Segundo del audio 03_voz_alta en que sale el meteoro (\"Así podrás ver animaciones…\")")]
    public float meteorStartTime = 2.94f;

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
    public Line intro = new Line { id = "01_intro", text = "¡Hola! ¡Bienvenido al libro El Principito! Antes de empezar nuestra aventura, te voy a comentar unas reglas muy fáciles." };
    public Line bookLine = new Line { id = "02_libro", text = "Primero: no tapes el cuadrado de barras con tus manos. Así sé en qué página estás." };
    public Line readLine = new Line { id = "03_voz_alta", text = "Segundo: lee en voz alta. Así podrás ver animaciones durante la historia." };
    public Line quizLine = new Line { id = "04_quiz", text = "Tercero: al terminar cada capítulo, te haré dos preguntas sobre lo que leíste. Responde diciendo la letra de la opción. Si eliges una respuesta que no es, no pasa nada. ¡Puedes intentarlo otra vez!" };
    public Line finalLine = new Line { id = "05_final", text = "Y cuando aciertes, ¡lo celebramos juntos! ¿Estás listo?" };
    [Tooltip("Solo si Incluir Dudas está activo")]
    public Line doubtLine = new Line { id = "04_duda", text = "Si tienes una duda, llámame diciendo: Principito, tengo una duda." };
    public Line doubtExample = new Line { id = "04b_duda_ejemplo", text = "Por ejemplo, si no entiendes la palabra dedicatoria, te la marco en el libro y te cuento lo que significa." };

    [Header("Tiempos")]
    public float pauseBetweenLines = 0.35f;
    [Tooltip("Espera tras \"¿Estás listo?\" antes de que todo se desvanezca")]
    public float finalHold = 1.2f;
    [Tooltip("Espera que el niño responda a \"¿Estás listo?\" (micrófono) antes de desvanecer")]
    public bool esperarRespuestaFinal = false;
    public float cleanupFade = 1.2f;

    [Header("Eventos")]
    public UnityEvent onStarted = new UnityEvent();
    public UnityEvent onFinished = new UnityEvent();

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
        if (startOnPlay) Invoke(nameof(IniciarOnboarding), startDelay);
    }

    /// Punto de entrada: lo llama la detección del ArUco de la primera página (marker_0).
    [ContextMenu("Iniciar onboarding")]
    public void IniciarOnboarding()
    {
        if (IsRunning) return;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        IsRunning = true;
        onStarted?.Invoke();
        HideAll(true);

        // 1. Bienvenida
        Show(title);
        Show(subtitle);
        Anim(animGreet);
        yield return FadePrincipito(1f);
        yield return Say(intro);
        Anim(animRest);

        // 2. Tarjeta 1: bordes del área del marcador
        Show(cardBook);
        Show(markerFrame);
        Anim(animBook);
        if (markerFrame) markerFrame.SetGlow(true);
        yield return Say(bookLine);
        Hide(markerFrame);
        Anim(animRest);

        // 3. Tarjeta 2: lee en voz alta → meteoro en "Así podrás ver animaciones…"
        Show(cardRead);
        if (mostrarMeteoro) Show(planet);
        Anim(animRead);
        bool meteorRunning = mostrarMeteoro && meteor != null;
        if (meteorRunning)
            StartCoroutine(At(readLine, meteorStartTime, () => StartCoroutine(PlayMeteor(() => meteorRunning = false))));
        yield return Say(readLine);
        while (meteorRunning) yield return null;
        Anim(animRest);
        Hide(planet);

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
        StartCoroutine(At(finalLine, celebrateTime, () =>
        {
            Glow(rightOption, false);
            Grow(rightOption);
            for (int i = 0; i < options.Length; i++) if (i != rightOption) Dim(i);
            if (stars && Valid(rightOption)) stars.Burst(options[rightOption].transform.position);
            Anim(animRight);
        }));
        yield return Say(finalLine);
        if (esperarRespuestaFinal && listener) yield return listener.WaitForResponse();
        else yield return new WaitForSeconds(finalHold);

        Anim(animRest);
        HideAll(false);
        yield return FadePrincipito(0f, cleanupFade);

        IsRunning = false;
        onFinished?.Invoke();
        Debug.Log("[Onboarding] Terminado: mesa limpia.");
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

    IEnumerator PlayMeteor(Action done)
    {
        yield return meteor.Play();
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
            while (voice.isPlaying && t < limit) { t += Time.unscaledDeltaTime; TalkIfResting(); yield return null; }
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
        foreach (var c in new[] { title, subtitle, cardBook, markerFrame, cardRead, planet, cardQuiz, cardDoubt, wordHighlight, definition, quizPanel, quizTitle, question })
            if (c) { c.ResetState(); c.Hide(instant); }
        if (options != null) foreach (var o in options) if (o) { o.ResetState(); o.Hide(instant); }
    }

    /// Lista de líneas (para asignar audios desde el editor).
    public Line[] AllLines() => new[] { intro, bookLine, readLine, quizLine, finalLine, doubtLine, doubtExample };
}
