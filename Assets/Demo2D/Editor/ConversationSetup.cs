using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Agrega a la escena actual el banco de términos, el resaltado proyectado, el cliente de ElevenLabs
/// y el flujo de la demo, sin regenerar la escena.
/// Menú: StoryScape > Configurar conversación ElevenLabs
/// </summary>
public static class ConversationSetup
{
    const string DataDir = "Assets/Demo2D/Data";
    const string BankPath = DataDir + "/BancoTerminos.asset";
    const string HighlightPath = "Assets/Demo2D/Sprites/resaltado.png";
    const string StreamingDir = "Assets/StreamingAssets";

    [MenuItem("StoryScape/Configurar conversación ElevenLabs")]
    public static void Setup()
    {
        var bank = CreateBank();
        var highlightSprite = CreateHighlightSprite();
        CreateConfigFiles();
        UpdateGitignore();

        // Objetos de escena
        var walker = Object.FindFirstObjectByType<CharacterPathWalker2D>();
        if (walker == null) Debug.LogWarning("[StoryScape] No se encontró CharacterPathWalker2D en la escena.");

        var hlGo = GameObject.Find("ResaltadoTermino") ?? new GameObject("ResaltadoTermino");
        var hl = hlGo.GetComponent<SpriteRenderer>();
        if (hl == null) hl = hlGo.AddComponent<SpriteRenderer>();
        hl.sprite = highlightSprite;
        hl.sortingOrder = 1;

        var agentGo = GameObject.Find("AgenteElevenLabs") ?? new GameObject("AgenteElevenLabs");
        if (agentGo.GetComponent<AudioSource>() == null) agentGo.AddComponent<AudioSource>();
        var agent = agentGo.GetComponent<ElevenLabsAgentClient>();
        if (agent == null) agent = agentGo.AddComponent<ElevenLabsAgentClient>();

        var flowGo = GameObject.Find("FlujoDemo") ?? new GameObject("FlujoDemo");
        var flow = flowGo.GetComponent<StoryScapeDemoFlow>();
        if (flow == null) flow = flowGo.AddComponent<StoryScapeDemoFlow>();
        flow.bank = bank;
        flow.walker = walker;
        flow.agent = agent;
        flow.highlight = hl;

        var t = bank.terms[0];
        hlGo.transform.position = new Vector3(t.highlightPosition.x, t.highlightPosition.y, 0f);
        hlGo.transform.localScale = new Vector3(t.highlightSize.x, t.highlightSize.y, 1f);
        hl.color = flow.highlightColor;

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = flowGo;
        Debug.Log("[StoryScape] Conversación configurada en la escena " + scene.name);
    }

    static TermBank CreateBank()
    {
        var bank = AssetDatabase.LoadAssetAtPath<TermBank>(BankPath);
        if (bank != null) return bank;

        Directory.CreateDirectory(DataDir);
        bank = ScriptableObject.CreateInstance<TermBank>();
        void Add(string term, string meaning, int page)
        {
            bank.terms.Add(new TermEntry
            {
                term = term,
                meaning = meaning,
                page = page,
                highlightPosition = new Vector2(-2f, 2.2f),
                highlightSize = new Vector2(2.6f, 0.55f)
            });
        }
        Add("domesticar", "Crear lazos con alguien, hacer que se vuelva especial para ti y tú para él.", 0);
        Add("baobab", "Árbol muy grande que, si crece sin control, puede destruir el pequeño planeta del Principito.", 0);
        Add("asteroide", "Cuerpo pequeño de roca que viaja por el espacio; el Principito vive en uno.", 0);
        Add("efímero", "Que dura poco tiempo, que no es para siempre.", 0);
        Add("lo esencial es invisible a los ojos", "Lo más importante, como el cariño o la amistad, no se ve con los ojos: se siente.", 0);

        AssetDatabase.CreateAsset(bank, BankPath);
        AssetDatabase.SaveAssets();
        return bank;
    }

    static Sprite CreateHighlightSprite()
    {
        const int W = 32, H = 32;
        const float radius = 8f, soft = 3f;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // Distancia a un rectángulo redondeado (borde suave)
                float cx = Mathf.Clamp(x + 0.5f, radius, W - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, H - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01((radius - d) / soft);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(HighlightPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(HighlightPath, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(HighlightPath);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 32; // 1 x 1 unidad: se escala con highlightSize
        ti.filterMode = FilterMode.Bilinear;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(HighlightPath);
    }

    static void CreateConfigFiles()
    {
        Directory.CreateDirectory(StreamingDir);
        const string template = "{\n  \"agentId\": \"PEGA_AQUI_TU_AGENT_ID\"\n}\n";
        File.WriteAllText(Path.Combine(StreamingDir, "elevenlabs.example.json"), template);
        string local = Path.Combine(StreamingDir, "elevenlabs.local.json");
        if (!File.Exists(local)) File.WriteAllText(local, template);
        AssetDatabase.Refresh();
    }

    static void UpdateGitignore()
    {
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".gitignore");
        string text = File.Exists(path) ? File.ReadAllText(path) : "";
        if (text.Contains("elevenlabs.local.json")) return;
        text += "\n# Configuración local de ElevenLabs (no subir)\n" +
                "/[Aa]ssets/[Ss]treaming[Aa]ssets/elevenlabs.local.json\n" +
                "/[Aa]ssets/[Ss]treaming[Aa]ssets/elevenlabs.local.json.meta\n";
        File.WriteAllText(path, text);
    }
}
