using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using TMPro;
using Mayo.Frontier.Presentation;

public static class PrepareFrontier
{
    public static void ImportTextResources()
    {
        if(Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF")!=null){EditorApplication.Exit(0);return;}
        double deadline=EditorApplication.timeSinceStartup+180;
        AssetDatabase.importPackageCompleted+=name=>{if(name!="TMP Essential Resources")return;AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);bool ready=Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF")!=null;Debug.Log("FRONTIER_TEXT_RESOURCES "+ready);EditorApplication.Exit(ready?0:1);};
        AssetDatabase.importPackageFailed+=(name,error)=>{Debug.LogError("Text resources import failed: "+error);EditorApplication.Exit(1);};
        EditorApplication.update+=()=>{if(EditorApplication.timeSinceStartup>deadline){Debug.LogError("Text resources import timed out.");EditorApplication.Exit(1);}};
        TMP_PackageResourceImporter.ImportResources(true,false,false);
    }
    public static void Prepare()
    {
        if(Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF")==null)throw new Exception("Run PrepareFrontier.ImportTextResources without -quit before preparing a fresh project.");
        AssetDatabase.Refresh();
        if(Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF")==null)throw new Exception("TMP essentials import did not produce Liberation Sans.");
        Directory.CreateDirectory("Assets/Resources");
        if(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/FrontierBase.mat")==null){var shader=Shader.Find("Standard");if(shader==null)throw new Exception("Built-in Standard shader unavailable.");var material=new Material(shader);material.color=Color.white;material.SetFloat("_Glossiness",.22f);AssetDatabase.CreateAsset(material,"Assets/Resources/FrontierBase.mat");}
        var glow=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/FrontierGlow.mat");if(glow==null){glow=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/FrontierBase.mat"));glow.EnableKeyword("_EMISSION");glow.SetColor("_EmissionColor",Color.white);AssetDatabase.CreateAsset(glow,"Assets/Resources/FrontierGlow.mat");}
        PreparePaidFootings();PrepareAudio();
        var profiles=JsonUtility.FromJson<GameProfiles>(Resources.Load<TextAsset>("FrontierProfiles").text);string id=Resources.Load<TextAsset>("FrontierDefault").text.Trim();var profile=profiles.games.First(p=>p.id==id);
        Directory.CreateDirectory("Assets/Scenes");if(!File.Exists("Assets/Scenes/Frontier.unity")){var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSceneManager.SaveScene(scene,"Assets/Scenes/Frontier.unity");}var scenes=EditorBuildSettings.scenes.Where(s=>s.path!="Assets/Scenes/Frontier.unity"&&s.path!="Assets/Scenes/SampleScene.unity").ToList();scenes.Insert(0,new EditorBuildSettingsScene("Assets/Scenes/Frontier.unity",true));EditorBuildSettings.scenes=scenes.ToArray();
        PlayerSettings.companyName="Mayo Games";PlayerSettings.productName=profile.title;PlayerSettings.bundleVersion="0.1.0";PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone,"com.mayogames."+id.Replace("-",""));PlayerSettings.runInBackground=true;PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.forceSingleInstance=false;
        // Input System owns both gameplay and menus. The selected template already
        // supplies its pinned packages; no manifest rewrite is needed.
        var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);var input=settings.FindProperty("activeInputHandler");if(input!=null){input.intValue=1;settings.ApplyModifiedPropertiesWithoutUndo();}
        PrepareIcon(profile.accent);AssetDatabase.SaveAssets();Debug.Log("FRONTIER_PREPARED "+profile.id);
    }
    static void PreparePaidFootings()
    {
        string texturePath="Assets/Resources/LicensedFrontier/SplineBuilderFooting/Polygon_Texture2.png";var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);if(texture==null){Debug.Log("FRONTIER_PAID_FOOTINGS_NOT_RESTORED");return;}
        var material=new Material(Shader.Find("Standard"));material.mainTexture=texture;material.SetFloat("_Glossiness",.15f);string materialPath="Assets/Resources/LicensedFrontier/SplineBuilderFooting/FootingPalette.mat";
        var prior=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(prior==null)AssetDatabase.CreateAsset(material,materialPath);else{UnityEngine.Object.DestroyImmediate(material);material=prior;}
        foreach(string name in new[]{"SM_StoneFoot","SM_StoneFootConnector"})
        {
            string path="Assets/Resources/LicensedFrontier/SplineBuilderFooting/"+name+".fbx";var importer=AssetImporter.GetAtPath(path)as ModelImporter;
            if(importer==null)throw new Exception("Selected footing mesh missing: "+name);importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);var instance=(GameObject)PrefabUtility.InstantiatePrefab(source);foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=Enumerable.Repeat(material,Mathf.Max(1,renderer.sharedMaterials.Length)).ToArray();
            PrefabUtility.SaveAsPrefabAsset(instance,"Assets/Resources/LicensedFrontier/SplineBuilderFooting/"+name+"_Prepared.prefab");UnityEngine.Object.DestroyImmediate(instance);
        }
    }
    static void PrepareIcon(string color)
    {
        string path="Assets/Resources/FrontierIcon.png";ColorUtility.TryParseHtmlString(color,out var accent);var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);var pixels=new Color[128*128];
        for(int y=0;y<128;y++)for(int x=0;x<128;x++){float dx=Math.Abs(x-64),dy=Math.Abs(y-64);bool gem=dx*.9f+dy<50;bool centre=dx*.8f+dy<25;pixels[y*128+x]=gem?(centre?new Color(.93f,.94f,.85f):accent):new Color(.025f,.065f,.075f,1);}
        texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);var icon=AssetDatabase.LoadAssetAtPath<Texture2D>(path);PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown,new[]{icon},IconKind.Any);
    }
    static void PrepareAudio()
    {
        Directory.CreateDirectory("Assets/Resources/FrontierAudio");foreach(string name in new[]{"Theme","Confirm","Strike","Collect"})
        {
            string path="Assets/Resources/FrontierAudio/"+name+".wav";if(File.Exists(path))continue;int rate=22050;float duration=name=="Theme"?12:name=="Strike"?.15f:.22f;int count=(int)(duration*rate);
            using(var stream=File.Create(path))using(var w=new BinaryWriter(stream)){w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+count*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(count*2);for(int i=0;i<count;i++){double t=(double)i/rate,f=name=="Strike"?130:name=="Collect"?660:440;double value;if(name=="Theme"){int beat=(int)(t*2)%8;double[] notes={220,261.63,329.63,261.63,196,246.94,293.66,246.94};value=.055*(Math.Sin(t*notes[beat]*Math.PI*2)+.35*Math.Sin(t*notes[beat]*Math.PI*4));}else value=.15*Math.Sin(t*f*Math.PI*2)*Math.Exp(-t*16);value*=Math.Min(1,Math.Min(t/.025,(duration-t)/.04));w.Write((short)(value*32767));}}AssetDatabase.ImportAsset(path);
        }
    }
    public static void Build()
    {
        Prepare();string output=Environment.GetCommandLineArgs().First(a=>a.StartsWith("-frontierOutput=")).Substring("-frontierOutput=".Length);string targetName=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-frontierTarget="))?.Substring("-frontierTarget=".Length)??"StandaloneOSX";var target=(BuildTarget)Enum.Parse(typeof(BuildTarget),targetName);
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Select(s=>s.path).ToArray(),locationPathName=output,target=target,options=BuildOptions.Development});
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"native-build.json"),JsonUtility.ToJson(new Result{status=report.summary.result.ToString(),errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,bytes=report.summary.totalSize},true));
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Native build failed: "+report.summary.result);
    }
    [Serializable]sealed class Result{public string status;public int errors,warnings;public ulong bytes;}
}
