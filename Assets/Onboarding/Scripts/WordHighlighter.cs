using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Resalta sobre el libro físico una palabra o frase de la página actual.
/// Las posiciones salen del PDF del libro (StreamingAssets/palabras_paginas.json, coordenadas 0–1 de la página)
/// y se colocan sobre "LibroReferencia" (mismo tamaño que el libro proyectado).
/// - Ignora mayúsculas, tildes y signos ("¿excusa?" = "EXCUSA,").
/// - Une palabras cortadas con guion al final de línea ("perso-" + "na" = "persona").
/// - Si la frase ocupa dos líneas, pone un resaltado por línea.
/// - Si no hay coincidencia exacta, acepta variaciones cercanas (excusa / excusas).
/// </summary>
public class WordHighlighter : MonoBehaviour
{
    [Serializable] class Palabra { public string t; public float x0, y0, x1, y1; }
    [Serializable] class PaginaPalabras { public int pagina; public int marcador; public Palabra[] palabras; }
    [Serializable] class Archivo { public PaginaPalabras[] paginas; }

    [Tooltip("Archivo en StreamingAssets con la posición de cada palabra")]
    public string wordsFile = "palabras_paginas.json";
    [Tooltip("Objeto que marca el libro físico (centro del libro)")]
    public Transform book;
    [Tooltip("Tamaño del libro en unidades de escena (31 × 21 cm)")]
    public Vector2 bookSize = new Vector2(5.511f, 3.733f);
    [Tooltip("Resaltado que se copia para cada palabra (ResaltadoPalabra)")]
    public ProjectedCard template;
    [Tooltip("Margen extra alrededor de la palabra (unidades de escena)")]
    public Vector2 padding = new Vector2(0.08f, 0.05f);
    [Tooltip("Máximo de apariciones que se resaltan si la palabra se repite en la página")]
    public int maxMatches = 3;

    class Token { public string norm; public List<int> words = new List<int>(); }

    readonly Dictionary<int, Palabra[]> pages = new Dictionary<int, Palabra[]>();
    readonly Dictionary<int, List<Token>> tokens = new Dictionary<int, List<Token>>();
    readonly List<ProjectedCard> active = new List<ProjectedCard>();

    void Awake()
    {
        string path = Path.Combine(Application.streamingAssetsPath, wordsFile);
        try
        {
            var a = JsonUtility.FromJson<Archivo>(File.ReadAllText(path, Encoding.UTF8));
            foreach (var p in a.paginas)
            {
                pages[p.pagina] = p.palabras;
                tokens[p.pagina] = BuildTokens(p.palabras);
            }
            Debug.Log($"[Resaltado] Posiciones de palabras cargadas para {a.paginas.Length} página(s).");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Resaltado] No se pudo leer {path}: {e.Message}");
        }
        if (template) template.Hide(true);
    }

    public bool HasPage(int page) => pages.ContainsKey(page);

    /// Palabras de la página con su rectángulo (0–1 de la página del PDF). Lo usa la página simulada.
    public IEnumerable<KeyValuePair<string, Rect>> GetWords(int page)
    {
        if (!pages.TryGetValue(page, out var words)) yield break;
        foreach (var p in words) yield return new KeyValuePair<string, Rect>(p.t, Rect.MinMaxRect(p.x0, p.y0, p.x1, p.y1));
    }

    /// Resalta la palabra o frase en la página indicada. Devuelve cuántas apariciones se resaltaron.
    public int Highlight(int page, string phrase)
    {
        Clear();
        if (!tokens.TryGetValue(page, out var toks))
        {
            Debug.LogWarning($"[Resaltado] No hay posiciones de palabras para la página {page}.");
            return 0;
        }
        var query = Split(phrase);
        if (query.Count == 0) return 0;

        var matches = Find(toks, query, false);
        if (matches.Count == 0) matches = Find(toks, query, true);
        if (matches.Count == 0)
        {
            Debug.Log($"[Resaltado] \"{phrase}\" no aparece en la página {page}.");
            return 0;
        }

        var words = pages[page];
        int shown = 0;
        foreach (int start in matches)
        {
            if (shown >= maxMatches) break;
            var idx = new List<int>();
            for (int k = 0; k < query.Count; k++) idx.AddRange(toks[start + k].words);
            foreach (var rect in LineRects(words, idx)) Spawn(rect);
            shown++;
        }
        Debug.Log($"[Resaltado] \"{phrase}\" resaltada {shown} vez/veces en la página {page}.");
        return shown;
    }

    public void Clear()
    {
        foreach (var c in active)
        {
            if (c == null) continue;
            c.Hide();
            Destroy(c.gameObject, 1.5f);
        }
        active.Clear();
    }

    // ───────────── Coincidencias ─────────────
    static List<int> Find(List<Token> toks, List<string> q, bool loose)
    {
        var result = new List<int>();
        for (int i = 0; i + q.Count <= toks.Count; i++)
        {
            bool ok = true;
            for (int k = 0; k < q.Count && ok; k++)
                ok = loose ? Similar(toks[i + k].norm, q[k]) : toks[i + k].norm == q[k];
            if (ok) result.Add(i);
        }
        return result;
    }

    /// Variaciones cercanas: plural, femenino, etc. (excusa/excusas, mayor/mayores).
    static bool Similar(string a, string b)
    {
        if (a == b) return true;
        if (a.Length < 4 || b.Length < 4) return false;
        string s = a.Length <= b.Length ? a : b, l = a.Length <= b.Length ? b : a;
        return l.StartsWith(s) && l.Length - s.Length <= 3;
    }

    static List<Token> BuildTokens(Palabra[] words)
    {
        var list = new List<Token>();
        for (int i = 0; i < words.Length; i++)
        {
            string raw = words[i].t.Trim();
            var group = new List<int> { i };
            // Palabra cortada con guion al final de la línea: se une con la siguiente
            if (raw.EndsWith("-") && i + 1 < words.Length && words[i + 1].y0 > words[i].y0 + 0.005f)
            {
                raw = raw.TrimEnd('-') + words[i + 1].t.Trim();
                group.Add(++i);
            }
            foreach (var part in Split(raw))   // "SAINT-EXUPÉRY" → saint, exupery
            {
                var t = new Token { norm = part };
                t.words.AddRange(group);
                list.Add(t);
            }
        }
        return list;
    }

    static List<string> Split(string text)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(text)) return parts;
        var sb = new StringBuilder();
        foreach (char ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }

    // ───────────── Rectángulos ─────────────
    /// Une las palabras seguidas de una misma línea en un solo rectángulo (coordenadas 0–1).
    static List<Rect> LineRects(Palabra[] words, List<int> idx)
    {
        var rects = new List<Rect>();
        idx.Sort();
        Rect cur = default;
        bool has = false;
        int prev = -10;
        foreach (int i in idx)
        {
            if (i == prev) continue;
            var w = words[i];
            var r = Rect.MinMaxRect(w.x0, w.y0, w.x1, w.y1);
            bool sameLine = has && i == prev + 1 && Mathf.Abs(w.y0 - cur.yMin) < 0.01f;
            if (sameLine)
                cur = Rect.MinMaxRect(Mathf.Min(cur.xMin, r.xMin), Mathf.Min(cur.yMin, r.yMin),
                                      Mathf.Max(cur.xMax, r.xMax), Mathf.Max(cur.yMax, r.yMax));
            else
            {
                if (has) rects.Add(cur);
                cur = r;
                has = true;
            }
            prev = i;
        }
        if (has) rects.Add(cur);
        return rects;
    }

    void Spawn(Rect r)
    {
        if (template == null || book == null) { Debug.LogWarning("[Resaltado] Falta asignar el libro o el resaltado base."); return; }
        var card = Instantiate(template, book);
        card.gameObject.name = "Resaltado_activo";
        card.gameObject.SetActive(true);
        // PDF: origen arriba a la izquierda (y hacia abajo). Escena: origen en el centro del libro (y hacia arriba).
        float cx = (r.center.x - 0.5f) * bookSize.x;
        float cy = (0.5f - r.center.y) * bookSize.y;
        var size = new Vector2(r.width * bookSize.x, r.height * bookSize.y) + padding;
        card.transform.localPosition = new Vector3(cx, cy, 0f);
        card.transform.localRotation = Quaternion.identity;
        if (card.fill) card.fill.size = size;
        if (card.border) card.border.size = size;
        card.Hide(true);
        card.Show();
        card.SetGlow(true);
        active.Add(card);
    }
}
