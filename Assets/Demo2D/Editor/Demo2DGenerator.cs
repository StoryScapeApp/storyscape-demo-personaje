using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// StoryScape – genera los sprites del personaje (pixel art) y la escena de proyección
/// Assets/Scenes/StoryScapeDemo.unity (fondo negro = sin luz sobre la mesa).
/// Menú: StoryScape > Generar demo personaje caminando
/// </summary>
public static class Demo2DGenerator
{
    const string Root = "Assets/Demo2D";
    const string SpriteDir = Root + "/Sprites";
    const string ScenePath = "Assets/Scenes/StoryScapeDemo.unity";
    const int PPU = 16;

    static readonly Color32 Outline = new Color32(25, 22, 30, 255);
    static readonly Color32 Skin = new Color32(250, 205, 165, 255);
    static readonly Color32 Hair = new Color32(95, 60, 40, 255);
    static readonly Color32 Eye = new Color32(35, 30, 45, 255);
    static readonly Color32 Shirt = new Color32(214, 72, 72, 255);
    static readonly Color32 ShirtDark = new Color32(165, 48, 55, 255);
    static readonly Color32 Pants = new Color32(60, 70, 130, 255);
    static readonly Color32 PantsDark = new Color32(42, 50, 98, 255);
    static readonly Color32 Shoe = new Color32(70, 45, 35, 255);

    // ───────────────────────── Lienzo de píxeles ─────────────────────────
    class Canvas
    {
        public readonly int W, H;
        public readonly Color32[] P;
        public Canvas(int w, int h) { W = w; H = h; P = new Color32[w * h]; }
        public void Set(int x, int y, Color32 c) { if (x < 0 || y < 0 || x >= W || y >= H) return; P[y * W + x] = c; }
        public void Rect(int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(x, y, c);
        }
        public static float EllipseValue(int x, int y, float cx, float cy, float rx, float ry)
        {
            float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
            return dx * dx + dy * dy;
        }
        public void AddOutline(Color32 o)
        {
            var src = (Color32[])P.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (src[y * W + x].a == 255) continue;
                    if (Opaque(src, x + 1, y) || Opaque(src, x - 1, y) || Opaque(src, x, y + 1) || Opaque(src, x, y - 1))
                        P[y * W + x] = o;
                }
        }
        bool Opaque(Color32[] a, int x, int y) => x >= 0 && y >= 0 && x < W && y < H && a[y * W + x].a == 255;
        public Canvas Mirror()
        {
            var m = new Canvas(W, H);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) m.P[y * W + (W - 1 - x)] = P[y * W + x];
            return m;
        }
    }

    // ───────────────────────── Personaje ─────────────────────────
    // dir: 0 abajo, 1 arriba, 2 derecha. frame: 0 quieto, 1 y 2 pasos.
    static Canvas DrawCharacter(int dir, int frame)
    {
        var c = new Canvas(16, 16);
        if (dir == 2) DrawSide(c, frame);
        else DrawFrontBack(c, dir == 1, frame);
        c.AddOutline(Outline);
        return c;
    }

    static void Leg(Canvas c, int x, bool lifted)
    {
        int b = lifted ? 2 : 1;
        c.Rect(x, b + 1, x + 1, 4, Pants);
        c.Rect(x, b, x + 1, b, Shoe);
    }

    static void Arm(Canvas c, int x, bool up)
    {
        if (up) { c.Rect(x, 7, x, 8, ShirtDark); c.Set(x, 6, Skin); }
        else { c.Rect(x, 6, x, 8, ShirtDark); c.Set(x, 5, Skin); }
    }

    static void DrawFrontBack(Canvas c, bool back, int f)
    {
        bool lLift = f == 1, rLift = f == 2;
        Leg(c, 5, lLift);
        Leg(c, 9, rLift);
        c.Rect(5, 4, 10, 4, Pants);
        c.Rect(5, 5, 10, 8, Shirt);
        Arm(c, 4, rLift);
        Arm(c, 11, lLift);

        if (back)
        {
            c.Rect(4, 9, 11, 14, Hair);
        }
        else
        {
            c.Rect(4, 9, 11, 14, Skin);
            c.Rect(4, 13, 11, 14, Hair);
            c.Rect(4, 10, 4, 12, Hair);
            c.Rect(11, 10, 11, 12, Hair);
            c.Set(5, 12, Hair);
            c.Set(10, 12, Hair);
            c.Set(6, 11, Eye);
            c.Set(9, 11, Eye);
        }
    }

    static void DrawSide(Canvas c, int f)
    {
        if (f == 0)
        {
            c.Rect(6, 2, 9, 4, Pants);
            c.Rect(6, 1, 10, 1, Shoe);
        }
        else
        {
            Color32 backLeg = f == 1 ? PantsDark : Pants;
            Color32 frontLeg = f == 1 ? Pants : PantsDark;
            c.Rect(5, 2, 6, 4, backLeg); c.Rect(4, 1, 6, 1, Shoe);
            c.Rect(8, 2, 9, 4, frontLeg); c.Rect(8, 1, 10, 1, Shoe);
            c.Rect(6, 4, 8, 4, Pants);
        }

        c.Rect(6, 5, 9, 8, Shirt);

        c.Rect(5, 9, 10, 14, Skin);
        c.Rect(5, 13, 10, 14, Hair);
        c.Rect(5, 9, 6, 12, Hair);
        c.Set(7, 12, Hair);
        c.Set(11, 11, Skin);
        c.Set(9, 11, Eye);

        if (f == 0) { c.Rect(7, 6, 8, 8, ShirtDark); c.Set(8, 5, Skin); }
        else if (f == 1) { c.Rect(6, 6, 7, 8, ShirtDark); c.Set(5, 6, Skin); }
        else { c.Rect(8, 6, 9, 8, ShirtDark); c.Set(10, 6, Skin); }
    }

    // Anillo de luz que aparece bajo los pies al llegar (blanco: se colorea desde el SpriteRenderer).
    static Canvas DrawRing()
    {
        var c = new Canvas(32, 16);
        for (int y = 0; y < c.H; y++)
            for (int x = 0; x < c.W; x++)
            {
                float v = Canvas.EllipseValue(x, y, 16f, 8f, 14.5f, 6.5f);
                float inner = Canvas.EllipseValue(x, y, 16f, 8f, 11.5f, 4.5f);
                if (v <= 1f && inner > 1f) c.Set(x, y, new Color32(255, 255, 255, 255));
                else if (v <= 1f) c.Set(x, y, new Color32(255, 255, 255, 45)); // relleno tenue
            }
        return c;
    }

    // ───────────────────────── Guardado de sprites ─────────────────────────
    static Sprite Save(Canvas c, string name, Vector2 pivot)
    {
        string path = $"{SpriteDir}/{name}.png";
        var tex = new Texture2D(c.W, c.H, TextureFormat.RGBA32, false);
        tex.SetPixels32(c.P);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = PPU;
        ti.filterMode = FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;

        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)SpriteAlignment.Custom;
        s.spritePivot = pivot;
        ti.SetTextureSettings(s);
        ti.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ───────────────────────── Escena ─────────────────────────
    [MenuItem("StoryScape/Generar demo personaje caminando")]
    public static void Generate()
    {
        Directory.CreateDirectory(SpriteDir);

        var feet = new Vector2(0.5f, 0f);
        string[] dirNames = { "abajo", "arriba", "derecha", "izquierda" };
        var frames = new Sprite[4][];
        for (int d = 0; d < 4; d++)
        {
            frames[d] = new Sprite[3];
            for (int f = 0; f < 3; f++)
            {
                Canvas cv = d == 3 ? DrawCharacter(2, f).Mirror() : DrawCharacter(d, f);
                frames[d][f] = Save(cv, $"personaje_{dirNames[d]}_{f}", feet);
            }
        }
        var ring = Save(DrawRing(), "anillo_llegada", new Vector2(0.5f, 0.5f));

        // Escena: copia de SampleScene (mantiene la cámara y la Global Light 2D de URP)
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
        UnityEngine.SceneManagement.Scene scene;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null
            && AssetDatabase.CopyAsset("Assets/Scenes/SampleScene.unity", ScenePath))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // Cámara fija sobre el área proyectada, fondo negro (= sin luz en la mesa)
        Camera cam = Camera.main;
        if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = cgo.AddComponent<Camera>();
        }
        cam.orthographic = true;
        cam.orthographicSize = 4f;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        // Puntos del recorrido
        var points = new GameObject("Recorrido").transform;
        var start = new GameObject("PuntoInicio").transform;
        start.SetParent(points, false);
        start.localPosition = new Vector3(-8.2f, -1.5f, 0f); // fuera del área: entra caminando
        var final = new GameObject("PuntoFinal").transform;
        final.SetParent(points, false);
        final.localPosition = new Vector3(2.5f, 0.3f, 0f);

        // Anillo de llegada en el punto final
        var ringGo = new GameObject("AnilloLlegada");
        ringGo.transform.SetParent(final, false);
        ringGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        var ringSr = ringGo.AddComponent<SpriteRenderer>();
        ringSr.sprite = ring;
        ringSr.color = new Color(1f, 0.9f, 0.45f, 0f);
        ringSr.sortingOrder = 0;

        // Personaje
        var character = new GameObject("Personaje");
        character.transform.position = start.position;
        var psr = character.AddComponent<SpriteRenderer>();
        psr.sprite = frames[2][0];
        psr.sortingOrder = 10;

        var walker = character.AddComponent<CharacterPathWalker2D>();
        walker.downFrames = frames[0];
        walker.upFrames = frames[1];
        walker.rightFrames = frames[2];
        walker.leftFrames = frames[3];
        walker.startPoint = start;
        walker.finalPoint = final;
        walker.arrivalRing = ringSr;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == ScenePath);
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();

        Selection.activeGameObject = character;
        Debug.Log("[StoryScape] Escena generada en " + ScenePath);
    }
}
