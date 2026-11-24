using System.Collections;
using UnityEngine;

/// <summary>
/// Personaje de una escena del libro que aparece al leer una frase (ReadingSession → Animaciones → Al Activar → Play):
/// aparece con el primer cuadro, lo sostiene un momento, reproduce los cuadros una vez (p. ej. el piloto que se
/// transforma en niño) y se queda en el último. Se va solo al pasar a otra página o con un alumno nuevo.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PersonajeEscena : MonoBehaviour
{
    [Tooltip("Cuadros en orden; el último queda en pantalla")]
    public Sprite[] cuadros;
    [Tooltip("Cuadros por segundo de la transformación")]
    public float fps = 4f;
    [Tooltip("Segundos que tarda en aparecer")]
    public float aparecer = 0.6f;
    [Tooltip("Segundos que se ve el primer cuadro antes de transformarse")]
    public float esperaInicio = 0.8f;
    [Tooltip("Segundos que tarda en irse")]
    public float desaparecer = 0.6f;

    [Header("Cuándo se va")]
    [Tooltip("Página del libro en la que aparece (marker + 1): al pasar a otra, se va")]
    public int pagina = 2;
    public BookPageTracker tracker;
    [Tooltip("Al empezar el onboarding de un alumno nuevo, se va")]
    public OnboardingSequence onboarding;

    public bool Visible { get; private set; }
    public bool IsPlaying { get; private set; }

    SpriteRenderer sr;
    Coroutine rutina;
    bool saliendo;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        SetAlpha(0f);
        if (cuadros != null && cuadros.Length > 0) sr.sprite = cuadros[0];
        if (!tracker) tracker = FindFirstObjectByType<BookPageTracker>();
        if (!onboarding) onboarding = FindFirstObjectByType<OnboardingSequence>();
    }

    [ContextMenu("Play")]
    public void Play()
    {
        if (cuadros == null || cuadros.Length == 0) return;
        if (rutina != null) StopCoroutine(rutina);
        rutina = StartCoroutine(Mostrar());
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

    IEnumerator Mostrar()
    {
        Visible = true; IsPlaying = true; saliendo = false;
        sr.sprite = cuadros[0];
        for (float t = 0f; t < aparecer; t += Time.deltaTime) { SetAlpha(t / aparecer); yield return null; }
        SetAlpha(1f);
        yield return new WaitForSeconds(esperaInicio);
        float paso = 1f / Mathf.Max(0.5f, fps);
        for (int i = 1; i < cuadros.Length; i++)
        {
            sr.sprite = cuadros[i];
            yield return new WaitForSeconds(paso);
        }
        IsPlaying = false;
        rutina = null;
    }

    IEnumerator Irse()
    {
        IsPlaying = false;
        saliendo = true;
        float from = sr.color.a;
        for (float t = 0f; t < desaparecer; t += Time.deltaTime) { SetAlpha(Mathf.Lerp(from, 0f, t / desaparecer)); yield return null; }
        SetAlpha(0f);
        Visible = false;
        saliendo = false;
        if (cuadros != null && cuadros.Length > 0) sr.sprite = cuadros[0];
        rutina = null;
    }

    void SetAlpha(float a) { var c = sr.color; c.a = a; sr.color = c; }
}
