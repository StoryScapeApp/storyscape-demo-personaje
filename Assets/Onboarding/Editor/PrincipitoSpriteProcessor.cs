using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Procesa las hojas de sprites del Principito (imágenes con fondo azul verdoso y cuadrícula):
/// 1) quita el fondo y la cuadrícula (relleno desde los bordes por color) y los huecos de fondo
///    encerrados dentro del dibujo (p. ej. entre el brazo y el letrero),
/// 2) separa cada cuadro (componentes conectados grandes; ignora títulos y etiquetas),
/// 3) alinea todos los cuadros por los pies en un lienzo común (sin saltos en la animación),
/// 4) guarda PNG transparentes en Assets/Onboarding/Sprites/Principito y los asigna al Principito.
/// Menú: StoryScape > Onboarding > Procesar sprites del Principito
/// </summary>
public static class PrincipitoSpriteProcessor
{
    static readonly string[] SourceDirs = { "Assets/Onboarding/Sprites/raw", "Assets/Demo2D/Onboarding/Sprites/raw" };
    const string OutDir = "Assets/Onboarding/Sprites/Principito";
    const float PixelsPerUnit = 150f;   // ~3 unidades de alto en escena
    const int MinFrameArea = 8000;      // píxeles mínimos para considerar un cuadro (no texto)
    const int MinHoleArea = 600;        // huecos de fondo encerrados a partir de este tamaño se borran (los ojos miden ~170 px)
    const int Pad = 6;

    static readonly Color32 BgColor = new Color32(48, 82, 96, 255);     // fondo
    static readonly Color32 GridColor = new Color32(82, 128, 144, 255); // líneas de la cuadrícula

    class Frame
    {
        public Color32[] px; public int w, h;     // imagen fuente completa
        public List<int> pixels = new List<int>();
        public int minX = int.MaxValue, minY = int.MaxValue, maxX, maxY; // y desde abajo (Unity)
        public float anchorX; public int anchorY;   // pies
        public float cx => (minX + maxX) * 0.5f;
    }

    [MenuItem("StoryScape/Onboarding/Procesar sprites del Principito")]
    public static void Process()
    {
        var files = new List<string>();
        foreach (var d in SourceDirs)
            if (Directory.Exists(d))
                foreach (var f in Directory.GetFiles(d))
                {
                    var e = Path.GetExtension(f).ToLower();
                    if (e == ".png" || e == ".jpg" || e == ".jpeg") files.Add(f.Replace('\\', '/'));
                }
        if (files.Count == 0) { Debug.LogWarning("[Principito] No se encontraron imágenes en las carpetas raw."); return; }
        files.Sort();

        var clips = new Dictionary<string, List<Frame>>();
        foreach (var file in files)
        {
            var frames = ExtractFrames(file);
            Debug.Log($"[Principito] {Path.GetFileName(file)}: {frames.Count} cuadros detectados.");
            if (frames.Count == 11)
            {
                clips["Listening"] = frames.GetRange(0, 8);
                clips["Idle"] = new List<Frame> { frames[8] };
                clips["Nod"] = new List<Frame> { frames[9] };
                clips["React"] = new List<Frame> { frames[10] };
            }
            else if (frames.Count == 8)
            {
                clips["Asking"] = frames;
            }
            else
            {
                clips[Path.GetFileNameWithoutExtension(file)] = frames;
                Debug.LogWarning($"[Principito] {Path.GetFileName(file)}: se esperaban 8 u 11 cuadros. Revisa los PNG generados.");
            }
        }

        // Lienzo común para todos los cuadros (misma escala y pies en el mismo punto)
        int halfW = 0, height = 0;
        foreach (var kv in clips)
            foreach (var f in kv.Value)
            {
                halfW = Mathf.Max(halfW, Mathf.CeilToInt(Mathf.Max(f.anchorX - f.minX, f.maxX - f.anchorX)) + 1);
                height = Mathf.Max(height, f.maxY - f.anchorY + 1);
            }
        int cw = halfW * 2 + Pad * 2, ch = height + Pad * 2;

        if (Directory.Exists(OutDir))
            foreach (var old in Directory.GetFiles(OutDir, "*.png")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
        Directory.CreateDirectory(OutDir);

        var paths = new List<string>();
        foreach (var kv in clips)
        {
            for (int i = 0; i < kv.Value.Count; i++)
            {
                var f = kv.Value[i];
                var outPx = new Color32[cw * ch];
                int offX = Pad + halfW - Mathf.RoundToInt(f.anchorX);
                int offY = Pad - f.anchorY;
                foreach (int idx in f.pixels)
                {
                    int x = idx % f.w, y = idx / f.w;
                    int ox = x + offX, oy = y + offY;
                    if (ox < 0 || oy < 0 || ox >= cw || oy >= ch) continue;
                    outPx[oy * cw + ox] = f.px[idx];
                }
                var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                tex.SetPixels32(outPx);
                tex.Apply();
                string path = $"{OutDir}/{kv.Key}_{i}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                paths.Add(path);
            }
        }

        AssetDatabase.Refresh();
        float pivotY = (float)Pad / ch;
        foreach (var p in paths)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.filterMode = FilterMode.Bilinear;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = new Vector2(0.5f, pivotY);
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
        }

        var result = new Dictionary<string, Sprite[]>();
        foreach (var kv in clips)
        {
            var arr = new Sprite[kv.Value.Count];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = AssetDatabase.LoadAssetAtPath<Sprite>($"{OutDir}/{kv.Key}_{i}.png");
            result[kv.Key] = arr;
        }

        AssignToPrincipito(result);
        Debug.Log($"[Principito] Sprites generados en {OutDir} ({paths.Count} cuadros, lienzo {cw}x{ch}).");
    }

    // ───────────── Extracción ─────────────
    static bool IsBackground(Color32 c)
    {
        // Familia azul verdosa del fondo y la cuadrícula: azul > verde > rojo, poco rojo.
        return c.b - c.g >= 4 && c.g - c.r >= 15 && c.b - c.r >= 25 && c.r < 130;
    }

    static int Dist2(Color32 a, Color32 b)
    {
        int dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
        return dr * dr + dg * dg + db * db;
    }

    /// Muy parecido al color exacto del fondo o de la cuadrícula (no a los ojos azules ni al abrigo).
    static bool IsExactBackground(Color32 c)
    {
        return IsBackground(c) && Dist2(c, BgColor) < 22 * 22; // solo el fondo: los ojos se parecen a la cuadrícula
    }

    static List<Frame> ExtractFrames(string file)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(file));
        int W = tex.width, H = tex.height;
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);

        // 1) Fondo: relleno desde los bordes a través de píxeles de la familia del fondo
        var bg = new bool[W * H];
        var q = new Queue<int>();
        void Seed(int i) { if (!bg[i] && IsBackground(px[i])) { bg[i] = true; q.Enqueue(i); } }
        for (int x = 0; x < W; x++) { Seed(x); Seed((H - 1) * W + x); }
        for (int y = 0; y < H; y++) { Seed(y * W); Seed(y * W + W - 1); }
        while (q.Count > 0)
        {
            int i = q.Dequeue(); int x = i % W, y = i / W;
            if (x > 0) Seed(i - 1);
            if (x < W - 1) Seed(i + 1);
            if (y > 0) Seed(i - W);
            if (y < H - 1) Seed(i + W);
        }

        // 1b) Huecos de fondo encerrados dentro del dibujo (entre brazo, letrero, cabeza o capa)
        var visited = new bool[W * H];
        var region = new List<int>();
        var st = new Stack<int>();
        int holes = 0;
        for (int s = 0; s < W * H; s++)
        {
            if (bg[s] || visited[s] || !IsExactBackground(px[s])) continue;
            region.Clear();
            visited[s] = true; st.Push(s);
            while (st.Count > 0)
            {
                int i = st.Pop(); region.Add(i);
                int x = i % W, y = i / W;
                void Try(int n) { if (!bg[n] && !visited[n] && IsBackground(px[n])) { visited[n] = true; st.Push(n); } }
                if (x > 0) Try(i - 1);
                if (x < W - 1) Try(i + 1);
                if (y > 0) Try(i - W);
                if (y < H - 1) Try(i + W);
            }
            if (region.Count >= MinHoleArea)
            {
                foreach (int i in region) bg[i] = true;
                holes++;
            }
        }
        if (holes > 0) Debug.Log($"[Principito] {Path.GetFileName(file)}: {holes} huecos de fondo interiores eliminados.");

        // Limpieza de bordes (halo de compresión JPEG): 2 pasadas sobre píxeles junto al fondo
        for (int pass = 0; pass < 2; pass++)
        {
            var toClear = new List<int>();
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    int i = y * W + x;
                    if (bg[i]) continue;
                    if (!(bg[i - 1] || bg[i + 1] || bg[i - W] || bg[i + W])) continue;
                    var c = px[i];
                    if (c.b > c.r + 8 && c.g > c.r && c.r < 120) toClear.Add(i);
                }
            foreach (int i in toClear) bg[i] = true;
        }

        // 2) Componentes conectados (8 vecinos) de lo que no es fondo
        var label = new int[W * H];
        var comps = new List<Frame>();
        var stack = new Stack<int>();
        for (int start = 0; start < W * H; start++)
        {
            if (bg[start] || label[start] != 0) continue;
            var f = new Frame { px = px, w = W, h = H };
            int id = comps.Count + 1;
            label[start] = id; stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop(); int x = i % W, y = i / W;
                f.pixels.Add(i);
                if (x < f.minX) f.minX = x; if (x > f.maxX) f.maxX = x;
                if (y < f.minY) f.minY = y; if (y > f.maxY) f.maxY = y;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int n = ny * W + nx;
                        if (bg[n] || label[n] != 0) continue;
                        label[n] = id; stack.Push(n);
                    }
            }
            comps.Add(f);
        }

        var big = comps.FindAll(c => c.pixels.Count >= MinFrameArea);
        var small = comps.FindAll(c => c.pixels.Count < MinFrameArea && c.pixels.Count >= 20);

        // Pedazos sueltos (mechones, puntas de capa) dentro del área de un cuadro grande → se unen
        foreach (var s in small)
            foreach (var b in big)
                if (s.cx >= b.minX - 15 && s.cx <= b.maxX + 15 && s.minY >= b.minY - 15 && s.maxY <= b.maxY + 15)
                {
                    b.pixels.AddRange(s.pixels);
                    b.minX = Mathf.Min(b.minX, s.minX); b.maxX = Mathf.Max(b.maxX, s.maxX);
                    b.minY = Mathf.Min(b.minY, s.minY); b.maxY = Mathf.Max(b.maxY, s.maxY);
                    break;
                }

        // 3) Ancla en los pies: centro horizontal de las filas más bajas
        foreach (var b in big)
        {
            int band = Mathf.Max(4, (b.maxY - b.minY) / 14);
            float sum = 0; int n = 0;
            foreach (int i in b.pixels)
            {
                int y = i / W;
                if (y <= b.minY + band) { sum += i % W; n++; }
            }
            b.anchorX = n > 0 ? sum / n : b.cx;
            b.anchorY = b.minY;
        }

        // 4) Orden de lectura: filas de arriba hacia abajo, luego izquierda a derecha
        float rowTol = H * 0.08f;
        big.Sort((a, c) =>
        {
            float ay = (a.minY + a.maxY) * 0.5f, cy = (c.minY + c.maxY) * 0.5f;
            if (Mathf.Abs(ay - cy) > rowTol) return cy.CompareTo(ay); // y mayor = más arriba
            return a.cx.CompareTo(c.cx);
        });
        return big;
    }

    // ───────────── Asignación al Principito ─────────────
    static void AssignToPrincipito(Dictionary<string, Sprite[]> clips)
    {
        var anim = Object.FindFirstObjectByType<SpriteAnimator2D>();
        if (anim == null)
        {
            Debug.Log("[Principito] Abre la escena Onboarding y vuelve a ejecutar el menú para asignar los sprites.");
            return;
        }

        Sprite[] Get(string k) => clips.TryGetValue(k, out var a) ? a : null;
        var idle = Get("Idle");
        var nod = Get("Nod");
        var react = Get("React");

        anim.clips.Clear();
        // Velocidades pausadas. Reposo = Idle sostenido con un Nod breve (se usa fuera de las reglas 1 y 2).
        if (idle != null) anim.clips.Add(new SpriteAnimator2D.Clip { name = "Idle", frames = idle, fps = 1, loop = true });
        if (idle != null && nod != null)
            anim.clips.Add(new SpriteAnimator2D.Clip { name = "Reposo", frames = new[] { idle[0], idle[0], idle[0], nod[0] }, fps = 1.5f, loop = true });
        if (Get("Listening") != null) anim.clips.Add(new SpriteAnimator2D.Clip { name = "Listening", frames = Get("Listening"), fps = 4.5f, loop = true });
        if (Get("Asking") != null) anim.clips.Add(new SpriteAnimator2D.Clip { name = "Asking", frames = Get("Asking"), fps = 3.5f, loop = true });
        if (nod != null && idle != null)
            anim.clips.Add(new SpriteAnimator2D.Clip { name = "Nod", frames = new[] { idle[0], nod[0] }, fps = 1.2f, loop = true });
        if (react != null && idle != null)
            anim.clips.Add(new SpriteAnimator2D.Clip { name = "React", frames = new[] { react[0], idle[0] }, fps = 1.2f, loop = true });
        anim.defaultClip = (idle != null && nod != null) ? "Reposo" : "Idle";
        anim.bobSpeed = 1.3f;
        anim.bobAmplitude = 0.035f;

        anim.transform.localScale = Vector3.one;
        var sr = anim.GetComponent<SpriteRenderer>();
        if (idle != null) sr.sprite = idle[0];
        EditorUtility.SetDirty(anim);
        EditorUtility.SetDirty(sr);
        EditorSceneManager.MarkSceneDirty(anim.gameObject.scene);
        EditorSceneManager.SaveScene(anim.gameObject.scene);
        Debug.Log("[Principito] Animaciones asignadas: " + string.Join(", ", anim.clips.ConvertAll(c => $"{c.name}({c.frames.Length})")));
    }
}
