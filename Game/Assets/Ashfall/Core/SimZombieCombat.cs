using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>The zombie side of combat: damage taken, knockdowns, deaths and zombie strikes on survivors (Bestiary owner).</summary>
    public sealed partial class Simulation
    {
        void HitZombie(Zombie z, Survivor by, float damage, float knock, bool blade)
        {
            z.hp -= damage;
            world.Emit(EventKind.Hit, z.pos, by == null ? -1 : by.id, blade ? "blade" : "blunt", z.id);
            if (z.hp <= 0) { KillZombie(z, by); return; }
            z.stagger = .6f; z.attackClock = 0;
            if (by != null) { z.state = z.state == ZombieState.Knocked ? ZombieState.Knocked : ZombieState.Chase; z.targetSurvivor = by.id; z.target = by.pos; z.lostSight = 0; }
            if (world.rng.Chance(knock)) Knock(z);
            else if (by != null) { var push = (z.pos - by.pos).Normalized * .35f; z.pos = world.map.Slide(z.pos, push, Zombie.Radius); world.index.Moved(z); }
        }
        void Knock(Zombie z) { z.state = ZombieState.Knocked; z.timer = world.rng.Range(1.8f, 3.6f); z.attackClock = 0; world.Emit(EventKind.Shove, z.pos, -1, "down", z.id); }

        void KillZombie(Zombie z, Survivor by)
        {
            z.state = ZombieState.Dead; z.hp = 0; world.index.Remove(z);
            world.corpses.Add(new Corpse { id = world.nextCorpse++, pos = z.pos, facing = z.facing, variant = z.variant, female = z.female, time = world.minutes });
            world.Emit(EventKind.Kill, z.pos, by == null ? -1 : by.id, "", z.id);
            if (by != null) { by.kills++; GainXp(by, Skill.Melee, 4); }
        }

        /// <summary>A zombie's attack resolves against a survivor: chance to hit, wound type, protection and infection.</summary>
        void ZombieStrike(Zombie z, Survivor s)
        {
            if (!s.alive) return;
            var to = s.pos - z.pos;
            bool fromBehind = Math.Abs(MathUtil.WrapAngle(to.Angle - s.facing)) < .9f;   // zombie direction matches the survivor's facing
            float chance = .5f + (fromBehind ? .25f : 0) + (s.knockedDown > 0 ? .3f : 0) - (s.swingClock > 0 && !fromBehind ? .15f : 0) - s.Level(Skill.Fitness) * .02f;
            // Being surrounded is deadly.
            world.index.Query(s.pos, 1.3f, query);
            int attackers = query.Count(q => q.state == ZombieState.Attack || q.state == ZombieState.Lunge);
            if (attackers >= 3 && s.health < 45 && world.rng.Chance(.35f)) { s.wounds.Add(new Wound { part = BodyPart.Torso, kind = WoundKind.Bite, bleeding = true }); s.zombieInfected |= world.settings.infection; Kill(s, "Dragged down by the horde."); return; }
            if (!world.rng.Chance(chance)) { world.Emit(EventKind.Swing, z.pos, -1, "miss", z.id); return; }
            float roll = world.rng.Float();
            var kind = roll < .14f ? WoundKind.Bite : roll < .42f ? WoundKind.Laceration : WoundKind.Scratch;
            var part = (BodyPart)world.rng.Range(0, 7);
            float protection = s.Protection(kind == WoundKind.Bite);
            if (world.rng.Chance(protection))
            {
                world.Emit(EventKind.Hurt, s.pos, s.id, "blocked");
                world.Message(s, "Your clothing stopped a " + (kind == WoundKind.Bite ? "bite" : "scratch") + ".");
                foreach (var kv in s.worn) { var c = s.inventory.ByUid(kv.Value); if (c != null) c.condition = Math.Max(.1f, c.condition - .05f); }
                return;
            }
            var wound = new Wound { part = part, kind = kind, severity = kind == WoundKind.Bite ? 1.4f : kind == WoundKind.Laceration ? 1.1f : .7f, bleeding = kind != WoundKind.Scratch || world.rng.Chance(.5f) };
            s.wounds.Add(wound);
            s.health -= kind == WoundKind.Bite ? world.rng.Range(12, 20) : kind == WoundKind.Laceration ? world.rng.Range(7, 12) : world.rng.Range(3, 7);
            s.hurtFlash = .5f; s.panic = Math.Min(100, s.panic + 18);
            CancelAction(s, "");
            if (world.settings.infection && world.rng.Chance(kind == WoundKind.Bite ? 1 : kind == WoundKind.Laceration ? .25f : .07f)) s.zombieInfected = true;
            world.Emit(kind == WoundKind.Bite ? EventKind.Bite : EventKind.Hurt, s.pos, s.id, Wound.Part(part) + ": " + (kind == WoundKind.Bite ? "BITTEN" : kind.ToString().ToLowerInvariant()));
            if (kind == WoundKind.Bite) world.Message(s, "You've been bitten on the " + Wound.Part(part).ToLowerInvariant() + ".");
            if (world.rng.Chance(.06f + (attackers >= 2 ? .12f : 0))) { s.knockedDown = 1.4f; world.Message(s, "You're knocked to the ground!"); }
            if (s.health <= 0) Kill(s, "Killed by the dead.");
        }
    }
}
