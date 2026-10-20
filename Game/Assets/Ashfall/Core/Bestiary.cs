using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>Zombie kinds. Threat 1 = take it alone, 3 = dangerous alone, 4+ = fight as a group or avoid.</summary>
    public enum ZombieKind : byte { Walker, Crawler, Screamer, Bloater, Armored, Feral, Juggernaut }

    /// <summary>Game-owned stats for a zombie kind.</summary>
    public sealed class ZombieKindDef
    {
        public ZombieKind kind; public string id = "", name = "", description = "";
        public int threat = 1;                 // 1..5 skulls shown to the player
        public float hp = 1.2f, speedScale = 1, damageScale = 1, scale = 1;
        public int influence;                  // community influence for the kill
    }

    public static class Bestiary
    {
        static readonly ZombieKindDef[] defs =
        {
            new ZombieKindDef { kind = ZombieKind.Walker, id = "walker", name = "Walker", threat = 1, hp = 1.2f, description = "Slow and numerous. Dangerous only in crowds." },
            new ZombieKindDef { kind = ZombieKind.Crawler, id = "crawler", name = "Crawler", threat = 1, hp = .8f, speedScale = .45f, description = "Drags itself along the ground." },
            new ZombieKindDef { kind = ZombieKind.Screamer, id = "screamer", name = "Screamer", threat = 2, hp = .9f, influence = 5, description = "Weak, but its scream calls every zombie nearby. Kill it first." },
            new ZombieKindDef { kind = ZombieKind.Bloater, id = "bloater", name = "Bloater", threat = 2, hp = 1.6f, speedScale = .6f, scale = 1.25f, influence = 5, description = "Bursts into a toxic cloud. Kill it from range." },
            new ZombieKindDef { kind = ZombieKind.Armored, id = "armored", name = "Armored", threat = 2, hp = 2.4f, scale = 1.08f, influence = 5, description = "Riot gear turns blows from the front. Knock it down or hit it from behind." },
            new ZombieKindDef { kind = ZombieKind.Feral, id = "feral", name = "Feral", threat = 3, hp = 3f, speedScale = 2.6f, damageScale = 1.6f, influence = 15, description = "Fast, leaping hunter. Dangerous alone." },
            new ZombieKindDef { kind = ZombieKind.Juggernaut, id = "juggernaut", name = "Juggernaut", threat = 4, hp = 26f, speedScale = .9f, damageScale = 3f, scale = 1.7f, influence = 50, description = "A mountain of dead flesh. Avoid it, or bring friends and explosives." },
        };
        public static IEnumerable<ZombieKindDef> All { get { return defs; } }
        public static ZombieKindDef Get(ZombieKind kind) { return defs[(int)kind]; }
        /// <summary>Threat of plague hearts (shown with the same skull scale).</summary>
        public const int HeartThreat = 5;
        public const int HeartInfluence = 100;

        // Game-owned balance for freak behaviour (distances in metres, times in real seconds unless noted).
        public const float FreakClearance = 60;                 // no freaks this close to the start site
        public const float HeartClearance = 400;                // plague hearts at least this far from the start site
        public const float ScreamNoise = 70, ScreamPanicRadius = 10, ScreamStunRadius = 5;
        public const float GasRadius = 3.5f, GasMinutes = 10;  // in-game minutes
        public const float GasDamagePerSecond = 4, FireDamagePerSecond = 6, BurnDamagePerSecond = .4f;
        public const float ArmorFrontFactor = .2f;              // armored zombies take 20% from the front unless knocked down
        public const float PounceMin = 3, PounceMax = 7, PounceSpeed = 9;
        public const float ChargeMin = 6, ChargeMax = 16, ChargeSpeed = 7.5f;
        public const float GrabSeconds = 2, GrabKillFraction = .35f;
        public const float HeartPulseRange = 40, HeartDiscoverRange = 30;
        public const int HeartSpawnCap = 12;
        public const float HordeMetersPerMinute = 1.8f;         // abstract horde drift per in-game minute

        /// <summary>Applies a kind's stats to a freshly spawned zombie (hp, speed, senses). Call once per zombie.</summary>
        public static void Apply(Zombie z, ZombieKind kind, Rng r)
        {
            var d = Get(kind); z.kind = kind;
            switch (kind)
            {
                case ZombieKind.Walker: z.maxHp = z.hp; break;
                case ZombieKind.Crawler: if (!z.crawler) { z.crawler = true; z.speed *= .45f; } z.maxHp = z.hp; break;
                default:
                    if (z.crawler) { z.crawler = false; z.speed /= .45f; }
                    z.hp = z.maxHp = d.hp * r.Range(.9f, 1.1f); z.speed *= d.speedScale;
                    break;
            }
            if (kind == ZombieKind.Feral) { z.speed = MathUtil.Clamp(z.speed, 3.4f, 5.2f); z.sight = Math.Max(z.sight, 1.2f); z.hearing = Math.Max(z.hearing, 1.2f); }
            else if (kind == ZombieKind.Screamer) z.sight = Math.Max(z.sight, 1.1f);
            else if (kind == ZombieKind.Juggernaut) z.speed = Math.Max(z.speed, 1.1f);
        }

        public static string Article(string name) { return name.Length > 0 && "AEIOUaeiou".IndexOf(name[0]) >= 0 ? "an " : "a "; }
    }

    /// <summary>A plague heart: a pulsing growth inside a building that spawns plague zombies. Destroying every heart wins the legacy.</summary>
    [Serializable]
    public sealed class PlagueHeart
    {
        public int id; public Vec2 pos; public int building = -1;
        public float hp = 45, maxHp = 45, spawnClock, pulse;   // pulse: 1 right after a spawn pulse, decays to 0 (view flash)
        public bool destroyed, discovered;
        public float screamClock;                               // cooldown before the next defensive scream
        public const float Radius = .9f;
    }

    /// <summary>Lingering area hazard: bloater gas or burning molotov fuel. Damages survivors (and zombies, for fire) inside.</summary>
    [Serializable]
    public sealed class Hazard { public Vec2 pos; public float radius = 3, until; public bool fire; public int by = -1; }   // until = in-game minutes

    /// <summary>A roaming pack of walkers that drifts between towns, or marches on a target (SendHorde).</summary>
    [Serializable]
    public sealed class Horde
    {
        public int id; public Vec2 center, target, waypoint;
        public bool viaWaypoint;   // heading for the bridge before the target
        public bool sent;          // marching on a SendHorde target; stays there until ReleaseHorde
        public readonly List<int> members = new List<int>();   // zombie ids (rebuilt from Zombie.horde on load)
    }

    /// <summary>Special moves that take over a freak's update for a moment.</summary>
    public enum FreakAct : byte { None, Pounce, Charge, Grab, Flee, Recover }

    public sealed partial class Zombie
    {
        public int horde = -1;                       // Horde id, -1 = none
        public FreakAct act; public float actTimer;  // current special move (views may show Pounce/Charge as a lunge, Grab as an attack)
        public Vec2 actDir; public int grabbed = -1; // juggernaut: survivor held
        public float ability, grabCooldown;          // cooldowns for scream / pounce / charge, and grabs
        public float burning; public int burnBy = -1; // seconds left on fire, and the survivor who lit it
        public bool fled;
        internal float stepClock;
    }

    public sealed partial class World
    {
        public readonly List<PlagueHeart> hearts = new List<PlagueHeart>();
        public readonly List<Hazard> hazards = new List<Hazard>();
        public readonly List<Horde> hordes = new List<Horde>();
        public int nextHorde;
        public int HeartsRemaining { get { int n = 0; foreach (var h in hearts) if (!h.destroyed) n++; return n; } }
        public Horde HordeById(int id) { foreach (var h in hordes) if (h.id == id) return h; return null; }

        const int BestiaryVersion = 1;
        internal void WriteBestiary(BinaryWriter b)
        {
            b.Write(BestiaryVersion);
            // Per living zombie, in SaveCodec's order.
            int alive = 0; foreach (var z in zombies) if (z.Alive) alive++;
            b.Write(alive);
            foreach (var z in zombies) if (z.Alive) { b.Write((byte)z.kind); b.Write(z.maxHp); b.Write(z.plague); b.Write(z.horde); }
            b.Write(hearts.Count);
            foreach (var h in hearts) { b.Write(h.id); b.Write(h.pos.x); b.Write(h.pos.y); b.Write(h.building); b.Write(h.hp); b.Write(h.maxHp); b.Write(h.spawnClock); b.Write(h.destroyed); b.Write(h.discovered); }
            b.Write(hazards.Count);
            foreach (var h in hazards) { b.Write(h.pos.x); b.Write(h.pos.y); b.Write(h.radius); b.Write(h.until); b.Write(h.fire); b.Write(h.by); }
            b.Write(nextHorde); b.Write(hordes.Count);
            foreach (var h in hordes) { b.Write(h.id); b.Write(h.center.x); b.Write(h.center.y); b.Write(h.target.x); b.Write(h.target.y); b.Write(h.waypoint.x); b.Write(h.waypoint.y); b.Write(h.viaWaypoint); b.Write(h.sent); }
        }

        internal void ReadBestiary(BinaryReader b)
        {
            int version = b.ReadInt32();
            if (version == 0) return;   // contract placeholder: walkers only
            if (version > BestiaryVersion) throw new InvalidDataException("Save bestiary section is newer than this build.");
            int n = b.ReadInt32();
            var alive = zombies.Where(z => z.Alive).ToList();
            if (n != alive.Count) throw new InvalidDataException("Save zombie kinds do not match the zombies.");
            foreach (var z in alive)
            {
                byte kind = b.ReadByte(); z.kind = kind <= (byte)ZombieKind.Juggernaut ? (ZombieKind)kind : ZombieKind.Walker;
                z.maxHp = b.ReadSingle(); z.plague = b.ReadBoolean(); z.horde = b.ReadInt32();
                if (z.state == ZombieState.Lunge) z.state = ZombieState.Chase;   // special moves are not saved
            }
            hearts.Clear();
            int hc = b.ReadInt32();
            for (int i = 0; i < hc; i++) hearts.Add(new PlagueHeart { id = b.ReadInt32(), pos = new Vec2(b.ReadSingle(), b.ReadSingle()), building = b.ReadInt32(), hp = b.ReadSingle(), maxHp = b.ReadSingle(), spawnClock = b.ReadSingle(), destroyed = b.ReadBoolean(), discovered = b.ReadBoolean() });
            hazards.Clear();
            int zc = b.ReadInt32();
            for (int i = 0; i < zc; i++) hazards.Add(new Hazard { pos = new Vec2(b.ReadSingle(), b.ReadSingle()), radius = b.ReadSingle(), until = b.ReadSingle(), fire = b.ReadBoolean(), by = b.ReadInt32() });
            nextHorde = b.ReadInt32();
            hordes.Clear();
            int hn = b.ReadInt32();
            for (int i = 0; i < hn; i++) hordes.Add(new Horde { id = b.ReadInt32(), center = new Vec2(b.ReadSingle(), b.ReadSingle()), target = new Vec2(b.ReadSingle(), b.ReadSingle()), waypoint = new Vec2(b.ReadSingle(), b.ReadSingle()), viaWaypoint = b.ReadBoolean(), sent = b.ReadBoolean() });
            foreach (var z in zombies) { if (z.horde < 0) continue; var h = HordeById(z.horde); if (h == null) z.horde = -1; else h.members.Add(z.id); }
        }
    }

    public sealed partial class Simulation
    {
        readonly List<Zombie> freakQuery = new List<Zombie>();   // never the shared `query`: callers may be iterating it
        readonly List<PlagueHeart> liveHearts = new List<PlagueHeart>();
        float hazardClock, heartClock, hordeMinutes;
        bool blasting, hitting; string blastCause = "", killCause = "";

        /// <summary>Per-tick freak behaviour (screams, gas clouds, charges), plague heart spawning and hordes.</summary>
        void UpdateBestiary(float dt, float hours)
        {
            hazardClock += dt;
            if (hazardClock >= .5f) { Hazards(hazardClock); hazardClock = 0; }
            heartClock += dt;
            if (heartClock >= .5f) { Hearts(heartClock); heartClock = 0; }
            foreach (var h in liveHearts) if (!h.destroyed) HeartObstacle(h);
            hordeMinutes += hours * 60;
            if (hordeMinutes >= 1) { Hordes(hordeMinutes); hordeMinutes = 0; }
        }

        /// <summary>Hourly migration: the dead are slowly replaced from the county edges, ferals come out at night.</summary>
        void HourlyBestiary()
        {
            var w = world;
            if (w.settings.respawn && w.Day > 2 && w.LivingZombies < w.initialPopulation * .8f)
                for (int i = 0; i < 6; i++)
                {
                    int x = w.rng.Chance(.5f) ? w.rng.Range(4, 40) : w.map.width - w.rng.Range(4, 40), y = w.rng.Range(4, w.map.height - 4);
                    var p = new Vec2(x + .5f, y + .5f);
                    if (w.map.TileBlocked(x, y) || w.map.IsWater(x, y)) continue;
                    bool far = true; foreach (var s in w.survivors) if (Vec2.Distance(s.pos, p) <= 120) { far = false; break; }
                    if (!far) continue;
                    var z = w.AddZombie(p, w.rng);
                    Bestiary.Apply(z, z.crawler ? ZombieKind.Crawler : w.RollKind(p, w.rng), w.rng);
                }
            NightFerals();
        }

        /// <summary>A generator seeded by the county seed, the in-game minute and a salt: deterministic (also across save/load)
        /// without disturbing world.rng, for background decisions such as night spawns and horde routes.</summary>
        Rng Moment(int salt) { return new Rng((ulong)world.settings.seed * 7919UL + (ulong)(world.minutes * 10) + (ulong)salt * 104729UL, 41); }

        /// <summary>At night, ferals come out of the woods near the community.</summary>
        void NightFerals()
        {
            var w = world; float density = w.settings.freakDensity;
            if (w.Daylight > .1f || density <= 0) return;
            var r = Moment(1);
            foreach (var s in w.survivors)
            {
                if (!s.alive || s.offscreen || s.faction != 0 || !r.Chance(.3f * Math.Min(2, density))) continue;
                w.index.Query(s.pos, 170, freakQuery);
                int ferals = 0; foreach (var z in freakQuery) if (z.kind == ZombieKind.Feral) ferals++;
                if (ferals >= 5) continue;
                for (int t = 0; t < 12; t++)
                {
                    var p = s.pos + Vec2.FromAngle(r.Range(0, 6.28f)) * r.Range(90, 170);
                    var zone = w.map.ZoneAt((int)p.x, (int)p.y);
                    if (zone != ZoneKind.Town && zone != ZoneKind.Commercial && zone != ZoneKind.Water && w.LandFree(p) && !w.map.Indoors(p)) { var f = w.SpawnKind(p, ZombieKind.Feral, r); f.state = ZombieState.Wander; f.target = s.pos; break; }
                }
            }
        }

        // ---------------------------------------------------------------- hazards
        void Hazards(float step)
        {
            var hz = world.hazards;
            for (int i = hz.Count - 1; i >= 0; i--)
            {
                var h = hz[i];
                if (h.until <= world.minutes) { hz.RemoveAt(i); continue; }
                var thrower = h.fire ? Survivor(h.by) : null;
                foreach (var s in world.survivors)
                {
                    if (!s.alive || s.offscreen || s.vehicle >= 0 || Vec2.Distance(s.pos, h.pos) > h.radius) continue;
                    if (h.fire) DamageSurvivor(s, thrower == s ? null : thrower, Bestiary.FireDamagePerSecond * step, "Burned to death.");
                    else { DamageSurvivor(s, null, Bestiary.GasDamagePerSecond * step, "Choked on bloater gas."); s.panic = Math.Min(100, s.panic + 8 * step); }
                }
                if (!h.fire) continue;
                world.index.Query(h.pos, h.radius, freakQuery);
                foreach (var z in freakQuery) { z.burning = Math.Max(z.burning, 4); z.burnBy = h.by; }
            }
        }

        // ---------------------------------------------------------------- plague hearts
        Survivor NearestSurvivor(Vec2 p, out float distance)
        {
            Survivor best = null; distance = float.MaxValue;
            foreach (var s in world.survivors)
            {
                if (!s.alive || s.offscreen) continue;
                float d = Vec2.Distance(s.pos, p); if (d < distance) { distance = d; best = s; }
            }
            return best;
        }
        bool NearSurvivor(Vec2 p, float radius) { float d; return NearestSurvivor(p, out d) != null && d <= radius; }

        void Hearts(float step)
        {
            liveHearts.Clear();
            foreach (var h in world.hearts)
            {
                if (h.destroyed) continue;
                h.screamClock -= step; h.pulse = Math.Max(0, h.pulse - step);
                float d; var near = NearestSurvivor(h.pos, out d);
                if (near == null || d > ActiveRadius) continue;
                liveHearts.Add(h);
                if (!h.discovered)
                    foreach (var s in world.survivors)
                        if (s.alive && s.faction == 0 && !s.offscreen && Vec2.Distance(s.pos, h.pos) <= Bestiary.HeartDiscoverRange && world.map.LineOfSight(s.pos, h.pos)) { h.discovered = true; break; }
                if (d > Bestiary.HeartPulseRange) continue;
                h.spawnClock -= step;
                if (h.spawnClock > 0) continue;
                h.spawnClock = world.rng.Range(14f, 22f); h.pulse = 1;
                world.Emit(EventKind.Heart, h.pos, -1, "pulse");
                SpawnPlague(h, world.rng.Range(1, 3), near, Bestiary.HeartSpawnCap);
            }
        }

        /// <summary>Plague zombies crawl out of a heart, up to a cap around it, and go for the survivor.</summary>
        void SpawnPlague(PlagueHeart h, int count, Survivor prey, int cap)
        {
            world.index.Query(h.pos, 30, freakQuery);
            int plague = 0; foreach (var z in freakQuery) if (z.plague) plague++;
            for (int i = 0; i < count && plague < cap; i++)
                for (int t = 0; t < 10; t++)
                {
                    var p = h.pos + Vec2.FromAngle(world.rng.Range(0, 6.28f)) * world.rng.Range(1.6f, 4.5f);
                    if (!world.LandFree(p) || !world.map.LineOfSight(h.pos, p)) continue;
                    var z = world.SpawnKind(p, ZombieKind.Walker, world.rng); z.plague = true; plague++;
                    if (prey != null) { z.state = ZombieState.Investigate; z.target = prey.pos; z.timer = 20; }
                    break;
                }
        }

        /// <summary>Hearts are solid: survivors and zombies are pushed out of them.</summary>
        void HeartObstacle(PlagueHeart h)
        {
            foreach (var s in world.survivors)
            {
                if (!s.alive || s.offscreen || s.vehicle >= 0) continue;
                var d = s.pos - h.pos; float l = d.Length, min = PlagueHeart.Radius + Ashfall.Core.Survivor.Radius;
                if (l < min) s.pos = world.map.Slide(s.pos, (l < 1e-4f ? new Vec2(1, 0) : d * (1 / l)) * (min - l), Ashfall.Core.Survivor.Radius);
            }
            world.index.Query(h.pos, PlagueHeart.Radius + Zombie.Radius, freakQuery);
            foreach (var z in freakQuery)
            {
                var d = z.pos - h.pos; float l = d.Length, min = PlagueHeart.Radius + Zombie.Radius;
                if (l < min) { z.pos = world.map.Slide(z.pos, (l < 1e-4f ? new Vec2(0, 1) : d * (1 / l)) * (min - l), Zombie.Radius); world.index.Moved(z); }
            }
        }

        /// <summary>The nearest intact heart inside a melee arc (used by SimCombat for swings and shots).</summary>
        public PlagueHeart HeartInArc(Survivor s, float range, float arcRadians)
        {
            PlagueHeart best = null; float bestD = range + PlagueHeart.Radius;
            foreach (var h in world.hearts)
            {
                if (h.destroyed) continue;
                var to = h.pos - s.pos; float d = to.Length;
                if (d > bestD) continue;
                if (Math.Abs(MathUtil.WrapAngle(to.Angle - s.facing)) > arcRadians * .5f + .3f) continue;
                if (!world.map.LineOfSight(s.pos, h.pos)) continue;
                best = h; bestD = d;
            }
            return best;
        }

        /// <summary>Damages a plague heart; by may be null for explosions and artillery. A struck heart screams for defenders.</summary>
        public void DamageHeart(PlagueHeart h, Survivor by, float damage)
        {
            if (h == null || h.destroyed || damage <= 0) return;
            h.hp -= damage; h.discovered = true;
            world.Emit(EventKind.Heart, h.pos, by == null ? -1 : by.id, "hit");
            if (h.hp <= 0) { DestroyHeart(h, by); return; }
            if (h.screamClock > 0) return;
            h.screamClock = 7;
            world.Emit(EventKind.Heart, h.pos, by == null ? -1 : by.id, "scream");
            world.MakeNoise(h.pos, 90);
            SpawnPlague(h, world.rng.Range(2, 4), by, Bestiary.HeartSpawnCap + 4);
        }

        void DestroyHeart(PlagueHeart h, Survivor by)
        {
            h.hp = 0; h.destroyed = true; h.pulse = 0;
            world.Emit(EventKind.HeartDestroyed, h.pos, by == null ? -1 : by.id, "A plague heart is destroyed.");
            world.Emit(EventKind.KillFeed, h.pos, by == null ? -1 : by.id, KillerName(by) + " destroyed a plague heart");
            AwardInfluence(by, Bestiary.HeartInfluence, h.pos);
            if (ItemCatalog.Exists("plague_sample"))
            {
                var pile = world.PileAt((int)h.pos.x, (int)h.pos.y, true);
                pile.inventory.Add(ItemCatalog.Make("plague_sample", world.rng.Range(2, 4)));
            }
        }

        /// <summary>Community influence for a kill by a member of the player's community (faction 0).</summary>
        void AwardInfluence(Survivor by, int amount, Vec2 at)
        {
            if (by == null || by.faction != 0 || amount <= 0) return;
            world.community.influence += amount;
            world.Emit(EventKind.Influence, at, by.id, "+" + amount);
        }

        string KillerName(Survivor by)
        {
            if (by != null) return by.name;
            if (blasting) return blastCause.IndexOf("artillery", StringComparison.OrdinalIgnoreCase) >= 0 ? "Artillery" : "A blast";
            return killCause.Length > 0 ? killCause : "The county";
        }

        /// <summary>Area damage from grenades, bloater bursts and artillery: zombies, survivors and hearts in radius.
        /// Explosions knock down even juggernauts and ignore riot armour.</summary>
        public void Blast(Vec2 at, float radius, float damage, Survivor by, string cause)
        {
            world.Emit(EventKind.Explosion, at, by == null ? -1 : by.id, cause);
            world.MakeNoise(at, 90, by == null ? -1 : by.id);
            var list = new List<Zombie>(); world.index.Query(at, radius, list);
            blasting = true; blastCause = cause ?? "";
            try
            {
                foreach (var z in list) { float f = 1 - Vec2.Distance(z.pos, at) / Math.Max(.1f, radius); HitZombie(z, by, damage * (.4f + .6f * f), 1, false); }
                foreach (var h in world.hearts) if (!h.destroyed && Vec2.Distance(h.pos, at) < radius + PlagueHeart.Radius) DamageHeart(h, by, damage * 4);
            }
            finally { blasting = false; blastCause = ""; }
            foreach (var s in world.survivors) if (s.alive && Vec2.Distance(s.pos, at) < radius) DamageSurvivor(s, by, damage * 40 * (1 - Vec2.Distance(s.pos, at) / Math.Max(.1f, radius)), cause);
        }
    }
}
