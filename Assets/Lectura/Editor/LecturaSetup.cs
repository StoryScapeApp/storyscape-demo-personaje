using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
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
        var anim = new ReadingSession.SceneAnimation
        {
            id = "principito_volando",
            pagina = 1,
            frase = FraseVuelo,
            coincidencia = 0.7f,
            soloUnaVez = true,
        };
        UnityEventTools.AddPersistentListener(anim.alActivar, fly.Play);
        session.animaciones.Add(anim);

        UnityEventTools.AddPersistentListener(seq.onFinished, session.Iniciar);

        EditorUtility.SetDirty(seq);
        EditorSceneManager.MarkSceneDirty(seq.gameObject.scene);
        EditorSceneManager.SaveScene(seq.gameObject.scene);
        Selection.activeGameObject = root;
        return true;
    }
}
