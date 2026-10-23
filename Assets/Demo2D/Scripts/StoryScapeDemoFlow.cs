using UnityEngine;

/// <summary>
/// Flujo de la demo StoryScape:
/// 1) Proyecta el resaltado del término actual sobre el libro.
/// 2) El personaje camina hasta su punto final (CharacterPathWalker2D).
/// 3) Al llegar, inicia la conversación con ElevenLabs sobre ese término.
/// 4) Al terminar la conversación, el resaltado se apaga.
/// </summary>
public class StoryScapeDemoFlow : MonoBehaviour
{
    [Header("Contenido")]
    public TermBank bank;
    [Tooltip("Término del banco que se usa en esta demo (más adelante: según la página detectada)")]
    public int termIndex = 0;

    [Header("Referencias")]
    public CharacterPathWalker2D walker;
    public ElevenLabsAgentClient agent;
    public SpriteRenderer highlight;

    [Header("Resaltado")]
    public Color highlightColor = new Color(1f, 0.9f, 0.3f, 0.45f);
    public float pulseSpeed = 2.5f;

    bool highlightOn;

    TermEntry Current =>
        bank != null && bank.terms.Count > 0 ? bank.terms[Mathf.Clamp(termIndex, 0, bank.terms.Count - 1)] : null;

    void Start()
    {
        ApplyHighlight();
        if (walker) walker.onArrived.AddListener(OnArrived);
        if (agent) agent.onConversationEnded.AddListener(OnConversationEnded);
    }

    void OnDestroy()
    {
        if (walker) walker.onArrived.RemoveListener(OnArrived);
        if (agent) agent.onConversationEnded.RemoveListener(OnConversationEnded);
    }

    void ApplyHighlight()
    {
        var t = Current;
        if (!highlight) return;
        if (t == null)
        {
            highlightOn = false;
            SetAlpha(0f);
            return;
        }
        highlight.transform.position = new Vector3(t.highlightPosition.x, t.highlightPosition.y, 0f);
        highlight.transform.localScale = new Vector3(t.highlightSize.x, t.highlightSize.y, 1f);
        highlightOn = true;
    }

    void Update()
    {
        if (!highlight) return;
        if (highlightOn)
            SetAlpha(highlightColor.a * (0.75f + 0.25f * Mathf.Sin(Time.time * pulseSpeed)));
    }

    void SetAlpha(float a)
    {
        var c = highlightColor;
        c.a = a;
        highlight.color = c;
    }

    void OnArrived()
    {
        var t = Current;
        if (t == null || !agent)
        {
            Debug.LogWarning("[StoryScape] No hay término o agente configurado.");
            return;
        }
        agent.StartConversation(t.term, t.meaning);
    }

    void OnConversationEnded()
    {
        highlightOn = false;
        SetAlpha(0f);
    }

    // Permite ver el resaltado al editar posiciones en el banco sin entrar a Play.
    void OnValidate()
    {
        if (!Application.isPlaying) ApplyHighlightPreview();
    }

    void ApplyHighlightPreview()
    {
        var t = Current;
        if (!highlight || t == null) return;
        highlight.transform.position = new Vector3(t.highlightPosition.x, t.highlightPosition.y, 0f);
        highlight.transform.localScale = new Vector3(t.highlightSize.x, t.highlightSize.y, 1f);
        highlight.color = highlightColor;
    }
}
