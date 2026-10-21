using Ashfall.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashfall.View
{
    /// <summary>Pooled presentation effects: a handful of world-space particle systems fed with Emit, a small pool of
    /// flash lights, tracer lines, ground decals, shockwave rings and thrown-object markers. Nothing here touches the
    /// simulation; every effect is capped so a firefight or an artillery barrage cannot run away with the frame.</summary>
    public sealed class Fx
    {
        public const int MaxLights = 6, MaxTracers = 16, MaxDecals = 40, MaxRings = 6, MaxThrown = 8, MaxHazardLights = 3;
        readonly Transform root;
        readonly ParticleSystem smoke, fire, debris, blood, gas, dust, column, spark;
        ParticleSystem.EmitParams ep;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        sealed class Glow { public Light light; public float life, duration, peak; }
        sealed class Line { public LineRenderer line; public float life, duration; }
        sealed class Mark { public Renderer renderer; public Transform t; public float life, duration, size, grow; public Color color; }
        readonly Glow[] glows = new Glow[MaxLights];
        readonly Line[] tracers = new Line[MaxTracers];
        readonly Mark[] decals = new Mark[MaxDecals], rings = new Mark[MaxRings];
        readonly Transform[] thrown = new Transform[MaxThrown];
        readonly Light[] hazardLights = new Light[MaxHazardLights];
        int nextDecal, nextRing, nextTracer, thrownUsed;

        // ---------------------------------------------------------------- shared materials
        static Material alphaSoft, alphaFlat, addSoft, addFlat; static Texture2D soft; static Mesh quad, ring, grenade;
        static Shader Load(string path)
        {
            var s = Resources.Load<Shader>(path);
            if (s == null || !s.isSupported) s = Shader.Find("Sprites/Default");
            return s;
        }
        static void Init()
        {
            if (alphaSoft != null) return;
            soft = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Fx soft", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1 - r); a = a * a * (.75f + .25f * Mathf.PerlinNoise(x * .13f, y * .13f) * 1.3f);
                    px[x + y * 64] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * 1.6f) * 255));
                }
            soft.SetPixels32(px); soft.Apply(false, true);
            var alpha = Load("Ashfall/AshfallFxAlpha"); var add = Resources.Load<Shader>("Ashfall/AshfallFxAdditive");
            if (add == null || !add.isSupported) add = alpha;
            alphaSoft = new Material(alpha) { name = "Fx alpha soft", mainTexture = soft };
            alphaFlat = new Material(alpha) { name = "Fx alpha flat", mainTexture = Texture2D.whiteTexture };
            addSoft = new Material(add) { name = "Fx additive soft", mainTexture = soft };
            addFlat = new Material(add) { name = "Fx additive flat", mainTexture = Texture2D.whiteTexture };
        }
        public static Material AlphaSoft { get { Init(); return alphaSoft; } }
        public static Material AdditiveSoft { get { Init(); return addSoft; } }
        /// <summary>A new tinted unlit material (callers cache it): flat for UV-less MeshKit meshes, soft for quads with UVs.</summary>
        public static Material Tinted(Color c, bool additive, bool softTexture = false)
        {
            Init(); var m = new Material(softTexture ? (additive ? addSoft : alphaSoft) : (additive ? addFlat : alphaFlat)); m.color = c; return m;
        }
        /// <summary>Flat quad on XZ (1 x 1, centred) with UVs, for decals and soft glows.</summary>
        public static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Fx quad" };
                quad.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
                quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                quad.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
                quad.triangles = new[] { 0, 1, 2, 0, 2, 3 }; quad.RecalculateNormals(); quad.RecalculateBounds();
                return quad;
            }
        }
        /// <summary>Flat annulus of radius 1 (inner .8) on XZ.</summary>
        public static Mesh Ring
        {
            get
            {
                if (ring != null) return ring;
                var k = new MeshKit(); var w = new Color32(255, 255, 255, 255); const int n = 48;
                for (int i = 0; i < n; i++)
                {
                    float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    k.Quad(d0 * .8f, d0, d1, d1 * .8f, w);
                }
                ring = k.Build("Fx ring"); return ring;
            }
        }

        // ---------------------------------------------------------------- construction
        public Fx(Transform parent)
        {
            Init();
            root = new GameObject("Effects").transform; root.SetParent(parent, false);
            smoke = System("Smoke", alphaSoft, 700, 0, 2.6f, true, true);
            fire = System("Fire", addSoft, 500, -.05f, .35f, false, false);
            debris = System("Debris", alphaFlat, 240, 1.1f, 1, false, false);
            blood = System("Blood", alphaSoft, 360, 1.3f, .7f, false, false);
            gas = System("Gas", alphaSoft, 600, 0, 1.8f, true, true);
            dust = System("Dust", alphaSoft, 360, -.02f, 2.2f, true, true);
            column = System("Smoke column", alphaSoft, 700, 0, 3.4f, true, true);
            spark = System("Sparks", addSoft, 300, .6f, .4f, false, false);
            for (int i = 0; i < MaxLights; i++)
            {
                var l = new GameObject("Flash light").AddComponent<Light>(); l.transform.SetParent(root, false);
                l.type = LightType.Point; l.shadows = LightShadows.None; l.enabled = false; glows[i] = new Glow { light = l };
            }
            for (int i = 0; i < MaxHazardLights; i++)
            {
                var l = new GameObject("Fire light").AddComponent<Light>(); l.transform.SetParent(root, false);
                l.type = LightType.Point; l.shadows = LightShadows.None; l.color = new Color(1, .55f, .22f); l.enabled = false; hazardLights[i] = l;
            }
            var tracerMat = Tinted(new Color(1, .86f, .55f, 1), true);
            for (int i = 0; i < MaxTracers; i++)
            {
                var lr = new GameObject("Tracer").AddComponent<LineRenderer>(); lr.transform.SetParent(root, false);
                lr.useWorldSpace = true; lr.positionCount = 2; lr.sharedMaterial = tracerMat; lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
                lr.startWidth = .08f; lr.endWidth = .03f; lr.numCapVertices = 0; lr.enabled = false; tracers[i] = new Line { line = lr };
            }
            for (int i = 0; i < MaxDecals; i++) decals[i] = MarkObject("Decal", Quad, alphaSoft);
            for (int i = 0; i < MaxRings; i++) rings[i] = MarkObject("Ring", Ring, addFlat);
            if (grenade == null) { var k = new MeshKit(); k.Blob(Vector3.zero, new Vector3(.09f, .11f, .09f), new Color32(70, 80, 56, 255), 3); k.Box(new Vector3(0, .08f, 0), new Vector3(.05f, .06f, .05f), new Color32(150, 150, 150, 255)); grenade = k.Build("Fx thrown"); }
            for (int i = 0; i < MaxThrown; i++) { var go = MeshKit.Object("Thrown", root, grenade, ArtLibrary.Vertex, false); go.SetActive(false); thrown[i] = go.transform; }
        }

        ParticleSystem System(string name, Material m, int max, float gravity, float sizeEnd, bool fadeIn, bool sorted)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity; main.startSpeed = 0; main.startLifetime = 1; main.startSize = 1; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission; emission.rateOverTime = 0;
            var shape = ps.shape; shape.enabled = false;
            float hi = Mathf.Max(1, sizeEnd);
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(hi, AnimationCurve.EaseInOut(0, 1 / hi, 1, sizeEnd / hi));
            var color = ps.colorOverLifetime; color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                fadeIn ? new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(.6f, .6f), new GradientAlphaKey(0, 1) }
                       : new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.8f, .5f), new GradientAlphaKey(0, 1) });
            color.color = new ParticleSystem.MinMaxGradient(g);
            if (sorted) { var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-.5f, .5f); }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortMode = sorted ? ParticleSystemSortMode.Distance : ParticleSystemSortMode.None; r.maxParticleSize = 3;
            ps.Play();
            return ps;
        }
        Mark MarkObject(string name, Mesh mesh, Material m)
        {
            var go = MeshKit.Object(name, root, mesh, m, false); go.GetComponent<MeshRenderer>().receiveShadows = false; go.SetActive(false);
            return new Mark { renderer = go.GetComponent<MeshRenderer>(), t = go.transform };
        }

        void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color32 color)
        {
            ep.position = pos; ep.velocity = vel; ep.startSize = size; ep.startLifetime = life; ep.startColor = color; ep.rotation = Random.Range(0f, 360f);
            ps.Emit(ep, 1);
        }
        static Color32 C(int r, int g, int b, int a) { return new Color32((byte)r, (byte)g, (byte)b, (byte)a); }
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        // ---------------------------------------------------------------- one-shot effects
        /// <summary>Brief point light; reuses the dimmest pooled light when all are busy.</summary>
        public void Flash(Vector3 at, Color color, float range, float intensity, float duration)
        {
            Glow best = glows[0];
            foreach (var g in glows) if (g.life <= 0) { best = g; break; } else if (g.life * g.peak < best.life * best.peak) best = g;
            best.light.transform.position = at; best.light.color = color; best.light.range = range; best.light.intensity = intensity;
            best.peak = intensity; best.life = best.duration = Mathf.Max(.02f, duration); best.light.enabled = true;
        }

        public void Explosion(Vector3 at, float scale)
        {
            Flash(at + Vector3.up * 1.6f, new Color(1, .62f, .3f), 18 * scale, 7, .5f);
            Emit(fire, at + Vector3.up * 1.0f, Vector3.zero, 6.5f * scale, .14f, C(255, 240, 200, 255));
            for (int i = 0; i < 18; i++)
                Emit(fire, at + Random.insideUnitSphere * .9f * scale + Vector3.up * .9f, Random.insideUnitSphere * 4.5f * scale + Vector3.up * 2, Random.Range(1.6f, 3.0f) * scale, Random.Range(.3f, .6f), C(255, Random.Range(140, 210), Random.Range(40, 80), 255));
            for (int i = 0; i < 22; i++)
                Emit(smoke, at + Random.insideUnitSphere * 1.5f * scale + Vector3.up, Flat(Random.insideUnitSphere) * 2.5f + Vector3.up * Random.Range(1.2f, 3.5f), Random.Range(1.8f, 3.4f) * scale, Random.Range(2.6f, 4.8f), C(44, 40, 38, 210));
            for (int i = 0; i < 20; i++)
            {
                var d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) + .3f;
                Emit(debris, at + Vector3.up * .4f, d * Random.Range(6f, 12f) * Mathf.Sqrt(scale), Random.Range(.1f, .24f), Random.Range(1f, 1.7f), C(52, 46, 40, 255));
            }
            for (int i = 0; i < 16; i++) Emit(spark, at + Vector3.up * .6f, Random.onUnitSphere * Random.Range(7f, 14f) + Vector3.up * 3, Random.Range(.12f, .22f), Random.Range(.35f, .8f), C(255, 196, 96, 255));
            Dust(at, 4 * scale, 14);
            Scorch(at, 3.2f * scale, new Color(.05f, .04f, .035f, .78f), 90);
            Shockwave(at, 8 * scale, new Color(1, .78f, .5f, .55f), .35f);
        }

        /// <summary>Bloater burst / gas canister: a green cloud bloom and ring.</summary>
        public void GasBurst(Vector3 at, float radius)
        {
            for (int i = 0; i < 34; i++)
            {
                var p = at + Flat(Random.insideUnitSphere) * radius * .6f + Vector3.up * Random.Range(.3f, 1.4f);
                Emit(gas, p, Flat(Random.insideUnitSphere) * 2.4f + Vector3.up * .4f, Random.Range(1.6f, 2.8f), Random.Range(2.2f, 3.6f), C(118, 176, 52, 120));
            }
            for (int i = 0; i < 12; i++) Emit(blood, at + Vector3.up, Random.onUnitSphere * 4 + Vector3.up * 2, Random.Range(.18f, .32f), Random.Range(.5f, .9f), C(96, 110, 30, 230));
            Flash(at + Vector3.up, new Color(.55f, 1, .3f), 10, 2.2f, .4f);
            Shockwave(at, radius * 1.4f, new Color(.5f, 1, .25f, .45f), .45f);
            Scorch(at, radius * .9f, new Color(.25f, .32f, .08f, .5f), 60);
        }

        /// <summary>Molotov splash: a burst of flame (the hazard then keeps burning).</summary>
        public void FireBurst(Vector3 at, float radius)
        {
            for (int i = 0; i < 24; i++) Emit(fire, at + Flat(Random.insideUnitSphere) * radius * .7f + Vector3.up * .3f, Vector3.up * Random.Range(1.5f, 3.5f) + Flat(Random.insideUnitSphere) * 1.5f, Random.Range(.8f, 1.6f), Random.Range(.4f, .8f), C(255, Random.Range(120, 190), 40, 255));
            Flash(at + Vector3.up, new Color(1, .55f, .2f), 12, 4, .5f);
            Scorch(at, radius * 1.1f, new Color(.06f, .05f, .04f, .7f), 90);
        }

        public void Muzzle(Vector3 at, Vector3 dir, float size)
        {
            dir.y = 0; if (dir.sqrMagnitude < .001f) dir = Vector3.forward; dir.Normalize();
            Emit(spark, at + dir * .15f, dir * .5f, .9f * size, .05f, C(255, 230, 160, 255));
            Emit(spark, at + dir * .45f * size, dir * 1.5f, .5f * size, .06f, C(255, 190, 90, 255));
            Emit(smoke, at + dir * .3f, dir * .8f + Vector3.up * .5f, .35f * size, .8f, C(150, 146, 140, 110));
            Flash(at, new Color(1, .82f, .5f), 7 * size, 3.2f, .07f);
        }

        public void Tracer(Vector3 from, Vector3 to)
        {
            var t = tracers[nextTracer]; nextTracer = (nextTracer + 1) % MaxTracers;
            t.line.SetPosition(0, from); t.line.SetPosition(1, to); t.line.enabled = true; t.life = t.duration = .09f;
            t.line.startColor = new Color(1, .9f, .6f, .9f); t.line.endColor = new Color(1, .7f, .35f, .6f);
            for (int i = 0; i < 3; i++) Emit(spark, to, Random.onUnitSphere * 3 + (from - to).normalized * 2, Random.Range(.08f, .15f), Random.Range(.15f, .3f), C(255, 210, 140, 255));
            Emit(dust, to, Vector3.up * .4f, .45f, .6f, C(140, 128, 110, 120));
        }

        public void Blood(Vector3 at, Vector3 dir, int count)
        {
            dir.y = 0; if (dir.sqrMagnitude > .001f) dir.Normalize();
            for (int i = 0; i < count; i++) Emit(blood, at, dir * Random.Range(.8f, 2.4f) + Random.insideUnitSphere * 1.6f + Vector3.up * Random.Range(.6f, 2f), Random.Range(.1f, .26f), Random.Range(.45f, .8f), C(Random.Range(110, 160), 10, 8, 235));
            Emit(blood, at, dir * .5f, .55f, .25f, C(150, 14, 12, 140));
        }
        /// <summary>Ichor from a plague heart: darker and more purple than blood.</summary>
        public void Ichor(Vector3 at, int count)
        {
            for (int i = 0; i < count; i++) Emit(blood, at + Random.insideUnitSphere * .5f, Random.onUnitSphere * 3 + Vector3.up * 2.5f, Random.Range(.15f, .32f), Random.Range(.5f, .9f), C(Random.Range(70, 110), 8, Random.Range(20, 40), 240));
        }

        public void Dust(Vector3 at, float radius, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var d = Flat(Random.onUnitSphere).normalized; if (d.sqrMagnitude < .01f) d = Vector3.right;
                Emit(dust, at + d * radius * .3f + Vector3.up * .2f, d * radius * Random.Range(1.2f, 2f) + Vector3.up * .5f, Random.Range(.9f, 1.6f), Random.Range(.8f, 1.4f), C(126, 112, 92, 140));
            }
        }
        /// <summary>A single ground puff (footfalls, landings).</summary>
        public void Puff(Vector3 at, float size) { Emit(dust, at + Vector3.up * .1f, Flat(Random.insideUnitSphere) + Vector3.up * .3f, size, Random.Range(.7f, 1.1f), C(120, 108, 90, 120)); }

        public void Scorch(Vector3 at, float size, Color color, float seconds)
        {
            var d = decals[nextDecal]; nextDecal = (nextDecal + 1) % MaxDecals;
            d.t.position = new Vector3(at.x, .06f + nextDecal * .0005f, at.z); d.t.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            d.t.localScale = new Vector3(size, 1, size); d.color = color; d.life = d.duration = seconds; d.grow = 0; d.size = size;
            Tint(d.renderer, color); d.t.gameObject.SetActive(true);
        }
        public void Shockwave(Vector3 at, float radius, Color color, float seconds)
        {
            var r = rings[nextRing]; nextRing = (nextRing + 1) % MaxRings;
            r.t.position = new Vector3(at.x, .25f, at.z); r.t.rotation = Quaternion.identity; r.t.localScale = Vector3.one * .3f;
            r.color = color; r.life = r.duration = seconds; r.size = radius; r.grow = 1; Tint(r.renderer, color); r.t.gameObject.SetActive(true);
        }
        void Tint(Renderer r, Color c) { block.SetColor("_Color", c); r.SetPropertyBlock(block); }

        // ---------------------------------------------------------------- continuous effects (called every frame)
        /// <summary>Tall rising smoke (airdrop flares); rate is particles per second.</summary>
        public void Column(Vector3 at, float dt, float rate, Color32 color)
        {
            float n = rate * dt; int count = (int)n + (Random.value < n - (int)n ? 1 : 0);
            for (int i = 0; i < count; i++)
                Emit(column, at + Flat(Random.insideUnitSphere) * .25f + Vector3.up * .5f, new Vector3(.35f, Random.Range(2.6f, 3.6f), .2f) + Flat(Random.insideUnitSphere) * .3f, Random.Range(.9f, 1.4f), Random.Range(8f, 11f), color);
        }
        /// <summary>Trail/fuse sparks behind a moving thing.</summary>
        public void Trail(Vector3 at, Color32 color) { Emit(spark, at, Random.insideUnitSphere * .4f, Random.Range(.08f, .14f), Random.Range(.15f, .3f), color); }
        public void Smoke(Vector3 at, float size, float life, Color32 color) { Emit(smoke, at, Vector3.up * .8f + Flat(Random.insideUnitSphere) * .4f, size, life, color); }

        /// <summary>Gas clouds and burning fuel near the focus. Rates scale with area; fire hazards get the nearest flicker lights.</summary>
        public void Hazards(World w, Vector3 focus, float dt)
        {
            int lights = 0;
            foreach (var h in w.hazards)
            {
                if (h.until <= w.minutes) continue;
                float dx = h.pos.x - focus.x, dz = h.pos.y - focus.z; if (dx * dx + dz * dz > 60 * 60) continue;
                var c = new Vector3(h.pos.x, 0, h.pos.y); float r = Mathf.Clamp(h.radius, .5f, 12), area = r * r;
                float fade = Mathf.Clamp01((float)(h.until - w.minutes) / 3f);   // last in-game minutes thin out
                if (h.fire)
                {
                    float n = Mathf.Min(60, area * 3.2f) * dt * (.4f + .6f * fade); int count = (int)n + (Random.value < n - (int)n ? 1 : 0);
                    for (int i = 0; i < count; i++)
                    {
                        var p = c + Flat(Random.insideUnitSphere) * r * .85f;
                        Emit(fire, p + Vector3.up * .1f, Vector3.up * Random.Range(1.2f, 2.4f) + Flat(Random.insideUnitSphere) * .3f, Random.Range(.5f, 1.1f), Random.Range(.45f, .85f), C(255, Random.Range(110, 190), 30, 255));
                        if (Random.value < .2f) Emit(smoke, p + Vector3.up * 1.2f, Vector3.up * Random.Range(1f, 2f), Random.Range(.8f, 1.4f), Random.Range(2f, 3f), C(40, 36, 34, 150));
                    }
                    if (lights < MaxHazardLights)
                    {
                        var l = hazardLights[lights++]; l.enabled = true; l.transform.position = c + Vector3.up * 1.2f;
                        l.range = r * 3 + 5; l.intensity = (1.8f + Mathf.PerlinNoise(Time.time * 6, h.pos.x) * 1.2f) * (.3f + .7f * fade);
                    }
                }
                else
                {
                    float n = Mathf.Min(30, area * 1.0f) * dt * (.3f + .7f * fade); int count = (int)n + (Random.value < n - (int)n ? 1 : 0);
                    for (int i = 0; i < count; i++)
                        Emit(gas, c + Flat(Random.insideUnitSphere) * r * .8f + Vector3.up * Random.Range(.2f, 1.1f), Flat(Random.insideUnitSphere) * .35f + Vector3.up * .12f, Random.Range(1.4f, 2.6f), Random.Range(2.4f, 3.6f), C(112, 170, 48, 95));
                }
            }
            for (int i = lights; i < MaxHazardLights; i++) if (hazardLights[i].enabled) hazardLights[i].enabled = false;
        }

        /// <summary>Thrown objects in flight: call BeginThrown, Thrown for each, then EndThrown.</summary>
        public void BeginThrown() { thrownUsed = 0; }
        public void Thrown(Vector3 at, float spin)
        {
            if (thrownUsed >= MaxThrown) return;
            var t = thrown[thrownUsed++]; if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t.position = at; t.rotation = Quaternion.Euler(spin * 720, spin * 300, 0);
            Trail(at, C(255, 200, 120, 255));
        }
        public void EndThrown() { for (int i = thrownUsed; i < MaxThrown; i++) if (thrown[i].gameObject.activeSelf) thrown[i].gameObject.SetActive(false); }

        // ---------------------------------------------------------------- per-frame upkeep
        public void Update(float dt)
        {
            foreach (var g in glows)
            {
                if (g.life <= 0) continue;
                g.life -= dt; float k = Mathf.Clamp01(g.life / g.duration);
                g.light.intensity = g.peak * k * k; if (g.life <= 0) g.light.enabled = false;
            }
            foreach (var t in tracers)
            {
                if (t.life <= 0) continue;
                t.life -= dt; if (t.life <= 0) { t.line.enabled = false; continue; }
                float k = t.life / t.duration; t.line.startColor = new Color(1, .9f, .6f, .9f * k); t.line.endColor = new Color(1, .7f, .35f, .6f * k);
            }
            foreach (var d in decals)
            {
                if (d.life <= 0) continue;
                d.life -= dt; if (d.life <= 0) { d.t.gameObject.SetActive(false); continue; }
                if (d.life < 8) { var c = d.color; c.a *= d.life / 8; Tint(d.renderer, c); }
            }
            foreach (var r in rings)
            {
                if (r.life <= 0) continue;
                r.life -= dt; if (r.life <= 0) { r.t.gameObject.SetActive(false); continue; }
                float k = 1 - r.life / r.duration; float e = 1 - (1 - k) * (1 - k);
                r.t.localScale = Vector3.one * Mathf.Max(.3f, r.size * e); var c = r.color; c.a *= 1 - k; Tint(r.renderer, c);
            }
        }

        public void Clear() { if (root != null) Object.Destroy(root.gameObject); }
    }
}
