using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Onboarding de StoryScape (se activa al escanear el ArUco de la primera página).
/// Principito a la izquierda, contenido a la derecha, libro físico al centro.
///
/// 1. Saludo y "¿Cómo te llamas?" → espera la respuesta del niño.
/// 2. Tres reglas, que aparecen escritas una por una (regla 1: Principito escucha; regla 2: pregunta).
/// 3. Ejemplo del juego de preguntas: intento incorrecto (la opción se apaga, "¡Casi! Otra vez")
///    y correcto (la opción crece, estrellas, "¡Muy bien!").
/// 4. "¿Estás listo? ¡Empecemos!" → espera la respuesta → onFinished.
///
/// Fuera de las reglas 1 y 2 y de las reacciones del ejemplo (correcto / incorrecto),
/// el Principito se mantiene en reposo (alterna Idle y Nod).
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
    public bool startOnPlay = true;
    public float startDelay = 1f;

    [Header("Personaje y voz")]
    public SpriteAnimator2D principito;
    public AudioSource voice;
    public VoiceActivityWaiter listener;

    [Header("Reglas (lado derecho)")]
    public ProjectedCard rulesTitle;
    public ProjectedCard[] rules = new ProjectedCard[3];

    [Header("Ejemplo del juego de preguntas")]
    public ProjectedCard question;
    [Tooltip("0 = Planeta #4FB3E8 (en silencio), 1 = Rosa #F2727F (en voz alta), 2 = Estrella #F7C948 (cantando)")]
    public ProjectedCard[] options = new ProjectedCard[3];
    public int wrongOption = 0;
    public int rightOption = 1;
    public ProjectedCard bubble;
    public StarBurst stars;

    [Header("Animaciones del Principito (nombres de clips)")]
    [Tooltip("Reposo: se usa en todo momento salvo en las reglas 1 y 2 (alterna Idle y Nod)")]
    public string animRest = "Reposo";
    [Tooltip("Mientras se presenta la regla 1 (lee en voz alta)")]
    public string animRule1 = "Listening";
    [Tooltip("Mientras se presenta la regla 2 (pídeme ayuda)")]
    public string animRule2 = "Asking";
    [Tooltip("Mientras se presenta la regla 3 (vacío = reposo)")]
    public string animRule3 = "";
    [Tooltip("Reacción a la respuesta incorrecta del ejemplo (desaprobación)")]
    public string animWrong = "Incorrecto";
    [Tooltip("Reacción a la respuesta correcta del ejemplo (alegría)")]
    public string animRight = "Correcto";

    [Header("Guion (audios en Assets/Onboarding/Audio con el mismo id)")]
    public Line intro = new Line { id = "01_intro", text = "¡Hola! ¡Bienvenido al libro El Principito! ¿Cómo te llamas?" };
    public Line nameReply = new Line { id = "02_nombre", text = "¡Qué lindo nombre! Antes de empezar nuestra aventura, te voy a contar tres reglas muy fáciles." };
    public Line rule1 = new Line { id = "03_regla1", text = "Uno: lee en voz alta. Así yo puedo escucharte y acompañarte en la lectura." };
    public Line rule2 = new Line { id = "04_regla2", text = "Dos: si tienes una duda, pídeme ayuda. Solo tienes que decir: Principito, tengo una duda, y yo te ayudaré." };
    public Line rule3 = new Line { id = "05_regla3", text = "Tres: al terminar cada capítulo, jugaremos a las preguntas. Te haré dos preguntas sobre lo que leíste. ¡No te preocupes, no es un examen, es un juego!" };
    public Line quizIntro = new Line { id = "06_juego_intro", text = "Mira, así se verá el juego. Por ejemplo: ¿cómo vas a leer el cuento? ¿En silencio, en voz alta o cantando?" };
    public Line quizWrong = new Line { id = "07_juego_casi", text = "Si eliges una respuesta que no es, no pasa nada. ¡Puedes intentarlo otra vez!" };
    public Line quizRight = new Line { id = "08_juego_bien", text = "Y cuando aciertes, ¡lo celebramos juntos! ¿Estás listo? ¡Empecemos!" };
    [Tooltip("Opcional: línea de cierre aparte (vacía = no se usa; el cierre está incluido en 08_juego_bien)")]
    public Line closing = new Line { id = "", text = "" };
    public Line farewell = new Line { id = "09_final", text = "¡Genial! Pasa la página y empieza a leer en voz alta." };

    [Header("Tiempos")]
    public float pauseBetweenLines = 0.35f;
    public float choiceThinkTime = 1.4f;

    [Header("Eventos")]
    public UnityEvent onStarted;
    public UnityEvent onFinished;

    public bool IsRunning { get; private set; }

    void Start()
    {
        HideAll(true);
        Anim(animRest);
        if (startOnPlay) Invoke(nameof(IniciarOnboarding), startDelay);
    }

    /// Punto de entrada: lo llamará la detección del ArUco de la primera página.
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
        Anim(animRest);

        // 1. Saludo y nombre
        yield return Say(intro);
        if (listener) yield return listener.WaitForResponse();
        yield return Say(nameReply);

        // 2. Reglas
        Show(rulesTitle);
        Show(rules, 0); Anim(animRule1);
        yield return Say(rule1);
        Show(rules, 1); Anim(animRule2);
        yield return Say(rule2);
        Show(rules, 2); Anim(string.IsNullOrEmpty(animRule3) ? animRest : animRule3);
        yield return Say(rule3);
        Anim(animRest);

        // 3. Ejemplo del juego de preguntas
        Hide(rulesTitle);
        foreach (var r in rules) Hide(r);
        yield return new WaitForSeconds(0.8f);

        Show(question);
        foreach (var o in options) Show(o);
        yield return Say(quizIntro);

        //    Intento incorrecto: la opción elegida brilla y luego se apaga, sin rojo ni error.
        Glow(wrongOption, true);
        yield return new WaitForSeconds(choiceThinkTime);
        Glow(wrongOption, false);
        Dim(wrongOption);
        SetBubble("¡Casi! Otra vez");
        Anim(animWrong);
        yield return Say(quizWrong);
        Anim(animRest);
        Hide(bubble);

        //    Segundo intento correcto: brilla, crece, estrellas y celebración.
        Glow(rightOption, true);
        yield return new WaitForSeconds(choiceThinkTime);
        Glow(rightOption, false);
        Grow(rightOption);
        for (int i = 0; i < options.Length; i++) if (i != rightOption) Dim(i);
        if (stars && Valid(rightOption)) stars.Burst(options[rightOption].transform.position);
        SetBubble("¡Muy bien!");
        Anim(animRight);
        yield return Say(quizRight);
        Anim(animRest);

        // 4. Cierre
        Hide(bubble);
        Hide(question);
        foreach (var o in options) Hide(o);
        yield return new WaitForSeconds(0.6f);
        if (HasContent(closing)) yield return Say(closing);
        if (listener) yield return listener.WaitForResponse();   // respuesta a "¿Estás listo?"
        if (HasContent(farewell)) yield return Say(farewell);

        IsRunning = false;
        onFinished?.Invoke();
        Debug.Log("[Onboarding] Terminado.");
    }

    // ───────────── Ayudas ─────────────
    IEnumerator Say(Line line)
    {
        if (line == null) yield break;
        if (line.clip != null && voice != null)
        {
            voice.clip = line.clip;
            voice.Play();
            yield return null;
            while (voice.isPlaying) yield return null;
        }
        else
        {
            Debug.Log($"[Onboarding] (sin audio {line.id}) {line.text}");
            yield return new WaitForSeconds(Mathf.Clamp(line.text.Length * 0.065f, 1.5f, 10f));
        }
        yield return new WaitForSeconds(pauseBetweenLines);
    }

    static bool HasContent(Line l) => l != null && (l.clip != null || !string.IsNullOrEmpty(l.text));

    void Anim(string clip) { if (principito && !string.IsNullOrEmpty(clip)) principito.Play(clip); }

    bool Valid(int i) => options != null && i >= 0 && i < options.Length && options[i] != null;
    void Glow(int i, bool on) { if (Valid(i)) options[i].SetGlow(on); }
    void Dim(int i) { if (Valid(i)) options[i].Dim(); }
    void Grow(int i) { if (Valid(i)) options[i].SetGrow(true); }

    static void Show(ProjectedCard c) { if (c) c.Show(); }
    static void Hide(ProjectedCard c) { if (c) c.Hide(); }
    static void Show(ProjectedCard[] arr, int i) { if (arr != null && i < arr.Length && arr[i]) arr[i].Show(); }

    void SetBubble(string text)
    {
        if (!bubble) return;
        if (bubble.label) bubble.label.text = text;
        bubble.Show();
    }

    void HideAll(bool instant)
    {
        if (rulesTitle) rulesTitle.Hide(instant);
        if (rules != null) foreach (var r in rules) if (r) r.Hide(instant);
        if (question) question.Hide(instant);
        if (options != null) foreach (var o in options) if (o) { o.ResetState(); o.Hide(instant); }
        if (bubble) bubble.Hide(instant);
    }
}
