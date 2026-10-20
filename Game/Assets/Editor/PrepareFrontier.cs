using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

/// <summary>Build entry points used by tools/playtest/build.py (names kept for the shared playtest contract).
/// Prepares the Ashfall County survival scene, owned art when restored, and player settings.</summary>
public static class PrepareFrontier
{
    const string Scene = "Assets/Scenes/Ashfall.unity";

    public static void ImportTextResources()
    {
        if (Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF") != null) { EditorApplication.Exit(0); return; }
        double deadline = EditorApplication.timeSinceStartup + 180;
        AssetDatabase.importPackageCompleted += name => { if (name != "TMP Essential Resources") return; AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); bool ready = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF") != null; Debug.Log("FRONTIER_TEXT_RESOURCES " + ready); EditorApplication.Exit(ready ? 0 : 1); };
        AssetDatabase.importPackageFailed += (name, error) => { Debug.LogError("Text resources import failed: " + error); EditorApplication.Exit(1); };
        EditorApplication.update += () => { if (EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Text resources import timed out."); EditorApplication.Exit(1); } };
        TMP_PackageResourceImporter.ImportResources(true, false, false);
    }

    public static void Prepare()
    {
        if (Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF") == null) throw new Exception("Run PrepareFrontier.ImportTextResources without -quit before preparing a fresh project.");
        AssetDatabase.Refresh();
        if (Shader.Find("Ashfall/VertexLit") == null) throw new Exception("Ashfall vertex shader failed to compile.");
        PrepareAshfallArt.Prepare();
        Directory.CreateDirectory("Assets/Scenes");
        if (!File.Exists(Scene)) { var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); EditorSceneManager.SaveScene(scene, Scene); }
        // The survival game is the only scene in the build. Legacy scenes stay on disk, unused.
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
        PlayerSettings.companyName = "Mayo Games"; PlayerSettings.productName = "Ashfall County"; PlayerSettings.bundleVersion = "0.2.0";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.mayogames.ashfallcounty");
        PlayerSettings.runInBackground = true; PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true; PlayerSettings.forceSingleInstance = false;
        // Input System owns gameplay and menus.
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]); var input = settings.FindProperty("activeInputHandler"); if (input != null) { input.intValue = 1; settings.ApplyModifiedPropertiesWithoutUndo(); }
        IncludeShaders();
        GameApplicationIconBuildHook.Apply(); AssetDatabase.SaveAssets(); Debug.Log("ASHFALL_PREPARED");
    }

    /// <summary>Runtime-created materials need their shaders in the player.</summary>
    static void IncludeShaders()
    {
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var list = graphics.FindProperty("m_AlwaysIncludedShaders");
        foreach (var name in new[] { "Ashfall/VertexLit", "Ashfall/Glass", "Standard", "Sprites/Default" })
        {
            var shader = Shader.Find(name); if (shader == null) continue;
            bool present = false; for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
            if (present) continue;
            list.InsertArrayElementAtIndex(list.arraySize); list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
        }
        graphics.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void Build()
    {
        Prepare();
        string output = Environment.GetCommandLineArgs().First(a => a.StartsWith("-frontierOutput=")).Substring("-frontierOutput=".Length);
        string targetName = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-frontierTarget="))?.Substring("-frontierTarget=".Length) ?? "StandaloneOSX";
        var target = (BuildTarget)Enum.Parse(typeof(BuildTarget), targetName);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = EditorBuildSettings.scenes.Select(s => s.path).ToArray(), locationPathName = output, target = target, options = BuildOptions.Development });
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "native-build.json"), JsonUtility.ToJson(new Result { status = report.summary.result.ToString(), errors = report.summary.totalErrors, warnings = report.summary.totalWarnings, bytes = report.summary.totalSize }, true));
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Native build failed: " + report.summary.result);
    }
    [Serializable] sealed class Result { public string status; public int errors, warnings; public ulong bytes; }
}
