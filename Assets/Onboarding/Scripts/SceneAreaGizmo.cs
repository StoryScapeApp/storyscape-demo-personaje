using UnityEngine;

/// <summary>
/// Dibuja un rectángulo de referencia solo en la vista Scene (no se proyecta).
/// Se usa para marcar dónde cae el libro físico al calibrar la proyección.
/// </summary>
public class SceneAreaGizmo : MonoBehaviour
{
    public Vector2 size = new Vector2(5f, 3.5f);
    public Color color = new Color(1f, 1f, 1f, 0.5f);
    public string label = "";

    void OnDrawGizmos()
    {
        Gizmos.color = color;
        Gizmos.DrawWireCube(transform.position, new Vector3(size.x, size.y, 0f));
        Gizmos.DrawLine(transform.position + new Vector3(0f, size.y / 2f, 0f), transform.position - new Vector3(0f, size.y / 2f, 0f));
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(label))
            UnityEditor.Handles.Label(transform.position + new Vector3(-size.x / 2f, size.y / 2f + 0.25f, 0f), label);
#endif
    }
}
