using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Recorta "Sprites Principito volando.png" (hoja 3x2, fondo blanco o transparente) en 6 PNG transparentes:
/// 01-02 de pie / impulso, 03 despegue, 04-06 vuelo con las aves.
/// Cada cuadro conserva su posición dentro de la celda y todos comparten la línea de suelo de su fila,
/// así la altura del salto dibujada en la hoja se mantiene. Pivote: en el suelo, centro de la celda.
/// Menú: StoryScape > Lectura > Procesar sprites Principito volando
/// </summary>
public static class PrincipitoVolandoSpriteProcessor
{
    const string Source = "Assets/Demo2D/Onboarding/Sprites/raw/Sprites Principito volando.png";
    public const string OutDir = "Assets/Lectura/Sprites/Vuelo";
    public const string Prefix = "volando_";
    const float PixelsPerUnit = 98f;    // la figura mide ~2 unidades de alto en escena (como en la referencia del recorrido)
    const int Pad = 4;
    const int Cols = 3, Rows = 2;

    static bool Light(Color32 c)
    {
        int mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)), mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return mn >= 200 && mx - mn <= 28;   // blanco, grises claros y sombra suave del suelo
    }

    static bool Ink(Color32 c)
    {
        int mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)), mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return mn < 215 || mx - mn > 30;
    }

    [MenuItem("StoryScape/Lectura/Procesar sprites Principito volando")]
    public static Sprite[] Process()
    {
        if (!File.Exists(Source)) { Debug.LogWarning("[Volando] No se encontró " + Source); return null; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(Source));
        int W = tex.width, H = tex.height;
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);

        // Hoja con fondo transparente: se apoya sobre blanco y se procesa igual que una hoja de fondo blanco
        int transparentes = 0;
        for (int i = 0; i < px.Length; i++) if (px[i].a < 250) transparentes++;
        if (transparentes > px.Length / 10)
        {
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i]; float a = c.a / 255f;
                px[i] = new Color32((byte)Mathf.RoundToInt(c.r * a + 255 * (1 - a)), (byte)Mathf.RoundToInt(c.g * a + 255 * (1 - a)),
                                   (byte)Mathf.RoundToInt(c.b * a + 255 * (1 - a)), 255);
            }
            Debug.Log("[Volando] Hoja con fondo transparente: se procesa sobre blanco.");
        }

        // Cortes por los huecos vacíos entre cuadros (la hoja no está dividida en partes iguales)
        var colInk = new int[W]; var rowInk = new int[H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (Ink(px[y * W + x])) { colInk[x]++; rowInk[y]++; }
        var xCut = new int[Cols + 1]; xCut[0] = 0; xCut[Cols] = W;
        for (int c = 1; c < Cols; c++) xCut[c] = Gap(colInk, c * W / Cols, W / 6);
        var yCut = new int[Rows + 1]; yCut[0] = 0; yCut[Rows] = H;   // desde abajo
        for (int r = 1; r < Rows; r++) yCut[r] = Gap(rowInk, r * H / Rows, H / 5);

        // Suelo de cada fila (y desde abajo): primera fila con trazo dentro de la fila
        var groundOfRow = new int[Rows];
        for (int r = 0; r < Rows; r++)
        {
            int y0 = yCut[Rows - 1 - r], y1 = yCut[Rows - r];
            groundOfRow[r] = y0;
            for (int y = y0; y < y1; y++)
            {
                bool any = false;
                for (int x = 0; x < W && !any; x++) if (Ink(px[y * W + x])) any = true;
                if (any) { groundOfRow[r] = y; break; }
            }
        }

        // Fondo: relleno desde los bordes de cada celda a través de píxeles claros
        var bg = new bool[W * H];
        var q = new Queue<int>();
        void Seed(int i) { if (!bg[i] && Light(px[i])) { bg[i] = true; q.Enqueue(i); } }
        for (int x = 0; x < W; x++) { Seed(x); Seed((H - 1) * W + x); }
        for (int y = 0; y < H; y++) { Seed(y * W); Seed(y * W + W - 1); }
        for (int c = 1; c < Cols; c++) for (int y = 0; y < H; y++) Seed(y * W + xCut[c]);
        for (int r = 1; r < Rows; r++) for (int x = 0; x < W; x++) Seed(yCut[r] * W + x);
        while (q.Count > 0)
        {
            int i = q.Dequeue(); int x = i % W, y = i / W;
            if (x > 0) Seed(i - 1);
            if (x < W - 1) Seed(i + 1);
            if (y > 0) Seed(i - W);
            if (y < H - 1) Seed(i + W);
        }

        // Quita manchas sueltas pequeñas (polvo, restos de sombra)
        var label = new int[W * H];
        var st = new Stack<int>();
        var comp = new List<int>();
        var comps = new List<List<int>>();
        int id = 0;
        for (int s = 0; s < W * H; s++)
        {
            if (bg[s] || label[s] != 0) continue;
            id++; comp.Clear(); label[s] = id; st.Push(s);
            while (st.Count > 0)
            {
                int i = st.Pop(); comp.Add(i); int x = i % W, y = i / W;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int n = ny * W + nx;
                        if (bg[n] || label[n] != 0) continue;
                        label[n] = id; st.Push(n);
                    }
            }
            if (comp.Count < 30) foreach (int i in comp) bg[i] = true;
            else comps.Add(new List<int>(comp));
        }

        // Polvo y sombra junto al suelo: manchas separadas del dibujo principal y pegadas a la línea de suelo
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int x0 = xCut[c], x1 = xCut[c + 1], y0 = yCut[Rows - 1 - r], y1 = yCut[Rows - r];
                List<int> main = null;
                var inCell = new List<List<int>>();
                foreach (var cp in comps)
                {
                    int i0 = cp[0], cx = i0 % W, cy = i0 / W;
                    if (cx < x0 || cx >= x1 || cy < y0 || cy >= y1) continue;
                    inCell.Add(cp);
                    if (main == null || cp.Count > main.Count) main = cp;
                }
                foreach (var cp in inCell)
                {
                    if (cp == main) continue;
                    int maxY = 0; foreach (int i in cp) maxY = Mathf.Max(maxY, i / W);
                    if (maxY < groundOfRow[r] + 28) foreach (int i in cp) bg[i] = true;
                }
            }

        // Lienzo común: ancho de celda, alto suficiente para la fila más alta
        int maxAbove = 0, cellW = 0;
        for (int r = 0; r < Rows; r++) maxAbove = Mathf.Max(maxAbove, yCut[Rows - r] - groundOfRow[r]);
        for (int c = 0; c < Cols; c++) cellW = Mathf.Max(cellW, xCut[c + 1] - xCut[c]);
        int cw = cellW + Pad * 2, ch = maxAbove + Pad * 2;

        Directory.CreateDirectory(OutDir);
        var paths = new List<string>();
        int n2 = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                n2++;
                int x0 = xCut[c], x1 = xCut[c + 1], y0 = yCut[Rows - 1 - r], y1 = yCut[Rows - r];
                int offX = (cellW - (x1 - x0)) / 2;
                int ground = groundOfRow[r];
                var outPx = new Color32[cw * ch];
                int opaque = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int i = y * W + x;
                        if (bg[i]) continue;
                        int ox = x - x0 + offX + Pad, oy = y - ground + Pad;
                        if (oy < 0 || oy >= ch) continue;
                        var col = px[i];
                        // Borde suave: los píxeles claros junto al fondo se vuelven semitransparentes
                        bool edge = (x > 0 && bg[i - 1]) || (x < W - 1 && bg[i + 1]) || (y > 0 && bg[i - W]) || (y < H - 1 && bg[i + W]);
                        int mn = Mathf.Min(col.r, Mathf.Min(col.g, col.b));
                        if (edge && mn > 170) col.a = (byte)Mathf.Clamp(Mathf.RoundToInt((255 - mn) / 85f * 255f), 40, 255);
                        outPx[oy * cw + ox] = col;
                        opaque++;
                    }
                var o = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                o.SetPixels32(outPx); o.Apply();
                string path = $"{OutDir}/{Prefix}{n2:00}.png";
                File.WriteAllBytes(path, o.EncodeToPNG());
                Object.DestroyImmediate(o);
                paths.Add(path);
                Debug.Log($"[Volando] {Path.GetFileName(path)}: {opaque} px opacos (celda {c},{r}).");
            }

        AssetDatabase.Refresh();
        var sprites = new Sprite[paths.Count];
        for (int k = 0; k < paths.Count; k++)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(paths[k]);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.filterMode = FilterMode.Bilinear;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = new Vector2(0.5f, (float)Pad / ch);   // en el suelo, centro de la celda
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            sprites[k] = AssetDatabase.LoadAssetAtPath<Sprite>(paths[k]);
        }
        Debug.Log($"[Volando] {sprites.Length} cuadros en {OutDir} (lienzo {cw}x{ch}, cortes x: {string.Join(",", xCut)}, y: {string.Join(",", yCut)}, suelo filas: {string.Join(", ", groundOfRow)}).");
        return sprites;
    }

    /// Columna/fila sin trazo más cercana a 'center' (dentro de ±range); si no hay, la de menos trazo.
    static int Gap(int[] ink, int center, int range)
    {
        int best = center, bestVal = int.MaxValue, bestDist = int.MaxValue;
        for (int i = Mathf.Max(1, center - range); i <= Mathf.Min(ink.Length - 2, center + range); i++)
        {
            int d = Mathf.Abs(i - center);
            if (ink[i] < bestVal || (ink[i] == bestVal && d < bestDist)) { best = i; bestVal = ink[i]; bestDist = d; }
        }
        // centro del hueco vacío que contiene 'best'
        int a = best, b = best;
        while (a > 0 && ink[a - 1] == bestVal) a--;
        while (b < ink.Length - 1 && ink[b + 1] == bestVal) b++;
        return (a + b) / 2;
    }

    public static Sprite[] LoadFrames()
    {
        var list = new List<Sprite>();
        for (int i = 1; i <= Cols * Rows; i++)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{OutDir}/{Prefix}{i:00}.png");
            if (s) list.Add(s);
        }
        return list.Count == Cols * Rows ? list.ToArray() : Process();
    }
}
