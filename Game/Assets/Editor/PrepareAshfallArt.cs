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
/// No vendor scripts are imported; only meshes, textures, clips and audio.
/// Selections: ContentSource/Art/zomboid-selection.json (Portfolio/Zomboid, Portfolio/Survival) and
/// decay-selection.json (Portfolio/Decay: freaks, plague heart, military props, guns and throwables).</summary>
public static class PrepareAshfallArt
{
    const string Root = "Assets/Resources/LicensedFrontier/Portfolio/";
    const string Z = Root + "Zomboid/";
    const string D = Root + "Decay/";
    const string Out = Root + "Zomboid/Prepared/";
    static readonly List<ArtBinding> bindings = new List<ArtBinding>();
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
    static bool decay;

    public static void Prepare()
    {
        bindings.Clear(); materials.Clear(); clips.Clear();
        if (!AssetDatabase.IsValidFolder(Z.TrimEnd('/'))) { Debug.Log("ASHFALL_ART_NOT_RESTORED (procedural art will be used)"); WritePlan(); return; }
        decay = AssetDatabase.IsValidFolder(D.TrimEnd('/'));
        if (!decay) Debug.Log("ASHFALL_ART_DECAY_NOT_RESTORED (freaks, military props and new guns stay procedural)");
        Directory.CreateDirectory(Out);
        AssetDatabase.Refresh();
        Sprites();
        Props();
        Items();
        Vehicles();
        Nature();
        Zombies();
        Survivors();
        if (decay) Heart();
        WritePlan();
        AssetDatabase.SaveAssets();
    }

    static void WritePlan()
    {
        var plan = new ArtPlan { bindings = bindings.ToArray() };
        File.WriteAllText("Assets/Resources/AshfallArt.json", JsonUtility.ToJson(plan, true));
        AssetDatabase.ImportAsset("Assets/Resources/AshfallArt.json", ImportAssetOptions.ForceSynchronousImport);
        foreach (var group in bindings.GroupBy(b => b.role.Contains('/') ? b.role.Substring(0, b.role.IndexOf('/')) : b.role).OrderBy(g => g.Key))
            Debug.Log("ASHFALL_ART_ROLES " + group.Key + "=" + group.Count() + " " + string.Join(",", group.Select(b => b.role)));
        Debug.Log("ASHFALL_ART_PLAN roles=" + bindings.Count);
    }
    static string ResourcePath(string assetPath) { const string r = "Assets/Resources/"; var p = assetPath.Substring(r.Length); return p.Substring(0, p.Length - Path.GetExtension(p).Length); }
    static void Bind(string role, string prefabPath, float scale = 1, float yaw = 0) { if (prefabPath != null) bindings.Add(new ArtBinding { role = role, prefab = ResourcePath(prefabPath), scale = scale, yaw = yaw }); }

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
    /// <summary>A saved copy of a material multiplied by a tint (zombie kinds, character skin tones).</summary>
    static Material Tinted(Material source, string name, Color tint)
    {
        if (source == null) return null;
        string path = Out + "M_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(source); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(source);
        m.color = tint; m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
    }
    static Material Plain(string name, Color color)
    {
        string path = Out + "M_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.color = color; m.SetFloat("_Glossiness", .1f); m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
    }
    static void LimitTexture(string path, int size)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && importer.maxTextureSize > size) { importer.maxTextureSize = size; importer.SaveAndReimport(); }
    }
    static Material MainPalette { get { return Palette(Z + "POLY/Textures/MainTexture.png", "PolyMain"); } }
    static Material SecondPalette { get { return Palette(Z + "POLY/Textures/Polygon_Texture.png", "PolyVol2"); } }

    // ---------------------------------------------------------------- static models
    enum Fit { Footprint, Length, Height }
    /// <summary>Imports a static mesh, applies a palette, normalizes size and pivot, and saves a prefab.</summary>
    static string StaticPrefab(string path, string id, Material material, Fit fit, float size, float maxHeight = 99, bool longestAlongZ = false)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) { Debug.LogWarning("ASHFALL_ART_MISSING " + path.Substring(Root.Length)); return null; }
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
        catch (Exception e) { Debug.LogWarning("ASHFALL_ART_FAILED " + path.Substring(Root.Length) + ": " + e.Message); return null; }
        finally { UnityEngine.Object.DestroyImmediate(container); }
    }
    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    static void Prop(string role, string path, float footprint, float maxHeight, Material m = null)
    {
        var p = StaticPrefab(path, "Prop_" + role, m ?? MainPalette, Fit.Footprint, footprint, maxHeight);
        if (p != null) Bind("prop/" + role, p);
    }
    static void Props()
    {
        Prop("Fridge", Z + "Props/Construction/SM_Fridge.fbx", .9f, 1.9f);
        Prop("Bed", Z + "Props/Construction/SM_Bed.fbx", 1.0f, .9f);
        Prop("Couch", Z + "Props/Interior/SM_Sofa_A.fbx", 1.0f, .95f);
        Prop("Dresser", Z + "Props/Construction/SM_Dresser_A.fbx", .95f, 1.1f);
        Prop("Wardrobe", Z + "Props/Construction/SM_Cupboard.fbx", .95f, 2.0f);
        Prop("Shelf", Z + "Props/Construction/SM_Food_Shelf_01.fbx", .98f, 1.8f);
        Prop("Crate", Z + "Props/Construction/SM_Box_01.fbx", .85f, .9f);
        Prop("WoodCrate", Z + "Props/Interior/SM_Wooden_Box_A.fbx", .85f, .9f);
        Prop("Desk", Z + "Props/Construction/SM_Table_01.fbx", .98f, .85f);
        Prop("Table", Z + "Props/Interior/SM_Table.fbx", .95f, .85f);
        Prop("Toolbox", Z + "Props/Construction/SM_Tool_Cabinet_01.fbx", .95f, 1.4f);
        Prop("Workbench", Z + "Props/Construction/SM_Workbench_01.fbx", .98f, 1.2f);
        Prop("Toilet", Z + "Props/Interior/SM_Toilet.fbx", .7f, .9f);
        Prop("Sink", Z + "Props/Interior/SM_Wash_Basin.fbx", .8f, 1.1f);
        Prop("Stove", Z + "Props/Interior/SM_Cooking_Table.fbx", .95f, 1.0f);
        Prop("Counter", Z + "Props/Construction/SM_Floor_Cabinet_A.fbx", .98f, 1.0f);
        Prop("MedicineCabinet", Z + "Props/Construction/SM_Hanging_Cabinet_A.fbx", .8f, 1.6f);
        Prop("Locker", Z + "Props/Interior/SM_Locker_Gray.fbx", .95f, 2.0f);
        Prop("RainCollector", Z + "Props/Workshop/SM_Barrel_A_01.fbx", .9f, 1.2f);
        Prop("Bin", Z + "Props/Construction/SM_Garbage_Pack.fbx", .8f, 1.0f);
        Prop("HayBale", Z + "Props/Construction/SM_Pile_Of_Firewood_A.fbx", .95f, .8f);
        // Open-county props (fixed size, centred on the prop footprint). AmmoBox and Barrier reuse zomboid-selection files.
        Prop("AmmoBox", Z + "Weapons/Firearms/SM_Ammo_Box_Closed.fbx", .6f, .4f, SecondPalette);
        Prop("Barrier", Z + "Vehicles/SM_Barriers_01.fbx", 1.9f, 1.1f);
        if (!decay) return;
        Prop("SupplyCrate", D + "Props/SM_Ammo_Box_Large_Closed.fbx", 1.1f, 1.0f, SecondPalette);
        Prop("MilitaryCrate", D + "Props/SM_Ammo_Box_Wood_Closed.fbx", 1.0f, .5f, SecondPalette);
        Prop("Sandbags", D + "Props/SM_Sandbags_B.fbx", 1.6f, .8f);
        Prop("Tent", D + "Props/Tent_A_01.fbx", 3.0f, 2.4f, SecondPalette);
        Prop("Watchtower", D + "Props/SM_Tower_03.fbx", 3.0f, 5.5f);
        Prop("MedicalBed", D + "Props/SM_Bed_01.fbx", 2.0f, 1.1f);
        Prop("Bunk", D + "Props/SM_Bed_02.fbx", 2.0f, 2.0f);
        Prop("RadioSet", D + "Props/Radio.fbx", .55f, .8f, SecondPalette);
        Prop("Generator", D + "Props/SM_Diesel_Generator.fbx", .95f, .7f);
        // GardenBed and OutpostFlag have no compatible owned model; the procedural ViewPropMeshes are used.
    }
    static void Item(string id, string path, float length, Material m)
    {
        var p = StaticPrefab(path, "Item_" + id, m, Fit.Length, length, 99, true);
        if (p != null) Bind("item/" + id, p);
    }
    static void Items()
    {
        var a = MainPalette; var b = SecondPalette;
        Item("baseball_bat", Z + "Weapons/Melee/SM_Kricket_Bat.fbx", .95f, a);
        Item("nailed_bat", Z + "Weapons/Melee/SM_Baseball_bat(Nails_reinforced).fbx", .95f, a);
        Item("axe", Z + "Weapons/Melee/SM_Fire_Axe.fbx", .95f, a);
        Item("kitchen_knife", Z + "Weapons/Melee/SM_Cleaver.fbx", .32f, a);
        Item("hunting_knife", Z + "Weapons/Melee/SM_Survival_Knife.fbx", .32f, a);
        Item("machete", Z + "Weapons/Melee/SM_Machete.fbx", .65f, a);
        Item("golf_club", Z + "Weapons/Melee/SM_Gofl_Club.fbx", 1.0f, a);
        Item("pipe", Z + "Weapons/Melee/SM_Pipe_01.fbx", .85f, a);
        Item("hammer", Z + "Weapons/Melee/SM_Hammer.fbx", .35f, a);
        Item("shovel", Z + "Weapons/Melee/SM_Shovel.fbx", 1.1f, a);
        Item("crowbar", Z + "Props/Tools/SM_Crowbar.fbx", .7f, b);
        Item("plank", Z + "Props/Workshop/SM_Wooden_Plank_A.fbx", 1.0f, a);
        Item("pistol", Z + "Weapons/Firearms/SM_Police_Gun.fbx", .24f, b);
        Item("revolver", Z + "Weapons/Firearms/SM_Revolver.fbx", .28f, b);
        Item("shotgun", Z + "Weapons/Firearms/Modern_Pump_Action_Shotgun.fbx", 1.0f, b);
        Item("rifle", Z + "Weapons/Firearms/SM_Hunting_Sniper_Rifle.fbx", 1.1f, b);
        Item("hunting_rifle", Z + "Weapons/Firearms/SM_Hunting_Sniper_Rifle.fbx", 1.1f, b);
        Item("flashlight", Z + "Props/Tools/SM_FLASHLIGHT.fbx", .22f, b);
        Item("molotov", Z + "Weapons/Firearms/SM_Molotov_01.fbx", .26f, b);
        Item("pipe_bomb", Z + "Weapons/Melee/SM_Pipe_01.fbx", .24f, a);
        if (!decay) return;
        Item("assault_rifle", D + "Weapons/SM_Assault_Rifle_556.fbx", .92f, b);
        Item("smg", D + "Weapons/SM_Army_Submachine_Gun.fbx", .6f, b);
        Item("sniper_rifle", D + "Weapons/SM_Army_Sniper_Rifle.fbx", 1.15f, b);
        Item("grenade", D + "Weapons/SM_Frag_Grenade.fbx", .11f, b);
        Item("smoke_grenade", D + "Weapons/SM_Smoke_Grenade.fbx", .14f, b);
        Item("flare_gun", D + "Weapons/Flare_Gun.fbx", .22f, b);
        Item("energy_drink", D + "Weapons/Energy_Drink_01.fbx", .17f, b);
    }
    static void Vehicles()
    {
        int i = 0;
        foreach (var f in new[] { "Vehicles/Car_02.fbx", "Vehicles/Car_03.fbx", "Vehicles/Truck.fbx" })
        {
            var p = StaticPrefab(Z + f, "Vehicle_" + i, MainPalette, Fit.Length, i == 2 ? 4.6f : 4.1f, 99, true);
            if (p != null) Bind("vehicle/" + i++, p);
        }
        // No owned Unity aircraft or parachute exists in the library; aircraft/plane, aircraft/helicopter and fx/parachute stay procedural.
    }
    static void Nature()
    {
        int t = 0, s = 0;
        foreach (var f in new[] { "Nature/SM_Tree_01.fbx", "Nature/SM_Tree_02.fbx", "Nature/SM_Tree_03.fbx", "Nature/SM_Tree_04.fbx" }) { var p = StaticPrefab(Z + f, "Tree_" + t, MainPalette, Fit.Height, 5.5f); if (p != null) Bind("tree/" + t++, p); }
        foreach (var f in new[] { "Nature/SM_Bush_01.fbx", "Nature/SM_Bush_02.fbx", "Nature/SM_Bush_03.fbx" }) { var p = StaticPrefab(Z + f, "Bush_" + s, MainPalette, Fit.Height, 1.0f); if (p != null) Bind("bush/" + s++, p); }
    }

    // ---------------------------------------------------------------- animated characters
    static AnimationClip FirstClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }
    static AnimationClip ImportClip(string path, ModelImporterAnimationType type, Avatar source, bool loop)
    {
        AnimationClip cached; if (clips.TryGetValue(path, out cached)) return cached;
        var importer = AssetImporter.GetAtPath(path) as ModelImporter; if (importer == null) return null;
        importer.animationType = type; importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.importAnimation = true;
        if (type == ModelImporterAnimationType.Generic && source != null) { importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = source; }
        else importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();
        var defaults = importer.defaultClipAnimations;
        foreach (var c in defaults) { c.loopTime = loop; c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true; c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true; }
        importer.clipAnimations = defaults; importer.SaveAndReimport();
        var clip = FirstClip(path); clips[path] = clip; return clip;
    }

    /// <summary>Controller with Speed, Down and Attack/Hit triggers, shared by survivors and zombies. An optional
    /// "Special" trigger plays a kind's signature move (screamer scream, bloater burst, feral pounce, juggernaut charge).</summary>
    static AnimatorController Controller(string name, AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip attack, AnimationClip hit, AnimationClip dead, float walkSpeed, float runSpeed, AnimationClip special = null)
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
        if (special != null)
        {
            c.AddParameter("Special", AnimatorControllerParameterType.Trigger);
            var s = sm.AddState("Special"); s.motion = special; var t = sm.AddAnyStateTransition(s); t.AddCondition(AnimatorConditionMode.If, 0, "Special"); t.duration = .08f; t.canTransitionToSelf = false;
            var back = s.AddTransition(move); back.hasExitTime = true; back.exitTime = .92f; back.duration = .15f;
        }
        if (dead != null)
        {
            var s = sm.AddState("Down"); s.motion = dead; var t = sm.AddAnyStateTransition(s); t.AddCondition(AnimatorConditionMode.If, 0, "Down"); t.duration = .1f; t.canTransitionToSelf = false;
            var up = s.AddTransition(move); up.AddCondition(AnimatorConditionMode.IfNot, 0, "Down"); up.duration = .3f; up.hasExitTime = false;
        }
        EditorUtility.SetDirty(c);
        return c;
    }

    /// <summary>One Cartoon Zombies rig: mesh and albedo in Zomboid or Decay, clips found in either folder by "prefix@Clip".</summary>
    sealed class ZombieRig
    {
        public string id, folder, mesh, texture, prefix;
        public string[] idle = { "Idle" }, walk = { "Walk_Z", "Walk" }, run = { "Run_Z", "Run" }, attack = { "Attack_1", "Attack" }, hit = { "Get_Hit" }, dead = { "Dead" }, special = new string[0];
        public float walkSpeed = 1.0f, runSpeed = 2.6f, height = 1.8f; public Color tint = Color.white;
    }
    static string Find(string folder, string file)
    {
        foreach (var dir in new[] { Z + "Characters/Zombies/" + folder + "/", D + "Zombies/" + folder + "/" }) if (File.Exists(dir + file)) return dir + file;
        return null;
    }
    static string ZombiePrefab(ZombieRig z)
    {
        string mesh = Find(z.folder, z.mesh), texture = Find(z.folder, z.texture);
        var importer = mesh == null ? null : AssetImporter.GetAtPath(mesh) as ModelImporter;
        if (importer == null) { Debug.LogWarning("ASHFALL_ART_MISSING zombie " + z.id); return null; }
        try
        {
            importer.animationType = ModelImporterAnimationType.Generic; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.importAnimation = false;
            importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(mesh).OfType<Avatar>().FirstOrDefault();
            if (texture != null) LimitTexture(texture, 1024);
            var material = texture == null ? null : Palette(texture, "Zombie_" + z.folder);
            if (material != null && z.tint != Color.white) material = Tinted(material, "Zombie_" + z.id, z.tint);
            if (material == null) { Debug.LogWarning("ASHFALL_ART zombie texture missing " + z.id); return null; }
            Func<string[], bool, AnimationClip> clip = (names, loop) =>
            {
                foreach (var n in names) { var p = Find(z.folder, z.prefix + "@" + n + ".FBX"); if (p != null) return ImportClip(p, ModelImporterAnimationType.Generic, avatar, loop); }
                return null;
            };
            var idle = clip(z.idle, true); var walk = clip(z.walk, true); var run = clip(z.run, true); var attack = clip(z.attack, false); var hit = clip(z.hit, false); var dead = clip(z.dead, false); var special = clip(z.special, false);
            if (idle == null || walk == null) { Debug.LogWarning("ASHFALL_ART zombie clips missing " + z.id); return null; }
            var controller = Controller("Zombie_" + z.id, idle, walk, run ?? walk, attack, hit, dead, z.walkSpeed, run != null ? z.runSpeed : z.walkSpeed * 1.8f, special);
            var container = new GameObject("Zombie_" + z.id);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(mesh), container.transform);
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, r.sharedMaterials.Length)).ToArray();
                var animator = instance.GetComponentInChildren<Animator>() ?? instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller; animator.avatar = avatar; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var b = Bounds(instance); float scale = z.height / Mathf.Max(.001f, b.size.y);
                instance.transform.localScale *= scale; b = Bounds(instance); instance.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                string prefab = Out + "Zombie_" + z.id + ".prefab"; PrefabUtility.SaveAsPrefabAsset(container, prefab);
                Debug.Log("ASHFALL_ART zombie " + z.id + " clips=" + new[] { idle, walk, run, attack, hit, dead, special }.Count(x => x != null) + (special != null ? " special=" + special.name : ""));
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(container); }
        }
        catch (Exception e) { Debug.LogWarning("ASHFALL_ART_FAILED zombie " + z.id + ": " + e.Message); return null; }
    }

    static void Zombies()
    {
        var drinker = ZombiePrefab(new ZombieRig { id = "Drinker", folder = "Drinker_Zombie", mesh = "Drinker_Mesh.FBX", texture = "Drinker_Albedo.tga", prefix = "Drinker" });
        var soldier = ZombiePrefab(new ZombieRig { id = "Soldier", folder = "Soldier_Zombie", mesh = "Soldier_Mesh.FBX", texture = "Soldier_Albedo.tga", prefix = "Soldier", runSpeed = 2.4f });
        var medic = ZombiePrefab(new ZombieRig { id = "Medic", folder = "Medic_Zombie", mesh = "Medic_Mesh.FBX", texture = "Medic_Albedo.tga", prefix = "Medic" });
        var fat = ZombiePrefab(new ZombieRig { id = "Fat", folder = "Fat_Zombie", mesh = "Fat_Zombie_Mesh.FBX", texture = "Fat_Zombie_Albedo.tga", prefix = "Fat_Zombies", special = new[] { "Attack_Boom" }, walkSpeed = .8f });
        // Generic pool (zombie/<n>) keeps the original four first so existing saves and seeds look the same.
        int n = 0; foreach (var p in new[] { drinker, soldier, medic, fat }) if (p != null) Bind("zombie/" + n++, p);
        if (!decay) return;
        var mask = ZombiePrefab(new ZombieRig { id = "Mask", folder = "Mask_Zombie", mesh = "Mask_Mesh.FBX", texture = "Mask_Albedo.tga", prefix = "Mask" });
        var screamer = ZombiePrefab(new ZombieRig { id = "Screamer", folder = "Drinker_Zombie", mesh = "Drinker_Mesh.FBX", texture = "Drinker_Albedo.tga", prefix = "Drinker", special = new[] { "Shout" }, tint = new Color(.92f, .88f, .96f), runSpeed = 3.0f });
        var feral = ZombiePrefab(new ZombieRig { id = "Feral", folder = "Monster_Zombie", mesh = "Monster_Mesh.FBX", texture = "Monster_Albedo.tga", prefix = "Monster", run = new[] { "Run", "Run_Z" }, special = new[] { "Jump" }, runSpeed = 4.2f });
        var juggernaut = ZombiePrefab(new ZombieRig { id = "Juggernaut", folder = "Tank_Zombie", mesh = "Tank_Mesh.FBX", texture = "Tank_Albedo.tga", prefix = "Tank", walk = new[] { "Move_Z" }, run = new[] { "Move" }, attack = new[] { "Attack" }, special = new[] { "Move" }, walkSpeed = 1.1f, runSpeed = 3.4f });
        if (mask != null) Bind("zombie/" + n++, mask);
        int w = 0; foreach (var p in new[] { drinker, medic, mask }) if (p != null) Bind("zombie/walker/" + w++, p);
        Bind("zombie/screamer/0", screamer);
        Bind("zombie/bloater/0", fat);
        Bind("zombie/armored/0", soldier);
        Bind("zombie/feral/0", feral);
        Bind("zombie/juggernaut/0", juggernaut);
        // Crawler: no owned crawling rig; the view lowers the generic walker prefab or uses its procedural crawler.
    }

    /// <summary>Plague heart: the Mega Creatures cocoon with its idle pulse (the view re-skins it with its flesh material).</summary>
    static void Heart()
    {
        string dir = D + "Creatures/Cocoon/", mesh = dir + "Cocoon.FBX";
        var importer = AssetImporter.GetAtPath(mesh) as ModelImporter; if (importer == null) { Debug.LogWarning("ASHFALL_ART_MISSING plague heart"); return; }
        try
        {
            importer.animationType = ModelImporterAnimationType.Generic; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.importAnimation = false;
            importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(mesh).OfType<Avatar>().FirstOrDefault();
            LimitTexture(dir + "Cocoon_Albedo.tga", 1024); LimitTexture(dir + "Cocoon_Emissive.tga", 512);
            var material = Palette(dir + "Cocoon_Albedo.tga", "PlagueHeart");
            var glow = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "Cocoon_Emissive.tga");
            if (material != null && glow != null) { material.EnableKeyword("_EMISSION"); material.SetTexture("_EmissionMap", glow); material.SetColor("_EmissionColor", new Color(1.4f, .35f, .3f)); EditorUtility.SetDirty(material); }
            var idle = File.Exists(dir + "Cocoon@Idle.FBX") ? ImportClip(dir + "Cocoon@Idle.FBX", ModelImporterAnimationType.Generic, avatar, true) : null;
            var container = new GameObject("PlagueHeart");
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(mesh), container.transform);
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, r.sharedMaterials.Length)).ToArray();
                if (idle != null)
                {
                    var animator = instance.GetComponentInChildren<Animator>() ?? instance.AddComponent<Animator>();
                    animator.runtimeAnimatorController = Controller("PlagueHeart", idle, null, null, null, null, null, 1, 2); animator.avatar = avatar; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                }
                var b = Bounds(instance); float scale = 1.7f / Mathf.Max(.001f, Mathf.Max(b.size.y, b.size.x * .8f, b.size.z * .8f));
                instance.transform.localScale *= scale; b = Bounds(instance); instance.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                string prefab = Out + "PlagueHeart.prefab"; PrefabUtility.SaveAsPrefabAsset(container, prefab);
                Bind("fx/plague_heart", prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(container); }
        }
        catch (Exception e) { Debug.LogWarning("ASHFALL_ART_FAILED plague heart: " + e.Message); }
    }

    // ---------------------------------------------------------------- humans (POLY Survival Character customizer)
    /// <summary>An outfit: visible customizer parts, fabric atlas (T_Main_A..K) and skin tone (0 light .. 2 dark).</summary>
    sealed class Look
    {
        public bool female; public string atlas = "A"; public int skin; public float height = 1.78f; public string[] parts;
        public Look(bool female, string atlas, int skin, params string[] parts) { this.female = female; this.atlas = atlas; this.skin = skin; this.parts = parts; }
    }
    static Look M(string atlas, int skin, params string[] parts) { return new Look(false, atlas, skin, new[] { "CHR_BaseMale_Head" }.Concat(parts.Select(p => "CHR_" + p)).ToArray()); }
    static Look F(string atlas, int skin, params string[] parts) { return new Look(true, atlas, skin, new[] { "CHR_BaseWemale_Head" }.Concat(parts.Select(p => "CHR_" + p)).ToArray()) { height = 1.7f }; }

    /// <summary>Faction outfits. Variants alternate male (even n) and female (odd n); see ArtLibrary.HumanVariant.
    /// Atlases: A tan camo, B tan, C green camo, D olive, E urban camo, F charcoal, G maroon, H navy, I grey, J orange, K brown.</summary>
    static readonly Dictionary<string, Look[]> Groups = new Dictionary<string, Look[]>
    {
        { "community", new[] { M("A", 0, "Male_Torso_01AOut", "Male_Pant_01A_Long", "Hair_01", "Shoe_01A"), F("B", 1, "Female_Torso_02AOut", "Female_Pant_02A_Long", "Hair_06", "Shoe_02A"),
                               M("H", 2, "Male_Torso_02AIn", "Male_Pant_03A_Long", "Hat_01A", "Beard_02", "Shoe_03A"), F("I", 0, "Female_Torso_01AIn", "Female_Pant_01A_Long", "Hair_05", "Shoe_04A") } },
        { "enclave", new[] { M("B", 1, "Male_Torso_01AIn", "Male_Pant_01A_Long", "Hair_03", "Beard_01", "Shoe_04A"), F("H", 0, "Female_Torso_02AIn", "Female_Pant_01A_Long", "Hair_08", "Shoe_05A"),
                             M("I", 2, "Male_Torso_02AOut", "Male_Pant_02A_Long", "Hat_02A", "Shoe_05A"), F("J", 1, "Female_Torso_01AOut", "Female_Pant_02A_Short", "Hair_09", "Shoe_06A") } },
        { "rival", new[] { M("E", 0, "Male_Torso_04A", "Male_Pant_03A_Long", "Hat_01A", "Shoe_02A"), F("F", 2, "Female_Torso_04A", "Female_Pant_03A_Long", "Hair_07", "Shoe_02A"),
                           M("J", 1, "Male_Torso_03A", "Male_Pant_02A_Long", "Hair_02", "Beard_03", "Shoe_01A"), F("E", 0, "Female_Torso_03A", "Female_Pant_02A_Long", "Hat_01B", "Shoe_01A") } },
        { "raider", new[] { M("G", 0, "Male_Torso_04A", "Male_Pant_02A_Long", "Hat_02A", "Beard_01", "Shoe_03A"), F("G", 1, "Female_Torso_04A", "Female_Pant_04A_Short", "Hair_07", "Shoe_03A"),
                            M("K", 2, "Male_Torso_03A", "Male_Pant_03A_Long", "Hair_04", "Beard_04", "Shoe_06A"), F("K", 0, "Female_Torso_02AOut", "Female_Pant_03A_Long", "Hat_02A", "Shoe_06A") } },
        { "soldier", new[] { M("C", 0, "Male_Torso_04A", "Male_Pant_03A_Long", "Hat_03A", "Shoe_03A"), F("D", 1, "Female_Torso_04A", "Female_Pant_03A_Long", "Hat_03A", "Shoe_03A"),
                             M("D", 2, "Male_Torso_02AIn", "Male_Pant_03A_Long", "Hat_03A", "Beard_03", "Shoe_03A"), F("C", 0, "Female_Torso_01AIn", "Female_Pant_03A_Long", "Hat_03B", "Shoe_03A") } },
        { "trader", new[] { M("K", 1, "Male_Torso_03A", "Male_Pant_02A_Long", "Hat_01A", "Beard_02", "Shoe_01A"), F("J", 0, "Female_Torso_03A", "Female_Pant_01A_Long", "Hair_010", "Shoe_05A") } },
    };
    /// <summary>Named characters (Docs/DESIGN.md): outfits chosen for each role.</summary>
    static readonly Dictionary<string, Look> Named = new Dictionary<string, Look>
    {
        { "ada_pike", F("H", 0, "Female_Torso_02AOut", "Female_Pant_01A_Long", "Hair_05", "Shoe_02A") },                      // radio operator: navy
        { "jonah_reed", M("J", 1, "Male_Torso_01AIn", "Male_Pant_02A_Long", "Hat_01A", "Beard_04", "Shoe_06A") },             // mechanic: orange, cap
        { "doc_hollis", M("I", 0, "Male_Torso_03A", "Male_Pant_01A_Long", "Hair_03", "Beard_02", "Shoe_04A") },               // field medic: grey long coat
        { "wren_calder", F("C", 1, "Female_Torso_04A", "Female_Pant_03A_Long", "Hair_07", "Shoe_03A") },                      // loner sniper: camo vest
        { "tess_monroe", F("E", 0, "Female_Torso_02AIn", "Female_Pant_02A_Short", "Hair_09", "Shoe_01A") },                   // rival scavenger: urban camo
        { "marshal_kade", M("D", 0, "Male_Torso_04A", "Male_Pant_03A_Long", "Hat_03A", "Beard_03", "Shoe_03A") },             // Guard deserter: olive, helmet
        { "big_sal", new Look(false, "G", 2, "CHR_BaseMale_Head", "CHR_Male_Torso_03A", "CHR_Male_Pant_02A_Long", "CHR_Hair_04", "CHR_Beard_01", "CHR_Shoe_06A") { height = 1.92f } }, // Red Talon boss
        { "pilgrim", M("K", 1, "Male_Torso_03A", "Male_Pant_01A_Long", "Hat_02B", "Beard_01", "Shoe_05A") },                  // wandering trader
    };
    static readonly Color[] SkinTones = { new Color(.78f, .6f, .46f), new Color(.6f, .43f, .3f), new Color(.4f, .28f, .19f) };

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
        Bind("survivor_female", Survivor("survivor_female", F("A", 0, "Female_Pant_01A_Long", "Female_Torso_01AOut", "Hair_05", "Shoe_01A"), controller));
        Bind("survivor_male", Survivor("survivor_male", M("A", 0, "Male_Pant_01A_Long", "Male_Torso_01AOut", "Hair_01", "Shoe_01A"), controller));
        foreach (var g in Groups) for (int i = 0; i < g.Value.Length; i++) Bind("human/" + g.Key + "/" + i, Survivor("Human_" + g.Key + "_" + i, g.Value[i], controller));
        foreach (var c in Named) Bind("character/" + c.Key, Survivor("Character_" + c.Key, c.Value, controller));
    }
    static string Atlas(string letter) { return letter == "A" ? Root + "Survival/T_Main_A.png" : Z + "Characters/Survivors/T_Main_" + letter + ".png"; }
    static string Survivor(string role, Look look, AnimatorController controller)
    {
        string path = Root + (look.female ? "Survival/Female_Survival_Unity_SK.fbx" : "Survival/Male_Survival_Unity_SK.fbx");
        var importer = AssetImporter.GetAtPath(path) as ModelImporter; if (importer == null) { Debug.LogWarning("ASHFALL_ART_MISSING " + path.Substring(Root.Length)); return null; }
        try
        {
            if (importer.animationType != ModelImporterAnimationType.Human || importer.importAnimation || importer.materialLocation != ModelImporterMaterialLocation.InPrefab)
            {
                importer.animationType = ModelImporterAnimationType.Human; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; importer.materialLocation = ModelImporterMaterialLocation.InPrefab; importer.importAnimation = false;
                importer.SaveAndReimport();
            }
            var palette = Palette(Atlas(look.atlas), "Survivor_" + look.atlas) ?? Palette(Atlas("A"), "Survivor_A");
            var skin = Plain("Skin_" + look.skin, SkinTones[Mathf.Clamp(look.skin, 0, SkinTones.Length - 1)]);
            var dark = Plain("Detail", new Color(.14f, .1f, .08f));
            var container = new GameObject(role);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), container.transform);
                int shown = 0;
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    bool on = look.parts.Contains(r.name); if (on) shown++;
                    r.gameObject.SetActive(on);
                    r.sharedMaterials = r.sharedMaterials.Select(m => { string n = m == null ? "" : m.name.ToLowerInvariant(); return n.Contains("skin") ? skin : n.Contains("eye") || n.Contains("hair") ? dark : palette; }).ToArray();
                }
                if (shown < look.parts.Length) Debug.LogWarning("ASHFALL_ART " + role + " shows " + shown + "/" + look.parts.Length + " outfit parts");
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman) { Debug.LogWarning("ASHFALL_ART survivor is not humanoid: " + role); return null; }
                animator.applyRootMotion = false; animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var b = Bounds(instance); float scale = look.height / Mathf.Max(.001f, b.size.y);
                instance.transform.localScale *= scale; b = Bounds(instance); instance.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                string prefab = Out + "Survivor_" + role + ".prefab"; PrefabUtility.SaveAsPrefabAsset(container, prefab);
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(container); }
        }
        catch (Exception e) { Debug.LogWarning("ASHFALL_ART_FAILED " + role + ": " + e.Message); return null; }
    }
}
