using UnityEngine;

/// <summary>
/// SOLO PARA PROBAR SIN PROYECTOR: dibuja en pantalla la página actual del libro (papel claro + cada palabra
/// en su posición real del PDF), para ver si el resaltado cae sobre la palabra correcta.
/// ¡Desmarca "Show" al proyectar sobre el libro real! (si no, se proyecta encima del libro).
/// </summary>
public class SimulatedBookPage : MonoBehaviour
{
    [Tooltip("Mostrar la página simulada (desmarcar al usar el proyector)")]
    public bool show = true;
    [Tooltip("Permite que Mostrar() la encienda (p. ej. al terminar el onboarding). Desmarcar al proyectar sobre el libro real")]
    public bool simulacionActiva = true;
    public WordHighlighter highlighter;
    public BookPageTracker tracker;

    [Header("Estilo")]
    public Color paper = new Color(0.96f, 0.94f, 0.88f);
    public Color ink = new Color(0.16f, 0.16f, 0.22f);
    [Tooltip("Zona del marcador ArUco en la página 1 (0–1 de la página, según el PDF)")]
    public Rect markerRect = Rect.MinMaxRect(0.1438f, 0.0871f, 0.3800f, 0.4138f);

    Transform root;
    int shownPage = -1;
    static Sprite white;

    /// Enciende la página simulada (lo llama el onboarding al terminar, si la simulación está activa).
    [ContextMenu("Mostrar página simulada")]
    public void Mostrar()
    {
        if (!simulacionActiva) return;
        show = true;
        Debug.Log("[Simulación] Página del libro en pantalla: lee en voz alta para probar animaciones y quiz.");
    }

    [ContextMenu("Ocultar página simulada")]
    public void Ocultar() => show = false;

    void Update()
    {
        if (!show)
        {
            if (root) { Destroy(root.gameObject); root = null; shownPage = -1; }
            return;
        }
        int page = tracker ? tracker.CurrentPage : 1;
        if (page != shownPage) Build(page);
    }

    void Build(int page)
    {
        if (root) Destroy(root.gameObject);
        shownPage = page;
        if (highlighter == null || highlighter.book == null) return;

        root = new GameObject("PaginaSimulada").transform;
        root.SetParent(highlighter.book, false);
        Vector2 size = highlighter.bookSize;

        // Papel
        Quad("Papel", Vector2.zero, size, paper, 1);
        // Línea central (lomo del libro)
        Quad("Lomo", Vector2.zero, new Vector2(0.02f, size.y), new Color(0f, 0f, 0f, 0.15f), 2);

        // Marcador ArUco de la primera página
        if (page == 1)
        {
            var m = Quad("Marcador", ToLocal(markerRect.center, size), Vector2.Scale(markerRect.size, size), new Color(0.12f, 0.12f, 0.12f), 2);
            Label("MarcadorTexto", m.transform.localPosition, new Vector2(1f, 0.3f), "ArUco 0", Color.white, 3);
        }

        int n = 0;
        foreach (var w in highlighter.GetWords(page))
        {
            Vector2 c = ToLocal(w.Value.center, size);
            Vector2 s = Vector2.Scale(w.Value.size, size);
            Label("P_" + w.Key, c, s, w.Key, ink, 3);
            n++;
        }
        if (n == 0) Label("SinDatos", Vector2.zero, new Vector2(4f, 0.5f), $"Página {page}: sin posiciones de palabras", ink, 3);
    }

    static Vector2 ToLocal(Vector2 pdf, Vector2 size) => new Vector2((pdf.x - 0.5f) * size.x, (0.5f - pdf.y) * size.y);

    SpriteRenderer Quad(string name, Vector2 pos, Vector2 size, Color color, int order)
    {
        if (white == null)
        {
            var tex = new Texture2D(4, 4);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = white;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    void Label(string name, Vector2 pos, Vector2 size, string text, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        var tmp = go.AddComponent<TMPro.TextMeshPro>();
        tmp.rectTransform.sizeDelta = size;
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.2f;
        tmp.fontSizeMax = 6f;
        tmp.margin = Vector4.zero;
        tmp.sortingOrder = order;
    }
}
