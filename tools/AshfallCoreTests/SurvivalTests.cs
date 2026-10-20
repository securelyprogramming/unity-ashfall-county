using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Ashfall.Core;

/// <summary>Farming, fishing, traits, skill books and their save data.</summary>
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
        foreach (var s in w.survivors) { s.hunger = s.thirst = 10; s.fatigue = 10; s.health = 100; s.wounds.Clear(); }
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

    public static void Farming(Action<bool, string> check)
    {
        var w = NewWorld("farmer"); var sim = Quiet(w); var s = w.survivors[0];
        check(s.inventory.FindTag("dig") != null && s.inventory.Count("seeds_carrot") == 3 && s.inventory.Find("watering_can") != null, "farmer starts with a shovel, seeds and a watering can");
        check(s.Level(Skill.Farming) == 2, "farmer has Farming 2");
        // Indoors: refused.
        Submit(sim, Command.Of(CommandKind.DigPlot)); sim.Tick(Simulation.Step);
        check(s.action == null, "cannot dig a plot indoors");

        OpenGround(w, Ground.Grass); Calm(w);
        int fx, fy; sim.FrontTile(s, out fx, out fy);
        check(sim.DigProblem(s, fx, fy) == "", "open grass in front can be dug (" + sim.DigProblem(s, fx, fy) + ")");
        var shovel = s.inventory.FindTag("dig"); s.inventory.items.Remove(shovel); if (s.primary == shovel.uid) s.primary = -1;
        Submit(sim, Command.Of(CommandKind.DigPlot)); sim.Tick(Simulation.Step);
        check(s.action == null, "digging needs a shovel");
        s.inventory.Add(shovel);
        Submit(sim, Command.Of(CommandKind.DigPlot)); Run(sim, 8);
        var plotProp = w.map.props.LastOrDefault(p => p.kind == PropKind.FarmPlot);
        check(plotProp != null && plotProp.x == fx && plotProp.y == fy, "a farm plot is dug on the faced tile");
        if (plotProp == null) return;
        var plot = sim.PlotRef(plotProp.id);
        check(plot != null && plot.Empty, "plot starts empty");
        check(!w.map.Collides(new Vec2(fx + .5f, fy + .5f), .2f), "plots do not block movement");
        check(sim.DigProblem(s, fx, fy) != "", "the same tile cannot be dug twice");

        // Plant, water.
        int seeds = s.inventory.Count("seeds_carrot");
        Submit(sim, new Command { kind = CommandKind.Plant, target = plotProp.id, item = s.inventory.Find("seeds_carrot").uid }); Run(sim, 4.5f);
        check(plot.crop == "carrot" && s.inventory.Count("seeds_carrot") == seeds - 1, "planting uses one seed packet (" + plot.crop + ")");
        check(plot.care > 1.07f, "planter's Farming skill speeds growth (" + plot.care + ")");
        Submit(sim, new Command { kind = CommandKind.Plant, target = plotProp.id, item = s.inventory.Find("seeds_carrot").uid }); sim.Tick(Simulation.Step);
        check(s.action == null, "cannot plant into an occupied plot");
        var can = s.inventory.Find("watering_can"); can.water = 20;
        float before = plot.water;
        Submit(sim, new Command { kind = CommandKind.WaterPlot, target = plotProp.id, item = can.uid }); Run(sim, 3.5f);
        check(plot.water >= .95f && can.water < 20 && can.water >= 10, "watering soaks the plot from the can (" + plot.water.ToString("0.00") + ", can " + can.water + ")");

        // Grow: keep watered (refill as a player would). Must not ripen instantly; must ripen within the crop's days.
        double planted = w.minutes; double ripeAt = 0;
        while (w.minutes - planted < 6 * 1440 && !plot.Ripe)
        {
            Hours(sim, 1, () => w.raining = false);
            if (plot.water < .3f) { can.water = 20; Submit(sim, new Command { kind = CommandKind.WaterPlot, target = plotProp.id, item = can.uid }); Run(sim, 3.5f); }
        }
        ripeAt = (w.minutes - planted) / 1440;
        check(plot.Ripe, "watered carrots ripen (growth " + plot.growth.ToString("0.00") + ")");
        check(ripeAt > 2 && ripeAt < 3.5, "carrots take about three in-game days (" + ripeAt.ToString("0.00") + " days)");
        check(plot.health > .9f, "well-watered crop stays healthy (" + plot.health.ToString("0.00") + ")");

        int carrots = s.inventory.Count("carrots"); float xp = s.xp[(int)Skill.Farming] + s.level[(int)Skill.Farming] * 1000;
        Submit(sim, new Command { kind = CommandKind.Harvest, target = plotProp.id }); Run(sim, 5);
        check(s.inventory.Count("carrots") >= carrots + 2, "harvest yields carrots (" + (s.inventory.Count("carrots") - carrots) + ")");
        check(plot.Empty, "harvested plot is empty again");
        check(s.xp[(int)Skill.Farming] + s.level[(int)Skill.Farming] * 1000 > xp, "harvesting trains Farming");

        // Unwatered crops wilt and die; dead plants can be cleared.
        Submit(sim, new Command { kind = CommandKind.Plant, target = plotProp.id, item = s.inventory.Find("seeds_potato").uid }); Run(sim, 4.5f);
        plot.water = 0;
        Hours(sim, 40, () => { w.raining = false; });
        check(plot.Dead, "an unwatered crop withers (" + plot.Stage + ", health " + plot.health.ToString("0.00") + ")");
        Submit(sim, new Command { kind = CommandKind.Harvest, target = plotProp.id }); Run(sim, 3);
        check(plot.Empty, "dead plants can be cleared");

        // Rain waters plots.
        plot.water = 0;
        Hours(sim, 2, () => { w.raining = true; w.rainIntensity = 1; });
        check(plot.water > .4f, "rain waters the plot (" + plot.water.ToString("0.00") + ")");
        w.raining = false;

        // Paved ground cannot be dug.
        OpenGround(w, Ground.Road); Calm(w); sim.FrontTile(s, out fx, out fy);
        check(sim.DigProblem(s, fx, fy) != "", "roads cannot be dug");

        // Seeds are lootable and every crop is consistent.
        check(Loot.ItemsIn("hardware").Contains("seeds_tomato") && Loot.ItemsIn("barn").Contains("watering_can"), "seeds and watering cans are in loot tables");
        foreach (var c in CropDef.All) check(ItemCatalog.Exists(c.seed) && ItemCatalog.Get(c.seed).Has("seeds") && ItemCatalog.Exists(c.produce) && ItemCatalog.Get(c.produce).Edible, "crop " + c.id + " has seeds and edible produce");
    }

    public static void Fishing(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var s = w.survivors[0];
        var rod = ItemCatalog.Make("fishing_rod");
        Submit(sim, new Command { kind = CommandKind.Fish, item = -1 }); sim.Tick(Simulation.Step);
        check(s.action == null, "fishing needs a rod");
        s.inventory.Add(rod);
        Submit(sim, new Command { kind = CommandKind.Fish, item = rod.uid }); sim.Tick(Simulation.Step);
        check(s.action == null, "fishing indoors far from water is refused");

        // A bank tile: outdoor land with water within two steps.
        var m = w.map; bool placed = false;
        for (int x = 4; x < m.width - 4 && !placed; x++)
            for (int y = 4; y < m.height - 4 && !placed; y++)
            {
                if (m.TileBlocked(x, y) || m.RoomAt(x, y) != null || m.GroundAt(x, y) == Ground.Bridge) continue;
                if (!m.IsWater(x + 2, y) || m.IsWater(x + 1, y)) continue;
                s.pos = new Vec2(x + .5f, y + .5f); placed = !m.Collides(s.pos, Survivor.Radius);
            }
        check(placed, "found a river or pond bank");
        Calm(w);
        check(sim.FishingSpot(s), "the bank is a fishing spot");
        Submit(sim, new Command { kind = CommandKind.Fish, item = rod.uid }); sim.Tick(Simulation.Step);
        check(s.action == null, "fishing needs bait");
        s.inventory.Add(ItemCatalog.Make("worms", 12));
        int caught = 0, attempts = 0;
        for (int i = 0; i < 12; i++)
        {
            int fish = s.inventory.items.Where(it => it.def.StartsWith("fish_")).Sum(it => it.count);
            Submit(sim, new Command { kind = CommandKind.Fish, item = rod.uid });
            sim.Tick(Simulation.Step); if (s.action != null && s.action.kind == ActionKind.Fish) attempts++;
            Run(sim, 15, () => Calm(w));
            if (s.inventory.items.Where(it => it.def.StartsWith("fish_")).Sum(it => it.count) > fish) caught++;
        }
        check(attempts == 12, "every cast starts (" + attempts + ")");
        check(s.inventory.Count("worms") == 0, "each cast uses a worm (" + s.inventory.Count("worms") + " left)");
        check(caught >= 2 && caught <= 11, "some casts catch fish (" + caught + "/12)");
        check(s.xp[(int)Skill.Fishing] > 0 || s.Level(Skill.Fishing) > 0, "fishing trains Fishing");
        check(rod.condition < 1, "the rod wears");
        // Lures are reusable.
        s.inventory.Add(ItemCatalog.Make("lure"));
        Submit(sim, new Command { kind = CommandKind.Fish, item = rod.uid }); Run(sim, 15, () => Calm(w));
        check(s.action == null && s.inventory.Count("lure") <= 1, "a lure works as bait");

        // Raw fish cooks over a fire; eating it raw makes you sick.
        var raw = s.inventory.items.FirstOrDefault(it => it.def.StartsWith("fish_") && !it.def.EndsWith("_cooked"));
        if (raw == null) { raw = ItemCatalog.Make("fish_bass"); s.inventory.Add(raw); }
        check(raw.Def.raw && ItemCatalog.Exists(raw.Def.cooksInto), "fish must be cooked");
        string cooked = raw.Def.cooksInto;
        var fire = PlaceNear(w, sim, s, PropKind.Campfire); fire.lit = true; fire.fuel = 3;
        check(sim.NearHeat(s), "campfire beside the bank is a heat source");
        Submit(sim, new Command { kind = CommandKind.Cook, item = raw.uid }); Run(sim, 7, () => Calm(w));
        check(s.inventory.Count(cooked) >= 1, "fish cooks into " + cooked);
        var sushi = ItemCatalog.Make("fish_perch"); s.inventory.Add(sushi); s.sickness = 0; s.hunger = 50;
        Submit(sim, new Command { kind = CommandKind.Use, item = sushi.uid }); Run(sim, 4);
        check(s.sickness > 0, "raw fish makes you sick");
        check(Recipe.Get("stew").anyOf.Contains("fish_bass"), "fish goes in stew");
    }

    static Prop PlaceNear(World w, Simulation sim, Survivor s, PropKind kind)
    {
        int sx = (int)Math.Floor(s.pos.x), sy = (int)Math.Floor(s.pos.y);
        foreach (var d in new[] { new Int2(1, 0), new Int2(-1, 0), new Int2(0, 1), new Int2(0, -1), new Int2(1, 1), new Int2(-1, -1) })
        {
            int x = sx + d.x, y = sy + d.y;
            if (w.map.TileBlocked(x, y) || sim.NonBlockingPropAt(x, y) != null) continue;
            var p = w.map.AddProp(kind, x, y); p.modified = true; return p;
        }
        throw new Exception("No room for a " + kind);
    }

    public static void Traits(Action<bool, string> check)
    {
        check(Trait.Validate(new[] { "strong", "weak" }) != "", "opposite traits are rejected");
        check(Trait.Validate(new[] { "fast_learner" }) != "", "traits over budget are rejected");
        check(Trait.Validate(new[] { "fast_learner", "smoker" }) == "", "negative traits pay for positive ones");
        check(Trait.Validate(new[] { "strong", "strong" }) != "" && Trait.Validate(new[] { "telepath" }) != "", "duplicate and unknown traits are rejected");
        check(Trait.All.All(t => t.exclusive == "" || Trait.Get(t.exclusive) != null && Trait.Get(t.exclusive).exclusive == t.id), "exclusive pairs are symmetric");
        bool threw = false; try { NewWorld("paramedic", new[] { "strong", "eagle_eyed" }); } catch (ArgumentException) { threw = true; }
        check(threw, "the host refuses an over-budget survivor");

        var plain = NewWorld(); var strong = NewWorld("paramedic", new[] { "strong" }); var weak = NewWorld("paramedic", new[] { "weak" });
        check(Math.Abs(strong.survivors[0].Capacity - plain.survivors[0].Capacity - 4) < .01f && Math.Abs(plain.survivors[0].Capacity - weak.survivors[0].Capacity - 4) < .01f, "Strong and Weak change carry capacity");
        var green = NewWorld("paramedic", new[] { "green_thumb", "angler" }).survivors[0];
        check(green.Level(Skill.Farming) == 1 && green.Level(Skill.Fishing) == 1, "Green Thumb and Angler grant a skill level");

        // Fast learner gains 30% more from the same craft.
        float plainXp = RipXp(plain), fastXp = RipXp(NewWorld("paramedic", new[] { "fast_learner", "smoker" })), slowXp = RipXp(NewWorld("paramedic", new[] { "slow_learner" }));
        check(Math.Abs(fastXp - plainXp * 1.3f) < .01f && Math.Abs(slowXp - plainXp * .7f) < .01f, "learning traits scale experience (" + plainXp + " / " + fastXp + " / " + slowXp + ")");

        // Short sighted / eagle eyed: a zombie 25 m away in daylight.
        float seen = Sight(null), shortSeen = Sight("short_sighted"), eagleSeen = Sight("eagle_eyed");
        check(seen == 1 && shortSeen == 0, "Short Sighted survivors miss a zombie at 25 m (" + seen + "/" + shortSeen + ")");
        check(eagleSeen == 1, "Eagle Eyed survivors see it");

        // Light sleeper wakes for a zombie chasing 10 m away.
        check(!WakesAt10m(null) && WakesAt10m("light_sleeper"), "Light Sleeper wakes when zombies are 10 m away");

        // Smoker: starts with cigarettes, craves after 8 hours, smoking resets.
        var sw = NewWorld("paramedic", new[] { "smoker" }); var sim = Quiet(sw); var s = sw.survivors[0];
        check(s.inventory.Count("cigarettes") == 12 && s.inventory.FindTag("fire") != null, "Smoker starts with cigarettes and a lighter");
        s.boredom = 0; Hours(sim, 12, () => { s.boredom = Math.Min(s.boredom, 100); });
        check(s.hoursSinceSmoke > 11 && s.panic >= 10, "an unsatisfied smoker panics (" + s.panic.ToString("0") + ")");
        check(sw.events.Any(e => e.kind == EventKind.Message && e.text.Contains("craving")), "the craving is announced");
        float boredom = s.boredom;
        Submit(sim, new Command { kind = CommandKind.Use, item = s.inventory.Find("cigarettes").uid }); Run(sim, 5);
        check(s.inventory.Count("cigarettes") == 11 && s.hoursSinceSmoke < .2f && s.boredom < boredom, "smoking a cigarette satisfies the craving");
        // Non-smokers do not crave.
        var nw = NewWorld(); var ns = Quiet(nw); nw.survivors[0].panic = 0; Hours(ns, 12);
        check(nw.survivors[0].panic < 5, "non-smokers do not crave");
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

    public static void SkillBooks(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var s = w.survivors[0];
        var book = ItemCatalog.Make("book_firstaid_1"); s.inventory.Add(book);
        check(Simulation.BookMultiplier(s, Skill.FirstAid) == 1, "no multiplier before reading");
        float plain = RipOnce(sim, s);
        Submit(sim, new Command { kind = CommandKind.Use, item = book.uid }); sim.Tick(Simulation.Step);
        check(s.action != null && s.action.kind == ActionKind.ReadBook, "reading a skill book is a timed action");
        Run(sim, 21);
        check(s.booksRead.Contains("book_firstaid_1"), "the book is remembered after reading");
        check(s.inventory.Count("book_firstaid_1") == 1, "the book is kept");
        check(Simulation.BookMultiplier(s, Skill.FirstAid) == 3, "Vol. 1 triples First Aid experience at level " + s.Level(Skill.FirstAid));
        float boosted = RipOnce(sim, s);
        check(Math.Abs(boosted - plain * 3) < .01f, "crafting after reading earns triple experience (" + plain + " -> " + boosted + ")");
        check(Simulation.BookMultiplier(s, Skill.Carpentry) == 1, "the book only affects its own skill");
        Submit(sim, new Command { kind = CommandKind.Use, item = book.uid }); sim.Tick(Simulation.Step);
        check(s.action == null, "a book cannot be studied twice");
        s.level[(int)Skill.FirstAid] = 5;
        check(Simulation.BookMultiplier(s, Skill.FirstAid) == 1, "Vol. 1 stops helping at level 5");
        s.booksRead.Add("book_firstaid_2");
        check(Simulation.BookMultiplier(s, Skill.FirstAid) == 4, "Vol. 2 quadruples experience for levels 5-9");
        // Darkness outdoors stops reading.
        var vol2 = ItemCatalog.Make("book_farming_1"); s.inventory.Add(vol2);
        OpenGround(w, Ground.Grass); w.minutes = 23 * 60; s.lightOn = false;
        Submit(sim, new Command { kind = CommandKind.Use, item = vol2.uid }); sim.Tick(Simulation.Step);
        check(s.action == null, "too dark to read outdoors at night without a light");
        foreach (var d in ItemCatalog.All.Where(d => d.bookSkill >= 0)) check(Loot.TableNames.Any(t => Loot.ItemsIn(t).Contains(d.id)), "book " + d.id + " can be found");
    }
    static float RipOnce(Simulation sim, Survivor s)
    {
        s.inventory.Add(ItemCatalog.Make("sheet")); int lvl = s.level[(int)Skill.FirstAid]; float before = s.xp[(int)Skill.FirstAid];
        Submit(sim, new Command { kind = CommandKind.Craft, recipe = "rip_sheet" }); Run(sim, 4);
        return s.level[(int)Skill.FirstAid] == lvl ? s.xp[(int)Skill.FirstAid] - before : float.NaN;
    }

    public static void SaveLoad(Action<bool, string> check)
    {
        var w = NewWorld("farmer", new[] { "green_thumb", "smoker", "short_sighted" }); var sim = Quiet(w); var s = w.survivors[0];
        OpenGround(w, Ground.Grass); Calm(w);
        Submit(sim, Command.Of(CommandKind.DigPlot)); Run(sim, 8);
        var prop = w.map.props.Last(p => p.kind == PropKind.FarmPlot); var plot = sim.PlotRef(prop.id);
        plot.crop = "tomato"; plot.growth = .42f; plot.water = .66f; plot.health = .8f; plot.care = 1.23f;
        s.booksRead.Add("book_cooking_1"); s.hoursSinceSmoke = 3.5f; s.xp[(int)Skill.Fishing] = 12; s.level[(int)Skill.Fishing] = 2;
        string path = Path.Combine(Path.GetTempPath(), "ashfall-survival-" + Guid.NewGuid().ToString("N") + ".sav");
        try
        {
            SaveCodec.Save(w, path);
            var l = SaveCodec.Load(path); var ls = l.survivors[0];
            check(ls.traits.SequenceEqual(s.traits), "traits restored (" + string.Join(",", ls.traits) + ")");
            check(ls.booksRead.SequenceEqual(s.booksRead) && Math.Abs(ls.hoursSinceSmoke - 3.5f) < .001f, "read books and smoking clock restored");
            check(ls.Level(Skill.Fishing) == 2 && Math.Abs(ls.xp[(int)Skill.Fishing] - 12) < .001f && ls.Level(Skill.Farming) == s.Level(Skill.Farming), "farming and fishing skills restored");
            var lp = l.map.props[prop.id]; FarmPlot lplot;
            check(lp.kind == PropKind.FarmPlot && lp.x == prop.x && lp.y == prop.y && !lp.removed, "dug plot restored");
            check(l.plots.TryGetValue(prop.id, out lplot) && lplot.crop == "tomato" && Math.Abs(lplot.growth - .42f) < .001f && Math.Abs(lplot.water - .66f) < .001f && Math.Abs(lplot.health - .8f) < .001f && Math.Abs(lplot.care - 1.23f) < .001f, "plot crop, growth, water and health restored");
            // The restored plot keeps growing.
            var lsim = new Simulation(l); float g = lplot.growth; lplot.water = 1;
            Hours(lsim, 3, () => l.raining = false);
            check(lplot.growth > g, "a restored plot keeps growing");
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
            check(SaveCodec.Version == 2, "save version is 2");
            check(File.Exists(path), "refusing leaves the old save untouched");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
