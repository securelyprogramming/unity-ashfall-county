using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    public enum Population { Low, Normal, High, Insane }
    /// <summary>Community stock types. Rucksacks (ItemDef.resource) deliver them to the home base (Community.cs).</summary>
    public enum Resource : byte { Food, Meds, Ammo, Materials, Fuel }
    public enum ZombieSpeed { Shamblers, FastShamblers, Sprinters }

    /// <summary>Sandbox options chosen on the New Game screen. The host owns them for the whole session.</summary>
    [Serializable]
    public sealed class SandboxSettings
    {
        public int seed = 20261019;
        public int mapSize = 3072;
        public Population population = Population.Normal;
        public ZombieSpeed speed = ZombieSpeed.Shamblers;
        public int dayLengthMinutes = 45;     // real minutes per in-game day
        public int utilitiesMinDay = 4, utilitiesMaxDay = 9;
        public int evacuationDay = 5;         // 0 = no evacuation convoy (endless survival)
        public float lootAbundance = 1;
        public bool infection = true;
        public bool respawn = true;
        // Open-county options (PUBG-scale world, State of Decay community).
        public int landingZone = -1;          // index into WorldGen.landingZones chosen on the drop map; -1 = random
        public bool airdrops = true;          // Guard supply drops (WorldEvents.cs)
        public bool redZones = true;          // Guard artillery cleansing zones (WorldEvents.cs)
        public float humanDensity = 1;        // rival squads, raiders and enclaves (Humans.cs)
        public float freakDensity = 1;        // screamers, bloaters, ferals, juggernauts and plague hearts (Bestiary.cs)
        public int startingMembers = 3;       // community members at the start, including the leader (Community.cs)
        public float PopulationScale { get { switch (population) { case Population.Low: return .45f; case Population.High: return 1.7f; case Population.Insane: return 2.8f; default: return 1; } } }
    }

    public enum EventKind
    {
        Message, Swing, Hit, Kill, Gunshot, Empty, Groan, Door, Glass, Thump, Hurt, Bite, Death, Craft, Eat, Drink, LevelUp, Radio, Helicopter, Footstep, Shove, Break, Barricade, Evacuated, Fire, Rain, Engine, Crash,
        // Open-county additions. text carries a short detail ("warn", "land", ...); see Docs/DESIGN.md for the vocabulary.
        Scream, Explosion, Gas, Pounce, Charge, Grab, Heart, HeartDestroyed, Plague, Airdrop, Artillery, Tracer, KillFeed,
        Recruit, Facility, Mission, Morale, Influence, Switch, Talk, Trade, Siege, Legacy
    }

    public struct SimEvent
    {
        public EventKind kind; public Vec2 pos; public int survivor, zombie; public string text;
        public SimEvent(EventKind kind, Vec2 pos, int survivor = -1, string text = "", int zombie = -1) { this.kind = kind; this.pos = pos; this.survivor = survivor; this.text = text; this.zombie = zombie; }
    }

    public struct Noise { public Vec2 pos; public float radius; public int survivor; }

    /// <summary>Per-tick held input from a survivor's owner. Discrete requests use <see cref="Command"/>.</summary>
    [Serializable]
    public struct InputState
    {
        public float moveX, moveY, face; public bool run, sneak, aim, attack, shove;
    }

    public enum CommandKind
    {
        Transfer, Equip, Unequip, Wear, Unwear, Use, Drop, Craft, ToggleDoor, Climb, BreakWindow, ClearGlass, Barricade, Unbarricade, Pry,
        Sleep, Wake, Reload, ToggleLight, Forage, Chop, Cook, Boil, FillWater, Harvest, AddFuel, LightFire, Cancel, OpenContainer, CloseContainer, Treat, DropAll,
        EnterVehicle, ExitVehicle, ToggleEngine, Hotwire, Refuel,
        DigPlot, Plant, WaterPlot, Fish,
        // Community (Community.cs): target = survivor id / building id / facility kind; recipe = extra string argument.
        SwitchCharacter, Recruit, BuildFacility, UpgradeFacility, ClaimOutpost, AbandonOutpost, DepositRucksack, AcceptMission, AbandonMission, Rest, CraftAtWorkshop,
        // Humans (Humans.cs): target = survivor id; slot = CompanionOrder / trade option.
        Talk, Trade, CompanionOrder, SetCompanion,
        // Combat (SimCombat.cs): item = throwable uid, x/y = target point.
        Throw
    }
    public enum InvKind { Player, Container, Pile, Corpse }

    [Serializable]
    public struct InvRef
    {
        public InvKind kind; public int id;
        public InvRef(InvKind kind, int id) { this.kind = kind; this.id = id; }
        public static readonly InvRef Player = new InvRef(InvKind.Player, 0);
    }

    [Serializable]
    public struct Command
    {
        public CommandKind kind; public int item, target, count, slot; public InvRef from, to; public string recipe;
        public float x, y;   // world point for aimed requests (Throw)
        public static Command Of(CommandKind kind, int item = -1, int target = -1) { return new Command { kind = kind, item = item, target = target, count = 1, recipe = "" }; }
    }

    public sealed partial class World
    {
        public SandboxSettings settings;
        public Map map;
        public WorldGen gen;
        public Rng rng;
        public double minutes;                 // in-game minutes since 00:00 of day 1
        public double startMinutes = 8 * 60 + 30;
        public readonly List<Survivor> survivors = new List<Survivor>();
        public readonly List<Zombie> zombies = new List<Zombie>();
        public readonly List<Corpse> corpses = new List<Corpse>();
        public readonly Dictionary<int, Pile> piles = new Dictionary<int, Pile>();
        public readonly List<SimEvent> events = new List<SimEvent>();
        public readonly List<Noise> noises = new List<Noise>();
        public readonly List<Vehicle> vehicles = new List<Vehicle>();
        public readonly Dictionary<int, FarmPlot> plots = new Dictionary<int, FarmPlot>();   // FarmPlot prop id -> plot
        public int powerOffDay, waterOffDay, helicopterDay; public double helicopterMinute;
        public bool raining; public float rainIntensity, fog;
        public int nextCorpse;
        public int generatedProps;
        public HashSet<string> flags = new HashSet<string>();
        public int initialPopulation;
        public string outcome = "";            // "", "legacy" (county cleansed), "dead" (whole community lost); "evacuated" only in pre-refactor saves
        public int playerId;                   // survivor the local owner currently controls (switching community members changes it)
        public ZombieIndex index;

        public int Day { get { return (int)(minutes / 1440) + 1; } }
        public float HourOfDay { get { return (float)(minutes % 1440 / 60); } }
        public double HoursSurvived { get { return (minutes - startMinutes) / 60; } }
        public bool PowerOn { get { return Day < powerOffDay || powerOffDay <= 0; } }
        public bool WaterOn { get { return Day < waterOffDay || waterOffDay <= 0; } }
        /// <summary>0 at night, 1 at midday, with dawn and dusk transitions.</summary>
        public float Daylight
        {
            get
            {
                float h = HourOfDay;
                if (h < 5 || h > 21.5f) return 0;
                if (h < 7) return (h - 5) / 2;
                if (h > 19.5f) return (21.5f - h) / 2;
                return 1;
            }
        }
        /// <summary>Game minutes per real second.</summary>
        public float TimeScale { get { return 1440f / (settings.dayLengthMinutes * 60f); } }
        public string Clock { get { int h = (int)HourOfDay, m = (int)(minutes % 60); return "Day " + Day + "  " + (h % 12 == 0 ? 12 : h % 12) + ":" + m.ToString("00") + (h < 12 ? " AM" : " PM"); } }

        public void Emit(EventKind kind, Vec2 pos, int survivor = -1, string text = "", int zombie = -1) { events.Add(new SimEvent(kind, pos, survivor, text, zombie)); }
        public void Message(Survivor s, string text) { Emit(EventKind.Message, s.pos, s.id, text); }
        public void MakeNoise(Vec2 pos, float radius, int survivor = -1) { noises.Add(new Noise { pos = pos, radius = radius, survivor = survivor }); }

        public Pile PileAt(int x, int y, bool create)
        {
            int key = map.Index(x, y); Pile p;
            if (!piles.TryGetValue(key, out p) && create) { p = new Pile { x = x, y = y }; piles[key] = p; }
            return p;
        }

        /// <summary>Creates a fresh county. Generation is deterministic for a seed and generator version.</summary>
        public static World Create(SandboxSettings settings, string name, string profession, bool female, IList<string> traits = null)
        {
            string invalid = Trait.Validate(traits); if (invalid.Length > 0) throw new ArgumentException(invalid);
            var w = new World { settings = settings, rng = new Rng((ulong)settings.seed, 3) };
            ItemCatalog.Uid = 0;
            WorldGen gen; w.map = WorldGen.Generate(settings.seed, settings.mapSize, out gen); w.gen = gen;
            w.generatedProps = w.map.props.Count;
            w.minutes = w.startMinutes;
            w.powerOffDay = settings.utilitiesMaxDay <= 0 ? 0 : w.rng.Range(settings.utilitiesMinDay, settings.utilitiesMaxDay + 1);
            w.waterOffDay = settings.utilitiesMaxDay <= 0 ? 0 : w.rng.Range(settings.utilitiesMinDay, settings.utilitiesMaxDay + 1);
            w.helicopterDay = w.rng.Range(2, 4); w.helicopterMinute = w.rng.Range(9 * 60, 17 * 60);
            // The drop: the community sets down at the chosen landing zone and claims a nearby home site.
            var home = WorldGen.StartSite(w.map, gen, w.rng, settings.landingZone);
            var s = w.AddSurvivor(name, profession, female, home, traits);
            w.playerId = s.id;
            w.SpawnZombies(s.pos, home);
            w.SetupBestiary(home);          // Bestiary.cs: zombie kinds, plague hearts, hordes
            w.index = new ZombieIndex(w);
            w.SetupVehicles();
            w.SetupCommunity(s, home);      // Community.cs: extra members, home base, stock
            w.SetupHumans();                // Humans.cs: factions, enclaves, named characters, rival squads
            w.SetupWorldEvents();           // WorldEvents.cs: airdrop and red-zone schedule
            return w;
        }

        public Survivor AddSurvivor(string name, string profession, bool female, Building home, IList<string> traits = null)
        {
            string invalid = Trait.Validate(traits); if (invalid.Length > 0) throw new ArgumentException(invalid);
            var s = new Survivor { id = survivors.Count, name = string.IsNullOrEmpty(name) ? "Mara Vale" : name, profession = profession, female = female, spawnedAt = minutes };
            var prof = Profession.Get(profession);
            foreach (var kv in prof.skills) s.level[(int)kv.Key] = kv.Value;
            if (traits != null) s.traits.AddRange(traits);
            foreach (var t in s.traits.Select(Trait.Get)) if (t.bonusLevels > 0) s.level[(int)t.bonusSkill] = Math.Min(10, s.level[(int)t.bonusSkill] + t.bonusLevels);
            // Spawn inside a free tile of the home's first room, otherwise beside the road.
            Vec2 spawn = new Vec2(map.width / 2f, map.height / 2f);
            if (home != null)
            {
                foreach (int rid in home.rooms)
                {
                    var r = map.rooms[rid]; bool found = false;
                    for (int x = r.rect.x + 1; x < r.rect.MaxX - 1 && !found; x++)
                        for (int y = r.rect.y + 1; y < r.rect.MaxY - 1 && !found; y++)
                            if (!map.TileBlocked(x, y)) { spawn = new Vec2(x + .5f, y + .5f); found = true; }
                    if (found) break;
                }
                home.visited = true;
            }
            s.pos = spawn;
            Give(s, "water_bottle"); Give(s, "chips"); Give(s, "radio"); Give(s, "county_map");
            var hoodie = Give(s, "hoodie"); s.worn[WearSlot.Torso] = hoodie.uid;
            var jeans = Give(s, "jeans"); s.worn[WearSlot.Legs] = jeans.uid;
            var boots = Give(s, "boots"); s.worn[WearSlot.Feet] = boots.uid;
            foreach (var id in prof.kit)
            {
                var item = Give(s, id, id == "nails" ? 30 : id == "ammo_9mm" ? 15 : id.StartsWith("seeds_") ? 3 : 1);
                if (item.Def.IsWeapon && s.primary < 0 && item.Def.category != ItemCategory.Material) s.primary = item.uid;
                if (item.Def.category == ItemCategory.Firearm) item.loaded = item.Def.magazine;
            }
            if (s.HasTrait("smoker")) { Give(s, "cigarettes", 12); if (s.inventory.FindTag("fire") == null) Give(s, "lighter"); }
            survivors.Add(s);
            map.Explore(s.pos, 40);
            return s;
        }
        ItemStack Give(Survivor s, string id, int count = 1) { var item = ItemCatalog.Make(id, count); s.inventory.Add(item); return s.inventory.items.Last(i => i.def == id); }

        static float ZoneDensity(ZoneKind z)
        {
            switch (z) { case ZoneKind.Commercial: return 1.1f; case ZoneKind.Town: return .6f; case ZoneKind.Suburb: return .22f; case ZoneKind.Farm: return .05f; case ZoneKind.Forest: return .012f; case ZoneKind.Wild: return .02f; default: return 0; }
        }

        void SpawnZombies(Vec2 safe, Building home)
        {
            var spawnRng = new Rng((ulong)settings.seed, 17);
            float scale = settings.PopulationScale;
            for (int cy = 0; cy < map.CellsY; cy++)
                for (int cx = 0; cx < map.CellsX; cx++)
                {
                    float density = ZoneDensity((ZoneKind)map.zone[cx + cy * map.CellsX]) * scale;
                    int count = (int)density + (spawnRng.Chance(density - (int)density) ? 1 : 0);
                    for (int i = 0; i < count; i++)
                    {
                        int x = cx * Map.Cell + spawnRng.Range(0, Map.Cell), y = cy * Map.Cell + spawnRng.Range(0, Map.Cell);
                        // Land-only spawns: never water, walls or furniture; never near the player's start.
                        if (!map.InBounds(x, y) || map.TileBlocked(x, y) || map.IsWater(x, y)) continue;
                        var p = new Vec2(x + .5f, y + .5f);
                        if (Vec2.Distance(p, safe) < 28) continue;
                        if (home != null && home.rect.Contains(x, y)) continue;
                        AddZombie(p, spawnRng);
                    }
                }
            initialPopulation = zombies.Count;
        }

        public Zombie AddZombie(Vec2 p, Rng r)
        {
            var z = new Zombie { id = zombies.Count, pos = p, facing = r.Range(-3.14f, 3.14f), hp = r.Range(.9f, 1.5f), female = r.Chance(.45f), variant = r.Range(0, 8), state = r.Chance(.5f) ? ZombieState.Idle : ZombieState.Wander, timer = r.Range(1f, 12f) };
            z.speed = settings.speed == ZombieSpeed.Sprinters ? r.Range(3.2f, 3.9f) : settings.speed == ZombieSpeed.FastShamblers ? r.Range(1.8f, 2.4f) : r.Range(1.05f, 1.55f);
            z.hearing = r.Range(.7f, 1.3f); z.sight = r.Range(.7f, 1.2f); z.crawler = r.Chance(.03f);
            if (z.crawler) z.speed *= .45f;
            z.target = p; zombies.Add(z); if (index != null) index.Insert(z); return z;
        }

        public int LivingZombies { get { int n = 0; foreach (var z in zombies) if (z.Alive) n++; return n; } }
    }

    /// <summary>Spatial buckets for zombies so only those near survivors are simulated.</summary>
    public sealed class ZombieIndex
    {
        public const int Bucket = 32;
        readonly World world; readonly List<int>[] buckets; readonly int bx, by;
        readonly Dictionary<int, int> where = new Dictionary<int, int>();
        public ZombieIndex(World world)
        {
            this.world = world; bx = (world.map.width + Bucket - 1) / Bucket; by = (world.map.height + Bucket - 1) / Bucket;
            buckets = new List<int>[bx * by]; for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<int>();
            foreach (var z in world.zombies) if (z.Alive) Insert(z);
        }
        int Key(Vec2 p) { int x = MathUtil.Clamp((int)(p.x / Bucket), 0, bx - 1), y = MathUtil.Clamp((int)(p.y / Bucket), 0, by - 1); return x + y * bx; }
        public void Insert(Zombie z) { int k = Key(z.pos); buckets[k].Add(z.id); where[z.id] = k; }
        public void Remove(Zombie z) { int k; if (where.TryGetValue(z.id, out k)) { buckets[k].Remove(z.id); where.Remove(z.id); } }
        public void Moved(Zombie z) { int k = Key(z.pos), old; if (where.TryGetValue(z.id, out old) && old != k) { buckets[old].Remove(z.id); buckets[k].Add(z.id); where[z.id] = k; } }
        public void Query(Vec2 p, float radius, List<Zombie> result)
        {
            result.Clear();
            int x0 = MathUtil.Clamp((int)((p.x - radius) / Bucket), 0, bx - 1), x1 = MathUtil.Clamp((int)((p.x + radius) / Bucket), 0, bx - 1);
            int y0 = MathUtil.Clamp((int)((p.y - radius) / Bucket), 0, by - 1), y1 = MathUtil.Clamp((int)((p.y + radius) / Bucket), 0, by - 1);
            float r2 = radius * radius;
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++)
                    foreach (int id in buckets[x + y * bx]) { var z = world.zombies[id]; if (z.Alive && (z.pos - p).SqrLength <= r2) result.Add(z); }
        }
    }
}
