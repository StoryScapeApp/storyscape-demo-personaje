using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dibuja la boca en los sprites frontales del personaje, crea los 3 sprites de habla
/// y agrega CharacterTalkAnimator al personaje.
/// Menú: StoryScape > Agregar boca al personaje
/// (Si vuelves a usar "Generar demo personaje caminando", ejecuta este menú otra vez.)
/// </summary>
public static class CharacterMouthTool
{
    const string Dir = "Assets/Demo2D/Sprites";

    static readonly Color32 Lip = new Color32(170, 80, 85, 255);
    static readonly Color32 MouthDark = new Color32(80, 25, 35, 255);
    static readonly Color32 Tongue = new Color32(230, 110, 120, 255);
    static readonly Color32 Skin = new Color32(250, 205, 165, 255);

    // Boca centrada bajo los ojos (ojos en x=6 y x=9, fila 11).
    static void DrawMouth(Color32[] px, int w, int state)
    {
        void Set(int x, int y, Color32 c) => px[y * w + x] = c;

        // Limpia la zona de la boca con piel
        Set(7, 9, Skin); Set(8, 9, Skin); Set(7, 10, Skin); Set(8, 10, Skin);

        switch (state)
        {
            case 0: // cerrada
                Set(7, 10, Lip); Set(8, 10, Lip);
                break;
            case 1: // entreabierta
                Set(7, 10, MouthDark); Set(8, 10, MouthDark);
                break;
            default: // abierta
                Set(7, 10, MouthDark); Set(8, 10, MouthDark);
                Set(7, 9, Tongue); Set(8, 9, Tongue);
                break;
        }
    }

    static Sprite WriteVariant(string sourceName, string targetName, int state)
    {
        string src = $"{Dir}/{sourceName}.png";
        string dst = $"{Dir}/{targetName}.png";

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(src));
        var px = tex.GetPixels32();
        DrawMouth(px, tex.width, state);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(dst, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
        if (dst != src) CopyImportSettings(src, dst);
        return AssetDatabase.LoadAssetAtPath<Sprite>(dst);
    }

    static void CopyImportSettings(string from, string to)
    {
        var a = (TextureImporter)AssetImporter.GetAtPath(from);
        var b = (TextureImporter)AssetImporter.GetAtPath(to);
        var s = new TextureImporterSettings();
        a.ReadTextureSettings(s);
        b.SetTextureSettings(s);
        b.textureType = TextureImporterType.Sprite;
        b.spriteImportMode = SpriteImportMode.Single;
        b.spritePixelsPerUnit = a.spritePixelsPerUnit;
        b.filterMode = a.filterMode;
        b.textureCompression = TextureImporterCompression.Uncompressed;
        b.mipmapEnabled = false;
        b.SaveAndReimport();
    }

    [MenuItem("StoryScape/Agregar boca al personaje")]
    public static void AddMouth()
    {
        // Sprites de habla (a partir del frontal quieto, antes de modificarlo)
        var talk = new Sprite[3];
        talk[0] = WriteVariant("personaje_abajo_0", "personaje_habla_0", 0);
        talk[1] = WriteVariant("personaje_abajo_0", "personaje_habla_1", 1);
        talk[2] = WriteVariant("personaje_abajo_0", "personaje_habla_2", 2);

        // Boca cerrada en los sprites frontales de caminar / quieto
        for (int f = 0; f < 3; f++) WriteVariant($"personaje_abajo_{f}", $"personaje_abajo_{f}", 0);

        // Componente en el personaje
        var walker = Object.FindFirstObjectByType<CharacterPathWalker2D>();
        var agent = Object.FindFirstObjectByType<ElevenLabsAgentClient>();
        if (walker == null)
        {
            Debug.LogWarning("[StoryScape] No se encontró el personaje en la escena.");
            return;
        }
        var anim = walker.GetComponent<CharacterTalkAnimator>();
        if (anim == null) anim = walker.gameObject.AddComponent<CharacterTalkAnimator>();
        anim.agent = agent;
        anim.talkFrames = talk;
        EditorUtility.SetDirty(anim);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[StoryScape] Boca agregada y animación de habla configurada.");
    }
}
