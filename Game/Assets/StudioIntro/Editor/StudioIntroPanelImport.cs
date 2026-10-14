using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProgrammingSecurely.EditorTools
{
    // Restore image bytes only. No third-party package code or prefab scripts are executed.
    public sealed class StudioIntroPanelImport : IPreprocessBuildWithReport
    {
        [Serializable] sealed class Panel { public string cacheFile,sha256; public long bytes; }
        [Serializable] sealed class Settings { public Panel purchasedPanel; }
        public int callbackOrder { get { return -900; } }
        [InitializeOnLoadMethod] static void Initialize() { EditorApplication.delayCall += () => { if (!Application.isBatchMode) Restore(false); }; }
        public void OnPreprocessBuild(BuildReport report) { Restore(true); }
        static void Restore(bool required)
        {
            var config=Resources.Load<TextAsset>("StudioIntro"); if (!config) return;
            var panel=JsonUtility.FromJson<Settings>(config.text)?.purchasedPanel; if (panel==null) return;
            if (string.IsNullOrEmpty(panel.cacheFile) || Path.GetFileName(panel.cacheFile)!=panel.cacheFile || panel.bytes<1 || panel.bytes>4*1024*1024 || panel.sha256==null || panel.sha256.Length!=64) throw new BuildFailedException("Invalid purchased intro panel manifest.");
            string root=Environment.GetEnvironmentVariable("STUDIO_INTRO_ASSET_CACHE");
            if (string.IsNullOrEmpty(root)) root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Caches","GameAssets","studio-intro");
            string cached=Path.Combine(root,panel.cacheFile),target="Assets/Resources/StudioIntroArt/Panel.png";
            if (!File.Exists(cached)) { string text="Restore purchased intro panel cache before building: "+panel.cacheFile+". See Docs/StudioIntro.md."; if(required)throw new BuildFailedException(text); Debug.LogWarning(text); return; }
            byte[] bytes=File.ReadAllBytes(cached);string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            if(bytes.Length!=panel.bytes || hash!=panel.sha256.ToLowerInvariant())throw new BuildFailedException("Purchased intro panel cache hash mismatch.");
            if(File.Exists(target)) { byte[] old=File.ReadAllBytes(target);using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(old)).Replace("-","").ToLowerInvariant()!=hash)throw new BuildFailedException("Preserving modified intro panel asset."); }
            else { Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,bytes); }
            AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var importer=AssetImporter.GetAtPath(target) as TextureImporter;
            if(importer && (importer.textureType!=TextureImporterType.Sprite || importer.mipmapEnabled)) { importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport(); }
        }
    }
}
