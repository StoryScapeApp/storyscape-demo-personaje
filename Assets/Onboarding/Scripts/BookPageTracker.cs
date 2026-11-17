using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Sabe en qué página del libro está el niño (según el último ArUco detectado) y arma
/// las variables que se envían al agente de ElevenLabs al abrir una conversación de dudas:
///   {{pagina_actual}} → número de página (1, 2, 3…)
///   {{texto_leido}}   → texto de las páginas 1 hasta la actual (nunca las siguientes)
///
/// Correspondencia: marker_0 = página 1, marker_1 = página 2, … (ver StreamingAssets/libro_paginas.json).
/// </summary>
public class BookPageTracker : MonoBehaviour
{
    [Serializable] class Pagina { public int pagina; public int marcador; public string titulo; public string texto; }
    [Serializable] class Libro { public string libro; public Pagina[] paginas; }

    [Tooltip("Archivo en StreamingAssets con el texto de cada página")]
    public string bookFile = "libro_paginas.json";
    public ArucoUdpReceiver receiver;
    public ElevenLabsAgentClient agent;

    [Tooltip("Página en la que se considera que está el niño al iniciar (antes de detectar marcadores)")]
    public int startPage = 1;

    public int CurrentPage { get; private set; }
    public int PageCount => libro?.paginas?.Length ?? 0;
    /// <summary>Se dispara cuando cambia la página actual (por un ArUco confirmado).</summary>
    public event Action<int> PageChanged;

    Libro libro;
    readonly Dictionary<int, Pagina> byMarker = new Dictionary<int, Pagina>();

    void Awake()
    {
        CurrentPage = startPage;
        string path = Path.Combine(Application.streamingAssetsPath, bookFile);
        try
        {
            libro = JsonUtility.FromJson<Libro>(File.ReadAllText(path, Encoding.UTF8));
            foreach (var p in libro.paginas) byMarker[p.marcador] = p;
            Debug.Log($"[Libro] {libro.paginas.Length} páginas cargadas de {bookFile}.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Libro] No se pudo leer {path}: {e.Message}");
        }
    }

    void OnEnable() { if (receiver) receiver.MarkerDetected += OnMarker; }
    void OnDisable() { if (receiver) receiver.MarkerDetected -= OnMarker; }

    void OnMarker(int markerId, Vector2 pos)
    {
        if (!byMarker.TryGetValue(markerId, out var p)) return;
        if (p.pagina == CurrentPage) return;
        CurrentPage = p.pagina;
        Debug.Log($"[Libro] Página actual: {CurrentPage} ({p.titulo})");
        PageChanged?.Invoke(CurrentPage);
    }

    /// Texto de una sola página.
    public string GetTextoPagina(int page)
    {
        if (libro?.paginas == null) return "";
        foreach (var p in libro.paginas) if (p.pagina == page) return p.texto;
        return "";
    }

    /// Texto de las páginas 1 a la actual, rotulado por página.
    public string GetTextoLeido()
    {
        if (libro?.paginas == null) return "";
        var sb = new StringBuilder();
        foreach (var p in libro.paginas)
        {
            if (p.pagina > CurrentPage) continue;
            sb.Append("PÁGINA ").Append(p.pagina).Append('\n').Append(p.texto).Append("\n\n");
        }
        return sb.ToString().Trim();
    }

    public Dictionary<string, string> BuildAgentVariables() => new Dictionary<string, string>
    {
        { "pagina_actual", CurrentPage.ToString() },
        { "texto_leido", GetTextoLeido() },
    };

    /// Abre la conversación de dudas con el agente (la llamará la detección de "Principito, tengo una duda").
    [ContextMenu("Abrir conversación de dudas")]
    public void AbrirConversacionDudas()
    {
        if (agent == null) { Debug.LogWarning("[Libro] Falta asignar el agente de ElevenLabs."); return; }
        if (agent.IsRunning) return;
        Debug.Log($"[Libro] Abriendo conversación de dudas en la página {CurrentPage}.");
        agent.StartConversation(BuildAgentVariables());
    }

    /// Para probar sin cámara: fija la página manualmente.
    public void SetPage(int page) => CurrentPage = Mathf.Clamp(page, 1, Mathf.Max(1, PageCount));
}
