// Shared first-party application branding. Editor-only; no runtime/save changes.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class GameApplicationIconBuildHook : IPreprocessBuildWithReport
{
    // Run after existing per-project branding hooks, using the same authored art.
    public int callbackOrder { get { return 10000; } }
    public void OnPreprocessBuild(BuildReport report) { Apply(); }

    public static void Apply()
    {
        const string path = "Assets/Editor/Branding/GameAppIcon.png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new BuildFailedException("Missing game application icon: " + path);
        if (importer.textureType != TextureImporterType.Default || importer.mipmapEnabled ||
            !importer.alphaIsTransparency || importer.textureCompression != TextureImporterCompression.Uncompressed || importer.maxTextureSize < 1024)
        {
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (icon == null || icon.width != icon.height || icon.width < 256)
            throw new BuildFailedException("Game application icon must be square and at least256px.");
        Assign(NamedBuildTarget.Unknown, icon);
        Assign(NamedBuildTarget.Standalone, icon);
        AssetDatabase.SaveAssets();
        Debug.Log("GAME_APPLICATION_ICON " + AssetDatabase.AssetPathToGUID(path));
    }

    static void Assign(NamedBuildTarget target, Texture2D icon)
    {
        int count = UnityEditor.PlayerSettings.GetIconSizes(target, IconKind.Any).Length;
        if (count == 0) count = 1;
        UnityEditor.PlayerSettings.SetIcons(target, Enumerable.Repeat(icon, count).ToArray(), IconKind.Any);
        var installed = UnityEditor.PlayerSettings.GetIcons(target, IconKind.Any);
        if (installed == null || installed.Length != count || installed.Any(item => item != icon))
            throw new BuildFailedException("Game application icon slots failed verification for " + target.TargetName);
    }
}
