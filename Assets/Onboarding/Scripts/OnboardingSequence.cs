using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Onboarding de StoryScape (se activa al escanear el ArUco de la primera página: marker_0).
/// Principito a la izquierda del libro, tarjetas arriba, ejemplos a la derecha; la zona de manos no se proyecta.
///
/// 1. Bienvenida: el Principito aparece, saluda y anuncia las reglas.
/// 2. "En el libro": se iluminan suavemente los bordes del área del marcador.
/// 3. "Lee en voz alta": un meteoro cruza junto al libro hacia un planeta.
/// 4. "¿Una duda?": el Principito dice la frase gatillo; se resalta "Dedicatoria" y aparece su significado.
/// 5. Quiz (solo mirar): respuesta incorrecta y luego correcta.
/// 6. Cierre: el Principito invita a leer en voz alta y todo se desvanece (mesa limpia).
///
/// Los audios son pregenerados con ElevenLabs (TTS). Si una línea no tiene audio,
/// se espera un tiempo según la longitud del texto (útil para probar sin audios).
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
    public VoiceActivityWaiter listener;
    public float principitoFade = 0.8f;


    [Header("2. En el libro")]
    public ProjectedCard cardBook;
    public ProjectedCard markerFrame;

    [Header("3. Lee en voz alta")]
    public ProjectedCard cardRead;
    public MeteorFlight meteor;
    public ProjectedCard planet;
    [Tooltip("Segundo del audio 03_voz_alta en que empieza el meteoro (\"¡Mira, ahí va un meteoro!\")")]
    public float meteorStartTime = 5.75f;

    [Header("4. ¿Una duda?")]
    public ProjectedCard cardDoubt;
    public ProjectedCard wordHighlight;
    public ProjectedCard definition;

    [Header("5. Quiz (solo mirar)")]
    public ProjectedCard quizPanel;
    public ProjectedCard quizTitle;
    public ProjectedCard question;
    [Tooltip("0 = Planeta #4FB3E8, 1 = Rosa #F2727F, 2 = Estrella #F7C948")]
    public ProjectedCard[] options = new ProjectedCard[3];
    public int wrongOption = 0;
    public int rightOption = 1;
    public StarBurst stars;

    [Header("Animaciones del Principito (nombres de clips)")]
    public string animRest = "Reposo";
    public string animGreet = "React";
    public string animDoubt = "Duda";
    public string animQuiz = "Asking";
    [Tooltip("Mientras habla en las líneas que no tienen animación propia (vacío = reposo)")]
    public string animTalk = "Hablando";
    [Tooltip("Regla 2: pide leer en voz alta, hasta que pasa el meteoro")]
    public string animRead = "VozAlta";
    [Tooltip("En el quiz, mientras una opción brilla antes de saber si es correcta")]
    public string animThink = "Pensando";
    public string animWrong = "Incorrecto";
    public string animRight = "Correcto";

    [Header("Guion (audios en Assets/Onboarding/Audio con el mismo id)")]
    public Line intro = new Line { id = "01_intro", text = "¡Hola! ¡Bienvenido al libro El Principito! Antes de empezar nuestra aventura, te voy a comentar unas reglas muy fáciles." };
    public Line bookLine = new Line { id = "02_libro", text = "Primero: no tapes el cuadro negro con tus manos. Así sé en qué página estás." };
    public Line readLine = new Line { id = "03_voz_alta", text = "Segundo: lee en voz alta. Así podrás ver animaciones durante la historia. ¡Mira, ahí va un meteoro!" };
    public Line doubtLine = new Line { id = "04_duda", text = "Tercero: si tienes una duda, llámame diciendo: Principito, tengo una duda." };
    public Line doubtExample = new Line { id = "04b_duda_ejemplo", text = "Por ejemplo, si no entiendes la palabra dedicatoria, te la marco en el libro y te cuento lo que significa: son palabras al inicio de un libro para regalárselo a alguien especial como cuando escribes \"Para mi mamá\" en un dibujo que le regalas." };
    public Line quizLine = new Line { id = "04c_quiz", text = "Y al terminar cada capítulo, te haré dos preguntas sobre lo que leíste." };
    public Line quizIntro = new Line { id = "05_juego_intro", text = "Mira, así se verán las preguntas. Por ejemplo: ¿cómo vas a leer el cuento? ¿En silencio, en voz alta o cantando? Se responde diciendo la letra de la opción." };
    public Line quizWrong = new Line { id = "06_juego_casi", text = "Si eliges una respuesta que no es como por ejemplo la opción A, no pasa nada. ¡Puedes intentarlo otra vez!" };
    public Line quizRight = new Line { id = "07_juego_bien", text = "Y cuando aciertes, ¡lo celebramos juntos! ¿Estás listo? ¡Empecemos!" };
    public Line farewell = new Line { id = "08_final", text = "¡Genial! Pasa la página y empieza a leer en voz alta." };

    [Header("Tiempos")]
    public float pauseBetweenLines = 0.35f;
    public float choiceThinkTime = 1.4f;
    public float cleanupFade = 1.2f;

    [Header("Eventos")]
    public UnityEvent onStarted;
    public UnityEvent onFinished;

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

        // 1. Bienvenida: el Principito aparece, saluda y anuncia las reglas
        Anim(animGreet);
        yield return FadePrincipito(1f);
        yield return Say(intro);
        Anim(animRest);

        // 2. En el libro: bordes del área del marcador
        Show(cardBook);
        Show(markerFrame);
        if (markerFrame) markerFrame.SetGlow(true);
        yield return Say(bookLine);
        Hide(markerFrame);

        // 3. Lee en voz alta: meteoro hacia el planeta
        Show(cardRead);
        Show(planet);
        Anim(animRead);
        meteorPending = meteor != null;
        if (meteorPending) StartCoroutine(MeteorAfter(MeteorDelay()));
        yield return Say(readLine);
        while (meteorPending || (meteor && meteor.IsPlaying)) yield return null;
        Anim(animRest);
        Hide(planet);

        // 4. ¿Una duda?: frase gatillo + ejemplo con "Dedicatoria"
        Show(cardDoubt);
        Anim(animDoubt);
        yield return Say(doubtLine);
        Show(wordHighlight);
        if (wordHighlight) wordHighlight.SetGlow(true);
        Show(definition);
        Anim(animTalk);   // explica "Dedicatoria": pasa a hablar
        yield return Say(doubtExample);
        Anim(animRest);
        Hide(wordHighlight);
        Hide(definition);
        yield return new WaitForSeconds(0.5f);

        // 5. Quiz (solo mirar)
        Show(quizPanel);
        Show(quizTitle);
        Show(question);
        foreach (var o in options) Show(o);
        Anim(animQuiz);
        yield return Say(quizLine);
        yield return Say(quizIntro);

        //    Intento incorrecto: la opción brilla y luego se apaga, sin rojo ni error.
        Anim(animThink);
        Glow(wrongOption, true);
        yield return new WaitForSeconds(choiceThinkTime);
        Glow(wrongOption, false);
        Dim(wrongOption);
        Anim(animWrong);
        yield return Say(quizWrong);

        //    Segundo intento correcto: brilla, crece, estrellas y celebración.
        Anim(animThink);
        Glow(rightOption, true);
        yield return new WaitForSeconds(choiceThinkTime);
        Glow(rightOption, false);
        Grow(rightOption);
        for (int i = 0; i < options.Length; i++) if (i != rightOption) Dim(i);
        if (stars && Valid(rightOption)) stars.Burst(options[rightOption].transform.position);
        Anim(animRight);
        yield return Say(quizRight);
        Anim(animRest);

        // 6. Cierre: espera la respuesta a "¿Estás listo?", invita a leer y deja la mesa limpia
        if (listener) yield return listener.WaitForResponse();
        if (HasContent(farewell)) yield return Say(farewell);
        HideAll(false);
        yield return FadePrincipito(0f, cleanupFade);

        IsRunning = false;
        onFinished?.Invoke();
        Debug.Log("[Onboarding] Terminado: mesa limpia.");
    }

    bool meteorPending;

    /// El meteoro sale cuando el Principito dice "¡Mira, ahí va un meteoro!".
    float MeteorDelay()
    {
        if (readLine != null && readLine.clip != null) return Mathf.Clamp(meteorStartTime, 0f, readLine.clip.length);
        // Sin audio: proporcional a la posición de la frase en el texto
        float total = Mathf.Clamp(readLine.text.Length * 0.065f, 1.5f, 10f);
        int i = readLine.text.IndexOf("¡Mira", StringComparison.Ordinal);
        return i > 0 ? total * i / readLine.text.Length : 0f;
    }

    IEnumerator MeteorAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        yield return meteor.Play();
        meteorPending = false;
    }

    // ───────────── Ayudas ─────────────
    bool speaking;

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
            speaking = false;
            StopTalking();
        }
        else
        {
            Debug.Log($"[Onboarding] (sin audio {line.id}) {line.text}");
            float dur = Mathf.Clamp(line.text.Length * 0.065f, 1.5f, 10f);
            for (float t = 0f; t < dur; t += Time.deltaTime) { TalkIfResting(); yield return null; }
            speaking = false;
            StopTalking();
        }
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

    static bool HasContent(Line l) => l != null && (l.clip != null || !string.IsNullOrEmpty(l.text));

    void Anim(string clip) { if (principito && !string.IsNullOrEmpty(clip)) principito.Play(clip); }

    bool Valid(int i) => options != null && i >= 0 && i < options.Length && options[i] != null;
    void Glow(int i, bool on) { if (Valid(i)) options[i].SetGlow(on); }
    void Dim(int i) { if (Valid(i)) options[i].Dim(); }
    void Grow(int i) { if (Valid(i)) options[i].SetGrow(true); }

    static void Show(ProjectedCard c) { if (c) c.Show(); }
    static void Hide(ProjectedCard c) { if (c) c.Hide(); }

    void HideAll(bool instant)
    {
        foreach (var c in new[] { cardBook, markerFrame, cardRead, planet, cardDoubt, wordHighlight, definition, quizPanel, quizTitle, question })
            if (c) { c.ResetState(); c.Hide(instant); }
        if (options != null) foreach (var o in options) if (o) { o.ResetState(); o.Hide(instant); }
    }

    // Compatibilidad con el generador anterior (ya no se usa en este flujo)
    [HideInInspector] public Line closing = new Line { id = "", text = "" };
}
