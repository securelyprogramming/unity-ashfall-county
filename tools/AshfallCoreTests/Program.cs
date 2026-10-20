using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ashfall.Core;

/// <summary>Plain-dotnet checks for the Ashfall survival simulation. Run: dotnet run --project tools/AshfallCoreTests</summary>
static class Program
{
    static int failures, passes;
    static void Check(bool condition, string what) { if (condition) passes++; else { failures++; Console.WriteLine("FAIL: " + what); } }

    static int Main(string[] args)
    {
        var report = new Dictionary<string, object>();
        bool full = !args.Contains("--quick");
        if (args.Contains("--bench")) { Bench(); return 0; }
        Run("generation-small", () => Generation(1024, report));
        if (full) Run("generation-county", () => Generation(3072, report));
        Run("determinism", Determinism);
        Run("needs", Needs);
        Run("loot", LootRolls);
        Run("melee", Melee);
        Run("zombie-attack", ZombieAttack);
        Run("doors-and-barricades", Barricades);
        Run("crafting", Crafting);
        Run("eat-drink", EatDrink);
        Run("pathfinding", Paths);
        Run("save-load", SaveLoad);
        Run("noise", NoiseAttracts);
        Run("vehicle-setup", () => VehicleTests.Setup(Check));
        Run("vehicle-enter-exit", () => VehicleTests.EnterExit(Check));
        Run("vehicle-drive-fuel", () => VehicleTests.DriveAndFuel(Check));
        Run("vehicle-collisions", () => VehicleTests.Collisions(Check));
        Run("vehicle-save-load", () => VehicleTests.SaveLoad(Check));
        Run("farming", () => SurvivalTests.Farming(Check));
        Run("fishing", () => SurvivalTests.Fishing(Check));
        Run("traits", () => SurvivalTests.Traits(Check));
        Run("skill-books", () => SurvivalTests.SkillBooks(Check));
        Run("survival-save-load", () => SurvivalTests.SaveLoad(Check));
        Run("old-save-refused", () => SurvivalTests.OldSaveRefused(Check));
        // Open-county systems: each area registers its own cases.
        WorldTests.All(Run, Check);
        BestiaryTests.All(Run, Check);
        HumanTests.All(Run, Check);
        CommunityTests.All(Run, Check);
        Console.WriteLine("Ashfall core: " + passes + " checks passed, " + failures + " failed.");
        foreach (var kv in report) Console.WriteLine("  " + kv.Key + " = " + kv.Value);
        return failures == 0 ? 0 : 1;
    }
    static void Run(string name, Action test)
    {
        var watch = Stopwatch.StartNew();
        try { test(); Console.WriteLine("ok   " + name + " (" + watch.ElapsedMilliseconds + " ms)"); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); }
    }

    static World NewWorld(int size = 1024, int seed = 4242)
    {
        return World.Create(new SandboxSettings { seed = seed, mapSize = size }, "Mara Vale", "paramedic", true);
    }

    static void Generation(int size, Dictionary<string, object> report)
    {
        var watch = Stopwatch.StartNew();
        var w = NewWorld(size);
        long ms = watch.ElapsedMilliseconds;
        var m = w.map; var s = w.survivors[0];
        report["county" + size + ".generationMs"] = ms;
        report["county" + size + ".buildings"] = m.buildings.Count;
        report["county" + size + ".rooms"] = m.rooms.Count;
        report["county" + size + ".openings"] = m.openings.Count;
        report["county" + size + ".props"] = m.props.Count;
        report["county" + size + ".containers"] = m.containers.Count;
        report["county" + size + ".zombies"] = w.zombies.Count;
        report["county" + size + ".settlements"] = string.Join("/", w.gen.settlements.Select(x => x.name));
        Check(m.buildings.Count > (size >= 3072 ? 800 : 60), "county has many buildings (" + m.buildings.Count + ")");
        Check(w.zombies.Count > (size >= 3072 ? 3000 : 200), "county is populated (" + w.zombies.Count + ")");
        Check(w.zombies.All(z => !m.IsWater((int)z.pos.x, (int)z.pos.y) && !m.TileBlocked((int)z.pos.x, (int)z.pos.y)), "zombies spawn on open land only");
        Check(!m.Collides(s.pos, Survivor.Radius), "survivor spawns on a free tile");
        Check(m.Indoors(s.pos), "survivor starts indoors");
        Check(w.zombies.All(z => Vec2.Distance(z.pos, s.pos) >= 28), "no zombies right beside the start");
        int noDoor = m.buildings.Count(b => !m.openings.Any(o => o.building == b.id && o.kind == Edge.Door && o.exterior));
        Check(noDoor == 0, "every building has an exterior door (" + noDoor + " without)");
        Check(m.ground.Count(g => g == Ground.Water) > size * 8, "the river exists");
        Check(m.ground.Count(g => g == Ground.Bridge) > 0, "a bridge crosses the river");
        Check(w.gen.settlements.Count >= 4, "several settlements");
        Check(!m.TileBlocked((int)w.gen.evacuationPoint.x, (int)w.gen.evacuationPoint.y), "evacuation point is reachable ground");
        Check(ms < (size >= 3072 ? 20000 : 6000), "generation completes in reasonable time (" + ms + " ms)");
    }

    static void Bench()
    {
        var w = NewWorld(3072, 777); var sim = new Simulation(w); var s = w.survivors[0];
        var town = w.gen.settlements[0]; s.pos = new Vec2(town.x + 30.5f, town.y + 30.5f);
        for (int i = 0; i < 40; i++) sim.Tick(Simulation.Step);
        var watch = Stopwatch.StartNew(); int ticks = 400;
        for (int i = 0; i < ticks; i++) { if (i % 20 == 0) w.MakeNoise(s.pos, 60); sim.SetInput(s.id, new InputState { moveX = (i / 100) % 2 == 0 ? 1 : -1, face = 0 }); sim.Tick(Simulation.Step); s.health = 100; s.alive = true; s.wounds.Clear(); }
        Console.WriteLine("bench: " + (watch.Elapsed.TotalMilliseconds / ticks).ToString("0.00") + " ms/tick, active zombies " + sim.ActiveZombies + ", chasing " + w.zombies.Count(z => z.state == ZombieState.Chase || z.state == ZombieState.Thump));
    }

    static void Determinism()
    {
        var a = NewWorld(); var b = NewWorld();
        Check(a.map.buildings.Count == b.map.buildings.Count && a.map.props.Count == b.map.props.Count && a.zombies.Count == b.zombies.Count, "same seed, same county");
        Check(a.map.ground.SequenceEqual(b.map.ground), "ground identical");
        Check(Vec2.Distance(a.survivors[0].pos, b.survivors[0].pos) < .001f, "same start");
        var c = NewWorld(1024, 99);
        Check(!a.map.ground.SequenceEqual(c.map.ground), "different seed, different county");
    }

    static Simulation Clear(World w)
    {
        // Remove zombies near the survivor so a test controls its own encounters.
        var s = w.survivors[0];
        foreach (var z in w.zombies) if (Vec2.Distance(z.pos, s.pos) < 120) { z.state = ZombieState.Dead; }
        w.index = new ZombieIndex(w);
        return new Simulation(w);
    }

    static void Needs()
    {
        var w = NewWorld(); var sim = Clear(w); var s = w.survivors[0];
        float hunger = s.hunger, thirst = s.thirst;
        double start = w.minutes;
        for (int i = 0; i < 20 * 60 * 4; i++) sim.Tick(Simulation.Step);   // 4 real minutes
        Check(w.minutes > start + 60, "time advances (" + (w.minutes - start).ToString("0") + " game minutes)");
        Check(s.hunger > hunger && s.thirst > thirst, "hunger and thirst rise");
        Check(s.alive && s.health > 95, "idle survivor stays healthy");
    }

    static void LootRolls()
    {
        var w = NewWorld(); var sim = Clear(w);
        int nonEmpty = 0, rolled = 0;
        foreach (var c in w.map.containers.Take(400)) { Loot.Roll(c, (ulong)w.settings.seed, 1); rolled++; if (c.inventory.items.Count > 0) nonEmpty++; }
        Check(nonEmpty > rolled / 3, "containers hold loot (" + nonEmpty + "/" + rolled + ")");
        var w2 = NewWorld(); foreach (var c in w2.map.containers.Take(400)) Loot.Roll(c, (ulong)w2.settings.seed, 1);
        Check(w.map.containers.Take(400).Select(c => c.inventory.items.Count).SequenceEqual(w2.map.containers.Take(400).Select(c => c.inventory.items.Count)), "loot is seeded");
        foreach (var t in Loot.TableNames) foreach (var id in Loot.ItemsIn(t)) Check(ItemCatalog.Exists(id), "loot item exists: " + id);
        // Opening through the simulation requires reach.
        var s = w.survivors[0];
        var far = w.map.containers.First(c => c.prop >= 0 && Vec2.Distance(w.map.props[c.prop].Center, s.pos) > 20);
        sim.Submit(s.id, new Command { kind = CommandKind.OpenContainer, target = far.id }); sim.Tick(Simulation.Step);
        Check(s.lootContainer != far.id, "cannot open distant containers");
    }

    static Survivor Arena(World w, Simulation sim)
    {
        // Teleport the survivor to an open grass tile for combat tests.
        var s = w.survivors[0];
        for (int x = 300; x < 900; x += 3)
            for (int y = 300; y < 900; y += 3)
            {
                bool clear = true;
                for (int dx = -6; dx <= 6 && clear; dx++) for (int dy = -6; dy <= 6 && clear; dy++) { var g = w.map.GroundAt(x + dx, y + dy); if (w.map.TileBlocked(x + dx, y + dy) || w.map.RoomAt(x + dx, y + dy) != null || w.map.H(x + dx, y + dy) != Edge.None || w.map.V(x + dx, y + dy) != Edge.None) clear = false; }
                if (!clear) continue;
                s.pos = new Vec2(x + .5f, y + .5f);
                foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, s.pos) < 120) z.state = ZombieState.Dead;
                w.index = new ZombieIndex(w);
                return s;
            }
        throw new Exception("No open arena tile found.");
    }

    static void Melee()
    {
        var w = NewWorld(); var sim = Clear(w); var s = Arena(w, sim);
        var bat = ItemCatalog.Make("baseball_bat"); s.inventory.Add(bat); s.primary = bat.uid;
        var z = w.AddZombie(s.pos + new Vec2(1.0f, 0), w.rng); z.state = ZombieState.Idle; z.timer = 99; z.hp = 1.2f; z.facing = 3.14f;
        int corpses = w.corpses.Count;
        for (int i = 0; i < 20 * 8 && z.Alive; i++) { sim.SetInput(s.id, new InputState { face = 0, attack = true }); sim.Tick(Simulation.Step); if (z.Alive) { z.stagger = 0; } }
        Check(!z.Alive, "baseball bat kills a zombie");
        Check(w.corpses.Count == corpses + 1, "corpse left behind");
        Check(s.kills == 1, "kill counted");
        Check(bat.condition < 1, "weapon wears");
        // Firearm: loud and lethal.
        var gun = ItemCatalog.Make("shotgun"); gun.loaded = 6; s.inventory.Add(gun); s.primary = gun.uid;
        var z2 = w.AddZombie(s.pos + new Vec2(4f, 0), w.rng); z2.state = ZombieState.Idle; z2.timer = 99;
        for (int i = 0; i < 20 * 8 && z2.Alive; i++) { sim.SetInput(s.id, new InputState { face = 0, attack = true, aim = true }); sim.Tick(Simulation.Step); }
        Check(!z2.Alive, "shotgun kills at range");
        Check(gun.loaded < 6, "ammo is spent");
    }

    static void ZombieAttack()
    {
        var w = NewWorld(); var sim = Clear(w); var s = Arena(w, sim);
        foreach (var kv in s.worn.ToList()) s.worn.Remove(kv.Key);   // no protection
        var z = w.AddZombie(s.pos + new Vec2(6, 0), w.rng); z.state = ZombieState.Idle; z.facing = 3.14f; z.speed = 1.5f;
        sim.SetInput(s.id, new InputState { face = 0 });
        float health = s.health;
        for (int i = 0; i < 20 * 40 && s.wounds.Count == 0 && s.alive; i++) sim.Tick(Simulation.Step);
        Check(z.state == ZombieState.Attack || z.state == ZombieState.Chase || s.wounds.Count > 0, "zombie notices and closes in (" + z.state + ")");
        Check(s.wounds.Count > 0 && s.health < health, "zombie wounds the survivor");
        // Bleeding wounds hurt until bandaged.
        var bleeding = s.wounds.FirstOrDefault(x => x.bleeding);
        if (bleeding != null)
        {
            z.state = ZombieState.Dead; w.index = new ZombieIndex(w); s.knockedDown = 0; s.action = null;
            var band = ItemCatalog.Make("bandage"); s.inventory.Add(band);
            sim.Submit(s.id, new Command { kind = CommandKind.Use, item = s.inventory.Find("bandage").uid });
            for (int i = 0; i < 20 * 6; i++) sim.Tick(Simulation.Step);
            Check(!bleeding.bleeding && bleeding.bandaged, "bandage stops bleeding (alive=" + s.alive + " action=" + (s.action == null ? "none" : s.action.kind.ToString()) + " wounds=" + string.Join("; ", s.wounds.Select(x => x.Label)) + " events=" + string.Join("|", w.events.Where(e => e.kind == EventKind.Message).Select(e => e.text).Reverse().Take(3)) + ")");
        }
    }

    static void Barricades()
    {
        var w = NewWorld(); var sim = Clear(w); var s = w.survivors[0];
        var home = w.map.BuildingAt(s.pos);
        var window = w.map.openings.FirstOrDefault(o => o.building == home.id && o.kind == Edge.Window);
        Check(window != null, "start house has windows");
        if (window == null) return;
        // Stand inside next to the window.
        var inside = w.map.RoomAt(window.Center + (window.vertical ? new Vec2(.5f, 0) : new Vec2(0, .5f))) != null ? window.Center + (window.vertical ? new Vec2(.45f, 0) : new Vec2(0, .45f)) : window.Center - (window.vertical ? new Vec2(.45f, 0) : new Vec2(0, .45f));
        s.pos = inside;
        if (w.map.Collides(s.pos, Survivor.Radius)) { var tile = w.map.RoomAt(inside); }
        s.inventory.Add(ItemCatalog.Make("hammer")); s.inventory.Add(ItemCatalog.Make("plank")); s.inventory.Add(ItemCatalog.Make("plank")); s.inventory.Add(ItemCatalog.Make("nails", 10));
        sim.Submit(s.id, new Command { kind = CommandKind.Barricade, target = window.id });
        for (int i = 0; i < 20 * 5; i++) sim.Tick(Simulation.Step);
        Check(window.barricade == 1, "window barricaded (" + window.barricade + ")");
        Check(s.inventory.Count("plank") == 1 && s.inventory.Count("nails") == 8, "barricade consumes plank and nails");
        Check(window.BlocksMovement && w.map.Collides(window.Center, .2f), "barricade blocks passage");
        // Doors toggle.
        var door = w.map.openings.First(o => o.building == home.id && o.kind == Edge.Door && !o.exterior);
        s.pos = door.Center + (door.vertical ? new Vec2(-.6f, 0) : new Vec2(0, -.6f));
        bool was = door.open;
        sim.Submit(s.id, new Command { kind = CommandKind.ToggleDoor, target = door.id }); sim.Tick(Simulation.Step);
        Check(door.open != was, "interior door toggles");
    }

    static void Crafting()
    {
        var w = NewWorld(); var sim = Clear(w); var s = w.survivors[0];
        s.inventory.Add(ItemCatalog.Make("sheet"));
        sim.Submit(s.id, new Command { kind = CommandKind.Craft, recipe = "rip_sheet" });
        for (int i = 0; i < 20 * 4; i++) sim.Tick(Simulation.Step);
        Check(s.inventory.Count("rag") == 4 && s.inventory.Count("sheet") == 0, "ripping a sheet yields rags");
        sim.Submit(s.id, new Command { kind = CommandKind.Craft, recipe = "nailed_bat" }); sim.Tick(Simulation.Step);
        Check(s.action == null, "missing ingredients refuse crafting");
        foreach (var r in Recipe.All) { foreach (var i in r.inputs) if (!i.IsTag) Check(ItemCatalog.Exists(i.item), "recipe input exists " + i.item); if (!r.places) Check(ItemCatalog.Exists(r.result), "recipe result exists " + r.result); }
    }

    static void EatDrink()
    {
        var w = NewWorld(); var sim = Clear(w); var s = w.survivors[0];
        s.hunger = 60; s.thirst = 60;
        var chips = s.inventory.Find("chips");
        sim.Submit(s.id, new Command { kind = CommandKind.Use, item = chips.uid });
        for (int i = 0; i < 20 * 5; i++) sim.Tick(Simulation.Step);
        Check(s.hunger < 55, "eating reduces hunger (" + s.hunger.ToString("0") + ")");
        var bottle = s.inventory.Find("water_bottle");
        sim.Submit(s.id, new Command { kind = CommandKind.Use, item = bottle.uid });
        for (int i = 0; i < 20 * 4; i++) sim.Tick(Simulation.Step);
        Check(s.thirst < 30 && bottle.water < 10, "drinking reduces thirst (" + s.thirst.ToString("0") + ")");
        var beans = ItemCatalog.Make("canned_beans"); s.inventory.Add(beans);
        sim.Submit(s.id, new Command { kind = CommandKind.Use, item = beans.uid }); sim.Tick(Simulation.Step);
        Check(s.action == null || s.action.duration > 5, "cans need an opener or a slow knife");
    }

    static void Paths()
    {
        var w = NewWorld(); var s = w.survivors[0];
        var home = w.map.BuildingAt(s.pos);
        var door = w.map.openings.First(o => o.building == home.id && o.kind == Edge.Door && o.exterior);
        var outside = door.vertical ? new Int2(door.x + (w.map.RoomAt(door.x, door.y) == null ? 0 : -1), door.y) : new Int2(door.x, door.y + (w.map.RoomAt(door.x, door.y) == null ? 0 : -1));
        var start = new Int2(outside.x + (door.vertical ? (outside.x < door.x ? -3 : 3) : 0), outside.y + (door.vertical ? 0 : (outside.y < door.y ? -3 : 3)));
        var pf = new Pathfinder(w.map); var path = new List<Int2>();
        bool found = pf.Find(start, new Int2((int)s.pos.x, (int)s.pos.y), path);
        Check(found && path.Count > 2, "zombie path reaches inside the house");
    }

    static void SaveLoad()
    {
        var w = NewWorld(); var sim = Clear(w); var s = w.survivors[0];
        for (int i = 0; i < 400; i++) sim.Tick(Simulation.Step);
        var box = w.map.containers.First(); Loot.Roll(box, (ulong)w.settings.seed, 1);
        w.map.openings[0].barricade = 2; w.map.openings[0].barricadeHp = 1;
        var tree = w.map.props.First(p => p.kind == PropKind.Tree); w.map.RemoveProp(tree); tree.modified = true;
        s.inventory.Add(ItemCatalog.Make("axe")); s.kills = 7; s.wounds.Add(new Wound { part = BodyPart.LeftArm, kind = WoundKind.Scratch });
        string path = Path.Combine(Path.GetTempPath(), "ashfall-test-" + Guid.NewGuid().ToString("N") + ".sav");
        SaveCodec.Save(w, path);
        var size = new FileInfo(path).Length;
        var l = SaveCodec.Load(path); var ls = l.survivors[0];
        Check(Math.Abs(l.minutes - w.minutes) < .001, "time restored");
        Check(Vec2.Distance(ls.pos, s.pos) < .001f && ls.kills == 7 && ls.inventory.Count("axe") == 1 && ls.wounds.Count == 1, "survivor restored");
        Check(l.LivingZombies == w.LivingZombies, "zombies restored (" + l.LivingZombies + "/" + w.LivingZombies + ")");
        Check(l.map.openings[0].barricade == 2, "barricade restored");
        Check(l.map.props[tree.id].removed && l.map.PropAt(tree.x, tree.y) == null, "chopped tree stays chopped");
        Check(l.map.containers[box.id].generated && l.map.containers[box.id].inventory.items.Count == box.inventory.items.Count, "looted containers restored");
        var peek = SaveCodec.Peek(path);
        Check(peek.name == "Mara Vale" && peek.kills == 7, "save summary readable");
        Check(size < 8 * 1024 * 1024, "save is compact (" + size / 1024 + " KB)");
        File.Delete(path);
    }

    static void NoiseAttracts()
    {
        var w = NewWorld(); var sim = Clear(w); var s = Arena(w, sim);
        var z = w.AddZombie(s.pos + new Vec2(25, 0), w.rng); z.state = ZombieState.Idle; z.timer = 99; z.facing = 0; z.hearing = 1;
        var gun = ItemCatalog.Make("pistol"); gun.loaded = 3; s.inventory.Add(gun); s.primary = gun.uid;
        sim.SetInput(s.id, new InputState { face = 3.14f, attack = true }); sim.Tick(Simulation.Step); sim.SetInput(s.id, new InputState { face = 3.14f });
        sim.Tick(Simulation.Step);
        Check(z.state == ZombieState.Investigate || z.state == ZombieState.Chase, "gunshot draws distant zombies (" + z.state + ")");
    }
}
