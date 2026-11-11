using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine;

/// <summary>
/// StoryScape > Lectura > Configurar lectura en vivo
/// - Importa los sprites de las animaciones (pivote en la cabeza del Principito, mismo tamaño entre poses).
/// - Agrega a la escena Onboarding el objeto "Lectura": agente que escucha la lectura, animación del
///   Principito volando y la lista de frases resaltadas. Se inicia al terminar el onboarding.
/// </summary>
public static class LecturaSetup
{
    const string SpriteDir = "Assets/Lectura/Sprites/Vuelo";
    // 1 unidad de escena = 165 px de estos sprites (todas las poses a la misma escala)
    const float PixelsPerUnit = 165f;
    static readonly Dictionary<string, Vector2> Pivots = new Dictionary<string, Vector2>
    {
        { "vuelo_01", new Vector2(0.7274f, 0.4572f) },
        { "vuelo_02", new Vector2(0.6606f, 0.4618f) },
        { "vuelo_03", new Vector2(0.4133f, 0.4252f) },
        { "asteroide", new Vector2(0.5f, 0.5f) },
    };

    public const string FraseVuelo = "animaciones inspiradas en escenas del libro El Principito";
    // Final de la página 1: el quiz empieza recién con la línea de créditos
    public const string FraseFinPagina1 = "carrera de Ingeniería de Software de la Universidad Peruana de Ciencias Aplicadas";

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    [MenuItem("StoryScape/Lectura/Configurar lectura en vivo")]
    public static void Menu()
    {
        ConfigureSprites();
        if (!AddToOpenScene()) return;
        Debug.Log("[Lectura] Listo: al terminar el onboarding, el agente escucha la lectura y activa las animaciones.");
    }

    public static void ConfigureSprites()
    {
        foreach (var kv in Pivots)
        {
            string path = $"{SpriteDir}/{kv.Key}.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[Lectura] No se encontró " + path); continue; }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            var st = new TextureImporterSettings();
            ti.ReadTextureSettings(st);
            st.spriteAlignment = (int)SpriteAlignment.Custom;
            st.spritePivot = kv.Value;
            st.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(st);
            ti.SaveAndReimport();
        }
    }

    static QuizSession CreateQuiz(Transform root, ElevenLabsAgentClient doubtAgent, DoubtSession doubts, PrincipitoVolando fly)
    {
        var quizRoot = new GameObject("Quiz").transform;
        quizRoot.SetParent(root, false);

        var agentGo = new GameObject("AgenteQuiz");
        agentGo.transform.SetParent(quizRoot, false);
        agentGo.AddComponent<AudioSource>();
        var agent = agentGo.AddComponent<ElevenLabsAgentClient>();
        agent.configKey = "agentIdQuiz";
        agent.simulate = false;
        agent.microphoneDevice = doubtAgent.microphoneDevice;
        agent.micGain = doubtAgent.micGain;
        agent.muteOutput = false;
        agent.muteMicWhileAgentSpeaks = true;
        agent.showMicLevel = false;
        agent.showSubtitles = false;

        var fill = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Onboarding/Sprites/card_fondo.png");
        var border = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Onboarding/Sprites/card_borde.png");
        var star = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Onboarding/Sprites/estrella.png");

        // Panel a la derecha del libro (el libro termina en x = 2.76)
        const float X = 4.65f;
        var panel = Card("PanelQuiz", quizRoot, new Vector3(X, -0.1f, 0f), new Vector2(2.9f, 3.6f), Hex("#D9D2CF"), "", 1f, fill, border, TextAlignmentOptions.Center);
        panel.fillAlpha = 0.12f;
        var titulo = Card("Titulo", quizRoot, new Vector3(X, 1.36f, 0f), new Vector2(2.6f, 0.6f), Color.white, "<b>Mini quiz</b>", 2.4f, null, null, TextAlignmentOptions.Center);
        var pregunta = Card("Pregunta", quizRoot, new Vector3(X, 0.7f, 0f), new Vector2(2.6f, 0.75f), Color.white, "", 2.1f, null, null, TextAlignmentOptions.Center);
        string[] letras = { "A", "B", "C" };
        Color[] colores = { Hex("#4FB3E8"), Hex("#F2727F"), Hex("#F7C948") };
        var opciones = new ProjectedCard[3];
        for (int i = 0; i < 3; i++)
            opciones[i] = Card("Opcion_" + letras[i], quizRoot, new Vector3(X, -0.02f - i * 0.68f, 0f), new Vector2(2.6f, 0.55f), colores[i],
                "<b>" + letras[i] + "</b>", 2.0f, fill, border, TextAlignmentOptions.MidlineLeft);

        var starsGo = new GameObject("Estrellas");
        starsGo.transform.SetParent(quizRoot, false);
        var stars = starsGo.AddComponent<StarBurst>();
        stars.star = star;
        stars.starSize = 0.3f;
        stars.radius = 1.0f;

        var quiz = root.gameObject.AddComponent<QuizSession>();
        quiz.agent = agent;
        quiz.doubtAgent = doubtAgent;
        quiz.doubts = doubts;
        quiz.principito = Object.FindFirstObjectByType<SpriteAnimator2D>(FindObjectsInactive.Include);
        quiz.panel = panel;
        quiz.titulo = titulo;
        quiz.pregunta = pregunta;
        quiz.opciones = opciones;
        quiz.estrellas = stars;
        quiz.esperarAnimaciones = new[] { fly };
        quiz.quizzes.Add(new QuizSession.Quiz
        {
            id = "pagina_1",
            intro = "¡Muy bien! Terminaste la primera página. Ahora, un mini quiz.",
            preguntas = new List<QuizSession.Pregunta>
            {
                new QuizSession.Pregunta { texto = "¿Cómo vas a leer el cuento?", opciones = new[] { "En silencio", "En voz alta", "Cantando" }, correcta = 1 },
                new QuizSession.Pregunta { texto = "¿Para qué sirve leer este libro de forma interactiva?",
                    opciones = new[] { "Para entender mejor la historia", "Para leer más rápido", "Para no tener que leer" }, correcta = 0 },
            },
        });
        return quiz;
    }

    static ProjectedCard Card(string name, Transform parent, Vector3 pos, Vector2 size, Color color, string text, float fontSize,
                              Sprite fillSprite, Sprite borderSprite, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        SpriteRenderer fill = null, border = null;
        if (fillSprite)
        {
            fill = new GameObject("Fondo").AddComponent<SpriteRenderer>();
            fill.transform.SetParent(go.transform, false);
            fill.sprite = fillSprite;
            fill.drawMode = SpriteDrawMode.Sliced;
            fill.size = size;
            fill.sortingOrder = 5;
        }
        if (borderSprite)
        {
            border = new GameObject("Borde").AddComponent<SpriteRenderer>();
            border.transform.SetParent(go.transform, false);
            border.sprite = borderSprite;
            border.drawMode = SpriteDrawMode.Sliced;
            border.size = size;
            border.sortingOrder = 6;
        }
        var tgo = new GameObject("Texto");
        tgo.transform.SetParent(go.transform, false);
        var tmp = tgo.AddComponent<TextMeshPro>();
        tmp.rectTransform.sizeDelta = size - new Vector2(0.3f, 0.1f);
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.color = Color.white;
        tmp.sortingOrder = 7;
        var card = go.AddComponent<ProjectedCard>();
        card.fill = fill;
        card.border = border;
        card.label = tmp;
        card.color = color;
        return card;
    }

    static Sprite Load(string n) => AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteDir}/{n}.png");

    /// Agrega (o vuelve a crear) el objeto "Lectura" en la escena abierta del onboarding.
    public static bool AddToOpenScene()
    {
        var seq = Object.FindFirstObjectByType<OnboardingSequence>(FindObjectsInactive.Include);
        var tracker = Object.FindFirstObjectByType<BookPageTracker>(FindObjectsInactive.Include);
        var doubts = Object.FindFirstObjectByType<DoubtSession>(FindObjectsInactive.Include);
        ElevenLabsAgentClient doubtAgent = doubts ? doubts.agent : null;
        if (!doubtAgent)
            foreach (var a in Object.FindObjectsByType<ElevenLabsAgentClient>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (a.configKey == "agentId") doubtAgent = a;
        if (!seq || !tracker || !doubtAgent)
        {
            Debug.LogWarning("[Lectura] Abre la escena Onboarding (StoryScape > Onboarding > Generar escena) y vuelve a intentarlo.");
            return false;
        }

        // Quita la versión anterior y las llamadas que quedaron sin destino
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t && t.name == "Lectura" && t.parent == null) Object.DestroyImmediate(t.gameObject);
        for (int i = seq.onFinished.GetPersistentEventCount() - 1; i >= 0; i--)
            if (seq.onFinished.GetPersistentTarget(i) == null) UnityEventTools.RemovePersistentListener(seq.onFinished, i);

        var root = new GameObject("Lectura");

        // Agente que escucha (voz silenciada)
        var agentGo = new GameObject("AgenteLectura");
        agentGo.transform.SetParent(root.transform, false);
        agentGo.AddComponent<AudioSource>();
        var agent = agentGo.AddComponent<ElevenLabsAgentClient>();
        agent.configKey = "agentIdLectura";
        agent.simulate = false;
        agent.microphoneDevice = doubtAgent.microphoneDevice;
        agent.micGain = doubtAgent.micGain;
        agent.muteOutput = true;
        agent.muteMicWhileAgentSpeaks = false;
        agent.showMicLevel = false;
        agent.showSubtitles = false;

        // Animación: Principito volando con las aves
        var flyGo = new GameObject("Anim_PrincipitoVolando");
        flyGo.transform.SetParent(root.transform, false);
        var fly = flyGo.AddComponent<PrincipitoVolando>();
        fly.pose01 = Load("vuelo_01");
        fly.pose02 = Load("vuelo_02");
        fly.pose03 = Load("vuelo_03");
        fly.asteroide = Load("asteroide");

        // Sesión de lectura
        var session = root.AddComponent<ReadingSession>();
        session.agent = agent;
        session.doubtAgent = doubtAgent;
        session.doubts = doubts;
        session.tracker = tracker;
        session.receptorVoz = Object.FindFirstObjectByType<ArucoUdpReceiver>(FindObjectsInactive.Include);
        session.dictadoWindows = false;   // el dictado de Unity no recibe audio en Windows 11: se usa Vosk (Python)
        // Transcripción en tiempo real con ElevenLabs Scribe v2 Realtime (texto parcial mientras el niño lee)
        var scribeGo = new GameObject("Scribe");
        scribeGo.transform.SetParent(root.transform, false);
        var scribe = scribeGo.AddComponent<ScribeRealtimeClient>();
        scribe.microphoneDevice = doubtAgent.microphoneDevice;
        scribe.micGain = doubtAgent.micGain;
        session.scribe = scribe;
        session.usarScribe = true;
        session.conectarAgente = false;   // Scribe ya detecta las frases; el agente lector quedaría compitiendo por el micrófono
        var anim = new ReadingSession.SceneAnimation
        {
            id = "principito_volando",
            pagina = 1,
            frase = FraseVuelo + "|animaciones inspiradas en escenas|escenas del libro El Principito",
            coincidencia = 0.7f,
            soloUnaVez = true,
            frasesRapidas = new[] { "animaciones inspiradas", "inspiradas en escenas", "escenas del libro El Principito",
                                    "proyecciones y animaciones", "animaciones inspiradas en escenas", "en escenas del libro" },
        };
        UnityEventTools.AddPersistentListener(anim.alActivar, fly.Play);
        session.animaciones.Add(anim);

        // Mini quiz al terminar la página 1
        var quiz = CreateQuiz(root.transform, doubtAgent, doubts, fly);
        session.quiz = quiz;
        quiz.readingAgent = agent;
        var finPagina = new ReadingSession.SceneAnimation
        {
            id = "quiz_pagina_1",
            pagina = 1,
            frase = FraseFinPagina1 + "|Universidad Peruana de Ciencias Aplicadas",
            coincidencia = 0.7f,
            soloUnaVez = true,
            frasesRapidas = new[] { "Universidad Peruana de Ciencias Aplicadas", "Peruana de Ciencias Aplicadas", "Ingeniería de Software de la Universidad" },
        };
        UnityEventTools.AddStringPersistentListener(finPagina.alActivar, quiz.IniciarQuiz, "pagina_1");
        session.animaciones.Add(finPagina);

        UnityEventTools.AddPersistentListener(seq.onFinished, session.Iniciar);

        EditorUtility.SetDirty(seq);
        EditorSceneManager.MarkSceneDirty(seq.gameObject.scene);
        EditorSceneManager.SaveScene(seq.gameObject.scene);
        Selection.activeGameObject = root;
        return true;
    }
}
