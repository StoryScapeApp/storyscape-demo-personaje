using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Datos del niño durante la sesión de lectura actual (por ahora, su nombre).
/// - El onboarding pregunta el nombre y lo guarda aquí; el quiz lo usa al empezar ("¡Muy bien, Camila!…").
/// - Se guarda solo en memoria: no se escribe en ningún archivo ni en los resultados del quiz,
///   y se borra al empezar un onboarding nuevo (el siguiente niño).
/// </summary>
public static class SesionLector
{
    public static string Nombre { get; private set; } = "";
    public static bool TieneNombre => !string.IsNullOrEmpty(Nombre);
    public static event Action<string> NombreCambiado;

    public static void SetNombre(string nombre)
    {
        Nombre = (nombre ?? "").Trim();
        Debug.Log(TieneNombre ? $"[Sesión] Nombre del niño: {Nombre}" : "[Sesión] Sin nombre.");
        NombreCambiado?.Invoke(Nombre);
    }

    /// Nuevo niño: olvida el nombre anterior.
    public static void Reiniciar()
    {
        if (TieneNombre) SetNombre("");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void AlIniciar() { Nombre = ""; NombreCambiado = null; }   // también sin recarga de dominio

    // ───────────── Sacar el nombre de lo que dijo el niño ─────────────
    static readonly string[] Frases = { "mi nombre es", "me llamo", "me dicen", "yo soy", "soy", "llamo" };
    static readonly HashSet<string> Relleno = new HashSet<string>
    {
        "hola", "eh", "em", "emm", "mmm", "este", "pues", "bueno", "ya", "y", "yo", "principito", "si", "sí", "ok", "oye",
        "el", "la", "los", "las", "un", "una", "de", "del", "que", "qué", "no", "se", "sé", "nada", "mi", "me", "es", "te",
        "llamas", "llamo", "nombre", "gracias", "aja", "ajá", "a", "o", "pero", "tengo", "años", "estoy", "listo", "lista",
    };

    /// "Me llamo Ana Lucía" → "Ana Lucía"; "soy piero" → "Piero"; "Camila." → "Camila". Vacío si no se reconoce.
    public static string ExtraerNombre(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var palabras = Palabras(texto);
        if (palabras.Count == 0) return "";
        var bajas = palabras.ConvertAll(p => p.ToLowerInvariant());

        // 1) Después de una frase como "me llamo" / "mi nombre es" / "soy" (la última que aparezca)
        int desde = -1;
        foreach (var f in Frases)
        {
            var fp = f.Split(' ');
            for (int i = 0; i + fp.Length <= bajas.Count; i++)
            {
                bool ok = true;
                for (int k = 0; k < fp.Length && ok; k++) ok = bajas[i + k] == fp[k];
                if (ok && i + fp.Length > desde) desde = i + fp.Length;
            }
            if (desde >= 0) break;   // las frases están ordenadas de más a menos específica
        }

        var candidatos = new List<string>();
        if (desde >= 0)
        {
            for (int i = desde; i < palabras.Count && candidatos.Count < 2; i++)
            {
                if (Relleno.Contains(bajas[i])) { if (candidatos.Count > 0) break; continue; }
                candidatos.Add(palabras[i]);
            }
        }
        else
        {
            // 2) Respuesta corta sin frase: "Camila", "Ana Lucía", "eh… Piero"
            var utiles = new List<string>();
            for (int i = 0; i < palabras.Count; i++) if (!Relleno.Contains(bajas[i])) utiles.Add(palabras[i]);
            if (utiles.Count == 0 || utiles.Count > 3) return "";
            candidatos.Add(utiles[0]);
            if (utiles.Count == 2) candidatos.Add(utiles[1]);
        }
        if (candidatos.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var c in candidatos)
        {
            if (c.Length < 2 || !char.IsLetter(c[0])) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(char.ToUpper(c[0], new CultureInfo("es-PE"))).Append(c.Substring(1).ToLower(new CultureInfo("es-PE")));
        }
        var nombre = sb.ToString();
        return nombre.Length > 30 ? nombre.Substring(0, 30).Trim() : nombre;
    }

    static List<string> Palabras(string texto)
    {
        var lista = new List<string>();
        var sb = new StringBuilder();
        foreach (char ch in texto)
        {
            if (char.IsLetter(ch) || ch == '\'' || ch == '-') sb.Append(ch);
            else if (sb.Length > 0) { lista.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) lista.Add(sb.ToString());
        return lista;
    }
}
