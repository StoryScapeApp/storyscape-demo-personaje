using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serpiente boa del capítulo I (dos frases de la misma página):
///  1. Recorrer()  ("En el libro se decía: Las serpientes boas…"): aparece por la izquierda y repta por un camino
///     suave (hijos Punto_0, Punto_1… que se pueden mover en la escena) alrededor del libro, girando con el camino.
///     Al llegar al final queda quieta, horizontal y mirando a la derecha.
///  2. Digerir()   ("Representaba una serpiente boa que digería un elefante"): reproduce la hoja del elefante
///     (se ve dentro de la boa y luego sale) y se queda en el último cuadro.
///     Si se lee antes de que termine el recorrido, empieza al llegar; si la serpiente no había aparecido,
///     aparece directamente en el punto final.
/// Se va sola al pasar a otra página o con un alumno nuevo.
/// </summary>
public class SerpienteRecorrido : MonoBehaviour
{
    [Tooltip("Hijo con el SpriteRenderer que se mueve por el camino (los puntos quedan fijos)")]
    public Transform cuerpo;

    [Header("Cuadros")]
    public Sprite[] reptar;
    [Tooltip("El primero es la boa quieta (igual que al terminar de reptar)")]
    public Sprite[] digerir;
    public float fpsReptar = 8f;
    public float fpsDigerir = 2.2f;

    [Header("Recorrido")]
    [Tooltip("Puntos del camino en orden (vacío = hijos Punto_0, Punto_1…)")]
    public Transform[] puntos;
    [Tooltip("Unidades de escena por segundo")]
    public float velocidad = 1.8f;
    [Tooltip("La serpiente gira siguiendo el camino (cabeza arriba al subir, abajo al bajar)")]
    public bool girarConCamino = true;
    [Tooltip("Suavizado del giro (grados por segundo)")]
    public float velocidadGiro = 360f;
    public float aparecer = 0.5f;
    public float desaparecer = 0.6f;

    [Header("Cuándo se va")]
    [Tooltip("Página del libro (marker + 1): al pasar a otra, se va")]
    public int pagina = 2;
    public BookPageTracker tracker;
    public OnboardingSequence onboarding;

    public bool Visible { get; private set; }
    public bool Recorriendo { get; private set; }
    public bool Digiriendo { get; private set; }

    SpriteRenderer sr;
    Coroutine rutina;
    bool digerirPendiente, digerido, saliendo;
    readonly List<Vector3> muestras = new List<Vector3>();
    readonly List<float> largo = new List<float>();

    void Awake()
    {
        if (!cuerpo) cuerpo = transform.Find("Cuerpo");
        sr = cuerpo.GetComponent<SpriteRenderer>();
        SetAlpha(0f);
        if (puntos == null || puntos.Length == 0)
        {
            var l = new List<Transform>();
            for (int i = 0; ; i++) { var t = transform.Find("Punto_" + i); if (!t) break; l.Add(t); }
            puntos = l.ToArray();
        }
        if (!tracker) tracker = FindFirstObjectByType<BookPageTracker>();
        if (!onboarding) onboarding = FindFirstObjectByType<OnboardingSequence>();
    }

    // ───────────── API (ReadingSession → Animaciones → Al Activar) ─────────────
    [ContextMenu("Recorrer")]
    public void Recorrer()
    {
        if (reptar == null || reptar.Length == 0 || puntos == null || puntos.Length < 2) return;
        if (Recorriendo || digerido) return;
        if (rutina != null) StopCoroutine(rutina);
        rutina = StartCoroutine(Reptar());
    }

    [ContextMenu("Digerir")]
    public void Digerir()
    {
        if (digerido || Digiriendo) return;
        if (Recorriendo) { digerirPendiente = true; return; }   // empieza al llegar
        if (rutina != null) StopCoroutine(rutina);
        rutina = StartCoroutine(Digestion(!Visible));
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

    // ───────────── Animaciones ─────────────
    IEnumerator Reptar()
    {
        ArmarCamino();
        Visible = true; Recorriendo = true; saliendo = false; digerirPendiente = false;
        sr.flipY = false;
        float total = largo[largo.Count - 1];
        float duracion = total / Mathf.Max(0.1f, velocidad);
        float ang = Angulo(0.001f);
        cuerpo.position = Punto(0f);
        cuerpo.rotation = Quaternion.Euler(0f, 0f, girarConCamino ? ang : 0f);
        for (float t = 0f; t < duracion; t += Time.deltaTime)
        {
            float d = t / duracion * total;
            cuerpo.position = Punto(d);
            if (girarConCamino)
            {
                float objetivo = Angulo(d);
                ang = Mathf.MoveTowardsAngle(ang, objetivo, velocidadGiro * Time.deltaTime);
                cuerpo.rotation = Quaternion.Euler(0f, 0f, ang);
                sr.flipY = Mathf.Cos(ang * Mathf.Deg2Rad) < -0.2f;   // yendo hacia la izquierda: la panza sigue abajo
            }
            sr.sprite = reptar[(int)(t * fpsReptar) % reptar.Length];
            SetAlpha(Mathf.Clamp01(t / Mathf.Max(0.01f, aparecer)));
            yield return null;
        }
        // Llega: horizontal, mirando a la derecha, quieta (primer cuadro de la digestión = la boa quieta)
        cuerpo.position = Punto(total);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            ang = Mathf.LerpAngle(ang, 0f, t / 0.3f);
            cuerpo.rotation = Quaternion.Euler(0f, 0f, ang);
            yield return null;
        }
        cuerpo.rotation = Quaternion.identity;
        sr.flipY = false;
        SetAlpha(1f);
        sr.sprite = digerir != null && digerir.Length > 0 ? digerir[0] : reptar[0];
        Recorriendo = false;
        rutina = null;
        if (digerirPendiente) { digerirPendiente = false; rutina = StartCoroutine(Digestion(false)); }
    }

    IEnumerator Digestion(bool aparecerAhi)
    {
        if (digerir == null || digerir.Length == 0) yield break;
        Digiriendo = true; Visible = true; saliendo = false;
        if (aparecerAhi)
        {
            ArmarCamino();
            cuerpo.position = Punto(largo[largo.Count - 1]);
            cuerpo.rotation = Quaternion.identity; sr.flipY = false;
            sr.sprite = digerir[0];
            for (float t = 0f; t < aparecer; t += Time.deltaTime) { SetAlpha(t / aparecer); yield return null; }
            SetAlpha(1f);
            yield return new WaitForSeconds(0.4f);
        }
        float paso = 1f / Mathf.Max(0.3f, fpsDigerir);
        for (int i = 1; i < digerir.Length; i++) { sr.sprite = digerir[i]; yield return new WaitForSeconds(paso); }
        Digiriendo = false; digerido = true;
        rutina = null;
    }

    IEnumerator Irse()
    {
        saliendo = true; Recorriendo = false; Digiriendo = false; digerirPendiente = false;
        float from = sr.color.a;
        for (float t = 0f; t < desaparecer; t += Time.deltaTime) { SetAlpha(Mathf.Lerp(from, 0f, t / desaparecer)); yield return null; }
        SetAlpha(0f);
        Visible = false; saliendo = false; digerido = false;
        cuerpo.rotation = Quaternion.identity; sr.flipY = false;
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
        if (muestras.Count == 0) return cuerpo.position;
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
        Vector3 a = Punto(Mathf.Max(0f, d - 0.25f)), b = Punto(d + 0.25f);
        Vector3 v = b - a;
        return v.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    void SetAlpha(float a) { var c = sr.color; c.a = a; sr.color = c; }

    void OnDrawGizmosSelected()
    {
        if (puntos == null || puntos.Length < 2) return;
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        for (int i = 0; i < puntos.Length - 1; i++) if (puntos[i] && puntos[i + 1]) Gizmos.DrawLine(puntos[i].position, puntos[i + 1].position);
    }
}
