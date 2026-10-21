using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>What an AI survivor is doing. Host working state only: it is rebuilt after a load.</summary>
    public enum BrainState : byte { Idle, Travel, Loot, Fight, Flee, Follow, Guard, Hunt, Airdrop }

    /// <summary>AI brains for every non-player survivor. Brains never touch the world directly: they submit the same
    /// InputState (SetInput) and Commands (Submit) a player would, so reach, line of sight and combat rules apply equally.</summary>
    public sealed partial class Simulation
    {
        /// <summary>LOD: a survivor goes offscreen beyond LodFar of every player-controlled survivor and returns inside LodNear.</summary>
        public const float LodFar = 170, LodNear = 150;
        /// <summary>Airdrops within this range draw crews, raiders and hunters.</summary>
        public const float AirdropPull = 600;
        const int MaxHumanSearches = 4;          // A* searches per tick for all brains together
        const float ThinkInterval = .2f;          // onscreen brains decide at 5 Hz and steer every tick

        sealed class Plan
        {
            public BrainState kind = BrainState.Idle; public Vec2 goal; public int building = -1, airdrop = -1; public double until;
            public readonly HashSet<int> visited = new HashSet<int>();
        }

        sealed class Brain
        {
            public BrainState state; public AiRole role; public int faction;
            public Vec2 goal, anchor, hold; public bool hasGoal, run, hasHold, holdFire;
            public readonly List<Int2> path = new List<Int2>(); public int pathIndex, pathFails; public Vec2 pathFor; public float repath;
            public float think, abstractClock, idle, openingClock, throwClock, medClock, lootClock, shoutClock, gearClock;
            public float stuckTime, unstick; public Vec2 unstickDir; public int stuckCount;
            public int enemy = -1, zombie = -1; public bool fightLos;
            public Survivor nearEnemy; public float nearEnemyDist; public Zombie nearZombie; public float nearZombieDist; public float threat; public int closeZombies;
            public int container = -1;
            public readonly HashSet<int> searched = new HashSet<int>();
            public int hunt = -1; public Vec2 huntPos; public double huntUntil;
            public bool bash; public float bashFace;
            public InputState input;
            public readonly Plan own = new Plan();
        }

        readonly Dictionary<int, Brain> brains = new Dictionary<int, Brain>();
        readonly Dictionary<int, Plan> squadPlans = new Dictionary<int, Plan>();
        readonly List<Zombie> humanQuery = new List<Zombie>();
        readonly List<Vec2> players = new List<Vec2>();
        Dictionary<int, List<int>> buildingContainers; int buildingContainersCount = -1;
        List<Vec2> travelPoints;
        readonly List<Prop> towers = new List<Prop>(); float towerClock;
        int humanSearches;
        static readonly List<int> NoContainers = new List<int>();
        static readonly float[] Detours = { 0, .6f, -.6f, 1.2f, -1.2f, 1.9f, -1.9f };

        /// <summary>The AI state of a survivor (Idle for players and the dead), for HUD labels and QA.</summary>
        public BrainState BrainStateOf(int survivor) { Brain b; return brains.TryGetValue(survivor, out b) ? b.state : BrainState.Idle; }
        /// <summary>Clears a survivor's AI working state (paths, plans, targets). Changing ai or faction does this automatically.</summary>
        public void ResetBrain(Survivor s) { if (s != null) brains.Remove(s.id); }

        /// <summary>AI brains for every non-player survivor: submit InputState/Commands through SetInput/Submit, and LOD far survivors.</summary>
        void UpdateHumans(float dt, float hours, bool perceive)
        {
            humanSearches = 0;
            UpdateThrowables(dt);   // SimCombat.cs
            players.Clear();
            foreach (var s in world.survivors) if (s.alive && s.ai == AiRole.None) players.Add(s.pos);
            Bounties();
            var list = world.survivors;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i]; if (!s.alive || s.ai == AiRole.None) continue;
                var b = BrainOf(s);
                Lod(s, b);
                if (s.offscreen)
                {
                    b.abstractClock += dt;
                    if (b.abstractClock >= 1) { float step = b.abstractClock; b.abstractClock = 0; Abstract(s, b, step); }
                    continue;
                }
                b.think -= dt;
                if (b.think <= 0) { b.think = ThinkInterval; Think(s, b); }
                if (!s.alive) continue;
                Steer(s, b, dt);
                SetInput(s.id, b.input);
            }
        }

        Brain BrainOf(Survivor s)
        {
            Brain b;
            if (brains.TryGetValue(s.id, out b) && b.role == s.ai && b.faction == s.faction) return b;
            b = new Brain { role = s.ai, faction = s.faction, anchor = s.pos, think = (s.id % 4) * .05f, abstractClock = (s.id % 20) * .05f };
            var f = world.FactionOf(s);
            if (f != null && (f.enclave || f.homeBuilding >= 0)) b.anchor = f.home;
            brains[s.id] = b; return b;
        }

        void Lod(Survivor s, Brain b)
        {
            float best = float.MaxValue;
            foreach (var p in players) { float d = (p - s.pos).SqrLength; if (d < best) best = d; }
            if (!s.offscreen && best > LodFar * LodFar)
            {
                s.offscreen = true; b.path.Clear(); b.state = b.state == BrainState.Fight ? BrainState.Idle : b.state;
                inputs.Remove(s.id); CancelAction(s, ""); s.lootContainer = -1; s.speed = 0; s.running = s.aiming = false;
            }
            else if (s.offscreen && best < LodNear * LodNear) { s.offscreen = false; b.path.Clear(); b.think = 0; b.stuckTime = 0; }
        }

        /// <summary>Hunter factions periodically get a bounty on the controlled survivor's last known position.</summary>
        void Bounties()
        {
            foreach (var f in world.factions)
            {
                if (f.disposition != Disposition.Hunter || f.nextBounty <= 0 || world.minutes < f.nextBounty) continue;
                f.nextBounty = world.minutes + world.rng.Range(360, 600);
                var c = world.Controlled; if (c == null || !c.alive || c.faction != 0) continue;
                f.bounty = c.pos + Vec2.FromAngle(world.rng.Range(0, 6.28f)) * world.rng.Range(5, 30); f.hasBounty = true;
                world.Emit(EventKind.Radio, f.bounty, -1, "Guard channel chatter: a bounty is posted on survivors near your position. " + f.name + " are moving.");
            }
        }

        // ---------------------------------------------------------------- decisions (5 Hz)
        void Think(Survivor s, Brain b)
        {
            var f = world.FactionOf(s);
            if (s.faction != 0) { s.hunger = Math.Min(s.hunger, 20); s.thirst = Math.Min(s.thirst, 20); s.fatigue = Math.Min(s.fatigue, 40); s.boredom = 10; }   // AI upkeep is abstract
            if (s.sleeping) { Submit(s.id, Command.Of(CommandKind.Wake)); return; }
            b.medClock -= ThinkInterval; b.throwClock -= ThinkInterval; b.shoutClock -= ThinkInterval; b.gearClock -= ThinkInterval;
            Perceive(s, b, f);
            if (s.vehicle >= 0 || s.knockedDown > 0 || s.action != null && !s.action.movable) return;
            // Patch up before anything else when nothing is close.
            if (b.medClock <= 0 && s.Bleeding && b.closeZombies == 0 && (b.nearEnemy == null || b.nearEnemyDist > 10))
            {
                var med = s.inventory.items.FirstOrDefault(i => i.Def.bandageQuality > 0);
                b.medClock = 5;
                if (med != null) { Submit(s.id, new Command { kind = CommandKind.Use, item = med.uid, recipe = "" }); b.hasGoal = false; return; }
            }
            ManageWeapon(s, b);
            if (Flee(s, b, f)) return;
            if (s.ai == AiRole.Companion && s.order == CompanionOrder.Attack && CompanionTarget(s, b)) { b.state = BrainState.Fight; FightLos(s, b); TryThrow(s, b); return; }
            if (PickFight(s, b, f)) { b.state = BrainState.Fight; FightLos(s, b); TryThrow(s, b); return; }
            b.enemy = b.zombie = -1; b.holdFire = false;
            if (b.state == BrainState.Fight || b.state == BrainState.Flee) { b.state = BrainState.Idle; b.hasGoal = false; }
            if (b.gearClock <= 0 && s.action == null) { b.gearClock = 2; if (ManageGear(s)) return; }
            switch (s.ai)
            {
                case AiRole.Companion: Companion(s, b); break;
                case AiRole.Resident: Resident(s, b); break;
                case AiRole.Enclave: Guard(s, b, f); break;
                default: Roam(s, b, f); break;
            }
        }

        float Sight(Survivor s)
        {
            float sight = 10 + 20 * world.Daylight;
            if (s.lightOn) sight = Math.Max(sight, 18);
            if (world.fog > 0) sight *= 1 - world.fog * .45f;
            if (s.HasTrait("eagle_eyed")) sight *= 1.2f;
            return sight;
        }

        /// <summary>What the survivor can actually see: zombies and hostile survivors in range with line of sight.</summary>
        void Perceive(Survivor s, Brain b, Faction f)
        {
            float sight = Sight(s);
            b.threat = 0; b.closeZombies = 0; b.nearZombie = null; b.nearZombieDist = 1e9f; b.nearEnemy = null; b.nearEnemyDist = 1e9f;
            world.index.Query(s.pos, Math.Min(sight, 26), humanQuery);
            int checks = 0;
            foreach (var z in humanQuery)
            {
                float d = Vec2.Distance(z.pos, s.pos);
                bool onMe = z.targetSurvivor == s.id && (z.state == ZombieState.Chase || z.state == ZombieState.Attack || z.state == ZombieState.Lunge);
                if (d > 3.2f && !onMe && d >= b.nearZombieDist && d > 9) continue;
                if (d > 3.2f) { if (checks++ > 14 || !world.map.LineOfSight(s.pos, z.pos)) continue; }
                if (d < 9 || onMe) { b.threat += Bestiary.Get(z.kind).threat * (d < 4 ? 1.5f : 1); if (d < 4) b.closeZombies++; }
                if (d < b.nearZombieDist) { b.nearZombieDist = d; b.nearZombie = z; }
            }
            foreach (var o in world.survivors)
            {
                if (!o.alive || o == s || o.faction == s.faction) continue;
                float d2 = (o.pos - s.pos).SqrLength; if (d2 > 45 * 45) continue;
                float d = (float)Math.Sqrt(d2), range = Math.Min(45, sight * 1.4f * (o.sneaking ? .55f : 1));
                if (d > range || !world.map.LineOfSight(s.pos, o.pos)) continue;
                bool hostile = world.Hostile(s, o);
                if (!hostile && f != null) { Encounter(s, f, o, d); hostile = world.Hostile(s, o); }
                if (!hostile) continue;
                if (o.faction == 0 && f != null && f.disposition == Disposition.Hunter) { b.hunt = o.id; b.huntPos = o.pos; b.huntUntil = world.minutes + 60; }
                if (d < b.nearEnemyDist) { b.nearEnemyDist = d; b.nearEnemy = o; }
            }
            if (b.nearEnemy != null && b.nearEnemy.faction == 0 && s.faction != 0 && b.shoutClock <= 0) { b.shoutClock = 40; Shout(s, f); }
        }

        /// <summary>Opportunists roll once per encounter (with a cooldown) to rob a weak-looking community member or another crew.</summary>
        void Encounter(Survivor s, Faction f, Survivor o, float d)
        {
            if (f.disposition != Disposition.Opportunist || d > 25 || world.minutes < f.nextRoll) return;
            if (o.faction == 0)
            {
                f.nextRoll = world.minutes + 45;
                float chance = .06f;
                bool alone = true; foreach (var m in world.survivors) if (m.alive && m.faction == 0 && m != o && Vec2.Distance(m.pos, o.pos) < 10) { alone = false; break; }
                if (alone) chance += .2f;
                if (o.health < 50) chance += .25f;
                if (CarriesRucksack(o)) chance += .25f;
                if (world.Daylight < .25f) chance += .15f;
                if (world.rng.Chance(chance)) f.provokedUntil = world.minutes + 20;
                return;
            }
            var of = world.FactionOf(o);
            if (of == null || !f.IsCrew || !(of.IsCrew || of.key == "wanderers")) return;
            f.nextRoll = world.minutes + 45;
            if (world.rng.Chance(.12f + (CarriesRucksack(o) ? .15f : 0))) { f.feud = of.id; f.feudUntil = world.minutes + 15; }
        }

        static bool CarriesRucksack(Survivor s) { foreach (var i in s.inventory.items) if (i.Def.resource >= 0) return true; return false; }

        void Shout(Survivor s, Faction f)
        {
            var c = Characters.Get(s.character);
            string line = c != null && c.hostileLine.Length > 0 ? c.hostileLine
                : f == null ? "Hey!" : f.key == "red_talons" ? "Fresh meat! Get them!" : f.disposition == Disposition.Hunter ? "Contact! Take them down!"
                : f.disposition == Disposition.Opportunist ? "Hand over the bag and nobody gets hurt!" : "You asked for this!";
            world.Emit(EventKind.Talk, s.pos, s.id, s.name + ": " + line);
        }

        // ---------------------------------------------------------------- weapons and gear
        static bool IsGun(ItemStack w) { return w != null && w.Def.category == ItemCategory.Firearm; }
        static float WeaponScore(Survivor s, ItemStack w)
        {
            if (w == null) return 0; var d = w.Def;
            if (d.category == ItemCategory.Firearm)
            {
                if (w.loaded <= 0 && s.inventory.Count(d.ammo) <= 0) return 0;
                return 2 + d.damage * (float)Math.Pow(Math.Max(1, d.pellets), .6) * d.accuracy * (1 + d.range / 20);
            }
            if (!d.IsWeapon || d.damage <= 0 || d.category == ItemCategory.Material || IsThrowable(d)) return 0;
            return d.damage * (1 + d.knockdown * .5f) / Math.Max(.45f, d.swingTime) * (w.condition > 0 ? 1 : .3f);
        }
        static ItemStack BestWeapon(Survivor s, bool meleeOnly)
        {
            ItemStack best = null; float score = 0;
            foreach (var i in s.inventory.items)
            {
                if (meleeOnly && i.Def.category == ItemCategory.Firearm) continue;
                float v = WeaponScore(s, i); if (v > score) { score = v; best = i; }
            }
            return best;
        }

        /// <summary>Reload when it is safe (or empty), fall back to melee when out of ammo, and carry the best weapon.</summary>
        void ManageWeapon(Survivor s, Brain b)
        {
            if (s.action != null) return;
            var w = s.Primary;
            if (IsGun(w))
            {
                int ammo = s.inventory.Count(w.Def.ammo);
                bool calm = b.nearEnemy == null && b.closeZombies == 0;
                if (w.loaded < w.Def.magazine && ammo > 0 && (w.loaded == 0 || calm && w.loaded < w.Def.magazine * .6f)) { Submit(s.id, Command.Of(CommandKind.Reload)); return; }
                if (w.loaded == 0 && ammo == 0)
                {
                    var other = BestWeapon(s, false);
                    if (other != null && other.uid != w.uid) Submit(s.id, Command.Of(CommandKind.Equip, other.uid));
                    return;
                }
            }
            var best = BestWeapon(s, false);
            if (best != null && best.uid != s.primary && WeaponScore(s, best) > WeaponScore(s, w) + .05f) Submit(s.id, Command.Of(CommandKind.Equip, best.uid));
        }

        /// <summary>Puts on better armour or a bigger bag (one timed Wear action at a time). Returns true when it started one.</summary>
        bool ManageGear(Survivor s)
        {
            foreach (var i in s.inventory.items)
            {
                var d = i.Def; if (d.slot == WearSlot.None || s.IsEquipped(i.uid)) continue;
                var cur = s.Worn(d.slot);
                bool better = d.slot == WearSlot.Back ? d.capacity > (cur == null ? 0 : cur.Def.capacity)
                    : ArmorOf(d) > (cur == null ? 0 : ArmorOf(cur.Def) * cur.condition) + .02f;
                if (!better) continue;
                Submit(s.id, Command.Of(CommandKind.Wear, i.uid)); return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- fight or flight
        float Courage(Survivor s, Faction f)
        {
            var w = s.Primary;
            float c = IsGun(w) && (w.loaded > 0 || s.inventory.Count(w.Def.ammo) > 0) ? 7 : w != null && w.Def.damage > 0 ? 4.5f : 2;
            if (s.ai == AiRole.Raider || s.ai == AiRole.Hunter) c *= 1.4f;
            else if (s.ai == AiRole.Companion || s.ai == AiRole.Resident) c *= 1.5f;
            else if (s.ai == AiRole.Trader) c *= .6f;
            return c;
        }

        bool Flee(Survivor s, Brain b, Faction f)
        {
            bool danger = b.closeZombies > 0 || b.nearEnemy != null && b.nearEnemyDist < 25;
            if (!danger) return false;
            float floor = f != null && f.disposition == Disposition.Hunter ? 18 : 32;
            bool swarmed = b.closeZombies >= 3 || b.closeZombies >= 2 && s.health < 50;   // three attackers drag a hurt survivor down
            if (s.health >= floor && b.threat < Courage(s, f) * .8f && !swarmed) return false;
            var from = b.nearEnemy != null && (b.nearZombie == null || b.nearEnemyDist < b.nearZombieDist) ? b.nearEnemy.pos : b.nearZombie != null ? b.nearZombie.pos : s.pos;
            var away = (s.pos - from).Normalized; if (away.Length < .1f) away = Vec2.FromAngle(world.rng.Range(0, 6.28f));
            var home = (b.anchor - s.pos).Normalized;
            var dir = (away * 2 + home * .6f).Normalized;
            b.state = BrainState.Flee; b.enemy = b.zombie = -1;
            SetGoal(b, s.pos + dir * 22, true);
            // A pipe bomb behind you pulls the horde off your back.
            if (b.closeZombies >= 3 && b.throwClock <= 0)
            {
                var bomb = s.inventory.items.FirstOrDefault(i => i.def == "pipe_bomb");
                if (bomb != null) { var at = s.pos - dir * 12; Submit(s.id, new Command { kind = CommandKind.Throw, item = bomb.uid, x = at.x, y = at.y, recipe = "" }); b.throwClock = 15; }
            }
            return true;
        }

        float EngageRange(Survivor s, Brain b)
        {
            bool gun = IsGun(s.Primary) && s.Primary.loaded > 0;
            float r;
            switch (s.ai)
            {
                case AiRole.Companion: r = 8; break;
                case AiRole.Resident: r = s.id == world.community.garrison || TowerFor(s) != null && gun ? 25 : 15; break;
                case AiRole.Enclave: r = 12; break;
                case AiRole.Raider: case AiRole.Hunter: r = 12; break;
                case AiRole.Trader: r = 3; break;
                default: r = 7; break;
            }
            return gun ? Math.Max(r, 10) : r;
        }

        bool PickFight(Survivor s, Brain b, Faction f)
        {
            var w = s.Primary; bool gun = IsGun(w) && (w.loaded > 0 || s.inventory.Count(w.Def.ammo) > 0);
            bool brute = s.ai == AiRole.Raider || s.ai == AiRole.Hunter;
            float chase = gun ? w.Def.range * 1.15f : brute ? 25 : s.ai == AiRole.Companion ? 12 : 10;
            if (s.ai == AiRole.Trader) chase = 4;
            if (b.nearEnemy != null && b.nearEnemyDist < chase) { b.enemy = b.nearEnemy.id; b.zombie = -1; b.holdFire = HoldPosition(s); return true; }
            if (b.nearZombie != null)
            {
                float engage = EngageRange(s, b);
                bool onMe = b.nearZombie.targetSurvivor == s.id && b.nearZombieDist < 12;
                if (s.ai == AiRole.Companion) { var leader = Leader(s); if (leader != null && Vec2.Distance(b.nearZombie.pos, leader.pos) < 8) onMe = true; }
                bool big = Bestiary.Get(b.nearZombie.kind).threat >= 4 && !gun;
                if ((b.nearZombieDist < engage || onMe) && (!big || b.nearZombieDist < 2.5f)) { b.zombie = b.nearZombie.id; b.enemy = -1; b.holdFire = HoldPosition(s); return true; }
            }
            return false;
        }

        bool HoldPosition(Survivor s)
        {
            if (s.id == world.community.garrison) return true;
            if (s.ai == AiRole.Companion && s.order == CompanionOrder.Hold) return true;
            return s.ai == AiRole.Resident && IsGun(s.Primary) && TowerFor(s) != null && Elevated(s);
        }

        void FightLos(Survivor s, Brain b)
        {
            var o = b.enemy >= 0 ? Survivor(b.enemy) : null;
            var p = o != null ? o.pos : b.zombie >= 0 ? world.zombies[b.zombie].pos : s.pos;
            b.fightLos = world.map.LineOfSight(s.pos, p);
        }

        /// <summary>Attack order: engage the threat nearest the leader's aim.</summary>
        bool CompanionTarget(Survivor s, Brain b)
        {
            var leader = Leader(s); if (leader == null) return false;
            float best = 1.1f; int zombie = -1, enemy = -1;
            world.index.Query(leader.pos, 25, humanQuery);
            foreach (var z in humanQuery)
            {
                var to = z.pos - leader.pos; float a = Math.Abs(MathUtil.WrapAngle(to.Angle - leader.facing)) + to.Length * .01f;
                if (a < best && world.map.LineOfSight(leader.pos, z.pos)) { best = a; zombie = z.id; enemy = -1; }
            }
            foreach (var o in world.survivors)
            {
                if (!o.alive || !world.Hostile(s, o) || Vec2.Distance(o.pos, leader.pos) > 25) continue;
                var to = o.pos - leader.pos; float a = Math.Abs(MathUtil.WrapAngle(to.Angle - leader.facing)) + to.Length * .01f - .2f;   // people first
                if (a < best && world.map.LineOfSight(leader.pos, o.pos)) { best = a; enemy = o.id; zombie = -1; }
            }
            if (zombie < 0 && enemy < 0) return false;
            b.zombie = zombie; b.enemy = enemy; b.holdFire = false; return true;
        }

        /// <summary>Throws a grenade or molotov at a cluster of zombies or a hostile, never near friends.</summary>
        void TryThrow(Survivor s, Brain b)
        {
            if (b.throwClock > 0 || s.action != null) return;
            ItemStack item = null; foreach (var i in s.inventory.items) if (IsThrowable(i.Def) && i.def != "pipe_bomb") { item = i; break; }
            if (item == null) return;
            Vec2 at; bool worth = false;
            var o = b.enemy >= 0 ? Survivor(b.enemy) : null;
            if (o != null) { at = o.pos; worth = true; }
            else if (b.zombie >= 0)
            {
                var z = world.zombies[b.zombie]; at = z.pos;
                world.index.Query(at, 4, humanQuery);
                int n = humanQuery.Count; foreach (var q in humanQuery) if (Bestiary.Get(q.kind).threat >= 4) n += 4;
                worth = n >= 4;
            }
            else return;
            float d = Vec2.Distance(at, s.pos), radius = BlastRadiusOf(item.Def);
            if (!worth || d < radius + 2 || d > MaxThrow || !world.map.LineOfSight(s.pos, at)) return;
            foreach (var f in world.survivors) if (f.alive && f != s && !world.Hostile(s, f) && Vec2.Distance(f.pos, at) < radius + 1.5f) return;
            Submit(s.id, new Command { kind = CommandKind.Throw, item = item.uid, x = at.x, y = at.y, recipe = "" });
            b.throwClock = 12;
        }

        // ---------------------------------------------------------------- roles
        Survivor Leader(Survivor s)
        {
            var l = s.leader >= 0 ? Survivor(s.leader) : world.Controlled;
            return l != null && l.alive && l != s && l.faction == s.faction ? l : null;
        }

        void Companion(Survivor s, Brain b)
        {
            var leader = Leader(s);
            if (leader == null) { Resident(s, b); return; }
            if (s.order == CompanionOrder.Hold)
            {
                if (!b.hasHold) { b.hold = s.pos; b.hasHold = true; }
                b.state = BrainState.Guard; SetGoal(b, b.hold, false); return;
            }
            b.hasHold = false; b.state = BrainState.Follow;
            var fwd = Vec2.FromAngle(leader.facing); var right = new Vec2(-fwd.y, fwd.x);
            float side = s.id % 2 == 0 ? 1 : -1;
            var spot = leader.pos - fwd * 2.2f + right * (1.3f * side);
            // Out of the leader's line of fire: step wide when they aim.
            var rel = s.pos - leader.pos;
            if (leader.aiming && rel.Length < 25 && Math.Abs(MathUtil.WrapAngle(rel.Angle - leader.facing)) < .5f) spot = leader.pos + right * (2.6f * side) - fwd * .8f;
            if (!Walkable(spot)) spot = leader.pos - fwd * 1.6f;
            float d = Vec2.Distance(s.pos, leader.pos);
            if (d > 140 && !leader.inVehicle) { var near = NearestWalkable(leader.pos - fwd * 3, 6); if (near.HasValue) { s.pos = near.Value; b.path.Clear(); } }
            if (leader.inVehicle) { b.hasGoal = false; return; }
            if (d < 3.4f && Vec2.Distance(s.pos, spot) < 1.6f) { b.hasGoal = false; return; }
            SetGoal(b, spot, d > 7);
        }

        Prop TowerFor(Survivor s)
        {
            if (towerClock <= 0 || world.minutes - towerClock > 10)
            {
                towerClock = (float)Math.Max(1, world.minutes); towers.Clear();
                var c = world.community; if (c.baseBuilding >= 0)
                    foreach (var p in world.map.props) if (p.kind == PropKind.Watchtower && !p.removed && Vec2.Distance(p.Center, c.basePos) < 45) towers.Add(p);
            }
            Prop best = null; float bd = 1e9f;
            foreach (var p in towers) { float d = Vec2.Distance(p.Center, s.pos); if (d < bd) { bd = d; best = p; } }
            return best;
        }

        void Resident(Survivor s, Brain b)
        {
            var c = world.community;
            // The community's watchtower garrison holds its post (Community.GarrisonPost) and shoots from there.
            if (s.id == c.garrison) { b.state = BrainState.Guard; var post = GarrisonPost; if (Vec2.Distance(s.pos, post) > .6f) SetGoal(b, post, false); else b.hasGoal = false; return; }
            Vec2 home = c.baseBuilding >= 0 ? c.basePos : b.anchor;
            var tower = IsGun(s.Primary) ? TowerFor(s) : null;
            b.state = BrainState.Guard;
            if (tower != null)
            {
                // Watchtower garrison: stand beside the tower; Fire gets the elevated range bonus there.
                if (Elevated(s)) { b.hasGoal = false; return; }
                var spot = NearestWalkable(tower.Center, 3); if (spot.HasValue) SetGoal(b, spot.Value, false); return;
            }
            b.idle -= ThinkInterval;
            if (!b.hasGoal && b.idle <= 0 || Vec2.Distance(s.pos, home) > 30)
            {
                var p = NearestWalkable(home + Vec2.FromAngle(world.rng.Range(0, 6.28f)) * world.rng.Range(2, 14), 4);
                if (p.HasValue) SetGoal(b, p.Value, false);
                b.idle = world.rng.Range(12, 30);
            }
        }

        void Guard(Survivor s, Brain b, Faction f)
        {
            b.state = BrainState.Guard;
            var home = f != null && (f.enclave || f.homeBuilding >= 0) ? f.home : b.anchor;   // wanderers keep to their own spot
            b.idle -= ThinkInterval;
            if (b.idle > 0 && Vec2.Distance(s.pos, home) < 25) return;
            b.idle = world.rng.Range(15, 45);
            Vec2? p = null;
            var bld = f != null && f.homeBuilding >= 0 ? world.map.buildings[f.homeBuilding] : null;
            if (bld != null && (Characters.Get(s.character) != null || world.rng.Chance(.6f)))
            {
                var room = world.map.rooms[bld.rooms[world.rng.Range(0, bld.rooms.Count)]];
                p = NearestWalkable(new Vec2(world.rng.Range(room.rect.x + 1, room.rect.MaxX - 1) + .5f, world.rng.Range(room.rect.y + 1, room.rect.MaxY - 1) + .5f), 2);
            }
            if (!p.HasValue) p = NearestWalkable(home + Vec2.FromAngle(world.rng.Range(0, 6.28f)) * world.rng.Range(4, 14), 4);
            if (p.HasValue) SetGoal(b, p.Value, false);
        }

        Plan PlanOf(Survivor s, Brain b)
        {
            if (s.squad < 0) return b.own;
            Plan p; if (!squadPlans.TryGetValue(s.squad, out p)) squadPlans[s.squad] = p = new Plan();
            return p;
        }

        Vec2 Spread(Survivor s) { return Vec2.FromAngle(s.id * 2.39996f) * (1.5f + s.id % 3); }

        /// <summary>Rival crews, raiders, hunters, wanderers and the trader: airdrops, bounties, looting and travel between towns.</summary>
        void Roam(Survivor s, Brain b, Faction f)
        {
            var plan = PlanOf(s, b);
            if (plan.until <= world.minutes || plan.kind == BrainState.Idle || f != null && f.hasBounty || plan.kind != BrainState.Airdrop && s.ai != AiRole.Trader && AirdropNear(s.pos) != null) NewPlan(s, b, f, plan);
            if ((s.ai == AiRole.Hunter || s.ai == AiRole.Raider) && b.hunt >= 0 && b.huntUntil > world.minutes)
            {
                b.state = BrainState.Hunt; SetGoal(b, b.huntPos, true);
                if (Vec2.Distance(s.pos, b.huntPos) < 3) b.hunt = -1;
                return;
            }
            switch (plan.kind)
            {
                case BrainState.Airdrop:
                    {
                        b.state = BrainState.Airdrop;
                        var drop = plan.airdrop >= 0 ? world.airdrops.FirstOrDefault(a => a.id == plan.airdrop) : null;
                        if (drop == null || !DropWorthIt(drop)) { plan.until = world.minutes; return; }
                        if (Vec2.Distance(s.pos, drop.pos) > 8) { SetGoal(b, drop.pos + Spread(s), true); return; }
                        if (!LootContainer(s, b, drop.container)) plan.until = world.minutes;
                        return;
                    }
                case BrainState.Loot:
                    {
                        b.state = BrainState.Loot;
                        var list = ContainersOf(plan.building);
                        if (b.container < 0 || b.searched.Contains(b.container)) b.container = NextContainer(s, b, list);
                        if (b.container < 0) { plan.visited.Add(plan.building); plan.until = world.minutes; return; }
                        if (!LootContainer(s, b, b.container)) b.container = -1;
                        return;
                    }
                case BrainState.Hunt:
                    b.state = BrainState.Hunt; SetGoal(b, plan.goal + Spread(s), true);
                    if (Vec2.Distance(s.pos, plan.goal) < 6) plan.until = world.minutes;
                    return;
                case BrainState.Guard:
                    {
                        b.state = BrainState.Guard; b.idle -= ThinkInterval;
                        if (b.idle <= 0 || !b.hasGoal) { b.idle = world.rng.Range(15, 40); var p = NearestWalkable(plan.goal + Vec2.FromAngle(world.rng.Range(0, 6.28f)) * world.rng.Range(1, 10), 4); if (p.HasValue) SetGoal(b, p.Value, false); }
                        return;
                    }
                default:
                    b.state = BrainState.Travel; SetGoal(b, plan.goal + Spread(s), false);
                    if (Vec2.Distance(s.pos, plan.goal) < 8) plan.until = world.minutes;
                    return;
            }
        }

        bool DropWorthIt(Airdrop a)
        {
            if (!a.landed || a.opened || a.container < 0 || a.container >= world.map.containers.Count) return false;
            var c = world.map.containers[a.container];
            return !c.generated || c.inventory.items.Count > 0;
        }
        Airdrop AirdropNear(Vec2 p)
        {
            Airdrop best = null; float bd = AirdropPull;
            foreach (var a in world.airdrops) { if (!DropWorthIt(a)) continue; float d = Vec2.Distance(a.pos, p); if (d < bd) { bd = d; best = a; } }
            return best;
        }

        void NewPlan(Survivor s, Brain b, Faction f, Plan plan)
        {
            if (plan.kind == BrainState.Loot && plan.building >= 0) plan.visited.Add(plan.building);
            plan.building = plan.airdrop = -1; b.container = -1;
            var drop = s.ai == AiRole.Trader ? null : AirdropNear(s.pos);
            if (drop != null) { plan.kind = BrainState.Airdrop; plan.airdrop = drop.id; plan.goal = drop.pos; plan.until = world.minutes + 180; return; }
            if (f != null && f.hasBounty) { f.hasBounty = false; plan.kind = BrainState.Hunt; plan.goal = f.bounty; plan.until = world.minutes + 150; return; }
            if (s.ai == AiRole.Trader)
            {
                if (plan.kind == BrainState.Travel) { plan.kind = BrainState.Guard; plan.goal = s.pos; plan.until = world.minutes + world.rng.Range(40, 120); return; }
                plan.kind = BrainState.Travel; plan.goal = TravelPoint(s.pos, 200, 900); plan.until = world.minutes + 400; return;
            }
            if (s.ai == AiRole.Raider && f != null)
            {
                int raid = plan.kind == BrainState.Guard && world.rng.Chance(.6f) ? LootBuilding(f.home, 140, plan.visited, f) : -1;
                if (raid >= 0) { plan.kind = BrainState.Loot; plan.building = raid; plan.until = world.minutes + 90; }
                else { plan.kind = BrainState.Guard; plan.goal = f.home; plan.until = world.minutes + world.rng.Range(90, 180); }
                return;
            }
            int bld = LootBuilding(s.pos, 70, plan.visited, f);
            if (bld >= 0) { plan.kind = BrainState.Loot; plan.building = bld; plan.until = world.minutes + 60; return; }
            plan.kind = BrainState.Travel; plan.goal = TravelPoint(s.pos, 150, 900); plan.until = world.minutes + 300;
        }

        bool GraceZone(Vec2 p) { var c = world.community; return world.Day <= 1 && c.baseBuilding >= 0 && Vec2.Distance(p, c.basePos) < 250; }

        Vec2 TravelPoint(Vec2 from, float min, float max)
        {
            if (travelPoints == null)
            {
                travelPoints = new List<Vec2>();
                if (world.gen != null) { foreach (var z in world.gen.landingZones) travelPoints.Add(z.Center); foreach (var l in world.gen.landmarks) travelPoints.Add(l.Center); }
                travelPoints.RemoveAll(p => !Walkable(p) && !NearestWalkable(p, 8).HasValue);
            }
            var ok = travelPoints.Where(p => { float d = Vec2.Distance(p, from); return d >= min && d <= max && !GraceZone(p); }).ToList();
            Vec2 pick = ok.Count > 0 ? ok[world.rng.Range(0, ok.Count)] : from + Vec2.FromAngle(world.rng.Range(0, 6.28f)) * min;
            pick = new Vec2(MathUtil.Clamp(pick.x, 8, world.map.width - 8), MathUtil.Clamp(pick.y, 8, world.map.height - 8));
            var w = NearestWalkable(pick + Vec2.FromAngle(world.rng.Range(0, 6.28f)) * world.rng.Range(0, 25), 10);
            return w.HasValue ? w.Value : pick;
        }

        /// <summary>The nearest building with containers that nobody claims (not the community base, outposts or another group's home).</summary>
        int LootBuilding(Vec2 from, float radius, HashSet<int> visited, Faction f)
        {
            int best = -1; float bd = radius;
            var c = world.community;
            foreach (var bld in world.map.buildings)
            {
                var center = new Vec2(bld.rect.x + bld.rect.w * .5f, bld.rect.y + bld.rect.h * .5f);
                float d = Vec2.Distance(center, from); if (d >= bd || visited.Contains(bld.id)) continue;
                if (bld.id == c.baseBuilding || GraceZone(center) || ContainersOf(bld.id).Count == 0) continue;
                bool claimed = false;
                foreach (var o in c.outposts) if (o.building == bld.id) claimed = true;
                foreach (var g in world.factions) if (g.homeBuilding == bld.id && g != f) claimed = true;
                if (claimed) continue;
                best = bld.id; bd = d;
            }
            return best;
        }

        List<int> ContainersOf(int building)
        {
            if (buildingContainers == null || buildingContainersCount != world.map.containers.Count)
            {
                buildingContainers = new Dictionary<int, List<int>>(); buildingContainersCount = world.map.containers.Count;
                foreach (var c in world.map.containers)
                {
                    if (c.prop < 0) continue; var p = world.map.props[c.prop]; if (p.building < 0) continue;
                    List<int> l; if (!buildingContainers.TryGetValue(p.building, out l)) buildingContainers[p.building] = l = new List<int>();
                    l.Add(c.id);
                }
            }
            List<int> list; return building >= 0 && buildingContainers.TryGetValue(building, out list) ? list : NoContainers;
        }

        int NextContainer(Survivor s, Brain b, List<int> list)
        {
            int best = -1; float bd = 1e9f;
            foreach (int id in list)
            {
                if (b.searched.Contains(id)) continue;
                var c = world.map.containers[id]; var p = world.map.props[c.prop];
                if (p.removed || c.generated && c.inventory.items.Count == 0) { b.searched.Add(id); continue; }
                float d = Vec2.Distance(p.Center, s.pos); if (d < bd) { bd = d; best = id; }
            }
            return best;
        }

        /// <summary>Walks to a container, opens it and transfers the best items. Returns false when it is done with it.</summary>
        bool LootContainer(Survivor s, Brain b, int id)
        {
            if (id < 0 || id >= world.map.containers.Count) return false;
            var c = world.map.containers[id]; if (c.prop < 0) return false;
            var prop = world.map.props[c.prop]; if (prop.removed) return false;
            if (ReachProp(s, prop))
            {
                b.hasGoal = false;
                if (s.lootContainer != c.id) { Submit(s.id, Command.Of(CommandKind.OpenContainer, -1, c.id)); return true; }
                TakeBest(s, c);
                b.searched.Add(c.id); b.lootClock = 0;
                return false;
            }
            b.lootClock += ThinkInterval;
            if (b.lootClock > 25) { b.searched.Add(c.id); b.lootClock = 0; return false; }
            var spot = ApproachPoint(prop, s.pos);
            if (!spot.HasValue) { b.searched.Add(c.id); return false; }
            SetGoal(b, spot.Value, false);
            return true;
        }

        Vec2? ApproachPoint(Prop p, Vec2 from)
        {
            Vec2? best = null; float bd = 1e9f;
            for (int x = p.x - 1; x <= p.x + p.w; x++)
                for (int y = p.y - 1; y <= p.y + p.h; y++)
                {
                    if (x >= p.x && x < p.x + p.w && y >= p.y && y < p.y + p.h) continue;
                    if (world.map.TileBlocked(x, y)) continue;
                    var room = world.map.RoomAt(x, y); if ((room == null ? -1 : room.id) != p.room) continue;
                    var q = new Vec2(x + .5f, y + .5f); if (world.map.Collides(q, Ashfall.Core.Survivor.Radius)) continue;
                    float d = Vec2.Distance(q, from); if (d < bd) { bd = d; best = q; }
                }
            return best;
        }

        /// <summary>How much an AI wants an item (0 = leave it): weapon upgrades, ammo for its guns, meds, armour, bags, throwables, rucksacks.</summary>
        float LootValue(Survivor s, ItemStack it)
        {
            var d = it.Def;
            if (d.resource >= 0) return s.faction == 0 || CarriesRucksack(s) ? 0 : 2;
            if (d.plagueCure > 0) return 5;
            if (IsThrowable(d)) return CountWhere(s, i => IsThrowable(i.Def)) < 3 ? 4 : 0;
            if (d.category == ItemCategory.Firearm) return WeaponScoreOf(d) > WeaponScore(s, BestWeapon(s, false)) + .1f ? 6 : 0;
            if (d.category == ItemCategory.Weapon && d.damage > 0) { var best = BestWeapon(s, true); return WeaponScore(s, it) > WeaponScore(s, best) + .1f ? 3 : 0; }
            if (d.category == ItemCategory.Ammo) { foreach (var g in s.inventory.items) if (g.Def.category == ItemCategory.Firearm && g.Def.ammo == d.id) return 4; return 0; }
            if (d.slot == WearSlot.Back) { var cur = s.Worn(WearSlot.Back); return d.capacity > (cur == null ? 0 : cur.Def.capacity) ? 4 : 0; }
            if (d.slot != WearSlot.None && ArmorOf(d) > 0) { var cur = s.Worn(d.slot); return ArmorOf(d) > (cur == null ? 0 : ArmorOf(cur.Def) * cur.condition) + .02f ? 5 : 0; }
            if (d.category == ItemCategory.Medical && (d.bandageQuality > 0 || d.heal > 0)) return CountWhere(s, i => i.Def.category == ItemCategory.Medical) < 5 ? 3 : 0;
            if (d.stamina > 0) return CountWhere(s, i => i.Def.stamina > 0) < 2 ? 1 : 0;
            return 0;
        }
        static float WeaponScoreOf(ItemDef d) { return 2 + d.damage * (float)Math.Pow(Math.Max(1, d.pellets), .6) * d.accuracy * (1 + d.range / 20); }
        static int CountWhere(Survivor s, Func<ItemStack, bool> f) { int n = 0; foreach (var i in s.inventory.items) if (f(i)) n += i.count; return n; }

        /// <summary>Submits Transfer commands for the best items in an open container (the host validates reach and weight).</summary>
        void TakeBest(Survivor s, Container c)
        {
            float room = s.Capacity * 1.05f - s.CarriedWeight;
            var picks = c.inventory.items.Select(i => new KeyValuePair<float, ItemStack>(LootValue(s, i), i)).Where(kv => kv.Key > 0).OrderByDescending(kv => kv.Key).Take(5).ToList();
            bool weapon = false;
            foreach (var kv in picks)
            {
                var it = kv.Value; var d = it.Def;
                if (d.IsWeapon && d.damage > 0 && !IsThrowable(d)) { if (weapon) continue; weapon = true; }
                float w = it.Weight;
                if (w > room && !(d.resource >= 0 && w <= room + s.Capacity * .5f)) continue;
                room -= w;
                Submit(s.id, new Command { kind = CommandKind.Transfer, item = it.uid, count = it.count, from = new InvRef(InvKind.Container, c.id), to = InvRef.Player, recipe = "" });
            }
        }

        // ---------------------------------------------------------------- steering (every tick)
        void SetGoal(Brain b, Vec2 goal, bool run)
        {
            if (!b.hasGoal || Vec2.Distance(b.goal, goal) > .5f) { if (Vec2.Distance(b.goal, goal) > 4) b.stuckCount = 0; b.goal = goal; b.hasGoal = true; }
            b.run = run;
        }

        void Steer(Survivor s, Brain b, float dt)
        {
            var input = new InputState { face = s.facing };
            if (s.vehicle >= 0 || s.knockedDown > 0 || s.action != null && !s.action.movable) { b.input = input; b.stuckTime = 0; return; }
            Vec2 move = Vec2.Zero;
            if (b.state == BrainState.Fight) move = SteerFight(s, b, dt, ref input);
            else if (b.hasGoal)
            {
                move = Navigate(s, b, b.goal, dt, .5f);
                if (move.Length < .01f && Vec2.Distance(s.pos, b.goal) < .6f) b.hasGoal = false;
            }
            if (b.unstick > 0) { b.unstick -= dt; move = b.unstickDir; }
            if (b.bash) { b.bash = false; input.attack = true; input.face = b.bashFace; move = Vec2.Zero; }
            if (move.Length > .05f)
            {
                input.moveX = move.x; input.moveY = move.y;
                if (b.state != BrainState.Fight) { input.face = move.Angle; input.run = b.run && s.endurance > 25; }
                // Stuck: wanted to move but barely did.
                if (s.speed < .25f) { b.stuckTime += dt; if (b.stuckTime > 1.5f) Unstick(s, b); }
                else b.stuckTime = Math.Max(0, b.stuckTime - dt);
            }
            else b.stuckTime = 0;
            b.input = input;
        }

        void Unstick(Survivor s, Brain b)
        {
            b.stuckTime = 0; b.stuckCount++; b.path.Clear();
            b.unstick = .6f; b.unstickDir = Vec2.FromAngle(world.rng.Range(0, 6.28f));
            if (b.stuckCount > 4) { b.stuckCount = 0; GiveUp(s, b); }
        }

        void GiveUp(Survivor s, Brain b)
        {
            b.hasGoal = false; b.path.Clear();
            if (b.container >= 0) { b.searched.Add(b.container); b.container = -1; }
            var plan = PlanOf(s, b);
            if (plan.kind == BrainState.Loot && plan.building >= 0) plan.visited.Add(plan.building);
            if (s.squad < 0 || plan.kind != BrainState.Guard) plan.until = world.minutes;
            if (b.state == BrainState.Hunt) b.hunt = -1;
        }

        Vec2 SteerFight(Survivor s, Brain b, float dt, ref InputState input)
        {
            Vec2 tp; bool person = b.enemy >= 0;
            if (person)
            {
                var o = Survivor(b.enemy);
                if (o == null || !o.alive || !world.Hostile(s, o)) { b.enemy = -1; b.state = BrainState.Idle; return Vec2.Zero; }
                tp = o.pos;
            }
            else if (b.zombie >= 0 && b.zombie < world.zombies.Count && world.zombies[b.zombie].Alive) tp = world.zombies[b.zombie].pos;
            else { b.zombie = -1; b.state = BrainState.Idle; return Vec2.Zero; }
            var to = tp - s.pos; float d = to.Length;
            input.face = to.Angle;
            var w = s.Primary; var move = Vec2.Zero;
            bool reloading = s.action != null && s.action.kind == ActionKind.Reload;
            if (IsGun(w) && (w.loaded > 0 || reloading))
            {
                float range = w.Def.range * (Elevated(s) ? 1.5f : 1);
                if (!b.holdFire)
                {
                    if (!b.fightLos || d > range * .85f) move = Navigate(s, b, tp, dt, 2);
                    else if (!person && d < 3.5f) move = (to * -1).Normalized;   // back off and keep shooting
                }
                input.aim = d < range && b.fightLos;
                input.attack = input.aim && !reloading;
                if (!person && d < 1.3f && s.shoveCooldown <= 0) input.shove = true;
            }
            else
            {
                float reach = (w != null && w.Def.damage > 0 ? w.Def.range : .95f) + (person ? Ashfall.Core.Survivor.Radius : Zombie.Radius) - .1f;
                if (!b.holdFire && d > reach * .8f) move = Navigate(s, b, tp, dt, reach * .7f);
                input.attack = d <= reach && b.fightLos;
                if (!person && b.closeZombies >= 2 && d < 1.2f && s.shoveCooldown <= 0) input.shove = true;
                input.run = d > 5 && s.endurance > 30;
            }
            return move;
        }

        /// <summary>Path-following move direction toward goal. Handles closed doors and windows by submitting the same commands a player would.</summary>
        Vec2 Navigate(Survivor s, Brain b, Vec2 goal, float dt, float arrive)
        {
            var to = goal - s.pos; float d = to.Length;
            if (d < arrive) { b.path.Clear(); return Vec2.Zero; }
            if (d < 8 && WalkClear(s.pos, goal)) { b.path.Clear(); return to * (1 / d); }
            b.repath -= dt;
            if (b.path.Count == 0 || b.pathIndex >= b.path.Count || b.repath <= 0 || Vec2.Distance(b.pathFor, goal) > 3)
            {
                if (!PlanPath(s, b, goal) && b.path.Count == 0)
                {
                    if (b.pathFails > 6) { b.pathFails = 0; GiveUp(s, b); }
                    return b.pathFails > 0 && b.repath > 0 ? to * (1 / d) : Vec2.Zero;   // no route yet: push straight on briefly
                }
            }
            var cur = new Int2((int)Math.Floor(s.pos.x), (int)Math.Floor(s.pos.y));
            var next = b.path[b.pathIndex];
            if (cur.Equals(next))
            {
                b.pathIndex++;
                if (b.pathIndex >= b.path.Count) { b.repath = 0; return to * (1 / d); }
                next = b.path[b.pathIndex];
            }
            int dx = next.x - cur.x, dy = next.y - cur.y;
            if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) { b.path.Clear(); return Vec2.Zero; }   // drifted off the path
            if (Math.Abs(dx) + Math.Abs(dy) == 1)
            {
                Opening o; world.map.Between(cur.x, cur.y, next.x, next.y, out o);
                if (o != null && o.BlocksMovement)
                {
                    var c = o.Center;
                    if (Vec2.Distance(s.pos, c) > 1.1f) return (c - s.pos).Normalized;
                    HandleOpening(s, b, o, dt); return Vec2.Zero;
                }
            }
            return (new Vec2(next.x + .5f, next.y + .5f) - s.pos).Normalized;
        }

        bool PlanPath(Survivor s, Brain b, Vec2 goal)
        {
            if (humanSearches >= MaxHumanSearches) return false;
            humanSearches++;
            var start = new Int2((int)Math.Floor(s.pos.x), (int)Math.Floor(s.pos.y));
            var to = goal - s.pos; float d = to.Length;
            Vec2 target = goal;
            if (d > 40)
            {
                // Long trips go waypoint by waypoint; failures rotate the waypoint to find a way round water or walls.
                float turn = b.pathFails == 0 ? 0 : (b.pathFails % 2 == 1 ? 1 : -1) * .6f * ((b.pathFails + 1) / 2);
                target = s.pos + Vec2.FromAngle(to.Angle + turn) * 40;
            }
            var open = NearestWalkable(target, 6);
            b.pathFor = goal; b.repath = d > 40 ? 2.5f : 4;
            if (!open.HasValue) { b.path.Clear(); b.pathFails++; b.repath = 1; return false; }
            var g = new Int2((int)Math.Floor(open.Value.x), (int)Math.Floor(open.Value.y));
            if (pathfinder.Find(start, g, b.path, 2000, true)) { b.pathIndex = b.path.Count > 1 ? 1 : 0; b.pathFails = 0; return true; }
            b.path.Clear(); b.pathFails++; b.repath = 1; return false;
        }

        void HandleOpening(Survivor s, Brain b, Opening o, float dt)
        {
            b.bashFace = (o.Center - s.pos).Angle;
            b.openingClock -= dt; if (b.openingClock > 0) return;
            b.openingClock = .8f;
            bool brute = s.ai == AiRole.Raider || s.ai == AiRole.Hunter;
            if (o.barricade > 0) { if (brute) b.bash = true; else GiveUp(s, b); return; }
            if (o.kind == Edge.Door)
            {
                if (o.locked && !InsideOf(s, o.building))
                {
                    if (s.inventory.FindTag("pry") != null) Submit(s.id, Command.Of(CommandKind.Pry, -1, o.id));
                    else if (brute || s.ai == AiRole.Rival) b.bash = true;
                    else GiveUp(s, b);
                    return;
                }
                Submit(s.id, Command.Of(CommandKind.ToggleDoor, -1, o.id)); return;
            }
            if (o.kind == Edge.Window)
            {
                if (o.Climbable) Submit(s.id, Command.Of(CommandKind.Climb, -1, o.id));
                else if (o.locked && !InsideOf(s, o.building)) Submit(s.id, Command.Of(CommandKind.BreakWindow, -1, o.id));
                else Submit(s.id, Command.Of(CommandKind.ToggleDoor, -1, o.id));
                return;
            }
            GiveUp(s, b);
        }

        bool InsideOf(Survivor s, int building) { var inside = world.map.BuildingAt(s.pos); return inside != null && inside.id == building; }

        /// <summary>Grid walk from a to b without crossing walls, closed openings, fences, water or furniture.</summary>
        bool WalkClear(Vec2 a, Vec2 b)
        {
            var m = world.map;
            int x = (int)Math.Floor(a.x), y = (int)Math.Floor(a.y), tx = (int)Math.Floor(b.x), ty = (int)Math.Floor(b.y);
            float dx = b.x - a.x, dy = b.y - a.y; int sx = Math.Sign(dx), sy = Math.Sign(dy);
            float tMaxX = dx == 0 ? float.MaxValue : ((sx > 0 ? x + 1 - a.x : a.x - x) / Math.Abs(dx));
            float tMaxY = dy == 0 ? float.MaxValue : ((sy > 0 ? y + 1 - a.y : a.y - y) / Math.Abs(dy));
            float tDX = dx == 0 ? float.MaxValue : 1 / Math.Abs(dx), tDY = dy == 0 ? float.MaxValue : 1 / Math.Abs(dy);
            for (int guard = 0; guard < 64 && (x != tx || y != ty); guard++)
            {
                int nx = x, ny = y;
                if (tMaxX < tMaxY) { nx += sx; tMaxX += tDX; } else { ny += sy; tMaxY += tDY; }
                Opening o; var e = m.Between(x, y, nx, ny, out o);
                if (e != Edge.None && m.EdgeBlocksMovement(e, o)) return false;
                if (m.TileBlocked(nx, ny)) return false;
                x = nx; y = ny;
            }
            return true;
        }

        bool Walkable(Vec2 p) { int x = (int)Math.Floor(p.x), y = (int)Math.Floor(p.y); return world.map.InBounds(x, y) && !world.map.TileBlocked(x, y) && !world.map.Collides(p, Ashfall.Core.Survivor.Radius); }
        Vec2? NearestWalkable(Vec2 p, int radius)
        {
            int cx = (int)Math.Floor(p.x), cy = (int)Math.Floor(p.y);
            for (int ring = 0; ring <= radius; ring++)
                for (int dx = -ring; dx <= ring; dx++)
                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        if (Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
                        var q = new Vec2(cx + dx + .5f, cy + dy + .5f);
                        if (Walkable(q)) return q;
                    }
            return null;
        }

        // ---------------------------------------------------------------- offscreen (about 1 Hz, abstract)
        /// <summary>Far from every player: move toward goals, loot by rolling containers, resolve fights by chance.</summary>
        void Abstract(Survivor s, Brain b, float dt)
        {
            var f = world.FactionOf(s);
            if (s.faction != 0) { s.hunger = Math.Min(s.hunger, 20); s.thirst = Math.Min(s.thirst, 20); s.fatigue = Math.Min(s.fatigue, 40); }
            s.attackCooldown = s.shoveCooldown = 0; s.knockedDown = 0;
            if (s.vehicle >= 0) return;
            if (s.health < s.MaxHealth) s.health = Math.Min(s.MaxHealth, s.health + dt * world.TimeScale / 60f * 1.6f);   // slow rest healing
            Vec2 goal = s.pos; bool run = false, move = true;
            switch (s.ai)
            {
                case AiRole.Companion:
                    {
                        var leader = Leader(s);
                        if (leader != null && s.order != CompanionOrder.Hold) goal = leader.pos; else move = false;
                        run = true; break;
                    }
                case AiRole.Resident: { var c = world.community; goal = c.baseBuilding >= 0 ? c.basePos : b.anchor; move = Vec2.Distance(s.pos, goal) > 12; break; }
                case AiRole.Enclave: goal = f != null && (f.enclave || f.homeBuilding >= 0) ? f.home : b.anchor; move = Vec2.Distance(s.pos, goal) > 10; break;
                default:
                    {
                        var plan = PlanOf(s, b);
                        if (plan.until <= world.minutes || plan.kind == BrainState.Idle || f != null && f.hasBounty || plan.kind != BrainState.Airdrop && s.ai != AiRole.Trader && AirdropNear(s.pos) != null) NewPlan(s, b, f, plan);
                        b.state = plan.kind;
                        if (plan.kind == BrainState.Loot)
                        {
                            var bld = world.map.buildings[plan.building];
                            goal = new Vec2(bld.rect.x + bld.rect.w * .5f, bld.rect.y + bld.rect.h * .5f);
                            var edge = new Vec2(MathUtil.Clamp(s.pos.x, bld.rect.x, bld.rect.MaxX), MathUtil.Clamp(s.pos.y, bld.rect.y, bld.rect.MaxY));
                            if (Vec2.Distance(s.pos, edge) < 3) { move = false; if (!AbstractLoot(s, b, ContainersOf(plan.building))) { plan.visited.Add(plan.building); plan.until = world.minutes; } }
                        }
                        else if (plan.kind == BrainState.Airdrop)
                        {
                            var drop = world.airdrops.FirstOrDefault(a => a.id == plan.airdrop);
                            if (drop == null || !DropWorthIt(drop)) { plan.until = world.minutes; move = false; break; }
                            goal = drop.pos; run = true;
                            if (Vec2.Distance(s.pos, goal) < 3) { move = false; AbstractTake(s, world.map.containers[drop.container]); plan.until = world.minutes; }
                        }
                        else
                        {
                            goal = plan.goal; run = plan.kind == BrainState.Hunt;
                            if (Vec2.Distance(s.pos, goal) < 8) { move = plan.kind == BrainState.Guard ? false : move; if (plan.kind != BrainState.Guard) plan.until = world.minutes; }
                        }
                        break;
                    }
            }
            if (move && Vec2.Distance(s.pos, goal) > 1.5f) MoveAbstract(s, goal, (run ? 3.2f : 2.1f) * dt);
            AbstractZombies(s);
            if (s.alive) AbstractSkirmish(s);
        }

        void MoveAbstract(Survivor s, Vec2 goal, float step)
        {
            var to = goal - s.pos; float d = to.Length; if (d < .01f) return;
            step = Math.Min(step, d);
            foreach (float turn in Detours)
            {
                float a = to.Angle + turn; var delta = Vec2.FromAngle(a) * step;
                var p = world.map.Slide(s.pos, delta, Ashfall.Core.Survivor.Radius);
                if (Vec2.Distance(p, s.pos) >= step * .5f) { s.pos = p; s.facing = a; return; }
            }
            // Abstract door use: step through an edge onto free ground (never into water or furniture).
            var q = s.pos + to * (step / d);
            if (Walkable(q)) s.pos = q;
        }

        /// <summary>Rolls and loots one container of the building per call; false when nothing is left.</summary>
        bool AbstractLoot(Survivor s, Brain b, List<int> list)
        {
            int id = NextContainer(s, b, list); if (id < 0) return false;
            b.searched.Add(id);
            AbstractTake(s, world.map.containers[id]);
            return true;
        }

        void AbstractTake(Survivor s, Container c)
        {
            world.RollContainer(c);
            float room = s.Capacity * 1.05f - s.CarriedWeight;
            var picks = c.inventory.items.Where(i => LootValue(s, i) > 0).OrderByDescending(i => LootValue(s, i)).Take(4).ToList();
            foreach (var it in picks)
            {
                if (it.Weight > room && it.Def.resource < 0) continue;
                room -= it.Weight;
                var moved = c.inventory.Take(it.uid, it.count); if (moved == null) continue;
                s.inventory.Add(moved);
                var d = moved.Def;
                if (d.slot != WearSlot.None && (d.slot == WearSlot.Back || ArmorOf(d) > 0) && s.inventory.items.Contains(moved)) s.worn[d.slot] = moved.uid;
            }
            var best = BestWeapon(s, false); if (best != null) s.primary = best.uid;
            var gun = s.Primary;
            if (IsGun(gun) && gun.loaded < gun.Def.magazine) { int load = Math.Min(gun.Def.magazine - gun.loaded, s.inventory.Count(gun.Def.ammo)); s.inventory.Consume(gun.Def.ammo, load); gun.loaded += load; }
        }

        float Power(Survivor s)
        {
            var w = s.Primary;
            if (IsGun(w) && (w.loaded > 0 || s.inventory.Count(w.Def.ammo) > 0)) return 1.6f + w.Def.damage * (float)Math.Sqrt(Math.Max(1, w.Def.pellets)) * .6f;
            if (w != null && w.Def.damage > 0) return .6f + w.Def.damage;
            return .45f;
        }
        void SpendRound(Survivor s)
        {
            var w = s.Primary; if (!IsGun(w)) return;
            if (w.loaded > 0) w.loaded--; else s.inventory.Consume(w.Def.ammo, 1);
        }

        /// <summary>Offscreen zombie fights resolve by chance: the survivor kills some, takes hits from others, and sometimes dies.</summary>
        void AbstractZombies(Survivor s)
        {
            world.index.Query(s.pos, 6, humanQuery);
            if (humanQuery.Count == 0) return;
            float power = Power(s);
            int n = 0;
            foreach (var z in humanQuery.ToList())
            {
                if (n++ >= 4 || !s.alive) break;
                var kd = Bestiary.Get(z.kind);
                if (world.rng.Chance(Math.Min(.9f, .5f * power / Math.Max(1, kd.threat)))) { SpendRound(s); HitZombie(z, s, z.hp * 10 + 5, 0, false); continue; }
                if (!world.rng.Chance(.1f * kd.threat * kd.damageScale)) continue;
                float damage = world.rng.Range(4, 10) * kd.damageScale;
                var vest = s.Worn(WearSlot.Torso); if (vest != null) damage *= 1 - ArmorOf(vest.Def) * vest.condition * .5f;
                s.health -= damage; s.lastHitBy = -1;
                if (s.health <= 0) Kill(s, "Killed by " + (StartsWithVowel(kd.name) ? "an " : "a ") + kd.name + ".");
            }
        }
        static bool StartsWithVowel(string t) { return t.Length > 0 && "AEIOUaeiou".IndexOf(t[0]) >= 0; }

        /// <summary>Offscreen gunfights between hostile survivors resolve by chance through DamageSurvivor (so kills reach the feed).</summary>
        void AbstractSkirmish(Survivor s)
        {
            float power = Power(s);
            foreach (var o in world.survivors)
            {
                if (!o.alive || o == s || o.faction == s.faction) continue;
                if ((o.pos - s.pos).SqrLength > 18 * 18 || !world.Hostile(s, o) || !world.map.LineOfSight(s.pos, o.pos)) continue;
                if (world.rng.Chance(.3f * power / 2))
                {
                    var c = Characters.Get(s.character);
                    SpendRound(s);
                    DamageSurvivor(o, s, world.rng.Range(8, 20) * (c != null ? c.damageScale : 1), IsGun(s.Primary) ? "shot" : "melee");
                }
                return;
            }
        }
    }
}
