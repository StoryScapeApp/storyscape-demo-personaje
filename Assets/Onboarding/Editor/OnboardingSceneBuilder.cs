using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Genera la escena Assets/Scenes/Onboarding.unity con el flujo de 6 momentos:
/// Principito a la izquierda del libro, tarjetas 1–3 arriba, ejemplos a la derecha
/// (meteoro y planeta, significado de "Dedicatoria", panel del quiz) y fondo negro.
/// El objeto "LibroReferencia" muestra en la vista Scene dónde cae el libro físico (no se proyecta).
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

    // Paleta StoryScape
    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    static readonly Color Planeta = Hex("#4FB3E8");
    static readonly Color Rosa = Hex("#F2727F");
    static readonly Color Estrella = Hex("#F7C948");
    static readonly Color Violeta = Hex("#9B6CF2");
    static readonly Color Gris = Hex("#D9D2CF");

    // Distribución (unidades de escena; cámara ortográfica de tamaño 4)
    static readonly Vector3 PrincipitoPos = new Vector3(-3.9f, -2.0f, 0f);
    const float PrincipitoScale = 0.768f;   // Principito de 260 px de alto en una proyección de 1920×1080
    // Área proyectada 1920×1080 px = 80×44.5 cm (1 unidad = 135 px ≈ 5.56 cm). Libro 744×504 px (31×21 cm), centrado.
    static readonly Vector3 BookCenter = Vector3.zero;
    static readonly Vector2 BookSize = new Vector2(5.511f, 3.733f);
    static readonly Vector3 MarkerCenter = new Vector3(-1.311f, 0.932f, 0f);   // relativo al centro del libro (según el PDF)
    const float MarkerFrameSize = 1.6f;
    static readonly Vector3 WordCenter = new Vector3(1.314f, 0.657f, 0f);    // "DEDICATORIA", relativo al centro del libro
    static readonly Vector2 WordSize = new Vector2(0.7f, 0.22f);
    const float CardsY = 2.5f;
    static readonly float[] CardsX = { -3.56f, 0.1f, 3.73f };
    static readonly Vector2 CardSize = new Vector2(2.7f, 1.0f);
    const float RightX = 4.45f;

    // ───────────── Menús ─────────────
    [MenuItem("StoryScape/Onboarding/Generar escena")]
    public static void Build()
    {
        Directory.CreateDirectory(SpriteDir);
        Directory.CreateDirectory(AudioDir);
        AssetDatabase.Refresh();

        // Conserva la configuración del micrófono de la escena abierta (si existe)
        string micDevice = "";
        float micGain = 8f, endSilence = 0.5f;
        var oldWaiter = Object.FindFirstObjectByType<VoiceActivityWaiter>();
        if (oldWaiter != null) { micDevice = oldWaiter.microphoneDevice; micGain = oldWaiter.micGain; endSilence = oldWaiter.endSilence; }
        else
        {
            var oldAgent = Object.FindFirstObjectByType<ElevenLabsAgentClient>();
            if (oldAgent != null) { micDevice = oldAgent.microphoneDevice; micGain = oldAgent.micGain; }
        }

        var fillSprite = SaveSprite(DrawRounded(false), 64, "card_fondo", new Vector4(24, 24, 24, 24));
        var borderSprite = SaveSprite(DrawRounded(true), 64, "card_borde", new Vector4(24, 24, 24, 24));
        var starSprite = SaveSprite(DrawStar(), 64, "estrella", Vector4.zero);
        var dashSprite = SaveSprite(DrawDashedFrame(256), 256, "marco_marcador", Vector4.zero);
        var planetSprite = SaveSprite(DrawPlanet(192), 192, "planeta", Vector4.zero);

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

        // ── Referencia del libro físico (solo en la vista Scene, no se proyecta)
        var bookRef = new GameObject("LibroReferencia");
        bookRef.transform.position = BookCenter;
        var gizmo = bookRef.AddComponent<SceneAreaGizmo>();
        gizmo.size = BookSize;
        gizmo.color = new Color(1f, 1f, 1f, 0.5f);
        gizmo.label = "Libro físico (no se proyecta)";

        // ── Principito (izquierda del libro)
        var prinGo = new GameObject("Principito");
        prinGo.transform.position = PrincipitoPos;
        prinGo.transform.localScale = Vector3.one * PrincipitoScale;
        var prinSr = prinGo.AddComponent<SpriteRenderer>();
        prinSr.sortingOrder = 10;
        var anim = prinGo.AddComponent<SpriteAnimator2D>();
        anim.defaultClip = "Reposo";
        anim.bobSpeed = 1.3f;
        anim.bobAmplitude = 0.03f;

        // ── Voz y escucha
        var voiceGo = new GameObject("VozPrincipito");
        var voice = voiceGo.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;
        var listener = voiceGo.AddComponent<VoiceActivityWaiter>();
        listener.microphoneDevice = micDevice;
        listener.micGain = micGain;
        listener.endSilence = endSilence;

        // ── Tarjetas superiores
        var cards = new GameObject("Tarjetas").transform;
        var cardBook = CreateCard("Tarjeta_1_Libro", cards, new Vector3(CardsX[0], CardsY, 0f), CardSize, Violeta,
            "<b>1. En el libro</b>\n<size=75%>No tapes el cuadro</size>", 2.2f, fillSprite, borderSprite, TextAlignmentOptions.Center);
        var cardRead = CreateCard("Tarjeta_2_VozAlta", cards, new Vector3(CardsX[1], CardsY, 0f), CardSize, Violeta,
            "<b>2. Lee en voz alta</b>\n<size=75%>Para que puedas ver animaciones durante la historia</size>", 2.2f, fillSprite, borderSprite, TextAlignmentOptions.Center);
        var cardDoubt = CreateCard("Tarjeta_3_Duda", cards, new Vector3(CardsX[2], CardsY, 0f), CardSize, Violeta,
            "<b>3. ¿Una duda?</b>\n<size=75%>Llama al Principito</size>", 2.2f, fillSprite, borderSprite, TextAlignmentOptions.Center);

        // ── 2. Bordes del área del marcador
        var frameGo = new GameObject("MarcoMarcador");
        frameGo.transform.SetParent(bookRef.transform, false);
        frameGo.transform.localPosition = MarkerCenter;
        var frameSr = frameGo.AddComponent<SpriteRenderer>();
        frameSr.sprite = dashSprite;
        frameSr.sortingOrder = 6;
        frameGo.transform.localScale = Vector3.one * (MarkerFrameSize / (256f / 100f));
        var markerFrame = frameGo.AddComponent<ProjectedCard>();
        markerFrame.border = frameSr;
        markerFrame.color = Estrella;
        markerFrame.fadeSpeed = 1.5f;

        // ── 3. Meteoro y planeta
        var planetGo = new GameObject("Planeta");
        planetGo.transform.position = new Vector3(4.1f, 2.35f, 0f);
        var planetSr = planetGo.AddComponent<SpriteRenderer>();
        planetSr.sprite = planetSprite;
        planetSr.sortingOrder = 4;
        planetGo.transform.localScale = Vector3.one * (1.7f / (192f / 100f));
        var planet = planetGo.AddComponent<ProjectedCard>();
        planet.fill = planetSr;
        planet.color = Color.white;
        planet.fillAlpha = 1f;
        planet.fadeSpeed = 1.5f;

        var meteorGo = new GameObject("Meteoro");
        var meteor = meteorGo.AddComponent<MeteorFlight>();
        var headGo = new GameObject("Cabeza");
        headGo.transform.SetParent(meteorGo.transform, false);
        var headSr = headGo.AddComponent<SpriteRenderer>();
        headSr.sprite = starSprite;
        headSr.sortingOrder = 30;
        headSr.color = new Color(1f, 0.85f, 0.35f, 0f);
        meteor.head = headSr;
        meteor.trailSprite = starSprite;
        Vector3[] path =
        {
            new Vector3(-2.8f, 3.2f, 0f), new Vector3(-0.6f, 3.3f, 0f), new Vector3(1.9f, 3.2f, 0f),
            new Vector3(2.75f, 2.8f, 0f), new Vector3(3.95f, 1.35f, 0f), new Vector3(4.3f, 0.3f, 0f)
        };
        meteor.waypoints = new Transform[path.Length];
        for (int i = 0; i < path.Length; i++)
        {
            var wp = new GameObject($"Punto_{i}").transform;
            wp.SetParent(meteorGo.transform, false);
            wp.position = path[i];
            meteor.waypoints[i] = wp;
        }

        // ── 4. Resaltado de "Dedicatoria" y su significado
        var word = CreateCard("ResaltadoPalabra", bookRef.transform, BookCenter + WordCenter, WordSize, Estrella, "", 1f, fillSprite, null, TextAlignmentOptions.Center);
        word.fillAlpha = 0.5f;
        var definition = CreateCard("Significado", null, new Vector3(RightX, -0.2f, 0f), new Vector2(2.4f, 1.7f), Gris,
            "<b><size=120%>Dedicatoria:</size></b>\nPalabras al inicio de un libro para regalárselo a alguien especial.", 2.4f,
            fillSprite, borderSprite, TextAlignmentOptions.MidlineLeft);
        definition.fillAlpha = 0.18f;

        // ── 5. Panel del quiz (solo mirar)
        var quiz = new GameObject("Quiz").transform;
        float panelTop = 1.55f, panelH = 3.85f;
        var panel = CreateCard("PanelQuiz", quiz, new Vector3(RightX, panelTop - panelH / 2f, 0f), new Vector2(2.6f, panelH), Gris,
            "", 1f, fillSprite, borderSprite, TextAlignmentOptions.Center);
        panel.fillAlpha = 0.12f;
        var title = CreateText("Quiz_Titulo", quiz, new Vector3(RightX, panelTop - 0.38f, 0f), new Vector2(2.4f, 0.6f),
            "<b>4. Quiz</b>\n<size=70%>Demo: solo mirar</size>", 2.5f, TextAlignmentOptions.Center);
        var question = CreateText("Pregunta", quiz, new Vector3(RightX, panelTop - 1.05f, 0f), new Vector2(2.4f, 0.6f),
            "<b>¿Cómo vas a leer el cuento?</b>", 2.3f, TextAlignmentOptions.Center);
        var options = new ProjectedCard[3];
        string[] optText = { "<b>A</b>   En silencio", "<b>B</b>   En voz alta", "<b>C</b>   Cantando" };
        Color[] optColor = { Planeta, Rosa, Estrella };
        string[] optName = { "Opcion_Planeta", "Opcion_Rosa", "Opcion_Estrella" };
        for (int i = 0; i < 3; i++)
            options[i] = CreateCard(optName[i], quiz, new Vector3(RightX, panelTop - 1.75f - i * 0.68f, 0f), new Vector2(2.2f, 0.52f),
                optColor[i], optText[i], 2.3f, fillSprite, borderSprite, TextAlignmentOptions.MidlineLeft);

        var starsGo = new GameObject("Estrellas");
        var stars = starsGo.AddComponent<StarBurst>();
        stars.star = starSprite;
        stars.starSize = 0.35f;
        stars.radius = 1.2f;

        // ── Secuencia
        var seqGo = new GameObject("Onboarding");
        var seq = seqGo.AddComponent<OnboardingSequence>();
        seq.startOnPlay = false;
        seq.principito = anim;
        seq.voice = voice;
        seq.listener = listener;
        seq.cardBook = cardBook;
        seq.markerFrame = markerFrame;
        seq.cardRead = cardRead;
        seq.meteor = meteor;
        seq.planet = planet;
        seq.cardDoubt = cardDoubt;
        seq.wordHighlight = word;
        seq.definition = definition;
        seq.quizPanel = panel;
        seq.quizTitle = title;
        seq.question = question;
        seq.options = options;
        seq.stars = stars;
        AssignAudioClips(seq);

        // ── ArUco (marker_0 inicia el onboarding), seguimiento de página y agente de dudas
        var recGo = new GameObject("ReceptorArUco");
        var rec = recGo.AddComponent<ArucoUdpReceiver>();
        rec.port = 5005;
        rec.showDebug = true;
        var action = new ArucoUdpReceiver.MarkerAction { description = "Primera página → Onboarding", markerId = 0, onlyOnce = true };
        UnityEventTools.AddPersistentListener(action.onDetected, seq.IniciarOnboarding);
        rec.actions.Add(action);

        var agentGo = new GameObject("AgenteElevenLabs");
        agentGo.AddComponent<AudioSource>();
        var agent = agentGo.AddComponent<ElevenLabsAgentClient>();
        agent.simulate = false;
        agent.microphoneDevice = micDevice;
        agent.micGain = micGain;
        agent.showSubtitles = false;

        var bookGo = new GameObject("LibroPaginas");
        var tracker = bookGo.AddComponent<BookPageTracker>();
        tracker.receiver = rec;
        tracker.agent = agent;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == ScenePath);
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();

        Selection.activeGameObject = seqGo;
        Debug.Log("[Onboarding] Escena generada en " + ScenePath + ". Ejecuta 'Procesar sprites del Principito' para asignar sus animaciones.");
    }

    [MenuItem("StoryScape/Onboarding/Asignar audios")]
    public static void AssignAudiosMenu()
    {
        var seq = Object.FindFirstObjectByType<OnboardingSequence>();
        if (seq == null) { Debug.LogWarning("[Onboarding] Abre la escena Onboarding primero."); return; }
        AssetDatabase.Refresh();
        int n = AssignAudioClips(seq, out var missing);
        EditorUtility.SetDirty(seq);
        EditorSceneManager.MarkSceneDirty(seq.gameObject.scene);
        EditorSceneManager.SaveScene(seq.gameObject.scene);
        Debug.Log($"[Onboarding] Audios asignados: {n}." + (missing.Count > 0 ? " Faltan: " + string.Join(", ", missing) : ""));
    }

    static int AssignAudioClips(OnboardingSequence seq) => AssignAudioClips(seq, out _);

    static int AssignAudioClips(OnboardingSequence seq, out List<string> missing)
    {
        missing = new List<string>();
        int n = 0;
        foreach (var line in new[] { seq.intro, seq.bookLine, seq.readLine, seq.doubtLine, seq.doubtExample,
                                     seq.quizLine, seq.quizIntro, seq.quizWrong, seq.quizRight, seq.farewell })
        {
            if (line == null || string.IsNullOrEmpty(line.id)) continue;
            line.clip = null;
            if (AssetDatabase.IsValidFolder(AudioDir))
                foreach (var g in AssetDatabase.FindAssets(line.id + " t:AudioClip", new[] { AudioDir }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (Path.GetFileNameWithoutExtension(p) == line.id)
                    {
                        line.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                        break;
                    }
                }
            if (line.clip != null) n++; else missing.Add(line.id);
        }
        return n;
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

        SpriteRenderer border = null;
        if (borderSprite != null)
        {
            border = new GameObject("Borde").AddComponent<SpriteRenderer>();
            border.transform.SetParent(go.transform, false);
            border.sprite = borderSprite;
            border.drawMode = SpriteDrawMode.Sliced;
            border.size = size;
            border.sortingOrder = 6;
        }

        TextMeshPro label = null;
        if (!string.IsNullOrEmpty(text))
            label = NewTmp("Texto", go.transform, Vector3.zero, size - new Vector2(0.3f, 0.12f), text, fontSize, align);

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
                float d = RoundedDist(x, y, N, r);
                float a = borderOnly
                    ? Mathf.Clamp01(thickness / 2f + 0.5f - Mathf.Abs(d + thickness / 2f))
                    : Mathf.Clamp01(0.5f - d);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        return px;
    }

    static float RoundedDist(int x, int y, int n, float r)
    {
        float qx = Mathf.Abs(x + 0.5f - n / 2f) - (n / 2f - 1f - r);
        float qy = Mathf.Abs(y + 0.5f - n / 2f) - (n / 2f - 1f - r);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    /// Marco discontinuo de esquinas redondeadas (bordes del área del marcador).
    static Color32[] DrawDashedFrame(int n)
    {
        const float r = 26f, thickness = 7f, dash = 22f;
        var px = new Color32[n * n];
        float straight = n - 2f - 2f * r;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = RoundedDist(x, y, n, r);
                float a = Mathf.Clamp01(thickness / 2f + 0.5f - Mathf.Abs(d + thickness / 2f));
                if (a <= 0f) continue;
                // Posición a lo largo del contorno para alternar trazos
                float cx = x + 0.5f - n / 2f, cy = y + 0.5f - n / 2f;
                float s = Mathf.Abs(cx) >= Mathf.Abs(cy) ? cy + (cx > 0 ? 0f : 1000f) : cx + (cy > 0 ? 2000f : 3000f);
                if (Mathf.Repeat(s + straight / 2f, dash * 2f) > dash) continue;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        return px;
    }

    /// Planeta azul con cráteres, anillo y estrellitas (paleta StoryScape).
    static Color32[] DrawPlanet(int n)
    {
        var px = new Color32[n * n];
        Color body = Planeta, dark = Hex("#2E7FB0"), light = Hex("#A5DBF5"), ring = Hex("#F7C948"), star = Hex("#F7C948");
        Vector2 c = new Vector2(n * 0.52f, n * 0.5f);
        float R = n * 0.26f;
        Vector2[] craters = { new Vector2(-0.35f, 0.25f), new Vector2(0.3f, -0.2f), new Vector2(0.05f, 0.45f), new Vector2(-0.15f, -0.4f) };
        float[] craterR = { 0.18f, 0.22f, 0.11f, 0.13f };
        Vector2[] starsPos = { new Vector2(0.15f, 0.85f), new Vector2(0.85f, 0.88f), new Vector2(0.9f, 0.2f), new Vector2(0.1f, 0.15f) };

        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                Vector2 q = (p - c) / R;
                Color col = Color.clear;

                // Anillo (elipse inclinada) detrás del planeta
                Vector2 rq = Rotate(q, -20f);
                float ringV = (rq.x * rq.x) / (1.75f * 1.75f) + (rq.y * rq.y) / (0.42f * 0.42f);
                bool onRing = ringV < 1f && ringV > 0.72f;
                bool ringFront = onRing && rq.y < 0f;
                if (onRing && !ringFront) col = ring;

                // Cuerpo
                float dd = q.magnitude;
                if (dd <= 1f)
                {
                    col = body;
                    for (int i = 0; i < craters.Length; i++)
                        if ((q - craters[i]).magnitude < craterR[i]) col = dark;
                    if (Vector2.Dot(q, new Vector2(-0.6f, 0.8f)) > 0.55f && dd > 0.6f) col = Color.Lerp(col, light, 0.6f);
                    if (dd > 0.9f) col = Color.Lerp(col, dark, 0.6f);
                }
                if (ringFront) col = ring;

                // Estrellitas
                foreach (var s in starsPos)
                {
                    Vector2 sp = new Vector2(s.x * n, s.y * n);
                    Vector2 dv = p - sp;
                    if (Mathf.Abs(dv.x) + Mathf.Abs(dv.y) * 3f < 7f || Mathf.Abs(dv.y) + Mathf.Abs(dv.x) * 3f < 7f) col = star;
                }
                px[y * n + x] = col;
            }
        return px;
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float a = deg * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
        return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
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

    static Sprite SaveSprite(Color32[] px, int size, string name, Vector4 border)
    {
        string path = $"{SpriteDir}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
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
