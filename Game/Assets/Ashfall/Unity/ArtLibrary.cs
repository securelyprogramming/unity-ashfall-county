using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    [Serializable] public sealed class ArtBinding { public string role, prefab; public float scale = 1, yaw; }
    [Serializable] public sealed class ArtPlan { public int schemaVersion = 1; public ArtBinding[] bindings = new ArtBinding[0]; }

    /// <summary>Owned-art lookup. Purchased prefabs (prepared by the editor step from the private cache) override
    /// procedural stand-ins by role; when a pack is not restored, the procedural model is used.</summary>
    public static class ArtLibrary
    {
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, ArtBinding> bindings = new Dictionary<string, ArtBinding>();
        static readonly Dictionary<PropKind, Mesh> propMeshes = new Dictionary<PropKind, Mesh>();
        static bool loaded;
        public static Material Vertex, Glass, Water;
        public static int PurchasedRoles { get { return prefabs.Count; } }

        public static void Load()
        {
            if (loaded) return; loaded = true;
            var shader = Resources.Load<Shader>("Ashfall/AshfallVertex") ?? Shader.Find("Ashfall/VertexLit");
            Vertex = new Material(shader) { name = "Ashfall Vertex", enableInstancing = true };
            var glass = Resources.Load<Shader>("Ashfall/AshfallGlass") ?? Shader.Find("Ashfall/Glass");
            Glass = new Material(glass) { name = "Ashfall Glass" }; Glass.SetColor("_Color", new Color(.62f, .78f, .9f, .32f));
            Water = new Material(glass) { name = "Ashfall Water" }; Water.SetColor("_Color", new Color(.16f, .32f, .38f, .82f));
            var text = Resources.Load<TextAsset>("AshfallArt");
            if (text == null) return;
            var plan = JsonUtility.FromJson<ArtPlan>(text.text);
            foreach (var b in plan.bindings ?? new ArtBinding[0])
            {
                var prefab = Resources.Load<GameObject>(b.prefab);
                if (prefab == null) continue;
                prefabs[b.role] = prefab; bindings[b.role] = b;
            }
            Debug.Log("ASHFALL_ART purchasedRoles=" + prefabs.Count + "/" + (plan.bindings == null ? 0 : plan.bindings.Length));
        }

        public static bool Has(string role) { return prefabs.ContainsKey(role); }
        static readonly Dictionary<string, int> variantCounts = new Dictionary<string, int>();
        /// <summary>Number of prepared variants named "prefix/0", "prefix/1", ...</summary>
        public static int Variants(string prefix)
        {
            int n; if (variantCounts.TryGetValue(prefix, out n)) return n;
            n = 0; while (prefabs.ContainsKey(prefix + "/" + n)) n++; variantCounts[prefix] = n; return n;
        }
        public static string Variant(string prefix, int seed) { int n = Variants(prefix); return n == 0 ? null : prefix + "/" + (Math.Abs(seed) % n); }
        /// <summary>A "human/&lt;group&gt;/&lt;n&gt;" role of the right gender: prepared group outfits alternate male (even n)
        /// and female (odd n). Falls back to any variant of the group, then null.</summary>
        public static string HumanVariant(string group, int seed, bool female)
        {
            string prefix = "human/" + group; int n = Variants(prefix); if (n == 0) return null;
            int matching = female ? n / 2 : (n + 1) / 2;
            if (matching == 0) return Variant(prefix, seed);
            return prefix + "/" + ((Math.Abs(seed) % matching) * 2 + (female ? 1 : 0));
        }
        /// <summary>True when the prepared prefab's animator has the named parameter (for example the zombie "Special"
        /// trigger: screamer scream, bloater burst, feral pounce, juggernaut charge).</summary>
        public static bool HasParameter(Animator animator, string name)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (var p in animator.parameters) if (p.name == name) return true;
            return false;
        }

        static readonly Dictionary<(Material, int), Material> tinted = new Dictionary<(Material, int), Material>();
        /// <summary>A shared copy of a purchased material multiplied by a tint (faction colours, plague flush). Cached per
        /// source and colour, so many actors with the same look share one material.</summary>
        public static Material Tinted(Material source, Color tint)
        {
            if (source == null) return null;
            Color32 c = tint; var key = (source, c.r << 24 | c.g << 16 | c.b << 8 | c.a);
            Material m; if (tinted.TryGetValue(key, out m) && m != null) return m;
            m = new Material(source) { name = source.name + " tinted" };
            if (m.HasProperty("_Color")) m.color = source.color * tint;
            tinted[key] = m; return m;
        }
        public static GameObject Spawn(string role, Transform parent)
        {
            GameObject prefab; if (!prefabs.TryGetValue(role, out prefab)) return null;
            var go = UnityEngine.Object.Instantiate(prefab, parent, false); var b = bindings[role];
            go.transform.localScale *= b.scale; go.transform.localRotation = Quaternion.Euler(0, b.yaw, 0); go.name = role;
            return go;
        }

        // ---------------------------------------------------------------- procedural stand-ins
        public static readonly Color32 Wood = new Color32(122, 86, 58, 255), DarkWood = new Color32(84, 58, 40, 255), White = new Color32(222, 220, 212, 255),
            Steel = new Color32(150, 156, 160, 255), Red = new Color32(150, 46, 40, 255), Fabric = new Color32(96, 104, 120, 255), Hay = new Color32(196, 168, 92, 255);

        public static Mesh PropMesh(PropKind kind)
        {
            Mesh mesh; if (propMeshes.TryGetValue(kind, out mesh)) return mesh;
            var k = new MeshKit(); var o = Vector3.zero;
            switch (kind)
            {
                case PropKind.Counter: k.Box(o, new Vector3(.95f, .9f, .62f), new Color32(170, 150, 120, 255)); k.Box(new Vector3(0, .9f, 0), new Vector3(1f, .06f, .66f), new Color32(200, 196, 188, 255)); break;
                case PropKind.Fridge: k.Box(o, new Vector3(.8f, 1.8f, .7f), White); k.Box(new Vector3(.3f, 1.1f, .36f), new Vector3(.05f, .5f, .04f), Steel); break;
                case PropKind.Stove: k.Box(o, new Vector3(.8f, .9f, .66f), White); k.Box(new Vector3(0, .9f, 0), new Vector3(.8f, .04f, .66f), new Color32(40, 40, 42, 255)); break;
                case PropKind.Sink: k.Box(o, new Vector3(.8f, .88f, .6f), new Color32(190, 180, 160, 255)); k.Box(new Vector3(0, .88f, 0), new Vector3(.5f, .05f, .4f), Steel); break;
                case PropKind.Toilet: k.Box(o, new Vector3(.42f, .42f, .62f), White); k.Box(new Vector3(0, 0, -.22f), new Vector3(.45f, .85f, .2f), White); break;
                case PropKind.MedicineCabinet: k.Box(new Vector3(0, 1.1f, -.32f), new Vector3(.6f, .6f, .14f), White); k.Box(o, new Vector3(.7f, .85f, .5f), new Color32(200, 196, 190, 255)); break;
                case PropKind.Bed: k.Box(o, new Vector3(.95f, .45f, .98f), DarkWood); k.Box(new Vector3(0, .45f, .05f), new Vector3(.9f, .14f, .9f), new Color32(200, 200, 210, 255)); k.Box(new Vector3(0, .45f, -.33f), new Vector3(.7f, .2f, .22f), White); k.Box(new Vector3(0, 0, -.47f), new Vector3(.98f, .9f, .06f), DarkWood); break;
                case PropKind.Couch: k.Box(o, new Vector3(.95f, .45f, .8f), Fabric); k.Box(new Vector3(0, 0, -.32f), new Vector3(.95f, .9f, .2f), MeshKit.Shade(Fabric, .85f)); k.Box(new Vector3(-.42f, 0, 0), new Vector3(.12f, .65f, .8f), Fabric); k.Box(new Vector3(.42f, 0, 0), new Vector3(.12f, .65f, .8f), Fabric); break;
                case PropKind.Dresser: k.Box(o, new Vector3(.9f, 1.0f, .5f), Wood); for (int i = 0; i < 3; i++) k.Box(new Vector3(0, .15f + i * .3f, .26f), new Vector3(.8f, .22f, .02f), MeshKit.Shade(Wood, 1.15f)); break;
                case PropKind.Wardrobe: k.Box(o, new Vector3(.95f, 2.0f, .6f), DarkWood); k.Box(new Vector3(0, .1f, .31f), new Vector3(.02f, 1.8f, .02f), new Color32(40, 30, 20, 255)); break;
                case PropKind.Shelf: k.Box(o, new Vector3(.98f, 1.7f, .55f), new Color32(160, 162, 168, 255), 0, false); for (int i = 0; i < 4; i++) k.Box(new Vector3(0, .1f + i * .45f, 0), new Vector3(.96f, .04f, .53f), new Color32(190, 190, 196, 255)); for (int i = 0; i < 6; i++) k.Box(new Vector3(-.35f + (i % 3) * .35f, .14f + (i / 3) * .45f, 0), new Vector3(.22f, .25f, .3f), new Color32((byte)(120 + i * 20), (byte)(60 + i * 25), 50, 255)); break;
                case PropKind.Crate: case PropKind.WoodCrate: k.Box(o, new Vector3(.8f, .7f, .8f), kind == PropKind.WoodCrate ? new Color32(150, 110, 70, 255) : Wood); k.Box(new Vector3(0, .32f, 0), new Vector3(.84f, .06f, .84f), DarkWood); break;
                case PropKind.Desk: k.Box(new Vector3(0, .7f, 0), new Vector3(.95f, .05f, .6f), Wood); k.Box(new Vector3(.32f, 0, 0), new Vector3(.3f, .7f, .55f), DarkWood); k.Box(new Vector3(-.42f, 0, 0), new Vector3(.06f, .7f, .55f), DarkWood); break;
                case PropKind.Table: k.Box(new Vector3(0, .72f, 0), new Vector3(.9f, .05f, .9f), Wood); foreach (var p in new[] { new Vector3(-.38f, 0, -.38f), new Vector3(.38f, 0, -.38f), new Vector3(-.38f, 0, .38f), new Vector3(.38f, 0, .38f) }) k.Box(p, new Vector3(.06f, .72f, .06f), DarkWood); break;
                case PropKind.Bin: k.Prism(o, .3f, .9f, 8, new Color32(60, 80, 64, 255)); break;
                case PropKind.Toolbox: k.Box(o, new Vector3(.9f, .8f, .5f), Red); k.Box(new Vector3(0, .8f, 0), new Vector3(.5f, .2f, .3f), Red); break;
                case PropKind.Workbench: k.Box(new Vector3(0, .8f, 0), new Vector3(.98f, .1f, .7f), Wood); k.Box(new Vector3(-.4f, 0, 0), new Vector3(.1f, .8f, .6f), DarkWood); k.Box(new Vector3(.4f, 0, 0), new Vector3(.1f, .8f, .6f), DarkWood); break;
                case PropKind.Cashier: k.Box(o, new Vector3(.95f, 1.0f, .6f), new Color32(120, 120, 130, 255)); k.Box(new Vector3(0, 1.0f, 0), new Vector3(.4f, .25f, .35f), new Color32(30, 30, 34, 255)); break;
                case PropKind.Locker: k.Box(o, new Vector3(.9f, 1.9f, .5f), new Color32(80, 96, 110, 255)); for (int i = 0; i < 3; i++) k.Box(new Vector3(-.3f + i * .3f, .2f, .26f), new Vector3(.02f, 1.5f, .02f), new Color32(40, 50, 60, 255)); break;
                case PropKind.GasPump: k.Box(o, new Vector3(.6f, 1.6f, .45f), new Color32(200, 60, 50, 255)); k.Box(new Vector3(0, 1.0f, .23f), new Vector3(.4f, .4f, .02f), new Color32(40, 40, 40, 255)); break;
                case PropKind.HayBale: k.Box(o, new Vector3(.95f, .6f, .7f), Hay); break;
                case PropKind.Mailbox: k.Box(o, new Vector3(.08f, 1.0f, .08f), DarkWood); k.Box(new Vector3(0, 1.0f, 0), new Vector3(.25f, .22f, .45f), new Color32(60, 70, 120, 255)); break;
                case PropKind.RainCollector: k.Prism(o, .42f, 1.0f, 10, new Color32(70, 90, 120, 255)); k.Prism(new Vector3(0, 1.0f, 0), .46f, .06f, 10, new Color32(30, 30, 30, 255)); break;
                case PropKind.Campfire:
                    for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; k.Box(new Vector3(Mathf.Cos(a) * .35f, 0, Mathf.Sin(a) * .35f), new Vector3(.16f, .14f, .16f), new Color32(110, 106, 100, 255)); }
                    for (int i = 0; i < 4; i++) k.Box(new Vector3(0, .05f, 0), new Vector3(.08f, .08f, .6f), DarkWood, i * 45);
                    break;
                case PropKind.FarmPlot:
                    k.Box(o, new Vector3(.96f, .05f, .96f), new Color32(96, 68, 44, 255));
                    for (int i = 0; i < 3; i++) k.Box(new Vector3(0, .05f, -.3f + i * .3f), new Vector3(.88f, .04f, .14f), new Color32(78, 54, 36, 255));
                    break;
                case PropKind.Crops: for (int i = 0; i < 4; i++) k.Pyramid(new Vector3(-.3f + i * .2f, 0, (i % 2) * .2f - .1f), .12f, .45f, 4, new Color32(70, 130, 50, 255), i * 20); break;
                default: k.Box(o, new Vector3(.8f, .8f, .8f), Wood); break;
            }
            mesh = k.Build("Prop " + kind); propMeshes[kind] = mesh; return mesh;
        }

        public static string PropRole(PropKind kind) { return "prop/" + kind; }

        static Mesh cube, blob;
        /// <summary>Unit cube and blob with white vertex colours, tinted by material colour.</summary>
        public static Mesh Cube { get { if (cube == null) { var k = new MeshKit(); k.Box(new Vector3(0, -.5f, 0), Vector3.one, new Color32(255, 255, 255, 255), 0, true, true); cube = k.Build("White cube"); } return cube; } }
        public static Mesh Blob { get { if (blob == null) { var k = new MeshKit(); k.Blob(Vector3.zero, Vector3.one * .5f, new Color32(255, 255, 255, 255), 7); blob = k.Build("White blob"); } return blob; } }
        public static GameObject Solid(string name, Transform parent, Mesh mesh, Color color, bool emissive = false)
        {
            var m = new Material(Vertex) { color = color };
            if (emissive) m.SetFloat("_Emission", 1.2f);
            var go = MeshKit.Object(name, parent, mesh, m, !emissive); return go;
        }
    }
}
