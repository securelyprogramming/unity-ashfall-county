using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>The zombie side of combat: damage taken, knockdowns, deaths and zombie strikes on survivors (Bestiary owner).</summary>
    public sealed partial class Simulation
    {
        /// <summary>Damage to a zombie from a weapon, car or blast. Riot armour turns frontal blows, ferals dodge swings,
        /// juggernauts do not stagger, and another survivor's hit breaks a juggernaut's grab.</summary>
        internal void HitZombie(Zombie z, Survivor by, float damage, float knock, bool blade)
        {
            if (!z.Alive) return;
            bool car = by != null && by.vehicle >= 0;
            bool melee = by != null && !blasting && !car && Vec2.Distance(by.pos, z.pos) < 2.6f;
            if (z.kind == ZombieKind.Feral && melee && z.state != ZombieState.Knocked && z.act != FreakAct.Grab && world.rng.Chance(.25f)) { Dodge(z, by); return; }
            bool armor = z.kind == ZombieKind.Armored && z.state != ZombieState.Knocked && by != null && !blasting && !car
                && Math.Abs(MathUtil.WrapAngle((by.pos - z.pos).Angle - z.facing)) < 1.25f;
            if (armor) damage *= Bestiary.ArmorFrontFactor;
            z.hp -= damage;
            world.Emit(EventKind.Hit, z.pos, by == null ? -1 : by.id, armor ? "armor" : blade ? "blade" : "blunt", z.id);
            if (z.act == FreakAct.Grab && by != null && by.id != z.grabbed) BreakGrab(z, by);
            if (z.hp <= 0) { KillZombie(z, by); return; }
            if (z.kind != ZombieKind.Juggernaut)
            {
                z.stagger = z.kind == ZombieKind.Armored ? .3f : .6f; z.attackClock = 0;
                if (z.act == FreakAct.Pounce) EndAct(z);
            }
            if (by != null && z.act == FreakAct.None) { z.state = z.state == ZombieState.Knocked ? ZombieState.Knocked : ZombieState.Chase; z.targetSurvivor = by.id; z.target = by.pos; z.lostSight = 0; }
            hitting = true;
            bool down = world.rng.Chance(knock) && Knock(z);
            hitting = false;
            if (!down && by != null && z.kind != ZombieKind.Juggernaut) { var push = (z.pos - by.pos).Normalized * .35f; z.pos = world.map.Slide(z.pos, push, Zombie.Radius); world.index.Moved(z); }
            if (z.kind == ZombieKind.Feral && by != null) MaybeFlee(z, by);
        }

        /// <summary>Knocks a zombie down. Armored ones usually keep their feet (shoves rarely work); juggernauts fall only to blasts
        /// (car hits set the state directly in Vehicles.cs). Returns true when it went down.</summary>
        bool Knock(Zombie z)
        {
            if (!blasting)
            {
                if (z.kind == ZombieKind.Juggernaut) return false;
                if (z.kind == ZombieKind.Armored && world.rng.Chance(hitting ? .45f : .8f)) return false;
            }
            EndAct(z);
            z.state = ZombieState.Knocked; z.timer = world.rng.Range(1.8f, 3.6f); z.attackClock = 0; world.Emit(EventKind.Shove, z.pos, -1, "down", z.id);
            return true;
        }

        /// <summary>True for zombies a shove cannot move (juggernauts). SimCombat may skip the push for them.</summary>
        public static bool ShoveImmune(Zombie z) { return z.kind == ZombieKind.Juggernaut; }

        internal void KillZombie(Zombie z, Survivor by)
        {
            if (!z.Alive) return;
            EndAct(z);
            z.state = ZombieState.Dead; z.hp = 0; z.burning = 0; world.index.Remove(z);
            world.corpses.Add(new Corpse { id = world.nextCorpse++, pos = z.pos, facing = z.facing, variant = z.variant, female = z.female, time = world.minutes });
            world.Emit(EventKind.Kill, z.pos, by == null ? -1 : by.id, "", z.id);
            if (by != null) { by.kills++; GainXp(by, Skill.Melee, 4); }
            if (z.kind == ZombieKind.Bloater) Burst(z, by);
            var def = Bestiary.Get(z.kind);
            if (def.threat >= 2)
            {
                world.Emit(EventKind.KillFeed, z.pos, by == null ? -1 : by.id, KillerName(by) + " killed " + Bestiary.Article(def.name) + def.name, z.id);
                AwardInfluence(by, def.influence, z.pos);
            }
        }

        /// <summary>A zombie's attack resolves against a survivor: chance to hit, wound type, protection and blood plague.
        /// Damage scales with the kind. Bites wound; they no longer turn the survivor.</summary>
        void ZombieStrike(Zombie z, Survivor s)
        {
            if (!s.alive) return;
            var to = s.pos - z.pos;
            float scale = Bestiary.Get(z.kind).damageScale;
            bool fromBehind = Math.Abs(MathUtil.WrapAngle(to.Angle - s.facing)) < .9f;   // zombie direction matches the survivor's facing
            float chance = .5f + (fromBehind ? .25f : 0) + (s.knockedDown > 0 ? .3f : 0) - (s.swingClock > 0 && !fromBehind ? .15f : 0) - s.Level(Skill.Fitness) * .02f;
            // Being surrounded is deadly.
            world.index.Query(s.pos, 1.3f, query);
            int attackers = query.Count(q => q.state == ZombieState.Attack || q.state == ZombieState.Lunge);
            if (attackers >= 3 && s.health < 45 && world.rng.Chance(.35f)) { s.wounds.Add(new Wound { part = BodyPart.Torso, kind = WoundKind.Bite, bleeding = true }); Plague(z, s, 1); Kill(s, "Dragged down by the horde."); return; }
            if (!world.rng.Chance(chance)) { world.Emit(EventKind.Swing, z.pos, -1, "miss", z.id); return; }
            float roll = world.rng.Float();
            var kind = roll < .14f ? WoundKind.Bite : roll < .42f ? WoundKind.Laceration : WoundKind.Scratch;
            var part = (BodyPart)world.rng.Range(0, 7);
            float protection = s.Protection(kind == WoundKind.Bite) / Math.Max(1, scale);   // heavy hitters tear through clothing
            if (world.rng.Chance(protection))
            {
                world.Emit(EventKind.Hurt, s.pos, s.id, "blocked");
                world.Message(s, "Your clothing stopped a " + (kind == WoundKind.Bite ? "bite" : "scratch") + ".");
                foreach (var kv in s.worn) { var c = s.inventory.ByUid(kv.Value); if (c != null) c.condition = Math.Max(.1f, c.condition - .05f); }
                return;
            }
            var wound = new Wound { part = part, kind = kind, severity = kind == WoundKind.Bite ? 1.4f : kind == WoundKind.Laceration ? 1.1f : .7f, bleeding = kind != WoundKind.Scratch || world.rng.Chance(.5f) };
            s.wounds.Add(wound);
            s.health -= (kind == WoundKind.Bite ? world.rng.Range(12, 20) : kind == WoundKind.Laceration ? world.rng.Range(7, 12) : world.rng.Range(3, 7)) * scale;
            s.hurtFlash = .5f; s.panic = Math.Min(100, s.panic + 18);
            CancelAction(s, "");
            Plague(z, s, kind == WoundKind.Bite ? 1 : kind == WoundKind.Laceration ? .6f : .3f);
            world.Emit(kind == WoundKind.Bite ? EventKind.Bite : EventKind.Hurt, s.pos, s.id, Wound.Part(part) + ": " + (kind == WoundKind.Bite ? "BITTEN" : kind.ToString().ToLowerInvariant()));
            if (kind == WoundKind.Bite) world.Message(s, "You've been bitten on the " + Wound.Part(part).ToLowerInvariant() + ".");
            if (world.rng.Chance(.06f + (attackers >= 2 ? .12f : 0) + (scale > 1 ? .15f : 0))) { s.knockedDown = 1.4f; world.Message(s, "You're knocked to the ground!"); }
            if (s.health <= 0) Kill(s, z.kind == ZombieKind.Walker || z.kind == ZombieKind.Crawler ? "Killed by the dead." : "Killed by " + Bestiary.Article(Bestiary.Get(z.kind).name).Trim() + " " + Bestiary.Get(z.kind).name.ToLowerInvariant() + ".");
        }
    }
}
