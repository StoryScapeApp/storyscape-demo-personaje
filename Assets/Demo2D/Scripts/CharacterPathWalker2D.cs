using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// StoryScape – demo de proyección: el personaje entra al área proyectada, camina por la mesa
/// haciendo algunas paradas naturales y se detiene en el punto final, mirando al lector.
/// No requiere ningún control. Al llegar dispara onArrived (p. ej. para iniciar el diálogo).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CharacterPathWalker2D : MonoBehaviour
{
    [Header("Recorrido")]
    [Tooltip("Desde dónde entra el personaje (puede estar fuera del área proyectada)")]
    public Transform startPoint;
    [Tooltip("Lugar donde se detiene al final (luego puede seguir a un marcador ArUco)")]
    public Transform finalPoint;
    [Tooltip("Paradas intermedias al azar antes de llegar al punto final")]
    [Range(0, 6)] public int randomStops = 3;
    [Tooltip("Zona de la mesa donde pueden caer las paradas intermedias")]
    public Vector2 areaMin = new Vector2(-5.5f, -3f);
    public Vector2 areaMax = new Vector2(5.5f, 2.5f);
    public float startDelay = 1f;

    [Header("Movimiento")]
    public float walkSpeed = 1.6f;
    [Tooltip("Segundos mínimos y máximos de cada parada intermedia")]
    public Vector2 pauseTime = new Vector2(0.5f, 1.5f);

    [Header("Animación (índice 0 = quieto, 1 y 2 = pasos)")]
    public Sprite[] downFrames;
    public Sprite[] upFrames;
    public Sprite[] rightFrames;
    public Sprite[] leftFrames;
    public float stepsPerSecond = 7f;

    [Header("Llegada")]
    public SpriteRenderer arrivalRing;
    public UnityEvent onArrived;
    [Tooltip("Repetir el recorrido indefinidamente (útil para mostrar la demo)")]
    public bool loop = false;
    public float loopDelay = 4f;

    static readonly int[] WalkCycle = { 1, 0, 2, 0 };

    SpriteRenderer sr;
    Sprite[] current;
    float animTime;
    float ringAlpha;
    Vector3 ringBaseScale = Vector3.one;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        current = rightFrames != null && rightFrames.Length > 0 ? rightFrames : downFrames;
        if (arrivalRing) ringBaseScale = arrivalRing.transform.localScale;
        SetRing(0f);
    }

    void Start()
    {
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        while (true)
        {
            if (startPoint) transform.position = Keep(startPoint.position);
            SetRing(0f);
            ShowIdle();
            yield return new WaitForSeconds(startDelay);

            Vector2 goal = finalPoint ? (Vector2)finalPoint.position : Vector2.zero;
            Vector2 prev = transform.position;

            for (int i = 0; i < randomStops; i++)
            {
                float t = (i + 1f) / (randomStops + 1f);
                Vector2 p = RandomPoint(prev, goal, t);
                yield return WalkTo(p);
                yield return new WaitForSeconds(Random.Range(pauseTime.x, pauseTime.y));
                prev = p;
            }

            yield return WalkTo(goal);

            // Llegó: mira al lector y aparece el anillo de luz.
            current = downFrames;
            ShowIdle();
            yield return FadeRing(1f, 0.6f);
            onArrived?.Invoke();

            if (!loop) yield break;
            yield return new WaitForSeconds(loopDelay);
            yield return FadeRing(0f, 0.4f);
        }
    }

    Vector2 RandomPoint(Vector2 from, Vector2 goal, float progress)
    {
        Vector2 best = goal;
        for (int i = 0; i < 15; i++)
        {
            var r = new Vector2(Random.Range(areaMin.x, areaMax.x), Random.Range(areaMin.y, areaMax.y));
            // Se va acercando poco a poco al punto final.
            Vector2 p = Vector2.Lerp(r, goal, progress * 0.6f);
            best = p;
            if ((p - from).sqrMagnitude > 1.5f * 1.5f) break;
        }
        return best;
    }

    IEnumerator WalkTo(Vector2 target)
    {
        while (true)
        {
            Vector2 pos = transform.position;
            Vector2 to = target - pos;
            float dist = to.magnitude;
            if (dist < 0.02f) break;

            float speed = walkSpeed * Mathf.Clamp(dist / 0.4f, 0.45f, 1f); // frena al llegar
            Vector2 next = Vector2.MoveTowards(pos, target, speed * Time.deltaTime);
            transform.position = Keep(next);

            Vector2 dir = to / dist;
            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)) current = dir.x > 0 ? rightFrames : leftFrames;
            else current = dir.y > 0 ? upFrames : downFrames;

            animTime += Time.deltaTime * stepsPerSecond * (speed / walkSpeed);
            if (current != null && current.Length >= 3)
                sr.sprite = current[WalkCycle[(int)animTime % WalkCycle.Length]];

            yield return null;
        }
        transform.position = Keep(target);
        ShowIdle();
    }

    void ShowIdle()
    {
        animTime = 0f;
        if (current != null && current.Length > 0) sr.sprite = current[0];
    }

    IEnumerator FadeRing(float to, float duration)
    {
        float from = ringAlpha;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            SetRing(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetRing(to);
    }

    void SetRing(float a)
    {
        ringAlpha = a;
        if (!arrivalRing) return;
        var c = arrivalRing.color;
        c.a = a;
        arrivalRing.color = c;
    }

    void Update()
    {
        // Pulso suave del anillo mientras está visible.
        if (arrivalRing && ringAlpha > 0f)
            arrivalRing.transform.localScale = ringBaseScale * (1f + 0.06f * Mathf.Sin(Time.time * 3f));
    }

    Vector3 Keep(Vector2 p) => new Vector3(p.x, p.y, transform.position.z);

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
        Gizmos.DrawWireCube((areaMin + areaMax) * 0.5f, areaMax - areaMin);
        if (finalPoint)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(finalPoint.position, 0.35f);
        }
        if (startPoint)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(startPoint.position, 0.25f);
        }
    }
}
