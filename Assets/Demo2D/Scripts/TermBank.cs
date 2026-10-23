using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Un término o frase de la lectura que se resalta en el libro y sobre el que conversa el personaje.
/// </summary>
[Serializable]
public class TermEntry
{
    [Tooltip("Término o frase tal como aparece en el libro")]
    public string term;

    [Tooltip("Significado esperado (se envía al agente como {{significado}})")]
    [TextArea(2, 4)] public string meaning;

    [Tooltip("Página del libro (referencia; más adelante, marcador ArUco asociado)")]
    public int page;

    [Header("Resaltado proyectado (unidades de la escena)")]
    public Vector2 highlightPosition = new Vector2(-2f, 2f);
    public Vector2 highlightSize = new Vector2(2.5f, 0.5f);
}

/// <summary>
/// Banco de términos de la demo. Crear: clic derecho > Create > StoryScape > Banco de términos.
/// </summary>
[CreateAssetMenu(menuName = "StoryScape/Banco de términos", fileName = "BancoTerminos")]
public class TermBank : ScriptableObject
{
    public List<TermEntry> terms = new List<TermEntry>();
}
