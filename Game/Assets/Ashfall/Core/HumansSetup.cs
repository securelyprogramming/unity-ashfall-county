using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>Spawning the county's other people: enclaves, rival crews, the Red Talons, Kade's deserters and the lone characters.</summary>
    public sealed partial class World
    {
        static readonly string[] FirstNames = { "Dale", "Rosa", "Marcus", "June", "Hank", "Lena", "Cody", "Irene", "Wade", "Nadia", "Vince", "Tammy", "Owen", "Grace", "Rusty", "Mei", "Carl", "Darla", "Eli", "Bea", "Troy", "Hazel", "Lyle", "Pam" };
        static readonly string[] LastNames = { "Rourke", "Mendez", "Kowalski", "Price", "Beaumont", "Tran", "Sutter", "Doyle", "Okafor", "Lindqvist", "Harlan", "Pruitt", "Castillo", "Wexler", "Boone", "Abernathy" };
        static readonly string[] CrewNames = { "Dust Devils", "Ash River Rats", "Highway Pack", "Lucky Strikes", "Night Owls", "Gravel Saints", "Long Haulers", "Quarry Kids", "Mile Marker Crew", "Burnt Barn Gang", "Rust Riders", "Cedar Fork Boys", "Hollow Pines", "Grain Elevator Crew", "Static Hearts", "Two Bridges Gang", "Scrap Kings", "Saltlick Crew" };
        static readonly string[] Melee = { "baseball_bat", "machete", "crowbar", "pipe", "hammer", "golf_club", "kitchen_knife", "axe", "hunting_knife" };

        /// <summary>Minimum distance from the community base to a rival crew's landing point.</summary>
        public const float CrewSpawnDistance = 300;

        void SpawnHumans()
        {
            if (factions.Count > 1) return;
            var r = new Rng((ulong)settings.seed, 41);
            var lead = survivors.FirstOrDefault();
            Vec2 basePos = community.baseBuilding >= 0 ? community.basePos : lead != null ? lead.pos : new Vec2(map.width / 2f, map.height / 2f);
            var used = new HashSet<int>(); if (community.baseBuilding >= 0) used.Add(community.baseBuilding);
            float area = settings.mapSize / 4096f; area *= area;
            float density = Math.Max(0, settings.humanDensity);

            // Enclaves: a named leader and 2-4 members holding a landmark building.
            Enclave(r, used, basePos, "radio_station", "Radio Station", "Ada Pike keeps the county emergency band on the air from the KASH studios.", "ada", "radio station volunteer",
                Disposition.Friendly, LandmarkKind.RadioStation, BuildingKind.RadioStation, BuildingKind.Office, BuildingKind.School, BuildingKind.Church);
            Enclave(r, used, basePos, "reeds_garage", "Reed's Garage", "Jonah Reed's family garage: fuel, parts and no patience for armed strangers.", "jonah", "Reed's Garage mechanic",
                Disposition.Wary, LandmarkKind.Garage, BuildingKind.Garage, BuildingKind.GasStation, BuildingKind.Hardware, BuildingKind.Warehouse);
            Enclave(r, used, basePos, "hospital", "County Hospital", "Doc Hollis and a handful of orderlies keep one ward of the county hospital open.", "doc", "hospital orderly",
                Disposition.Friendly, LandmarkKind.Hospital, BuildingKind.Hospital, BuildingKind.Pharmacy, BuildingKind.Office, BuildingKind.Church);

            // The Red Talon raiders hold their compound; "Big Sal" Varga leads them.
            var talonHome = FindHome(r, used, basePos, 400, LandmarkKind.RaiderCompound, BuildingKind.Compound, BuildingKind.Warehouse, BuildingKind.Barn, BuildingKind.Farmhouse);
            var talons = AddFaction("red_talons", "Red Talons", "Raiders who take what they want from anyone still breathing.", AiRole.Raider, Disposition.Hunter, -30, "Red Talon raider", "raider", "sal", talonHome);
            int raiders = density <= 0 ? 0 : MathUtil.Clamp((int)Math.Round(5 * Math.Sqrt(area) * density), 3, 8);
            SpawnGroup(r, talons, talonHome, talons.home, raiders, "raider");

            // Kade's deserters roam from the military base and hunt for bounties.
            var fort = FindHome(r, used, basePos, 400, LandmarkKind.MilitaryBase, BuildingKind.Barracks, BuildingKind.Armory, BuildingKind.Hangar, BuildingKind.Police);
            var deserters = AddFaction("deserters", "Kade's Deserters", "Guardsmen who walked off the cordon with their rifles. They hunt survivors for gear and bounties.", AiRole.Hunter, Disposition.Hunter, -40, "Guard deserter", "soldier", "kade", null);
            deserters.home = fort != null ? FreeSpotNear(Center(fort), 14, r) : FarPoint(r, basePos, 400);
            deserters.nextBounty = 1440 + 6 * 60;   // first bounty at 06:00 on day 2
            SpawnGroup(r, deserters, null, deserters.home, density <= 0 ? 0 : MathUtil.Clamp((int)Math.Round(3 * density), 1, 5), "soldier");

            // Wanderers: Wren Calder and lone survivors (rescue and recruit candidates).
            var wanderers = AddFaction("wanderers", "Wanderers", "Lone survivors scattered across the county. Most would join a community that can keep them safe.", AiRole.Enclave, Disposition.Friendly, 25, "wanderer", "rival", "", null);
            var wrenHome = FindHome(r, used, basePos, 250, LandmarkKind.Farmstead, BuildingKind.Farmhouse, BuildingKind.Barn, BuildingKind.House);
            var wren = SpawnCharacter(r, Characters.Get("wren"), wanderers, wrenHome != null ? FreeTileIn(wrenHome, r) : FarPoint(r, basePos, 300));
            wanderers.home = wren.pos;
            int lone = density <= 0 ? 0 : MathUtil.Clamp((int)Math.Round(6 * area * density), 1, 10);
            for (int i = 0; i < lone; i++)
            {
                var house = FindHome(r, used, basePos, 200, LandmarkKind.Town, BuildingKind.House, BuildingKind.Farmhouse, BuildingKind.Motel);
                SpawnMember(r, wanderers, house != null ? FreeTileIn(house, r) : FarPoint(r, basePos, 250), AiRole.Enclave, "rival");
                if (house != null) used.Remove(house.id);   // houses stay available to others
            }

            // The Pilgrim: a friendly trader walking between towns.
            var church = FindHome(r, used, basePos, 150, LandmarkKind.Church, BuildingKind.Church, BuildingKind.Motel, BuildingKind.Diner);
            var pilgrimFaction = AddFaction("pilgrim", "The Pilgrim", "A wandering trader with rare goods.", AiRole.Trader, Disposition.Friendly, 10, "trader", "trader", "pilgrim", null);
            var pilgrim = SpawnCharacter(r, Characters.Get("pilgrim"), pilgrimFaction, church != null ? FreeSpotNear(Center(church), 10, r) : FarPoint(r, basePos, 200));
            pilgrimFaction.home = pilgrim.pos;

            // Rival crews land at landing zones and towns away from the community base.
            int crews = density <= 0 ? 0 : MathUtil.Clamp((int)Math.Round(12 * area * density), 2, 18);
            var points = CrewPoints(basePos);
            for (int i = 0; i < crews; i++)
            {
                bool tess = i == 0;
                var disposition = tess ? Disposition.Opportunist : r.Chance(.5f) ? Disposition.Opportunist : Disposition.Wary;
                string name = tess ? "Monroe's Scavengers" : CrewNames[(i - 1) % CrewNames.Length] + (i > CrewNames.Length ? " " + (i / CrewNames.Length + 1) : "");
                var crew = AddFaction("crew_" + i, name, tess ? "Tess Monroe's scavengers: fast, greedy and always first to the airdrop." : "A rival crew scavenging the county.", AiRole.Rival, disposition, 0, name + " scavenger", "rival", tess ? "tess" : "", null);
                var p = points.Count > 0 ? points[r.Range(0, points.Count)] : FarPoint(r, basePos, CrewSpawnDistance);
                crew.home = FreeSpotNear(p + Vec2.FromAngle(r.Range(0, 6.28f)) * r.Range(0, 30), 8, r);
                if (tess) { var t = SpawnCharacter(r, Characters.Get("tess"), crew, crew.home); t.squad = crew.id; }
                SpawnGroup(r, crew, null, crew.home, tess ? 3 : r.Range(1, 5), "rival");
            }
            foreach (var s in survivors) if (s.ai != AiRole.None) s.offscreen = !NearPlayer(s.pos, 160);
        }

        bool NearPlayer(Vec2 p, float range) { foreach (var s in survivors) if (s.alive && s.ai == AiRole.None && Vec2.Distance(s.pos, p) < range) return true; return false; }

        Faction AddFaction(string key, string name, string description, AiRole role, Disposition d, float standing, string member, string art, string leader, Building home)
        {
            var f = new Faction { id = factions.Count, key = key, name = name, description = description, role = role, disposition = d, standing = standing, member = member, art = art, leader = leader };
            if (home != null) { f.homeBuilding = home.id; f.home = Center(home); }
            factions.Add(f); return f;
        }

        void Enclave(Rng r, HashSet<int> used, Vec2 basePos, string key, string name, string description, string leader, string member, Disposition d, LandmarkKind kind, params BuildingKind[] kinds)
        {
            var home = FindHome(r, used, basePos, 150, kind, kinds);
            var f = AddFaction(key, name, description, AiRole.Enclave, d, d == Disposition.Friendly ? 15 : 0, member, "enclave", leader, home);
            f.enclave = true;
            if (home == null) f.home = FarPoint(r, basePos, 150);
            ClearZombies(f.home, home != null ? Math.Max(home.rect.w, home.rect.h) * .7f + 10 : 14);
            SpawnCharacter(r, Characters.Get(leader), f, home != null ? FreeTileIn(home, r) : f.home);
            SpawnGroup(r, f, home, f.home, r.Range(2, 5), "enclave");
        }

        void SpawnGroup(Rng r, Faction f, Building home, Vec2 at, int count, string group)
        {
            if (home == null) ClearZombies(at, 16);
            else ClearZombies(Center(home), Math.Max(home.rect.w, home.rect.h) * .7f + 10);
            if (f.leader.Length > 0 && !survivors.Any(s => s.character == f.leader)) SpawnCharacter(r, Characters.Get(f.leader), f, home != null ? FreeTileIn(home, r) : FreeSpotNear(at, 4, r));
            AiRole role = f.role;
            for (int i = 0; i < count; i++) SpawnMember(r, f, home != null ? FreeTileIn(home, r) : FreeSpotNear(at + Vec2.FromAngle(r.Range(0, 6.28f)) * r.Range(1, 5), 4, r), role, group);
        }

        Survivor SpawnMember(Rng r, Faction f, Vec2 pos, AiRole role, string group)
        {
            bool female = r.Chance(.45f);
            string name = FirstNames[r.Range(0, FirstNames.Length)] + " " + LastNames[r.Range(0, LastNames.Length)];
            var s = AddSurvivor(name, group == "soldier" || group == "raider" ? "deputy" : r.Pick(new[] { "unemployed", "carpenter", "farmer", "firefighter", "cook" }), female, null);
            Place(s, f, pos, role);
            Kit(r, s, group);
            return s;
        }

        Survivor SpawnCharacter(Rng r, CharacterDef c, Faction f, Vec2 pos)
        {
            var traits = c.traits.Where(t => Trait.Get(t) != null).ToList(); if (Trait.Validate(traits).Length > 0) traits.Clear();
            var s = AddSurvivor(c.name, c.profession, c.female, null, traits);
            Place(s, f, pos, c.role);
            s.character = c.key; s.disposition = c.disposition;
            // Kit: the first weapon that exists becomes the primary (lists name fallbacks such as assault_rifle, rifle).
            bool armed = false;
            foreach (var id in c.kit)
            {
                if (!ItemCatalog.Exists(id)) continue;
                var d = ItemCatalog.Get(id);
                if (d.IsWeapon && d.category == ItemCategory.Firearm) { if (armed) continue; Arm(s, id); armed = true; }
                else if (d.IsWeapon) GiveItem(s, id);
                else GiveItem(s, id);
            }
            if (!armed) { var melee = s.inventory.items.FirstOrDefault(i => i.Def.category == ItemCategory.Weapon); if (melee != null) s.primary = melee.uid; }
            switch (c.key)
            {
                case "kade": Wear(s, "vest_2", "police_vest"); Wear(s, "helmet_2", "helmet"); GiveFirst(s, "grenade"); break;
                case "sal": Wear(s, "vest_3", "vest_2", "police_vest"); GiveFirst(s, "molotov"); GiveFirst(s, "grenade"); break;
                case "wren": Wear(s, "backpack_2", "hiking_bag"); break;
                case "tess": Wear(s, "backpack_2", "duffel_bag"); Wear(s, "vest_1", "leather_jacket"); break;
                case "jonah": Wear(s, "vest_1", "leather_jacket"); break;
            }
            return s;
        }

        void Place(Survivor s, Faction f, Vec2 pos, AiRole role)
        {
            s.pos = pos; s.facing = 0; s.faction = f.id; s.ai = role; s.disposition = f.disposition;
            s.squad = f.IsCrew || f.key == "red_talons" || f.key == "deserters" || f.enclave ? f.id : -1;
            s.inventory.items.RemoveAll(i => i.def == "county_map" || i.def == "radio");   // the radio story is for the community
            s.hunger = s.thirst = 5;
        }

        /// <summary>Gear by group: raiders and deserters carry guns and vests; crews mostly melee with a chance of a gun.</summary>
        void Kit(Rng r, Survivor s, string group)
        {
            GiveItem(s, Melee[r.Range(0, Melee.Length)]);
            switch (group)
            {
                case "raider":
                    Arm(s, r.Chance(.5f) ? "assault_rifle" : "shotgun", "shotgun", "pistol");
                    Wear(s, r.Chance(.5f) ? "vest_2" : "vest_1", "vest_1", "police_vest");
                    if (r.Chance(.3f)) Wear(s, "helmet_1", "helmet");
                    if (r.Chance(.3f)) GiveFirst(s, r.Chance(.5f) ? "molotov" : "pipe_bomb");
                    break;
                case "soldier":
                    Arm(s, "assault_rifle", "rifle", "pistol");
                    Wear(s, "vest_2", "police_vest"); Wear(s, "helmet_2", "helmet_1", "helmet");
                    if (r.Chance(.5f)) GiveFirst(s, "grenade");
                    break;
                case "enclave":
                    if (r.Chance(.5f)) Arm(s, r.Chance(.5f) ? "pistol" : "shotgun", "pistol");
                    if (r.Chance(.3f)) Wear(s, "vest_1", "police_vest");
                    break;
                default:   // rival crews and wanderers
                    if (r.Chance(.45f)) Arm(s, r.Pick(new[] { "pistol", "revolver", "shotgun", "smg", "hunting_rifle" }), "pistol");
                    if (r.Chance(.3f)) Wear(s, "backpack_1", "school_bag");
                    if (r.Chance(.15f)) Wear(s, "vest_1", "leather_jacket");
                    break;
            }
            if (s.primary < 0) { var melee = s.inventory.items.FirstOrDefault(i => i.Def.category == ItemCategory.Weapon); if (melee != null) s.primary = melee.uid; }
        }

        ItemStack GiveItem(Survivor s, string id, int count = 1) { if (!ItemCatalog.Exists(id)) return null; var item = ItemCatalog.Make(id, count); s.inventory.Add(item); return s.inventory.items.LastOrDefault(i => i.def == id); }
        ItemStack GiveFirst(Survivor s, params string[] ids) { foreach (var id in ids) if (ItemCatalog.Exists(id)) return GiveItem(s, id); return null; }
        /// <summary>Equips the first existing gun, loaded, with two spare magazines.</summary>
        void Arm(Survivor s, params string[] ids)
        {
            var gun = GiveFirst(s, ids); if (gun == null) return;
            var d = gun.Def; s.primary = gun.uid;
            if (d.category != ItemCategory.Firearm) return;
            gun.loaded = d.magazine;
            if (ItemCatalog.Exists(d.ammo)) GiveItem(s, d.ammo, Math.Max(6, d.magazine * 2));
        }
        void Wear(Survivor s, params string[] ids)
        {
            var item = GiveFirst(s, ids); if (item == null || item.Def.slot == WearSlot.None) return;
            s.worn[item.Def.slot] = item.uid;
        }

        static Vec2 Center(Building b) { return new Vec2(b.rect.x + b.rect.w * .5f, b.rect.y + b.rect.h * .5f); }

        /// <summary>A building for a group: the landmark of that kind (matching building kinds first), then any building of those kinds,
        /// then any town building. Prefers candidates at least minDist from the community base; otherwise the farthest one.</summary>
        Building FindHome(Rng r, HashSet<int> used, Vec2 basePos, float minDist, LandmarkKind kind, params BuildingKind[] kinds)
        {
            var tiers = new List<List<Building>>();
            if (gen != null)
                foreach (var lm in gen.landmarks.Where(l => l.kind == kind))
                {
                    var inside = lm.buildings.Where(id => id >= 0 && id < map.buildings.Count).Select(id => map.buildings[id]).ToList();
                    tiers.Add(inside.Where(b => kinds.Contains(b.kind)).ToList());
                    tiers.Add(inside);
                }
            foreach (var k in kinds) tiers.Add(map.buildings.Where(b => b.kind == k).ToList());
            tiers.Add(map.buildings.Where(b => map.ZoneAt(b.rect.x, b.rect.y) == ZoneKind.Town || map.ZoneAt(b.rect.x, b.rect.y) == ZoneKind.Commercial).ToList());
            foreach (var tier in tiers)
            {
                var ok = tier.Where(b => !used.Contains(b.id) && b.rooms.Count > 0 && Vec2.Distance(Center(b), basePos) >= minDist).ToList();
                if (ok.Count == 0) continue;
                var pick = ok[r.Range(0, ok.Count)]; used.Add(pick.id); return pick;
            }
            var far = map.buildings.Where(b => !used.Contains(b.id) && b.rooms.Count > 0).OrderByDescending(b => Vec2.Distance(Center(b), basePos)).FirstOrDefault();
            if (far != null) used.Add(far.id);
            return far;
        }

        /// <summary>Landing zones and towns at least <see cref="CrewSpawnDistance"/> from the base (the farthest ones if none qualify).</summary>
        List<Vec2> CrewPoints(Vec2 basePos)
        {
            var all = new List<Vec2>();
            if (gen != null)
            {
                foreach (var z in gen.landingZones) all.Add(z.Center);
                foreach (var l in gen.landmarks) if (l.kind == LandmarkKind.Town || l.kind == LandmarkKind.Airfield || l.kind == LandmarkKind.Campground) all.Add(l.Center);
            }
            var ok = all.Where(p => Vec2.Distance(p, basePos) >= CrewSpawnDistance + 30).ToList();
            if (ok.Count == 0) ok = all.OrderByDescending(p => Vec2.Distance(p, basePos)).Take(2).ToList();
            return ok;
        }

        /// <summary>A free land point roughly minDist from avoid (used when a landmark is missing).</summary>
        Vec2 FarPoint(Rng r, Vec2 avoid, float minDist)
        {
            Vec2 best = avoid; float bestD = -1;
            for (int i = 0; i < 40; i++)
            {
                var p = new Vec2(r.Range(20, map.width - 20), r.Range(20, map.height - 20));
                if (!OpenGround(p)) continue;
                float d = Vec2.Distance(p, avoid);
                if (d >= minDist && d < minDist + 400) return p;
                if (d > bestD) { bestD = d; best = p; }
            }
            return FreeSpotNear(best, 10, r);
        }

        bool OpenGround(Vec2 p) { int x = (int)p.x, y = (int)p.y; return map.InBounds(x, y) && !map.TileBlocked(x, y) && !map.IsWater(x, y) && !map.Collides(p, Survivor.Radius); }

        /// <summary>A free outdoor spot near p (never water, walls or furniture).</summary>
        Vec2 FreeSpotNear(Vec2 p, float radius, Rng r)
        {
            for (int i = 0; i < 40; i++)
            {
                var q = p + Vec2.FromAngle(r.Range(0, 6.28f)) * r.Range(0, radius + i * .5f);
                q = new Vec2((float)Math.Floor(q.x) + .5f, (float)Math.Floor(q.y) + .5f);
                if (OpenGround(q) && map.RoomAt(q) == null) return q;
            }
            for (int ring = 1; ring < 60; ring++)
                for (int dx = -ring; dx <= ring; dx++)
                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        if (Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
                        var q = new Vec2((float)Math.Floor(p.x) + dx + .5f, (float)Math.Floor(p.y) + dy + .5f);
                        if (OpenGround(q)) return q;
                    }
            return p;
        }

        /// <summary>A random free tile inside one of the building's rooms.</summary>
        Vec2 FreeTileIn(Building b, Rng r)
        {
            var free = new List<Vec2>();
            foreach (int rid in b.rooms)
            {
                var rm = map.rooms[rid];
                for (int x = rm.rect.x; x < rm.rect.MaxX; x++)
                    for (int y = rm.rect.y; y < rm.rect.MaxY; y++)
                    {
                        var q = new Vec2(x + .5f, y + .5f);
                        if (!map.TileBlocked(x, y) && !map.Collides(q, Survivor.Radius) && !survivors.Any(o => Vec2.Distance(o.pos, q) < .7f)) free.Add(q);
                    }
            }
            return free.Count > 0 ? free[r.Range(0, free.Count)] : FreeSpotNear(Center(b), 10, r);
        }

        /// <summary>Enclaves and camps start cleared of the dead.</summary>
        void ClearZombies(Vec2 at, float radius)
        {
            foreach (var z in zombies)
                if (z.Alive && Vec2.Distance(z.pos, at) < radius) { z.state = ZombieState.Dead; if (index != null) index.Remove(z); }
        }
    }
}
