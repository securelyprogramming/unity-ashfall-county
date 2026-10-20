using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashfall.View
{
    /// <summary>Streams the county around the camera focus in 32-tile chunks. Presentation only: reads the
    /// authoritative world and never changes it.</summary>
    public sealed class WorldView : MonoBehaviour
    {
        public const int ChunkSize = 32;
        public const float WallHeight = 2.6f, CutHeight = .75f, WallThickness = .14f;
        public int LoadRadius = 2;
        World world; Map map;
        readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        readonly Dictionary<int, BuildingView> buildings = new Dictionary<int, BuildingView>();
        List<int>[] chunkProps; List<int>[] chunkBuildings; List<int>[] buildingOpenings;
        int chunksX, chunksY;
        readonly HashSet<int> cut = new HashSet<int>();
        public int LoadedChunks { get { return chunks.Count; } }
        public int LoadedBuildings { get { return buildings.Count; } }
        Transform root;
        readonly Dictionary<int, GameObject> pileViews = new Dictionary<int, GameObject>();
        readonly List<Light> fireLights = new List<Light>();
        int propVersion;
        public VehicleView Vehicles { get; private set; }

        sealed class Chunk { public int cx, cy; public GameObject root; public Mesh ground, water, nature, fences; public GameObject natureObject; public int natureRemoved; public readonly List<GameObject> props = new List<GameObject>(); public readonly Dictionary<int, GameObject> propObjects = new Dictionary<int, GameObject>(); }
        sealed class OpeningView { public Opening o; public Transform door; public GameObject[] planks; public int state = -1; public Vector3 outward, center, along; }
        sealed class BuildingView { public Building b; public GameObject root, full, low, roof, glass; public Mesh glassMesh; public int refs, glassState = -1; public readonly List<OpeningView> openings = new List<OpeningView>(); public readonly List<Light> lights = new List<Light>(); public bool isCut, lit; }

        public void Initialize(World world)
        {
            this.world = world; map = world.map; ArtLibrary.Load();
            root = new GameObject("County").transform; root.SetParent(transform, false);
            chunksX = (map.width + ChunkSize - 1) / ChunkSize; chunksY = (map.height + ChunkSize - 1) / ChunkSize;
            chunkProps = new List<int>[chunksX * chunksY]; chunkBuildings = new List<int>[chunksX * chunksY];
            for (int i = 0; i < chunkProps.Length; i++) { chunkProps[i] = new List<int>(); chunkBuildings[i] = new List<int>(); }
            foreach (var p in map.props) if (map.InBounds(p.x, p.y)) chunkProps[p.x / ChunkSize + p.y / ChunkSize * chunksX].Add(p.id);
            foreach (var b in map.buildings)
                for (int cx = b.rect.x / ChunkSize; cx <= (b.rect.MaxX) / ChunkSize; cx++)
                    for (int cy = b.rect.y / ChunkSize; cy <= (b.rect.MaxY) / ChunkSize; cy++)
                        if (cx < chunksX && cy < chunksY) chunkBuildings[cx + cy * chunksX].Add(b.id);
            buildingOpenings = new List<int>[map.buildings.Count];
            for (int i = 0; i < buildingOpenings.Length; i++) buildingOpenings[i] = new List<int>();
            foreach (var o in map.openings) if (o.building >= 0) buildingOpenings[o.building].Add(o.id);
            propVersion = map.props.Count;
            Vehicles = new VehicleView(world, root);
        }

        public void Clear()
        {
            foreach (var c in chunks.Values) { Destroy(c.root); DestroyMesh(c.ground); DestroyMesh(c.water); DestroyMesh(c.nature); DestroyMesh(c.fences); }
            chunks.Clear(); foreach (var b in buildings.Values) Destroy(b.root); buildings.Clear();
            foreach (var p in pileViews.Values) Destroy(p); pileViews.Clear();
            if (Vehicles != null) Vehicles.Clear();
            if (root != null) Destroy(root.gameObject);
        }
        static void DestroyMesh(Mesh m) { if (m != null) Destroy(m); }

        /// <summary>Streams chunks around the focus and updates dynamic state. budget = chunks built per call.</summary>
        public void Refresh(Vector3 focus, Vector3 cameraBack, int budget = 2)
        {
            if (world == null) return;
            int fx = Mathf.FloorToInt(focus.x / ChunkSize), fy = Mathf.FloorToInt(focus.z / ChunkSize);
            // Load nearest first.
            var wanted = new List<KeyValuePair<int, int>>();
            for (int dx = -LoadRadius; dx <= LoadRadius; dx++)
                for (int dy = -LoadRadius; dy <= LoadRadius; dy++)
                {
                    int cx = fx + dx, cy = fy + dy; if (cx < 0 || cy < 0 || cx >= chunksX || cy >= chunksY) continue;
                    int key = cx + cy * chunksX; if (!chunks.ContainsKey(key)) wanted.Add(new KeyValuePair<int, int>(dx * dx + dy * dy, key));
                }
            foreach (var w in wanted.OrderBy(w => w.Key)) { if (budget-- <= 0) break; LoadChunk(w.Value % chunksX, w.Value / chunksX); }
            foreach (var key in chunks.Keys.ToList())
            {
                var c = chunks[key];
                if (Mathf.Abs(c.cx - fx) > LoadRadius + 1 || Mathf.Abs(c.cy - fy) > LoadRadius + 1) UnloadChunk(key);
            }
            if (map.props.Count != propVersion) { AddNewProps(); propVersion = map.props.Count; }
            foreach (var c in chunks.Values) SyncProps(c);
            UpdateCutaway(focus, cameraBack);
            foreach (var b in buildings.Values) { foreach (var o in b.openings) SyncOpening(o, b.root.transform); SyncGlass(b); }
            SyncPiles(focus);
            Vehicles.Sync(focus, Time.deltaTime);
        }

        // ---------------------------------------------------------------- chunks
        static Color32 C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }
        static readonly Color32 Grass = C(92, 118, 62), Dirt = C(120, 100, 70), Road = C(58, 60, 64), Sidewalk = C(150, 148, 140), WaterBed = C(40, 58, 60), WoodFloor = C(132, 98, 66),
            TileFloor = C(186, 184, 172), Concrete = C(128, 128, 124), Field = C(110, 86, 52), Sand = C(176, 160, 118), Parking = C(76, 78, 80), Bridge = C(96, 92, 86), Gravel = C(132, 124, 110);

        Color32 GroundColor(Ground g, int x, int y)
        {
            float h = MeshKit.Hash(x * 7349 + y * 1931);
            switch (g)
            {
                case Ground.Grass:
                    {
                        var z = map.ZoneAt(x, y);
                        var baseColor = z == ZoneKind.Forest ? C(70, 98, 52) : z == ZoneKind.Farm ? C(110, 124, 66) : Grass;
                        return MeshKit.Shade(baseColor, .9f + h * .18f);
                    }
                case Ground.Dirt: return MeshKit.Shade(Dirt, .9f + h * .2f);
                case Ground.Road:
                    {
                        // Dashed centre lines where a road is wide in one axis.
                        bool lineX = map.GroundAt(x, y - 3) == Ground.Road && map.GroundAt(x, y + 2) == Ground.Road && map.GroundAt(x, y - 4) != Ground.Road && (x / 2) % 2 == 0;
                        return lineX ? C(196, 176, 80) : MeshKit.Shade(Road, .94f + h * .1f);
                    }
                case Ground.Sidewalk: return MeshKit.Shade(Sidewalk, .95f + h * .08f);
                case Ground.Water: return WaterBed;
                case Ground.WoodFloor: return MeshKit.Shade(WoodFloor, .92f + ((x + y) % 2) * .06f);
                case Ground.TileFloor: return (x + y) % 2 == 0 ? TileFloor : MeshKit.Shade(TileFloor, .9f);
                case Ground.Concrete: return MeshKit.Shade(Concrete, .95f + h * .06f);
                case Ground.Field: return (y % 3 == 0) ? MeshKit.Shade(Field, 1.1f) : MeshKit.Shade(Field, .92f + h * .08f);
                case Ground.Sand: return MeshKit.Shade(Sand, .94f + h * .1f);
                case Ground.Parking: return (x % 4 == 0) ? C(170, 170, 160) : MeshKit.Shade(Parking, .95f + h * .06f);
                case Ground.Bridge: return MeshKit.Shade(Bridge, .95f + h * .05f);
                case Ground.Gravel: return MeshKit.Shade(Gravel, .9f + h * .15f);
            }
            return Grass;
        }
        public static float GroundHeight(Ground g) { return g == Ground.Water ? -.45f : g == Ground.Sidewalk ? .03f : g == Ground.Bridge ? .25f : g == Ground.WoodFloor || g == Ground.TileFloor ? .05f : 0; }

        void LoadChunk(int cx, int cy)
        {
            var c = new Chunk { cx = cx, cy = cy, root = new GameObject("Chunk " + cx + "," + cy) };
            c.root.transform.SetParent(root, false);
            int x0 = cx * ChunkSize, y0 = cy * ChunkSize, x1 = Mathf.Min(map.width, x0 + ChunkSize), y1 = Mathf.Min(map.height, y0 + ChunkSize);
            var k = new MeshKit(); var water = new MeshKit();
            for (int x = x0; x < x1; x++)
                for (int y = y0; y < y1; y++)
                {
                    var g = map.ground[map.Index(x, y)]; float h = GroundHeight(g); var col = GroundColor(g, x, y);
                    k.Quad(new Vector3(x, h, y), new Vector3(x, h, y + 1), new Vector3(x + 1, h, y + 1), new Vector3(x + 1, h, y), col);
                    // Kerbs and banks: side faces where a neighbour is lower.
                    float hr = GroundHeight(map.GroundAt(x + 1, y)), hu = GroundHeight(map.GroundAt(x, y + 1)), hl = GroundHeight(map.GroundAt(x - 1, y)), hd = GroundHeight(map.GroundAt(x, y - 1));
                    var side = MeshKit.Shade(col, .7f);
                    if (hr < h) k.Quad(new Vector3(x + 1, h, y), new Vector3(x + 1, h, y + 1), new Vector3(x + 1, hr, y + 1), new Vector3(x + 1, hr, y), side);
                    if (hl < h) k.Quad(new Vector3(x, h, y + 1), new Vector3(x, h, y), new Vector3(x, hl, y), new Vector3(x, hl, y + 1), side);
                    if (hu < h) k.Quad(new Vector3(x + 1, h, y + 1), new Vector3(x, h, y + 1), new Vector3(x, hu, y + 1), new Vector3(x + 1, hu, y + 1), side);
                    if (hd < h) k.Quad(new Vector3(x, h, y), new Vector3(x + 1, h, y), new Vector3(x + 1, hd, y), new Vector3(x, hd, y), side);
                    if (g == Ground.Water || g == Ground.Bridge && map.IsWater(x, y))
                        water.Quad(new Vector3(x, -.12f, y), new Vector3(x, -.12f, y + 1), new Vector3(x + 1, -.12f, y + 1), new Vector3(x + 1, -.12f, y), new Color32(255, 255, 255, 255));
                }
            c.ground = k.Build("Ground " + cx + "," + cy);
            var groundObject = MeshKit.Object("Ground", c.root.transform, c.ground, ArtLibrary.Vertex, false);
            if (water.Vertices > 0) { c.water = water.Build("Water"); MeshKit.Object("Water", c.root.transform, c.water, ArtLibrary.Water, false); }
            // Bridge railings and field fences that are not part of buildings.
            var fence = new MeshKit();
            for (int x = x0; x < x1; x++) for (int y = y0; y < y1; y++)
                {
                    if (map.H(x, y) == Edge.Fence) FenceSegment(fence, new Vector3(x, 0, y), new Vector3(x + 1, 0, y), map.GroundAt(x, y) == Ground.Bridge || map.GroundAt(x, y - 1) == Ground.Bridge);
                    if (map.V(x, y) == Edge.Fence) FenceSegment(fence, new Vector3(x, 0, y), new Vector3(x, 0, y + 1), false);
                }
            if (fence.Vertices > 0) { c.fences = fence.Build("Fences"); MeshKit.Object("Fences", c.root.transform, c.fences, ArtLibrary.Vertex); }
            BuildNature(c);
            foreach (int id in chunkProps[cx + cy * chunksX]) SpawnProp(c, map.props[id]);
            chunks[cx + cy * chunksX] = c;
            StaticBatchingUtility.Combine(c.root.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null && f.sharedMesh.isReadable && f.GetComponentInParent<Light>() == null && f.name != "Flame").Select(f => f.gameObject).ToArray(), c.root);
            foreach (int b in chunkBuildings[cx + cy * chunksX]) AcquireBuilding(b);
        }

        void UnloadChunk(int key)
        {
            var c = chunks[key]; chunks.Remove(key);
            foreach (int b in chunkBuildings[key]) ReleaseBuilding(b);
            Destroy(c.root); DestroyMesh(c.ground); DestroyMesh(c.water); DestroyMesh(c.nature); DestroyMesh(c.fences);
        }

        void FenceSegment(MeshKit k, Vector3 a, Vector3 b, bool rail)
        {
            var col = rail ? C(150, 150, 150) : C(150, 120, 86); var mid = (a + b) * .5f; float yaw = Mathf.Abs(a.x - b.x) > .1f ? 0 : 90;
            k.Box(a, new Vector3(.1f, rail ? 1.0f : 1.1f, .1f), MeshKit.Shade(col, .8f));
            k.Box(mid + Vector3.up * .45f, new Vector3(1.0f, .08f, .05f), col, yaw);
            k.Box(mid + Vector3.up * .85f, new Vector3(1.0f, .08f, .05f), col, yaw);
            if (rail) k.Box(mid + Vector3.up * .25f, new Vector3(1.0f, .5f, .04f), MeshKit.Shade(col, .9f), yaw);
        }

        void BuildNature(Chunk c)
        {
            var k = new MeshKit();
            foreach (int id in chunkProps[c.cx + c.cy * chunksX])
            {
                var p = map.props[id]; if (p.removed) continue;
                if (PurchasedNature(p)) continue;
                var pos = new Vector3(p.x + .5f, 0, p.y + .5f); int seed = p.x * 92821 + p.y * 68917;
                if (p.kind == PropKind.Tree)
                {
                    float h = MeshKit.Hash(seed); bool pine = map.ZoneAt(p.x, p.y) == ZoneKind.Forest ? h < .6f : h < .3f;
                    pos += new Vector3((MeshKit.Hash(seed + 1) - .5f) * .4f, 0, (MeshKit.Hash(seed + 2) - .5f) * .4f);
                    float scale = .8f + MeshKit.Hash(seed + 3) * .6f;
                    k.Box(pos, new Vector3(.22f, 1.2f * scale, .22f), C(88, 64, 44));
                    if (pine)
                    {
                        var green = MeshKit.Shade(C(48, 84, 52), .85f + h * .3f);
                        k.Pyramid(pos + Vector3.up * .9f * scale, 1.25f * scale, 1.9f * scale, 7, green, h * 90);
                        k.Pyramid(pos + Vector3.up * 1.9f * scale, .95f * scale, 1.7f * scale, 7, MeshKit.Shade(green, 1.08f), h * 140);
                        k.Pyramid(pos + Vector3.up * 2.8f * scale, .6f * scale, 1.4f * scale, 6, MeshKit.Shade(green, 1.15f), h * 30);
                    }
                    else
                    {
                        var green = MeshKit.Shade(C(78, 112, 52), .85f + h * .3f);
                        k.Blob(pos + Vector3.up * 2.2f * scale, new Vector3(1.3f, 1.1f, 1.3f) * scale, green, seed);
                        k.Blob(pos + new Vector3(.5f, 2.7f, .3f) * scale, new Vector3(.8f, .7f, .8f) * scale, MeshKit.Shade(green, 1.1f), seed + 5);
                    }
                }
                else if (p.kind == PropKind.Bush) k.Blob(pos + Vector3.up * .35f, new Vector3(.55f, .4f, .55f), MeshKit.Shade(C(66, 100, 48), .9f + MeshKit.Hash(seed) * .2f), seed);
            }
            if (c.natureObject != null) Destroy(c.natureObject);
            DestroyMesh(c.nature); c.nature = null; c.natureObject = null;
            if (k.Vertices > 0) { c.nature = k.Build("Nature"); c.natureObject = MeshKit.Object("Nature", c.root.transform, c.nature, ArtLibrary.Vertex); }
        }

        static bool PurchasedNature(Prop p) { return false; }   // the owned tree meshes need a palette we have not restored; procedural trees read better
        void SpawnProp(Chunk c, Prop p)
        {
            if (p.removed) return;
            if (p.kind == PropKind.Car && world.VehicleOfProp(p.id) != null) return;   // drivable cars are drawn by VehicleView
            if (p.kind == PropKind.Tree || p.kind == PropKind.Bush)
            {
                if (!PurchasedNature(p)) return;
                int seed = p.x * 92821 + p.y * 68917;
                var tree = ArtLibrary.Spawn(ArtLibrary.Variant(p.kind == PropKind.Tree ? "tree" : "bush", seed), c.root.transform);
                tree.transform.localPosition = new Vector3(p.x + .5f + (MeshKit.Hash(seed + 1) - .5f) * .4f, 0, p.y + .5f + (MeshKit.Hash(seed + 2) - .5f) * .4f);
                tree.transform.localRotation = Quaternion.Euler(0, MeshKit.Hash(seed) * 360, 0); tree.transform.localScale *= .75f + MeshKit.Hash(seed + 3) * .55f;
                c.props.Add(tree); c.propObjects[p.id] = tree; return;
            }
            GameObject go;
            var pos = new Vector3(p.x + p.w * .5f, GroundHeight(map.GroundAt(p.x, p.y)), p.y + p.h * .5f);
            float yaw = p.kind == PropKind.Car ? (p.rotation % 2 == 1 ? 90 : 0) + (p.rotation >= 2 ? 180 : 0) : new[] { 0f, 270f, 180f, 90f }[p.rotation & 3];
            string role = ArtLibrary.PropRole(p.kind);
            if (p.kind == PropKind.Car) { var v = ArtLibrary.Variant("vehicle", (int)(p.hp * 997)); go = (v == null ? null : ArtLibrary.Spawn(v, c.root.transform)) ?? CarModel(c.root.transform, p); }
            else go = ArtLibrary.Spawn(role, c.root.transform) ?? MeshKit.Object(p.kind.ToString(), c.root.transform, ArtLibrary.PropMesh(p.kind), ArtLibrary.Vertex);
            go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(0, yaw, 0) * go.transform.localRotation;
            if (p.kind == PropKind.Campfire)
            {
                var flame = ArtLibrary.Solid("Flame", go.transform, ArtLibrary.Blob, new Color(1, .55f, .15f), true);
                flame.transform.localPosition = new Vector3(0, .25f, 0); flame.transform.localScale = new Vector3(.35f, .5f, .35f);
                var light = new GameObject("Fire light").AddComponent<Light>(); light.transform.SetParent(go.transform, false); light.transform.localPosition = new Vector3(0, 1, 0);
                light.type = LightType.Point; light.range = 9; light.color = new Color(1, .62f, .3f); light.intensity = 1.6f; light.shadows = LightShadows.None;
            }
            c.props.Add(go); c.propObjects[p.id] = go;
        }

        public static GameObject CarModel(Transform parent, Prop p)
        {
            var k = new MeshKit(); float h = p.hp; bool wreck = h < .2f;
            var palette = new[] { C(150, 40, 36), C(40, 70, 130), C(200, 200, 196), C(40, 42, 46), C(90, 110, 80), C(170, 140, 60) };
            var body = palette[(int)(h * 997) % palette.Length]; if (wreck) body = MeshKit.Shade(body, .55f);
            // Model along +Z (length 4), width 2.
            k.Box(new Vector3(0, .3f, 0), new Vector3(1.8f, .6f, 3.9f), body);
            k.Box(new Vector3(0, .9f, -.2f), new Vector3(1.6f, .55f, 2.0f), MeshKit.Shade(body, .9f));
            k.Box(new Vector3(0, .95f, -.2f), new Vector3(1.62f, .4f, 1.6f), C(60, 74, 86));
            foreach (var w in new[] { new Vector3(-.85f, 0, 1.25f), new Vector3(.85f, 0, 1.25f), new Vector3(-.85f, 0, -1.25f), new Vector3(.85f, 0, -1.25f) }) k.Box(w, new Vector3(.25f, .6f, .62f), C(26, 26, 28));
            var go = MeshKit.Object("Car", parent, k.Build("Car"), ArtLibrary.Vertex);
            return go;
        }

        void SyncProps(Chunk c)
        {
            int removedNature = 0;
            foreach (int id in chunkProps[c.cx + c.cy * chunksX])
            {
                var p = map.props[id];
                if (!p.removed) continue;
                if ((p.kind == PropKind.Tree || p.kind == PropKind.Bush) && !PurchasedNature(p)) { removedNature++; continue; }
                GameObject go; if (c.propObjects.TryGetValue(id, out go)) { Destroy(go); c.propObjects.Remove(id); c.props.Remove(go); }
            }
            if (removedNature != c.natureRemoved) { c.natureRemoved = removedNature; BuildNature(c); }
            // Campfire flame and light follow the lit state.
            foreach (var kv in c.propObjects)
            {
                var p = map.props[kv.Key];
                if (p.kind == PropKind.FarmPlot) { SyncPlot(kv.Value, p); continue; }
                if (p.kind != PropKind.Campfire) continue;
                var flame = kv.Value.transform.Find("Flame"); var light = kv.Value.GetComponentInChildren<Light>(true);
                if (flame != null) { flame.gameObject.SetActive(p.lit); flame.localScale = new Vector3(.35f, .45f + Mathf.PerlinNoise(Time.time * 3, p.id) * .2f, .35f); }
                if (light != null) { light.enabled = p.lit; light.intensity = 1.4f + Mathf.PerlinNoise(Time.time * 5, p.id) * .5f; }
            }
        }
        /// <summary>Plants on a farm plot follow its crop, growth stage and moisture; rebuilt only when the stage changes.</summary>
        void SyncPlot(GameObject go, Prop p)
        {
            FarmPlot plot; if (!world.plots.TryGetValue(p.id, out plot)) return;
            int stage = plot.Empty ? -2 : plot.Dead ? -1 : plot.Ripe ? 5 : Mathf.Clamp((int)(plot.growth * 5), 0, 4);
            string key = "Plants " + plot.crop + " " + stage + (plot.water > .35f ? " wet" : "");
            var old = go.transform.Find("Plants");
            var tag = old == null || old.childCount == 0 ? null : old.GetChild(0);
            if (tag != null && tag.name == key) return;
            if (old != null) Destroy(old.gameObject);
            Mesh mesh; if (!plotMeshes.TryGetValue(key, out mesh)) plotMeshes[key] = mesh = PlotMesh(plot, stage);
            var plants = MeshKit.Object("Plants", go.transform, mesh, ArtLibrary.Vertex);
            new GameObject(key).transform.SetParent(plants.transform, false);   // stage marker
        }
        // One shared mesh per crop, stage and moisture (a few dozen at most), so chunk reloads never leak meshes.
        static readonly Dictionary<string, Mesh> plotMeshes = new Dictionary<string, Mesh>();
        static Mesh PlotMesh(FarmPlot plot, int stage)
        {
            var k = new MeshKit();
            if (plot.water > .35f) k.Box(new Vector3(0, .005f, 0), new Vector3(.9f, .062f, .9f), C(58, 40, 28));   // darker wet soil
            if (stage != -2)
            {
                Color32 leaf = stage == -1 ? C(110, 84, 50) : C(70, 132, 52), fruit = C(220, 120, 40);
                switch (plot.crop) { case "tomato": fruit = C(200, 40, 34); break; case "potato": fruit = C(176, 140, 92); break; case "cabbage": fruit = C(150, 196, 110); break; }
                float g = stage == -1 ? .35f : stage == 5 ? 1 : .15f + stage * .18f;
                for (int i = 0; i < 4; i++)
                {
                    var at = new Vector3(i % 2 == 0 ? -.22f : .22f, .06f, i < 2 ? -.22f : .22f);
                    if (plot.crop == "cabbage" && stage >= 2) k.Blob(at + Vector3.up * .12f * g, new Vector3(.17f, .13f, .17f) * g, stage == -1 ? leaf : fruit, 11 + i);
                    else k.Pyramid(at, .12f * g + .03f, .5f * g, 5, leaf, i * 37);
                    if (stage == 5 && plot.crop != "cabbage") k.Blob(at + new Vector3(.06f, plot.crop == "tomato" ? .3f : .05f, .04f), Vector3.one * .07f, fruit, 23 + i);
                }
            }
            return k.Vertices > 0 ? k.Build("Plot plants") : null;
        }

        void AddNewProps()
        {
            for (int i = propVersion; i < map.props.Count; i++)
            {
                var p = map.props[i]; int key = p.x / ChunkSize + p.y / ChunkSize * chunksX;
                chunkProps[key].Add(p.id);
                Chunk c; if (chunks.TryGetValue(key, out c)) SpawnProp(c, p);
            }
        }

        // ---------------------------------------------------------------- buildings
        void AcquireBuilding(int id)
        {
            BuildingView v; if (buildings.TryGetValue(id, out v)) { v.refs++; return; }
            v = BuildBuilding(map.buildings[id]); v.refs = 1; buildings[id] = v;
            StaticBatchingUtility.Combine(new[] { v.full, v.low, v.roof }, v.root);
        }
        void ReleaseBuilding(int id)
        {
            BuildingView v; if (!buildings.TryGetValue(id, out v)) return;
            if (--v.refs > 0) return;
            buildings.Remove(id);
            foreach (var mf in v.root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("B ")) Destroy(mf.sharedMesh);
            Destroy(v.root);
        }

        static readonly Color32[] Siding = { C(176, 196, 206), C(222, 210, 176), C(160, 180, 150), C(190, 186, 180), C(206, 186, 160), C(150, 160, 178), C(214, 214, 206), C(176, 150, 130) };
        static Color32 ExteriorColor(Building b)
        {
            switch (b.kind)
            {
                case BuildingKind.Barn: return C(140, 52, 42);
                case BuildingKind.Shed: return C(130, 112, 90);
                case BuildingKind.Grocery: case BuildingKind.Hardware: case BuildingKind.Pharmacy: case BuildingKind.Clothing: case BuildingKind.Diner: case BuildingKind.Office:
                    return MeshKit.Hash(b.id) < .5f ? C(156, 84, 66) : C(186, 176, 156);
                case BuildingKind.Police: return C(170, 168, 160);
                case BuildingKind.GasStation: return C(220, 220, 214);
                default: return Siding[b.id % Siding.Length];
            }
        }
        static Color32 RoofColor(Building b)
        {
            if (b.kind == BuildingKind.Barn) return C(90, 90, 96);
            var roofs = new[] { C(70, 62, 60), C(96, 70, 56), C(60, 70, 80), C(110, 100, 90), C(52, 56, 52) };
            return roofs[(b.id * 7) % roofs.Length];
        }
        static bool Commercial(Building b) { return b.kind != BuildingKind.House && b.kind != BuildingKind.Farmhouse && b.kind != BuildingKind.Barn && b.kind != BuildingKind.Shed; }

        BuildingView BuildBuilding(Building b)
        {
            var v = new BuildingView { b = b, root = new GameObject("Building " + b.id + " " + b.name) };
            v.root.transform.SetParent(root, false);
            var full = new MeshKit(); var low = new MeshKit();
            var r = b.rect; var ext = ExteriorColor(b); var inside = C(214, 208, 196);
            for (int x = r.x; x < r.MaxX; x++)
                for (int y = r.y; y <= r.MaxY; y++) WallEdge(full, low, x, y, false, ext, inside, b);
            for (int x = r.x; x <= r.MaxX; x++)
                for (int y = r.y; y < r.MaxY; y++) WallEdge(full, low, x, y, true, ext, inside, b);
            v.full = MeshKit.Object("Walls", v.root.transform, full.Build("B walls " + b.id), ArtLibrary.Vertex);
            v.low = MeshKit.Object("Walls (cut)", v.root.transform, low.Build("B low " + b.id), ArtLibrary.Vertex);
            v.low.SetActive(false);
            var roof = new MeshKit(); Roof(roof, b);
            v.roof = MeshKit.Object("Roof", v.root.transform, roof.Build("B roof " + b.id), ArtLibrary.Vertex);
            if (Commercial(b)) Sign(v, b);
            foreach (int id in buildingOpenings[b.id]) v.openings.Add(BuildOpening(map.openings[id], v.root.transform, b));
            v.glassMesh = new Mesh { name = "B glass " + b.id };
            v.glass = MeshKit.Object("Glass", v.root.transform, v.glassMesh, ArtLibrary.Glass, false);
            return v;
        }

        /// <summary>All window glass and shards of a building in one transparent mesh, rebuilt when a window changes.</summary>
        void SyncGlass(BuildingView v)
        {
            int code = 17;
            foreach (var ov in v.openings) if (ov.o.kind == Edge.Window) code = code * 31 + (ov.o.open ? 1 : 0) + (ov.o.broken ? 2 : 0) + (ov.o.glassCleared ? 4 : 0);
            if (code == v.glassState) return; v.glassState = code;
            var k = new MeshKit(); var clear = new Color32(255, 255, 255, 255); var shard = new Color32(200, 220, 230, 255);
            foreach (var ov in v.openings)
            {
                var o = ov.o; if (o.kind != Edge.Window) continue;
                var a = ov.along; var c = ov.center;
                if (!o.broken && !o.open)
                {
                    Vector3 p0 = c - a * .44f + Vector3.up * .92f, p1 = c - a * .44f + Vector3.up * 1.93f, p2 = c + a * .44f + Vector3.up * 1.93f, p3 = c + a * .44f + Vector3.up * .92f;
                    k.Quad(p0, p1, p2, p3, clear); k.Quad(p3, p2, p1, p0, clear);
                }
                else if (o.broken && !o.glassCleared)
                    for (int i = 0; i < 5; i++)
                    {
                        float x = -.4f + i * .2f, h = 1.05f + MeshKit.Hash(o.id + i) * .2f;
                        Vector3 q0 = c + a * x + Vector3.up * .92f, q1 = c + a * (x + .08f) + Vector3.up * h, q2 = c + a * (x + .16f) + Vector3.up * .92f;
                        k.Tri(q0, q1, q2, shard); k.Tri(q2, q1, q0, shard);
                    }
            }
            k.Build(v.glassMesh.name, v.glassMesh);
        }

        void WallEdge(MeshKit full, MeshKit low, int x, int y, bool vertical, Color32 ext, Color32 inside, Building b)
        {
            var e = vertical ? map.V(x, y) : map.H(x, y); if (e == Edge.None || e == Edge.Fence) return;
            var o = map.OpeningAt(x, y, vertical);
            bool exterior = vertical ? (x == b.rect.x || x == b.rect.MaxX) : (y == b.rect.y || y == b.rect.MaxY);
            // Interior edges also exist on building borders shared by nothing; only draw edges within this building's rect.
            var col = exterior ? MeshKit.Shade(ext, .95f + MeshKit.Hash(x * 31 + y * 17) * .05f) : inside;
            Vector3 center = vertical ? new Vector3(x, 0, y + .5f) : new Vector3(x + .5f, 0, y);
            float yaw = vertical ? 90 : 0; float len = 1 + WallThickness;
            if (e == Edge.Wall || o == null)
            {
                full.Box(center, new Vector3(len, WallHeight, WallThickness), col, yaw);
                low.Box(center, new Vector3(len, CutHeight, WallThickness), col, yaw);
                if (exterior) full.Box(center, new Vector3(len + .02f, .22f, WallThickness + .04f), MeshKit.Shade(col, .7f), yaw);   // foundation band
                return;
            }
            if (o.kind == Edge.Door)
            {
                full.Box(center + Vector3.up * 2.1f, new Vector3(len, WallHeight - 2.1f, WallThickness), col, yaw);
                // Door jambs.
                var along = vertical ? Vector3.forward : Vector3.right;
                full.Box(center - along * .48f, new Vector3(.06f, 2.1f, WallThickness + .03f), C(230, 228, 220), yaw);
                full.Box(center + along * .48f, new Vector3(.06f, 2.1f, WallThickness + .03f), C(230, 228, 220), yaw);
                return;
            }
            if (o.kind == Edge.Window)
            {
                full.Box(center, new Vector3(len, .9f, WallThickness), col, yaw); low.Box(center, new Vector3(len, CutHeight, WallThickness), col, yaw);
                full.Box(center + Vector3.up * 2.0f, new Vector3(len, WallHeight - 2.0f, WallThickness), col, yaw);
                full.Box(center + Vector3.up * .88f, new Vector3(1.0f, .06f, WallThickness + .1f), C(236, 234, 228), yaw);
                var along = vertical ? Vector3.forward : Vector3.right; var white = C(236, 234, 228);
                full.Box(center - along * .47f + Vector3.up * .9f, new Vector3(.06f, 1.1f, WallThickness + .06f), white, yaw);
                full.Box(center + along * .47f + Vector3.up * .9f, new Vector3(.06f, 1.1f, WallThickness + .06f), white, yaw);
                full.Box(center + Vector3.up * 1.95f, new Vector3(1f, .06f, WallThickness + .06f), white, yaw);
                full.Box(center + Vector3.up * 1.43f, new Vector3(.9f, .04f, .04f), white, yaw);
            }
        }

        void Roof(MeshKit k, Building b)
        {
            var r = b.rect; var col = RoofColor(b); float baseY = WallHeight, o = .35f;
            if (Commercial(b))
            {
                k.Box(new Vector3(r.Center.x, baseY, r.Center.y), new Vector3(r.w + .3f, .25f, r.h + .3f), MeshKit.Shade(col, 1.2f));
                k.Box(new Vector3(r.Center.x, baseY + .25f, r.y - .05f), new Vector3(r.w + .3f, .5f, .2f), ExteriorColor(b));
                return;
            }
            float rise = b.kind == BuildingKind.Barn ? 2.6f : 1.6f;
            bool alongX = r.w >= r.h;
            float x0 = r.x - o, x1 = r.MaxX + o, z0 = r.y - o, z1 = r.MaxY + o;
            if (alongX)
            {
                float zm = (z0 + z1) * .5f;
                k.Quad(new Vector3(x0, baseY, z0), new Vector3(x0, baseY + rise, zm), new Vector3(x1, baseY + rise, zm), new Vector3(x1, baseY, z0), col);
                k.Quad(new Vector3(x1, baseY, z1), new Vector3(x1, baseY + rise, zm), new Vector3(x0, baseY + rise, zm), new Vector3(x0, baseY, z1), MeshKit.Shade(col, .85f));
                var gable = ExteriorColor(b);
                k.Tri(new Vector3(r.x, baseY, r.y), new Vector3(r.x, baseY + rise * (1 - o / ((z1 - z0) * .5f)), zm), new Vector3(r.x, baseY, r.MaxY), MeshKit.Shade(gable, .9f));
                k.Tri(new Vector3(r.MaxX, baseY, r.MaxY), new Vector3(r.MaxX, baseY + rise * (1 - o / ((z1 - z0) * .5f)), zm), new Vector3(r.MaxX, baseY, r.y), MeshKit.Shade(gable, .85f));
            }
            else
            {
                float xm = (x0 + x1) * .5f;
                k.Quad(new Vector3(x0, baseY, z1), new Vector3(xm, baseY + rise, z1), new Vector3(xm, baseY + rise, z0), new Vector3(x0, baseY, z0), col);
                k.Quad(new Vector3(x1, baseY, z0), new Vector3(xm, baseY + rise, z0), new Vector3(xm, baseY + rise, z1), new Vector3(x1, baseY, z1), MeshKit.Shade(col, .85f));
                var gable = ExteriorColor(b);
                k.Tri(new Vector3(r.MaxX, baseY, r.y), new Vector3(xm, baseY + rise * (1 - o / ((x1 - x0) * .5f)), r.y), new Vector3(r.x, baseY, r.y), MeshKit.Shade(gable, .9f));
                k.Tri(new Vector3(r.x, baseY, r.MaxY), new Vector3(xm, baseY + rise * (1 - o / ((x1 - x0) * .5f)), r.MaxY), new Vector3(r.MaxX, baseY, r.MaxY), MeshKit.Shade(gable, .85f));
            }
        }

        void Sign(BuildingView v, Building b)
        {
            // Storefront sign on the side that has the double doors.
            var door = buildingOpenings[b.id].Select(i => map.openings[i]).FirstOrDefault(o => o.kind == Edge.Door && o.exterior);
            if (door == null) return;
            var go = new GameObject("Sign"); go.transform.SetParent(v.roof.transform, false);
            var text = go.AddComponent<TextMeshPro>(); text.text = b.name.ToUpperInvariant(); text.fontSize = 6; text.alignment = TextAlignmentOptions.Center; text.color = new Color(1, .95f, .8f);
            text.rectTransform.sizeDelta = new Vector2(Mathf.Max(4, (door.vertical ? b.rect.h : b.rect.w) - 1), 1.2f);
            var c = door.Center; bool front = !door.vertical && door.y == b.rect.y, back = !door.vertical && door.y == b.rect.MaxY, west = door.vertical && door.x == b.rect.x;
            Vector3 pos; float yaw;
            if (front) { pos = new Vector3(b.rect.Center.x, WallHeight + .45f, b.rect.y - .2f); yaw = 0; }
            else if (back) { pos = new Vector3(b.rect.Center.x, WallHeight + .45f, b.rect.MaxY + .2f); yaw = 180; }
            else if (west) { pos = new Vector3(b.rect.x - .2f, WallHeight + .45f, b.rect.Center.y); yaw = 90; }
            else { pos = new Vector3(b.rect.MaxX + .2f, WallHeight + .45f, b.rect.Center.y); yaw = 270; }
            go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var back2 = ArtLibrary.Solid("Sign board", go.transform, ArtLibrary.Cube, new Color(.18f, .2f, .24f));
            back2.transform.localPosition = new Vector3(0, 0, .06f); back2.transform.localScale = new Vector3(text.rectTransform.sizeDelta.x, 1.0f, .08f);
        }

        static readonly Dictionary<int, Mesh> doorMeshes = new Dictionary<int, Mesh>();
        static Mesh plankMesh;
        static Mesh DoorMesh(int variant)
        {
            Mesh m; if (doorMeshes.TryGetValue(variant, out m)) return m;
            var k = new MeshKit(); var col = variant < 4 ? new[] { C(120, 40, 36), C(60, 70, 90), C(110, 84, 60), C(40, 60, 46) }[variant] : C(196, 180, 150);
            k.Box(new Vector3(.46f, 0, 0), new Vector3(.9f, 2.05f, .05f), col, 0);
            k.Box(new Vector3(.8f, 1.0f, .05f), new Vector3(.06f, .06f, .08f), C(200, 180, 90), 0);
            m = k.Build("Shared door " + variant); doorMeshes[variant] = m; return m;
        }

        OpeningView BuildOpening(Opening o, Transform parent, Building b)
        {
            var v = new OpeningView { o = o };
            var along = o.vertical ? Vector3.forward : Vector3.right;
            var start = new Vector3(o.x, 0, o.y);
            var center = start + along * .5f;
            v.along = along; v.center = center;
            // Outward normal points away from the building interior.
            var probe = center + (o.vertical ? Vector3.right : Vector3.forward) * .5f;
            var probeRoom = map.RoomAt(Mathf.FloorToInt(probe.x), Mathf.FloorToInt(probe.z));
            bool positiveInside = probeRoom != null && probeRoom.building == b.id;
            v.outward = (o.vertical ? Vector3.right : Vector3.forward) * (positiveInside ? -1 : 1);
            if (o.kind == Edge.Door)
            {
                var pivot = new GameObject("Door " + o.id).transform; pivot.SetParent(parent, false); pivot.localPosition = start + along * .04f;
                MeshKit.Object("Panel", pivot, DoorMesh(o.exterior ? o.id % 4 : 4), ArtLibrary.Vertex);
                pivot.localRotation = Quaternion.FromToRotation(Vector3.right, along);
                v.door = pivot;
            }
            return v;
        }

        void SyncOpening(OpeningView v, Transform parent)
        {
            var o = v.o;
            int state = (o.open ? 1 : 0) | (o.broken ? 2 : 0) | (o.glassCleared ? 4 : 0) | (o.barricade << 3);
            if (state == v.state) return; v.state = state;
            if (v.door != null)
            {
                v.door.gameObject.SetActive(!o.broken);
                var closed = Quaternion.FromToRotation(Vector3.right, v.along);
                v.door.localRotation = o.open ? Quaternion.AngleAxis(Vector3.Dot(v.outward, Vector3.Cross(v.along, Vector3.up)) > 0 ? 95 : -95, Vector3.up) * closed : closed;
            }
            if (o.barricade > 0 && v.planks == null)
            {
                if (plankMesh == null) { var k = new MeshKit(); k.Box(Vector3.zero, new Vector3(1.15f, .18f, .05f), C(130, 96, 60)); plankMesh = k.Build("Shared plank"); }
                v.planks = new GameObject[4];
                for (int i = 0; i < 4; i++)
                {
                    var plank = MeshKit.Object("Plank " + i, parent, plankMesh, ArtLibrary.Vertex);
                    float tilt = i % 2 == 0 ? 14 : -12; float height = o.kind == Edge.Door ? .5f + i * .42f : 1.0f + i * .25f;
                    plank.transform.localPosition = v.center + Vector3.up * height + v.outward * .1f;
                    plank.transform.localRotation = Quaternion.FromToRotation(Vector3.right, v.along) * Quaternion.Euler(0, 0, tilt);
                    v.planks[i] = plank;
                }
            }
            if (v.planks != null) for (int i = 0; i < 4; i++) v.planks[i].SetActive(i < o.barricade);
        }

        // ---------------------------------------------------------------- cutaway
        void UpdateCutaway(Vector3 focus, Vector3 cameraBack)
        {
            cut.Clear();
            var inside = map.BuildingAt(new Vec2(focus.x, focus.z)); if (inside != null) cut.Add(inside.id);
            // Buildings between the focus and the camera hide their roofs and upper walls.
            var dir = new Vector2(cameraBack.x, cameraBack.z).normalized;
            for (float t = .5f; t < 11; t += .5f)
                for (int side = -1; side <= 1; side++)
                {
                    var p = new Vector2(focus.x, focus.z) + dir * t + new Vector2(-dir.y, dir.x) * side * 1.2f;
                    var b = map.BuildingAt(new Vec2(p.x, p.y)); if (b != null) cut.Add(b.id);
                }
            bool night = world.Daylight < .45f;
            foreach (var kv in buildings)
            {
                bool c = cut.Contains(kv.Key);
                if (c != kv.Value.isCut) { kv.Value.isCut = c; kv.Value.full.SetActive(!c); kv.Value.low.SetActive(c); kv.Value.roof.SetActive(!c); }
                bool lit = world.PowerOn && night && inside != null && inside.id == kv.Key;
                if (lit != kv.Value.lit) { kv.Value.lit = lit; if (lit && kv.Value.lights.Count == 0) RoomLights(kv.Value); foreach (var l in kv.Value.lights) l.enabled = lit; }
            }
        }
        public bool IsCut(int building) { return cut.Contains(building); }
        void RoomLights(BuildingView v)
        {
            foreach (int rid in v.b.rooms)
            {
                var room = map.rooms[rid];
                var l = new GameObject("Room light").AddComponent<Light>(); l.transform.SetParent(v.root.transform, false);
                l.transform.localPosition = new Vector3(room.rect.Center.x, 2.3f, room.rect.Center.y); l.type = LightType.Point; l.range = Mathf.Max(room.rect.w, room.rect.h) * .9f + 2;
                l.color = new Color(1, .9f, .75f); l.intensity = .9f; l.shadows = LightShadows.None; v.lights.Add(l);
            }
        }

        // ---------------------------------------------------------------- floor piles
        void SyncPiles(Vector3 focus)
        {
            var keep = new HashSet<int>();
            foreach (var kv in world.piles)
            {
                var p = kv.Value; if (p.inventory.items.Count == 0) continue;
                if (Mathf.Abs(p.x - focus.x) > 60 || Mathf.Abs(p.y - focus.z) > 60) continue;
                keep.Add(kv.Key);
                if (!pileViews.ContainsKey(kv.Key))
                {
                    var k = new MeshKit(); int n = Mathf.Min(4, p.inventory.items.Count);
                    for (int i = 0; i < n; i++) k.Box(new Vector3(-.2f + MeshKit.Hash(kv.Key + i) * .4f, 0, -.2f + MeshKit.Hash(kv.Key * 3 + i) * .4f), new Vector3(.22f, .12f, .3f), MeshKit.Shade(C(150, 130, 90), .7f + i * .1f), i * 37);
                    var go = MeshKit.Object("Pile", root, k.Build("Pile"), ArtLibrary.Vertex, false);
                    go.transform.localPosition = new Vector3(p.x + .5f, GroundHeight(map.GroundAt(p.x, p.y)), p.y + .5f);
                    pileViews[kv.Key] = go;
                }
            }
            foreach (var key in pileViews.Keys.Where(k => !keep.Contains(k)).ToList()) { var go = pileViews[key]; Destroy(go.GetComponent<MeshFilter>().sharedMesh); Destroy(go); pileViews.Remove(key); }
        }
    }
}
