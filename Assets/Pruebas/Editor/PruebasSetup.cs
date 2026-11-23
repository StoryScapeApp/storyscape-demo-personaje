using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menú "StoryScape/Pruebas": agrega a la escena el objeto "PruebasVoz" (desactivado) con el ejecutor de pruebas
/// de voz y agentes (PruebasRunner). Para correr una prueba: activa "PruebasVoz", elige el Modo en el Inspector y da Play.
/// (La detección de páginas usa otro objeto: PruebaDeteccion.)
/// </summary>
public static class PruebasSetup
{
    [MenuItem("StoryScape/Pruebas/Agregar ejecutor de pruebas de voz y agentes")]
    public static void Agregar()
    {
        var runner = Object.FindFirstObjectByType<PruebasRunner>(FindObjectsInactive.Include);
        GameObject go;
        if (runner) go = runner.gameObject;
        else
        {
            go = new GameObject("PruebasVoz");
            Undo.RegisterCreatedObjectUndo(go, "Agregar ejecutor de pruebas");
            runner = go.AddComponent<PruebasRunner>();
        }
        runner.lectura = Object.FindFirstObjectByType<ReadingSession>(FindObjectsInactive.Include);
        runner.scribe = runner.lectura && runner.lectura.scribe ? runner.lectura.scribe : Object.FindFirstObjectByType<ScribeRealtimeClient>(FindObjectsInactive.Include);
        runner.tracker = Object.FindFirstObjectByType<BookPageTracker>(FindObjectsInactive.Include);
        runner.quiz = Object.FindFirstObjectByType<QuizSession>(FindObjectsInactive.Include);
        runner.onboarding = Object.FindFirstObjectByType<OnboardingSequence>(FindObjectsInactive.Include);
        runner.receptor = Object.FindFirstObjectByType<ArucoUdpReceiver>(FindObjectsInactive.Include);
        runner.fondos = Object.FindFirstObjectByType<FondosPorPagina>(FindObjectsInactive.Include);

        var fuentes = go.GetComponents<AudioSource>();
        runner.altavoz = fuentes.Length > 0 ? fuentes[0] : go.AddComponent<AudioSource>();
        runner.ruido = fuentes.Length > 1 ? fuentes[1] : go.AddComponent<AudioSource>();
        runner.altavoz.playOnAwake = runner.ruido.playOnAwake = false;
        runner.altavoz.spatialBlend = runner.ruido.spatialBlend = 0f;

        go.SetActive(false);   // solo se usa al hacer pruebas
        EditorUtility.SetDirty(runner);
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("[Pruebas] Objeto \"PruebasVoz\" listo (desactivado). Actívalo, elige el Modo y da Play.");
    }

    [MenuItem("StoryScape/Pruebas/Abrir carpeta de pruebas")]
    public static void AbrirCarpeta()
    {
        string ruta = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Pruebas", "planes");
        Directory.CreateDirectory(ruta);
        EditorUtility.RevealInFinder(ruta);
    }
}
