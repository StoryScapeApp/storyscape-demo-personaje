using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Procesa las hojas de sprites del Principito (imágenes con fondo azul verdoso y cuadrícula):
/// 1) quita el fondo, la cuadrícula y los huecos de fondo encerrados en el dibujo,
/// 2) separa cada cuadro (componentes conectados grandes; ignora títulos y etiquetas),
/// 3) alinea todos los cuadros en un lienzo común: por los pies, o por la línea de suelo de la fila
///    en hojas con saltos (para conservar la altura del salto),
/// 4) guarda PNG transparentes en Assets/Onboarding/Sprites/Principito y los asigna al Principito.
///
/// Hojas reconocidas por nombre de archivo:
///   personaje_posicion_1 → Listening (8) + Idle + Nod + React
///   personaje_posicion_2 → Asking (8)
///   personaje_posicion_3 → Correcto (alegría, con saltos)
///   personaje_posicion_4 → Incorrecto (desaprobación)
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

    static readonly Color32 BgColor = new Color32(48, 82, 96, 255);

    class Frame
    {
        public Color32[] px; public int w, h;     // imagen fuente completa
        public List<int> pixels = new List<int>();
        public int minX = int.MaxValue, minY = int.MaxValue, maxX, maxY; // y desde abajo (Unity)
        public float anchorX; public int anchorY;   // punto que queda en los pies del sprite
        public float cx => (minX + maxX) * 0.5f;
        public float cy => (minY + maxY) * 0.5f;
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
            string name = Path.GetFileNameWithoutExtension(file).ToLower();
            bool keepJumps = name.EndsWith("_3");
            var frames = ExtractFrames(file, keepJumps);
            Debug.Log($"[Principito] {Path.GetFileName(file)}: {frames.Count} cuadros detectados.");

            if (name.EndsWith("_1") && frames.Count >= 11)
            {
                clips["Listening"] = frames.GetRange(0, 8);
                clips["Idle"] = new List<Frame> { frames[8] };
                clips["Nod"] = new List<Frame> { frames[9] };
                clips["React"] = new List<Frame> { frames[10] };
            }
            else if (name.EndsWith("_2")) clips["Asking"] = frames;
            else if (name.EndsWith("_3")) clips["Correcto"] = frames;
            else if (name.EndsWith("_4")) clips["Incorrecto"] = frames;
            else
            {
                clips[Path.GetFileNameWithoutExtension(file)] = frames;
                Debug.LogWarning($"[Principito] {Path.GetFileName(file)}: nombre no reconocido; se guarda con su propio nombre.");
            }
        }

        // Lienzo común para todos los cuadros (misma escala y pies en el mismo punto)
        int halfW = 0, above = 0, below = 0;
        foreach (var kv in clips)
            foreach (var f in kv.Value)
            {
                halfW = Mathf.Max(halfW, Mathf.CeilToInt(Mathf.Max(f.anchorX - f.minX, f.maxX - f.anchorX)) + 1);
                above = Mathf.Max(above, f.maxY - f.anchorY + 1);
                below = Mathf.Max(below, f.anchorY - f.minY);
            }
        int cw = halfW * 2 + Pad * 2, ch = above + below + Pad * 2;
        int groundY = Pad + below;

        Directory.CreateDirectory(OutDir);
        var written = new HashSet<string>();
        var paths = new List<string>();
        foreach (var kv in clips)
        {
            for (int i = 0; i < kv.Value.Count; i++)
            {
                var f = kv.Value[i];
                var outPx = new Color32[cw * ch];
                int offX = Pad + halfW - Mathf.RoundToInt(f.anchorX);
                int offY = groundY - f.anchorY;
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
                File.WriteAllBytes(path, tex.EncodeToPNG()); // se sobrescribe: el .meta (y sus referencias) se conserva
                Object.DestroyImmediate(tex);
                paths.Add(path);
                written.Add(path);
            }
        }
        // Quita solo los PNG que ya no corresponden a ningún cuadro
        foreach (var old in Directory.GetFiles(OutDir, "*.png"))
        {
            var p = old.Replace('\\', '/');
            if (!written.Contains(p)) AssetDatabase.DeleteAsset(p);
        }

        AssetDatabase.Refresh();
        float pivotY = (float)groundY / ch;
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
        // Familia azul verdosa del fondo, la cuadrícula y las sombras: azul > verde > rojo, poco rojo.
        return c.b - c.g >= 4 && c.g - c.r >= 15 && c.b - c.r >= 25 && c.r < 130;
    }

    static int Dist2(Color32 a, Color32 b)
    {
        int dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
        return dr * dr + dg * dg + db * db;
    }

    static bool IsExactBackground(Color32 c) => IsBackground(c) && Dist2(c, BgColor) < 22 * 22;

    static List<Frame> ExtractFrames(string file, bool keepJumps)
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
            if (region.Count >= MinHoleArea) foreach (int i in region) bg[i] = true;
        }

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

        // Pedazos sueltos (mechones, puntas de capa, trazos de movimiento) cerca de un cuadro grande → se unen
        foreach (var s in small)
            foreach (var b in big)
                if (s.cx >= b.minX - 25 && s.cx <= b.maxX + 25 && s.minY >= b.minY - 15 && s.maxY <= b.maxY + 15)
                {
                    b.pixels.AddRange(s.pixels);
                    b.minX = Mathf.Min(b.minX, s.minX); b.maxX = Mathf.Max(b.maxX, s.maxX);
                    b.minY = Mathf.Min(b.minY, s.minY); b.maxY = Mathf.Max(b.maxY, s.maxY);
                    break;
                }

        // 3) Orden de lectura: filas de arriba hacia abajo, luego izquierda a derecha
        float rowTol = H * 0.08f;
        big.Sort((a, c) =>
        {
            if (Mathf.Abs(a.cy - c.cy) > rowTol && !keepJumps) return c.cy.CompareTo(a.cy); // y mayor = más arriba
            return a.cx.CompareTo(c.cx);
        });

        // 4) Ancla horizontal en los pies; vertical en los pies o en el suelo común (saltos)
        int ground = int.MaxValue;
        foreach (var b in big) ground = Mathf.Min(ground, b.minY);
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
            b.anchorY = keepJumps ? ground : b.minY;
        }
        return big;
    }

    // ───────────── Cuadros sueltos (PNG transparentes) ─────────────
    /// Importa los PNG de raw/<carpeta> sin redimensionarlos. El tamaño en escena se fija con la escala de
    /// importación (Pixels Per Unit): la altura de la imagen equivale a la altura del Principito en Idle,
    /// así que con la escala del objeto Principito cada píxel del PNG se proyecta como un píxel (1:1).
    /// El pivote se coloca en los pies de cada cuadro para que no salte al animar.
    public static Sprite[] ImportFrameFolder(string folder)
    {
        string dir = null;
        foreach (var d in SourceDirs) if (Directory.Exists(d + "/" + folder)) { dir = d + "/" + folder; break; }
        if (dir == null) return null;
        var files = new List<string>(Directory.GetFiles(dir, "*.png"));
        files.Sort();
        if (files.Count == 0) return null;

        float idleWorld = IdleWorldHeight();
        var result = new List<Sprite>();
        foreach (var f0 in files)
        {
            string f = f0.Replace('\\', '/');
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(f));
            int W = tex.width, H = tex.height;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);

            int minY = H, maxY = 0;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (px[y * W + x].a > 20) { if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (minY > maxY) { minY = 0; maxY = H - 1; }
            int band = Mathf.Max(2, (maxY - minY) / 14);
            float sum = 0; int cnt = 0;
            for (int y = minY; y <= minY + band && y < H; y++) for (int x = 0; x < W; x++) if (px[y * W + x].a > 20) { sum += x + 0.5f; cnt++; }
            float ax = cnt > 0 ? sum / cnt : W / 2f;

            var ti = (TextureImporter)AssetImporter.GetAtPath(f);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = H / idleWorld;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            var st = new TextureImporterSettings();
            ti.ReadTextureSettings(st);
            st.spriteAlignment = (int)SpriteAlignment.Custom;
            st.spritePivot = new Vector2(Mathf.Round(ax) / W, (float)minY / H);
            st.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(st);
            ti.SaveAndReimport();
            result.Add(AssetDatabase.LoadAssetAtPath<Sprite>(f));
        }
        Debug.Log($"[Principito] {folder}: {result.Count} cuadros importados sin redimensionar (PPU {result[0].pixelsPerUnit:0.0}).");
        return result.ToArray();
    }

    /// Altura del Principito en Idle, en unidades de escena (sin la escala del objeto).
    static float IdleWorldHeight()
    {
        var idle = AssetDatabase.LoadAssetAtPath<Sprite>(OutDir + "/Idle_0.png");
        if (idle == null) return 376f / PixelsPerUnit;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(OutDir + "/Idle_0.png"));
        var px = tex.GetPixels32(); int W = tex.width, H = tex.height, minY = H, maxY = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (px[y * W + x].a > 20) { if (y < minY) minY = y; if (y > maxY) maxY = y; }
        Object.DestroyImmediate(tex);
        return (maxY - minY + 1) / idle.pixelsPerUnit;
    }

    [MenuItem("StoryScape/Onboarding/Importar sprites de duda")]
    public static void ImportDudaMenu()
    {
        var anim = Object.FindFirstObjectByType<SpriteAnimator2D>();
        var frames = ImportFrameFolder("duda");
        if (anim == null || frames == null) { Debug.LogWarning("[Principito] Falta la escena Onboarding o la carpeta raw/duda."); return; }
        anim.clips.RemoveAll(c => c.name == "Duda");
        anim.clips.Add(new SpriteAnimator2D.Clip { name = "Duda", frames = frames, fps = 4f, loop = true });
        var seq = Object.FindFirstObjectByType<OnboardingSequence>();
        if (seq != null) { seq.animDoubt = "Duda"; EditorUtility.SetDirty(seq); }
        EditorUtility.SetDirty(anim);
        EditorSceneManager.MarkSceneDirty(anim.gameObject.scene);
        EditorSceneManager.SaveScene(anim.gameObject.scene);
        Debug.Log("[Principito] Animación \"Duda\" asignada al momento ¿Una duda?");
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
        if (Get("Correcto") != null) anim.clips.Add(new SpriteAnimator2D.Clip { name = "Correcto", frames = Get("Correcto"), fps = 4f, loop = true });
        if (Get("Incorrecto") != null) anim.clips.Add(new SpriteAnimator2D.Clip { name = "Incorrecto", frames = Get("Incorrecto"), fps = 2.5f, loop = true });
        if (nod != null && idle != null)
            anim.clips.Add(new SpriteAnimator2D.Clip { name = "Nod", frames = new[] { idle[0], nod[0] }, fps = 1.2f, loop = true });
        if (react != null && idle != null)
            anim.clips.Add(new SpriteAnimator2D.Clip { name = "React", frames = new[] { react[0], idle[0] }, fps = 1.2f, loop = true });
        var duda = ImportFrameFolder("duda");
        if (duda != null && duda.Length > 0)
            anim.clips.Add(new SpriteAnimator2D.Clip { name = "Duda", frames = duda, fps = 4f, loop = true });
        anim.defaultClip = (idle != null && nod != null) ? "Reposo" : "Idle";

        var sr = anim.GetComponent<SpriteRenderer>();
        if (idle != null) sr.sprite = idle[0];
        EditorUtility.SetDirty(anim);
        EditorUtility.SetDirty(sr);
        EditorSceneManager.MarkSceneDirty(anim.gameObject.scene);
        EditorSceneManager.SaveScene(anim.gameObject.scene);
        Debug.Log("[Principito] Animaciones asignadas: " + string.Join(", ", anim.clips.ConvertAll(c => $"{c.name}({c.frames.Length})")));
    }
}
