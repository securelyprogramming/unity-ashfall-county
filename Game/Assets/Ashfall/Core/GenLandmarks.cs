using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>Specialised landmarks (Fort Ashby, the airfield, the hospital, the radio station, Reed's Garage, the Red Talon Yard,
    /// warehouses, churches, schools, motels, campgrounds and named farmsteads), their building layouts, and the drop-map landing zones.</summary>
    public sealed partial class WorldGen
    {
        readonly List<Rect> landmarkRects = new List<Rect>();
        readonly List<Landmark> farmSites = new List<Landmark>();
        bool[] noTrees;            // per map cell: landmark grounds stay clear of forest
        bool[] landmarkCell;       // per map cell: overlaps a landmark lot (roads never pave inside one)
        Landmark current;          // landmark whose buildings are being constructed

        bool NearLandmark(Rect r, int margin) { foreach (var l in landmarkRects) if (l.Overlaps(r, margin)) return true; return false; }
        bool InLandmark(int x, int y) { foreach (var l in landmarkRects) if (l.Contains(x, y)) return true; return false; }

        struct Site { public Rect r; public Facing gate; public int road; }
        static readonly Facing[] NorthSouth = { Facing.North, Facing.South }, EastWest = { Facing.East, Facing.West };
        static Int2 Outward(Facing f) { switch (f) { case Facing.North: return new Int2(0, 1); case Facing.South: return new Int2(0, -1); case Facing.East: return new Int2(1, 0); default: return new Int2(-1, 0); } }
        static Facing FacingOf(int dx, int dy) { return dx > 0 ? Facing.East : dx < 0 ? Facing.West : dy > 0 ? Facing.North : Facing.South; }
        /// <summary>World facing of a direction given in a lot's local (u along the gate side, v inward from it) coordinates.</summary>
        static Facing Dir(Local L, int du, int dv) { var a = L.T(0, 0); var b = L.T(du, dv); return FacingOf(b.x - a.x, b.y - a.y); }

        // ---------------------------------------------------------------- site search
        /// <summary>Searches for a free lot w wide (along its gate side) and d deep with a straight road link from the middle of the
        /// gate side to an existing road. The best scoring candidate (shorter roads score higher) wins.</summary>
        bool FindSite(int w, int d, int attempts, int maxRoad, Func<Int2> sample, Func<Rect, float> score, out Site best)
        {
            best = default(Site); float bestScore = float.MinValue; bool found = false;
            for (int i = 0; i < attempts; i++)
            {
                var c = sample();
                for (int o = 0; o < 2; o++)
                {
                    var r = o == 0 ? new Rect(c.x - w / 2, c.y - d / 2, w, d) : new Rect(c.x - d / 2, c.y - w / 2, d, w);
                    float sc = score(r);
                    if (found && sc <= bestScore) continue;
                    if (!SiteFree(r)) continue;
                    foreach (var f in o == 0 ? NorthSouth : EastWest)
                    {
                        int road = RoadRay(r, f, maxRoad); if (road < 0) continue;
                        float total = sc - road * .4f;
                        if (!found || total > bestScore) { found = true; bestScore = total; best = new Site { r = r, gate = f, road = road }; }
                    }
                }
            }
            return found;
        }
        bool SiteFree(Rect r)
        {
            if (r.x < 12 || r.y < 12 || r.MaxX > W - 12 || r.MaxY > H - 12) return false;
            if (ReservedOverlap(r, 3) || NearLandmark(r, 24)) return false;
            foreach (var s in settlements) if (r.DistanceTo(new Vec2(s.x, s.y)) < s.radius + 20) return false;
            var q = r.Expand(2);
            for (int x = q.x; x < q.MaxX; x++)
                for (int y = q.y; y < q.MaxY; y++)
                {
                    int i = map.Index(x, y); var g = map.ground[i];
                    if (g == Ground.Road || g == Ground.Water || g == Ground.Bridge || g == Ground.Sand || g == Ground.Gravel || map.roomAt[i] >= 0) return false;
                }
            return true;
        }
        /// <summary>Length of a straight road from just outside the middle of a side of r to the nearest road, or -1.</summary>
        int RoadRay(Rect r, Facing f, int max)
        {
            var L = new Local { r = r, f = f }; var p = L.T(L.Wd / 2, -1); var d = Outward(f);
            for (int i = 0; i <= max; i++)
            {
                int x = p.x + d.x * i, y = p.y + d.y * i;
                if (!map.InBounds(x, y)) return -1;
                int idx = map.Index(x, y); var g = map.ground[idx];
                if (g == Ground.Road || g == Ground.Bridge) return i;
                if (g == Ground.Water || map.roomAt[idx] >= 0 || InLandmark(x, y)) return -1;
            }
            return -1;
        }

        Int2 Anywhere() { return new Int2(rng.Range(80, W - 80), rng.Range(80, H - 80)); }
        Func<Int2> Around(Settlement s, float min, float max)
        {
            return () => { float a = rng.Range(0, 6.2832f), d = rng.Range(min, max); return new Int2((int)(s.x + Math.Cos(a) * d), (int)(s.y + Math.Sin(a) * d)); };
        }
        /// <summary>A point beside a random primary road, offset to the lot centre so the gate side faces the road.</summary>
        Func<Int2> BesideRoad(int halfDepth)
        {
            return () =>
            {
                int k = rng.Range(0, roadsY.Count + roadsX.Count); bool side = rng.Chance(.5f);
                if (k < roadsY.Count) { var road = roadsY[k]; int off = road.y + 4 + halfDepth; return new Int2(rng.Range(80, W - 80), road.x + (side ? off : -off)); }
                var rx = roadsX[k - roadsY.Count]; int o = rx.y + 4 + halfDepth; return new Int2(rx.x + (side ? o : -o), rng.Range(80, H - 80));
            };
        }
        /// <summary>Distance from r to the nearest town edge or landmark, capped.</summary>
        float Remote(Rect r, float cap)
        {
            float m = cap; var c = r.Center;
            foreach (var s in settlements) m = Math.Min(m, Vec2.Distance(c, new Vec2(s.x, s.y)) - s.radius);
            foreach (var l in landmarkRects) m = Math.Min(m, l.DistanceTo(c));
            return m;
        }
        float EdgeDistance(Rect r) { return Math.Min(Math.Min(r.x, W - r.MaxX), Math.Min(r.y, H - r.MaxY)); }

        // ---------------------------------------------------------------- building a landmark
        Local Begin(Site s, string name, LandmarkKind kind, ZoneKind zone, string description)
        {
            var c = s.r.Center;
            var lm = new Landmark { name = name, kind = kind, x = (int)c.x, y = (int)c.y, radius = (int)(Math.Sqrt(s.r.w * s.r.w + s.r.h * s.r.h) / 2), rect = s.r, description = description };
            landmarks.Add(lm); landmarkRects.Add(s.r); current = lm;
            for (int x = s.r.x / Map.Cell; x <= (s.r.MaxX - 1) / Map.Cell; x++) for (int y = s.r.y / Map.Cell; y <= (s.r.MaxY - 1) / Map.Cell; y++) landmarkCell[x + y * map.CellsX] = true;
            for (int x = s.r.x; x < s.r.MaxX; x += Map.Cell) for (int y = s.r.y; y < s.r.MaxY; y += Map.Cell) SetCellZone(x, y, zone, true);
            for (int x = s.r.x - 2; x < s.r.MaxX + 2; x++) for (int y = s.r.y - 2; y < s.r.MaxY + 2; y++) if (map.InBounds(x, y)) noTrees[x / Map.Cell + y / Map.Cell * map.CellsX] = true;
            return new Local { r = s.r, f = s.gate };
        }
        /// <summary>Lays the access road from the gate and reserves the lot.</summary>
        void Finish(Site s)
        {
            var L = new Local { r = s.r, f = s.gate }; var p = L.T(L.Wd / 2, -1); var d = Outward(s.gate);
            if (s.road > 0)
            {
                int x1 = p.x + d.x * s.road, y1 = p.y + d.y * s.road;
                if (d.x == 0) Road(p.x - 3, Math.Min(p.y, y1), 6, Math.Abs(y1 - p.y) + 1, false, Ground.Road);
                else Road(Math.Min(p.x, x1), p.y - 3, Math.Abs(x1 - p.x) + 1, 6, true, Ground.Road);
            }
            Reserve(s.r); current = null;
        }
        /// <summary>A building on the lot, front facing the local direction (du, dv). width runs along its front.</summary>
        Building Block(Local L, int u, int v, int width, int depth, int du, int dv, BuildingKind kind, string name)
        {
            var r = dv != 0 ? L.R(u, v, width, depth) : L.R(u, v, depth, width);
            var b = Construct(r, kind, Dir(L, du, dv));
            if (b != null) { if (name != null) b.name = name; if (current != null) current.buildings.Add(b.id); }
            return b;
        }
        /// <summary>An outdoor prop at lot-local (u, v), optionally a container.</summary>
        Prop Put(Local L, int u, int v, PropKind kind, string table = "")
        {
            var t = L.T(u, v);
            if (!map.InBounds(t.x, t.y) || map.TileBlocked(t.x, t.y) || map.RoomAt(t.x, t.y) != null) return null;
            var p = map.AddProp(kind, t.x, t.y, 1, 1, (int)L.f);
            if (table.Length > 0) map.AddContainer(p, table);
            return p;
        }
        /// <summary>Perimeter fence with a gate gap in the middle of the gate side.</summary>
        void Fence(Local L, int gapHalf)
        {
            int U = L.Wd, V = L.Dp;
            for (int u = 0; u < U; u++)
            {
                if (u < U / 2 - gapHalf || u >= U / 2 + gapHalf) SetEdge(L.T(u, 0), L.T(u, -1), Edge.Fence);
                SetEdge(L.T(u, V - 1), L.T(u, V), Edge.Fence);
            }
            for (int v = 0; v < V; v++) { SetEdge(L.T(0, v), L.T(-1, v), Edge.Fence); SetEdge(L.T(U - 1, v), L.T(U, v), Edge.Fence); }
        }
        /// <summary>Parks cars in rows on a pad (world rectangle). Returns the car props placed.</summary>
        List<Prop> ParkCars(Rect pad, int max)
        {
            var cars = new List<Prop>();
            if (pad.w >= pad.h)
            {
                for (int y = pad.y + 1; y + 2 <= pad.MaxY - 1; y += 4) for (int x = pad.x + 1; x + 4 <= pad.MaxX - 1 && cars.Count < max; x += 6) { var c = TryCar(x, y, true); if (c != null) cars.Add(c); }
            }
            else
            {
                for (int x = pad.x + 1; x + 2 <= pad.MaxX - 1; x += 4) for (int y = pad.y + 1; y + 4 <= pad.MaxY - 1 && cars.Count < max; y += 6) { var c = TryCar(x, y, false); if (c != null) cars.Add(c); }
            }
            return cars;
        }
        void Runners(List<Prop> cars) { foreach (var c in cars) c.hp = .45f + .55f * rng.Float(); }
        void Wrecks(List<Prop> cars) { foreach (var c in cars) c.hp = .05f + .1f * rng.Float(); }

        // ---------------------------------------------------------------- the big landmarks (placed before the towns)
        void MajorLandmarks()
        {
            FortAshby();
            RedTalonYard();
            Airfield();
            Hospital();
            RadioStation();
            ReedsGarage();
        }

        bool Search(int w, int d, int maxRoad, Func<Int2> sample, Func<Rect, float> score, out Site s)
        {
            // Strict pass first, then a wider one anywhere on the county with longer roads.
            return FindSite(w, d, 220, maxRoad, sample, score, out s) || FindSite(w, d, 400, maxRoad * 3, Anywhere, score, out s);
        }

        void FortAshby()
        {
            int w = scale >= 2 ? 128 : 108, d = scale >= 2 ? 100 : 86;
            Site s; if (!Search(w, d, (int)(160 + 60 * S), Anywhere, r => Remote(r, W * .4f) + Math.Min(EdgeDistance(r), 200) * .2f, out s)) return;
            var L = Begin(s, "Fort Ashby", LandmarkKind.MilitaryBase, ZoneKind.Military, "Abandoned National Guard base: barracks, an armory and the motor pool. Military crates and vehicles, but it is overrun.");
            int U = L.Wd, V = L.Dp;
            Fill(s.r, Ground.Gravel); Fence(L, 4);
            Fill(L.R(U / 2 - 18, 18, 36, V - 44), Ground.Concrete);   // parade ground
            int bw = (U - 16) / 2 - 2;
            Block(L, 6, V - 20, bw, 14, 0, -1, BuildingKind.Barracks, "Fort Ashby Barracks A");
            Block(L, U - 6 - bw, V - 20, bw, 14, 0, -1, BuildingKind.Barracks, "Fort Ashby Barracks B");
            Block(L, 5, 14, 24, 14, 1, 0, BuildingKind.Garage, "Fort Ashby Motor Pool");
            var pad = L.R(21, 12, 14, 28); Fill(pad, Ground.Parking);
            Block(L, U - 19, 14, 16, 14, -1, 0, BuildingKind.Armory, "Fort Ashby Armory");
            Block(L, U - 19, 34, 20, 14, -1, 0, BuildingKind.Office, "Fort Ashby Headquarters");
            Put(L, U - 4, 32, PropKind.Generator);
            // Supply dump inside the gate, field tents on the parade ground, sandbags and barriers at the gate, towers in the corners.
            for (int i = 0; i < 6; i++) Put(L, U / 2 + 8 + (i % 3) * 2, 6 + (i / 3) * 2, i % 2 == 0 ? PropKind.MilitaryCrate : PropKind.AmmoBox, "military");
            for (int i = 0; i < 4; i++) Put(L, U / 2 - 12 + i * 8, V - 25, PropKind.Tent, "military");
            for (int k = 0; k < 4; k++) { Put(L, U / 2 - 8 + k, 3, PropKind.Sandbags); Put(L, U / 2 + 4 + k, 3, PropKind.Sandbags); }
            Put(L, U / 2 - 5, 1, PropKind.Barrier); Put(L, U / 2 + 4, 1, PropKind.Barrier);
            Put(L, 2, 2, PropKind.Watchtower); Put(L, U - 3, 2, PropKind.Watchtower); Put(L, 2, V - 3, PropKind.Watchtower); Put(L, U - 3, V - 3, PropKind.Watchtower);
            Runners(ParkCars(pad, 4));
            Finish(s);
        }

        void RedTalonYard()
        {
            var fort = landmarks.FirstOrDefault(l => l.kind == LandmarkKind.MilitaryBase);
            Site s; if (!Search(76, 62, (int)(160 + 60 * S), Anywhere, r => Remote(r, W * .4f) + (fort == null ? 0 : Math.Min(fort.rect.DistanceTo(r.Center), W * .5f) * .5f), out s)) return;
            var L = Begin(s, "Red Talon Yard", LandmarkKind.RaiderCompound, ZoneKind.Wild, "The Red Talon raiders' fenced scrapyard: sandbags, barricades and heavy weapons. Hostile; keep away.");
            int U = L.Wd, V = L.Dp;
            Fill(s.r, Ground.Mud); Fence(L, 4);
            Block(L, U / 2 - 15, V - 22, 30, 16, 0, -1, BuildingKind.Compound, "Red Talon Yard");
            foreach (int u in new[] { 8, 15, 22, U - 24, U - 17, U - 10 }) Put(L, u, 16, PropKind.Tent, "raider");
            for (int u = U / 2 - 14; u < U / 2 - 6; u++) Put(L, u, 6, PropKind.Sandbags);
            for (int u = U / 2 + 6; u < U / 2 + 14; u++) Put(L, u, 6, PropKind.Sandbags);
            Put(L, U / 2 - 6, 2, PropKind.Barrier); Put(L, U / 2 + 5, 2, PropKind.Barrier);
            Put(L, U / 2 - 3, 10, PropKind.Barrier); Put(L, U / 2 + 2, 10, PropKind.Barrier);
            Put(L, 3, 3, PropKind.Watchtower); Put(L, U - 4, 3, PropKind.Watchtower);
            Put(L, U / 2, 26, PropKind.Campfire); Put(L, 12, 30, PropKind.Campfire);
            Put(L, U - 8, 26, PropKind.WoodCrate, "raider"); Put(L, U - 6, 26, PropKind.WoodCrate, "raider");
            Wrecks(ParkCars(L.R(4, 34, 16, 6), 2)); Wrecks(ParkCars(L.R(U - 20, 34, 16, 6), 2));
            Finish(s);
        }

        void Airfield()
        {
            int run = MathUtil.Clamp((int)(170 + 60 * S), 220, 360), U0 = run + 30;
            Site s; if (!Search(U0, 74, (int)(160 + 60 * S), Anywhere, r => Math.Min(Remote(r, 600), 400), out s)) return;
            var L = Begin(s, "Ashfall Regional Airfield", LandmarkKind.Airfield, ZoneKind.Suburb, "Regional airfield with a runway, hangars and a control tower. Open ground with few walls.");
            int U = L.Wd, V = L.Dp;
            Fill(s.r, Ground.Grass); Fence(L, 4);
            Fill(L.R(6, 4, U - 12, 30), Ground.Concrete);    // apron
            int hangars = U >= 330 ? 3 : 2;
            for (int i = 0; i < hangars; i++) Block(L, 10 + i * 32, 8, 26, 20, 0, 1, BuildingKind.Hangar, "Hangar " + (i + 1));   // doors open onto the taxiway
            Block(L, U - 26, 8, 10, 10, 0, -1, BuildingKind.Office, "Control Tower");
            Put(L, U - 13, 9, PropKind.Watchtower); Put(L, 8, 6, PropKind.Generator);
            Fill(L.R(8, 32, U - 16, 8), Ground.Concrete);    // taxiway
            Fill(L.R(12, 46, U - 24, 18), Ground.Runway);
            for (int i = 0; i < hangars; i++) Put(L, 12 + i * 32, 30, PropKind.WoodCrate, "hangar");
            Runners(ParkCars(L.R(U / 2 + 8, 20, 14, 7), 2));
            Finish(s);
        }

        void Hospital()
        {
            if (settlements.Count == 0) return;
            var seat = settlements[0]; var sc = new Vec2(seat.x, seat.y);
            Site s; if (!Search(56, 46, (int)(120 + 40 * S), Around(seat, seat.radius + 25, seat.radius + 200), r => -Math.Abs(Vec2.Distance(r.Center, sc) - (seat.radius + 55)), out s)) return;
            var L = Begin(s, "Ashfall County Hospital", LandmarkKind.Hospital, ZoneKind.Commercial, "The county hospital: wards full of medicine, and the dead who came for it.");
            int U = L.Wd, V = L.Dp;
            Fill(s.r, Ground.Grass); var lot = L.R(4, 2, U - 8, 14); Fill(lot, Ground.Parking);
            Block(L, 5, 18, 46, 22, 0, -1, BuildingKind.Hospital, "Ashfall County Hospital");
            Put(L, 3, V - 3, PropKind.Generator);
            var cars = ParkCars(lot, 5); Wrecks(cars.Where((c, i) => i % 2 == 0).ToList());
            Finish(s);
        }

        void RadioStation()
        {
            Site s; if (!Search(40, 36, (int)(160 + 60 * S), Anywhere, r => Math.Min(Remote(r, 500), 350) + Fractal(r.Center.x, r.Center.y, 11) * 200, out s)) return;
            var L = Begin(s, "KAFC Radio Station", LandmarkKind.RadioStation, ZoneKind.Suburb, "Hilltop radio station, Ada Pike's enclave. Fenced and friendly, but short on loot.");
            int U = L.Wd, V = L.Dp;
            Fill(s.r, Ground.Gravel); Fence(L, 3);
            Block(L, 11, 12, 18, 14, 0, -1, BuildingKind.RadioStation, "KAFC Radio Station");
            Put(L, U - 6, V - 6, PropKind.Watchtower);   // the mast
            Put(L, 5, V - 6, PropKind.Generator);
            Put(L, 5, 14, PropKind.Tent, "camp"); Put(L, U - 6, 14, PropKind.Tent, "camp");
            for (int k = 0; k < 3; k++) { Put(L, U / 2 - 6 + k, 3, PropKind.Sandbags); Put(L, U / 2 + 3 + k, 3, PropKind.Sandbags); }
            Finish(s);
        }

        void ReedsGarage()
        {
            var seat = settlements.Count > 0 ? settlements[0] : null;
            Func<Rect, float> score = r => seat == null ? 0 : -Math.Abs(Vec2.Distance(r.Center, new Vec2(seat.x, seat.y)) - (seat.radius + 260)) * .1f;
            Site s; if (!FindSite(46, 36, 220, 40, BesideRoad(18), score, out s) && !Search(46, 36, (int)(120 + 40 * S), Anywhere, score, out s)) return;
            var L = Begin(s, "Reed's Garage", LandmarkKind.Garage, ZoneKind.Suburb, "Jonah Reed's garage and tow yard: fuel, tools and cars that still run.");
            int U = L.Wd, V = L.Dp;
            Fill(L.R(0, 0, U, 18), Ground.Parking); Fill(L.R(0, 18, U, V - 18), Ground.Gravel);
            Block(L, 4, 19, 26, 14, 0, -1, BuildingKind.Garage, "Reed's Garage");
            Put(L, 9, 7, PropKind.GasPump); Put(L, 15, 7, PropKind.GasPump);
            Runners(ParkCars(L.R(31, 2, 13, 31), 4));
            Finish(s);
        }

        // ---------------------------------------------------------------- smaller landmarks (after the towns)
        void MinorLandmarks()
        {
            int n = Math.Max(1, (int)Math.Round(scale));
            string[] warehouses = { "Ash Valley Freight", "Cedar Fork Feed & Supply", "Route 9 Distribution", "Harlan Cold Storage", "County Line Depot", "Millbrook Lumber Yard", "Dunmore Freight Terminal" };
            for (int i = 0; i < Math.Min(n, warehouses.Length); i++) Warehouse(warehouses[i]);
            string[] churches = { "St. Brigid's Church", "First Baptist Church", "Grace Chapel", "Holy Cross Church" };
            for (int i = 0; i < Math.Min(1 + (int)(S - 1), churches.Length); i++) Church(churches[i]);
            string[] schools = { "Ashfall County High School", "Cedar Fork Elementary", "Harlan Springs Middle School" };
            for (int i = 0; i < Math.Min(scale >= 3 ? 2 : 1, schools.Length); i++) School(schools[i], i == 0);
            string[] motels = { "Starlite Motel", "Ashfall Motor Inn" };
            for (int i = 0; i < (scale >= 3 ? 2 : 1); i++) Motel(motels[i]);
            string[] camps = { "Ash River Campground", "Pine Ridge Campground", "Kettle Lake Campground", "Elk Hollow Campground" };
            for (int i = 0; i < Math.Min(1 + (int)(scale / 2), camps.Length); i++) Campground(camps[i]);
        }

        float NearTown(Rect r) { float m = float.MaxValue; var c = r.Center; foreach (var s in settlements) m = Math.Min(m, Vec2.Distance(c, new Vec2(s.x, s.y)) - s.radius); return m; }

        void Warehouse(string name)
        {
            Site s; if (!FindSite(40, 34, 160, 50, BesideRoad(17), r => -Math.Abs(NearTown(r) - 120) * .05f, out s) && !Search(40, 34, 200, Anywhere, r => 0, out s)) return;
            var L = Begin(s, name, LandmarkKind.Warehouse, ZoneKind.Suburb, "Warehouse stacked with building materials and stock.");
            int U = L.Wd;
            var yard = L.R(2, 2, U - 4, 7); Fill(yard, Ground.Parking);
            Block(L, 4, 10, 32, 22, 0, -1, BuildingKind.Warehouse, name);
            ParkCars(yard, 2);
            Finish(s);
        }

        void Church(string name)
        {
            if (settlements.Count == 0) return;
            var town = settlements[rng.Range(0, settlements.Count)]; var tc = new Vec2(town.x, town.y);
            Site s; if (!FindSite(28, 34, 160, 160, Around(town, town.radius + 10, town.radius + 140), r => -Vec2.Distance(r.Center, tc) * .05f, out s) && !Search(28, 34, 200, Anywhere, r => -NearTown(r) * .05f, out s)) return;
            var L = Begin(s, name, LandmarkKind.Church, ZoneKind.Suburb, "A church with a vestry and storerooms: a good place to hole up.");
            int U = L.Wd;
            Block(L, 7, 6, 14, 24, 0, -1, BuildingKind.Church, name);
            Put(L, 3, 4, PropKind.Table); Put(L, U - 4, 4, PropKind.Table);
            for (int v = 14; v < 30; v += 4) { Put(L, 2, v, PropKind.Bush); Put(L, U - 3, v, PropKind.Bush); }
            Finish(s);
        }

        void School(string name, bool countySeat)
        {
            if (settlements.Count == 0) return;
            var town = countySeat ? settlements[0] : settlements[rng.Range(0, settlements.Count)]; var tc = new Vec2(town.x, town.y);
            Site s; if (!FindSite(52, 40, 160, 160, Around(town, town.radius + 15, town.radius + 160), r => -Vec2.Distance(r.Center, tc) * .05f, out s) && !Search(52, 40, 200, Anywhere, r => -NearTown(r) * .05f, out s)) return;
            var L = Begin(s, name, LandmarkKind.School, ZoneKind.Town, "School: classrooms along a central hall, an office and a playground.");
            Fill(L.R(4, 2, 30, 12), Ground.Concrete);
            Block(L, 4, 17, 44, 18, 0, -1, BuildingKind.School, name);
            Put(L, 8, 6, PropKind.Table); Put(L, 14, 6, PropKind.Table);
            Finish(s);
        }

        void Motel(string name)
        {
            Site s; if (!FindSite(52, 30, 160, 40, BesideRoad(15), r => 0, out s) && !Search(52, 30, 200, Anywhere, r => 0, out s)) return;
            var L = Begin(s, name, LandmarkKind.Motel, ZoneKind.Suburb, "Roadside motel: a row of small rooms and a parking lot.");
            int U = L.Wd;
            var lot = L.R(2, 2, U - 4, 14); Fill(lot, Ground.Parking);
            Block(L, 3, 18, 46, 9, 0, -1, BuildingKind.Motel, name);
            ParkCars(lot, 4);
            Finish(s);
        }

        void Campground(string name)
        {
            Func<Rect, float> score = r =>
            {
                float sc = Math.Min(Remote(r, 600), 600) * .08f; var q = r.Expand(40);
                for (int x = q.x; x < q.MaxX; x += 16) for (int y = q.y; y < q.MaxY; y += 16) if (map.ZoneAt(x, y) == ZoneKind.Forest) sc += 2;
                for (int a = 0; a < 16; a++) { var p = r.Center + Vec2.FromAngle(a * .3927f) * 70; if (map.IsWater((int)p.x, (int)p.y)) { sc += 20; break; } }
                return sc;
            };
            Site s; if (!Search(50, 40, (int)(160 + 60 * S), Anywhere, score, out s)) return;
            var L = Begin(s, name, LandmarkKind.Campground, ZoneKind.Forest, "Wooded campground with tents and a ranger station. Remote and quiet.");
            int V = L.Dp;
            Fill(s.r, Ground.Dirt);
            Block(L, 4, V - 12, 12, 9, 0, -1, BuildingKind.Office, "Ranger Station");
            foreach (var t in new[] { new Int2(20, 8), new Int2(31, 8), new Int2(42, 9), new Int2(22, 20), new Int2(35, 20), new Int2(44, 30) }) Put(L, t.x, t.y, PropKind.Tent, "camp");
            foreach (var t in new[] { new Int2(26, 13), new Int2(39, 15), new Int2(30, 30) }) Put(L, t.x, t.y, PropKind.Campfire);
            foreach (var t in new[] { new Int2(27, 25), new Int2(33, 26), new Int2(20, 32) }) Put(L, t.x, t.y, PropKind.Table);
            ParkCars(L.R(2, 2, 14, 7), 2);
            Finish(s);
        }

        /// <summary>A few of the farms get names and become countryside landmarks (and landing-zone candidates).</summary>
        void Farmsteads()
        {
            string[] names = { "Miller Farm", "Calder Ranch", "Hollis Orchard", "Pruitt Dairy", "Vance Homestead", "Okafor Farm", "Lindqvist Ranch" };
            var pool = new List<Landmark>(farmSites); var chosen = new List<Landmark>();
            int count = Math.Min(names.Length, Math.Min(pool.Count, 2 + (int)S));
            while (chosen.Count < count && pool.Count > 0)
            {
                Landmark best = null; float bestD = -1;
                foreach (var f in pool)
                {
                    float d = float.MaxValue;
                    foreach (var c in chosen) d = Math.Min(d, Vec2.Distance(f.Center, c.Center));
                    foreach (var l in landmarks) d = Math.Min(d, l.rect.DistanceTo(f.Center));
                    if (settlements.Count > 0) d = Math.Min(d, Vec2.Distance(f.Center, new Vec2(settlements[0].x, settlements[0].y)));
                    if (d > bestD) { bestD = d; best = f; }
                }
                pool.Remove(best); chosen.Add(best);
                best.name = names[chosen.Count - 1];
                best.description = "Isolated farmland with a sturdy farmhouse and barn. A quiet start, sparse loot.";
                foreach (int id in best.buildings) map.buildings[id].name = best.name + " " + BuildingName(map.buildings[id].kind);
                landmarks.Add(best);
            }
        }

        // ---------------------------------------------------------------- landing zones
        /// <summary>8 to 12 named drop points spread over the county: the county seat, the major landmarks, a couple of countryside
        /// spots and then towns, picked farthest-first. Every zone is on dry land.</summary>
        void LandingZones()
        {
            int target = MathUtil.Clamp(8 + (int)scale, 8, 12);
            var seat = landmarks.FirstOrDefault(l => l.kind == LandmarkKind.Town);
            if (seat != null) AddZone(seat);
            foreach (var k in new[] { LandmarkKind.MilitaryBase, LandmarkKind.Airfield, LandmarkKind.Hospital, LandmarkKind.RadioStation, LandmarkKind.Garage }) { var l = Find(k); if (l != null) AddZone(l); }
            var country = landmarks.Where(l => l.kind == LandmarkKind.Farmstead || l.kind == LandmarkKind.Campground).ToList();
            var towns = landmarks.Where(l => l.kind == LandmarkKind.Town && l != seat).ToList();
            Spread(country, Math.Min(2, target - landingZones.Count));
            Spread(towns, target - landingZones.Count);
            Spread(country, target - landingZones.Count);
            var others = landmarks.Where(l => l.kind == LandmarkKind.School || l.kind == LandmarkKind.Church || l.kind == LandmarkKind.Motel || l.kind == LandmarkKind.Warehouse).ToList();
            Spread(others, target - landingZones.Count);
        }
        void Spread(List<Landmark> pool, int count)
        {
            for (; count > 0 && pool.Count > 0; count--)
            {
                Landmark best = null; float bestD = -1;
                foreach (var l in pool)
                {
                    float d = float.MaxValue; foreach (var z in landingZones) d = Math.Min(d, Vec2.Distance(l.Center, z.Center));
                    if (d > bestD) { bestD = d; best = l; }
                }
                pool.Remove(best); AddZone(best);
            }
        }
        void AddZone(Landmark l)
        {
            int x = l.x, y = l.y;
            // Nudge onto dry land (a town centre can sit on a bridge or a pond edge).
            for (int r = 0; r < 40 && map.IsWater(x, y); r++)
                for (int a = 0; a < 8; a++) { int tx = l.x + (int)(Math.Cos(a * .785) * r), ty = l.y + (int)(Math.Sin(a * .785) * r); if (!map.IsWater(tx, ty) && map.InBounds(tx, ty)) { x = tx; y = ty; break; } }
            landingZones.Add(new LandingZone { name = l.name, x = x, y = y, description = ZoneDescription(l) });
        }
        string ZoneDescription(Landmark l)
        {
            if (l.kind != LandmarkKind.Town) return l.description;
            if (settlements.Count > 0 && l.name == settlements[0].name) return "County seat: stores, the sheriff's office and crowded streets. Rich loot, heavy infestation.";
            return l.radius >= 170 ? "A sizeable town with stores and plenty of houses to search." : "A small town: a few shops and quiet residential streets.";
        }

        // ---------------------------------------------------------------- landmark building layouts
        static List<Int2> Segments(int W, int n)
        {
            var list = new List<Int2>(); n = Math.Max(1, n);
            for (int i = 0; i < n; i++) list.Add(new Int2(i * W / n, (i + 1) * W / n));
            return list;
        }

        /// <summary>Rooms either side of a three-tile corridor that runs along the front; the centre-front room is the lobby
        /// holding the entrance. Fire exits at both ends of the corridor.</summary>
        List<Room> CorridorLayout(Building b, Local L, int W, int D, List<Int2> doors, int roomW, Ground floor, RoomType[] cycle)
        {
            var rooms = new List<Room>();
            int c0 = Math.Max(3, (D - 3) / 2), c1 = c0 + 3;
            rooms.Add(AddRoom(b, L.R(0, c0, W, 3), RoomType.Corridor, floor));
            int door = W / 2, k = 0;
            var segs = Segments(W, W / roomW);
            for (int band = 0; band < 2; band++)
            {
                int v0 = band == 0 ? 0 : c1, v1 = band == 0 ? c0 : D;
                for (int i = 0; i < segs.Count; i++)
                {
                    int u0 = segs[i].x, u1 = segs[i].y;
                    bool lobby = band == 0 && u0 <= door && door < u1;
                    var type = lobby ? RoomType.Lobby : cycle[k++ % cycle.Length];
                    rooms.Add(AddRoom(b, L.R(u0, v0, u1 - u0, v1 - v0), type, type == RoomType.Bathroom ? Ground.TileFloor : floor));
                    InteriorWallAcross(b, L, band == 0 ? c0 : c1, u0, u1, lobby ? door : (u0 + u1) / 2);
                    if (i > 0) InteriorWallAlong(b, L, u0, v0, v1, -1);
                }
            }
            FrontDoor(b, L, door, doors, false); if (W >= 24) FrontDoor(b, L, door - 1, doors, false);
            int mid = c0 + 1;
            var a = L.T(0, mid); var o = AddOpening(a, L.T(-1, mid), Edge.Door, b.id, true); o.locked = rng.Chance(.4f); doors.Add(a);
            var e = L.T(W - 1, mid); o = AddOpening(e, L.T(W, mid), Edge.Door, b.id, true); o.locked = rng.Chance(.4f); doors.Add(e);
            return rooms;
        }

        /// <summary>One large front hall with a band of back rooms, each opening onto the hall; a back door from the first.</summary>
        Room HallLayout(Building b, Local L, int W, int D, List<Int2> doors, RoomType hallType, Ground floor, int back, RoomType[] backTypes, List<Room> rooms, int frontDoors)
        {
            if (D - back < 5) back = 0;
            var hall = AddRoom(b, L.R(0, 0, W, D - back), hallType, floor); rooms.Add(hall);
            if (back > 0)
            {
                var segs = Segments(W, backTypes.Length);
                for (int i = 0; i < segs.Count; i++)
                {
                    int u0 = segs[i].x, u1 = segs[i].y; var type = backTypes[i];
                    rooms.Add(AddRoom(b, L.R(u0, D - back, u1 - u0, back), type, type == RoomType.Bathroom ? Ground.TileFloor : floor));
                    InteriorWallAcross(b, L, D - back, u0, u1, (u0 + u1) / 2);
                    if (i > 0) InteriorWallAlong(b, L, u0, D - back, D, -1);
                }
                BackDoor(b, L, segs[0].x + Math.Max(1, (segs[0].y - segs[0].x) / 2), D, doors);
            }
            for (int k = 0; k < frontDoors; k++) FrontDoor(b, L, W / 2 - frontDoors / 2 + k, doors, false);
            return hall;
        }

        /// <summary>Office at one end, then a row of rooms each with its own door onto the parking lot.</summary>
        void MotelLayout(Building b, Local L, int W, int D, List<Int2> doors)
        {
            const int office = 6, roomW = 5;
            var o = AddRoom(b, L.R(0, 0, office, D), RoomType.Office, Ground.TileFloor);
            FrontDoor(b, L, office / 2, doors, false);
            Furnish(o, PropKind.Counter, "office", 1); Furnish(o, PropKind.Desk, "office", 1);
            for (int u = office; u + 4 <= W; u += roomW)
            {
                int w = W - u - roomW < 4 ? W - u : roomW;
                var room = AddRoom(b, L.R(u, 0, w, D), RoomType.MotelRoom, Ground.WoodFloor);
                InteriorWallAlong(b, L, u, 0, D, -1);
                FrontDoor(b, L, u + w / 2, doors, rng.Chance(.4f));
                Furnish(room, PropKind.Bed, "", 1); Furnish(room, PropKind.Dresser, "bedroom", 1);
                if (rng.Chance(.5f)) Furnish(room, PropKind.MedicineCabinet, "bathroom", 1);
                if (w != roomW) break;
            }
        }

        static string StoreTable(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.Hospital: return "hospital";
                case BuildingKind.Hangar: return "hangar";
                case BuildingKind.RadioStation: return "radio";
                case BuildingKind.Compound: return "raider";
                case BuildingKind.Church: return "church";
                case BuildingKind.Garage: return "garage";
                case BuildingKind.School: return "school";
                case BuildingKind.Barracks: case BuildingKind.Armory: return "military";
                default: return "warehouse";
            }
        }

        bool LandmarkLayout(Building b, BuildingKind kind, Local L, int W, int D, List<Int2> doors)
        {
            var rooms = new List<Room>();
            switch (kind)
            {
                case BuildingKind.Hospital: rooms = CorridorLayout(b, L, W, D, doors, 8, Ground.TileFloor, new[] { RoomType.Ward, RoomType.Ward, RoomType.Pharmacy, RoomType.Office, RoomType.Ward, RoomType.Storage, RoomType.Bathroom }); break;
                case BuildingKind.Barracks: rooms = CorridorLayout(b, L, W, D, doors, 8, Ground.MilitaryFloor, new[] { RoomType.Bunks, RoomType.Bunks, RoomType.Bathroom, RoomType.Bunks, RoomType.Storage }); break;
                case BuildingKind.School: rooms = CorridorLayout(b, L, W, D, doors, 9, Ground.TileFloor, new[] { RoomType.Classroom, RoomType.Classroom, RoomType.Office, RoomType.Classroom, RoomType.Bathroom, RoomType.Storage }); break;
                case BuildingKind.RadioStation: rooms = CorridorLayout(b, L, W, D, doors, 6, Ground.WoodFloor, new[] { RoomType.Studio, RoomType.Office, RoomType.Storage, RoomType.Bathroom, RoomType.Studio }); break;
                case BuildingKind.Warehouse: Aisles(b, HallLayout(b, L, W, D, doors, RoomType.Storage, Ground.Concrete, 6, new[] { RoomType.Office, RoomType.Bathroom, RoomType.Storage }, rooms, 3), L, W, D - 6, "warehouse"); break;
                case BuildingKind.Hangar: HallLayout(b, L, W, D, doors, RoomType.Hangar, Ground.Concrete, 6, new[] { RoomType.Office, RoomType.Storage }, rooms, 4); break;
                case BuildingKind.Armory: HallLayout(b, L, W, D, doors, RoomType.Office, Ground.MilitaryFloor, D - 5, new[] { RoomType.Armory, RoomType.Armory }, rooms, 1); break;
                case BuildingKind.Garage: HallLayout(b, L, W, D, doors, RoomType.Workshop, Ground.Concrete, 5, new[] { RoomType.Office, RoomType.Storage, RoomType.Bathroom }, rooms, 3); break;
                case BuildingKind.Compound: HallLayout(b, L, W, D, doors, RoomType.Storage, Ground.Concrete, 6, new[] { RoomType.Bunks, RoomType.Office, RoomType.Bunks }, rooms, 2); break;
                case BuildingKind.Church: Pews(b, HallLayout(b, L, W, D, doors, RoomType.Chapel, Ground.WoodFloor, 6, new[] { RoomType.Office, RoomType.Storage, RoomType.Bathroom }, rooms, 2), L, W, D - 6); break;
                case BuildingKind.Motel: MotelLayout(b, L, W, D, doors); return true;
                default: return false;
            }
            foreach (var r in rooms) FurnishRoom(r, kind);
            return true;
        }
        void Aisles(Building b, Room hall, Local L, int W, int depth, string table)
        {
            for (int v = 4; v < depth - 2; v += 3)
                for (int u = 2; u < W - 2; u++)
                    if (Math.Abs(u - W / 2) > 2 && u % 8 != 0) Place(b, hall, L, u, v, PropKind.Shelf, u % 3 == 0 ? table : "");
        }
        void Pews(Building b, Room hall, Local L, int W, int depth)
        {
            for (int v = 3; v < depth - 3; v += 2) for (int u = 2; u < W - 2; u++) if (Math.Abs(u - W / 2) > 1) Place(b, hall, L, u, v, PropKind.Table, "");
            Place(b, hall, L, W / 2 + 2, depth - 2, PropKind.Counter, "church");
        }
        void FurnishRoom(Room room, BuildingKind kind)
        {
            bool military = kind == BuildingKind.Barracks || kind == BuildingKind.Armory;
            switch (room.type)
            {
                case RoomType.Lobby: Furnish(room, PropKind.Counter, kind == BuildingKind.Hospital ? "hospital" : military ? "military" : "office", 2); Furnish(room, PropKind.Couch, "", 1); break;
                case RoomType.Ward: Furnish(room, PropKind.MedicalBed, "", rng.Range(3, 5)); Furnish(room, PropKind.MedicineCabinet, "hospital", rng.Range(1, 3)); break;
                case RoomType.Pharmacy: Furnish(room, PropKind.Shelf, "pharmacy", 4); break;
                case RoomType.Office:
                    Furnish(room, PropKind.Desk, military ? "military" : kind == BuildingKind.RadioStation ? "radio" : kind == BuildingKind.Compound ? "raider" : kind == BuildingKind.Church ? "church" : "office", 2);
                    if (kind == BuildingKind.Compound) Furnish(room, PropKind.Couch, "", 1);
                    break;
                case RoomType.Storage: Furnish(room, PropKind.Crate, StoreTable(kind), rng.Range(2, 5)); break;
                case RoomType.Bathroom: Furnish(room, PropKind.Toilet, "", 1); Furnish(room, PropKind.Sink, "", 1); Furnish(room, PropKind.MedicineCabinet, kind == BuildingKind.Hospital ? "hospital" : "bathroom", 1); break;
                case RoomType.Bunks: Furnish(room, PropKind.Bunk, "", rng.Range(3, 5)); Furnish(room, PropKind.Locker, kind == BuildingKind.Compound ? "raider" : "military", 2); break;
                case RoomType.Classroom: Furnish(room, PropKind.Desk, "school", 4); Furnish(room, PropKind.Shelf, "school", 1); break;
                case RoomType.Studio: Furnish(room, PropKind.RadioSet, "radio", 1); Furnish(room, PropKind.Desk, "radio", 1); Furnish(room, PropKind.Shelf, "radio", 1); break;
                case RoomType.Armory: Furnish(room, PropKind.Locker, "armory", 2); Furnish(room, PropKind.AmmoBox, "armory", 2); Furnish(room, PropKind.MilitaryCrate, "armory", 2); break;
                case RoomType.Workshop: Furnish(room, PropKind.Toolbox, "garage", 2); Furnish(room, PropKind.Workbench, "tools", 1); Furnish(room, PropKind.Shelf, "garage", 2); break;
                case RoomType.Hangar: Furnish(room, PropKind.WoodCrate, "hangar", rng.Range(3, 6)); Furnish(room, PropKind.Workbench, "tools", 1); Furnish(room, PropKind.Toolbox, "garage", 1); Furnish(room, PropKind.Generator, "", 1); break;
            }
        }
    }
}
