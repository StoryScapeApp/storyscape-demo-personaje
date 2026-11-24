using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "…aprendí a pilotar aviones. He volado un poco por todo el mundo." (fin del capítulo I):
///  1. El piloto arranca en su avión en el piso (cuadros de suelo: la hélice empieza a girar) y despega.
///  2. Vuela por un camino suave (hijos Punto_0, Punto_1… que se pueden mover en la escena), inclinándose un poco
///     al subir y achicándose al acercarse al mundo.
///  3. Al llegar aparece el mundo (hijo Mundo) y el avión le da la vuelta (cuadros del mundo); se queda en el último.
/// El último punto del camino coincide con el avión del primer cuadro del mundo, así el cambio no se nota.
/// Se va solo al pasar a otra página o con un alumno nuevo.
/// </summary>
public class AvionVueltaMundo : MonoBehaviour
{
    [Header("Objetos")]
    [Tooltip("Hijo con el SpriteRenderer del avión (se mueve por el camino)")]
    public Transform avion;
    [Tooltip("Hijo con el SpriteRenderer del mundo (fijo)")]
    public Transform mundo;

    [Header("Cuadros")]
    [Tooltip("En el piso: la hélice empieza a girar y despega")]
    public Sprite[] suelo;
    [Tooltip("En el aire (se repiten mientras vuela)")]
    public Sprite[] vuelo;
    [Tooltip("La vuelta al mundo (el último queda en pantalla)")]
    public Sprite[] vueltaMundo;
    public float fpsSuelo = 3f;
    public float fpsVuelo = 4f;
    public float fpsMundo = 2f;

    [Header("Vuelo")]
    [Tooltip("Puntos del camino en orden (vacío = hijos Punto_0, Punto_1…)")]
    public Transform[] puntos;
    [Tooltip("Unidades de escena por segundo")]
    public float velocidad = 2.2f;
    [Tooltip("Inclinación máxima del avión al subir o bajar (grados)")]
    public float inclinacionMax = 25f;
    [Tooltip("Tamaño del avión al llegar al mundo (1 = como despegó). Debe coincidir con el avión del primer cuadro del mundo")]
    public float escalaFinal = 0.64f;
    [Tooltip("Parte final del camino (0–1) en la que se va achicando")]
    public float tramoAchicar = 0.35f;
    public float aparecer = 0.4f;
    public float desaparecer = 0.6f;
    public float fundidoMundo = 0.35f;

    [Header("Cuándo se va")]
    [Tooltip("Página del libro (marker + 1): al pasar a otra, se va")]
    public int pagina = 3;
    public BookPageTracker tracker;
    public OnboardingSequence onboarding;

    public bool Visible { get; private set; }
    public bool IsPlaying { get; private set; }

    SpriteRenderer srAvion, srMundo;
    Coroutine rutina;
    bool saliendo;
    readonly List<Vector3> muestras = new List<Vector3>();
    readonly List<float> largo = new List<float>();

    void Awake()
    {
        if (!avion) avion = transform.Find("Avion");
        if (!mundo) mundo = transform.Find("Mundo");
        srAvion = avion.GetComponent<SpriteRenderer>();
        srMundo = mundo.GetComponent<SpriteRenderer>();
        SetAlpha(srAvion, 0f); SetAlpha(srMundo, 0f);
        if (puntos == null || puntos.Length == 0)
        {
            var l = new List<Transform>();
            for (int i = 0; ; i++) { var t = transform.Find("Punto_" + i); if (!t) break; l.Add(t); }
            puntos = l.ToArray();
        }
        if (!tracker) tracker = FindFirstObjectByType<BookPageTracker>();
        if (!onboarding) onboarding = FindFirstObjectByType<OnboardingSequence>();
    }

    [ContextMenu("Play")]
    public void Play()
    {
        if (IsPlaying || puntos == null || puntos.Length < 2) return;
        if (rutina != null) StopCoroutine(rutina);
        rutina = StartCoroutine(Volar());
    }

    [ContextMenu("Ocultar")]
    public void Ocultar()
    {
        if (!Visible || saliendo) return;
        if (rutina != null) StopCoroutine(rutina);
        rutina = StartCoroutine(Irse());
    }

    void Update()
    {
        if (!Visible || saliendo) return;
        bool otraPagina = tracker && pagina > 0 && tracker.CurrentPage != pagina;
        bool alumnoNuevo = onboarding && onboarding.IsRunning;
        if (otraPagina || alumnoNuevo) Ocultar();
    }

    IEnumerator Volar()
    {
        Visible = true; IsPlaying = true; saliendo = false;
        ArmarCamino();
        float total = largo[largo.Count - 1];
        avion.position = Punto(0f);
        avion.rotation = Quaternion.identity;
        avion.localScale = Vector3.one;
        SetAlpha(srMundo, 0f);

        // 1. En el piso: aparece, la hélice arranca y despega
        if (suelo != null && suelo.Length > 0)
        {
            srAvion.sprite = suelo[0];
            for (float t = 0f; t < aparecer; t += Time.deltaTime) { SetAlpha(srAvion, t / aparecer); yield return null; }
            SetAlpha(srAvion, 1f);
            float paso = 1f / Mathf.Max(0.5f, fpsSuelo);
            yield return new WaitForSeconds(paso);
            for (int i = 1; i < suelo.Length; i++) { srAvion.sprite = suelo[i]; yield return new WaitForSeconds(paso); }
        }
        else SetAlpha(srAvion, 1f);

        // 2. Vuelo por el camino
        float duracion = total / Mathf.Max(0.1f, velocidad);
        float ang = 0f;
        for (float t = 0f; t < duracion; t += Time.deltaTime)
        {
            float k = t / duracion, d = k * total;
            avion.position = Punto(d);
            float objetivo = Mathf.Clamp(Mathf.DeltaAngle(0f, Angulo(d)), -inclinacionMax, inclinacionMax);
            ang = Mathf.MoveTowardsAngle(ang, objetivo, 90f * Time.deltaTime);
            avion.rotation = Quaternion.Euler(0f, 0f, ang);
            float achicar = Mathf.InverseLerp(1f - tramoAchicar, 1f, k);
            avion.localScale = Vector3.one * Mathf.Lerp(1f, escalaFinal, achicar * achicar * (3f - 2f * achicar));
            if (vuelo != null && vuelo.Length > 0) srAvion.sprite = vuelo[(int)(t * fpsVuelo) % vuelo.Length];
            yield return null;
        }
        avion.position = Punto(total);

        // 3. Aparece el mundo y el avión le da la vuelta
        if (vueltaMundo != null && vueltaMundo.Length > 0)
        {
            srMundo.sprite = vueltaMundo[0];
            for (float t = 0f; t < fundidoMundo; t += Time.deltaTime)
            {
                float k = t / fundidoMundo;
                SetAlpha(srMundo, k); SetAlpha(srAvion, 1f - k);
                avion.rotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(ang, 0f, k));
                yield return null;
            }
            SetAlpha(srMundo, 1f); SetAlpha(srAvion, 0f);
            float paso = 1f / Mathf.Max(0.3f, fpsMundo);
            for (int i = 1; i < vueltaMundo.Length; i++) { yield return new WaitForSeconds(paso); srMundo.sprite = vueltaMundo[i]; }
        }
        IsPlaying = false;
        rutina = null;
    }

    IEnumerator Irse()
    {
        saliendo = true; IsPlaying = false;
        float a0 = srAvion.color.a, m0 = srMundo.color.a;
        for (float t = 0f; t < desaparecer; t += Time.deltaTime)
        {
            float k = t / desaparecer;
            SetAlpha(srAvion, Mathf.Lerp(a0, 0f, k)); SetAlpha(srMundo, Mathf.Lerp(m0, 0f, k));
            yield return null;
        }
        SetAlpha(srAvion, 0f); SetAlpha(srMundo, 0f);
        Visible = false; saliendo = false;
        rutina = null;
    }

    // ───────────── Camino (Catmull-Rom, velocidad constante) ─────────────
    void ArmarCamino()
    {
        muestras.Clear(); largo.Clear();
        int n = puntos.Length;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = puntos[Mathf.Max(0, i - 1)].position, p1 = puntos[i].position, p2 = puntos[i + 1].position, p3 = puntos[Mathf.Min(n - 1, i + 2)].position;
            for (int k = 0; k < 24; k++) muestras.Add(CatmullRom(p0, p1, p2, p3, k / 24f));
        }
        muestras.Add(puntos[n - 1].position);
        float acc = 0f; largo.Add(0f);
        for (int i = 1; i < muestras.Count; i++) { acc += Vector3.Distance(muestras[i - 1], muestras[i]); largo.Add(acc); }
    }

    Vector3 Punto(float d)
    {
        if (muestras.Count == 0) return avion.position;
        if (d <= 0f) return muestras[0];
        for (int i = 1; i < largo.Count; i++)
            if (largo[i] >= d)
            {
                float seg = largo[i] - largo[i - 1];
                return Vector3.Lerp(muestras[i - 1], muestras[i], seg > 1e-5f ? (d - largo[i - 1]) / seg : 0f);
            }
        return muestras[muestras.Count - 1];
    }

    float Angulo(float d)
    {
        Vector3 v = Punto(d + 0.25f) - Punto(Mathf.Max(0f, d - 0.25f));
        return v.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    static void SetAlpha(SpriteRenderer sr, float a) { if (!sr) return; var c = sr.color; c.a = a; sr.color = c; }

    void OnDrawGizmosSelected()
    {
        if (puntos == null || puntos.Length < 2) return;
        Gizmos.color = new Color(0.2f, 0.5f, 1f);
        for (int i = 0; i < puntos.Length - 1; i++) if (puntos[i] && puntos[i + 1]) Gizmos.DrawLine(puntos[i].position, puntos[i + 1].position);
    }
}
