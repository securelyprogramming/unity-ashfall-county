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
            new ZombieKindDef { kind = ZombieKind.Screamer, id = "screamer", name = "Screamer", threat = 2, hp = .9f, description = "Weak, but its scream calls every zombie nearby. Kill it first." },
            new ZombieKindDef { kind = ZombieKind.Bloater, id = "bloater", name = "Bloater", threat = 2, hp = 1.5f, speedScale = .6f, description = "Bursts into a toxic cloud. Kill it from range." },
            new ZombieKindDef { kind = ZombieKind.Armored, id = "armored", name = "Armored", threat = 2, hp = 2.4f, description = "Riot gear turns blows from the front. Knock it down or hit it from behind." },
            new ZombieKindDef { kind = ZombieKind.Feral, id = "feral", name = "Feral", threat = 3, hp = 3f, speedScale = 2.6f, damageScale = 1.6f, description = "Fast, leaping hunter. Dangerous alone." },
            new ZombieKindDef { kind = ZombieKind.Juggernaut, id = "juggernaut", name = "Juggernaut", threat = 4, hp = 30f, speedScale = .9f, damageScale = 3f, scale = 1.7f, description = "A mountain of dead flesh. Avoid it, or bring friends and explosives." },
        };
        public static IEnumerable<ZombieKindDef> All { get { return defs; } }
        public static ZombieKindDef Get(ZombieKind kind) { return defs[(int)kind]; }
        /// <summary>Threat of plague hearts (shown with the same skull scale).</summary>
        public const int HeartThreat = 5;
    }

    /// <summary>A plague heart: a pulsing growth inside a building that spawns plague zombies. Destroying every heart wins the legacy.</summary>
    [Serializable]
    public sealed class PlagueHeart
    {
        public int id; public Vec2 pos; public int building = -1;
        public float hp = 60, maxHp = 60, spawnClock, pulse;
        public bool destroyed, discovered;
        public const float Radius = .9f;
    }

    /// <summary>Lingering area hazard: bloater gas or burning molotov fuel. Damages survivors (and zombies, for fire) inside.</summary>
    [Serializable]
    public sealed class Hazard { public Vec2 pos; public float radius = 3, until; public bool fire; public int by = -1; }   // until = in-game minutes

    public sealed partial class World
    {
        public readonly List<PlagueHeart> hearts = new List<PlagueHeart>();
        public readonly List<Hazard> hazards = new List<Hazard>();
        public int HeartsRemaining { get { int n = 0; foreach (var h in hearts) if (!h.destroyed) n++; return n; } }

        /// <summary>Assigns zombie kinds, places plague hearts and hordes after the base population is spawned.</summary>
        void SetupBestiary(Building home) { }

        internal void WriteBestiary(BinaryWriter b) { b.Write(0); }
        internal void ReadBestiary(BinaryReader b) { b.ReadInt32(); }
    }

    public sealed partial class Simulation
    {
        /// <summary>Per-tick freak behaviour (screams, gas clouds, charges), plague heart spawning and hordes.</summary>
        void UpdateBestiary(float dt, float hours) { }

        /// <summary>Hourly migration: the dead are slowly replaced from the county edges, hordes drift.</summary>
        void HourlyBestiary()
        {
            var w = world;
            if (w.settings.respawn && w.Day > 2 && w.LivingZombies < w.initialPopulation * .8f)
                for (int i = 0; i < 6; i++)
                {
                    int x = w.rng.Chance(.5f) ? w.rng.Range(4, 40) : w.map.width - w.rng.Range(4, 40), y = w.rng.Range(4, w.map.height - 4);
                    if (!w.map.TileBlocked(x, y) && w.survivors.All(s => Vec2.Distance(s.pos, new Vec2(x, y)) > 120)) w.AddZombie(new Vec2(x + .5f, y + .5f), w.rng);
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

        /// <summary>Damages a plague heart; by may be null for explosions and artillery.</summary>
        public void DamageHeart(PlagueHeart h, Survivor by, float damage)
        {
            if (h == null || h.destroyed) return;
            h.hp -= damage; h.discovered = true;
            world.Emit(EventKind.Heart, h.pos, by == null ? -1 : by.id, "hit");
            if (h.hp <= 0) { h.hp = 0; h.destroyed = true; world.Emit(EventKind.HeartDestroyed, h.pos, by == null ? -1 : by.id, "A plague heart is destroyed."); }
        }

        /// <summary>Area damage from grenades, bloater bursts and artillery: zombies, survivors and hearts in radius.</summary>
        public void Blast(Vec2 at, float radius, float damage, Survivor by, string cause)
        {
            world.Emit(EventKind.Explosion, at, by == null ? -1 : by.id, cause);
            world.MakeNoise(at, 90, by == null ? -1 : by.id);
            var list = new List<Zombie>(); world.index.Query(at, radius, list);
            foreach (var z in list) { float f = 1 - Vec2.Distance(z.pos, at) / Math.Max(.1f, radius); HitZombie(z, by, damage * (.4f + .6f * f), 1, false); }
            foreach (var h in world.hearts) if (!h.destroyed && Vec2.Distance(h.pos, at) < radius + PlagueHeart.Radius) DamageHeart(h, by, damage * 4);
            foreach (var s in world.survivors) if (s.alive && Vec2.Distance(s.pos, at) < radius) DamageSurvivor(s, by, damage * 40 * (1 - Vec2.Distance(s.pos, at) / Math.Max(.1f, radius)), cause);
        }
    }
}
