using UnityEngine;

/// <summary>
/// Elemento proyectado (tarjeta, opción, globo de diálogo o texto) con transiciones suaves:
/// aparecer/desaparecer, atenuar, brillar (opción elegida) y crecer (respuesta correcta).
/// Fondo y borde son opcionales: sin ellos funciona como texto que aparece con fundido.
/// </summary>
public class ProjectedCard : MonoBehaviour
{
    [Header("Partes (opcionales)")]
    public SpriteRenderer fill;
    public SpriteRenderer border;
    public TMPro.TextMeshPro label;

    [Header("Estilo")]
    public Color color = Color.white;
    [Range(0f, 1f)] public float fillAlpha = 0.22f;
    public Color textColor = Color.white;
    public float fadeSpeed = 3f;

    [Header("Estado inicial")]
    public bool startVisible = false;

    float alpha, targetAlpha;
    float glow, targetGlow;
    float scaleMul = 1f, targetScaleMul = 1f;
    Vector3 baseScale;

    public bool IsVisible => targetAlpha > 0.5f;

    void Awake()
    {
        baseScale = transform.localScale;
        alpha = targetAlpha = startVisible ? 1f : 0f;
        Apply();
    }

    public void Show(bool instant = false) { targetAlpha = 1f; if (instant) alpha = 1f; }
    public void Hide(bool instant = false)
    {
        targetAlpha = 0f; targetGlow = 0f; targetScaleMul = 1f;
        if (instant) { alpha = 0f; glow = 0f; scaleMul = 1f; Apply(); }
    }
    /// Se apaga casi del todo (opción descartada), sin rojo ni error.
    public void Dim() { targetAlpha = 0.18f; targetGlow = 0f; targetScaleMul = 1f; }
    /// Brilla (opción elegida).
    public void SetGlow(bool on) { targetGlow = on ? 1f : 0f; }
    /// Crece (respuesta correcta).
    public void SetGrow(bool on) { targetScaleMul = on ? 1.15f : 1f; }

    public void ResetState()
    {
        glow = targetGlow = 0f;
        scaleMul = targetScaleMul = 1f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        alpha = Mathf.MoveTowards(alpha, targetAlpha, fadeSpeed * dt);
        glow = Mathf.MoveTowards(glow, targetGlow, 4f * dt);
        scaleMul = Mathf.Lerp(scaleMul, targetScaleMul, 8f * dt);
        Apply();
    }

    void Apply()
    {
        float pulse = 1f + 0.12f * glow * Mathf.Sin(Time.time * 7f);

        if (fill)
        {
            float a = Mathf.Lerp(fillAlpha, Mathf.Min(1f, fillAlpha + 0.4f), glow) * alpha;
            fill.color = new Color(color.r, color.g, color.b, a);
        }
        if (border)
        {
            Color bc = Color.Lerp(color, Color.white, 0.45f * glow);
            border.color = new Color(bc.r, bc.g, bc.b, Mathf.Clamp01(alpha * Mathf.Lerp(0.85f, 1f, glow) * pulse));
        }
        if (label)
            label.color = new Color(textColor.r, textColor.g, textColor.b, alpha);

        if (baseScale == Vector3.zero) baseScale = transform.localScale;
        transform.localScale = baseScale * scaleMul * (1f + 0.03f * glow * Mathf.Sin(Time.time * 7f));
    }
}
