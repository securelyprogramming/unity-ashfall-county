using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>County setup for the bestiary: zombie kinds by zone and landmark, juggernauts, plague hearts and hordes.</summary>
    public sealed partial class World
    {
        List<Vec2> policeSites;

        /// <summary>Assigns zombie kinds, places plague hearts and hordes after the base population is spawned.</summary>
        void SetupBestiary(Building home)
        {
            var r = new Rng((ulong)settings.seed, 29);
            var start = survivors.Count > 0 ? survivors[0].pos : home != null ? home.rect.Center : new Vec2(map.width * .5f, map.height * .5f);
            float freaks = Math.Max(0, settings.freakDensity);
            foreach (var z in zombies)
            {
                if (z.crawler) { Bestiary.Apply(z, ZombieKind.Crawler, r); continue; }
                var kind = freaks > 0 && Vec2.Distance(z.pos, start) >= Bestiary.FreakClearance ? RollKind(z.pos, r) : ZombieKind.Walker;
                Bestiary.Apply(z, kind, r);
            }
            if (freaks > 0) { Juggernauts(start, freaks, r); PlaceHearts(home, start, freaks, r); }
            PlaceHordes(start, r);
            initialPopulation = zombies.Count;
        }

        /// <summary>A kind for a zombie at p: mostly walkers; bloaters in shops and near the hospital, armour at police and
        /// military sites, ferals in the woods. Juggernauts are placed separately (Juggernauts).</summary>
        internal ZombieKind RollKind(Vec2 p, Rng r)
        {
            if (policeSites == null) policeSites = map.buildings.Where(b => b.kind == BuildingKind.Police).Select(b => b.rect.Center).ToList();
            float freaks = Math.Max(0, settings.freakDensity);
            if (freaks <= 0) return ZombieKind.Walker;
            var zone = map.ZoneAt((int)p.x, (int)p.y);
            float screamer = .024f, bloater = zone == ZoneKind.Commercial ? .035f : .02f, armored = .026f;
            float feral = zone == ZoneKind.Forest || zone == ZoneKind.Wild ? .05f : zone == ZoneKind.Farm ? .03f : zone == ZoneKind.Suburb ? .014f : .009f;
            if (zone == ZoneKind.Military) armored += .25f;
            if (gen != null)
                foreach (var lm in gen.landmarks)
                {
                    if (Vec2.Distance(p, lm.Center) > lm.radius + 40) continue;
                    switch (lm.kind)
                    {
                        case LandmarkKind.Hospital: bloater += .07f; break;
                        case LandmarkKind.MilitaryBase: armored += .25f; break;
                        case LandmarkKind.Airfield: armored += .06f; break;
                        case LandmarkKind.Campground: case LandmarkKind.Quarry: feral += .03f; break;
                    }
                }
            foreach (var c in policeSites) if (Vec2.Distance(p, c) < 45) { armored += .12f; break; }
            float total = screamer + bloater + armored + feral, scale = Math.Min(freaks, .9f / Math.Max(.0001f, total));
            float x = r.Float();
            if ((x -= screamer * scale) < 0) return ZombieKind.Screamer;
            if ((x -= bloater * scale) < 0) return ZombieKind.Bloater;
            if ((x -= armored * scale) < 0) return ZombieKind.Armored;
            if ((x -= feral * scale) < 0) return ZombieKind.Feral;
            return ZombieKind.Walker;
        }

        /// <summary>Open land a zombie may stand on: in bounds, not water, wall or furniture.</summary>
        public bool LandFree(Vec2 p)
        {
            int x = (int)Math.Floor(p.x), y = (int)Math.Floor(p.y);
            return map.InBounds(x, y) && !map.TileBlocked(x, y) && !map.IsWater(x, y);
        }

        /// <summary>Spawns a zombie of a kind (stats applied). Use world.rng at runtime for determinism.</summary>
        public Zombie SpawnKind(Vec2 p, ZombieKind kind, Rng r)
        {
            var z = AddZombie(p, r);
            Bestiary.Apply(z, kind == ZombieKind.Walker && z.crawler ? ZombieKind.Crawler : kind, r);
            return z;
        }

        /// <summary>About one juggernaut per 0.6 km², mostly at landmarks (the military base above all).</summary>
        void Juggernauts(Vec2 start, float freaks, Rng r)
        {
            float km2 = map.width * (float)map.height / 1e6f;
            int count = (int)Math.Round(km2 / .6f * freaks);
            var sites = gen == null ? new List<Landmark>() : gen.landmarks;
            float total = 0; foreach (var lm in sites) total += JuggernautWeight(lm.kind);
            for (int i = 0; i < count; i++)
                for (int t = 0; t < 40; t++)
                {
                    Vec2 c; float radius;
                    if (total > 0 && r.Chance(.75f))
                    {
                        float pick = r.Range(0, total); Landmark lm = sites[sites.Count - 1];
                        foreach (var s in sites) { pick -= JuggernautWeight(s.kind); if (pick < 0) { lm = s; break; } }
                        c = lm.Center; radius = Math.Max(20, lm.radius);
                    }
                    else { c = new Vec2(r.Range(0, map.width), r.Range(0, map.height)); radius = 0; }
                    var p = c + Vec2.FromAngle(r.Range(0, 6.28f)) * r.Range(0, radius);
                    if (!LandFree(p) || Vec2.Distance(p, start) < Math.Max(Bestiary.FreakClearance, 120)) continue;
                    if (radius == 0) { var z0 = map.ZoneAt((int)p.x, (int)p.y); if (z0 != ZoneKind.Town && z0 != ZoneKind.Commercial && z0 != ZoneKind.Military && z0 != ZoneKind.Suburb) continue; }
                    var z = SpawnKind(p, ZombieKind.Juggernaut, r); z.state = ZombieState.Idle;
                    break;
                }
        }
        static float JuggernautWeight(LandmarkKind k)
        {
            switch (k)
            {
                case LandmarkKind.MilitaryBase: return 5;
                case LandmarkKind.Hospital: case LandmarkKind.Airfield: case LandmarkKind.RaiderCompound: return 2;
                case LandmarkKind.Warehouse: return 1.5f;
                case LandmarkKind.Town: case LandmarkKind.Quarry: return 1;
                default: return .5f;
            }
        }

        /// <summary>About 11 hearts on a 4 km county (scaled by area and freak density), inside town and landmark buildings,
        /// far from the start, each guarded by plague zombies.</summary>
        void PlaceHearts(Building home, Vec2 start, float freaks, Rng r)
        {
            float km2 = map.width * (float)map.height / 1e6f;
            int count = Math.Max(1, (int)Math.Round(11 * km2 / 16.78f * freaks));
            var inLandmark = new HashSet<int>();
            if (gen != null) foreach (var lm in gen.landmarks) foreach (int b in lm.buildings) inLandmark.Add(b);
            // Shops, offices and landmark buildings first; town houses only when those run out.
            var candidates = new List<Building>(); var houses = new List<Building>();
            foreach (var b in map.buildings)
            {
                if (home != null && b.id == home.id || b.rect.w < 6 || b.rect.h < 6) continue;
                var zone = map.ZoneAt((int)b.rect.Center.x, (int)b.rect.Center.y);
                if (!inLandmark.Contains(b.id) && zone != ZoneKind.Town && zone != ZoneKind.Commercial && zone != ZoneKind.Military) continue;
                if (Vec2.Distance(b.rect.Center, start) < Bestiary.HeartClearance) continue;
                (b.kind == BuildingKind.House || b.kind == BuildingKind.Shed ? houses : candidates).Add(b);
            }
            float spacing = Math.Max(120, (float)Math.Sqrt(map.width * (float)map.height / Math.Max(1, count)) * .5f);
            for (int tries = 0; tries < count * 30 && hearts.Count < count && candidates.Count + houses.Count > 0; tries++)
            {
                var pool = candidates.Count > 0 ? candidates : houses;
                int i = r.Range(0, pool.Count); var b = pool[i]; pool.RemoveAt(i);
                Vec2 spot; if (!HeartSpot(b, out spot)) continue;
                bool crowded = false; foreach (var h in hearts) if (Vec2.Distance(h.pos, spot) < spacing) { crowded = true; break; }
                if (crowded) continue;
                var heart = new PlagueHeart { id = hearts.Count, pos = spot, building = b.id, spawnClock = r.Range(4f, 10f) };
                hearts.Add(heart);
                int guards = r.Range(5, 9);
                for (int g = 0, t = 0; g < guards && t < 60; t++)
                {
                    var p = spot + Vec2.FromAngle(r.Range(0, 6.28f)) * r.Range(2f, 10f);
                    if (!LandFree(p) || Vec2.Distance(p, spot) < PlagueHeart.Radius + 1) continue;
                    var z = SpawnKind(p, ZombieKind.Walker, r); z.plague = true; g++;
                }
            }
        }

        /// <summary>A tile centre deep inside one of the building's rooms (5×5 of the same room), so the heart never blocks a door.</summary>
        bool HeartSpot(Building b, out Vec2 spot)
        {
            spot = Vec2.Zero; float best = float.MaxValue;
            foreach (int rid in b.rooms)
            {
                var room = map.rooms[rid];
                for (int x = room.rect.x + 2; x < room.rect.MaxX - 2; x++)
                    for (int y = room.rect.y + 2; y < room.rect.MaxY - 2; y++)
                    {
                        bool ok = true;
                        for (int dx = -2; dx <= 2 && ok; dx++)
                            for (int dy = -2; dy <= 2 && ok; dy++)
                            {
                                var at = map.RoomAt(x + dx, y + dy);
                                if (at == null || at.id != room.id || Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && map.TileBlocked(x + dx, y + dy)) ok = false;
                            }
                        if (!ok) continue;
                        var p = new Vec2(x + .5f, y + .5f); float d = Vec2.Distance(p, room.rect.Center);
                        if (d < best) { best = d; spot = p; }
                    }
            }
            return best < float.MaxValue;
        }

        // ---------------------------------------------------------------- hordes
        /// <summary>Roaming packs of walkers (about 0.4 per km² at Normal population) that start in and between towns.</summary>
        void PlaceHordes(Vec2 start, Rng r)
        {
            float km2 = map.width * (float)map.height / 1e6f;
            int count = Math.Max(1, (int)Math.Round(km2 * .4f * settings.PopulationScale));
            var anchors = HordeAnchors();
            if (anchors.Count == 0) return;
            float clearance = Math.Min(250, map.width * .25f);
            for (int i = 0; i < count; i++)
                for (int t = 0; t < 30; t++)
                {
                    var p = anchors[r.Range(0, anchors.Count)] + Vec2.FromAngle(r.Range(0, 6.28f)) * r.Range(10, 60);
                    if (!LandFree(p) || Vec2.Distance(p, start) < clearance) continue;
                    var h = NewHorde(p, r.Range(15, 41), r);
                    PlanHorde(h, NextHordeTarget(h.center, r));
                    break;
                }
        }

        List<Vec2> HordeAnchors()
        {
            var list = new List<Vec2>();
            if (gen == null) return list;
            foreach (var s in gen.settlements) list.Add(new Vec2(s.x + .5f, s.y + .5f));
            foreach (var lm in gen.landmarks) { bool dup = false; foreach (var a in list) if (Vec2.Distance(a, lm.Center) < 60) { dup = true; break; } if (!dup) list.Add(lm.Center); }
            return list;
        }

        internal Vec2 NextHordeTarget(Vec2 from, Rng r)
        {
            var anchors = HordeAnchors();
            for (int t = 0; t < 8 && anchors.Count > 0; t++) { var a = anchors[r.Range(0, anchors.Count)]; if (Vec2.Distance(a, from) > 120) return a; }
            return new Vec2(r.Range(40, map.width - 40), r.Range(40, map.height - 40));
        }

        /// <summary>Spawns a horde of walkers around a point.</summary>
        internal Horde NewHorde(Vec2 at, int size, Rng r)
        {
            var h = new Horde { id = nextHorde++, center = at, target = at };
            hordes.Add(h);
            AddToHorde(h, size, r);
            return h;
        }
        internal void AddToHorde(Horde h, int count, Rng r)
        {
            for (int i = 0, t = 0; i < count && t < count * 6; t++)
            {
                var p = h.center + Vec2.FromAngle(r.Range(0, 6.28f)) * r.Range(0, 9f);
                if (!LandFree(p)) continue;
                var z = SpawnKind(p, ZombieKind.Walker, r); z.horde = h.id; z.state = ZombieState.Idle; z.timer = r.Range(2f, 8f);
                h.members.Add(z.id); i++;
            }
        }

        /// <summary>Sets a horde's destination, routing over the bridge when the straight line crosses the river.</summary>
        internal void PlanHorde(Horde h, Vec2 target)
        {
            h.target = target; h.viaWaypoint = false;
            if (gen == null || gen.bridge.w <= 0 || !CrossesWater(h.center, target)) return;
            h.waypoint = gen.bridge.Center; h.viaWaypoint = true;
        }
        bool CrossesWater(Vec2 a, Vec2 b)
        {
            float d = Vec2.Distance(a, b); int steps = Math.Max(1, (int)(d / 8));
            for (int i = 1; i < steps; i++) { var p = a + (b - a) * (i / (float)steps); if (map.IsWater((int)p.x, (int)p.y)) return true; }
            return false;
        }
    }
}
