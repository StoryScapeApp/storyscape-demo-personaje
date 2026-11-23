using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build de macOS para la presentación (pantalla completa, sin ventana).
/// Menú: StoryScape → Build macOS. En consola: Unity -batchmode -quit -projectPath . -executeMethod BuildMac.Build [-buildPath ruta]
/// </summary>
public static class BuildMac
{
    const string RutaPorDefecto = "~/Desktop/StoryScape-Build/StoryScape.app";

    [MenuItem("StoryScape/Build macOS")]
    public static void Build()
    {
        string ruta = Environment.GetEnvironmentVariable("STORYSCAPE_BUILD_PATH");
        if (string.IsNullOrEmpty(ruta)) ruta = RutaPorDefecto;
        ruta = ruta.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta));

        // Pantalla completa (sin bordes) y permiso de micrófono (sin esto macOS no deja usar Scribe ni el agente)
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.macOS.microphoneUsageDescription = "StoryScape escucha al niño mientras lee en voz alta.";
        PlayerSettings.runInBackground = true;

        var opciones = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Onboarding.unity" },
            locationPathName = ruta,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        var r = BuildPipeline.BuildPlayer(opciones);
        Debug.Log($"[Build] {r.summary.result} → {ruta} ({r.summary.totalSize / (1024 * 1024)} MB, {r.summary.totalErrors} errores)");
        if (Application.isBatchMode) EditorApplication.Exit(r.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
