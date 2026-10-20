using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    public enum Ground : byte { Grass, Dirt, Road, Sidewalk, Water, WoodFloor, TileFloor, Concrete, Field, Sand, Parking, Bridge, Gravel,
        Runway, Mud, MilitaryFloor }   // appended for the open county; views must render unknown values with a neutral fallback
    public enum Edge : byte { None, Wall, Window, Door, Fence }
    public enum PropKind : byte
    {
        Tree, Bush, Counter, Fridge, Stove, Sink, Toilet, MedicineCabinet, Bed, Couch, Dresser, Wardrobe, Shelf, Crate,
        Desk, Table, Bin, Toolbox, Workbench, Car, GasPump, HayBale, Campfire, RainCollector, WoodCrate, Cashier, Locker, Mailbox, Crops, FarmPlot,
        // Open-county additions (appended; byte values of earlier kinds are stable).
        SupplyCrate, MilitaryCrate, Sandbags, Barrier, Tent, Watchtower, GardenBed, MedicalBed, RadioSet, Generator, OutpostFlag, AmmoBox, Bunk
    }
    public enum RoomType : byte { Living, Kitchen, Bedroom, Bathroom, Garage, Grocery, Pharmacy, Hardware, Diner, GasStation, Police, Barn, Storage, Office, Clothing }
    public enum BuildingKind : byte { House, Farmhouse, Barn, Grocery, Pharmacy, Hardware, Diner, GasStation, Police, Clothing, Shed, Office,
        Warehouse, Church, School, Hospital, Barracks, Armory, Hangar, RadioStation, Garage, Motel, Compound }

    /// <summary>A door or window on a tile edge. State is authoritative on the host.</summary>
    [Serializable]
    public sealed class Opening
    {
        public int id, x, y; public bool vertical; public Edge kind;
        public bool open, broken, locked, glassCleared, exterior;
        public int barricade;              // 0..4 plank layers
        public float hp = 1, barricadeHp;  // door/window frame health; barricade health
        public int building = -1;
        public Vec2 Center { get { return vertical ? new Vec2(x, y + .5f) : new Vec2(x + .5f, y); } }
        public bool BlocksMovement { get { return barricade > 0 || kind == Edge.Window || kind == Edge.Door && !open && !broken; } }
        public bool BlocksSight { get { return barricade >= 2 || kind == Edge.Door && !open && !broken; } }
        /// <summary>Zombies and survivors may climb through open or broken windows.</summary>
        public bool Climbable { get { return kind == Edge.Window && barricade == 0 && (open || broken); } }
    }

    [Serializable]
    public sealed class Prop
    {
        public int id, x, y, w = 1, h = 1, rotation, container = -1, building = -1, room = -1;
        public PropKind kind;
        public float fuel, water, hp = 1;   // campfire fuel hours; stored water units; tree health
        public bool lit, removed, modified;
        public bool Blocks { get { return !removed && kind != PropKind.Crops && kind != PropKind.FarmPlot && kind != PropKind.Campfire && kind != PropKind.Bush && kind != PropKind.Mailbox; } }
        public bool IsHeat { get { return kind == PropKind.Stove || kind == PropKind.Campfire; } }
        public bool IsWaterSource { get { return kind == PropKind.Sink || kind == PropKind.Toilet || kind == PropKind.RainCollector; } }
        public bool CanSleep { get { return kind == PropKind.Bed || kind == PropKind.Couch; } }
        public Vec2 Center { get { return new Vec2(x + w * .5f, y + h * .5f); } }
    }

    [Serializable]
    public sealed class Container
    {
        public int id, prop = -1, room = -1;
        public string table = "";
        public bool generated, cold;
        public Inventory inventory = new Inventory();
    }

    [Serializable]
    public sealed class Room { public int id, building; public Rect rect; public RoomType type; }

    [Serializable]
    public sealed class Building
    {
        public int id; public BuildingKind kind; public Rect rect; public string name = "";
        public List<int> rooms = new List<int>();
        public bool visited;
    }

    /// <summary>Tile grid with ground, wall edges, openings and props. One tile is one meter.</summary>
    public sealed class Map
    {
        public readonly int width, height;
        public Ground[] ground;
        public Edge[] hEdges;  // edge along +X at the boundary y; index x + y*width, y in 0..height
        public Edge[] vEdges;  // edge along +Y at the boundary x; index x + y*(width+1), x in 0..width
        public const int Cell = 8;           // coarse cell size for zones and exploration
        public readonly Dictionary<int, int> blockers = new Dictionary<int, int>(); // tile index -> blocking prop id (sparse)
        public int[] roomAt;                 // room id per tile, -1 outdoors
        public byte[] zone;                  // ZoneKind per coarse cell
        public bool[] explored;              // per coarse cell
        public int CellsX { get { return (width + Cell - 1) / Cell; } }
        public int CellsY { get { return (height + Cell - 1) / Cell; } }
        public readonly List<Opening> openings = new List<Opening>();
        public readonly List<Prop> props = new List<Prop>();
        public readonly List<Container> containers = new List<Container>();
        public readonly List<Room> rooms = new List<Room>();
        public readonly List<Building> buildings = new List<Building>();
        readonly Dictionary<long, int> openingAt = new Dictionary<long, int>();

        public Map(int width, int height)
        {
            this.width = width; this.height = height;
            ground = new Ground[width * height]; hEdges = new Edge[width * (height + 1)]; vEdges = new Edge[(width + 1) * height];
            roomAt = new int[width * height]; zone = new byte[CellsX * CellsY]; explored = new bool[CellsX * CellsY];
            for (int i = 0; i < roomAt.Length; i++) roomAt[i] = -1;
        }

        public bool InBounds(int x, int y) { return x >= 0 && y >= 0 && x < width && y < height; }
        public int Index(int x, int y) { return x + y * width; }
        public Ground GroundAt(int x, int y) { return InBounds(x, y) ? ground[Index(x, y)] : Ground.Water; }
        public Room RoomAt(int x, int y) { if (!InBounds(x, y)) return null; int r = roomAt[Index(x, y)]; return r < 0 ? null : rooms[r]; }
        public Room RoomAt(Vec2 p) { return RoomAt((int)Math.Floor(p.x), (int)Math.Floor(p.y)); }
        public Building BuildingAt(Vec2 p) { var r = RoomAt(p); return r == null ? null : buildings[r.building]; }
        public Prop PropAt(int x, int y) { int p; return InBounds(x, y) && blockers.TryGetValue(Index(x, y), out p) ? props[p] : null; }
        public ZoneKind ZoneAt(int x, int y) { return InBounds(x, y) ? (ZoneKind)zone[x / Cell + y / Cell * CellsX] : ZoneKind.Water; }
        public void Explore(Vec2 p, float radius)
        {
            int r = (int)Math.Ceiling(radius / Cell), cx = (int)p.x / Cell, cy = (int)p.y / Cell;
            for (int x = cx - r; x <= cx + r; x++) for (int y = cy - r; y <= cy + r; y++)
                if (x >= 0 && y >= 0 && x < CellsX && y < CellsY && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) explored[x + y * CellsX] = true;
        }
        public bool IsWater(int x, int y) { return GroundAt(x, y) == Ground.Water; }

        static long Key(int x, int y, bool vertical) { return ((long)(vertical ? 1 : 0) << 62) | ((long)(uint)x << 31) | (uint)y; }
        public Edge H(int x, int y) { return x < 0 || x >= width || y < 0 || y > height ? Edge.Wall : hEdges[x + y * width]; }
        public Edge V(int x, int y) { return x < 0 || x > width || y < 0 || y >= height ? Edge.Wall : vEdges[x + y * (width + 1)]; }
        public void SetH(int x, int y, Edge e) { if (x >= 0 && x < width && y >= 0 && y <= height) hEdges[x + y * width] = e; }
        public void SetV(int x, int y, Edge e) { if (x >= 0 && x <= width && y >= 0 && y < height) vEdges[x + y * (width + 1)] = e; }
        public Opening OpeningAt(int x, int y, bool vertical) { int id; return openingAt.TryGetValue(Key(x, y, vertical), out id) ? openings[id] : null; }

        public Opening AddOpening(int x, int y, bool vertical, Edge kind, int building, bool exterior)
        {
            var existing = OpeningAt(x, y, vertical); if (existing != null) return existing;
            if (vertical) SetV(x, y, kind); else SetH(x, y, kind);
            var o = new Opening { id = openings.Count, x = x, y = y, vertical = vertical, kind = kind, building = building, exterior = exterior, open = kind == Edge.Door && !exterior };
            openings.Add(o); openingAt[Key(x, y, vertical)] = o.id; return o;
        }
        public void RebuildOpeningIndex() { openingAt.Clear(); foreach (var o in openings) openingAt[Key(o.x, o.y, o.vertical)] = o.id; }

        public Prop AddProp(PropKind kind, int x, int y, int w = 1, int h = 1, int rotation = 0)
        {
            var p = new Prop { id = props.Count, kind = kind, x = x, y = y, w = w, h = h, rotation = rotation };
            if (InBounds(x, y)) { var room = RoomAt(x, y); if (room != null) { p.room = room.id; p.building = room.building; } }
            props.Add(p);
            if (p.Blocks) for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) if (InBounds(x + i, y + j)) blockers[Index(x + i, y + j)] = p.id;
            return p;
        }
        public void RemoveProp(Prop p)
        {
            p.removed = true;
            int owner;
            for (int i = 0; i < p.w; i++) for (int j = 0; j < p.h; j++) if (InBounds(p.x + i, p.y + j) && blockers.TryGetValue(Index(p.x + i, p.y + j), out owner) && owner == p.id) blockers.Remove(Index(p.x + i, p.y + j));
        }
        public Container AddContainer(Prop prop, string table, bool cold = false)
        {
            var c = new Container { id = containers.Count, prop = prop == null ? -1 : prop.id, room = prop == null ? -1 : prop.room, table = table, cold = cold };
            c.inventory.capacity = prop != null && (prop.kind == PropKind.Shelf || prop.kind == PropKind.Car || prop.kind == PropKind.WoodCrate) ? 60 : 40;
            containers.Add(c); if (prop != null) prop.container = c.id; return c;
        }

        /// <summary>Edge between orthogonally adjacent tiles a and b.</summary>
        public Edge Between(int ax, int ay, int bx, int by, out Opening opening)
        {
            opening = null;
            if (bx == ax + 1) { opening = OpeningAt(bx, ay, true); return V(bx, ay); }
            if (bx == ax - 1) { opening = OpeningAt(ax, ay, true); return V(ax, ay); }
            if (by == ay + 1) { opening = OpeningAt(ax, by, false); return H(ax, by); }
            if (by == ay - 1) { opening = OpeningAt(ax, ay, false); return H(ax, ay); }
            return Edge.None;
        }

        public bool EdgeBlocksMovement(Edge e, Opening o) { return o != null ? o.BlocksMovement : e == Edge.Wall || e == Edge.Fence || e == Edge.Window; }
        public bool EdgeBlocksSight(Edge e, Opening o) { return o != null ? o.BlocksSight : e == Edge.Wall; }

        public bool TileBlocked(int x, int y)
        {
            if (!InBounds(x, y)) return true;
            int i = Index(x, y); return ground[i] == Ground.Water || blockers.ContainsKey(i);
        }

        /// <summary>Circle against blocking tiles and edges, used by survivors and zombies.</summary>
        public bool Collides(Vec2 p, float r)
        {
            int minX = (int)Math.Floor(p.x - r), maxX = (int)Math.Floor(p.x + r), minY = (int)Math.Floor(p.y - r), maxY = (int)Math.Floor(p.y + r);
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                {
                    if (TileBlocked(x, y))
                    {
                        float cx = MathUtil.Clamp(p.x, x, x + 1), cy = MathUtil.Clamp(p.y, y, y + 1);
                        float dx = p.x - cx, dy = p.y - cy; if (dx * dx + dy * dy < r * r) return true;
                    }
                }
            // Vertical edges at integer x within reach.
            for (int x = (int)Math.Ceiling(p.x - r); x <= (int)Math.Floor(p.x + r); x++)
                for (int y = minY; y <= maxY; y++)
                {
                    var e = V(x, y); if (e == Edge.None) continue;
                    if (!EdgeBlocksMovement(e, OpeningAt(x, y, true))) continue;
                    float cy = MathUtil.Clamp(p.y, y, y + 1), dx = p.x - x, dy = p.y - cy; if (dx * dx + dy * dy < r * r) return true;
                }
            for (int y = (int)Math.Ceiling(p.y - r); y <= (int)Math.Floor(p.y + r); y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var e = H(x, y); if (e == Edge.None) continue;
                    if (!EdgeBlocksMovement(e, OpeningAt(x, y, false))) continue;
                    float cx = MathUtil.Clamp(p.x, x, x + 1), dx = p.x - cx, dy = p.y - y; if (dx * dx + dy * dy < r * r) return true;
                }
            return false;
        }

        /// <summary>Moves a circle with axis separation so actors slide along walls.</summary>
        public Vec2 Slide(Vec2 from, Vec2 delta, float r)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(delta.Length / (r * .5f)));
            Vec2 step = delta * (1f / steps), p = from;
            for (int i = 0; i < steps; i++)
            {
                var nx = new Vec2(p.x + step.x, p.y); if (!Collides(nx, r)) p = nx;
                var ny = new Vec2(p.x, p.y + step.y); if (!Collides(ny, r)) p = ny;
            }
            return p;
        }

        /// <summary>Grid traversal line of sight. Walls and closed doors block; windows and fences do not.</summary>
        public bool LineOfSight(Vec2 a, Vec2 b, bool throughProps = true)
        {
            int x = (int)Math.Floor(a.x), y = (int)Math.Floor(a.y), tx = (int)Math.Floor(b.x), ty = (int)Math.Floor(b.y);
            float dx = b.x - a.x, dy = b.y - a.y;
            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            float tMaxX = dx == 0 ? float.MaxValue : ((sx > 0 ? x + 1 - a.x : a.x - x) / Math.Abs(dx));
            float tMaxY = dy == 0 ? float.MaxValue : ((sy > 0 ? y + 1 - a.y : a.y - y) / Math.Abs(dy));
            float tDX = dx == 0 ? float.MaxValue : 1 / Math.Abs(dx), tDY = dy == 0 ? float.MaxValue : 1 / Math.Abs(dy);
            for (int guard = 0; guard < 512 && (x != tx || y != ty); guard++)
            {
                int nx = x, ny = y;
                if (tMaxX < tMaxY) { nx += sx; tMaxX += tDX; } else { ny += sy; tMaxY += tDY; }
                Opening o; var e = Between(x, y, nx, ny, out o);
                if (e != Edge.None && EdgeBlocksSight(e, o)) return false;
                x = nx; y = ny;
                if (!throughProps) { var p = PropAt(x, y); if (p != null && p.kind == PropKind.Tree && (x != tx || y != ty)) return false; }
            }
            return true;
        }

        public bool Indoors(Vec2 p) { return RoomAt(p) != null; }
    }

    public enum ZoneKind : byte { Wild, Forest, Farm, Suburb, Town, Commercial, Water, Military }
}
