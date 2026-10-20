using System;
using System.Linq;

namespace Ashfall.Core
{
    public sealed partial class Simulation
    {
        void UpdateSurvivor(Survivor s, float dt, float hours)
        {
            InputState input; inputs.TryGetValue(s.id, out input);
            s.attackCooldown -= dt; s.shoveCooldown -= dt; s.hurtFlash = Math.Max(0, s.hurtFlash - dt); s.swingClock = Math.Max(0, s.swingClock - dt);
            if (s.knockedDown > 0) { s.knockedDown -= dt; s.speed = 0; input = new InputState { face = s.facing }; }
            if (s.sleeping) { UpdateSleep(s, hours); Needs(s, hours, dt, true); Health(s, hours); return; }
            if (s.vehicle >= 0) { RideAlong(s, dt, hours); return; }

            // Movement: clamp client input; the host decides speed from stance, endurance and load.
            var move = new Vec2(input.moveX, input.moveY); if (move.Length > 1) move = move.Normalized;
            bool moving = move.Length > .1f;
            if (s.action != null && moving && !s.action.movable) CancelAction(s, "");
            s.aiming = input.aim && s.knockedDown <= 0;
            s.sneaking = input.sneak && !input.run;
            s.running = input.run && moving && s.endurance > 5 && !s.aiming;
            if (!float.IsNaN(input.face) && !float.IsInfinity(input.face)) s.facing = MathUtil.WrapAngle(input.face);
            float speed = s.running ? 4.1f * (s.HasTrait("athletic") ? 1.08f : 1) : s.sneaking ? 1.25f : s.aiming ? 1.3f : 2.15f;
            float load = s.Encumbrance; if (load > 1) speed *= load > 1.5f ? .55f : .78f;
            if (s.fatigue > 80) speed *= .85f;
            if (s.endurance < 15) speed *= .8f;
            if (s.wounds.Any(w => (w.part == BodyPart.LeftLeg || w.part == BodyPart.RightLeg) && w.severity > .5f && !w.bandaged)) speed *= .8f;
            if (s.action != null && s.action.movable) speed *= .6f;
            if (moving && s.knockedDown <= 0)
            {
                var before = s.pos;
                s.pos = world.map.Slide(s.pos, move * (speed * dt), Ashfall.Core.Survivor.Radius);
                s.speed = Vec2.Distance(before, s.pos) / dt;
                s.footstepClock -= dt;
                if (s.footstepClock <= 0)
                {
                    s.footstepClock = s.running ? .32f : .55f;
                    float radius = s.running ? 11 : s.sneaking ? 1.2f * (1 - s.Level(Skill.Sneaking) * .06f) : 3.5f;
                    if (s.HasTrait("graceful")) radius *= .7f; else if (s.HasTrait("clumsy")) radius *= 1.3f;
                    world.MakeNoise(s.pos, radius, s.id);
                    if (s.sneaking) GainXp(s, Skill.Sneaking, .4f);
                    if (s.running) GainXp(s, Skill.Fitness, .25f);
                    world.Emit(EventKind.Footstep, s.pos, s.id, s.running ? "run" : s.sneaking ? "sneak" : "walk");
                }
            }
            else s.speed = 0;
            // Endurance: running and fighting drain, resting recovers.
            if (s.running) s.endurance -= dt * (4.5f - s.Level(Skill.Fitness) * .25f) * (load > 1 ? 1.6f : 1) * (s.HasTrait("athletic") ? .75f : s.HasTrait("out_of_shape") ? 1.35f : 1);
            else if (s.swingClock <= 0) s.endurance += dt * (moving ? 2.5f : 5f) * (s.fatigue > 70 ? .5f : 1);
            s.endurance = MathUtil.Clamp(s.endurance, 0, 100);

            if (s.knockedDown <= 0)
            {
                if (input.shove && s.shoveCooldown <= 0) Shove(s);
                else if (input.attack && s.attackCooldown <= 0 && (s.action == null || s.action.movable)) Attack(s);
            }
            if (s.action != null) ProgressAction(s, dt);
            Needs(s, hours, dt, false);
            Health(s, hours);
            // Flashlights drain batteries.
            var light = s.Secondary;
            if (s.lightOn && light != null && light.Def.category == ItemCategory.Light)
            {
                light.condition -= hours / Math.Max(1, light.Def.batteryHours);
                if (light.condition <= 0) { light.condition = 0; s.lightOn = false; world.Message(s, "The flashlight battery is dead."); }
            }
            else if (s.lightOn && (light == null || light.Def.category != ItemCategory.Light)) s.lightOn = false;
            // Leaving an open container's reach closes it.
            if (s.lootContainer >= 0)
            {
                var c = world.map.containers[s.lootContainer];
                if (c.prop < 0 || !ReachProp(s, world.map.props[c.prop])) s.lootContainer = -1;
            }
        }

        void Needs(Survivor s, float hours, float dt, bool asleep)
        {
            float activity = s.running ? 1.6f : 1;
            float appetite = s.HasTrait("light_eater") ? .75f : s.HasTrait("hearty_appetite") ? 1.3f : 1;
            s.hunger = MathUtil.Clamp(s.hunger + hours * 4.0f * appetite * (asleep ? .55f : activity), 0, 100);
            s.thirst = MathUtil.Clamp(s.thirst + hours * 5.2f * (asleep ? .5f : activity), 0, 100);
            if (!asleep) s.fatigue = MathUtil.Clamp(s.fatigue + hours * (4.2f + (s.running ? 3 : 0)), 0, 100);
            s.boredom = MathUtil.Clamp(s.boredom + hours * (world.map.Indoors(s.pos) ? 2.2f : .4f), 0, 100);
            s.sickness = MathUtil.Clamp(s.sickness - hours * 3.5f, 0, 100);
            bool outside = !world.map.Indoors(s.pos);
            s.wetness = MathUtil.Clamp(s.wetness + hours * (world.raining && outside ? 45 * world.rainIntensity : -25), 0, 100);
            if (s.wetness > 70 && world.Daylight < .4f) s.sickness = Math.Min(100, s.sickness + hours * 2);
            s.panic = MathUtil.Clamp(s.panic - dt * 1.2f, 0, 100);
            // Pain follows wounds, eased by painkillers (tracked as negative stress).
            float pain = 0; foreach (var w in s.wounds) pain += w.severity * (w.kind == WoundKind.Bite ? 30 : w.kind == WoundKind.Laceration ? 20 : w.kind == WoundKind.GlassCut ? 18 : 8) * (w.infected ? 1.6f : 1);
            s.stress = Math.Max(0, s.stress - hours);
            s.pain = MathUtil.Clamp(pain - s.stress * 1.5f, 0, 100);
            if (s.hunger < 25 && s.thirst < 25) s.wellFedHours += hours; else s.wellFedHours = 0;
            SmokerNeeds(s, hours);
        }

        void Health(Survivor s, float hours)
        {
            float damage = 0;
            for (int i = s.wounds.Count - 1; i >= 0; i--)
            {
                var w = s.wounds[i];
                if (w.bleeding) damage += hours * (w.kind == WoundKind.Laceration || w.kind == WoundKind.GlassCut ? 14 : w.kind == WoundKind.Bite ? 16 : 6);
                float heal = hours * .035f * (w.bandaged ? 1.6f : .6f) * (w.disinfected ? 1.3f : 1) * (w.sutured ? 1.5f : 1) * (s.hunger > 70 || s.thirst > 70 ? .4f : 1);
                if (w.infected) { heal = 0; damage += hours * 1.6f; }
                w.severity -= heal;
                if (w.bandaged) { w.bandageAge += hours; if (w.bandageAge > 14 && !w.dirtyBandage) { w.dirtyBandage = true; world.Message(s, "A bandage is soaked through and dirty."); } }
                if (w.bleeding && w.bandaged && !w.dirtyBandage) w.bleeding = false;
                if (w.bleeding && w.kind == WoundKind.Scratch && world.rng.Chance(hours * .8f)) w.bleeding = false;
                if (!w.infected && !w.disinfected && (w.dirtyBandage || !w.bandaged && w.kind != WoundKind.Scratch) && world.rng.Chance(hours * .02f)) { w.infected = true; world.Message(s, Wound.Part(w.part) + " wound looks red and swollen. It's infected."); }
                if (w.severity <= 0) { s.wounds.RemoveAt(i); world.Message(s, Wound.Part(w.part) + " has healed."); }
            }
            if (s.hunger > 90) damage += hours * 3;
            if (s.thirst > 90) damage += hours * 6;
            if (s.sickness > 60) damage += hours * 4 * (s.sickness / 100);
            if (s.zombieInfected && world.settings.infection)
            {
                s.infectionHours += hours;
                if (s.infectionHours > 10) s.sickness = Math.Max(s.sickness, 40 + (float)s.infectionHours);
                if (s.infectionHours > 34) damage += hours * 7;
            }
            if (damage <= 0 && s.hunger < 70 && s.thirst < 70 && !s.Bleeding) s.health = Math.Min(100, s.health + hours * (s.sleeping ? 5 : 1.6f) * (s.wellFedHours > 2 ? 1.4f : 1));
            s.health -= damage;
            if (s.health <= 0) Kill(s, s.zombieInfected ? "The infection took them." : s.Bleeding ? "Bled out." : s.thirst > 90 ? "Died of thirst." : s.hunger > 90 ? "Starved." : s.sickness > 60 ? "Died of sickness." : "Succumbed to their wounds.");
        }

        public void Kill(Survivor s, string cause)
        {
            if (!s.alive) return;
            ReleaseVehicle(s);
            s.alive = false; s.health = 0; s.diedAt = world.minutes; s.causeOfDeath = cause; s.action = null; s.sleeping = false;
            // The body drops everything it carried.
            var pile = world.PileAt((int)s.pos.x, (int)s.pos.y, true);
            foreach (var item in s.inventory.items) pile.inventory.Add(item);
            s.inventory.items.Clear(); s.primary = s.secondary = -1; s.worn.Clear();
            world.Emit(EventKind.Death, s.pos, s.id, cause);
            OnSurvivorKilled(s, cause);   // Humans.cs: kill feed, faction reactions
            CommunityOnDeath(s);          // Community.cs: morale, switching, "dead" outcome when the community is gone
            // An infected survivor rises again.
            if (s.zombieInfected) { var z = world.AddZombie(s.pos, world.rng); z.female = s.female; z.variant = 0; z.state = ZombieState.Idle; z.timer = 30; }
        }

        void UpdateSleep(Survivor s, float hours)
        {
            var bed = s.action != null && s.action.target >= 0 ? world.map.props[s.action.target] : null;
            float rate = bed == null ? 7 : bed.kind == PropKind.Bed ? 13 : 10;
            s.fatigue = Math.Max(0, s.fatigue - hours * rate);
            s.endurance = 100;
            world.index.Query(s.pos, s.HasTrait("light_sleeper") ? 14 : 7, query);
            bool danger = query.Any(z => z.state == ZombieState.Chase || z.state == ZombieState.Thump || z.state == ZombieState.Attack);
            if (danger) { Wake(s, "You jolt awake. Something is right outside!"); return; }
            if (s.fatigue <= 2 || s.hunger > 85 || s.thirst > 85 || s.Bleeding) Wake(s, s.fatigue <= 2 ? "You wake up rested." : "You wake up.");
        }
        void Wake(Survivor s, string why) { s.sleeping = false; s.action = null; world.Message(s, why); }

        void CancelAction(Survivor s, string why)
        {
            if (s.action == null) return;
            if (s.action.kind == ActionKind.Sleep) s.sleeping = false;
            s.action = null; if (why.Length > 0) world.Message(s, why);
        }

        void Begin(Survivor s, ActionKind kind, float seconds, int target = -1, int item = -1, string recipe = "", bool movable = false, int extra = -1)
        {
            if (s.action != null && s.action.kind == ActionKind.Sleep) return;
            s.action = new TimedAction { kind = kind, duration = Math.Max(.1f, seconds), target = target, item = item, recipe = recipe ?? "", movable = movable, extra = extra };
        }

        void ProgressAction(Survivor s, float dt)
        {
            var a = s.action; if (a == null) return;
            if (a.kind == ActionKind.Sleep) return;
            a.elapsed += dt * (s.panic > 60 ? .8f : 1);
            // Chopping, prying and foraging make noise while working.
            if ((a.kind == ActionKind.Chop || a.kind == ActionKind.Pry || a.kind == ActionKind.Barricade) && world.rng.Chance(dt * 1.2f)) world.MakeNoise(s.pos, a.kind == ActionKind.Barricade ? 14 : 9, s.id);
            if (a.elapsed < a.duration) return;
            s.action = null;
            Complete(s, a);
        }
    }
}
