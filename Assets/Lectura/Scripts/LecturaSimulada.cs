using UnityEngine;

/// <summary>
/// SOLO PARA PROBAR SIN PROYECTOR NI CÁMARA (escena PruebaLectura):
/// - Al terminar el onboarding (cuando el Principito empieza a escuchar), pasa a la página indicada
///   (por defecto la 4: inicio del CAPÍTULO II) como si el niño hubiera mostrado su ArUco.
/// - Flechas ← → del teclado: página anterior / siguiente (simulan los marcadores ArUco).
/// La página se dibuja en pantalla con SimulatedBookPage.
/// </summary>
public class LecturaSimulada : MonoBehaviour
{
    public BookPageTracker tracker;
    [Tooltip("Página a la que pasa al empezar la escucha (4 = CAPÍTULO II, marker_3)")]
    public int paginaAlEscuchar = 4;
    [Tooltip("Flechas ← → cambian de página")]
    public bool teclasPagina = true;

    /// Lo llama el onboarding al terminar (onFinished).
    public void AlEscuchar()
    {
        IrA(paginaAlEscuchar);
    }

    void IrA(int page)
    {
        if (!tracker) return;
        tracker.SetPage(page);
        Debug.Log($"[Simulación] Página {tracker.CurrentPage} (como si se detectara el marker_{tracker.CurrentPage - 1}).");
    }

    void Update()
    {
        if (!teclasPagina || !tracker) return;
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        if (k == null) return;
        if (k.rightArrowKey.wasPressedThisFrame) IrA(tracker.CurrentPage + 1);
        if (k.leftArrowKey.wasPressedThisFrame) IrA(tracker.CurrentPage - 1);
#else
        if (Input.GetKeyDown(KeyCode.RightArrow)) IrA(tracker.CurrentPage + 1);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) IrA(tracker.CurrentPage - 1);
#endif
    }
}
