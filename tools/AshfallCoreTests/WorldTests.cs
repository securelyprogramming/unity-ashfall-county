using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;

/// <summary>World checks. Register each case with run("name", () => ...); assert with check(condition, "what").</summary>
static class WorldTests
{
    static Action<bool, string> check;
    static void Check(bool ok, string what) { check(ok, what); }
    static World NewWorld(int size = 1024, int seed = 4242, int zone = -1)
    {
        return World.Create(new SandboxSettings { seed = seed, mapSize = size, landingZone = zone }, "Mara Vale", "paramedic", true);
    }
    static void Ticks(Simulation sim, int n, float boost) { for (int i = 0; i < n; i++) sim.Tick(Simulation.Step, boost); }

    public static void All(Action<string, Action> run, Action<bool, string> check)
    {
        WorldTests.check = check;
        WorldGen big = null; Map bigMap = null;
        run("world-determinism", () => { bigMap = Determinism(out big); });
        run("world-landmarks", () => { Landmarks(NewWorld().map, NewWorld().gen, 1024); if (bigMap != null) Landmarks(bigMap, big, 4096); });
        run("world-landing-zones", () => { var w = NewWorld(); LandingZones(w.map, w.gen); if (bigMap != null) LandingZones(bigMap, big); });
        run("world-start-site", StartSites);
        run("world-items-contract", ItemsContract);
        run("world-loot-rucksacks-tiers", LootRucksacksAndTiers);
        run("world-airdrop", AirdropLands);
        run("world-red-zone", RedZoneShells);
        run("world-events-save-load", EventsSaveLoad);
        run("world-vehicle-depots", VehicleDepots);
    }

    static Map Determinism(out WorldGen gen4096)
    {
        var a = NewWorld(); var b = NewWorld();
        Check(a.map.ground.SequenceEqual(b.map.ground) && a.map.props.Count == b.map.props.Count && a.map.buildings.Count == b.map.buildings.Count, "1024: same seed, same county");
        Check(a.gen.landmarks.Select(l => l.name + l.x + "," + l.y).SequenceEqual(b.gen.landmarks.Select(l => l.name + l.x + "," + l.y)), "1024: same landmarks");
        Check(a.gen.landingZones.Select(z => z.name + z.x).SequenceEqual(b.gen.landingZones.Select(z => z.name + z.x)) && Vec2.Distance(a.survivors[0].pos, b.survivors[0].pos) < .001f, "1024: same zones and start");
        WorldGen g1, g2; var m1 = WorldGen.Generate(777, 4096, out g1); var m2 = WorldGen.Generate(777, 4096, out g2);
        Check(m1.ground.SequenceEqual(m2.ground) && m1.props.Count == m2.props.Count && m1.containers.Count == m2.containers.Count, "4096: same seed, same county");
        Check(g1.landmarks.Select(l => l.name + l.x + "," + l.y + ":" + l.buildings.Count).SequenceEqual(g2.landmarks.Select(l => l.name + l.x + "," + l.y + ":" + l.buildings.Count)), "4096: same landmarks");
        WorldGen g3; var m3 = WorldGen.Generate(778, 4096, out g3);
        Check(!m1.ground.SequenceEqual(m3.ground), "4096: another seed, another county");
        Check(g1.settlements.Count >= 12, "4096 has many towns (" + g1.settlements.Count + ")");
        gen4096 = g1; return m1;
    }

    static readonly LandmarkKind[] Required = { LandmarkKind.MilitaryBase, LandmarkKind.Airfield, LandmarkKind.Hospital, LandmarkKind.RadioStation, LandmarkKind.Garage, LandmarkKind.RaiderCompound,
        LandmarkKind.Warehouse, LandmarkKind.Church, LandmarkKind.School, LandmarkKind.Motel, LandmarkKind.Campground, LandmarkKind.Farmstead };

    static void Landmarks(Map m, WorldGen g, int size)
    {
        string tag = size + ": ";
        foreach (var k in Required) Check(g.landmarks.Any(l => l.kind == k), tag + "landmark exists: " + k);
        Check(g.Find(LandmarkKind.MilitaryBase) != null && g.Find(LandmarkKind.MilitaryBase).name == "Fort Ashby", tag + "Fort Ashby is named");
        Check(g.landmarks.Count(l => l.kind == LandmarkKind.Town) == g.settlements.Count && g.landmarks[0].kind == LandmarkKind.Town, tag + "every settlement is a town landmark, listed first");
        var special = g.landmarks.Where(l => l.kind != LandmarkKind.Town).ToList();
        foreach (var l in special)
        {
            Check(l.buildings.Count > 0 && l.buildings.All(id => l.rect.Expand(1).Contains(m.buildings[id].rect.x, m.buildings[id].rect.y)), tag + l.name + " has its buildings");
            int water = 0; for (int x = l.rect.x; x < l.rect.MaxX; x++) for (int y = l.rect.y; y < l.rect.MaxY; y++) if (m.IsWater(x, y)) water++;
            Check(water == 0, tag + l.name + " is on dry land (" + water + " water tiles)");
            if (l.kind == LandmarkKind.Farmstead) continue;
            // Road access: a road or bridge tile just outside the lot.
            var ring = l.rect.Expand(2); bool road = false;
            for (int x = ring.x; x < ring.MaxX && !road; x++) for (int y = ring.y; y < ring.MaxY && !road; y++)
                    if (!l.rect.Contains(x, y)) { var gr = m.GroundAt(x, y); road = gr == Ground.Road || gr == Ground.Bridge; }
            Check(road, tag + l.name + " has road access");
            int paved = 0; for (int x = l.rect.x; x < l.rect.MaxX; x++) for (int y = l.rect.y; y < l.rect.MaxY; y++) if (m.GroundAt(x, y) == Ground.Road) paved++;
            Check(paved == 0, tag + l.name + " has no public road through it (" + paved + ")");
            foreach (var t in g.settlements) Check(l.rect.DistanceTo(new Vec2(t.x, t.y)) > t.radius, tag + l.name + " stays out of " + t.name);
            foreach (var o in special) if (o != l && o.kind != LandmarkKind.Farmstead) Check(!o.rect.Overlaps(l.rect), tag + l.name + " does not overlap " + o.name);
        }
        foreach (var l in special) foreach (int id in l.buildings)
                Check(m.openings.Any(o => o.building == id && o.kind == Edge.Door && o.exterior), tag + m.buildings[id].name + " has an exterior door");
        // Fort Ashby: fenced military base with barracks, an armory, a motor pool, crates and ammo boxes.
        var fort = g.Find(LandmarkKind.MilitaryBase);
        if (fort != null)
        {
            var kinds = fort.buildings.Select(id => m.buildings[id].kind).ToList();
            Check(kinds.Count(k => k == BuildingKind.Barracks) >= 2 && kinds.Contains(BuildingKind.Armory) && kinds.Contains(BuildingKind.Garage), tag + "Fort Ashby has barracks, an armory and a motor pool");
            Check(m.ZoneAt(fort.x, fort.y) == ZoneKind.Military, tag + "Fort Ashby is a military zone");
            var props = m.props.Where(p => fort.rect.Contains(p.x, p.y)).ToList();
            Check(props.Any(p => p.kind == PropKind.MilitaryCrate && p.container >= 0) && props.Any(p => p.kind == PropKind.AmmoBox && p.container >= 0), tag + "Fort Ashby has military crates and ammo boxes");
            Check(m.containers.Any(c => c.table == "armory" && c.prop >= 0 && fort.rect.Contains(m.props[c.prop].x, m.props[c.prop].y)), tag + "the armory holds armory loot");
            Check(Fenced(m, fort.rect), tag + "Fort Ashby is fenced");
        }
        var air = g.Find(LandmarkKind.Airfield);
        if (air != null)
        {
            int runway = 0; for (int x = air.rect.x; x < air.rect.MaxX; x++) for (int y = air.rect.y; y < air.rect.MaxY; y++) if (m.GroundAt(x, y) == Ground.Runway) runway++;
            Check(runway > 2500, tag + "the airfield has a runway strip (" + runway + " m2)");
            Check(air.buildings.Count(id => m.buildings[id].kind == BuildingKind.Hangar) >= 2 && air.buildings.Any(id => m.buildings[id].name == "Control Tower"), tag + "the airfield has hangars and a tower");
        }
        var hospital = g.Find(LandmarkKind.Hospital);
        if (hospital != null) Check(hospital.buildings.Any(id => m.buildings[id].kind == BuildingKind.Hospital && m.buildings[id].rooms.Count >= 8 && m.buildings[id].rooms.Any(r => m.rooms[r].type == RoomType.Ward)), tag + "the hospital is a large multi-room building with wards");
        var raiders = g.Find(LandmarkKind.RaiderCompound);
        if (raiders != null)
        {
            var props = m.props.Where(p => raiders.rect.Contains(p.x, p.y)).ToList();
            Check(props.Any(p => p.kind == PropKind.Sandbags) && props.Any(p => p.kind == PropKind.Barrier) && props.Count(p => p.kind == PropKind.Tent) >= 3, tag + "the Red Talon Yard has sandbags, barriers and tents");
            Check(Fenced(m, raiders.rect) && raiders.name == "Red Talon Yard", tag + "the Red Talon Yard is fenced");
        }
        Check(g.Find(LandmarkKind.Garage) != null && g.Find(LandmarkKind.Garage).name == "Reed's Garage", tag + "Reed's Garage exists");
        Check(g.Find(LandmarkKind.RadioStation) != null && m.props.Any(p => p.kind == PropKind.RadioSet && g.Find(LandmarkKind.RadioStation).rect.Contains(p.x, p.y)), tag + "the radio station has a radio set");
    }
    static bool Fenced(Map m, Rect r)
    {
        int fence = 0; for (int x = r.x; x < r.MaxX; x++) { if (m.H(x, r.y) == Edge.Fence) fence++; if (m.H(x, r.MaxY) == Edge.Fence) fence++; }
        for (int y = r.y; y < r.MaxY; y++) { if (m.V(r.x, y) == Edge.Fence) fence++; if (m.V(r.MaxX, y) == Edge.Fence) fence++; }
        return fence > (r.w + r.h) * 2 - 12;
    }

    static void LandingZones(Map m, WorldGen g)
    {
        string tag = m.width + ": ";
        Check(g.landingZones.Count >= 8 && g.landingZones.Count <= 12, tag + "8-12 landing zones (" + g.landingZones.Count + ")");
        Check(g.landingZones.Select(z => z.name).Distinct().Count() == g.landingZones.Count, tag + "landing zone names are unique");
        Check(g.landingZones.All(z => z.description.Length > 10 && !m.IsWater(z.x, z.y) && m.InBounds(z.x, z.y)), tag + "landing zones are described and on dry land");
        float minD = float.MaxValue; foreach (var a in g.landingZones) foreach (var b in g.landingZones) if (a != b) minD = Math.Min(minD, Vec2.Distance(a.Center, b.Center));
        Check(minD > m.width * .06f, tag + "landing zones are spread over the county (closest pair " + minD.ToString("0") + " m)");
        Check(g.landingZones.Any(z => z.name == "Fort Ashby") && g.landingZones.Any(z => g.landmarks.Any(l => l.name == z.name && (l.kind == LandmarkKind.Farmstead || l.kind == LandmarkKind.Campground))), tag + "zones include landmarks and countryside");
        Check(!g.landingZones.Any(z => z.name == "Red Talon Yard"), tag + "nobody is dropped into the raider yard");
    }

    static void StartSites()
    {
        var probe = NewWorld();
        for (int i = 0; i < probe.gen.landingZones.Count; i++)
        {
            var w = NewWorld(1024, 4242, i); var zone = w.gen.landingZones[i]; var s = w.survivors[0];
            var home = w.map.BuildingAt(s.pos);
            Check(home != null && WorldGen.HomeCandidate(w.map, w.gen, home), "start at " + zone.name + ": a Farmhouse, House, Church or Warehouse with 3+ rooms (" + (home == null ? "none" : home.kind + "/" + home.rooms.Count) + ")");
            Check(home != null && !w.gen.InHostileLandmark(home.rect.Center), "start at " + zone.name + ": not inside Fort Ashby or the Red Talon Yard");
            Check(home != null && Vec2.Distance(home.rect.Center, zone.Center) < 700, "start at " + zone.name + ": near the zone (" + (home == null ? -1 : Vec2.Distance(home.rect.Center, zone.Center)).ToString("0") + " m)");
            Check(!w.map.Collides(s.pos, Survivor.Radius), "start at " + zone.name + ": on a free tile");
        }
    }

    static void ItemsContract()
    {
        foreach (var r in new[] { "food", "meds", "ammo", "materials", "fuel" })
        {
            var d = ItemCatalog.Get("rucksack_" + r);
            Check(d.resource >= 0 && d.resourceAmount > 0 && d.weight >= 6 && d.stack == 1 && (Resource)d.resource == (Resource)Enum.Parse(typeof(Resource), r, true), "rucksack_" + r + " carries " + r);
        }
        Check(ItemCatalog.Get("plague_cure").plagueCure == 100 && ItemCatalog.Exists("plague_sample"), "plague sample and cure");
        foreach (var id in new[] { "grenade", "molotov", "pipe_bomb" }) { var d = ItemCatalog.Get(id); Check(d.Has("throwable") && d.blastRadius > 0 && d.blastDamage > 0 && d.fuse >= 0 && !d.IsWeapon, id + " is a throwable"); }
        Check(ItemCatalog.Get("pipe_bomb").fuse > ItemCatalog.Get("grenade").fuse, "the pipe bomb has a long fuse (noise lure)");
        Check(ItemCatalog.Get("energy_drink").stamina > 0, "energy drink restores stamina");
        for (int i = 1; i <= 3; i++)
        {
            var v = ItemCatalog.Get("vest_" + i); var h = ItemCatalog.Get("helmet_" + i); var b = ItemCatalog.Get("backpack_" + i);
            Check(v.slot == WearSlot.Torso && v.armor > 0 && h.slot == WearSlot.Head && h.armor > 0, "vest_" + i + " and helmet_" + i + " are armour");
            Check(b.slot == WearSlot.Back && b.capacity > 0, "backpack_" + i + " is a bag");
            if (i > 1) Check(v.armor > ItemCatalog.Get("vest_" + (i - 1)).armor && h.armor > ItemCatalog.Get("helmet_" + (i - 1)).armor && b.capacity > ItemCatalog.Get("backpack_" + (i - 1)).capacity, "level " + i + " gear is better");
        }
        var ar = ItemCatalog.Get("assault_rifle"); var smg = ItemCatalog.Get("smg"); var hr = ItemCatalog.Get("hunting_rifle"); var pistol = ItemCatalog.Get("pistol"); var shotgun = ItemCatalog.Get("shotgun");
        Check(ar.ammo == "ammo_556" && smg.ammo == "ammo_9mm" && hr.ammo == "ammo_308" && ItemCatalog.Get("ammo_556").category == ItemCategory.Ammo && ItemCatalog.Get("ammo_308").category == ItemCategory.Ammo, "guns use the contract ammo");
        var guns = new[] { ar, smg, hr, pistol, shotgun };
        Check(guns.Select(d => d.range).Distinct().Count() == guns.Length && guns.Select(d => d.noise).Distinct().Count() == guns.Length && guns.Select(d => d.accuracy).Distinct().Count() == guns.Length, "guns differ in range, noise and accuracy");
        Check(hr.range > ar.range && ar.range > pistol.range && smg.swingTime < pistol.swingTime && hr.accuracy > smg.accuracy, "rifles reach farther, the SMG fires faster");
        Check(ItemCatalog.All.All(d => d.tier >= 0 && d.tier <= 3), "every item has a tier 0-3");
        Check(ar.tier == 3 && ItemCatalog.Get("vest_3").tier == 3 && ItemCatalog.Get("canned_beans").tier == 0, "tiers follow rarity");
        foreach (var id in new[] { "rifle", "helmet", "police_vest", "fishing_rod", "seeds_carrot", "book_cooking_1", "water_jug", "school_bag", "hiking_bag" }) Check(ItemCatalog.Exists(id), "legacy item still defined: " + id);
    }

    static void LootRucksacksAndTiers()
    {
        var w = NewWorld(2048, 4242); var m = w.map;
        foreach (var c in m.containers) Loot.Roll(c, (ulong)w.settings.seed, 1);
        var perBuilding = new Dictionary<int, int>();
        foreach (var c in m.containers) { if (c.room < 0) continue; int b = m.rooms[c.room].building; int n; perBuilding.TryGetValue(b, out n); perBuilding[b] = n + c.inventory.items.Count(i => i.Def.resource >= 0); }
        var landmarkBuildings = new HashSet<int>(w.gen.landmarks.Where(l => l.kind != LandmarkKind.Town && l.kind != LandmarkKind.Farmstead).SelectMany(l => l.buildings));
        var ordinary = perBuilding.Where(kv => !landmarkBuildings.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        var special = perBuilding.Where(kv => landmarkBuildings.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        float rate = ordinary.Sum() / (float)Math.Max(1, ordinary.Count), landmarkRate = special.Sum() / (float)Math.Max(1, special.Count);
        Console.WriteLine("     rucksacks per searched building: " + rate.ToString("0.00") + " ordinary, " + landmarkRate.ToString("0.00") + " in landmarks");
        Check(rate > .2f && rate < .6f, "about one rucksack per two to four searched buildings (" + rate.ToString("0.00") + " per building)");
        Check(landmarkRate > rate * 1.5f, "landmarks hold more rucksacks (" + landmarkRate.ToString("0.00") + " per building)");
        Func<string, Resource, bool> type = (table, res) => m.containers.Where(c => c.table == table).SelectMany(c => c.inventory.items).Any(i => i.Def.resource == (int)res);
        Check(type("grocery", Resource.Food) && type("pharmacy", Resource.Meds) && type("police", Resource.Ammo) && type("hardware", Resource.Materials) && type("gas", Resource.Fuel), "rucksacks by building type");
        Check(Loot.RucksackChance("armory") > Loot.RucksackChance("kitchen") * 2.5f, "military stores carry more rucksacks than kitchens");
        // Tiers by location.
        Check(Loot.ItemsIn("airdrop").Where(id => ItemCatalog.Get(id).resource < 0).All(id => ItemCatalog.Get(id).tier >= 2), "airdrop loot is tier 2-3 only");
        float Mean(string table) { var items = m.containers.Where(c => c.table == table).SelectMany(c => c.inventory.items).Where(i => i.Def.resource < 0).ToList(); return items.Count == 0 ? 0 : (float)items.Average(i => i.Def.tier); }
        var mil = m.containers.Where(c => c.table == "military" || c.table == "armory").SelectMany(c => c.inventory.items).Where(i => i.Def.resource < 0).ToList();
        Check(mil.Count > 0 && mil.Count(i => i.Def.tier >= 2) > mil.Count / 2, "military loot is mostly tier 2-3 (" + mil.Count(i => i.Def.tier >= 2) + "/" + mil.Count + ")");
        Check(Mean("armory") > Mean("kitchen") + 1, "an armory outranks a kitchen");
        foreach (var t in new[] { "airdrop", "military", "armory", "raider", "hospital", "warehouse", "human" }) Check(Loot.HasTable(t), "loot table exists: " + t);
        var human = new Inventory { capacity = 999 }; var r = new Rng(5, 5); for (int i = 0; i < 30; i++) Loot.RollInto(human, "human", r, 1);
        Check(human.items.Any(i => i.Def.category == ItemCategory.Firearm || i.Def.category == ItemCategory.Ammo), "an armed survivor's body carries guns or ammo");
        Check(Loot.TableNames.Where(t => t != "camp" && t != "office" && t != "school").SelectMany(Loot.ItemsIn).Count(id => id.StartsWith("book_") || id == "fishing_rod" || id == "worms") == 2, "skill books and fishing gear are confined to a few tables");
    }

    static Simulation Quiet(World w, Vec2 p, float radius = 150)
    {
        foreach (var z in w.zombies) if (Vec2.Distance(z.pos, p) < radius) z.state = ZombieState.Dead;
        w.index = new ZombieIndex(w); return new Simulation(w);
    }

    static Airdrop LandDrop(World w, Simulation sim)
    {
        w.nextAirdropAt = w.minutes + 29.9;
        Ticks(sim, 1, 1);
        Check(w.airdrops.Count == 1 && w.events.Any(e => e.kind == EventKind.Airdrop && e.text == "announce") && w.events.Any(e => e.kind == EventKind.Radio && e.text.Contains("supply drop")), "airdrop announced on the radio about 30 minutes ahead");
        var a = w.airdrops[0]; Check(a.landsAt - w.minutes > 25 && a.landsAt - w.minutes < 45, "landing time is ahead (" + (a.landsAt - w.minutes).ToString("0") + " min)");
        for (int i = 0; i < 4000 && !a.landed; i++) { sim.Tick(Simulation.Step, 20); w.survivors[0].health = 100; }
        return a;
    }

    static void AirdropLands()
    {
        var w = NewWorld(); var s = w.survivors[0]; var sim = Quiet(w, s.pos, 600);
        Check(w.nextAirdropAt - w.minutes > 90 && w.nextAirdropAt - w.minutes < 150, "first airdrop about two hours in (" + (w.nextAirdropAt - w.minutes).ToString("0") + " min)");
        var a = LandDrop(w, sim);
        Check(a.landed && w.events.Any(e => e.kind == EventKind.Airdrop && e.text == "plane"), "a plane passes, then the crate lands");
        Check(w.events.Any(e => e.kind == EventKind.Airdrop && e.text == "land"), "landing is announced");
        var prop = w.map.props[a.prop]; var c = w.map.containers[a.container];
        Check(prop.kind == PropKind.SupplyCrate && prop.container == c.id && c.table == "airdrop" && c.generated, "the crate is a supply crate container");
        Check(w.map.RoomAt(prop.x, prop.y) == null && !w.map.IsWater(prop.x, prop.y), "the crate lands outdoors on dry land");
        Check(c.inventory.items.Count >= 3 && c.inventory.items.All(i => i.Def.tier >= 2), "airdrop loot is tier 2+ (" + string.Join(", ", c.inventory.items.Select(i => i.def)) + ")");
        Check(w.nextAirdropAt - a.landsAt >= 6 * 60 - 1 && w.nextAirdropAt - a.landsAt <= 10 * 60 + 1, "next drop 6-10 hours later");
        // Opening: walk up and search it.
        s.pos = new Vec2(prop.x - .6f, prop.y + .5f); if (w.map.Collides(s.pos, Survivor.Radius)) s.pos = new Vec2(prop.x + 1.6f, prop.y + .5f);
        sim.Submit(s.id, new Command { kind = CommandKind.OpenContainer, target = c.id }); Ticks(sim, 2, 1);
        Check(a.opened && w.events.Any(e => e.kind == EventKind.Airdrop && e.text == "open" && e.survivor == s.id), "opening the crate is announced (" + s.lootContainer + ")");
        // Old drops expire after two days.
        w.minutes = a.ExpiresAt + 1; w.nextAirdropAt = w.minutes + 600; Ticks(sim, 1, 1);
        Check(w.airdrops.Count == 0 && prop.removed && w.map.PropAt(prop.x, prop.y) == null, "old drops are cleared after two days");
        // Disabled in the sandbox: nothing is scheduled.
        var off = World.Create(new SandboxSettings { seed = 4242, mapSize = 1024, airdrops = false, redZones = false }, "Mara Vale", "paramedic", true); var os = Quiet(off, off.survivors[0].pos);
        off.nextAirdropAt = off.minutes + 1; off.nextRedZoneAt = off.minutes + 1; Ticks(os, 50, 30);
        Check(off.airdrops.Count == 0 && off.redZone == null, "airdrops and red zones honour the sandbox settings");
    }

    static void RedZoneShells()
    {
        var w = NewWorld(); var s = w.survivors[0]; var sim = Quiet(w, s.pos, 300);
        Check(w.nextRedZoneAt - w.minutes >= 8 * 60 && w.nextRedZoneAt - w.minutes <= 14 * 60, "first red zone 8-14 hours in");
        w.nextRedZoneAt = w.minutes + 29.9; Ticks(sim, 1, 1);
        var z = w.redZone;
        Check(z != null && z.radius >= 60 && z.radius <= 120 && z.Warning(w.minutes), "a red zone is warned");
        if (z == null) return;
        Check(w.events.Any(e => e.kind == EventKind.Artillery && e.text == "warn") && w.events.Any(e => e.kind == EventKind.Radio && e.text.Contains(z.area)), "the warning names the area (" + z.area + ")");
        Check(!w.map.IsWater((int)z.center.x, (int)z.center.y), "the zone is centred on land");
        if (w.community.baseBuilding >= 0) Check(Vec2.Distance(z.center, w.community.basePos) > z.radius, "the zone is not on top of the base");
        // Keep the survivor clear, fill the circle with the dead.
        var away = z.center + new Vec2(z.radius + 200, 0); if (away.x > w.map.width - 10) away = z.center - new Vec2(z.radius + 200, 0); s.pos = away;
        var r = new Rng(1, 1); var targets = new List<Zombie>();
        for (int i = 0; i < 400; i++) { var p = z.center + Vec2.FromAngle(r.Range(0, 6.28f)) * (z.radius * (float)Math.Sqrt(r.Float())); if (w.map.TileBlocked((int)p.x, (int)p.y)) continue; var zz = w.AddZombie(p, r); zz.hp = 1.2f; targets.Add(zz); }
        Check(w.events.Count(e => e.kind == EventKind.Artillery && e.text == "shell") == 0, "no shells during the warning");
        for (int i = 0; i < 20000 && w.redZone != null; i++) { sim.Tick(Simulation.Step, 8); s.health = 100; }
        int shells = w.events.Count(e => e.kind == EventKind.Artillery && e.text == "shell");
        Check(shells >= 10 && shells <= 40, "shelling lasts about 20 minutes, a shell every couple of real seconds (" + shells + " shells)");
        Check(w.events.Any(e => e.kind == EventKind.Explosion && e.text == "artillery"), "shells explode as artillery blasts");
        Check(w.events.Where(e => e.kind == EventKind.Artillery && e.text == "shell").All(e => Vec2.Distance(e.pos, z.center) <= z.radius + .01f), "shells land inside the circle");
        int hurt = targets.Count(t => !t.Alive || t.hp < 1.2f);
        Check(hurt > 0, "the shelling kills and wounds zombies (" + hurt + "/" + targets.Count + ")");
        Check(w.redZone == null && w.events.Any(e => e.kind == EventKind.Artillery && e.text == "clear"), "the zone clears");
        Check(w.nextRedZoneAt - z.endsAt >= 8 * 60 && w.nextRedZoneAt - z.endsAt <= 14 * 60, "the next red zone is 8-14 hours later");
    }

    static void EventsSaveLoad()
    {
        var w = NewWorld(); var sim = Quiet(w, w.survivors[0].pos, 600);
        var a = LandDrop(w, sim);
        w.nextRedZoneAt = w.minutes + 29.9; Ticks(sim, 1, 1);
        var pending = new Airdrop(); w.airdrops.Add(pending); pending.id = 99; pending.pos = new Vec2(100, 100); pending.landsAt = w.minutes + 500; pending.announcedAt = w.minutes;
        string path = Path.Combine(Path.GetTempPath(), "ashfall-events-" + Guid.NewGuid().ToString("N") + ".sav");
        try
        {
            SaveCodec.Save(w, path); var l = SaveCodec.Load(path);
            Check(l.airdrops.Count == 2 && l.airdrops[0].landed && l.airdrops[0].container == a.container && !l.airdrops[1].landed && l.airdrops[1].id == 99, "airdrops restored");
            var lc = l.map.containers[a.container]; var lp = l.map.props[a.prop];
            Check(lp.kind == PropKind.SupplyCrate && !lp.removed && lc.inventory.items.Select(i => i.def).SequenceEqual(w.map.containers[a.container].inventory.items.Select(i => i.def)), "the landed crate and its loot are restored");
            Check(l.redZone != null && Vec2.Distance(l.redZone.center, w.redZone.center) < .01f && l.redZone.radius == w.redZone.radius && l.redZone.area == w.redZone.area && l.redZone.startsAt == w.redZone.startsAt, "the red zone is restored");
            Check(l.nextAirdropAt == w.nextAirdropAt && l.nextRedZoneAt == w.nextRedZoneAt && l.airdropCount == w.airdropCount && l.EventRng.State == w.EventRng.State, "event schedule restored");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    static void VehicleDepots()
    {
        var w = NewWorld();
        foreach (var k in new[] { LandmarkKind.MilitaryBase, LandmarkKind.Garage })
        {
            var l = w.gen.Find(k); if (l == null) continue;
            var cars = w.vehicles.Where(v => l.rect.Contains(v.pos)).ToList();
            Check(cars.Count >= 2 && cars.All(v => v.fuel > Vehicle.TankLiters * .3f && !v.Wrecked), l.name + " has working vehicles (" + cars.Count + ")");
        }
        // The trunk is reachable for deposits (Community.cs).
        var car = w.vehicles[0]; var trunk = w.Trunk(car);
        Check(trunk != null && trunk.generated && trunk.prop == car.prop, "a car's trunk is its prop container");
        trunk.inventory.Add(ItemCatalog.Make("rucksack_food"));
        Check(w.ParkedNear(car.pos, 3).Contains(car) && w.Trunk(car).inventory.Count("rucksack_food") == 1, "rucksacks can be stowed in and found in a parked car's trunk");
    }
}
