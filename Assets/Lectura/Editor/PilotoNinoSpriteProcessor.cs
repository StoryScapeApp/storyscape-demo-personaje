using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Recorta "pilot_becoming_kid.png" (hoja 3x2 con fondo transparente) en 6 PNG: el piloto adulto que se
/// transforma en niño (orden: izquierda a derecha, arriba a abajo).
/// Cada cuadro se alinea por los PIES y por el CENTRO DEL CUERPO, así el personaje se transforma en su sitio
/// (en la hoja cada columna lo tiene un poco corrido). Pivote: en los pies.
/// Menú: StoryScape > Lectura > Procesar sprites piloto → niño
/// </summary>
public static class PilotoNinoSpriteProcessor
{
    const string Source = "Assets/Demo2D/Onboarding/Sprites/raw/pilot_becoming_kid.png";
    public const string OutDir = "Assets/Lectura/Sprites/Piloto";
    public const string Prefix = "piloto_";
    const float PixelsPerUnit = 95f;   // ~2,4 unidades de alto en escena
    const int Cols = 3, Rows = 2;
    const int Margen = 30;             // espacio a los lados para centrar el cuerpo
    const int BajoPies = 20;           // espacio bajo los pies (sombra y brillos)
    const byte Opaco = 200;

    [MenuItem("StoryScape/Lectura/Procesar sprites piloto → niño")]
    public static Sprite[] Process()
    {
        if (!File.Exists(Source)) { Debug.LogWarning("[Piloto] No se encontró " + Source); return null; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(Source));
        int W = tex.width, H = tex.height;
        var px = tex.GetPixels32();   // fila 0 = abajo
        Object.DestroyImmediate(tex);

        int cellH = H / Rows;
        var x0s = new int[Cols + 1];
        for (int c = 0; c <= Cols; c++) x0s[c] = Mathf.RoundToInt(c * W / (float)Cols);
        int cellW = 0;
        for (int c = 0; c < Cols; c++) cellW = Mathf.Max(cellW, x0s[c + 1] - x0s[c]);

        // Pies (fila opaca más baja) y centro del cuerpo (media de x opacos en la mitad inferior) de cada cuadro
        var pies = new int[Cols * Rows]; var centro = new float[Cols * Rows]; var alto = new int[Cols * Rows];
        int maxSobrePies = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int k = r * Cols + c;
                int xa = x0s[c], xb = x0s[c + 1], ya = (Rows - 1 - r) * cellH, yb = ya + cellH;   // fila 0 de la hoja = arriba
                int minY = int.MaxValue, maxY = -1;
                for (int y = ya; y < yb; y++) for (int x = xa; x < xb; x++)
                    if (px[y * W + x].a >= Opaco) { minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
                int mitad = (minY + maxY) / 2;
                double sum = 0; int n = 0;
                for (int y = minY; y <= mitad; y++) for (int x = xa; x < xb; x++)
                    if (px[y * W + x].a >= Opaco) { sum += x - xa; n++; }
                pies[k] = minY - ya;
                centro[k] = n > 0 ? (float)(sum / n) : (xb - xa) / 2f;
                alto[k] = cellH - pies[k];
                maxSobrePies = Mathf.Max(maxSobrePies, alto[k]);
            }

        int cw = cellW + Margen * 2, ch = BajoPies + maxSobrePies + 4;
        Directory.CreateDirectory(OutDir);
        var paths = new List<string>();
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int k = r * Cols + c;
                int xa = x0s[c], xb = x0s[c + 1], ya = (Rows - 1 - r) * cellH;
                var outPx = new Color32[cw * ch];
                int dx = Mathf.RoundToInt(cw / 2f - centro[k]);
                for (int y = ya; y < ya + cellH; y++)
                    for (int x = xa; x < xb; x++)
                    {
                        var col = px[y * W + x];
                        if (col.a == 0) continue;
                        int ox = x - xa + dx, oy = y - ya - pies[k] + BajoPies;
                        if (ox < 0 || ox >= cw || oy < 0 || oy >= ch) continue;
                        outPx[oy * cw + ox] = col;
                    }
                var o = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                o.SetPixels32(outPx); o.Apply();
                string path = $"{OutDir}/{Prefix}{k + 1:00}.png";
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
            s.spritePivot = new Vector2(0.5f, (float)BajoPies / ch);   // en los pies
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            sprites[k] = AssetDatabase.LoadAssetAtPath<Sprite>(paths[k]);
        }
        Debug.Log($"[Piloto] {sprites.Length} cuadros en {OutDir} (lienzo {cw}x{ch}, pies {string.Join(",", pies)}, centros {string.Join(",", System.Array.ConvertAll(centro, v => v.ToString("0")))}).");
        return sprites;
    }
}
