using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    public enum Facing { North, East, South, West }

    [Serializable]
    public sealed class Settlement { public string name = ""; public int x, y, radius; public ZoneKind zone; }

    /// <summary>Seeded generator for the whole county: river, highway, towns, farms, forests, buildings and furniture.</summary>
    public sealed class WorldGen
    {
        public const int Version = 1;
        readonly Map map; readonly Rng rng; readonly int seed;
        public readonly List<Settlement> settlements = new List<Settlement>();
        public Rect bridge;
        public Vec2 evacuationPoint;
        readonly List<Rect> reserved = new List<Rect>();

        public static Map Generate(int seed, int size, out WorldGen gen)
        {
            var map = new Map(size, size); gen = new WorldGen(map, seed); gen.Run(); return map;
        }
        WorldGen(Map map, int seed) { this.map = map; this.seed = seed; rng = new Rng((ulong)seed, 7); }
        int W { get { return map.width; } }
        int H { get { return map.height; } }

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
            if (!overrideTown && (current == ZoneKind.Town || current == ZoneKind.Commercial) && z != ZoneKind.Commercial) return;
            map.zone[i] = (byte)z;
        }

        int RiverX(int y) { return (int)(W * .3f + Math.Sin(y / 260.0 + seed % 7) * 70 + Math.Sin(y / 83.0) * 18); }

        void Run()
        {
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
            // A pond in the north-east hills.
            Pond(new Vec2(W * .78f, H * .82f), 38);
            Pond(new Vec2(W * .12f, H * .78f), 24);

            // Primary roads: the county highway (east-west) and Route 9 (north-south, east of the river).
            int highwayY = H / 2, routeX = (int)(W * .62f);
            Road(0, highwayY - 4, W, 8, true, Ground.Road);
            Road(routeX - 3, 0, 6, H, false, Ground.Road);
            int bx = RiverX(highwayY);
            bridge = new Rect(bx - 16, highwayY - 5, 32, 10);
            for (int x = bridge.x; x < bridge.MaxX; x++) for (int y = highwayY - 4; y < highwayY + 4; y++) if (map.InBounds(x, y)) map.ground[map.Index(x, y)] = Ground.Bridge;
            for (int x = bridge.x; x < bridge.MaxX; x++) { map.SetH(x, highwayY - 4, Edge.Fence); map.SetH(x, highwayY + 4, Edge.Fence); }
            evacuationPoint = new Vec2(bx - 30, highwayY);
            Fill(new Rect(bx - 46, highwayY + 5, 26, 14), Ground.Parking);
            reserved.Add(new Rect(bx - 50, highwayY - 20, 70, 40));

            // Settlements scale with the county: a county seat on the crossroads plus towns and hamlets.
            float scale = W / 1024f;
            Town("Cedar Fork", routeX, highwayY, (int)(170 + 70 * scale), true);
            var names = new[] { "Harlan Springs", "Millbrook", "Pine Hollow", "Riverside", "Dunmore", "Elk Ridge", "Fairhaven", "Grant's Mill", "Hollis", "Ironwood", "Juniper Flats", "Kettle Creek", "Lowell", "Marrow Bend", "Northgate", "Oak Valley" };
            int wanted = Math.Min(names.Length, (int)(4 * scale * scale * .6f) + 3);
            for (int i = 0, n = 0; i < 400 && n < wanted; i++)
            {
                int radius = rng.Range(80, (int)(120 + 40 * scale));
                int x = rng.Range(radius + 30, W - radius - 30), y = rng.Range(radius + 30, H - radius - 30);
                if (Math.Abs(x - RiverX(y)) < radius + 30) continue;
                if (settlements.Any(t => Vec2.Distance(new Vec2(t.x, t.y), new Vec2(x, y)) < t.radius + radius + 90)) continue;
                Town(names[n++], x, y, radius, false);
            }
            // County roads joining every settlement to the primary roads.
            foreach (var s in settlements.Skip(1)) ConnectToRoads(s.x, s.y, highwayY, routeX);
            // Farms throughout the countryside west of the river and along the south.
            int farms = (int)(W / 30 * scale);
            for (int i = 0; i < farms * 6 && settlements.Count > 0 && farmCount < farms; i++)
                Farm(rng.Range(60, W - 60), rng.Range(60, H - 60));
            // Rural homes along the highway and Route 9.
            for (int i = 0; i < (int)(W / 9 * scale); i++)
            {
                if (rng.Chance(.5f)) { int x = rng.Range(40, W - 40); RoadsideLot(x, highwayY + (rng.Chance(.5f) ? 6 : -6), rng.Chance(.5f) ? Facing.South : Facing.North); }
                else { int y = rng.Range(40, H - 40); RoadsideLot(routeX + (rng.Chance(.5f) ? 5 : -5), y, rng.Chance(.5f) ? Facing.West : Facing.East); }
            }
            Forests();
            Cars(highwayY, routeX);
        }

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
                    if (map.ground[idx] == Ground.Bridge || map.roomAt[idx] >= 0) continue;
                    if (g == Ground.Gravel && map.ground[idx] == Ground.Road) continue;
                    map.ground[idx] = g;
                }
            reserved.Add(new Rect(r.x - 1, r.y - 1, r.w + 2, r.h + 2));
        }
        void ConnectToRoads(int x, int y, int highwayY, int routeX)
        {
            // L-shaped county road: first horizontally toward Route 9 if east of the river, then vertically to the highway.
            int riverHere = RiverX(y);
            bool east = x > riverHere;
            int targetX = east ? routeX : x;
            if (east && Math.Abs(targetX - x) > 4) Road(Math.Min(x, targetX), y - 3, Math.Abs(targetX - x) + 3, 6, true, Ground.Road);
            else Road(x - 3, Math.Min(y, highwayY), 6, Math.Abs(highwayY - y), false, Ground.Road);
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
            if (!map.InBounds(x, y) || map.ZoneAt(x, y) == ZoneKind.Town || map.ZoneAt(x, y) == ZoneKind.Commercial) return;
            int w = rng.Range(9, 13), d = rng.Range(8, 11);
            int gap = 3;
            var r = front == Facing.South ? new Rect(x, y + gap, w, d) : front == Facing.North ? new Rect(x, y - gap - d, w, d) : front == Facing.West ? new Rect(x + gap, y, d, w) : new Rect(x - gap - d, y, d, w);
            if (!Free(r, 3)) return;
            Construct(r, rng.Chance(.2f) ? BuildingKind.GasStation : BuildingKind.House, front);
            for (int cx = r.x; cx < r.MaxX; cx += Map.Cell) for (int cy = r.y; cy < r.MaxY; cy += Map.Cell) SetCellZone(cx, cy, ZoneKind.Suburb);
        }

        int farmCount;
        void Farm(int x, int y)
        {
            var area = new Rect(x - 40, y - 34, 80, 68);
            if (!Free(area, 6) || AnyWater(area) || map.ZoneAt(x, y) == ZoneKind.Town || map.ZoneAt(x, y) == ZoneKind.Commercial) return;
            if (settlements.Any(s => Vec2.Distance(new Vec2(s.x, s.y), new Vec2(x, y)) < s.radius + 60)) return;
            farmCount++;
            for (int cx = area.x; cx < area.MaxX; cx += Map.Cell) for (int cy = area.y; cy < area.MaxY; cy += Map.Cell) SetCellZone(cx, cy, ZoneKind.Farm);
            var house = new Rect(x - 30, y - 26, rng.Range(11, 15), rng.Range(10, 13));
            Construct(house, BuildingKind.Farmhouse, Facing.East);
            var barn = new Rect(x + 6, y - 28, rng.Range(13, 17), rng.Range(16, 20));
            Construct(barn, BuildingKind.Barn, Facing.West);
            // Fields with rows of crops, fenced.
            var field = new Rect(x - 34, y + 2, 66, 28);
            for (int i = field.x; i < field.MaxX; i++) for (int j = field.y; j < field.MaxY; j++) if (map.InBounds(i, j)) map.ground[map.Index(i, j)] = Ground.Field;
            for (int i = field.x + 1; i < field.MaxX - 1; i += 2) for (int j = field.y + 1; j < field.MaxY - 1; j += 3) if (rng.Chance(.55f)) map.AddProp(PropKind.Crops, i, j);
            for (int i = field.x; i < field.MaxX; i++) { if (i % 9 != 4) { map.SetH(i, field.y, Edge.Fence); map.SetH(i, field.MaxY, Edge.Fence); } }
            for (int j = field.y; j < field.MaxY; j++) { if (j % 9 != 4) { map.SetV(field.x, j, Edge.Fence); map.SetV(field.MaxX, j, Edge.Fence); } }
            reserved.Add(area);
            // Gravel drive to the nearest primary road, axis aligned.
            int highwayY = H / 2;
            int driveX = area.x + 2;
            Road(driveX - 2, Math.Min(y, highwayY), 4, Math.Abs(highwayY - y), false, Ground.Gravel);
        }

        bool Free(Rect r, int margin)
        {
            if (r.x < 4 || r.y < 4 || r.MaxX > W - 4 || r.MaxY > H - 4) return false;
            foreach (var b in reserved) if (b.Overlaps(r, margin)) return false;
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
                default: return "House";
            }
        }

        void Construct(Rect r, BuildingKind kind, Facing front)
        {
            if (!Free(r, kind == BuildingKind.Shed ? 0 : 1)) return;
            reserved.Add(r);
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
                default: StoreLayout(b, kind, L, W, D, doors); break;
            }
            Windows(b, L, W, D, doors, kind == BuildingKind.Barn || kind == BuildingKind.Shed ? .2f : kind == BuildingKind.House || kind == BuildingKind.Farmhouse ? .65f : .9f);
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
        void Cars(int highwayY, int routeX)
        {
            // Abandoned cars on the highway (evacuation traffic) and parked along town streets.
            for (int i = 0; i < W / 12; i++)
            {
                int x = rng.Range(20, W - 20), y = highwayY + (rng.Chance(.5f) ? -3 : 1);
                if (bridge.Contains(x, y) || Math.Abs(x - evacuationPoint.x) < 60) continue;
                TryCar(x, y, true);
            }
            for (int i = 0; i < H / 14; i++) TryCar(routeX + (rng.Chance(.5f) ? -2 : 1), rng.Range(20, H - 20), false);
            int parked = 0;
            for (int guard = 0; guard < W * 2 && parked < W / 5; guard++)
            {
                int x = rng.Range(10, W - 10), y = rng.Range(10, H - 10);
                var z = map.ZoneAt(x, y); if (z != ZoneKind.Town && z != ZoneKind.Commercial && z != ZoneKind.Suburb) continue;
                if (map.GroundAt(x, y) == Ground.Parking || map.GroundAt(x, y) == Ground.Road && map.GroundAt(x, y + 3) == Ground.Sidewalk) { if (TryCar(x, y, true)) parked++; }
            }
        }
        bool TryCar(int x, int y, bool horizontal)
        {
            int w = horizontal ? 4 : 2, h = horizontal ? 2 : 4;
            for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) { var g = map.GroundAt(x + i, y + j); if (map.TileBlocked(x + i, y + j) || g == Ground.Bridge || map.RoomAt(x + i, y + j) != null) return false; }
            var car = map.AddProp(PropKind.Car, x, y, w, h, horizontal ? (rng.Chance(.5f) ? 1 : 3) : (rng.Chance(.5f) ? 0 : 2));
            car.hp = rng.Float();   // view tint / wreck state
            map.AddContainer(car, "car");
            return true;
        }

        /// <summary>A safe-ish starting house away from the busiest streets.</summary>
        public static Building StartHouse(Map map, Rng rng)
        {
            var homes = map.buildings.Where(b => b.kind == BuildingKind.House && map.ZoneAt(b.rect.x, b.rect.y) == ZoneKind.Suburb).ToList();
            if (homes.Count == 0) homes = map.buildings.Where(b => b.kind == BuildingKind.House || b.kind == BuildingKind.Farmhouse).ToList();
            if (homes.Count == 0) return map.buildings.FirstOrDefault();
            return homes[rng.Range(0, homes.Count)];
        }
    }
}
