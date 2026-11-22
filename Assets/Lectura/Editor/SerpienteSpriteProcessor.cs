using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Recorta las hojas de la serpiente boa (2 columnas x 3 filas, fondo transparente) en PNG sueltos:
///  - snake_1.png → reptar_01..06  (ciclo de reptar)
///  - snake_2.png → digerir_01..06 (la boa quieta → se ve el elefante → el elefante sale)
/// Todos se alinean por la CABEZA (borde derecho del cuerpo naranja) y la PANZA (línea de abajo), así la
/// serpiente no tiembla al cambiar de cuadro ni al pasar de una hoja a la otra. Pivote: centro del cuerpo, en la panza.
/// Menú: StoryScape > Lectura > Procesar sprites serpiente boa
/// </summary>
public static class SerpienteSpriteProcessor
{
    const string Raw = "Assets/Demo2D/Onboarding/Sprites/raw/";
    public const string OutDir = "Assets/Lectura/Sprites/Serpiente";
    const float PixelsPerUnit = 125f;   // la boa mide ~2,2 unidades de largo en escena
    const int Cols = 2, Rows = 3;
    const int CabezaX = 330;            // posición de la cabeza en el lienzo
    const int Panza = 40;               // altura de la panza en el lienzo
    const int Ancho = 380, Alto = 190;
    const int MitadCuerpo = 135;        // del borde de la cabeza al centro del cuerpo

    static bool Naranja(Color32 c) =>
        c.a > 200 && c.r > 170 && c.g > 80 && c.g < 215 && c.b < 110 && c.r - c.b > 110;

    [MenuItem("StoryScape/Lectura/Procesar sprites serpiente boa")]
    public static void ProcessAll()
    {
        Process("snake_1.png", "reptar_");
        Process("snake_2.png", "digerir_");
    }

    public static Sprite[] Process(string archivo, string prefijo)
    {
        string src = Raw + archivo;
        if (!File.Exists(src)) { Debug.LogWarning("[Serpiente] No se encontró " + src); return null; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(src));
        int W = tex.width, H = tex.height;
        var px = tex.GetPixels32();   // fila 0 = abajo
        Object.DestroyImmediate(tex);

        Directory.CreateDirectory(OutDir);
        var paths = new List<string>();
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int k = r * Cols + c;
                int xa = Mathf.RoundToInt(c * W / (float)Cols), xb = Mathf.RoundToInt((c + 1) * W / (float)Cols);
                int ya = Mathf.RoundToInt((Rows - 1 - r) * H / (float)Rows), yb = Mathf.RoundToInt((Rows - r) * H / (float)Rows);
                int cabeza = -1, panza = int.MaxValue;
                for (int y = ya; y < yb; y++) for (int x = xa; x < xb; x++)
                    if (Naranja(px[y * W + x])) { cabeza = Mathf.Max(cabeza, x); panza = Mathf.Min(panza, y); }
                if (cabeza < 0) { Debug.LogWarning($"[Serpiente] {archivo} cuadro {k + 1}: no se encontró el cuerpo."); continue; }
                int dx = CabezaX - cabeza, dy = Panza - panza;
                var outPx = new Color32[Ancho * Alto];
                for (int y = ya; y < yb; y++)
                    for (int x = xa; x < xb; x++)
                    {
                        var col = px[y * W + x];
                        if (col.a == 0) continue;
                        int ox = x + dx, oy = y + dy;
                        if (ox < 0 || ox >= Ancho || oy < 0 || oy >= Alto) continue;
                        outPx[oy * Ancho + ox] = col;
                    }
                var o = new Texture2D(Ancho, Alto, TextureFormat.RGBA32, false);
                o.SetPixels32(outPx); o.Apply();
                string path = $"{OutDir}/{prefijo}{k + 1:00}.png";
                File.WriteAllBytes(path, o.EncodeToPNG());
                Object.DestroyImmediate(o);
                paths.Add(path);
            }

        AssetDatabase.Refresh();
        var sprites = new Sprite[paths.Count];
        for (int k = 0; k < paths.Count; k++)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(paths[k]);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.filterMode = FilterMode.Bilinear;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = new Vector2((CabezaX - MitadCuerpo) / (float)Ancho, Panza / (float)Alto);
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            sprites[k] = AssetDatabase.LoadAssetAtPath<Sprite>(paths[k]);
        }
        Debug.Log($"[Serpiente] {archivo}: {sprites.Length} cuadros en {OutDir}.");
        return sprites;
    }
}
