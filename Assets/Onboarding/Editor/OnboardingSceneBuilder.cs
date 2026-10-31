using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Genera la escena Assets/Scenes/Onboarding.unity:
/// Principito a la izquierda, reglas y ejemplo del juego de preguntas a la derecha, fondo negro.
/// Menús:
///  - StoryScape > Onboarding > Generar escena
///  - StoryScape > Onboarding > Asignar audios (busca Assets/Onboarding/Audio/&lt;id&gt;.*)
/// </summary>
public static class OnboardingSceneBuilder
{
    const string Root = "Assets/Onboarding";
    const string SpriteDir = Root + "/Sprites";
    const string AudioDir = Root + "/Audio";
    const string ScenePath = "Assets/Scenes/Onboarding.unity";

    // Columna derecha (reglas y juego) y columna izquierda (Principito)
    const float RightX = 4.9f;
    const float QuizX = 4.7f;
    const float LeftX = -5f;

    // ───────────── Menús ─────────────
    [MenuItem("StoryScape/Onboarding/Generar escena")]
    public static void Build()
    {
        Directory.CreateDirectory(SpriteDir);
        Directory.CreateDirectory(AudioDir);
        AssetDatabase.Refresh();

        // Toma la configuración del micrófono de la demo anterior (si está abierta)
        string micDevice = "";
        float micGain = 8f;
        var agent = Object.FindFirstObjectByType<ElevenLabsAgentClient>();
        if (agent != null) { micDevice = agent.microphoneDevice; micGain = agent.micGain; }

        var fillSprite = SaveSprite(DrawRounded(false), "card_fondo", new Vector4(24, 24, 24, 24));
        var borderSprite = SaveSprite(DrawRounded(true), "card_borde", new Vector4(24, 24, 24, 24));
        var starSprite = SaveSprite(DrawStar(), "estrella", Vector4.zero);

        // Escena: copia de SampleScene (cámara + Global Light 2D de URP)
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
        UnityEngine.SceneManagement.Scene scene;
        if (AssetDatabase.CopyAsset("Assets/Scenes/SampleScene.unity", ScenePath))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 4f;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        // ── Principito (izquierda)
        var prinGo = new GameObject("Principito");
        prinGo.transform.position = new Vector3(LeftX, -1.6f, 0f);
        prinGo.transform.localScale = Vector3.one * 2.5f;
        var prinSr = prinGo.AddComponent<SpriteRenderer>();
        prinSr.sortingOrder = 10;
        var anim = prinGo.AddComponent<SpriteAnimator2D>();
        AddPlaceholderClips(anim);
        prinSr.sprite = anim.clips[0].frames[0];

        var bubble = CreateCard("GloboDialogo", null, new Vector3(LeftX, 1.9f, 0f), new Vector2(2.8f, 0.75f),
            new Color(0.55f, 0.92f, 0.75f), "¡Muy bien!", 4f, fillSprite, borderSprite, TextAlignmentOptions.Center);

        // ── Voz y escucha
        var voiceGo = new GameObject("VozPrincipito");
        var voice = voiceGo.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;
        var listener = voiceGo.AddComponent<VoiceActivityWaiter>();
        listener.microphoneDevice = micDevice;
        listener.micGain = micGain;

        // ── Reglas (derecha)
        var right = new GameObject("PanelDerecho").transform;
        var title = CreateText("Reglas_Titulo", right, new Vector3(RightX, 3.3f, 0f), new Vector2(4.2f, 0.7f),
            "<b>Reglas:</b>", 3.6f, TextAlignmentOptions.TopLeft);
        var rules = new ProjectedCard[3];
        string[] ruleTexts =
        {
            "<b>1. Lee en voz alta.</b> Así yo puedo escucharte y acompañarte en la lectura.",
            "<b>2. Si tienes una duda, pídeme ayuda.</b> Solo tienes que decir: <i>\"Principito, tengo una duda\"</i>, y yo te ayudaré.",
            "<b>3. Al terminar cada capítulo, jugaremos a las preguntas.</b> Te haré dos preguntas sobre lo que leíste. ¡No te preocupes, no es un examen, es un juego!"
        };
        float[] ruleY = { 2.15f, 0.95f, -0.35f };
        float[] ruleH = { 1.5f, 1.5f, 1.7f };
        for (int i = 0; i < 3; i++)
            rules[i] = CreateText($"Regla_{i + 1}", right, new Vector3(RightX, ruleY[i], 0f), new Vector2(4.2f, ruleH[i]),
                ruleTexts[i], 2.6f, TextAlignmentOptions.TopLeft);

        // ── Ejemplo del juego de preguntas (derecha)
        var quiz = new GameObject("EjemploPreguntas").transform;
        var question = CreateCard("Pregunta", quiz, new Vector3(QuizX, 2.5f, 0f), new Vector2(3.8f, 1.05f),
            new Color(0.92f, 0.88f, 0.8f), "¿Cómo vas a leer el cuento?", 4f, fillSprite, borderSprite, TextAlignmentOptions.Center);
        var options = new ProjectedCard[3];
        options[0] = CreateCard("Opcion_Azul", quiz, new Vector3(QuizX, 1.15f, 0f), new Vector2(3.8f, 0.85f),
            new Color(0.35f, 0.62f, 1f), "En silencio", 4f, fillSprite, borderSprite, TextAlignmentOptions.Center);
        options[1] = CreateCard("Opcion_Verde", quiz, new Vector3(QuizX, 0.0f, 0f), new Vector2(3.8f, 0.85f),
            new Color(0.35f, 0.88f, 0.45f), "En voz alta", 4f, fillSprite, borderSprite, TextAlignmentOptions.Center);
        options[2] = CreateCard("Opcion_Naranja", quiz, new Vector3(QuizX, -1.15f, 0f), new Vector2(3.8f, 0.85f),
            new Color(1f, 0.65f, 0.3f), "Cantando", 4f, fillSprite, borderSprite, TextAlignmentOptions.Center);

        var starsGo = new GameObject("Estrellas");
        var stars = starsGo.AddComponent<StarBurst>();
        stars.star = starSprite;
        stars.starSize = 0.55f;

        // ── Secuencia
        var seqGo = new GameObject("Onboarding");
        var seq = seqGo.AddComponent<OnboardingSequence>();
        seq.principito = anim;
        seq.voice = voice;
        seq.listener = listener;
        seq.rulesTitle = title;
        seq.rules = rules;
        seq.question = question;
        seq.options = options;
        seq.bubble = bubble;
        seq.stars = stars;
        AssignAudioClips(seq);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == ScenePath);
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();

        Selection.activeGameObject = seqGo;
        Debug.Log("[Onboarding] Escena generada en " + ScenePath);
    }

    [MenuItem("StoryScape/Onboarding/Asignar audios")]
    public static void AssignAudiosMenu()
    {
        var seq = Object.FindFirstObjectByType<OnboardingSequence>();
        if (seq == null) { Debug.LogWarning("[Onboarding] Abre la escena Onboarding primero."); return; }
        AssetDatabase.Refresh();
        int n = AssignAudioClips(seq);
        EditorUtility.SetDirty(seq);
        EditorSceneManager.MarkSceneDirty(seq.gameObject.scene);
        EditorSceneManager.SaveScene(seq.gameObject.scene);
        Debug.Log($"[Onboarding] Audios asignados: {n} de 10.");
    }

    static int AssignAudioClips(OnboardingSequence seq)
    {
        if (!AssetDatabase.IsValidFolder(AudioDir)) return 0;
        int n = 0;
        foreach (var line in new[] { seq.intro, seq.nameReply, seq.rule1, seq.rule2, seq.rule3,
                                     seq.quizIntro, seq.quizWrong, seq.quizRight, seq.closing, seq.farewell })
        {
            var guids = AssetDatabase.FindAssets(line.id + " t:AudioClip", new[] { AudioDir });
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == line.id)
                {
                    line.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                    n++;
                    break;
                }
            }
        }
        return n;
    }

    // ───────────── Principito provisional (se reemplaza con los sprites reales) ─────────────
    static void AddPlaceholderClips(SpriteAnimator2D anim)
    {
        Sprite S(string n) => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Demo2D/Sprites/{n}.png");
        anim.clips.Clear();
        anim.clips.Add(new SpriteAnimator2D.Clip { name = "Idle", frames = new[] { S("personaje_habla_0") }, fps = 1, loop = true });
        anim.clips.Add(new SpriteAnimator2D.Clip { name = "Listening", frames = new[] { S("personaje_abajo_0"), S("personaje_abajo_1"), S("personaje_abajo_0"), S("personaje_abajo_2") }, fps = 4, loop = true });
        anim.clips.Add(new SpriteAnimator2D.Clip { name = "Asking", frames = new[] { S("personaje_habla_1"), S("personaje_habla_0") }, fps = 3, loop = true });
        anim.clips.Add(new SpriteAnimator2D.Clip { name = "Nod", frames = new[] { S("personaje_abajo_0"), S("personaje_habla_0") }, fps = 4, loop = true });
        anim.clips.Add(new SpriteAnimator2D.Clip { name = "React", frames = new[] { S("personaje_habla_2"), S("personaje_habla_0") }, fps = 5, loop = true });
        anim.defaultClip = "Idle";
    }

    // ───────────── Creación de elementos ─────────────
    static ProjectedCard CreateCard(string name, Transform parent, Vector3 pos, Vector2 size, Color color, string text,
                                    float fontSize, Sprite fillSprite, Sprite borderSprite, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        go.transform.position = pos;

        var fill = new GameObject("Fondo").AddComponent<SpriteRenderer>();
        fill.transform.SetParent(go.transform, false);
        fill.sprite = fillSprite;
        fill.drawMode = SpriteDrawMode.Sliced;
        fill.size = size;
        fill.sortingOrder = 5;

        var border = new GameObject("Borde").AddComponent<SpriteRenderer>();
        border.transform.SetParent(go.transform, false);
        border.sprite = borderSprite;
        border.drawMode = SpriteDrawMode.Sliced;
        border.size = size;
        border.sortingOrder = 6;

        var label = NewTmp("Texto", go.transform, Vector3.zero, size - new Vector2(0.3f, 0.15f), text, fontSize, align);

        var card = go.AddComponent<ProjectedCard>();
        card.fill = fill;
        card.border = border;
        card.label = label;
        card.color = color;
        return card;
    }

    static ProjectedCard CreateText(string name, Transform parent, Vector3 pos, Vector2 size, string text,
                                    float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var label = NewTmp("Texto", go.transform, Vector3.zero, size, text, fontSize, align);
        var card = go.AddComponent<ProjectedCard>();
        card.label = label;
        return card;
    }

    static TextMeshPro NewTmp(string name, Transform parent, Vector3 localPos, Vector2 size, string text, float fontSize,
                              TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.rectTransform.sizeDelta = size;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.color = Color.white;
        tmp.sortingOrder = 7;
        return tmp;
    }

    // ───────────── Gráficos generados ─────────────
    static Color32[] DrawRounded(bool borderOnly)
    {
        const int N = 64;
        const float r = 20f, thickness = 4f;
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - N / 2f) - (N / 2f - 1f - r);
                float qy = Mathf.Abs(y + 0.5f - N / 2f) - (N / 2f - 1f - r);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r; // distancia con signo
                float a = borderOnly
                    ? Mathf.Clamp01(thickness / 2f + 0.5f - Mathf.Abs(d + thickness / 2f))
                    : Mathf.Clamp01(0.5f - d);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        return px;
    }

    static Color32[] DrawStar()
    {
        const int N = 64;
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float ang = Mathf.PI / 2f + i * Mathf.PI / 5f;
            float rad = (i % 2 == 0) ? 30f : 12f;
            pts[i] = new Vector2(32f + Mathf.Cos(ang) * rad, 32f + Mathf.Sin(ang) * rad);
        }
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int inside = 0;
                for (int s = 0; s < 4; s++)
                {
                    var p = new Vector2(x + 0.25f + (s % 2) * 0.5f, y + 0.25f + (s / 2) * 0.5f);
                    if (InPolygon(p, pts)) inside++;
                }
                px[y * N + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 4));
            }
        return px;
    }

    static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                c = !c;
        return c;
    }

    static Sprite SaveSprite(Color32[] px, string name, Vector4 border)
    {
        string path = $"{SpriteDir}/{name}.png";
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 100;
        ti.filterMode = FilterMode.Bilinear;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteMeshType = SpriteMeshType.FullRect;
        s.spriteBorder = border;
        ti.SetTextureSettings(s);
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
