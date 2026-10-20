using System;
using System.IO;
using System.Linq;
using Mayo.Frontier.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PrepareFrontierPurchasedArt
{
    const string Root = "Assets/Resources/LicensedFrontier/Portfolio/";
    public static void Prepare()
    {
        var text = Resources.Load<TextAsset>("FrontierPurchasedArt");
        if (text == null) return;
        var plan = JsonUtility.FromJson<FrontierPurchasedArtPlan>(text.text);
        foreach (var image in plan.images ?? new FrontierPurchasedImage[0])
        {
            var importer = AssetImporter.GetAtPath(Root + image.file) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Purchased menu source missing: " + image.file);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(image.border[0], image.border[1], image.border[2], image.border[3]);
            importer.spritePixelsPerUnit = image.pixelsPerUnit; importer.maxTextureSize = 4096; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }
        Directory.CreateDirectory(Root + "Prepared");
        AssetDatabase.Refresh();
        foreach (var model in plan.models)
        {
            string path = Root + model.file;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("Restore the reviewed purchased presentation selection before building.", path);
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.animationType = model.character ? model.genericRig ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human : ModelImporterAnimationType.None;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.isReadable = model.pitchedRoof;
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var container = new GameObject(model.id);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, container.transform);
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (model.renderers != null && model.renderers.Length > 0)
                    foreach (var renderer in renderers) renderer.gameObject.SetActive(model.renderers.Contains(renderer.name));
                else if (model.singleBody)
                {
                    var body = renderers.OfType<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh == null ? 0 : r.sharedMesh.vertexCount).FirstOrDefault();
                    if (body == null) throw new InvalidOperationException("Purchased character has no skinned body: " + model.id);
                    foreach (var renderer in renderers) if (renderer != body) renderer.gameObject.SetActive(false);
                }
                var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + model.palette);
                if (palette == null) throw new FileNotFoundException("Purchased palette missing: " + model.palette);
                string materialPath = Root + "Prepared/" + model.id + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, materialPath); }
                material.mainTexture = palette; material.SetFloat("_Glossiness", .16f);
                foreach (var renderer in renderers)
                {
                    if (!renderer.gameObject.activeInHierarchy) continue;
                    if (model.surfaces != null && model.surfaces.Length > 0)
                    {
                        var surface = model.surfaces.FirstOrDefault(s => s.renderer == renderer.name);
                        if (surface == null || surface.materials.Length < renderer.sharedMaterials.Length)
                            throw new InvalidOperationException("Purchased original surface slots differ: " + model.id + "/" + renderer.name);
                        // Some original prefabs retain unused trailing material slots. Do not
                        // draw the final submesh twice merely to preserve an unused author slot.
                        renderer.sharedMaterials = surface.materials.Take(renderer.sharedMaterials.Length).Select(SurfaceMaterial).ToArray();
                        continue;
                    }
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                    {
                        string name = original == null ? "" : original.name.ToLowerInvariant();
                        if (!model.file.StartsWith("Survival/") || (!name.Contains("skin") && !name.Contains("eye") && !name.Contains("hair"))) return material;
                        string specialPath = Root + "Prepared/" + model.id + "-" + (name.Contains("skin") ? "skin" : "detail") + ".mat";
                        var special = AssetDatabase.LoadAssetAtPath<Material>(specialPath);
                        if (special == null) { special = new Material(Shader.Find("Standard")); special.color = name.Contains("skin") ? new Color(.73f,.56f,.37f) : new Color(.13f,.1f,.08f); special.SetFloat("_Glossiness", .1f); AssetDatabase.CreateAsset(special, specialPath); }
                        return special;
                    }).ToArray();
                }
                var visible = instance.GetComponentsInChildren<Renderer>();
                if (visible.Length == 0) throw new InvalidOperationException("No selected visible parts: " + model.id);
                if (model.pitchedRoof) OrientRoofRidge(instance);
                Bounds bounds = visible[0].bounds;
                foreach (var renderer in visible) bounds.Encapsulate(renderer.bounds);
                float scale = model.height / Mathf.Max(.001f, bounds.size.y);
                instance.transform.localScale *= scale;
                instance.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
                if (model.character)
                {
                    var animator = instance.GetComponentInChildren<Animator>();
                    if (animator == null || !model.genericRig && !animator.isHuman) throw new InvalidOperationException("Purchased humanoid mapping failed: " + model.id);
                    animator.applyRootMotion = false;
                    var rig = container.AddComponent<FrontierPurchasedCharacter>();
                    rig.purchasedLocomotion = model.goblinLocomotion;
                    rig.genericRig = model.genericRig;
                    if (!string.IsNullOrEmpty(model.idleClip)) { rig.purchasedIdle = IdleClip(model,animator.gameObject,rig); rig.idleAbsolutePose = model.idleAbsolutePose; }
                    if (model.goblinLocomotion) animator.runtimeAnimatorController = GoblinController();
                }
                PrefabUtility.SaveAsPrefabAsset(container, Root + "Prepared/" + model.id + ".prefab");
                Debug.Log("FRONTIER_PURCHASED_MODEL " + plan.gameId + " " + model.id + " parts=" + visible.Length + " height=" + model.height);
            }
            finally { UnityEngine.Object.DestroyImmediate(container); }
        }
        AssetDatabase.SaveAssets();
    }
    static AnimationClip IdleClip(FrontierPurchasedModel model,GameObject rig,FrontierPurchasedCharacter character)
    {
        var original = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+model.idleClip);
        if (original == null) throw new FileNotFoundException("Purchased exact-rig idle missing: "+model.idleClip);
        string path = Root+"Prepared/"+model.id+"-PurchasedIdle.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip,path); }
        clip.ClearCurves(); clip.frameRate = original.frameRate; clip.wrapMode = WrapMode.Loop;
        int count = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(original))
        {
            // The vendor customization clip also contains scene/renderer tracks. Keep only
            // its exact original skeleton tracks, never actor transforms or customizer state.
            if (binding.type != typeof(Transform) || !binding.path.Contains("CC_Base_") || !binding.propertyName.StartsWith("m_LocalRotation.")) continue;
            if (rig.transform.Find(binding.path) == null) throw new InvalidOperationException("Purchased idle bone binding differs: "+model.id+"/"+binding.path);
            AnimationUtility.SetEditorCurve(clip,binding,AnimationUtility.GetEditorCurve(original,binding)); count++;
        }
        if (count == 0) throw new InvalidOperationException("Purchased idle has no matching skeleton curves: "+model.id);
        character.purchasedIdleBones = AnimationUtility.GetCurveBindings(clip).GroupBy(binding => binding.path).Select(group =>
        {
            Func<string,AnimationCurve> curve = axis => AnimationUtility.GetEditorCurve(clip,group.First(binding => binding.propertyName == "m_LocalRotation."+axis));
            return new FrontierPurchasedIdleBone { path=group.Key,x=curve("x"),y=curve("y"),z=curve("z"),w=curve("w") };
        }).ToArray();
        clip.EnsureQuaternionContinuity(); EditorUtility.SetDirty(clip);
        Debug.Log("FRONTIER_PURCHASED_IDLE "+model.id+" exactBoneCurves="+count+" source="+model.idleClip);
        return clip;
    }
    static void OrientRoofRidge(GameObject instance)
    {
        var points = instance.GetComponentsInChildren<MeshFilter>().SelectMany(filter => filter.sharedMesh.vertices.Select(vertex => filter.transform.TransformPoint(vertex))).ToArray();
        if (points.Length == 0) throw new InvalidOperationException("Purchased roof has no original mesh vertices.");
        float low = points.Min(point => point.y), high = points.Max(point => point.y), range = high-low;
        var ridge = points.Where(point => point.y > low+range*.85f).Aggregate(Vector3.zero,(sum,point) => sum+point);
        ridge /= points.Count(point => point.y > low+range*.85f);
        var eave = points.Where(point => point.y < low+range*.15f).Aggregate(Vector3.zero,(sum,point) => sum+point);
        eave /= points.Count(point => point.y < low+range*.15f);
        Vector3 direction = ridge-eave; direction.y = 0;
        if (direction.sqrMagnitude < .00001f) throw new InvalidOperationException("Purchased roof is not a single pitched slope.");
        instance.transform.rotation = Quaternion.FromToRotation(direction,Vector3.forward)*instance.transform.rotation;
    }
    static Material SurfaceMaterial(FrontierPurchasedMaterial source)
    {
        string path = Root + "Prepared/Surface-" + source.id + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
        material.mainTexture = string.IsNullOrEmpty(source.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(Root + source.texture);
        if (!string.IsNullOrEmpty(source.texture) && material.mainTexture == null) throw new FileNotFoundException("Purchased surface texture missing: " + source.texture);
        material.color = new Color(source.color[0], source.color[1], source.color[2], source.color[3]);
        material.SetFloat("_Glossiness",source.smoothness); material.SetFloat("_Metallic",source.metallic);
        if (source.color[3] < .99f)
        {
            material.SetFloat("_Mode",3); material.SetOverrideTag("RenderType","Transparent");
            material.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite",0); material.EnableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue = 3000;
        }
        EditorUtility.SetDirty(material);
        return material;
    }
    static AnimatorController GoblinController()
    {
        string path = Root + "Prepared/GoblinLocomotion.controller";
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (existing != null) return existing;
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var machine = controller.layers[0].stateMachine;
        var idle = machine.AddState("Purchased goblin idle"); var walk = machine.AddState("Purchased goblin walk");
        idle.motion = Clip("Locomotion/Idle.fbx"); walk.motion = Clip("Locomotion/Walk.fbx");
        machine.defaultState = idle;
        var moving = idle.AddTransition(walk); moving.hasExitTime = false; moving.duration = .15f; moving.AddCondition(AnimatorConditionMode.Greater, .15f, "Speed");
        var stopped = walk.AddTransition(idle); stopped.hasExitTime = false; stopped.duration = .15f; stopped.AddCondition(AnimatorConditionMode.Less, .15f, "Speed");
        return controller;
    }
    static AnimationClip Clip(string file)
    {
        string path = Root + file;
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new FileNotFoundException("Purchased locomotion missing.", path);
        importer.animationType = ModelImporterAnimationType.Human; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; importer.importAnimation = true;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips) { clip.loopTime = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true; clip.lockRootRotation = true; }
        importer.clipAnimations = clips; importer.SaveAndReimport();
        var result = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (result == null) throw new InvalidOperationException("Purchased locomotion clip failed import: " + file);
        return result;
    }
}
