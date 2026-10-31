using System.Collections;
using UnityEngine;

/// <summary>
/// Explosión de estrellas para celebrar una respuesta correcta.
/// </summary>
public class StarBurst : MonoBehaviour
{
    public Sprite star;
    public int count = 12;
    public float radius = 1.8f;
    public float duration = 1.4f;
    public float starSize = 0.35f;
    public Color[] colors = { new Color(1f, 0.9f, 0.35f), new Color(1f, 1f, 0.75f), new Color(1f, 0.75f, 0.3f) };
    public int sortingOrder = 50;

    public void Burst(Vector3 center)
    {
        if (star == null) return;
        for (int i = 0; i < count; i++)
        {
            float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
            var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.75f, 0f);
            StartCoroutine(Fly(center, dir, Random.Range(0.7f, 1.1f), colors[i % colors.Length]));
        }
    }

    IEnumerator Fly(Vector3 center, Vector3 dir, float distMul, Color c)
    {
        var go = new GameObject("Estrella");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = star;
        sr.sortingOrder = sortingOrder;
        float spin = Random.Range(-360f, 360f);

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            float ease = 1f - (1f - k) * (1f - k);
            go.transform.position = center + dir * radius * distMul * ease;
            go.transform.localScale = Vector3.one * starSize * Mathf.Sin(Mathf.Min(1f, k * 1.6f) * Mathf.PI * 0.5f + 0.2f) * (1f - k * 0.5f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, spin * t);
            sr.color = new Color(c.r, c.g, c.b, 1f - Mathf.Pow(k, 2f));
            yield return null;
        }
        Destroy(go);
    }
}
