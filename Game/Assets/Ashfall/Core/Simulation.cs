using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>Host-authoritative rules. Owners submit input and commands; only the host mutates the world.</summary>
    public sealed partial class Simulation
    {
        public const float Step = .05f;
        public readonly World world;
        readonly Pathfinder pathfinder;
        readonly List<Zombie> query = new List<Zombie>(), active = new List<Zombie>();
        readonly Dictionary<int, InputState> inputs = new Dictionary<int, InputState>();
        readonly Queue<KeyValuePair<int, Command>> commands = new Queue<KeyValuePair<int, Command>>();
        public readonly Dictionary<int, HashSet<int>> visible = new Dictionary<int, HashSet<int>>();
        float accumulator, perceptionClock, hourClock;
        public int ActiveZombies { get { return active.Count; } }
        /// <summary>Time races while the controlled survivor sleeps (AI survivors never block it).</summary>
        public bool FastForward { get { var p = Survivor(world.playerId); return p != null && p.alive && p.sleeping; } }
        public const float ActiveRadius = 72;

        public Simulation(World world)
        {
            this.world = world; pathfinder = new Pathfinder(world.map);
            if (world.index == null) world.index = new ZombieIndex(world);
            foreach (var s in world.survivors) visible[s.id] = new HashSet<int>();
        }

        public void SetInput(int survivor, InputState input) { inputs[survivor] = input; }
        public void Submit(int survivor, Command command) { commands.Enqueue(new KeyValuePair<int, Command>(survivor, command)); }

        /// <summary>Advances by real seconds using fixed steps; sleeping survivors fast-forward time.</summary>
        public void Advance(float realSeconds)
        {
            accumulator += Math.Min(realSeconds, .25f);
            while (accumulator >= Step) { accumulator -= Step; Tick(Step); }
            if (FastForward) for (int i = 0; i < 40 && FastForward; i++) Tick(Step, 6);
        }

        public void Tick(float dt, float timeBoost = 1)
        {
            float gameMinutes = dt * world.TimeScale * timeBoost;
            world.minutes += gameMinutes;
            float hours = gameMinutes / 60f;
            hourClock += hours;
            while (commands.Count > 0) { var c = commands.Dequeue(); var s = Survivor(c.Key); if (s != null && s.alive) Execute(s, c.Value); }
            Weather(hours);
            Story();
            perceptionClock -= dt;
            bool perceive = perceptionClock <= 0; if (perceive) perceptionClock = .2f;
            // AI brains (companions, residents, rivals, raiders, enclaves) submit input/commands before survivors move.
            UpdateHumans(dt, hours, perceive);
            UpdateCommunity(dt, hours);
            foreach (var s in world.survivors) if (s.alive && !s.offscreen) UpdateSurvivor(s, dt, hours);
            UpdateVehicles(dt);
            CollectActive();
            searches = 0;
            foreach (var z in active) UpdateZombie(z, dt, perceive);
            UpdateBestiary(dt, hours);
            UpdateWorldEvents(dt, hours);
            Separate();
            ProcessNoise();
            if (perceive) foreach (var s in world.survivors) if (s.alive && s.id == world.playerId) UpdateVisibility(s);
            if (hourClock >= 1) { hourClock -= 1; Hourly(); }
            if (world.corpses.Count > 400) world.corpses.RemoveRange(0, world.corpses.Count - 400);
        }

        public Survivor Survivor(int id) { return id >= 0 && id < world.survivors.Count ? world.survivors[id] : null; }

        void CollectActive()
        {
            active.Clear();
            var seen = new HashSet<int>();
            foreach (var s in world.survivors)
            {
                if (!s.alive || s.offscreen) continue;
                world.index.Query(s.pos, ActiveRadius, query);
                foreach (var z in query) if (seen.Add(z.id)) { z.dormant = false; active.Add(z); }
            }
        }

        // ---------------------------------------------------------------- weather, utilities, story
        void Weather(float hours)
        {
            if (hours <= 0) return;
            if (world.rng.Chance(hours * (world.raining ? .18f : .035f)))
            {
                world.raining = !world.raining; world.rainIntensity = world.raining ? world.rng.Range(.3f, 1f) : 0;
                foreach (var s in world.survivors) if (s.alive) world.Message(s, world.raining ? "It starts to rain." : "The rain stops.");
                world.Emit(EventKind.Rain, Vec2.Zero, -1, world.raining ? "start" : "stop");
            }
            float h = world.HourOfDay; world.fog = h > 4.5f && h < 8 ? MathUtil.Clamp01(1 - Math.Abs(h - 6.2f) / 1.8f) * .7f : 0;
            if (world.raining)
                foreach (var p in world.map.props)
                    if (p.kind == PropKind.RainCollector && !p.removed) { p.water = Math.Min(40, p.water + hours * 8 * world.rainIntensity); p.modified = true; }
        }

        void Hourly()
        {
            HourlySurvival();   // SimSurvival.cs (community upkeep)
            HourlyBestiary();   // Bestiary.cs (migration, hordes, heart growth)
        }

        // ---------------------------------------------------------------- noise and perception
        void ProcessNoise()
        {
            foreach (var n in world.noises)
            {
                world.index.Query(n.pos, n.radius, query);
                foreach (var z in query)
                {
                    if (z.state == ZombieState.Chase || z.state == ZombieState.Attack || z.state == ZombieState.Knocked || z.state == ZombieState.Climb) continue;
                    float d = Vec2.Distance(z.pos, n.pos);
                    // Walls between muffle sound; zombie hearing varies.
                    float reach = n.radius * z.hearing * (world.map.LineOfSight(z.pos, n.pos) ? 1 : .55f) * (world.raining ? .8f : 1);
                    if (d > reach) continue;
                    z.dormant = false;
                    z.state = ZombieState.Investigate; z.target = n.pos + Vec2.FromAngle(world.rng.Range(0, 6.28f)) * world.rng.Range(0, 2.5f);
                    z.timer = world.rng.Range(15, 40); z.path.Clear(); z.repath = 0;
                }
            }
            world.noises.Clear();
        }

        /// <summary>Range at which a zombie can notice a survivor in the current light and stance.</summary>
        float NoticeRange(Survivor s)
        {
            float light = world.Daylight;
            float range = 6 + 16 * light;
            if (world.fog > 0) range *= 1 - world.fog * .5f;
            if (world.raining) range *= .8f;
            if (s.lightOn && light < .6f) range += 10;
            if (s.sneaking) range *= .55f * (1 - s.Level(Skill.Sneaking) * .04f);
            if (world.map.Indoors(s.pos) && light < .3f && !s.lightOn) range *= .7f;
            return range;
        }

        void UpdateVisibility(Survivor s)
        {
            HashSet<int> set; if (!visible.TryGetValue(s.id, out set)) visible[s.id] = set = new HashSet<int>();
            set.Clear();
            float light = world.Daylight, range = 9 + 22 * light;
            if (s.lightOn) range = Math.Max(range, 18);
            if (world.fog > 0) range *= 1 - world.fog * .45f;
            if (s.HasTrait("eagle_eyed")) range *= 1.2f; else if (s.HasTrait("short_sighted")) range *= .7f;
            world.index.Query(s.pos, Math.Max(range, 4), query);
            var forward = Vec2.FromAngle(s.facing);
            foreach (var z in query)
            {
                var to = z.pos - s.pos; float d = to.Length;
                bool inCone = d < 3.2f || Vec2.Dot(to * (1 / Math.Max(.01f, d)), forward) > -0.25f;
                if (inCone && d <= range && world.map.LineOfSight(s.pos, z.pos)) set.Add(z.id);
            }
            world.map.Explore(s.pos, Math.Max(s.HasTrait("short_sighted") ? 8 : 12, range));
            // Panic rises with visible zombies nearby.
            int close = 0; foreach (int id in set) if (Vec2.Distance(world.zombies[id].pos, s.pos) < 10) close++;
            s.panic = MathUtil.Clamp(s.panic + close * 1.2f - 1.5f, 0, 100);
        }

        public bool IsVisible(int survivor, int zombie) { HashSet<int> set; return visible.TryGetValue(survivor, out set) && set.Contains(zombie); }

        // ---------------------------------------------------------------- shared helpers
        void GainXp(Survivor s, Skill skill, float amount)
        {
            if (s.profession == "unemployed") amount *= 1.25f;
            amount *= TraitXpMultiplier(s) * BookMultiplier(s, skill);
            int i = (int)skill; if (s.level[i] >= 10) return;
            s.xp[i] += amount;
            while (s.level[i] < 10 && s.xp[i] >= Ashfall.Core.Survivor.XpCurve[s.level[i]])
            {
                s.xp[i] -= Ashfall.Core.Survivor.XpCurve[s.level[i]]; s.level[i]++;
                world.Emit(EventKind.LevelUp, s.pos, s.id, skill + " increased to level " + s.level[i] + ".");
            }
        }
        bool Reach(Survivor s, Vec2 p, float range = 1.7f) { return Vec2.Distance(s.pos, p) <= range && world.map.LineOfSight(s.pos, p); }
        bool ReachProp(Survivor s, Prop p)
        {
            if (p == null || p.removed) return false;
            float cx = MathUtil.Clamp(s.pos.x, p.x, p.x + p.w), cy = MathUtil.Clamp(s.pos.y, p.y, p.y + p.h);
            var near = new Vec2(cx, cy);
            if (Vec2.Distance(s.pos, near) > 1.25f) return false;
            // Line of sight to the nearest face, nudged toward the survivor, so walls between block access.
            var face = near + (s.pos - near).Normalized * .05f;
            return world.map.LineOfSight(s.pos, face);
        }
        bool ReachOpening(Survivor s, Opening o) { return o != null && Vec2.Distance(s.pos, o.Center) <= 1.35f; }
        public Prop NearbyProp(Survivor s, Func<Prop, bool> filter)
        {
            int x0 = (int)s.pos.x - 2, y0 = (int)s.pos.y - 2; Prop best = null; float bestD = 99;
            for (int x = x0; x <= x0 + 4; x++) for (int y = y0; y <= y0 + 4; y++)
                {
                    var p = world.map.PropAt(x, y);
                    if (p == null) p = NonBlockingPropAt(x, y);
                    if (p == null || !filter(p) || !ReachProp(s, p)) continue;
                    float d = Vec2.Distance(p.Center, s.pos); if (d < bestD) { bestD = d; best = p; }
                }
            return best;
        }
        readonly Dictionary<int, int> nonBlocking = new Dictionary<int, int>();
        int nonBlockingCount = -1;
        public Prop NonBlockingPropAt(int x, int y)
        {
            if (nonBlockingCount != world.map.props.Count)
            {
                nonBlocking.Clear();
                foreach (var p in world.map.props) if (!p.Blocks && !p.removed && world.map.InBounds(p.x, p.y)) nonBlocking[world.map.Index(p.x, p.y)] = p.id;
                nonBlockingCount = world.map.props.Count;
            }
            int id; if (!world.map.InBounds(x, y) || !nonBlocking.TryGetValue(world.map.Index(x, y), out id)) return null;
            var prop = world.map.props[id]; return prop.removed ? null : prop;
        }
        public bool NearHeat(Survivor s)
        {
            return NearbyProp(s, p => p.kind == PropKind.Campfire && p.lit || p.kind == PropKind.Stove && world.PowerOn) != null;
        }
        public bool NearWater(Survivor s)
        {
            int x = (int)s.pos.x, y = (int)s.pos.y;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (world.map.IsWater(x + dx, y + dy)) return true;
            return false;
        }
        Inventory Resolve(Survivor s, InvRef r, bool forWrite)
        {
            switch (r.kind)
            {
                case InvKind.Player: return s.inventory;
                case InvKind.Container:
                    {
                        if (r.id < 0 || r.id >= world.map.containers.Count) return null;
                        var c = world.map.containers[r.id]; var p = c.prop >= 0 ? world.map.props[c.prop] : null;
                        if (p == null || !ReachProp(s, p)) return null;
                        world.RollContainer(c);
                        return c.inventory;
                    }
                case InvKind.Pile:
                    {
                        int x = r.id % world.map.width, y = r.id / world.map.width;
                        if (Vec2.Distance(s.pos, new Vec2(x + .5f, y + .5f)) > 1.8f) return null;
                        var pile = world.PileAt(x, y, forWrite); return pile == null ? null : pile.inventory;
                    }
                case InvKind.Corpse:
                    {
                        var c = world.corpses.FirstOrDefault(k => k.id == r.id);
                        if (c == null || Vec2.Distance(s.pos, c.pos) > 1.8f) return null;
                        if (!c.rolled) { c.rolled = true; Loot.RollInto(c.inventory, "corpse", new Rng((ulong)(world.settings.seed + c.id * 31), 5), world.settings.lootAbundance); }
                        return c.inventory;
                    }
            }
            return null;
        }
        public Inventory Peek(Survivor s, InvRef r) { return Resolve(s, r, false); }
    }
}
