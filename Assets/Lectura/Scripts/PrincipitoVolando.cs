using System.Collections;
using UnityEngine;

/// <summary>
/// Animación "El Principito volando con las aves" (primera frase activadora).
/// Sigue la hoja "Sprites Principito volando" en orden de lectura y su recorrido:
///   IN (abajo a la izquierda, junto al árbol) → 01-02 toma impulso → 03 despega → 04-05 → 06 se mantiene mientras
///   vuela por los puntos de vuelo (arriba, de izquierda a derecha) → OUT por la derecha.
/// Un solo ciclo: ningún cuadro se repite.
/// Los sprites tienen el pivote en la línea de suelo de la hoja, así la altura del salto dibujada se conserva.
/// Coordenadas en unidades de escena (cámara ortográfica de tamaño 4: x de -7.1 a 7.1, y de -4 a 4;
/// libro: x de -2.76 a 2.76, y de -1.87 a 1.87).
/// </summary>
public class PrincipitoVolando : MonoBehaviour
{
    [Header("Sprites (Sprites Principito volando: 6 cuadros en orden)")]
    public Sprite[] cuadros = new Sprite[6];
    public int sortingOrder = 20;

    [Header("Recorrido (posición de los pies / línea de suelo)")]
    [Tooltip("IN: donde aparece, de pie junto al árbol (abajo a la izquierda)")]
    public Vector2 entrada = new Vector2(-5.84f, -3.70f);
    [Tooltip("Fin del impulso en el suelo (cuadros 01-02)")]
    public Vector2 impulso = new Vector2(-5.40f, -3.70f);
    [Tooltip("Donde termina el despegue (cuadro 03): primera posición en el aire")]
    public Vector2 despegue = new Vector2(-4.40f, -0.08f);
    [Tooltip("Puntos por los que pasa volando, en orden (curva suave que pasa por cada uno)")]
    public Vector2[] puntosVuelo = { new Vector2(-0.63f, 1.19f), new Vector2(3.71f, 1.26f) };
    [Tooltip("OUT: fuera de la proyección por la derecha")]
    public Vector2 salida = new Vector2(9.8f, 1.5f);

    [Header("Tiempos (s)")]
    [Tooltip("Multiplica la velocidad de toda la animación (1 = normal, 2 = el doble de rápido)")]
    [Range(0.5f, 3f)] public float velocidad = 1.3f;
    public float aparecer = 0.35f;
    public float tomarImpulso = 1.0f;
    public float despegar = 1.0f;
    public float volar = 4.5f;
    [Tooltip("Cuadros por segundo al pasar de 04 a 06; después el 06 se mantiene hasta salir")]
    public float fpsVuelo = 4f;
    [Tooltip("Ondulación vertical mientras vuela")]
    public float ondulacion = 0.08f;

    public bool IsPlaying { get; private set; }

    SpriteRenderer sr;
    Vector2 pos;

    void Awake()
    {
        var go = new GameObject("Principito");
        go.transform.SetParent(transform, false);
        sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;
        SetAlpha(0f);
    }

    [ContextMenu("Reproducir")]
    public void Play()
    {
        if (IsPlaying || !gameObject.activeInHierarchy) return;
        if (cuadros == null || cuadros.Length < 6) { Debug.LogWarning("[Animación] Faltan los 6 cuadros del Principito volando."); return; }
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        IsPlaying = true;
        Debug.Log("[Animación] Principito volando con las aves.");

        // IN · aparece en la entrada (cuadro 01)
        pos = entrada; Frame(0); Place();
        for (float t = 0f; t < D(aparecer); t += Time.deltaTime) { SetAlpha(t / D(aparecer)); yield return null; }
        SetAlpha(1f);

        // 01-02 · toma impulso hacia la derecha (cada cuadro una sola vez)
        for (float t = 0f; t < D(tomarImpulso); t += Time.deltaTime)
        {
            float k = t / D(tomarImpulso);
            pos = Vector2.Lerp(entrada, impulso, Smooth(k));
            Frame(k < 0.5f ? 0 : 1);
            Place();
            yield return null;
        }

        // 03 · despega
        Frame(2);
        for (float t = 0f; t < D(despegar); t += Time.deltaTime)
        {
            float k = t / D(despegar);
            pos = Vector2.Lerp(impulso, despegue, k * (2f - k));   // sale rápido y frena arriba
            Place();
            yield return null;
        }

        // 04-05 una sola vez, luego 06 se mantiene mientras se aleja volando por la derecha (OUT)
        for (float t = 0f; t < D(volar); t += Time.deltaTime)
        {
            float k = t / D(volar);
            float u = k * k * 0.35f + k * 0.65f;   // acelera un poco hacia la salida
            pos = Ruta(u) + Vector2.up * Mathf.Sin(t * velocidad * Mathf.PI * 1.6f) * ondulacion * (1f - k);
            Frame(Mathf.Min(3 + (int)(t * velocidad * fpsVuelo), 5));
            Place();
            yield return null;
        }

        SetAlpha(0f);
        IsPlaying = false;
    }

    void Frame(int i) { if (i < cuadros.Length && cuadros[i]) sr.sprite = cuadros[i]; }
    void Place() { sr.transform.position = pos; }
    void SetAlpha(float v) { if (!sr) return; var c = sr.color; c.a = v; sr.color = c; }

    /// Duración ajustada a la velocidad.
    float D(float seconds) => seconds / Mathf.Max(0.1f, velocidad);
    static float Smooth(float x) => x * x * (3f - 2f * x);
    // ───────────── Ruta de vuelo: despegue → puntos de vuelo → salida ─────────────
    /// Punto de la ruta para u de 0 a 1, a velocidad pareja (u proporcional a la distancia recorrida).
    Vector2 Ruta(float u)
    {
        var pts = PuntosRuta();
        const int pasos = 40;
        int segs = pts.Count - 1;
        // largo aproximado de cada tramo
        var largo = new float[segs]; float total = 0f;
        for (int s = 0; s < segs; s++)
        {
            Vector2 prev = pts[s];
            for (int i = 1; i <= pasos; i++) { var q = Tramo(pts, s, i / (float)pasos); largo[s] += (q - prev).magnitude; prev = q; }
            total += largo[s];
        }
        float d = Mathf.Clamp01(u) * total;
        for (int s = 0; s < segs; s++)
        {
            if (d <= largo[s] || s == segs - 1) return Tramo(pts, s, largo[s] > 0f ? Mathf.Clamp01(d / largo[s]) : 1f);
            d -= largo[s];
        }
        return pts[pts.Count - 1];
    }

    System.Collections.Generic.List<Vector2> PuntosRuta()
    {
        var pts = new System.Collections.Generic.List<Vector2> { despegue };
        if (puntosVuelo != null) pts.AddRange(puntosVuelo);
        pts.Add(salida);
        return pts;
    }

    /// Catmull-Rom: curva que pasa exactamente por cada punto.
    static Vector2 Tramo(System.Collections.Generic.List<Vector2> p, int s, float t)
    {
        Vector2 p0 = p[Mathf.Max(s - 1, 0)], p1 = p[s], p2 = p[s + 1], p3 = p[Mathf.Min(s + 2, p.Count - 1)];
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(entrada, impulso);
        Gizmos.DrawLine(impulso, despegue);
        Vector2 prev = despegue;
        for (int i = 1; i <= 60; i++) { var p = Ruta(i / 60f); Gizmos.DrawLine(prev, p); prev = p; }
        Gizmos.color = Color.yellow;
        if (puntosVuelo != null) foreach (var p in puntosVuelo) Gizmos.DrawWireSphere(p, 0.1f);
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(entrada, 0.12f);
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(salida, 0.12f);
    }
}
