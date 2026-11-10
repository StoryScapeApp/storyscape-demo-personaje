using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// StoryScape > Dudas > Generar escena de prueba (dedicatoria)
/// Crea Assets/Scenes/PruebaDudas.unity a partir de la escena del onboarding (mismo libro, Principito,
/// micrófono y agente), quita los elementos del onboarding y agrega el modo de dudas con resaltado.
/// </summary>
public static class DudasSceneBuilder
{
    const string Source = "Assets/Scenes/Onboarding.unity";
    const string ScenePath = "Assets/Scenes/PruebaDudas.unity";

    // Tarjeta del significado, a la derecha del libro (el libro va de x = -2.76 a 2.76)
    static readonly Vector3 DefinitionPos = new Vector3(4.65f, -0.1f, 0f);
    static readonly Vector2 DefinitionSize = new Vector2(2.9f, 2.1f);

    [MenuItem("StoryScape/Dudas/Generar escena de prueba (dedicatoria)")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Source) == null)
        {
            Debug.LogWarning("[Dudas] Primero genera la escena del onboarding (StoryScape > Onboarding > Generar escena).");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
        AssetDatabase.CopyAsset(Source, ScenePath);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Quita lo que solo usa el onboarding
        foreach (var name in new[] { "Tarjetas", "Meteoro", "Planeta", "Quiz", "Estrellas", "Onboarding", "VozPrincipito", "MarcoMarcador", "Encabezado", "Dudas" })
        {
            var go = FindIncludingInactive(name);
            if (go) Object.DestroyImmediate(go);
        }

        var bookRef = FindIncludingInactive("LibroReferencia");
        var principito = Object.FindFirstObjectByType<SpriteAnimator2D>(FindObjectsInactive.Include);
        var agent = Object.FindFirstObjectByType<ElevenLabsAgentClient>(FindObjectsInactive.Include);
        var tracker = Object.FindFirstObjectByType<BookPageTracker>(FindObjectsInactive.Include);
        var receiver = Object.FindFirstObjectByType<ArucoUdpReceiver>(FindObjectsInactive.Include);
        var wordGo = FindIncludingInactive("ResaltadoPalabra");
        var defGo = FindIncludingInactive("Significado");

        if (!bookRef || !principito || !agent || !tracker || !wordGo || !defGo)
        {
            Debug.LogError("[Dudas] Falta algún objeto de la escena del onboarding. Vuelve a generarla e inténtalo de nuevo.");
            return;
        }

        // El marcador 0 ya no inicia el onboarding: aquí solo indica la página (BookPageTracker)
        if (receiver) receiver.actions.Clear();
        tracker.startPage = 1;
        agent.showSubtitles = false;

        // Tarjeta del significado más grande, para definiciones de cualquier largo
        var definition = defGo.GetComponent<ProjectedCard>();
        defGo.transform.position = DefinitionPos;
        if (definition.fill) definition.fill.size = DefinitionSize;
        if (definition.border) definition.border.size = DefinitionSize;
        if (definition.label)
        {
            definition.label.rectTransform.sizeDelta = DefinitionSize - new Vector2(0.35f, 0.2f);
            definition.label.text = "<b><size=120%>Palabra:</size></b>\nSignificado";
        }

        var dudasGo = new GameObject("Dudas");
        var highlighter = dudasGo.AddComponent<WordHighlighter>();
        highlighter.book = bookRef.transform;
        highlighter.template = wordGo.GetComponent<ProjectedCard>();

        // Página simulada para probar sin proyector (desmarcar "Show" al proyectar sobre el libro real)
        var sim = dudasGo.AddComponent<SimulatedBookPage>();
        sim.highlighter = highlighter;
        sim.tracker = tracker;

        var session = dudasGo.AddComponent<DoubtSession>();
        session.agent = agent;
        session.tracker = tracker;
        session.highlighter = highlighter;
        session.principito = principito;
        session.definition = definition;
        session.receiver = receiver;

        // "Principito, tengo una duda" → abre las dudas
        var phrase = dudasGo.AddComponent<TriggerPhraseListener>();
        phrase.agent = agent;
        UnityEditor.Events.UnityEventTools.AddPersistentListener(phrase.onPhrase, session.AbrirDudas);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = dudasGo;
        Debug.Log("[Dudas] Escena de prueba generada en " + ScenePath + ". Dale Play y di \"Principito, tengo una duda\" (o presiona D).");
    }

    static GameObject FindIncludingInactive(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name) return t.gameObject;
        return null;
    }
}
