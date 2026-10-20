using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Turns the privately restored, hash-pinned owned packs into prepared prefabs and writes
/// Resources/AshfallArt.json (role -> prefab). Missing packs are skipped; the game falls back to procedural art.
/// No vendor scripts are imported; only meshes, textures, clips and audio.</summary>
public static class PrepareAshfallArt
{
    const string Root = "Assets/Resources/LicensedFrontier/Portfolio/";
    const string Z = Root + "Zomboid/";
    const string Out = Root + "Zomboid/Prepared/";
    static readonly List<ArtBinding> bindings = new List<ArtBinding>();
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    public static void Prepare()
    {
        bindings.Clear(); materials.Clear();
        if (!AssetDatabase.IsValidFolder(Z.TrimEnd('/'))) { Debug.Log("ASHFALL_ART_NOT_RESTORED (procedural art will be used)"); WritePlan(); return; }
        Directory.CreateDirectory(Out);
        AssetDatabase.Refresh();
        Sprites();
        Props();
        Items();
        Vehicles();
        Nature();
        Zombies();
        Survivors();
        WritePlan();
        AssetDatabase.SaveAssets();
    }

    static void WritePlan()
    {
        var plan = new ArtPlan { bindings = bindings.ToArray() };
        File.WriteAllText("Assets/Resources/AshfallArt.json", JsonUtility.ToJson(plan, true));
        AssetDatabase.ImportAsset("Assets/Resources/AshfallArt.json", ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("ASHFALL_ART_PLAN roles=" + bindings.Count);
    }
    static string ResourcePath(string assetPath) { const string r = "Assets/Resources/"; var p = assetPath.Substring(r.Length); return p.Substring(0, p.Length - Path.GetExtension(p).Length); }
    static void Bind(string role, string prefabPath, float scale = 1, float yaw = 0) { bindings.Add(new ArtBinding { role = role, prefab = ResourcePath(prefabPath), scale = scale, yaw = yaw }); }

    // ---------------------------------------------------------------- textures
    static void Sprites()
    {
        if (!Directory.Exists(Z + "UI/ApocalypseHUD")) return;
        foreach (var path in Directory.GetFiles(Z + "UI/ApocalypseHUD", "*.png", SearchOption.AllDirectories))
        {
            var importer = AssetImporter.GetAtPath(path.Replace('\\', '/')) as TextureImporter; if (importer == null) continue;
            if (importer.textureType == TextureImporterType.Sprite) continue;
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            if (Path.GetFileName(path).StartsWith("SPR_HUD_Frame") || Path.GetFileName(path).StartsWith("SPR_Apocalypse_Box")) importer.spriteBorder = new Vector4(24, 24, 24, 24);
            importer.SaveAndReimport();
        }
    }
    static Material Palette(string texturePath, string name)
    {
        Material m; if (materials.TryGetValue(texturePath, out m)) return m;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath); if (tex == null) { Debug.LogWarning("Palette missing: " + texturePath); return null; }
        string path = Out + "M_" + name + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.mainTexture = tex; m.SetFloat("_Glossiness", .12f); m.enableInstancing = true; EditorUtility.SetDirty(m);
        materials[texturePath] = m; return m;
    }
    static Material MainPalette { get { return Palette(Z + "POLY/Textures/MainTexture.png", "PolyMain"); } }
    static Material SecondPalette { get { return Palette(Z + "POLY/Textures/Polygon_Texture.png", "PolyVol2"); } }

    // ---------------------------------------------------------------- static models
    enum Fit { Footprint, Length, Height }
    /// <summary>Imports a static mesh, applies a palette, normalizes size and pivot, and saves a prefab.</summary>
    static string StaticPrefab(string file, string id, Material material, Fit fit, float size, float maxHeight = 99, bool longestAlongZ = false)
    {
        string path = Z + file;
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) { Debug.LogWarning("ASHFALL_ART_MISSING " + file); return null; }
        if (material == null) return null;
        importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.animationType = ModelImporterAnimationType.None; importer.importAnimation = false; importer.isReadable = false;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (source == null) return null;
        var container = new GameObject(id);
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, container.transform);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, r.sharedMaterials.Length)).ToArray();
            foreach (var c in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            var b = Bounds(instance); if (b.size == Vector3.zero) return null;
            if (longestAlongZ && b.size.x > b.size.z) { instance.transform.localRotation = Quaternion.Euler(0, 90, 0) * instance.transform.localRotation; b = Bounds(instance); }
            float scale = fit == Fit.Height ? size / Mathf.Max(.001f, b.size.y) : fit == Fit.Length ? size / Mathf.Max(.001f, Mathf.Max(b.size.x, b.size.y, b.size.z)) : size / Mathf.Max(.001f, Mathf.Max(b.size.x, b.size.z));
            scale = Mathf.Min(scale, maxHeight / Mathf.Max(.001f, b.size.y));
            instance.transform.localScale *= scale; b = Bounds(instance);
            instance.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            string prefab = Out + id + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(container, prefab);
            return prefab;
        }
        catch (Exception e) { Debug.LogWarning("ASHFALL_ART_FAILED " + file + ": " + e.Message); return null; }
        finally { UnityEngine.Object.DestroyImmediate(container); }
    }
    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    static void Prop(string role, string file, float footprint, float maxHeight, Material m = null)
    {
        var p = StaticPrefab(file, "Prop_" + role, m ?? MainPalette, Fit.Footprint, footprint, maxHeight);
        if (p != null) Bind("prop/" + role, p);
    }
    static void Props()
    {
        Prop("Fridge", "Props/Construction/SM_Fridge.fbx", .9f, 1.9f);
        Prop("Bed", "Props/Construction/SM_Bed.fbx", 1.0f, .9f);
        Prop("Couch", "Props/Interior/SM_Sofa_A.fbx", 1.0f, .95f);
        Prop("Dresser", "Props/Construction/SM_Dresser_A.fbx", .95f, 1.1f);
        Prop("Wardrobe", "Props/Construction/SM_Cupboard.fbx", .95f, 2.0f);
        Prop("Shelf", "Props/Construction/SM_Food_Shelf_01.fbx", .98f, 1.8f);
        Prop("Crate", "Props/Construction/SM_Box_01.fbx", .85f, .9f);
        Prop("WoodCrate", "Props/Interior/SM_Wooden_Box_A.fbx", .85f, .9f);
        Prop("Desk", "Props/Construction/SM_Table_01.fbx", .98f, .85f);
        Prop("Table", "Props/Interior/SM_Table.fbx", .95f, .85f);
        Prop("Toolbox", "Props/Construction/SM_Tool_Cabinet_01.fbx", .95f, 1.4f);
        Prop("Workbench", "Props/Construction/SM_Workbench_01.fbx", .98f, 1.2f);
        Prop("Toilet", "Props/Interior/SM_Toilet.fbx", .7f, .9f);
        Prop("Sink", "Props/Interior/SM_Wash_Basin.fbx", .8f, 1.1f);
        Prop("Stove", "Props/Interior/SM_Cooking_Table.fbx", .95f, 1.0f);
        Prop("Counter", "Props/Construction/SM_Floor_Cabinet_A.fbx", .98f, 1.0f);
        Prop("MedicineCabinet", "Props/Construction/SM_Hanging_Cabinet_A.fbx", .8f, 1.6f);
        Prop("Locker", "Props/Interior/SM_Locker_Gray.fbx", .95f, 2.0f);
        Prop("RainCollector", "Props/Workshop/SM_Barrel_A_01.fbx", .9f, 1.2f);
        Prop("Bin", "Props/Construction/SM_Garbage_Pack.fbx", .8f, 1.0f);
        Prop("HayBale", "Props/Construction/SM_Pile_Of_Firewood_A.fbx", .95f, .8f);
    }
    static void Item(string id, string file, float length, Material m)
    {
        var p = StaticPrefab(file, "Item_" + id, m, Fit.Length, length, 99, true);
        if (p != null) Bind("item/" + id, p);
    }
    static void Items()
    {
        var a = MainPalette; var b = SecondPalette;
        Item("baseball_bat", "Weapons/Melee/SM_Kricket_Bat.fbx", .95f, a);
        Item("nailed_bat", "Weapons/Melee/SM_Baseball_bat(Nails_reinforced).fbx", .95f, a);
        Item("axe", "Weapons/Melee/SM_Fire_Axe.fbx", .95f, a);
        Item("kitchen_knife", "Weapons/Melee/SM_Cleaver.fbx", .32f, a);
        Item("hunting_knife", "Weapons/Melee/SM_Survival_Knife.fbx", .32f, a);
        Item("machete", "Weapons/Melee/SM_Machete.fbx", .65f, a);
        Item("golf_club", "Weapons/Melee/SM_Gofl_Club.fbx", 1.0f, a);
        Item("pipe", "Weapons/Melee/SM_Pipe_01.fbx", .85f, a);
        Item("hammer", "Weapons/Melee/SM_Hammer.fbx", .35f, a);
        Item("shovel", "Weapons/Melee/SM_Shovel.fbx", 1.1f, a);
        Item("crowbar", "Props/Tools/SM_Crowbar.fbx", .7f, b);
        Item("plank", "Props/Workshop/SM_Wooden_Plank_A.fbx", 1.0f, a);
        Item("pistol", "Weapons/Firearms/SM_Police_Gun.fbx", .24f, b);
        Item("revolver", "Weapons/Firearms/SM_Revolver.fbx", .28f, b);
        Item("shotgun", "Weapons/Firearms/Modern_Pump_Action_Shotgun.fbx", 1.0f, b);
        Item("rifle", "Weapons/Firearms/SM_Hunting_Sniper_Rifle.fbx", 1.1f, b);
        Item("flashlight", "Props/Tools/SM_FLASHLIGHT.fbx", .22f, b);
    }
    static void Vehicles()
    {
        int i = 0;
        foreach (var f in new[] { "Vehicles/Car_02.fbx", "Vehicles/Car_03.fbx", "Vehicles/Truck.fbx" })
        {
            var p = StaticPrefab(f, "Vehicle_" + i, MainPalette, Fit.Length, i == 2 ? 4.6f : 4.1f, 99, true);
            if (p != null) Bind("vehicle/" + i++, p);
        }
    }
    static void Nature()
    {
        int t = 0, s = 0;
        foreach (var f in new[] { "Nature/SM_Tree_01.fbx", "Nature/SM_Tree_02.fbx", "Nature/SM_Tree_03.fbx", "Nature/SM_Tree_04.fbx" }) { var p = StaticPrefab(f, "Tree_" + t, MainPalette, Fit.Height, 5.5f); if (p != null) Bind("tree/" + t++, p); }
        foreach (var f in new[] { "Nature/SM_Bush_01.fbx", "Nature/SM_Bush_02.fbx", "Nature/SM_Bush_03.fbx" }) { var p = StaticPrefab(f, "Bush_" + s, MainPalette, Fit.Height, 1.0f); if (p != null) Bind("bush/" + s++, p); }
    }

    // ---------------------------------------------------------------- animated characters
    static AnimationClip FirstClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }
    static AnimationClip ImportClip(string path, ModelImporterAnimationType type, Avatar source, bool loop)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter; if (importer == null) return null;
        importer.animationType = type; importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.importAnimation = true;
        if (type == ModelImporterAnimationType.Generic && source != null) { importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = source; }
        else importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips) { c.loopTime = loop; c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true; c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true; }
        importer.clipAnimations = clips; importer.SaveAndReimport();
        return FirstClip(path);
    }

    /// <summary>Controller with Speed, Down and Attack/Hit triggers, shared by survivors and zombies.</summary>
    static AnimatorController Controller(string name, AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip attack, AnimationClip hit, AnimationClip dead, float walkSpeed, float runSpeed)
    {
        string path = Out + name + ".controller";
        AssetDatabase.DeleteAsset(path);
        var c = AnimatorController.CreateAnimatorControllerAtPath(path);
        c.AddParameter("Speed", AnimatorControllerParameterType.Float); c.AddParameter("Down", AnimatorControllerParameterType.Bool);
        c.AddParameter("Attack", AnimatorControllerParameterType.Trigger); c.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        var sm = c.layers[0].stateMachine;
        BlendTree tree; var move = c.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendParameter = "Speed"; tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0); if (walk != null) tree.AddChild(walk, walkSpeed); if (run != null) tree.AddChild(run, runSpeed);
        sm.defaultState = move;
        if (attack != null) { var s = sm.AddState("Attack"); s.motion = attack; var t = sm.AddAnyStateTransition(s); t.AddCondition(AnimatorConditionMode.If, 0, "Attack"); t.duration = .08f; t.canTransitionToSelf = false; var back = s.AddTransition(move); back.hasExitTime = true; back.exitTime = .9f; back.duration = .12f; }
        if (hit != null) { var s = sm.AddState("Hit"); s.motion = hit; var t = sm.AddAnyStateTransition(s); t.AddCondition(AnimatorConditionMode.If, 0, "Hit"); t.duration = .05f; t.canTransitionToSelf = false; var back = s.AddTransition(move); back.hasExitTime = true; back.exitTime = .9f; back.duration = .1f; }
        if (dead != null)
        {
            var s = sm.AddState("Down"); s.motion = dead; var t = sm.AddAnyStateTransition(s); t.AddCondition(AnimatorConditionMode.If, 0, "Down"); t.duration = .1f; t.canTransitionToSelf = false;
            var up = s.AddTransition(move); up.AddCondition(AnimatorConditionMode.IfNot, 0, "Down"); up.duration = .3f; up.hasExitTime = false;
        }
        EditorUtility.SetDirty(c);
        return c;
    }

    static void Zombies()
    {
        var types = new[] { new[] { "Drinker", "Drinker_Mesh", "Drinker", "Drinker_Albedo.tga" }, new[] { "Soldier", "Soldier_Mesh", "Soldier", "Soldier_Albedo.tga" }, new[] { "Medic", "Medic_Mesh", "Medic", "Medic_Albedo.tga" }, new[] { "Fat", "Fat_Zombie_Mesh", "Fat_Zombies", "Fat_Zombie_Albedo.tga" } };
        int index = 0;
        foreach (var t in types)
        {
            string dir = Z + "Characters/Zombies/" + (t[0] == "Fat" ? "Fat_Zombie" : t[0] + "_Zombie") + "/";
            string mesh = dir + t[1] + ".FBX";
            var importer = AssetImporter.GetAtPath(mesh) as ModelImporter; if (importer == null) { Debug.LogWarning("ASHFALL_ART_MISSING zombie " + t[0]); continue; }
            try
            {
                importer.animationType = ModelImporterAnimationType.Generic; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.importAnimation = false;
                importer.SaveAndReimport();
                var avatar = AssetDatabase.LoadAllAssetsAtPath(mesh).OfType<Avatar>().FirstOrDefault();
                var texImporter = AssetImporter.GetAtPath(dir + t[3]) as TextureImporter; if (texImporter != null && texImporter.maxTextureSize > 1024) { texImporter.maxTextureSize = 1024; texImporter.SaveAndReimport(); }
                var material = Palette(dir + t[3], "Zombie_" + t[0]);
                Func<string, bool, AnimationClip> clip = (name, loop) => File.Exists(dir + t[2] + "@" + name + ".FBX") ? ImportClip(dir + t[2] + "@" + name + ".FBX", ModelImporterAnimationType.Generic, avatar, loop) : null;
                var idle = clip("Idle", true); var walk = clip("Walk_Z", true); var run = clip("Run_Z", true); var attack = clip("Attack_1", false); var hit = clip("Get_Hit", false); var dead = clip("Dead", false);
                if (idle == null || walk == null) { Debug.LogWarning("ASHFALL_ART zombie clips missing " + t[0]); continue; }
                var controller = Controller("Zombie_" + t[0], idle, walk, run ?? walk, attack, hit, dead, 1.0f, run != null ? 2.6f : 1.8f);
                var container = new GameObject("Zombie_" + t[0]);
                try
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(mesh), container.transform);
                    foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, r.sharedMaterials.Length)).ToArray();
                    var animator = instance.GetComponentInChildren<Animator>() ?? instance.AddComponent<Animator>();
                    animator.runtimeAnimatorController = controller; animator.avatar = avatar; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    var b = Bounds(instance); float scale = 1.8f / Mathf.Max(.001f, b.size.y);
                    instance.transform.localScale *= scale; b = Bounds(instance); instance.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                    string prefab = Out + "Zombie_" + t[0] + ".prefab"; PrefabUtility.SaveAsPrefabAsset(container, prefab);
                    Bind("zombie/" + index++, prefab);
                    Debug.Log("ASHFALL_ART zombie " + t[0] + " clips=" + new[] { idle, walk, run, attack, hit, dead }.Count(x => x != null));
                }
                finally { UnityEngine.Object.DestroyImmediate(container); }
            }
            catch (Exception e) { Debug.LogWarning("ASHFALL_ART_FAILED zombie " + t[0] + ": " + e.Message); }
        }
    }

    static void Survivors()
    {
        string dir = Z + "Animations/Mannequin/";
        Func<string, bool, AnimationClip> clip = (name, loop) => File.Exists(dir + "Warrior@" + name + ".FBX") ? ImportClip(dir + "Warrior@" + name + ".FBX", ModelImporterAnimationType.Human, null, loop) : null;
        AnimatorController controller = null;
        try
        {
            var idle = clip("Idle_Without_Weapon", true); var walk = clip("Walk_Without_Weapon", true); var run = clip("Run_Without_Weapon", true);
            var attack = clip("Attack_1", false); var hit = clip("Get_Hit_1", false); var dead = clip("Dead_2_Without_Weapon", false);
            bool human = idle != null && AssetDatabase.LoadAllAssetsAtPath(dir + "Warrior@Idle_Without_Weapon.FBX").OfType<Avatar>().Any(a => a.isValid && a.isHuman);
            if (human) controller = Controller("Survivor", idle, walk, run, attack, hit, dead, 2.1f, 4.1f);
            else Debug.LogWarning("ASHFALL_ART survivor clips are not a valid humanoid; procedural gait will be used");
        }
        catch (Exception e) { Debug.LogWarning("ASHFALL_ART_FAILED survivor clips: " + e.Message); }
        Survivor("survivor_female", "Survival/Female_Survival_Unity_SK.fbx", new[] { "CHR_BaseWemale_Head", "CHR_Female_Pant_01A_Long", "CHR_Female_Torso_01AOut", "CHR_Hair_05", "CHR_Shoe_01A" }, controller);
        Survivor("survivor_male", "Survival/Male_Survival_Unity_SK.fbx", new[] { "CHR_BaseMale_Head", "CHR_Male_Pant_01A_Long", "CHR_Male_Torso_01AOut", "CHR_Hair_01", "CHR_Shoe_01A" }, controller);
    }
    static void Survivor(string role, string file, string[] parts, AnimatorController controller)
    {
        string path = Root + file;
        var importer = AssetImporter.GetAtPath(path) as ModelImporter; if (importer == null) { Debug.LogWarning("ASHFALL_ART_MISSING " + file); return; }
        try
        {
            importer.animationType = ModelImporterAnimationType.Human; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; importer.materialLocation = ModelImporterMaterialLocation.InPrefab; importer.importAnimation = false;
            importer.SaveAndReimport();
            var palette = Palette(Root + "Survival/T_Main_A.png", "Survivor");
            var skin = AssetDatabase.LoadAssetAtPath<Material>(Out + "M_Skin.mat"); if (skin == null) { skin = new Material(Shader.Find("Standard")) { color = new Color(.78f, .6f, .46f) }; AssetDatabase.CreateAsset(skin, Out + "M_Skin.mat"); }
            var dark = AssetDatabase.LoadAssetAtPath<Material>(Out + "M_Detail.mat"); if (dark == null) { dark = new Material(Shader.Find("Standard")) { color = new Color(.14f, .1f, .08f) }; AssetDatabase.CreateAsset(dark, Out + "M_Detail.mat"); }
            var container = new GameObject(role);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), container.transform);
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    r.gameObject.SetActive(parts.Contains(r.name));
                    r.sharedMaterials = r.sharedMaterials.Select(m => { string n = m == null ? "" : m.name.ToLowerInvariant(); return n.Contains("skin") ? skin : n.Contains("eye") || n.Contains("hair") ? dark : palette; }).ToArray();
                }
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman) { Debug.LogWarning("ASHFALL_ART survivor is not humanoid: " + role); return; }
                animator.applyRootMotion = false; animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var b = Bounds(instance); float scale = 1.78f / Mathf.Max(.001f, b.size.y);
                instance.transform.localScale *= scale; b = Bounds(instance); instance.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                string prefab = Out + "Survivor_" + role + ".prefab"; PrefabUtility.SaveAsPrefabAsset(container, prefab);
                Bind(role, prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(container); }
        }
        catch (Exception e) { Debug.LogWarning("ASHFALL_ART_FAILED " + role + ": " + e.Message); }
    }
}
