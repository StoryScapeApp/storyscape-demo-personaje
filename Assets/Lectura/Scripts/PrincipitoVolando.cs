using System.Collections;
using UnityEngine;

/// <summary>
/// Animación "El Principito volando con las aves silvestres" (de derecha a izquierda, parte superior de la mesa).
/// 01: despega de su asteroide (a la derecha del libro) → 02: planea con las aves sobre el libro → 03: sale por la izquierda.
/// Los sprites tienen el pivote en la cabeza del Principito, así no salta al cambiar de pose.
/// Coordenadas en unidades de escena (cámara ortográfica de tamaño 4: x de -7.1 a 7.1, y de -4 a 4; libro: y de -1.87 a 1.87).
/// </summary>
public class PrincipitoVolando : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite pose01;
    public Sprite pose02;
    public Sprite pose03;
    public Sprite asteroide;
    public int sortingOrder = 20;

    [Header("Recorrido (posición de la cabeza del Principito)")]
    public Vector2 despegue = new Vector2(5.75f, 0.35f);
    public Vector2 asteroidePos = new Vector2(5.25f, -1.25f);
    [Tooltip("Altura del vuelo sobre el libro (el libro termina en y = 1.87)")]
    public float alturaVuelo = 2.75f;
    public float inicioPlaneo = 4.4f;
    [Tooltip("x donde cambia a la pose 03 (su borde izquierdo ya queda fuera de la proyección)")]
    public float finPlaneo = -6.4f;
    public float salida = -9.8f;

    [Header("Tiempos (s)")]
    public float aparecer = 0.6f;
    public float espera = 0.6f;
    public float despegar = 1.4f;
    public float planear = 5.5f;
    public float salir = 1.3f;
    public float cambioPose = 0.3f;
    [Tooltip("Ondulación vertical mientras planea")]
    public float ondulacion = 0.1f;

    public bool IsPlaying { get; private set; }

    SpriteRenderer a, b, ast;
    Vector2 head;

    void Awake()
    {
        a = NewRenderer("Pose_A", sortingOrder);
        b = NewRenderer("Pose_B", sortingOrder);
        ast = NewRenderer("Asteroide", sortingOrder - 1);
        HideAll();
    }

    SpriteRenderer NewRenderer(string n, int order)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        return sr;
    }

    [ContextMenu("Reproducir")]
    public void Play()
    {
        if (IsPlaying || !gameObject.activeInHierarchy) return;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        IsPlaying = true;
        Debug.Log("[Animación] Principito volando con las aves.");

        // 01 · aparece en su asteroide
        head = despegue;
        a.sprite = pose01; SetAlpha(a, 0f); SetAlpha(b, 0f);
        ast.sprite = asteroide; ast.transform.position = asteroidePos; SetAlpha(ast, 0f);
        Place();
        for (float t = 0f; t < aparecer; t += Time.deltaTime)
        {
            float k = t / aparecer;
            SetAlpha(a, k); SetAlpha(ast, k);
            yield return null;
        }
        SetAlpha(a, 1f); SetAlpha(ast, 1f);
        yield return new WaitForSeconds(espera);

        // 01 · despega hacia la parte superior de la mesa
        Vector2 from = head, to = new Vector2(inicioPlaneo, alturaVuelo);
        for (float t = 0f; t < despegar; t += Time.deltaTime)
        {
            float k = Smooth(t / despegar);
            head = Vector2.Lerp(from, to, k) + Vector2.up * Mathf.Sin(k * Mathf.PI) * 0.35f;
            SetAlpha(ast, 1f - Mathf.Clamp01((t / despegar - 0.3f) / 0.7f));
            Place();
            yield return null;
        }
        SetAlpha(ast, 0f);

        // 02 · planea con las aves sobre el libro
        StartCoroutine(SwapPose(pose02));
        from = head;
        for (float t = 0f; t < planear; t += Time.deltaTime)
        {
            float k = t / planear;
            head = new Vector2(Mathf.Lerp(from.x, finPlaneo, k), alturaVuelo + Mathf.Sin(t * Mathf.PI * 1.4f) * ondulacion);
            Place();
            yield return null;
        }

        // 03 · sale por la izquierda
        StartCoroutine(SwapPose(pose03));
        from = head;
        for (float t = 0f; t < salir; t += Time.deltaTime)
        {
            float k = t / salir;
            head = new Vector2(Mathf.Lerp(from.x, salida, k * k), Mathf.Lerp(from.y, alturaVuelo + 0.25f, k));
            Place();
            yield return null;
        }

        HideAll();
        IsPlaying = false;
    }

    /// Cambia de pose con un fundido corto (las dos poses comparten el pivote en la cabeza).
    IEnumerator SwapPose(Sprite next)
    {
        b.sprite = next;
        for (float t = 0f; t < cambioPose; t += Time.deltaTime)
        {
            float k = t / cambioPose;
            SetAlpha(a, 1f - k); SetAlpha(b, k);
            yield return null;
        }
        a.sprite = next; SetAlpha(a, 1f); SetAlpha(b, 0f);
    }

    void Place()
    {
        a.transform.position = head;
        b.transform.position = head;
    }

    void HideAll()
    {
        SetAlpha(a, 0f); SetAlpha(b, 0f); SetAlpha(ast, 0f);
    }

    static void SetAlpha(SpriteRenderer sr, float v)
    {
        if (!sr) return;
        var c = sr.color; c.a = v; sr.color = c;
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);
}
