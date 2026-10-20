using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Ashfall.Core;

/// <summary>The survivor model after the State of Decay rebuild: traits, retired Zomboid systems (farming, fishing,
/// skill books, smoking) and the survival save data.</summary>
static class SurvivalTests
{
    static World NewWorld(string profession = "paramedic", string[] traits = null, int seed = 4242)
    {
        return World.Create(new SandboxSettings { seed = seed, mapSize = 1024 }, "Mara Vale", profession, true, traits);
    }

    static Simulation Quiet(World w)
    {
        var s = w.survivors[0];
        foreach (var z in w.zombies) if (Vec2.Distance(z.pos, s.pos) < 200) z.state = ZombieState.Dead;
        w.index = new ZombieIndex(w);
        return new Simulation(w);
    }

    static void Calm(World w)
    {
        foreach (var s in w.survivors) { s.fatigue = 10; s.health = 100; s.wounds.Clear(); }
        foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, w.survivors[0].pos) < 200) { z.state = ZombieState.Dead; w.index.Remove(z); }
    }

    /// <summary>Moves the survivor onto open outdoor ground of the given kind with room around it.</summary>
    static void OpenGround(World w, Ground kind)
    {
        var m = w.map; var s = w.survivors[0];
        for (int x = 40; x < m.width - 40; x += 2)
            for (int y = 40; y < m.height - 40; y += 2)
            {
                bool ok = true;
                for (int dx = -3; dx <= 3 && ok; dx++) for (int dy = -3; dy <= 3 && ok; dy++)
                    {
                        int px = x + dx, py = y + dy;
                        if (m.GroundAt(px, py) != kind || m.TileBlocked(px, py) || m.RoomAt(px, py) != null || m.H(px, py) != Edge.None || m.V(px, py) != Edge.None) ok = false;
                    }
                if (!ok) continue;
                bool props = false; for (int dx = -3; dx <= 3; dx++) for (int dy = -3; dy <= 3; dy++) if (m.props.Any(p => !p.removed && p.x == x + dx && p.y == y + dy)) props = true;
                if (props) continue;
                s.pos = new Vec2(x + .5f, y + .5f); s.facing = 0; return;
            }
        throw new Exception("No open " + kind + " ground found.");
    }

    static void Run(Simulation sim, float seconds, Action each = null)
    {
        for (int i = 0; i < (int)(seconds / Simulation.Step); i++) { sim.SetInput(0, new InputState { face = sim.world.survivors[0].facing }); sim.Tick(Simulation.Step); if (each != null) each(); }
    }

    /// <summary>Skips in-game hours quickly (under an hour per tick so hourly rules still run every hour).</summary>
    static void Hours(Simulation sim, float hours, Action each = null)
    {
        double end = sim.world.minutes + hours * 60;
        while (sim.world.minutes < end) { Calm(sim.world); sim.SetInput(0, new InputState { face = sim.world.survivors[0].facing }); sim.Tick(Simulation.Step, 1500); if (each != null) each(); }
    }

    static void Submit(Simulation sim, Command c) { if (c.recipe == null) c.recipe = ""; sim.Submit(0, c); }
    static string LastMessage(World w) { var e = w.events.LastOrDefault(x => x.kind == EventKind.Message); return e.text ?? ""; }

    /// <summary>Personal farm plots are retired: digging, planting and watering answer with a message; the Garden feeds the base.</summary>
    public static void Farming(Action<bool, string> check)
    {
        var w = NewWorld("farmer"); var sim = Quiet(w); var s = w.survivors[0];
        OpenGround(w, Ground.Grass); Calm(w);
        int fx, fy; sim.FrontTile(s, out fx, out fy);
        check(sim.DigProblem(s, fx, fy).Contains("Garden"), "digging a plot points to the base Garden");
        int props = w.map.props.Count;
        Submit(sim, Command.Of(CommandKind.DigPlot)); Run(sim, 8);
        check(w.map.props.Count == props && s.action == null, "no plot is dug");
        check(LastMessage(w).Contains("Garden"), "the player is told why (" + LastMessage(w) + ")");
        Submit(sim, new Command { kind = CommandKind.Use, item = s.inventory.Find("seeds_carrot").uid }); Run(sim, 1);
        check(LastMessage(w).Contains("Garden"), "seed packets explain the change");
        check(Recipe.Get("stew").Missing(s, true).Count > 0, "cooking is retired");
        Submit(sim, new Command { kind = CommandKind.Cook, item = s.inventory.Find("potato").uid }); Run(sim, 7);
        check(s.inventory.Count("potato") == 1, "cooking does nothing");
    }

    /// <summary>Fishing is retired.</summary>
    public static void Fishing(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var s = w.survivors[0];
        var rod = ItemCatalog.Make("fishing_rod"); s.inventory.Add(rod); s.inventory.Add(ItemCatalog.Make("worms", 5));
        Submit(sim, new Command { kind = CommandKind.Fish, item = rod.uid }); Run(sim, 15);
        check(s.action == null && s.inventory.Count("worms") == 5 && !s.inventory.items.Any(i => i.def.StartsWith("fish_")), "casting does nothing");
        check(LastMessage(w).Contains("Fishing is gone"), "the player is told why");
        Submit(sim, new Command { kind = CommandKind.Use, item = rod.uid }); Run(sim, 1);
        check(LastMessage(w).Contains("Fishing is gone"), "using a rod explains the change");
    }

    public static void Traits(Action<bool, string> check)
    {
        check(Trait.Validate(new[] { "strong", "weak" }) != "", "opposite traits are rejected");
        check(Trait.Validate(new[] { "fast_learner" }) != "", "traits over budget are rejected");
        check(Trait.Validate(new[] { "fast_learner", "smoker" }) == "", "negative traits pay for positive ones");
        check(Trait.Validate(new[] { "strong", "strong" }) != "" && Trait.Validate(new[] { "telepath" }) != "", "duplicate and unknown traits are rejected");
        check(Trait.All.All(t => t.exclusive == "" || Trait.Get(t.exclusive) != null && Trait.Get(t.exclusive).exclusive == t.id), "exclusive pairs are symmetric");
        check(Trait.All.Select(t => t.id).Distinct().Count() == Trait.All.Length, "trait ids are unique");
        bool threw = false; try { NewWorld("paramedic", new[] { "strong", "eagle_eyed" }); } catch (ArgumentException) { threw = true; }
        check(threw, "the host refuses an over-budget survivor");

        var plain = NewWorld(); var strong = NewWorld("paramedic", new[] { "strong" }); var weak = NewWorld("paramedic", new[] { "weak" });
        check(Math.Abs(strong.survivors[0].Capacity - plain.survivors[0].Capacity - 4) < .01f && Math.Abs(plain.survivors[0].Capacity - weak.survivors[0].Capacity - 4) < .01f, "Strong and Weak change carry capacity");
        var marks = NewWorld("paramedic", new[] { "marksman" }).survivors[0];
        check(marks.Level(Skill.Aiming) == 2 && NewWorld("paramedic", new[] { "brawler" }).survivors[0].Level(Skill.Melee) == 2, "Marksman and Brawler raise shooting and fighting");

        // Fast learner gains 30% more from the same craft.
        float plainXp = RipXp(plain), fastXp = RipXp(NewWorld("paramedic", new[] { "fast_learner", "smoker" })), slowXp = RipXp(NewWorld("paramedic", new[] { "slow_learner" }));
        check(Math.Abs(fastXp - plainXp * 1.3f) < .01f && Math.Abs(slowXp - plainXp * .7f) < .01f, "learning traits scale experience (" + plainXp + " / " + fastXp + " / " + slowXp + ")");

        // Short sighted / eagle eyed: a zombie 25 m away in daylight.
        float seen = Sight(null), shortSeen = Sight("short_sighted"), eagleSeen = Sight("eagle_eyed");
        check(seen == 1 && shortSeen == 0, "Short Sighted survivors miss a zombie at 25 m (" + seen + "/" + shortSeen + ")");
        check(eagleSeen == 1, "Eagle Eyed survivors see it");

        // Light sleeper wakes for a zombie chasing 10 m away.
        check(!WakesAt10m(null) && WakesAt10m("light_sleeper"), "Light Sleeper wakes when zombies are 10 m away");

        // Stamina and recovery traits.
        check(StaminaAfterRest(new[] { "smoker" }) < StaminaAfterRest(null), "Smokers get their breath back slower");
        check(InjuryHealed(new[] { "quick_healer" }) > InjuryHealed(null) * 1.3f && InjuryHealed(new[] { "slow_healer" }) < InjuryHealed(null), "Quick and Slow Healer change recovery");
        // Community traits: upkeep and morale.
        var eater = NewWorld("paramedic", new[] { "light_eater" }).survivors[0]; var hearty = NewWorld("paramedic", new[] { "hearty_appetite" }).survivors[0];
        check(Trait.Upkeep(eater) == .5f && Trait.Upkeep(hearty) == 1.5f && Trait.Upkeep(plain.survivors[0]) == 1, "appetite traits change the daily Food ration");
        check(Trait.Morale(NewWorld("paramedic", new[] { "cheerful" }).survivors[0]) == 5, "Cheerful members lift morale");
        var sick = NewWorld("paramedic", new[] { "hardy" }); var ss = Quiet(sick); ss.InfectPlague(sick.survivors[0]); Hours(ss, 10);
        var soft = NewWorld(); var sf = Quiet(soft); sf.InfectPlague(soft.survivors[0]); Hours(sf, 10);
        check(sick.survivors[0].plague < soft.survivors[0].plague * .8f, "Hardy slows the blood plague (" + sick.survivors[0].plague.ToString("0") + " vs " + soft.survivors[0].plague.ToString("0") + ")");
    }

    static float StaminaAfterRest(string[] traits)
    {
        var w = NewWorld("paramedic", traits); var sim = Quiet(w); var s = w.survivors[0]; s.endurance = 10;
        Run(sim, 4); return s.endurance;
    }
    static float InjuryHealed(string[] traits)
    {
        var w = NewWorld("paramedic", traits); var sim = Quiet(w); var s = w.survivors[0]; s.injury = 40; s.health = 60;
        double end = w.minutes + 3 * 60; while (w.minutes < end) sim.Tick(Simulation.Step, 1500);
        return 40 - s.injury;
    }
    static float RipXp(World w)
    {
        var sim = Quiet(w); var s = w.survivors[0]; s.inventory.Add(ItemCatalog.Make("sheet"));
        float before = s.xp[(int)Skill.FirstAid];
        Submit(sim, new Command { kind = CommandKind.Craft, recipe = "rip_sheet" }); Run(sim, 4);
        return s.xp[(int)Skill.FirstAid] - before;
    }
    static float Sight(string trait)
    {
        var w = NewWorld("paramedic", trait == null ? null : new[] { trait }); var sim = Quiet(w); OpenGround(w, Ground.Grass);
        var s = w.survivors[0]; w.minutes = 12 * 60; w.raining = false; w.fog = 0;
        var z = w.AddZombie(s.pos + new Vec2(25, 0), w.rng); z.state = ZombieState.Idle; z.timer = 99;
        w.index = new ZombieIndex(w);
        for (int i = 0; i < 6; i++) { w.raining = false; sim.SetInput(0, new InputState { face = 0 }); sim.Tick(Simulation.Step); }
        return sim.IsVisible(s.id, z.id) ? 1 : 0;
    }
    static bool WakesAt10m(string trait)
    {
        var w = NewWorld("paramedic", trait == null ? null : new[] { trait }); var sim = Quiet(w); OpenGround(w, Ground.Grass);
        var s = w.survivors[0]; s.fatigue = 80;
        s.sleeping = true; s.action = new TimedAction { kind = ActionKind.Sleep, duration = 1e9f };
        var z = w.AddZombie(s.pos + new Vec2(10, 0), w.rng); z.state = ZombieState.Chase; z.targetSurvivor = s.id; z.speed = 0;
        sim.Tick(Simulation.Step);
        return !s.sleeping;
    }

    /// <summary>Skill books and smoking are retired: skills only level by use.</summary>
    public static void SkillBooks(Action<bool, string> check)
    {
        var w = NewWorld("paramedic", new[] { "smoker" }); var sim = Quiet(w); var s = w.survivors[0];
        var book = ItemCatalog.Make("book_firstaid_1"); s.inventory.Add(book);
        Submit(sim, new Command { kind = CommandKind.Use, item = book.uid }); Run(sim, 21);
        check(s.action == null && s.booksRead.Count == 0 && Simulation.BookMultiplier(s, Skill.FirstAid) == 1, "skill books are not studied");
        check(LastMessage(w).Contains("Skills improve"), "the player is told why");
        float xp = s.xp[(int)Skill.FirstAid]; RipXp(w);
        check(s.xp[(int)Skill.FirstAid] > xp, "skills still level by use");
        int smokes = s.inventory.Count("cigarettes");
        Submit(sim, new Command { kind = CommandKind.Use, item = s.inventory.Find("cigarettes").uid }); Run(sim, 5);
        check(s.inventory.Count("cigarettes") == smokes, "cigarettes are not smoked");
        s.panic = 0; Hours(sim, 12);
        check(s.panic < 5 && s.hoursSinceSmoke == 0 && s.boredom == 0, "no cravings, boredom or panic from smoking");
    }

    public static void SaveLoad(Action<bool, string> check)
    {
        var w = NewWorld("farmer", new[] { "green_thumb", "smoker", "short_sighted" }); var sim = Quiet(w); var s = w.survivors[0];
        Run(sim, 1);
        s.xp[(int)Skill.Aiming] = 12; s.level[(int)Skill.Aiming] = 2; s.fatigue = 44; s.endurance = 63; s.injury = 17; s.health = 70;
        string path = Path.Combine(Path.GetTempPath(), "ashfall-survival-" + Guid.NewGuid().ToString("N") + ".sav");
        try
        {
            SaveCodec.Save(w, path);
            var l = SaveCodec.Load(path); var ls = l.survivors[0];
            check(ls.traits.SequenceEqual(s.traits), "traits restored (" + string.Join(",", ls.traits) + ")");
            check(ls.Level(Skill.Aiming) == 2 && Math.Abs(ls.xp[(int)Skill.Aiming] - 12) < .001f && ls.Level(Skill.Farming) == s.Level(Skill.Farming), "skills restored");
            check(Math.Abs(ls.fatigue - 44) < .01f && Math.Abs(ls.endurance - 63) < .01f && Math.Abs(ls.injury - 17) < .01f && Math.Abs(ls.health - 70) < .01f, "fatigue, stamina, injury and health restored");
            check(l.survivors.Count == w.survivors.Count && l.survivors.Skip(1).All(x => Trait.Validate(x.traits) == ""), "every member's traits restored");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    public static void OldSaveRefused(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "ashfall-v1-" + Guid.NewGuid().ToString("N") + ".sav");
        try
        {
            using (var file = File.Create(path)) using (var zip = new GZipStream(file, CompressionLevel.Fastest)) using (var b = new BinaryWriter(zip)) { b.Write(0x41534846u); b.Write(1); b.Write(WorldGen.Version); b.Write(new byte[64]); }
            string load = "", peek = "";
            try { SaveCodec.Load(path); } catch (InvalidDataException e) { load = e.Message; }
            try { SaveCodec.Peek(path); } catch (InvalidDataException e) { peek = e.Message; }
            check(load.Contains("older build") && load.Contains("save version 1"), "loading a version 1 save is refused clearly (" + load + ")");
            check(peek == load, "the load menu shows the same refusal");
            check(SaveCodec.Version == 3, "save version is 3");
            check(File.Exists(path), "refusing leaves the old save untouched");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
