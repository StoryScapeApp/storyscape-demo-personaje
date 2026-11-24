using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cambia el fondo proyectado según la página del libro durante la lectura.
/// El fondo base (el del prólogo, que deja el onboarding) queda siempre debajo; el fondo de una página aparece
/// encima con un fundido y se va al pasar a una página que no tiene fondo propio.
/// Solo actúa después del onboarding (cuando el fondo base ya está a la vista).
/// </summary>
public class FondosPorPagina : MonoBehaviour
{
    [Serializable]
    public class FondoPagina
    {
        [Tooltip("Página del libro (marker + 1). Ej.: 2 = ArUco 1, la doble página donde empieza el capítulo I")]
        public int pagina = 2;
        public ProjectedCard fondo;
    }

    public BookPageTracker tracker;
    public OnboardingSequence onboarding;
    [Tooltip("Fondo de las páginas sin fondo propio (el del prólogo)")]
    public ProjectedCard fondoBase;
    public List<FondoPagina> fondos = new List<FondoPagina>();

    ProjectedCard actual;   // fondo de página a la vista (null = solo el base)
    bool enLectura;

    void Awake()
    {
        if (!tracker) tracker = FindFirstObjectByType<BookPageTracker>();
        if (!onboarding) onboarding = FindFirstObjectByType<OnboardingSequence>();
    }

    void Update()
    {
        // Alumno nuevo: el onboarding pone su propio fondo
        if (onboarding && onboarding.IsRunning && !(fondoBase && fondoBase.IsVisible))
        {
            if (actual) { actual.Hide(); actual = null; }
            enLectura = false;
            return;
        }
        if (!enLectura)
        {
            if (!fondoBase || !fondoBase.IsVisible) return;
            enLectura = true;
        }
        if (fondoBase && !fondoBase.IsVisible) { if (actual) { actual.Hide(); actual = null; } enLectura = false; return; }

        var deseado = Buscar(tracker ? tracker.CurrentPage : 0);
        if (deseado == actual) return;
        if (deseado) { deseado.ResetState(); deseado.Show(); }
        if (actual) actual.Hide();
        actual = deseado;
    }

    ProjectedCard Buscar(int pagina)
    {
        foreach (var f in fondos) if (f != null && f.fondo && f.pagina == pagina) return f.fondo;
        return null;
    }
}
