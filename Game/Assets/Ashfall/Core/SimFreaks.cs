using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    /// <summary>Freak behaviour: screamer screams, feral pounces and flights, juggernaut charges and grabs, bloater bursts and burning.</summary>
    public sealed partial class Simulation
    {
        /// <summary>Cooldowns tick for every freak; juggernauts shrug off staggers (their stuns are Recover moves).</summary>
        void FreakClocks(Zombie z, float dt)
        {
            z.ability -= dt; z.grabCooldown -= dt;
            if (z.kind == ZombieKind.Juggernaut) z.stagger = 0;
        }

        /// <summary>Chase-state decisions for freaks. Returns true when the freak handled its own movement this tick.</summary>
        bool FreakChase(Zombie z, Survivor prey, float d, float dt)
        {
            switch (z.kind)
            {
                case ZombieKind.Screamer: return ScreamerChase(z, prey, d, dt);
                case ZombieKind.Feral:
                    if (d < Bestiary.PounceMin || d > Bestiary.PounceMax || z.ability > 0 || prey.vehicle >= 0 || !world.map.LineOfSight(z.pos, prey.pos) || PathCrossesBlock(z.pos, prey.pos)) return false;
                    z.act = FreakAct.Pounce; z.actDir = (prey.pos - z.pos).Normalized; z.actTimer = d / Bestiary.PounceSpeed + .25f;
                    z.state = ZombieState.Lunge; z.facing = z.actDir.Angle; z.ability = world.rng.Range(5f, 8f);
                    world.Emit(EventKind.Pounce, z.pos, prey.id, "leap", z.id);
                    return true;
                case ZombieKind.Juggernaut:
                    if (d < Bestiary.ChargeMin || d > Bestiary.ChargeMax || z.ability > 0 || prey.vehicle >= 0 || !world.map.LineOfSight(z.pos, prey.pos) || PathCrossesBlock(z.pos, prey.pos)) return false;
                    z.act = FreakAct.Charge; z.actDir = (prey.pos - z.pos).Normalized; z.actTimer = (d + 3) / Bestiary.ChargeSpeed;
                    z.state = ZombieState.Lunge; z.facing = z.actDir.Angle; z.ability = world.rng.Range(9f, 14f);
                    world.Emit(EventKind.Charge, z.pos, prey.id, "start", z.id); world.MakeNoise(z.pos, 25);
                    return true;
            }
            return false;
        }

        /// <summary>Screamers keep their distance and scream when they see a survivor; only a cornered one fights.</summary>
        bool ScreamerChase(Zombie z, Survivor prey, float d, float dt)
        {
            bool sight = world.map.LineOfSight(z.pos, prey.pos);
            if (z.ability <= 0 && d < 30 && sight) Scream(z);
            if (d < .9f) return false;
            if (d < 9)
            {
                var away = (z.pos - prey.pos).Normalized;
                z.facing = away.Angle;
                z.pos = world.map.Slide(z.pos, away * (z.speed * 1.05f * dt), Zombie.Radius); world.index.Moved(z);
            }
            else z.facing = (prey.pos - z.pos).Angle;
            return true;
        }

        void Scream(Zombie z)
        {
            z.ability = world.rng.Range(14f, 20f);
            world.Emit(EventKind.Scream, z.pos, -1, "scream", z.id);
            world.MakeNoise(z.pos, Bestiary.ScreamNoise);
            foreach (var s in world.survivors)
            {
                if (!s.alive || s.offscreen) continue;
                float d = Vec2.Distance(s.pos, z.pos);
                if (d > Bestiary.ScreamPanicRadius) continue;
                s.panic = Math.Min(100, s.panic + 35);
                if (d <= Bestiary.ScreamStunRadius && s.vehicle < 0) { s.knockedDown = Math.Max(s.knockedDown, .8f); CancelAction(s, ""); world.Message(s, "The scream drives you to your knees!"); }
            }
        }

        /// <summary>A special move in progress (pounce, charge, grab, flight, recovery).</summary>
        void UpdateAct(Zombie z, float dt)
        {
            z.actTimer -= dt;
            var def = Bestiary.Get(z.kind);
            switch (z.act)
            {
                case FreakAct.Pounce:
                    {
                        z.state = ZombieState.Lunge;
                        var prey = Survivor(z.targetSurvivor);
                        float moved = Leap(z, Bestiary.PounceSpeed, dt);
                        if (prey != null && prey.alive && prey.vehicle < 0 && Vec2.Distance(z.pos, prey.pos) < .85f) { PounceHit(z, prey, def); break; }
                        if (z.actTimer <= 0 || moved < Bestiary.PounceSpeed * dt * .3f) { EndAct(z); z.state = ZombieState.Chase; z.stagger = .5f; }
                        break;
                    }
                case FreakAct.Charge:
                    {
                        z.state = ZombieState.Lunge;
                        float moved = Leap(z, Bestiary.ChargeSpeed, dt);
                        Footsteps(z, moved, dt, .3f);
                        bool hitTarget = Trample(z, def);
                        if (moved < Bestiary.ChargeSpeed * dt * .3f) { z.act = FreakAct.Recover; z.actTimer = 1.5f; world.MakeNoise(z.pos, 15); world.Emit(EventKind.Thump, z.pos, -1, "charge", z.id); }
                        else if (hitTarget || z.actTimer <= 0) { z.act = FreakAct.Recover; z.actTimer = .6f; }
                        break;
                    }
                case FreakAct.Grab:
                    {
                        z.state = ZombieState.Attack;
                        var prey = Survivor(z.grabbed);
                        if (prey == null || !prey.alive) { EndAct(z); z.state = ZombieState.Idle; z.timer = 3; break; }
                        prey.knockedDown = Math.Max(prey.knockedDown, .3f);
                        z.facing = (prey.pos - z.pos).Angle;
                        if (z.actTimer <= 0) FinishGrab(z, prey, def);
                        break;
                    }
                case FreakAct.Flee:
                    {
                        z.state = ZombieState.Chase; z.facing = z.actDir.Angle;
                        var before = z.pos;
                        z.pos = world.map.Slide(z.pos, z.actDir * (z.speed * dt), Zombie.Radius); world.index.Moved(z);
                        if (Vec2.Distance(before, z.pos) < z.speed * dt * .3f) z.actDir = Vec2.FromAngle(z.actDir.Angle + (world.rng.Chance(.5f) ? .9f : -.9f));
                        if (z.actTimer <= 0) { EndAct(z); z.state = z.targetSurvivor >= 0 ? ZombieState.Chase : ZombieState.Investigate; z.timer = 15; }
                        break;
                    }
                default:
                    if (z.actTimer <= 0) { EndAct(z); z.state = z.targetSurvivor >= 0 ? ZombieState.Chase : ZombieState.Idle; z.timer = 3; z.path.Clear(); z.repath = 0; }
                    break;
            }
        }

        /// <summary>Straight-line burst along actDir; returns the distance actually covered.</summary>
        float Leap(Zombie z, float speed, float dt)
        {
            var before = z.pos;
            z.facing = z.actDir.Angle;
            z.pos = world.map.Slide(z.pos, z.actDir * (speed * dt), Zombie.Radius); world.index.Moved(z);
            return Vec2.Distance(before, z.pos);
        }

        void PounceHit(Zombie z, Survivor prey, ZombieKindDef def)
        {
            EndAct(z);
            prey.knockedDown = Math.Max(prey.knockedDown, 1.6f); CancelAction(prey, "");
            prey.wounds.Add(new Wound { part = (BodyPart)world.rng.Range(0, 7), kind = WoundKind.Laceration, severity = 1.1f, bleeding = true });
            prey.panic = Math.Min(100, prey.panic + 30);
            world.Emit(EventKind.Pounce, prey.pos, prey.id, "hit", z.id);
            world.Message(prey, "A feral slams you to the ground!");
            Plague(z, prey, .6f);
            DamageSurvivor(prey, null, 12 * def.damageScale, "Mauled by a feral.");
            z.state = ZombieState.Attack; z.attackClock = .5f; z.facing = (prey.pos - z.pos).Angle;
        }

        /// <summary>A charging juggernaut bowls over survivors and zombies in its path. True when its target went down.</summary>
        bool Trample(Zombie z, ZombieKindDef def)
        {
            bool target = false;
            foreach (var s in world.survivors)
            {
                if (!s.alive || s.offscreen || s.vehicle >= 0 || s.knockedDown > .6f || Vec2.Distance(s.pos, z.pos) > .95f) continue;
                var side = new Vec2(-z.actDir.y, z.actDir.x); if (Vec2.Dot(s.pos - z.pos, side) < 0) side = side * -1;
                s.pos = world.map.Slide(s.pos, (side + z.actDir) * .8f, Ashfall.Core.Survivor.Radius);
                s.knockedDown = 1.5f; CancelAction(s, "");
                world.Emit(EventKind.Charge, s.pos, s.id, "hit", z.id);
                world.Message(s, "The juggernaut bowls you over!");
                DamageSurvivor(s, null, 6 * def.damageScale, "Trampled by a juggernaut.");
                if (s.id == z.targetSurvivor) target = true;
            }
            world.index.Query(z.pos, 1.1f, freakQuery);
            foreach (var o in freakQuery)
            {
                if (o == z || o.state == ZombieState.Knocked || o.kind == ZombieKind.Juggernaut) continue;
                var side = new Vec2(-z.actDir.y, z.actDir.x); if (Vec2.Dot(o.pos - z.pos, side) < 0) side = side * -1;
                o.pos = world.map.Slide(o.pos, side * .8f, Zombie.Radius); world.index.Moved(o);
                EndAct(o); o.state = ZombieState.Knocked; o.timer = world.rng.Range(1.5f, 3f); o.attackClock = 0;
                world.Emit(EventKind.Shove, o.pos, -1, "down", o.id);
            }
            return target;
        }

        /// <summary>A juggernaut next to its prey seizes it instead of striking (grab cooldown permitting).</summary>
        bool TryGrab(Zombie z, Survivor prey)
        {
            if (z.grabCooldown > 0 || prey.vehicle >= 0 || PreyDistance(z, prey) > 1.25f || !world.map.LineOfSight(z.pos, prey.pos)) return false;
            z.act = FreakAct.Grab; z.grabbed = prey.id; z.actTimer = Bestiary.GrabSeconds; z.state = ZombieState.Attack;
            prey.knockedDown = Math.Max(prey.knockedDown, Bestiary.GrabSeconds + .2f); CancelAction(prey, "");
            world.Emit(EventKind.Grab, prey.pos, prey.id, "start", z.id);
            world.Message(prey, "The juggernaut seizes you!");
            return true;
        }

        void FinishGrab(Zombie z, Survivor prey, ZombieKindDef def)
        {
            z.act = FreakAct.None; z.grabbed = -1; z.grabCooldown = world.rng.Range(6f, 9f); z.state = ZombieState.Attack; z.attackClock = 1.2f;
            if (prey.health < prey.MaxHealth * Bestiary.GrabKillFraction)
            {
                world.Emit(EventKind.Grab, prey.pos, prey.id, "kill", z.id);
                Kill(prey, "Torn apart by a juggernaut.");
                return;
            }
            world.Emit(EventKind.Grab, prey.pos, prey.id, "slam", z.id);
            prey.knockedDown = 1.4f;
            prey.pos = world.map.Slide(prey.pos, (prey.pos - z.pos).Normalized * 1.6f, Ashfall.Core.Survivor.Radius);
            Plague(z, prey, .5f);
            DamageSurvivor(prey, null, 10 * def.damageScale, "Crushed by a juggernaut.");
        }

        /// <summary>Another survivor's hit breaks a juggernaut's grab and staggers it.</summary>
        void BreakGrab(Zombie z, Survivor by)
        {
            var prey = Survivor(z.grabbed);
            z.grabbed = -1; z.act = FreakAct.Recover; z.actTimer = 1; z.grabCooldown = world.rng.Range(5f, 8f);
            if (prey != null) prey.knockedDown = Math.Min(prey.knockedDown, .4f);
            world.Emit(EventKind.Grab, z.pos, by.id, "break", z.id);
        }

        /// <summary>Ends any special move, releasing a held survivor.</summary>
        void EndAct(Zombie z)
        {
            if (z.act == FreakAct.Grab && z.grabbed >= 0) { var prey = Survivor(z.grabbed); if (prey != null) prey.knockedDown = Math.Min(prey.knockedDown, .4f); }
            z.grabbed = -1; z.act = FreakAct.None; z.actTimer = 0;
        }

        /// <summary>A hurt feral breaks off for a moment, then comes back.</summary>
        void MaybeFlee(Zombie z, Survivor from)
        {
            if (z.act != FreakAct.None || z.state == ZombieState.Knocked) return;
            if (z.fled ? !world.rng.Chance(.15f) : z.hp > z.maxHp * .6f) return;
            z.fled = true; z.act = FreakAct.Flee; z.actTimer = world.rng.Range(2f, 3.5f); z.stagger = 0;
            z.actDir = (z.pos - from.pos).Normalized; if (z.actDir.SqrLength < .5f) z.actDir = Vec2.FromAngle(world.rng.Range(0, 6.28f));
            world.Emit(EventKind.Groan, z.pos, -1, "flee", z.id);
        }

        /// <summary>Ferals sidestep some melee swings.</summary>
        void Dodge(Zombie z, Survivor by)
        {
            var side = new Vec2(-(z.pos - by.pos).y, (z.pos - by.pos).x).Normalized * (world.rng.Chance(.5f) ? .8f : -.8f);
            z.pos = world.map.Slide(z.pos, side, Zombie.Radius); world.index.Moved(z);
            world.Emit(EventKind.Swing, z.pos, by.id, "dodge", z.id);
        }

        /// <summary>Heavy footsteps: juggernauts are heard before they are seen.</summary>
        void Footsteps(Zombie z, float moved, float dt, float interval)
        {
            if (moved < .01f) return;
            z.stepClock -= dt;
            if (z.stepClock > 0) return;
            z.stepClock = interval;
            world.MakeNoise(z.pos, 8);
            world.Emit(EventKind.Footstep, z.pos, -1, "juggernaut", z.id);
        }

        /// <summary>Burning zombies (molotov fire) lose health until the flames die.</summary>
        void Burn(Zombie z, float dt)
        {
            z.burning -= dt;
            z.hp -= Bestiary.BurnDamagePerSecond * dt;
            if (z.hp > 0) return;
            killCause = "Fire";
            try { KillZombie(z, Survivor(z.burnBy)); } finally { killCause = ""; }
        }

        /// <summary>A bloater bursts into a gas cloud. Killed in melee, it bursts on the attacker.</summary>
        void Burst(Zombie z, Survivor by)
        {
            bool melee = by != null && by.vehicle < 0 && !blasting && Vec2.Distance(by.pos, z.pos) < 2.6f;
            var at = melee ? z.pos + (by.pos - z.pos) * .5f : z.pos;
            world.Emit(EventKind.Gas, at, by == null ? -1 : by.id, "burst", z.id);
            world.MakeNoise(at, 12);
            world.hazards.Add(new Hazard { pos = at, radius = Bestiary.GasRadius, until = (float)world.minutes + Bestiary.GasMinutes, fire = false });
            foreach (var s in world.survivors)
            {
                if (!s.alive || s.offscreen || s.vehicle >= 0 || Vec2.Distance(s.pos, z.pos) > 2.6f) continue;
                s.panic = Math.Min(100, s.panic + 25);
                world.Message(s, "The bloater bursts all over you!");
                DamageSurvivor(s, null, 8, "Choked on bloater gas.");
            }
        }

        /// <summary>Plague zombies pass on blood plague (Community.cs makes it rise and kill unless cured).</summary>
        void Plague(Zombie z, Survivor s, float chance)
        {
            if (!z.plague || !world.settings.infection || !s.alive || !world.rng.Chance(chance)) return;
            s.plague = Math.Min(100, s.plague + 8);
            if (s.plagueInfected) return;
            s.plagueInfected = true;
            world.Emit(EventKind.Plague, s.pos, s.id, "infected", z.id);
            world.Message(s, "Your blood burns. You've caught the blood plague.");
        }
    }
}
