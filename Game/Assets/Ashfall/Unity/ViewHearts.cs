using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashfall.View
{
    /// <summary>Plague hearts near the focus: a pulsating organic mass with tendrils spread over the floor and a red
    /// glow. The beat quickens as the heart is hurt, kicks on "pulse"/"scream" events, flashes when hit, and the
    /// destroyed heart collapses into a dark husk. Presentation only (reads world.hearts).</summary>
    public sealed class ViewHearts
    {
        public const float DrawRadius = 60;
        readonly World world; readonly Transform root; readonly Fx fx;
        sealed class HeartView
        {
            public PlagueHeart h; public GameObject root; public Transform core; public Material flesh; public Light light; public MeshRenderer tendrils;
            public float kick, flash, collapse, phase; public Vector3 coreScale; public bool husk;
        }
        readonly Dictionary<int, HeartView> views = new Dictionary<int, HeartView>();
        readonly List<int> stale = new List<int>();
        static Mesh coreMesh, tendrilMesh; static Material tendrilMat, huskMat;
        static readonly int EmissionId = Shader.PropertyToID("_Emission");
        static readonly Color Live = new Color(.62f, .1f, .14f), Husk = new Color(.17f, .11f, .09f);

        public ViewHearts(World world, Transform parent, Fx fx)
        {
            this.world = world; this.fx = fx;
            root = new GameObject("Plague hearts").transform; root.SetParent(parent, false);
        }
        public int Drawn { get { return views.Count; } }

        static Color32 C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }
        static void BuildMeshes()
        {
            if (coreMesh != null) return;
            var k = new MeshKit();
            k.Blob(new Vector3(0, .75f, 0), new Vector3(.95f, .8f, .9f), C(200, 60, 70), 11);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.26f + .3f; var at = new Vector3(Mathf.Cos(a) * .7f, .35f + MeshKit.Hash(i * 5) * .5f, Mathf.Sin(a) * .7f);
                k.Blob(at, Vector3.one * (.32f + MeshKit.Hash(i * 3 + 1) * .2f), C(170, 40, 60), 20 + i);
            }
            for (int i = 0; i < 7; i++)
            {
                float a = i * .9f; var at = new Vector3(Mathf.Cos(a) * .55f, .9f + MeshKit.Hash(i) * .5f, Mathf.Sin(a) * .5f);
                k.Blob(at, Vector3.one * (.1f + MeshKit.Hash(i + 9) * .08f), C(240, 200, 120), 40 + i);   // pustules
            }
            coreMesh = k.Build("Heart core");
            var t = new MeshKit();
            for (int i = 0; i < 11; i++)
            {
                float a = i * Mathf.PI * 2 / 11 + MeshKit.Hash(i * 7) * .4f, len = 2.2f + MeshKit.Hash(i * 13) * 2.6f, bend = (MeshKit.Hash(i * 17) - .5f) * 1.2f;
                var p = Vector3.zero; float thick = .32f;
                for (int sgm = 0; sgm < 6; sgm++)
                {
                    float ang = a + bend * sgm / 6f; var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                    float seg = len / 6; var mid = p + dir * seg * .5f;
                    t.Box(mid + Vector3.up * .0f, new Vector3(thick, thick * .45f, seg * 1.08f), MeshKit.Shade(C(120, 26, 40), .85f + sgm * .04f), Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                    p += dir * seg; thick *= .8f;
                }
                t.Blob(p + Vector3.up * .05f, Vector3.one * .14f, C(170, 50, 60), 60 + i);
            }
            tendrilMesh = t.Build("Heart tendrils");
            tendrilMat = new Material(ArtLibrary.Vertex) { color = new Color(.8f, .45f, .5f) }; tendrilMat.SetFloat(EmissionId, .18f);
            huskMat = new Material(ArtLibrary.Vertex) { color = new Color(.28f, .2f, .18f) };
        }

        HeartView Build(PlagueHeart h)
        {
            BuildMeshes();
            var v = new HeartView { h = h, root = new GameObject("Plague heart " + h.id), phase = MeshKit.Hash(h.id) * 6 };
            v.root.transform.SetParent(root, false);
            v.root.transform.localPosition = new Vector3(h.pos.x, .02f, h.pos.y);
            v.root.transform.localRotation = Quaternion.Euler(0, MeshKit.Hash(h.id * 31) * 360, 0);
            var t = MeshKit.Object("Tendrils", v.root.transform, tendrilMesh, tendrilMat, false); v.tendrils = t.GetComponent<MeshRenderer>();
            v.flesh = new Material(ArtLibrary.Vertex) { color = Live };
            var art = ArtLibrary.Spawn("fx/plague_heart", v.root.transform);
            if (art != null)
            {
                v.core = art.transform;
                foreach (var r in art.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) r.sharedMaterial = v.flesh;   // so pulse, flash and husk tint apply
            }
            else v.core = MeshKit.Object("Core", v.root.transform, coreMesh, v.flesh).transform;
            v.coreScale = v.core.localScale;
            var lg = new GameObject("Heart glow"); lg.transform.SetParent(v.root.transform, false); lg.transform.localPosition = new Vector3(0, 1.6f, 0);
            v.light = lg.AddComponent<Light>(); v.light.type = LightType.Point; v.light.color = new Color(1, .16f, .1f); v.light.range = 10; v.light.shadows = LightShadows.None;
            if (h.destroyed) { v.collapse = 1; v.husk = true; }
            return v;
        }

        public void Sync(Vector3 focus, float dt)
        {
            foreach (var h in world.hearts)
            {
                float dx = h.pos.x - focus.x, dz = h.pos.y - focus.z; bool near = dx * dx + dz * dz < DrawRadius * DrawRadius;
                HeartView v; bool has = views.TryGetValue(h.id, out v);
                if (!near) { if (has) { Destroy(v); views.Remove(h.id); } continue; }
                if (!has) { v = Build(h); views[h.id] = v; }
                Animate(v, dt);
            }
            if (views.Count > world.hearts.Count)
            {
                stale.Clear(); foreach (var kv in views) if (!Exists(kv.Key)) stale.Add(kv.Key);
                foreach (var id in stale) { Destroy(views[id]); views.Remove(id); }
            }
        }
        bool Exists(int id) { foreach (var h in world.hearts) if (h.id == id) return true; return false; }
        static void Destroy(HeartView v) { if (v.flesh != null) Object.Destroy(v.flesh); if (v.root != null) Object.Destroy(v.root); }

        void Animate(HeartView v, float dt)
        {
            var h = v.h;
            if (h.destroyed && !v.husk) { v.husk = true; }
            v.kick = Mathf.Max(0, v.kick - dt * 2.2f); v.flash = Mathf.Max(0, v.flash - dt * 5);
            if (v.husk)
            {
                v.collapse = Mathf.MoveTowards(v.collapse, 1, dt * .6f);
                float c = v.collapse;
                v.core.localScale = Vector3.Scale(v.coreScale, new Vector3(1 + .18f * c, 1 - .62f * c, 1 + .18f * c));
                v.flesh.color = Color.Lerp(Live, Husk, c); v.flesh.SetFloat(EmissionId, (1 - c) * .4f + v.flash * .5f);
                v.light.intensity = Mathf.Lerp(3, 0, c * 1.6f); if (v.light.intensity <= .01f && v.light.enabled) v.light.enabled = false;
                if (c > .3f && v.tendrils.sharedMaterial != huskMat) v.tendrils.sharedMaterial = huskMat;
                return;
            }
            // The beat quickens as the heart is hurt; the core swells on each beat and on event kicks.
            float hurt = h.maxHp > 0 ? 1 - Mathf.Clamp01(h.hp / h.maxHp) : 0;
            v.phase += dt * Mathf.PI * 2 * (.9f + hurt * 1.6f + Mathf.Clamp01(h.pulse) * .5f);
            float beat = Mathf.Pow(Mathf.Max(0, Mathf.Sin(v.phase)), 6);   // sharp lub, slow relax
            float swell = 1 + beat * .07f + v.kick * .14f + v.flash * .05f;
            v.core.localScale = Vector3.Scale(v.coreScale, new Vector3(swell, 1 + (swell - 1) * 1.3f, swell));
            v.flesh.color = Color.Lerp(Live, new Color(1, .75f, .7f), v.flash * .6f);
            v.flesh.SetFloat(EmissionId, .18f + beat * .35f + v.kick * .4f + v.flash * .9f);
            v.light.intensity = 1.6f + beat * 1.6f + v.kick * 2.4f + v.flash * 2; v.light.range = 9 + v.kick * 4;
        }

        /// <summary>Heart / HeartDestroyed events: hit flash, pulse and scream kicks, destruction burst.</summary>
        public void OnEvent(SimEvent e)
        {
            HeartView v = null; float best = 3 * 3;
            foreach (var kv in views)
            {
                var p = kv.Value.h.pos; float d = (p.x - e.pos.x) * (p.x - e.pos.x) + (p.y - e.pos.y) * (p.y - e.pos.y);
                if (d < best) { best = d; v = kv.Value; }
            }
            var at = new Vector3(e.pos.x, .9f, e.pos.y);
            if (e.kind == EventKind.HeartDestroyed)
            {
                fx.Ichor(at, 40); fx.Flash(at + Vector3.up, new Color(1, .2f, .1f), 16, 6, .8f); fx.Shockwave(at, 9, new Color(1, .2f, .15f, .6f), .6f);
                fx.Scorch(at, 4.5f, new Color(.12f, .02f, .03f, .7f), 240);
                if (v != null) { v.husk = true; v.flash = 1; }
                return;
            }
            if (v == null) return;
            switch (e.text)
            {
                case "hit": v.flash = 1; v.kick = Mathf.Max(v.kick, .5f); fx.Ichor(at, 10); break;
                case "pulse": v.kick = 1; break;
                case "scream": v.kick = 1.6f; fx.Shockwave(at, 12, new Color(1, .15f, .1f, .55f), .7f); break;
            }
        }

        public void Clear() { foreach (var v in views.Values) Destroy(v); views.Clear(); if (root != null) Object.Destroy(root.gameObject); }
    }
}
