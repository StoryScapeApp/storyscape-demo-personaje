using System.Collections;
using UnityEngine;

/// <summary>
/// Modo "Principito, tengo una duda".
/// 1. Al abrir la conversación, el Principito aparece y saluda.
/// 2. El niño pregunta por una palabra o frase. El agente llama a la herramienta de cliente
///    "resaltar_palabra" (palabra, significado) → se resalta en el libro y el significado aparece
///    a la derecha del libro mientras el Principito lo explica.
/// 3. Al terminar la conversación (end_call), se apaga todo y el Principito desaparece.
///
/// Se abre con la frase "Principito, tengo una duda" (TriggerPhraseListener), con el mensaje UDP
/// {"tipo":"duda"} desde Python, o para probar con la tecla D (Esc cierra).
/// </summary>
public class DoubtSession : MonoBehaviour
{
    [Header("Referencias")]
    public ElevenLabsAgentClient agent;
    public BookPageTracker tracker;
    public WordHighlighter highlighter;
    public SpriteAnimator2D principito;
    [Tooltip("Tarjeta a la derecha del libro donde se escribe el significado")]
    public ProjectedCard definition;
    [Tooltip("Opcional: abre las dudas al recibir {\"tipo\":\"duda\"} por UDP (detección de la frase en Python)")]
    public ArucoUdpReceiver receiver;
    public string udpCommand = "duda";

    [Header("Herramienta del agente")]
    public string toolName = "resaltar_palabra";

    [Header("Animaciones")]
    public string animGreet = "Saludo";
    public string animRest = "Reposo";
    public string animTalk = "Hablando";
    public float fadeTime = 0.6f;
    [Tooltip("Oculta al Principito mientras no hay una conversación de dudas")]
    public bool hideWhenIdle = true;

    [Header("Prueba")]
    [Tooltip("D abre la conversación de dudas y Esc la cierra (mientras no exista la detección de la frase)")]
    public bool keyboardShortcuts = true;
    [Tooltip("Tecla T: prueba el resaltado SIN conectarse a ElevenLabs, recorriendo esta lista (palabra|significado)")]
    public string[] testWords =
    {
        "excusa|Razón que das para explicar por qué hiciste algo.",
        "persona mayor|Una persona adulta, como tus papás o tus profesores.",
        "consuelo|Cariño o ayuda que hace sentir mejor a alguien que está triste.",
        "dedicatoria|Palabras al inicio de un libro para regalárselo a alguien especial.",
        "comprenderlo todo|Entender todas las cosas, hasta las más difíciles.",
        "recuerdan|Que todavía tienen algo en su memoria.",
        "elefante|(No está en la página: solo debe aparecer el significado.)",
    };
    int testIndex;
    Coroutine testCo;

    SpriteRenderer principitoSr;
    Coroutine fadeCo;

    void Awake()
    {
        if (principito) principitoSr = principito.GetComponent<SpriteRenderer>();
        if (definition && definition.label)
        {
            definition.label.enableAutoSizing = true;
            definition.label.fontSizeMin = 1.3f;
            definition.label.fontSizeMax = definition.label.fontSize;
        }
    }

    void OnEnable()
    {
        if (receiver) receiver.CommandReceived += OnUdpCommand;
        if (!agent) return;
        agent.ClientToolHandler = OnClientTool;
        agent.onConversationStarted.AddListener(OnStarted);
        agent.onConversationEnded.AddListener(OnEnded);
    }

    void OnDisable()
    {
        if (receiver) receiver.CommandReceived -= OnUdpCommand;
        if (!agent) return;
        agent.ClientToolHandler = null;
        agent.onConversationStarted.RemoveListener(OnStarted);
        agent.onConversationEnded.RemoveListener(OnEnded);
    }

    void Start()
    {
        if (hideWhenIdle) SetAlpha(0f);
        if (definition) definition.Hide(true);
        if (keyboardShortcuts) Debug.Log("[Dudas] Prueba: presiona D para abrir la conversación de dudas y Esc para cerrarla.");
    }

    void Update()
    {
        if (keyboardShortcuts)
        {
            if (KeyOpen()) AbrirDudas();
            if (KeyClose() && agent && agent.IsRunning) agent.EndConversation();
            if (KeyTest()) ProbarResaltado();
        }

        // Boca: habla mientras suena la voz del agente; en reposo mientras escucha al niño.
        if (principito && agent && agent.IsRunning)
        {
            string cur = principito.CurrentClip;
            if (agent.IsAgentTalking && cur == animRest) principito.Play(animTalk);
            else if (!agent.IsAgentTalking && cur == animTalk) principito.Play(animRest);
        }
    }

    void OnUdpCommand(string cmd)
    {
        if (string.Equals(cmd, udpCommand, System.StringComparison.OrdinalIgnoreCase)) AbrirDudas();
    }

    [ContextMenu("Abrir dudas")]
    public void AbrirDudas()
    {
        if (agent == null || agent.IsRunning) return;
        Debug.Log("[Dudas] Abriendo conversación de dudas.");
        if (tracker) tracker.AbrirConversacionDudas();
        else agent.StartConversation(new System.Collections.Generic.Dictionary<string, string>());
    }

    /// Prueba sin ElevenLabs: el Principito aparece, se resalta la siguiente palabra de la lista y se muestra su significado.
    [ContextMenu("Probar resaltado (siguiente palabra)")]
    public void ProbarResaltado()
    {
        if (agent && agent.IsRunning) return;
        if (testWords == null || testWords.Length == 0) return;
        var parts = testWords[testIndex % testWords.Length].Split('|');
        testIndex++;
        var args = new ElevenLabsAgentClient.ToolArgs { palabra = parts[0], significado = parts.Length > 1 ? parts[1] : "" };
        string result = OnClientTool(toolName, args);
        Debug.Log($"[Dudas] Prueba sin ElevenLabs → {result}");
        if (testCo != null) StopCoroutine(testCo);
        testCo = StartCoroutine(TestTalk());
    }

    IEnumerator TestTalk()
    {
        Fade(1f);
        if (principito) principito.Play(animTalk);
        yield return new WaitForSeconds(4f);
        if (principito) principito.Play(animRest);
        yield return new WaitForSeconds(4f);
        OnEnded();
        testCo = null;
    }

    // ───────────── Eventos del agente ─────────────
    void OnStarted()
    {
        Fade(1f);
        if (principito) principito.Play(string.IsNullOrEmpty(animGreet) ? animRest : animGreet, true);
    }

    void OnEnded()
    {
        if (highlighter) highlighter.Clear();
        if (definition) definition.Hide();
        if (principito) principito.Play(animRest);
        if (hideWhenIdle) Fade(0f, 0.8f);
    }

    string OnClientTool(string name, ElevenLabsAgentClient.ToolArgs args)
    {
        if (name != toolName) return null;

        string palabra = (args.palabra ?? "").Trim();
        string significado = (args.significado ?? "").Trim();
        int page = tracker ? tracker.CurrentPage : 1;

        int found = highlighter && palabra.Length > 0 ? highlighter.Highlight(page, palabra) : 0;
        ShowDefinition(palabra, significado);

        return found > 0
            ? $"Listo: \"{palabra}\" está resaltada en el libro y su significado se muestra al lado. Ahora explícala."
            : $"\"{palabra}\" no se encontró en la página {page}; solo se muestra el significado. Explícala igual.";
    }

    void ShowDefinition(string palabra, string significado)
    {
        if (definition == null) return;
        if (definition.label)
        {
            string title = palabra.Length > 0 ? char.ToUpper(palabra[0]) + palabra.Substring(1) : "";
            definition.label.text = $"<b><size=120%>{title}:</size></b>\n{significado}";
        }
        definition.ResetState();
        definition.Show();
    }

    // ───────────── Aparecer / desaparecer ─────────────
    void Fade(float to, float delay = 0f)
    {
        if (principitoSr == null) return;
        if (fadeCo != null) StopCoroutine(fadeCo);
        fadeCo = StartCoroutine(FadeCo(to, delay));
    }

    IEnumerator FadeCo(float to, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float from = principitoSr.color.a;
        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            SetAlpha(Mathf.Lerp(from, to, t / fadeTime));
            yield return null;
        }
        SetAlpha(to);
        fadeCo = null;
    }

    void SetAlpha(float a)
    {
        if (principitoSr == null) return;
        var c = principitoSr.color; c.a = a; principitoSr.color = c;
    }

    // ───────────── Teclado ─────────────
    static bool KeyOpen()
    {
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        return k != null && k.dKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.D);
#endif
    }

    static bool KeyTest()
    {
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        return k != null && k.tKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.T);
#endif
    }

    static bool KeyClose()
    {
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        return k != null && k.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}
