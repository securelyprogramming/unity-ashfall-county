using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashfall.View
{
    /// <summary>PUBG-style world events: the cargo plane fly-over, parachuting supply crates, the red smoke column of a
    /// landed crate, the artillery red zone on the ground, and lantern glows at the home base and outposts at night.
    /// Presentation only: reads world.airdrops, world.redZone and the community.</summary>
    public sealed class ViewWorldEvents
    {
        public const float PlaneAltitude = 38, PlaneSpeed = 52, PlaneHalfPath = 750, DropDrawRadius = 260, DescentSeconds = 22, DropHeight = 70;
        readonly World world; readonly Transform root; readonly Fx fx;

        Transform plane, planeShadow; float planeClock = -1, planeDuration; Vector3 planeFrom, planeDir;
        sealed class Drop { public Transform crate, chute; public Light light; }
        readonly Dictionary<int, Drop> drops = new Dictionary<int, Drop>();
        readonly List<int> stale = new List<int>();
        readonly HashSet<int> keep = new HashSet<int>();
        Transform zone; Material zoneMaterial; bool zoneShader;
        readonly Light[] lanterns = new Light[4];
        static Mesh planeMesh, chuteMesh, discMesh;
        static readonly int ColorId = Shader.PropertyToID("_Color"), CenterId = Shader.PropertyToID("_Center"), RadiusId = Shader.PropertyToID("_Radius"),
            EdgeId = Shader.PropertyToID("_Edge"), PulseId = Shader.PropertyToID("_Pulse"), HatchId = Shader.PropertyToID("_Hatch");

        public ViewWorldEvents(World world, Transform parent, Fx fx)
        {
            this.world = world; this.fx = fx;
            root = new GameObject("World events").transform; root.SetParent(parent, false);
            for (int i = 0; i < lanterns.Length; i++)
            {
                var l = new GameObject("Base lantern").AddComponent<Light>(); l.transform.SetParent(root, false);
                l.type = LightType.Point; l.color = new Color(1, .7f, .38f); l.range = 13; l.shadows = LightShadows.None; l.enabled = false; lanterns[i] = l;
            }
        }
        public bool PlaneFlying { get { return planeClock >= 0; } }
        public Vector3 PlanePosition { get { return plane == null ? Vector3.zero : plane.position; } }

        static Color32 C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }

        // ---------------------------------------------------------------- plane
        /// <summary>Starts a fly-over that passes above `over` along heading (radians, simulation convention).</summary>
        public void Fly(Vector3 over, float heading)
        {
            if (plane == null)
            {
                var art = ArtLibrary.Spawn("aircraft/plane", root);
                if (art == null)
                {
                    if (planeMesh == null) planeMesh = PlaneMesh();
                    art = MeshKit.Object("Cargo plane", root, planeMesh, ArtLibrary.Vertex);
                }
                plane = new GameObject("Plane").transform; plane.SetParent(root, false); art.transform.SetParent(plane, false);
                var shadow = MeshKit.Object("Plane shadow", root, Fx.Quad, Fx.Tinted(new Color(0, 0, 0, .32f), false, true), false);
                planeShadow = shadow.transform; planeShadow.localScale = new Vector3(30, 1, 24);
            }
            planeDir = new Vector3(Mathf.Cos(heading), 0, Mathf.Sin(heading)); if (planeDir.sqrMagnitude < .01f) planeDir = Vector3.right;
            planeFrom = new Vector3(over.x, PlaneAltitude, over.z) - planeDir * PlaneHalfPath;
            planeClock = 0; planeDuration = PlaneHalfPath * 2 / PlaneSpeed;
            plane.gameObject.SetActive(true); planeShadow.gameObject.SetActive(true);
            UpdatePlane(0);
        }
        void UpdatePlane(float dt)
        {
            if (planeClock < 0 || plane == null) return;
            planeClock += dt;
            if (planeClock > planeDuration) { planeClock = -1; plane.gameObject.SetActive(false); planeShadow.gameObject.SetActive(false); return; }
            var pos = planeFrom + planeDir * PlaneSpeed * planeClock;
            plane.SetPositionAndRotation(pos, Quaternion.LookRotation(planeDir) * Quaternion.Euler(0, 0, Mathf.Sin(planeClock * .4f) * 2));
            planeShadow.SetPositionAndRotation(new Vector3(pos.x, .1f, pos.z), Quaternion.LookRotation(planeDir));
        }
        static Mesh PlaneMesh()
        {
            // Low-poly four-engine cargo plane along +Z.
            var k = new MeshKit(); var body = C(86, 94, 74); var belly = C(120, 124, 116); var dark = C(40, 42, 40);
            k.Box(new Vector3(0, -1.5f, 0), new Vector3(3.4f, 3.2f, 20), body, 0, true, true);
            k.Box(new Vector3(0, -1.6f, 0), new Vector3(3.2f, .4f, 19), belly);
            k.Box(new Vector3(0, -.2f, 11.2f), new Vector3(2.4f, 1.6f, 2.4f), body);
            k.Box(new Vector3(0, .9f, 11.6f), new Vector3(2.0f, .5f, 1.2f), C(60, 74, 86));                     // cockpit glass
            k.Box(new Vector3(0, 1.1f, 1.5f), new Vector3(32, .45f, 4.6f), body);                               // wings
            k.Box(new Vector3(0, 1.0f, -9.5f), new Vector3(11, .3f, 2.8f), body);                               // tailplane
            k.Box(new Vector3(0, 1.2f, -10f), new Vector3(.35f, 4.6f, 3.4f), body);                             // fin
            foreach (float x in new[] { -11.5f, -6.5f, 6.5f, 11.5f })
            {
                k.Box(new Vector3(x, .3f, 2.4f), new Vector3(1.2f, 1.2f, 3.4f), dark);
                k.Box(new Vector3(x, .2f, 4.15f), new Vector3(3.4f, 1.4f, .12f), C(30, 30, 30));               // prop disc
            }
            k.Box(new Vector3(0, 1.12f, -3f), new Vector3(1.2f, .02f, 1.2f), C(200, 200, 190));                 // roundel
            return k.Build("Cargo plane");
        }

        // ---------------------------------------------------------------- supply drops
        static Mesh ChuteMesh()
        {
            var k = new MeshKit(); const int sides = 12; float r = 3.4f, h = 1.7f;
            for (int ring = 0; ring < 3; ring++)
            {
                float r0 = r * Mathf.Cos(ring * .45f), r1 = r * Mathf.Cos((ring + 1) * .45f), y0 = h * Mathf.Sin(ring * .45f) / Mathf.Sin(1.35f), y1 = h * Mathf.Sin((ring + 1) * .45f) / Mathf.Sin(1.35f);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides; var col = i % 2 == 0 ? C(196, 44, 36) : C(226, 222, 210);
                    Vector3 p0 = new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0), p1 = new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0);
                    Vector3 q0 = new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1), q1 = new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1);
                    k.Quad(p0, q0, q1, p1, MeshKit.Shade(col, 1 - ring * .05f)); k.Quad(p1, q1, q0, p0, MeshKit.Shade(col, .7f));
                }
            }
            // Rigging lines down to the crate (the crate hangs 4.5 m below the canopy rim).
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2 + Mathf.PI / 4; var rim = new Vector3(Mathf.Cos(a) * r * .95f, 0, Mathf.Sin(a) * r * .95f); var end = new Vector3(Mathf.Cos(a) * .4f, -4.4f, Mathf.Sin(a) * .4f);
                var mid = (rim + end) * .5f; var dir = end - rim;
                var rot = Quaternion.FromToRotation(Vector3.up, dir.normalized);
                var w = .03f; Vector3 ax = rot * new Vector3(w, 0, 0), az = rot * new Vector3(0, 0, w);
                k.Quad(rim - ax, end - ax, end + ax, rim + ax, C(40, 40, 40)); k.Quad(rim - az, end - az, end + az, rim + az, C(40, 40, 40));
            }
            return k.Build("Parachute");
        }

        Drop BuildDrop(Airdrop a)
        {
            var d = new Drop();
            var go = new GameObject("Airdrop " + a.id); go.transform.SetParent(root, false); d.crate = go.transform;
            var model = ArtLibrary.Spawn("prop/SupplyCrate", d.crate) ?? MeshKit.Object("Crate", d.crate, ViewPropMeshes.Get(PropKind.SupplyCrate), ArtLibrary.Vertex);
            model.transform.localPosition = Vector3.zero;
            var chute = ArtLibrary.Spawn("fx/parachute", root);
            if (chute == null) { if (chuteMesh == null) chuteMesh = ChuteMesh(); chute = MeshKit.Object("Parachute", root, chuteMesh, ArtLibrary.Vertex); }
            d.chute = chute.transform;
            var lg = new GameObject("Flare light"); lg.transform.SetParent(root, false);
            d.light = lg.AddComponent<Light>(); d.light.type = LightType.Point; d.light.color = new Color(1, .18f, .12f); d.light.range = 7; d.light.shadows = LightShadows.None; d.light.enabled = false;
            return d;
        }
        /// <summary>When the landed crate is a map container with a prop, WorldView draws it; this view only adds smoke and light.</summary>
        bool CrateIsProp(Airdrop a)
        {
            var map = world.map; if (a.container < 0 || a.container >= map.containers.Count) return false;
            int prop = map.containers[a.container].prop; return prop >= 0 && prop < map.props.Count && !map.props[prop].removed;
        }

        void SyncDrops(Vector3 focus, float dt)
        {
            keep.Clear();
            double minutes = world.minutes; float descent = DescentSeconds * Mathf.Max(.01f, world.TimeScale);   // in-game minutes the visible fall takes
            foreach (var a in world.airdrops)
            {
                if (a.opened) continue;
                float dx = a.pos.x - focus.x, dz = a.pos.y - focus.z; if (dx * dx + dz * dz > DropDrawRadius * DropDrawRadius) continue;
                float left = (float)(a.landsAt - minutes);
                bool falling = !a.landed && left < descent;
                if (!a.landed && !falling) continue;
                keep.Add(a.id);
                Drop d; if (!drops.TryGetValue(a.id, out d)) { d = BuildDrop(a); drops[a.id] = d; }
                var ground = new Vector3(a.pos.x, WorldView.GroundHeight(world.map.GroundAt(Mathf.FloorToInt(a.pos.x), Mathf.FloorToInt(a.pos.y))), a.pos.y);
                if (falling)
                {
                    float k = Mathf.Clamp01(left / descent); float sway = Mathf.Sin(Time.time * 1.3f + a.id) * 6 * k;
                    var at = ground + Vector3.up * (DropHeight * k + .02f) + new Vector3(Mathf.Sin(Time.time * .7f + a.id) * 1.5f * k, 0, 0);
                    d.crate.gameObject.SetActive(true); d.crate.SetPositionAndRotation(at, Quaternion.Euler(sway, a.id * 37, sway * .5f));
                    d.chute.gameObject.SetActive(true); d.chute.SetPositionAndRotation(at + Vector3.up * 5.2f, Quaternion.Euler(sway * .6f, a.id * 37, sway * .3f));
                    d.light.enabled = false;
                    if (Random.value < dt * 6) fx.Trail(at + Vector3.up * .8f, new Color32(255, 80, 60, 255));
                }
                else
                {
                    d.chute.gameObject.SetActive(false);
                    d.crate.gameObject.SetActive(!CrateIsProp(a));
                    d.crate.SetPositionAndRotation(ground, Quaternion.Euler(0, a.id * 37, 0));
                    // Tall red smoke column: readable from far off on the isometric view.
                    fx.Column(ground + new Vector3(.6f, .4f, .6f), dt, 14, new Color32(214, 46, 38, 170));
                    d.light.enabled = true; d.light.transform.position = ground + new Vector3(.6f, .9f, .6f);
                    d.light.intensity = 1.4f + Mathf.PerlinNoise(Time.time * 9, a.id) * 1.2f;
                    if (Random.value < dt * 20) fx.Trail(ground + new Vector3(.6f, .45f, .6f), new Color32(255, 120, 90, 255));
                }
            }
            if (drops.Count == keep.Count) return;
            stale.Clear(); foreach (var id in drops.Keys) if (!keep.Contains(id)) stale.Add(id);
            foreach (var id in stale) { var d = drops[id]; Object.Destroy(d.crate.gameObject); Object.Destroy(d.chute.gameObject); Object.Destroy(d.light.gameObject); drops.Remove(id); }
        }

        // ---------------------------------------------------------------- red zone
        void SyncZone(Vector3 focus)
        {
            var z = world.redZone;
            bool warn = z != null && z.Warning(world.minutes), live = z != null && z.Active(world.minutes);
            bool show = (warn || live) && z.radius > 0;
            if (show)
            {
                float dx = z.center.x - focus.x, dz = z.center.y - focus.z; show = Mathf.Sqrt(dx * dx + dz * dz) - z.radius < 160;
            }
            if (!show) { if (zone != null && zone.gameObject.activeSelf) zone.gameObject.SetActive(false); return; }
            if (zone == null) BuildZone();
            if (!zone.gameObject.activeSelf) zone.gameObject.SetActive(true);
            float pulse = warn ? .5f + .5f * Mathf.Sin(Time.time * 4.2f) : .75f + .1f * Mathf.Sin(Time.time * 9);
            if (zoneShader)
            {
                zone.position = new Vector3(focus.x, .09f, focus.z); zone.localScale = new Vector3(320, 1, 320);
                zoneMaterial.SetVector(CenterId, new Vector4(z.center.x, z.center.y, 0, 0));
                zoneMaterial.SetFloat(RadiusId, z.radius); zoneMaterial.SetFloat(EdgeId, Mathf.Clamp(z.radius * .015f, 1.2f, 3));
                zoneMaterial.SetFloat(PulseId, pulse); zoneMaterial.SetFloat(HatchId, live ? 1 : 0);
                zoneMaterial.SetColor(ColorId, live ? new Color(1, .1f, .05f, .2f) : new Color(1, .2f, .08f, .13f));
            }
            else
            {
                zone.position = new Vector3(z.center.x, .09f, z.center.y); zone.localScale = new Vector3(z.radius, 1, z.radius);
                zoneMaterial.color = new Color(1, .15f, .08f, (live ? .2f : .12f) * (.6f + .4f * pulse));
            }
        }
        void BuildZone()
        {
            var shader = Resources.Load<Shader>("Ashfall/AshfallZone");
            zoneShader = shader != null && shader.isSupported;
            if (zoneShader)
            {
                zoneMaterial = new Material(shader) { name = "Red zone" };
                zone = MeshKit.Object("Red zone", root, Fx.Quad, zoneMaterial, false).transform;
            }
            else
            {
                // Fallback: a flat disc with a brighter rim, scaled to the radius.
                if (discMesh == null)
                {
                    var k = new MeshKit(); const int n = 64;
                    for (int i = 0; i < n; i++)
                    {
                        float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n; Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                        k.Tri(Vector3.zero, d0 * .985f, d1 * .985f, new Color32(255, 255, 255, 140)); k.Quad(d0 * .985f, d0, d1, d1 * .985f, new Color32(255, 255, 255, 255));
                    }
                    discMesh = k.Build("Red zone disc");
                }
                zoneMaterial = Fx.Tinted(new Color(1, .15f, .08f, .2f), false);
                zone = MeshKit.Object("Red zone", root, discMesh, zoneMaterial, false).transform;
            }
            zone.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        // ---------------------------------------------------------------- base lanterns
        void SyncLanterns(Vector3 focus)
        {
            int used = 0; float dark = Mathf.Clamp01((.55f - world.Daylight) / .4f);
            if (dark > 0)
            {
                var c = world.community;
                if (c != null && c.baseBuilding >= 0) Lantern(ref used, c.basePos, focus, dark, 1.2f);
                if (c != null) foreach (var o in c.outposts) Lantern(ref used, o.pos, focus, dark, .85f);
            }
            for (int i = used; i < lanterns.Length; i++) if (lanterns[i].enabled) lanterns[i].enabled = false;
        }
        void Lantern(ref int used, Vec2 at, Vector3 focus, float dark, float strength)
        {
            if (used >= lanterns.Length) return;
            float dx = at.x - focus.x, dz = at.y - focus.z; if (dx * dx + dz * dz > 90 * 90) return;
            var l = lanterns[used++]; l.enabled = true; l.transform.position = new Vector3(at.x, 3.3f, at.y);
            l.intensity = dark * strength * (1.5f + Mathf.PerlinNoise(Time.time * 2.5f, at.x) * .35f);
        }

        public void Update(Vector3 focus, float dt)
        {
            UpdatePlane(dt);
            SyncDrops(focus, dt);
            SyncZone(focus);
            SyncLanterns(focus);
        }

        /// <summary>Airdrop event: "plane" starts the fly-over toward the drop nearest the event; "land" kicks up dust.</summary>
        public void OnAirdrop(SimEvent e)
        {
            var at = new Vector3(e.pos.x, 0, e.pos.y);
            if (e.text == "plane")
            {
                Airdrop best = null; float bestD = float.MaxValue;
                foreach (var a in world.airdrops) { if (a.landed) continue; float d = Vec2.Distance(a.pos, e.pos); if (d < bestD) { bestD = d; best = a; } }
                Fly(best != null ? new Vector3(best.pos.x, 0, best.pos.y) : at, best != null ? best.planeHeading : 0);
            }
            else if (e.text == "land") { fx.Dust(at, 3, 18); }
        }

        public void Clear() { if (root != null) Object.Destroy(root.gameObject); drops.Clear(); }
    }
}
