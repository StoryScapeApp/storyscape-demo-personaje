using System.Collections;
using UnityEngine;

/// <summary>
/// Meteoro que recorre una ruta suave (Catmull-Rom) dejando una estela de estrellitas.
/// La ruta se define con hijos "Punto_0", "Punto_1"… (se pueden mover en la escena para calibrar).
/// </summary>
public class MeteorFlight : MonoBehaviour
{
    public SpriteRenderer head;
    public Sprite trailSprite;
    public Transform[] waypoints;
    public float duration = 3.5f;
    public float headSize = 0.45f;
    public Color headColor = new Color(1f, 0.85f, 0.35f);
    public Color trailColor = new Color(1f, 0.95f, 0.6f);
    public float trailInterval = 0.035f;
    public float trailLife = 0.7f;
    public float trailSize = 0.18f;

    public bool IsPlaying { get; private set; }

    void Awake()
    {
        if (head) head.color = new Color(headColor.r, headColor.g, headColor.b, 0f);
    }

    public IEnumerator Play()
    {
        if (head == null || waypoints == null || waypoints.Length < 2) yield break;
        IsPlaying = true;
        float t = 0f, spawn = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float eased = k * k * (3f - 2f * k);
            Vector3 p = Evaluate(eased);
            head.transform.position = p;
            head.transform.localScale = Vector3.one * headSize * (1f + 0.12f * Mathf.Sin(t * 20f));
            head.transform.rotation = Quaternion.Euler(0f, 0f, t * 240f);
            float a = Mathf.Min(1f, k * 6f) * Mathf.Min(1f, (1f - k) * 6f);
            head.color = new Color(headColor.r, headColor.g, headColor.b, a);

            spawn += Time.deltaTime;
            while (spawn >= trailInterval && a > 0.05f)
            {
                spawn -= trailInterval;
                StartCoroutine(Trail(p));
            }
            yield return null;
        }
        head.color = new Color(headColor.r, headColor.g, headColor.b, 0f);
        yield return new WaitForSeconds(trailLife);
        IsPlaying = false;
    }

    IEnumerator Trail(Vector3 pos)
    {
        if (trailSprite == null) yield break;
        var go = new GameObject("Estela");
        go.transform.SetParent(transform, false);
        go.transform.position = pos + (Vector3)(Random.insideUnitCircle * 0.06f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = trailSprite;
        sr.sortingOrder = head.sortingOrder - 1;
        float size = trailSize * Random.Range(0.7f, 1.2f);
        for (float t = 0f; t < trailLife; t += Time.deltaTime)
        {
            float k = t / trailLife;
            go.transform.localScale = Vector3.one * size * (1f - k);
            sr.color = new Color(trailColor.r, trailColor.g, trailColor.b, (1f - k) * 0.9f);
            yield return null;
        }
        Destroy(go);
    }

    Vector3 Evaluate(float t)
    {
        int n = waypoints.Length - 1;
        float f = t * n;
        int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, n - 1);
        float u = f - i;
        Vector3 p0 = waypoints[Mathf.Max(i - 1, 0)].position;
        Vector3 p1 = waypoints[i].position;
        Vector3 p2 = waypoints[i + 1].position;
        Vector3 p3 = waypoints[Mathf.Min(i + 2, n)].position;
        return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
    }

    void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Length < 2) return;
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
        Vector3 prev = waypoints[0] ? waypoints[0].position : Vector3.zero;
        for (int s = 1; s <= 40; s++)
        {
            if (System.Array.Exists(waypoints, w => w == null)) return;
            Vector3 p = Evaluate(s / 40f);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}
