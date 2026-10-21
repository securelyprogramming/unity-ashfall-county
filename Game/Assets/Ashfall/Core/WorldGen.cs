using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    public enum Facing { North, East, South, West }

    [Serializable]
    public sealed class Settlement { public string name = ""; public int x, y, radius; public ZoneKind zone; }

    /// <summary>Named points of interest beyond ordinary towns. Consumers (Humans, Bestiary, Community, WorldEvents, views)
    /// must tolerate kinds that a generator version does not place.</summary>
    public enum LandmarkKind : byte { Town, MilitaryBase, Airfield, Hospital, RadioStation, Garage, RaiderCompound, Warehouse, Church, School, Farmstead, Campground, Quarry,
        Motel }   // appended
    public sealed class Landmark
    {
        public string name = ""; public LandmarkKind kind; public int x, y, radius; public List<int> buildings = new List<int>();
        public Rect rect;                    // bounds (the fenced compound or lot; a square around a town)
        public string description = "";
        public Vec2 Center { get { return new Vec2(x + .5f, y + .5f); } }
        public bool Contains(Vec2 p, float margin = 0) { return rect.DistanceTo(p) <= margin; }
    }
    /// <summary>A named drop point on the New Game map (PUBG-style landing choice).</summary>
    public sealed class LandingZone { public string name = ""; public int x, y; public string description = ""; public Vec2 Center { get { return new Vec2(x + .5f, y + .5f); } } }

    /// <summary>Seeded generator for the whole county: river, road network, towns, landmarks, farms, forests, buildings and furniture.
    /// Landmarks, their layouts and the landing zones are in GenLandmarks.cs.</summary>
    public sealed partial class WorldGen
    {
        /// <summary>2: open-county scale (4-6 km), road grid, specialised landmarks and landing zones.</summary>
        public const int Version = 2;
        readonly Map map; readonly Rng rng; readonly int seed;
        public readonly List<Settlement> settlements = new List<Settlement>();
        public readonly List<Landmark> landmarks = new List<Landmark>();
        public readonly List<LandingZone> landingZones = new List<LandingZone>();
        public Rect bridge;
        public Vec2 evacuationPoint;
        readonly List<Rect> reserved = new List<Rect>();
        // Spatial index over reserved rectangles so placement checks stay local on large counties.
        const int ReserveCell = 64;
        List<int>[] reserveGrid; int reserveW, reserveH;
        // Primary roads: centre lines and half widths (the county highway, Route 9 and the larger-county roads).
        readonly List<Int2> roadsY = new List<Int2>(), roadsX = new List<Int2>();   // (centre, half width)
        float scale;   // county width / 1024

        public static Map Generate(int seed, int size, out WorldGen gen)
        {
            var map = new Map(size, size); gen = new WorldGen(map, seed); gen.Run(); return map;
        }
        WorldGen(Map map, int seed)
        {
            this.map = map; this.seed = seed; rng = new Rng((ulong)seed, 7);
            reserveW = (map.width + ReserveCell - 1) / ReserveCell; reserveH = (map.height + ReserveCell - 1) / ReserveCell;
            reserveGrid = new List<int>[reserveW * reserveH];
            noTrees = new bool[map.CellsX * map.CellsY]; landmarkCell = new bool[map.CellsX * map.CellsY];
        }
        int W { get { return map.width; } }
        int H { get { return map.height; } }
        /// <summary>Scale for footprints and radii: grows to 3 (3 km) and then stays, so bigger counties get more places, not bigger ones.</summary>
        float S { get { return Math.Min(scale, 3); } }

        // ---------------------------------------------------------------- terrain
        float Hash(int x, int y, int salt)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1274126177 + seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xffffff) / 16777216f;
        }
        float Noise(float x, float y, int salt)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y); float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(ix, iy, salt), b = Hash(ix + 1, iy, salt), c = Hash(ix, iy + 1, salt), d = Hash(ix + 1, iy + 1, salt);
            return MathUtil.Lerp(MathUtil.Lerp(a, b, fx), MathUtil.Lerp(c, d, fx), fy);
        }
        float Fractal(float x, float y, int salt) { return Noise(x / 220, y / 220, salt) * .55f + Noise(x / 90, y / 90, salt + 1) * .3f + Noise(x / 35, y / 35, salt + 2) * .15f; }

        void Fill(Rect r, Ground g)
        {
            for (int x = Math.Max(0, r.x); x < Math.Min(W, r.MaxX); x++)
                for (int y = Math.Max(0, r.y); y < Math.Min(H, r.MaxY); y++) map.ground[map.Index(x, y)] = g;
        }
        void SetCellZone(int x, int y, ZoneKind z, bool overrideTown = false)
        {
            if (!map.InBounds(x, y)) return; int i = x / Map.Cell + y / Map.Cell * map.CellsX;
            var current = (ZoneKind)map.zone[i];
            if (!overrideTown && (current == ZoneKind.Town || current == ZoneKind.Commercial || current == ZoneKind.Military) && z != ZoneKind.Commercial) return;
            map.zone[i] = (byte)z;
        }

        int RiverX(int y) { return (int)(W * .3f + Math.Sin(y / 260.0 + seed % 7) * 70 + Math.Sin(y / 83.0) * 18); }

        void Reserve(Rect r)
        {
            int id = reserved.Count; reserved.Add(r);
            int x0 = MathUtil.Clamp(r.x / ReserveCell, 0, reserveW - 1), x1 = MathUtil.Clamp((r.MaxX - 1) / ReserveCell, 0, reserveW - 1);
            int y0 = MathUtil.Clamp(r.y / ReserveCell, 0, reserveH - 1), y1 = MathUtil.Clamp((r.MaxY - 1) / ReserveCell, 0, reserveH - 1);
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) { var list = reserveGrid[x + y * reserveW]; if (list == null) reserveGrid[x + y * reserveW] = list = new List<int>(); list.Add(id); }
        }
        bool ReservedOverlap(Rect r, int margin)
        {
            var q = r.Expand(margin + 1);
            int x0 = MathUtil.Clamp(q.x / ReserveCell, 0, reserveW - 1), x1 = MathUtil.Clamp((q.MaxX - 1) / ReserveCell, 0, reserveW - 1);
            int y0 = MathUtil.Clamp(q.y / ReserveCell, 0, reserveH - 1), y1 = MathUtil.Clamp((q.MaxY - 1) / ReserveCell, 0, reserveH - 1);
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++)
                {
                    var list = reserveGrid[x + y * reserveW]; if (list == null) continue;
                    foreach (int id in list) if (reserved[id].Overlaps(r, margin)) return true;
                }
            return false;
        }

        void Run()
        {
            scale = W / 1024f;
            // Forest and wild ground from fractal noise.
            for (int cy = 0; cy < map.CellsY; cy++)
                for (int cx = 0; cx < map.CellsX; cx++)
                {
                    float n = Fractal(cx * Map.Cell, cy * Map.Cell, 11);
                    map.zone[cx + cy * map.CellsX] = (byte)(n > .56f ? ZoneKind.Forest : ZoneKind.Wild);
                }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (Hash(x, y, 3) < .05f && Noise(x / 40f, y / 40f, 5) > .6f) map.ground[map.Index(x, y)] = Ground.Dirt;

            // The Ash River runs north to south with sandy banks.
            for (int y = 0; y < H; y++)
            {
                int cx = RiverX(y), half = 6 + (int)(Noise(y / 60f, 0, 9) * 6);
                for (int x = cx - half - 2; x <= cx + half + 2; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    bool water = Math.Abs(x - cx) <= half;
                    map.ground[map.Index(x, y)] = water ? Ground.Water : Ground.Sand;
                    if (water) SetCellZone(x, y, ZoneKind.Water, true);
                }
            }
            // Ponds in the hills; larger counties get a few more lakes away from the river.
            Pond(new Vec2(W * .78f, H * .82f), 38);
            Pond(new Vec2(W * .12f, H * .78f), 24);
            for (int i = 0, made = 0; i < 60 && made < (int)(scale * scale / 4); i++)
            {
                int r = rng.Range(18, 46), x = rng.Range(r + 80, W - r - 80), y = rng.Range(r + 80, H - r - 80);
                if (Math.Abs(x - RiverX(y)) < r + 90) continue;
                Pond(new Vec2(x, y), r); made++;
            }

            // Primary roads: the county highway (east-west) and Route 9 (north-south, east of the river). Larger counties add
            // a north and a south county road and a west and an east road so every place is a short drive from a main road.
            int highwayY = H / 2, routeX = (int)(W * .62f);
            Road(0, highwayY - 4, W, 8, true, Ground.Road); roadsY.Add(new Int2(highwayY, 4));
            Road(routeX - 3, 0, 6, H, false, Ground.Road); roadsX.Add(new Int2(routeX, 3));
            if (scale >= 2)
            {
                foreach (float f in new[] { .2f, .8f }) { int y = (int)(H * f); Road(0, y - 3, W, 6, true, Ground.Road); roadsY.Add(new Int2(y, 3)); }
                foreach (float f in new[] { .1f, .88f }) { int x = (int)(W * f); Road(x - 3, 0, 6, H, false, Ground.Road); roadsX.Add(new Int2(x, 3)); }
            }
            int bx = RiverX(highwayY);
            bridge = new Rect(bx - 16, highwayY - 5, 32, 10);
            for (int x = bridge.x; x < bridge.MaxX; x++) for (int y = highwayY - 4; y < highwayY + 4; y++) if (map.InBounds(x, y)) map.ground[map.Index(x, y)] = Ground.Bridge;
            for (int x = bridge.x; x < bridge.MaxX; x++) { map.SetH(x, highwayY - 4, Edge.Fence); map.SetH(x, highwayY + 4, Edge.Fence); }
            evacuationPoint = new Vec2(bx - 30, highwayY);
            Fill(new Rect(bx - 46, highwayY + 5, 26, 14), Ground.Parking);
            Reserve(new Rect(bx - 50, highwayY - 20, 70, 40));

            // The county seat sits on the crossroads. Footprints stop growing past 3 km; bigger counties get more towns instead.
            Town("Cedar Fork", routeX, highwayY, (int)(170 + 70 * S), true);
            // Big landmarks claim open country before the other towns fill it.
            MajorLandmarks();
            int wanted = Math.Min(TownNames.Length, (int)(scale * scale * 1.1f) + 3), spacing = scale < 2 ? 36 : 90;   // small test counties pack towns tighter
            for (int i = 0, n = 0; i < 300 + (int)(80 * scale * scale) && n < wanted; i++)
            {
                int radius = rng.Range(80, (int)(120 + 40 * S));
                int x = rng.Range(radius + 30, W - radius - 30), y = rng.Range(radius + 30, H - radius - 30);
                if (Math.Abs(x - RiverX(y)) < radius + 30) continue;
                if (settlements.Any(t => Vec2.Distance(new Vec2(t.x, t.y), new Vec2(x, y)) < t.radius + radius + spacing)) continue;
                if (NearLandmark(new Rect(x - radius, y - radius, radius * 2, radius * 2), spacing / 3)) continue;
                Town(TownNames[n++], x, y, radius, false);
            }
            // County roads joining every settlement to the primary roads.
            foreach (var s in settlements.Skip(1)) { Rect c; bool hz; if (PlanConnector(s.x, s.y, 6, new Rect(0, 0, 0, 0), true, out c, out hz)) Road(c.x, c.y, c.w, c.h, hz, Ground.Road); }
            // Smaller landmarks sit beside towns and along the roads.
            MinorLandmarks();
            // Farms throughout the countryside.
            int farms = (int)(10 * scale * scale);
            for (int i = 0; i < farms * 6 && settlements.Count > 0 && farmCount < farms; i++)
                Farm(rng.Range(60, W - 60), rng.Range(60, H - 60));
            Farmsteads();
            // Rural homes and the odd gas station along every primary road.
            foreach (var road in roadsY)
                for (int i = 0; i < W / 20; i++) { int x = rng.Range(40, W - 40); bool south = rng.Chance(.5f); RoadsideLot(x, road.x + (south ? road.y + 2 : -road.y - 2), south ? Facing.South : Facing.North); }
            foreach (var road in roadsX)
                for (int i = 0; i < H / 20; i++) { int y = rng.Range(40, H - 40); bool west = rng.Chance(.5f); RoadsideLot(road.x + (west ? road.y + 2 : -road.y - 2), y, west ? Facing.West : Facing.East); }
            Forests();
            DefaultLandmarks();
            LandingZones();
            Cars();
        }

        static readonly string[] TownNames =
        {
            "Harlan Springs", "Millbrook", "Pine Hollow", "Riverside", "Dunmore", "Elk Ridge", "Fairhaven", "Grant's Mill", "Hollis", "Ironwood", "Juniper Flats", "Kettle Creek", "Lowell", "Marrow Bend", "Northgate", "Oak Valley",
            "Quarry Hill", "Redbank", "Saltlick", "Tamarack", "Union Grove", "Vesper", "Willow Bend", "Yarrow", "Ashby Crossing", "Bellwether", "Coldwater", "Dry Creek", "Eastbrook", "Garnet",
            "Hadley", "Jasper", "Kinross", "Laurel", "Maple Ridge", "New Bethel", "Orchard Park", "Possum Trot", "Quincy Mill", "Rook's Ferry", "Silver Lake", "Thistle", "Upton", "West Ashfall",
            "Wicker", "Yellow Branch", "Zion Hill", "Barlow", "Copper Run", "Deer Lick",
        };

        void Pond(Vec2 c, int r)
        {
            for (int x = (int)c.x - r - 3; x <= c.x + r + 3; x++)
                for (int y = (int)c.y - r - 3; y <= c.y + r + 3; y++)
                {
                    if (!map.InBounds(x, y)) continue;
                    float d = Vec2.Distance(new Vec2(x, y), c) / (r * (.8f + .4f * Noise(x / 9f, y / 9f, 21)));
                    if (d < 1) { map.ground[map.Index(x, y)] = Ground.Water; SetCellZone(x, y, ZoneKind.Water, true); }
                    else if (d < 1.12f) map.ground[map.Index(x, y)] = Ground.Sand;
                }
        }

        void Road(int x, int y, int w, int h, bool horizontal, Ground g)
        {
            var r = new Rect(x, y, w, h);
            for (int i = Math.Max(0, r.x); i < Math.Min(W, r.MaxX); i++)
                for (int j = Math.Max(0, r.y); j < Math.Min(H, r.MaxY); j++)
                {
                    int idx = map.Index(i, j);
                    if (map.ground[idx] == Ground.Water) { map.ground[idx] = Ground.Bridge; continue; }
                    if (map.ground[idx] == Ground.Bridge || map.roomAt[idx] >= 0 || landmarkCell[i / Map.Cell + j / Map.Cell * map.CellsX] && InLandmark(i, j)) continue;
                    if (g == Ground.Gravel && map.ground[idx] == Ground.Road) continue;
                    map.ground[idx] = g;
                }
            Reserve(new Rect(r.x - 1, r.y - 1, r.w + 2, r.h + 2));
        }

        /// <summary>Plans the shortest straight connector from (x, y) to a primary road that avoids landmarks, buildings and the
        /// excluded rectangle, and open water unless the fallback pass (which may bridge) is allowed. Returns false when none exists.</summary>
        bool PlanConnector(int x, int y, int width, Rect exclude, bool fallback, out Rect corridor, out bool horizontal)
        {
            corridor = default(Rect); horizontal = false; int best = int.MaxValue;
            for (int pass = 0; pass < (fallback ? 2 : 1) && best == int.MaxValue; pass++)
            {
                foreach (var road in roadsY)
                {
                    int len = Math.Abs(road.x - y); if (len >= best) continue;
                    var c = new Rect(x - width / 2, Math.Min(y, road.x), width, len + 1);
                    if (!ConnectorClear(c, exclude, false, pass == 1)) continue;
                    best = len; corridor = c; horizontal = false;
                }
                foreach (var road in roadsX)
                {
                    int len = Math.Abs(road.x - x); if (len >= best) continue;
                    var c = new Rect(Math.Min(x, road.x), y - width / 2, len + 1, width);
                    if (!ConnectorClear(c, exclude, true, pass == 1)) continue;
                    best = len; corridor = c; horizontal = true;
                }
            }
            return best != int.MaxValue;
        }
        bool ConnectorClear(Rect c, Rect exclude, bool horizontal, bool bridges)
        {
            if (NearLandmark(c, 1) || exclude.w > 0 && exclude.Overlaps(c)) return false;
            // Sample the centre line: no water (rivers and ponds keep their few bridges) and no buildings in the way.
            int n = horizontal ? c.w : c.h;
            for (int i = 0; i < n; i += 2)
            {
                int x = horizontal ? c.x + i : c.x + c.w / 2, y = horizontal ? c.y + c.h / 2 : c.y + i;
                if (!map.InBounds(x, y)) return false;
                if (!bridges && map.ground[map.Index(x, y)] == Ground.Water || map.roomAt[map.Index(x, y)] >= 0) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- settlements
        void Town(string name, int cx, int cy, int radius, bool county)
        {
            settlements.Add(new Settlement { name = name, x = cx, y = cy, radius = radius, zone = ZoneKind.Town });
            int blockW = 46, blockH = 40, street = 6;
            int nx = radius * 2 / blockW, ny = radius * 2 / blockH;
            int ox = cx - nx * blockW / 2, oy = cy - ny * blockH / 2;
            // Align one street with the local primary road so the grid meets it cleanly.
            oy = cy - ((cy - oy) / blockH) * blockH - 3;
            ox = cx - ((cx - ox) / blockW) * blockW - 3;
            bool[,] active = new bool[nx, ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    var c = new Vec2(ox + i * blockW + blockW / 2f, oy + j * blockH + blockH / 2f);
                    float d = Vec2.Distance(c, new Vec2(cx, cy)) / radius;
                    active[i, j] = d < .95f - Noise(i, j, 31) * .25f && !AnyWater(new Rect(ox + i * blockW, oy + j * blockH, blockW, blockH));
                }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    if (!active[i, j]) continue;
                    var b = new Rect(ox + i * blockW, oy + j * blockH, blockW, blockH);
                    // Streets on every edge of an active block (shared with neighbours).
                    Road(b.x, b.y, blockW + street, street, true, Ground.Road);
                    Road(b.x, b.y, street, blockH + street, false, Ground.Road);
                    Road(b.x, b.MaxY, blockW + street, street, true, Ground.Road);
                    Road(b.MaxX, b.y, street, blockH + street, false, Ground.Road);
                    var inner = new Rect(b.x + street, b.y + street, blockW - street, blockH - street);
                    float d = Vec2.Distance(inner.Center, new Vec2(cx, cy)) / radius;
                    bool commercial = d < (county ? .38f : .3f);
                    for (int x = inner.x; x < inner.MaxX; x += Map.Cell) for (int y = inner.y; y < inner.MaxY; y += Map.Cell) SetCellZone(x, y, commercial ? ZoneKind.Commercial : d < .7f ? ZoneKind.Town : ZoneKind.Suburb, true);
                    // Sidewalks ring the block in town.
                    Sidewalk(inner);
                    var lotArea = new Rect(inner.x + 2, inner.y + 2, inner.w - 4, inner.h - 4);
                    if (commercial) CommercialBlock(lotArea, county); else ResidentialBlock(lotArea);
                }
            for (int x = cx - radius; x < cx + radius; x += Map.Cell) for (int y = cy - radius; y < cy + radius; y += Map.Cell)
                    if (Vec2.Distance(new Vec2(x, y), new Vec2(cx, cy)) < radius * 1.1f && map.ZoneAt(x, y) == ZoneKind.Forest) SetCellZone(x, y, ZoneKind.Suburb, true);
        }
        bool AnyWater(Rect r)
        {
            for (int x = r.x; x < r.MaxX; x += 3) for (int y = r.y; y < r.MaxY; y += 3) { var g = map.GroundAt(x, y); if (g == Ground.Water || g == Ground.Sand || g == Ground.Bridge) return true; }
            return false;
        }
        void Sidewalk(Rect inner)
        {
            for (int x = inner.x; x < inner.MaxX; x++) { Paint(x, inner.y, Ground.Sidewalk); Paint(x, inner.y + 1, Ground.Sidewalk); Paint(x, inner.MaxY - 1, Ground.Sidewalk); Paint(x, inner.MaxY - 2, Ground.Sidewalk); }
            for (int y = inner.y; y < inner.MaxY; y++) { Paint(inner.x, y, Ground.Sidewalk); Paint(inner.x + 1, y, Ground.Sidewalk); Paint(inner.MaxX - 1, y, Ground.Sidewalk); Paint(inner.MaxX - 2, y, Ground.Sidewalk); }
        }
        void Paint(int x, int y, Ground g) { if (map.InBounds(x, y) && map.ground[map.Index(x, y)] != Ground.Road && map.ground[map.Index(x, y)] != Ground.Water) map.ground[map.Index(x, y)] = g; }

        void CommercialBlock(Rect area, bool county)
        {
            // Two rows of storefronts, one facing each long street.
            var kinds = county
                ? new[] { BuildingKind.Grocery, BuildingKind.Pharmacy, BuildingKind.Hardware, BuildingKind.Diner, BuildingKind.Police, BuildingKind.Clothing, BuildingKind.GasStation, BuildingKind.Office }
                : new[] { BuildingKind.Grocery, BuildingKind.Pharmacy, BuildingKind.Hardware, BuildingKind.Diner, BuildingKind.GasStation, BuildingKind.Office, BuildingKind.Clothing };
            int depth = area.h / 2 - 1;
            for (int row = 0; row < 2; row++)
            {
                int x = area.x;
                while (x < area.MaxX - 12)
                {
                    int w = Math.Min(rng.Range(14, 24), area.MaxX - x);
                    if (w < 12) break;
                    var kind = kinds[rng.Range(0, kinds.Length)];
                    if (kind == BuildingKind.GasStation)
                    {
                        // Forecourt with pumps, small kiosk set back.
                        var lot = new Rect(x, row == 0 ? area.y : area.MaxY - depth, w, depth);
                        Fill(lot, Ground.Parking);
                        var kiosk = new Rect(x + 2, row == 0 ? lot.MaxY - 9 : lot.y + 1, Math.Min(10, w - 3), 8);
                        Construct(kiosk, kind, row == 0 ? Facing.South : Facing.North);
                        for (int p = 0; p < 2; p++) map.AddProp(PropKind.GasPump, x + 3 + p * 5, row == 0 ? lot.y + 3 : lot.MaxY - 4);
                    }
                    else
                    {
                        int d = Math.Min(depth, rng.Range(12, 19));
                        var r = new Rect(x, row == 0 ? area.y : area.MaxY - d, w, d);
                        Construct(r, kind, row == 0 ? Facing.South : Facing.North);
                    }
                    x += w + rng.Range(1, 4);
                }
            }
        }
        void ResidentialBlock(Rect area)
        {
            for (int row = 0; row < 2; row++)
            {
                int x = area.x;
                while (x < area.MaxX - 12)
                {
                    int lotW = rng.Range(15, 20);
                    if (x + lotW > area.MaxX) break;
                    int lotD = area.h / 2;
                    var lot = new Rect(x, row == 0 ? area.y : area.MaxY - lotD, lotW, lotD);
                    if (rng.Chance(.9f)) House(lot, row == 0 ? Facing.South : Facing.North);
                    x += lotW;
                }
            }
        }
        void House(Rect lot, Facing front)
        {
            int w = Math.Min(lot.w - 3, rng.Range(9, 14)), d = Math.Min(lot.h - 5, rng.Range(8, 12));
            if (w < 8 || d < 7) return;
            int x = lot.x + (lot.w - w) / 2;
            int setback = rng.Range(2, 4);
            int y = front == Facing.South ? lot.y + setback : lot.MaxY - setback - d;
            var r = new Rect(x, y, w, d);
            Construct(r, BuildingKind.House, front);
            // Driveway and a backyard shed for some homes.
            if (rng.Chance(.35f))
            {
                int sx = lot.x + 1, sy = front == Facing.South ? r.MaxY + 1 : r.y - 5;
                var shed = new Rect(sx, sy, 4, 4);
                if (Free(shed, 0)) Construct(shed, BuildingKind.Shed, front == Facing.South ? Facing.South : Facing.North);
            }
            if (rng.Chance(.3f)) { int bx = r.MaxX + 1, by = front == Facing.South ? lot.y : lot.MaxY - 2; if (bx < lot.MaxX - 1) { var bin = map.AddProp(PropKind.Bin, bx, by); map.AddContainer(bin, "bin"); } }
            if (rng.Chance(.4f)) map.AddProp(PropKind.Mailbox, x + 1, front == Facing.South ? lot.y : lot.MaxY - 1);
        }
        void RoadsideLot(int x, int y, Facing front)
        {
            if (!map.InBounds(x, y) || map.ZoneAt(x, y) == ZoneKind.Town || map.ZoneAt(x, y) == ZoneKind.Commercial || map.ZoneAt(x, y) == ZoneKind.Military) return;
            int w = rng.Range(9, 13), d = rng.Range(8, 11);
            int gap = 3;
            var r = front == Facing.South ? new Rect(x, y + gap, w, d) : front == Facing.North ? new Rect(x, y - gap - d, w, d) : front == Facing.West ? new Rect(x + gap, y, d, w) : new Rect(x - gap - d, y, d, w);
            if (!Free(r, 3) || NearLandmark(r, 6)) return;
            Construct(r, rng.Chance(.2f) ? BuildingKind.GasStation : BuildingKind.House, front);
            for (int cx = r.x; cx < r.MaxX; cx += Map.Cell) for (int cy = r.y; cy < r.MaxY; cy += Map.Cell) SetCellZone(cx, cy, ZoneKind.Suburb);
        }

        int farmCount;
        void Farm(int x, int y)
        {
            var area = new Rect(x - 40, y - 34, 80, 68);
            if (!Free(area, 6) || AnyWater(area) || map.ZoneAt(x, y) == ZoneKind.Town || map.ZoneAt(x, y) == ZoneKind.Commercial || NearLandmark(area, 12)) return;
            if (settlements.Any(s => Vec2.Distance(new Vec2(s.x, s.y), new Vec2(x, y)) < s.radius + 60)) return;
            // Gravel drive from just west of the farm to the nearest primary road, never through a landmark or the fields.
            Rect drive; bool hz;
            if (!PlanConnector(area.x - 3, y, 4, area, false, out drive, out hz)) return;
            farmCount++;
            for (int cx = area.x; cx < area.MaxX; cx += Map.Cell) for (int cy = area.y; cy < area.MaxY; cy += Map.Cell) SetCellZone(cx, cy, ZoneKind.Farm);
            var site = new Landmark { name = "", kind = LandmarkKind.Farmstead, x = x, y = y, radius = 52, rect = area };
            var house = Construct(new Rect(x - 30, y - 26, rng.Range(11, 15), rng.Range(10, 13)), BuildingKind.Farmhouse, Facing.East);
            var barn = Construct(new Rect(x + 6, y - 28, rng.Range(13, 17), rng.Range(16, 20)), BuildingKind.Barn, Facing.West);
            if (house != null) site.buildings.Add(house.id); if (barn != null) site.buildings.Add(barn.id);
            farmSites.Add(site);
            // Fields with rows of crops, fenced.
            var field = new Rect(x - 34, y + 2, 66, 28);
            for (int i = field.x; i < field.MaxX; i++) for (int j = field.y; j < field.MaxY; j++) if (map.InBounds(i, j)) map.ground[map.Index(i, j)] = Ground.Field;
            for (int i = field.x + 1; i < field.MaxX - 1; i += 2) for (int j = field.y + 1; j < field.MaxY - 1; j += 3) if (rng.Chance(.55f)) map.AddProp(PropKind.Crops, i, j);
            for (int i = field.x; i < field.MaxX; i++) { if (i % 9 != 4) { map.SetH(i, field.y, Edge.Fence); map.SetH(i, field.MaxY, Edge.Fence); } }
            for (int j = field.y; j < field.MaxY; j++) { if (j % 9 != 4) { map.SetV(field.x, j, Edge.Fence); map.SetV(field.MaxX, j, Edge.Fence); } }
            Reserve(area);
            Road(drive.x, drive.y, drive.w, drive.h, hz, Ground.Gravel);
        }

        bool Free(Rect r, int margin)
        {
            if (r.x < 4 || r.y < 4 || r.MaxX > W - 4 || r.MaxY > H - 4) return false;
            if (ReservedOverlap(r, margin)) return false;
            for (int x = r.x; x < r.MaxX; x++) for (int y = r.y; y < r.MaxY; y++)
                {
                    var g = map.GroundAt(x, y);
                    if (g == Ground.Road || g == Ground.Water || g == Ground.Bridge || g == Ground.Sand || map.roomAt[map.Index(x, y)] >= 0) return false;
                }
            return true;
        }

        // ---------------------------------------------------------------- buildings
        struct Local
        {
            public Rect r; public Facing f;
            public int Wd { get { return f == Facing.North || f == Facing.South ? r.w : r.h; } }
            public int Dp { get { return f == Facing.North || f == Facing.South ? r.h : r.w; } }
            /// <summary>u runs along the front; v is depth from the front wall inward.</summary>
            public Int2 T(int u, int v)
            {
                switch (f)
                {
                    case Facing.South: return new Int2(r.x + u, r.y + v);
                    case Facing.North: return new Int2(r.x + u, r.MaxY - 1 - v);
                    case Facing.West: return new Int2(r.x + v, r.y + u);
                    default: return new Int2(r.MaxX - 1 - v, r.y + u);
                }
            }
            public Rect R(int u, int v, int w, int d)
            {
                var a = T(u, v); var b = T(u + w - 1, v + d - 1);
                return new Rect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Abs(a.x - b.x) + 1, Math.Abs(a.y - b.y) + 1);
            }
        }

        void SetEdge(Int2 a, Int2 b, Edge e)
        {
            if (b.x == a.x + 1) map.SetV(b.x, a.y, e); else if (b.x == a.x - 1) map.SetV(a.x, a.y, e);
            else if (b.y == a.y + 1) map.SetH(a.x, b.y, e); else if (b.y == a.y - 1) map.SetH(a.x, a.y, e);
        }
        Opening AddOpening(Int2 a, Int2 b, Edge kind, int building, bool exterior)
        {
            if (b.x == a.x + 1) return map.AddOpening(b.x, a.y, true, kind, building, exterior);
            if (b.x == a.x - 1) return map.AddOpening(a.x, a.y, true, kind, building, exterior);
            if (b.y == a.y + 1) return map.AddOpening(a.x, b.y, false, kind, building, exterior);
            return map.AddOpening(a.x, a.y, false, kind, building, exterior);
        }

        Room AddRoom(Building b, Rect r, RoomType type, Ground floor)
        {
            var room = new Room { id = map.rooms.Count, building = b.id, rect = r, type = type };
            map.rooms.Add(room); b.rooms.Add(room.id);
            for (int x = r.x; x < r.MaxX; x++) for (int y = r.y; y < r.MaxY; y++) { int i = map.Index(x, y); map.roomAt[i] = room.id; map.ground[i] = floor; }
            return room;
        }

        static string BuildingName(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.Grocery: return "Grocery";
                case BuildingKind.Pharmacy: return "Pharmacy";
                case BuildingKind.Hardware: return "Hardware Store";
                case BuildingKind.Diner: return "Diner";
                case BuildingKind.GasStation: return "Gas Station";
                case BuildingKind.Police: return "Sheriff's Office";
                case BuildingKind.Clothing: return "Clothing Store";
                case BuildingKind.Office: return "Office";
                case BuildingKind.Barn: return "Barn";
                case BuildingKind.Shed: return "Shed";
                case BuildingKind.Farmhouse: return "Farmhouse";
                case BuildingKind.Warehouse: return "Warehouse";
                case BuildingKind.Church: return "Church";
                case BuildingKind.School: return "School";
                case BuildingKind.Hospital: return "Hospital";
                case BuildingKind.Barracks: return "Barracks";
                case BuildingKind.Armory: return "Armory";
                case BuildingKind.Hangar: return "Hangar";
                case BuildingKind.RadioStation: return "Radio Station";
                case BuildingKind.Garage: return "Garage";
                case BuildingKind.Motel: return "Motel";
                case BuildingKind.Compound: return "Compound";
                default: return "House";
            }
        }

        Building Construct(Rect r, BuildingKind kind, Facing front)
        {
            if (!Free(r, kind == BuildingKind.Shed ? 0 : 1)) return null;
            Reserve(r);
            var b = new Building { id = map.buildings.Count, kind = kind, rect = r, name = BuildingName(kind) };
            map.buildings.Add(b);
            var L = new Local { r = r, f = front };
            int W = L.Wd, D = L.Dp;
            // Exterior walls around the footprint.
            for (int x = r.x; x < r.MaxX; x++) { map.SetH(x, r.y, Edge.Wall); map.SetH(x, r.MaxY, Edge.Wall); }
            for (int y = r.y; y < r.MaxY; y++) { map.SetV(r.x, y, Edge.Wall); map.SetV(r.MaxX, y, Edge.Wall); }
            var doors = new List<Int2>();
            switch (kind)
            {
                case BuildingKind.House: case BuildingKind.Farmhouse: HouseLayout(b, L, W, D, doors); break;
                case BuildingKind.Shed:
                    {
                        var room = AddRoom(b, r, RoomType.Garage, Ground.Concrete);
                        int u = W / 2; FrontDoor(b, L, u, doors, rng.Chance(.3f));
                        Furnish(room, PropKind.Toolbox, "tools", 1); Furnish(room, PropKind.Shelf, "tools", 1);
                        break;
                    }
                case BuildingKind.Barn:
                    {
                        var room = AddRoom(b, r, RoomType.Barn, Ground.Concrete);
                        int u = W / 2 - 1; FrontDoor(b, L, u, doors, false); FrontDoor(b, L, u + 1, doors, false);
                        BackDoor(b, L, W / 2, D, doors);
                        Furnish(room, PropKind.HayBale, "", rng.Range(3, 7)); Furnish(room, PropKind.Crate, "barn", rng.Range(2, 4)); Furnish(room, PropKind.Workbench, "tools", 1);
                        break;
                    }
                default: if (!LandmarkLayout(b, kind, L, W, D, doors)) StoreLayout(b, kind, L, W, D, doors); break;
            }
            Windows(b, L, W, D, doors, WindowChance(kind));
            return b;
        }
        static float WindowChance(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.Barn: case BuildingKind.Shed: return .2f;
                case BuildingKind.House: case BuildingKind.Farmhouse: return .65f;
                case BuildingKind.Armory: return .05f;
                case BuildingKind.Warehouse: case BuildingKind.Hangar: case BuildingKind.Garage: case BuildingKind.Compound: return .3f;
                case BuildingKind.Barracks: return .6f;
                default: return .9f;
            }
        }

        void FrontDoor(Building b, Local L, int u, List<Int2> doors, bool locked)
        {
            var inside = L.T(u, 0); var outside = L.T(u, -1);
            var o = AddOpening(inside, outside, Edge.Door, b.id, true); o.locked = locked; o.open = false; doors.Add(inside);
        }
        void BackDoor(Building b, Local L, int u, int D, List<Int2> doors)
        {
            var inside = L.T(u, D - 1); var outside = L.T(u, D);
            var o = AddOpening(inside, outside, Edge.Door, b.id, true); o.locked = rng.Chance(.5f); doors.Add(inside);
        }
        void InteriorWallAcross(Building b, Local L, int v, int u0, int u1, int doorU)
        {
            // Wall between depth v-1 and v for u in [u0, u1).
            for (int u = u0; u < u1; u++) { var a = L.T(u, v - 1); var c = L.T(u, v); if (u == doorU) AddOpening(a, c, Edge.Door, b.id, false); else SetEdge(a, c, Edge.Wall); }
        }
        void InteriorWallAlong(Building b, Local L, int u, int v0, int v1, int doorV)
        {
            for (int v = v0; v < v1; v++) { var a = L.T(u - 1, v); var c = L.T(u, v); if (v == doorV) AddOpening(a, c, Edge.Door, b.id, false); else SetEdge(a, c, Edge.Wall); }
        }

        void HouseLayout(Building b, Local L, int W, int D, List<Int2> doors)
        {
            int frontD = D / 2, split = Math.Max(4, W / 2 + rng.Range(-1, 2));
            var living = AddRoom(b, L.R(0, 0, split, frontD), RoomType.Living, Ground.WoodFloor);
            var kitchen = AddRoom(b, L.R(split, 0, W - split, frontD), RoomType.Kitchen, Ground.TileFloor);
            InteriorWallAlong(b, L, split, 0, frontD, rng.Range(1, frontD - 1));
            int bath = 3, bed1 = Math.Max(4, (W - bath) / 2);
            if (W - bed1 - bath < 4) bed1 = W - bath;
            var bedroom = AddRoom(b, L.R(0, frontD, bed1, D - frontD), RoomType.Bedroom, Ground.WoodFloor);
            var bathroom = AddRoom(b, L.R(bed1, frontD, bath, D - frontD), RoomType.Bathroom, Ground.TileFloor);
            InteriorWallAcross(b, L, frontD, 0, W, -1);
            // Doors from the living room into the bedroom and from the kitchen into the bathroom.
            AddOpening(L.T(1, frontD - 1), L.T(1, frontD), Edge.Door, b.id, false);
            AddOpening(L.T(Math.Min(W - 1, bed1 + 1), frontD - 1), L.T(Math.Min(W - 1, bed1 + 1), frontD), Edge.Door, b.id, false);
            InteriorWallAlong(b, L, bed1, frontD, D, -1);
            Room bedroom2 = null;
            if (W - bed1 - bath >= 4)
            {
                bedroom2 = AddRoom(b, L.R(bed1 + bath, frontD, W - bed1 - bath, D - frontD), RoomType.Bedroom, Ground.WoodFloor);
                InteriorWallAlong(b, L, bed1 + bath, frontD, D, -1);
                AddOpening(L.T(W - 2, frontD - 1), L.T(W - 2, frontD), Edge.Door, b.id, false);
            }
            FrontDoor(b, L, rng.Range(1, split - 1), doors, rng.Chance(.4f));
            // Side door from the kitchen.
            var side = L.T(W - 1, 1); var sideOut = L.T(W, 1); var o = AddOpening(side, sideOut, Edge.Door, b.id, true); o.locked = rng.Chance(.5f); doors.Add(side);
            Furnish(living, PropKind.Couch, "living", 1); Furnish(living, PropKind.Table, "living", 1); if (rng.Chance(.5f)) Furnish(living, PropKind.Desk, "office", 1);
            Furnish(kitchen, PropKind.Fridge, "fridge", 1); Furnish(kitchen, PropKind.Stove, "stove", 1); Furnish(kitchen, PropKind.Sink, "", 1); Furnish(kitchen, PropKind.Counter, "kitchen", rng.Range(2, 4));
            Furnish(bedroom, PropKind.Bed, "", 1); Furnish(bedroom, PropKind.Dresser, "bedroom", 1); if (rng.Chance(.6f)) Furnish(bedroom, PropKind.Wardrobe, "wardrobe", 1);
            Furnish(bathroom, PropKind.Toilet, "", 1); Furnish(bathroom, PropKind.MedicineCabinet, "bathroom", 1);
            if (bedroom2 != null) { Furnish(bedroom2, PropKind.Bed, "", 1); Furnish(bedroom2, PropKind.Dresser, "bedroom", 1); }
        }

        void StoreLayout(Building b, BuildingKind kind, Local L, int W, int D, List<Int2> doors)
        {
            RoomType type; string shelf; Ground floor = Ground.TileFloor;
            switch (kind)
            {
                case BuildingKind.Grocery: type = RoomType.Grocery; shelf = "grocery"; break;
                case BuildingKind.Pharmacy: type = RoomType.Pharmacy; shelf = "pharmacy"; break;
                case BuildingKind.Hardware: type = RoomType.Hardware; shelf = "hardware"; floor = Ground.Concrete; break;
                case BuildingKind.Diner: type = RoomType.Diner; shelf = "diner"; break;
                case BuildingKind.GasStation: type = RoomType.GasStation; shelf = "gas"; break;
                case BuildingKind.Police: type = RoomType.Police; shelf = "office"; break;
                case BuildingKind.Clothing: type = RoomType.Clothing; shelf = "clothing"; break;
                default: type = RoomType.Office; shelf = "office"; floor = Ground.WoodFloor; break;
            }
            int back = D >= 11 ? Math.Max(4, D / 4) : 0;
            var floorRoom = AddRoom(b, L.R(0, 0, W, D - back), type, floor);
            Room storage = null;
            if (back > 0)
            {
                storage = AddRoom(b, L.R(0, D - back, W, back), kind == BuildingKind.Police ? RoomType.Police : kind == BuildingKind.Diner ? RoomType.Kitchen : RoomType.Storage, Ground.Concrete);
                InteriorWallAcross(b, L, D - back, 0, W, rng.Range(1, W - 1));
                BackDoor(b, L, rng.Range(1, W - 1), D, doors);
            }
            int door = W / 2;
            FrontDoor(b, L, door, doors, false); if (W >= 14) FrontDoor(b, L, door - 1, doors, false);
            // Aisles of shelving parallel to the storefront.
            if (kind == BuildingKind.Diner)
            {
                for (int v = 3; v < D - back - 2; v += 3) for (int u = 2; u < W - 2; u += 3) if (Math.Abs(u - door) > 1) Place(b, floorRoom, L, u, v, PropKind.Table, "");
                for (int u = 2; u < W - 2; u++) if (Math.Abs(u - door) > 2 && u % 4 != 0) Place(b, floorRoom, L, u, D - back - 1, PropKind.Counter, "diner");
            }
            else if (kind == BuildingKind.Police || kind == BuildingKind.Office)
            {
                for (int v = 3; v < D - back - 1; v += 3) for (int u = 2; u < W - 2; u += 4) Place(b, floorRoom, L, u, v, PropKind.Desk, "office");
            }
            else
            {
                for (int v = 4; v < D - back - 2; v += 3)
                    for (int u = 2; u < W - 2; u++)
                        if (Math.Abs(u - door) > 1 && u != W / 4 && u != W * 3 / 4) Place(b, floorRoom, L, u, v, PropKind.Shelf, shelf);
                Place(b, floorRoom, L, door + 2 < W - 1 ? door + 2 : 1, 1, PropKind.Cashier, "cashier");
                if (kind == BuildingKind.Grocery || kind == BuildingKind.GasStation) Furnish(floorRoom, PropKind.Fridge, kind == BuildingKind.Grocery ? "grocery_cold" : "gas_cold", kind == BuildingKind.Grocery ? 3 : 1);
            }
            if (storage != null)
            {
                if (kind == BuildingKind.Police) Furnish(storage, PropKind.Locker, "police", rng.Range(2, 4));
                else if (kind == BuildingKind.Diner) { Furnish(storage, PropKind.Stove, "stove", 1); Furnish(storage, PropKind.Fridge, "fridge", 2); Furnish(storage, PropKind.Sink, "", 1); Furnish(storage, PropKind.Counter, "diner", 2); }
                else Furnish(storage, PropKind.Crate, shelf, rng.Range(2, 5));
                if (rng.Chance(.6f)) Furnish(storage, PropKind.Toilet, "", 1);
            }
        }
        void Place(Building b, Room room, Local L, int u, int v, PropKind kind, string table)
        {
            var t = L.T(u, v);
            if (!room.rect.Contains(t.x, t.y) || map.PropAt(t.x, t.y) != null || NearDoor(t.x, t.y)) return;
            var p = map.AddProp(kind, t.x, t.y, 1, 1, (int)L.f);
            if (table.Length > 0) map.AddContainer(p, table, kind == PropKind.Fridge);
        }

        bool NearDoor(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                {
                    int tx = x + dx, ty = y + dy;
                    var o = map.OpeningAt(tx, ty, true) ?? map.OpeningAt(tx + 1, ty, true) ?? map.OpeningAt(tx, ty, false) ?? map.OpeningAt(tx, ty + 1, false);
                    if (o != null && o.kind == Edge.Door && Math.Abs(dx) + Math.Abs(dy) <= 1) return true;
                }
            return false;
        }

        /// <summary>Places furniture against walls of a room, keeping doorways clear.</summary>
        void Furnish(Room room, PropKind kind, string table, int count)
        {
            var r = room.rect; var spots = new List<Int2>();
            for (int x = r.x; x < r.MaxX; x++)
                for (int y = r.y; y < r.MaxY; y++)
                {
                    bool wall = map.V(x, y) == Edge.Wall || map.V(x + 1, y) == Edge.Wall || map.H(x, y) == Edge.Wall || map.H(x, y + 1) == Edge.Wall;
                    if (!wall || map.PropAt(x, y) != null || NearDoor(x, y)) continue;
                    // Do not seal a room's only path: keep a one-tile margin from interior openings both sides.
                    spots.Add(new Int2(x, y));
                }
            for (int i = 0; i < count && spots.Count > 0; i++)
            {
                int k = rng.Range(0, spots.Count); var s = spots[k]; spots.RemoveAt(k);
                int rot = map.H(s.x, s.y) == Edge.Wall ? 0 : map.V(s.x + 1, s.y) == Edge.Wall ? 1 : map.H(s.x, s.y + 1) == Edge.Wall ? 2 : 3;
                var p = map.AddProp(kind, s.x, s.y, 1, 1, rot);
                if (table.Length > 0) map.AddContainer(p, table, kind == PropKind.Fridge);
                if (kind == PropKind.Toilet) p.water = 12;
                if (kind == PropKind.Sink) p.water = 4;
            }
        }

        void Windows(Building b, Local L, int W, int D, List<Int2> doors, float chance)
        {
            Action<Int2, Int2, int> consider = (inside, outside, index) =>
            {
                if (doors.Any(d => d.Equals(inside))) return;
                Opening existing; map.Between(inside.x, inside.y, outside.x, outside.y, out existing); if (existing != null) return;
                if (index % 3 == 1 && rng.Chance(chance)) { var o = AddOpening(inside, outside, Edge.Window, b.id, true); o.open = false; o.locked = rng.Chance(.5f); }
            };
            for (int u = 1; u < W - 1; u++) { consider(L.T(u, 0), L.T(u, -1), u); consider(L.T(u, D - 1), L.T(u, D), u + 1); }
            for (int v = 1; v < D - 1; v++) { consider(L.T(0, v), L.T(-1, v), v); consider(L.T(W - 1, v), L.T(W, v), v + 2); }
        }

        // ---------------------------------------------------------------- nature and vehicles
        void Forests()
        {
            for (int y = 2; y < H - 2; y++)
                for (int x = 2; x < W - 2; x++)
                {
                    int i = map.Index(x, y);
                    if (map.ground[i] != Ground.Grass && map.ground[i] != Ground.Dirt) continue;
                    if (map.roomAt[i] >= 0 || map.blockers.ContainsKey(i)) continue;
                    if (noTrees[x / Map.Cell + y / Map.Cell * map.CellsX]) continue;
                    var z = map.ZoneAt(x, y);
                    float density = z == ZoneKind.Forest ? .085f : z == ZoneKind.Wild ? .006f : z == ZoneKind.Farm ? .002f : z == ZoneKind.Suburb ? .004f : 0;
                    if (density <= 0 || Hash(x, y, 77) > density) continue;
                    if (NearEdge(x, y)) continue;
                    var t = map.AddProp(Hash(x, y, 78) < .25f ? PropKind.Bush : PropKind.Tree, x, y);
                    t.rotation = (int)(Hash(x, y, 79) * 4);
                }
        }
        bool NearEdge(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                {
                    var g = map.GroundAt(x + dx, y + dy);
                    if (g == Ground.Road || g == Ground.Gravel || g == Ground.Sidewalk || g == Ground.Parking || map.roomAt[map.Index(MathUtil.Clamp(x + dx, 0, W - 1), MathUtil.Clamp(y + dy, 0, H - 1))] >= 0) return true;
                }
            return map.H(x, y) != Edge.None || map.V(x, y) != Edge.None || map.H(x, y + 1) != Edge.None || map.V(x + 1, y) != Edge.None;
        }
        void Cars()
        {
            // Abandoned cars on the primary roads (evacuation traffic) and parked along town streets.
            foreach (var road in roadsY)
                for (int i = 0; i < W / 12; i++)
                {
                    int x = rng.Range(20, W - 20), y = road.x + (rng.Chance(.5f) ? -road.y + 1 : 1);
                    if (bridge.Contains(x, y) || road.x == H / 2 && Math.Abs(x - evacuationPoint.x) < 60) continue;
                    TryCar(x, y, true);
                }
            foreach (var road in roadsX)
                for (int i = 0; i < H / 14; i++) TryCar(road.x + (rng.Chance(.5f) ? -2 : 1), rng.Range(20, H - 20), false);
            int parked = 0;
            for (int guard = 0; guard < W * 2 && parked < W / 5; guard++)
            {
                int x = rng.Range(10, W - 10), y = rng.Range(10, H - 10);
                var z = map.ZoneAt(x, y); if (z != ZoneKind.Town && z != ZoneKind.Commercial && z != ZoneKind.Suburb) continue;
                if (map.GroundAt(x, y) == Ground.Parking || map.GroundAt(x, y) == Ground.Road && map.GroundAt(x, y + 3) == Ground.Sidewalk) { if (TryCar(x, y, true) != null) parked++; }
            }
        }
        Prop TryCar(int x, int y, bool horizontal)
        {
            int w = horizontal ? 4 : 2, h = horizontal ? 2 : 4;
            for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) { var g = map.GroundAt(x + i, y + j); if (map.TileBlocked(x + i, y + j) || g == Ground.Bridge || map.RoomAt(x + i, y + j) != null) return null; }
            var car = map.AddProp(PropKind.Car, x, y, w, h, horizontal ? (rng.Chance(.5f) ? 1 : 3) : (rng.Chance(.5f) ? 0 : 2));
            car.hp = rng.Float();   // view tint / wreck state
            map.AddContainer(car, "car");
            return car;
        }

        /// <summary>Every settlement is a town landmark; specialised landmarks are added by GenLandmarks.cs. Town landmarks come first.</summary>
        void DefaultLandmarks()
        {
            var towns = new List<Landmark>();
            foreach (var t in settlements)
            {
                var lm = new Landmark { name = t.name, kind = LandmarkKind.Town, x = t.x, y = t.y, radius = t.radius, rect = new Rect(t.x - t.radius, t.y - t.radius, t.radius * 2, t.radius * 2) };
                lm.description = t == settlements[0] ? "County seat" : t.radius >= 170 ? "Town" : "Small town";
                foreach (var b in map.buildings) if (Vec2.Distance(new Vec2(b.rect.x, b.rect.y), new Vec2(t.x, t.y)) < t.radius) lm.buildings.Add(b.id);
                towns.Add(lm);
            }
            landmarks.InsertRange(0, towns);
        }

        /// <summary>The landmark nearest to p (distance to its bounds), optionally skipping towns. Null when there are none.</summary>
        public Landmark NearestLandmark(Vec2 p, bool includeTowns = true)
        {
            Landmark best = null; float bestD = float.MaxValue;
            foreach (var l in landmarks)
            {
                if (!includeTowns && l.kind == LandmarkKind.Town) continue;
                float d = l.rect.DistanceTo(p); if (d < bestD) { bestD = d; best = l; }
            }
            return best;
        }
        /// <summary>The first landmark of a kind, or null (consumers must tolerate a generator that did not place one).</summary>
        public Landmark Find(LandmarkKind kind) { foreach (var l in landmarks) if (l.kind == kind) return l; return null; }
        /// <summary>True inside Fort Ashby or the Red Talon Yard (never a home site).</summary>
        public bool InHostileLandmark(Vec2 p, float margin = 0)
        {
            foreach (var l in landmarks) if ((l.kind == LandmarkKind.MilitaryBase || l.kind == LandmarkKind.RaiderCompound) && l.Contains(p, margin)) return true;
            return false;
        }

        /// <summary>Home site for a new community: a Farmhouse, House, Church or Warehouse with at least three rooms near the chosen
        /// landing zone (random zone when -1), never inside Fort Ashby or the Red Talon Yard.</summary>
        public static Building StartSite(Map map, WorldGen gen, Rng rng, int landingZone)
        {
            if (gen == null || gen.landingZones.Count == 0) return StartHouse(map, rng);
            var zone = gen.landingZones[landingZone >= 0 && landingZone < gen.landingZones.Count ? landingZone : rng.Range(0, gen.landingZones.Count)];
            var c = zone.Center;
            var homes = map.buildings.Where(b => HomeCandidate(map, gen, b)).OrderBy(b => Vec2.Distance(b.rect.Center, c)).Take(4).ToList();
            return homes.Count == 0 ? StartHouse(map, rng) : homes[rng.Range(0, homes.Count)];
        }
        public static bool HomeCandidate(Map map, WorldGen gen, Building b)
        {
            if (b.kind != BuildingKind.House && b.kind != BuildingKind.Farmhouse && b.kind != BuildingKind.Church && b.kind != BuildingKind.Warehouse) return false;
            if (b.rooms.Count < 3) return false;
            return gen == null || !gen.InHostileLandmark(b.rect.Center, 12);
        }
        public static Building StartHouse(Map map, Rng rng)
        {
            var homes = map.buildings.Where(b => b.kind == BuildingKind.House && map.ZoneAt(b.rect.x, b.rect.y) == ZoneKind.Suburb).ToList();
            if (homes.Count == 0) homes = map.buildings.Where(b => b.kind == BuildingKind.House || b.kind == BuildingKind.Farmhouse).ToList();
            if (homes.Count == 0) return map.buildings.FirstOrDefault();
            return homes[rng.Range(0, homes.Count)];
        }
    }
}
