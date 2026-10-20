using System;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>
    /// The survivor model, State of Decay style: stamina, fatigue, health with lasting injuries, bleeding wounds and the
    /// blood plague. Zomboid micro-needs (hunger, thirst, boredom, wetness, smoking, sickness) are frozen at harmless values.
    /// </summary>
    public sealed partial class Simulation
    {
        void UpdateSurvivor(Survivor s, float dt, float hours)
        {
            InputState input; inputs.TryGetValue(s.id, out input);
            s.attackCooldown -= dt; s.shoveCooldown -= dt; s.hurtFlash = Math.Max(0, s.hurtFlash - dt); s.swingClock = Math.Max(0, s.swingClock - dt);
            if (s.knockedDown > 0) { s.knockedDown -= dt; s.speed = 0; input = new InputState { face = s.facing }; }
            if (s.sleeping) { UpdateSleep(s, hours); Needs(s, hours, dt, true); Health(s, hours); return; }
            if (s.vehicle >= 0) { RideAlong(s, dt, hours); return; }

            // Movement: clamp client input; the host decides speed from stance, stamina, fatigue and load.
            var move = new Vec2(input.moveX, input.moveY); if (move.Length > 1) move = move.Normalized;
            bool moving = move.Length > .1f;
            if (s.action != null && moving && !s.action.movable) CancelAction(s, "");
            s.aiming = input.aim && s.knockedDown <= 0;
            s.sneaking = input.sneak && !input.run;
            s.running = input.run && moving && s.endurance > 5 && !s.aiming;
            if (!float.IsNaN(input.face) && !float.IsInfinity(input.face)) s.facing = MathUtil.WrapAngle(input.face);
            float speed = s.running ? 4.1f * (s.HasTrait("athletic") ? 1.08f : 1) : s.sneaking ? 1.25f : s.aiming ? 1.3f : 2.15f;
            float load = s.Encumbrance; if (load > 1) speed *= load > 1.5f ? .55f : .78f;
            if (s.fatigue > 90) speed *= .8f; else if (s.fatigue > 70) speed *= .88f;
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
            // Stamina: running and fighting drain it, resting recovers it up to a cap that fatigue and the plague lower.
            if (s.running) s.endurance -= dt * (4.5f - s.Level(Skill.Fitness) * .25f) * (load > 1 ? 1.6f : 1) * (s.HasTrait("athletic") ? .75f : s.HasTrait("out_of_shape") ? 1.35f : 1);
            else if (s.swingClock <= 0) s.endurance += dt * (moving ? 2.5f : 5f) * (s.fatigue > 70 ? .5f : 1) * Trait.StaminaRecovery(s);
            float cap = StaminaCap(s); if (s.endurance > cap) s.endurance = Math.Max(cap, s.endurance - dt * 2);   // a fresh drink tops it up for a while
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

        // ---------------------------------------------------------------- balance
        /// <summary>Health lost in one update that counts as a serious hit; that share of it becomes a lasting injury.</summary>
        public const float SeriousHit = 6, InjuryShare = .6f;
        /// <summary>In-game hours from blood plague infection to turning, before traits.</summary>
        public const float PlagueHours = 21;

        /// <summary>Most stamina a survivor can have: fatigue and the blood plague wear it down.</summary>
        public static float StaminaCap(Survivor s) { return Math.Max(25, 100 - Math.Max(0, s.fatigue - 50) * 1.2f - s.plague * .2f); }
        /// <summary>Melee and firearm damage scale from fatigue: a worn-out member hits softer (for SimCombat).</summary>
        public static float FatigueDamage(Survivor s) { return s.fatigue > 90 ? .75f : s.fatigue > 70 ? .87f : 1; }

        void Needs(Survivor s, float hours, float dt, bool asleep)
        {
            // Retired Zomboid needs stay at harmless values; the fields remain in phase 1 because other owners read them.
            s.hunger = 0; s.thirst = 0; s.boredom = 0; s.wetness = 0; s.sickness = 0; s.hoursSinceSmoke = 0; s.infectionHours = 0; s.wellFedHours = 0;
            // Fatigue rises with activity and time awake; residents idling at base take turns resting.
            if (!asleep)
            {
                float rise = 2.2f + (s.running ? 3.5f : 0) + (s.swingClock > 0 ? 3 : 0);
                if (s.ai == AiRole.Resident && s.speed < .2f && AtBase(s)) rise = -4;
                s.fatigue = MathUtil.Clamp(s.fatigue + hours * rise, 0, 100);
            }
            s.panic = MathUtil.Clamp(s.panic - dt * 1.2f, 0, 100);
            // Pain follows wounds, eased by painkillers (tracked as negative stress).
            float pain = 0; foreach (var w in s.wounds) pain += w.severity * (w.kind == WoundKind.Bite ? 30 : w.kind == WoundKind.Laceration ? 20 : w.kind == WoundKind.GlassCut ? 18 : 8);
            s.stress = Math.Max(0, s.stress - hours);
            s.pain = MathUtil.Clamp(pain - s.stress * 1.5f, 0, 100);
        }

        void Health(Survivor s, float hours)
        {
            // A sharp drop since the last update (a bite, a bullet, a blast) leaves a lasting injury.
            if (s.healthSeen >= 0 && s.healthSeen - s.health >= SeriousHit) s.injury = Math.Min(90, s.injury + (s.healthSeen - s.health) * InjuryShare);
            float damage = 0;
            for (int i = s.wounds.Count - 1; i >= 0; i--)
            {
                var w = s.wounds[i];
                // Wounds bleed until bandaged; they no longer get infected or need disinfecting.
                if (w.bleeding) damage += hours * (w.kind == WoundKind.Laceration || w.kind == WoundKind.GlassCut ? 14 : w.kind == WoundKind.Bite ? 16 : 6);
                w.infected = false; w.dirtyBandage = false;
                if (w.bleeding && w.bandaged) w.bleeding = false;
                if (w.bleeding && w.kind == WoundKind.Scratch && world.rng.Chance(hours * .8f)) w.bleeding = false;
                w.severity -= hours * .05f * (w.bandaged ? 1.6f : .8f) * (w.sutured ? 1.5f : 1) * Trait.Recovery(s);
                if (w.severity <= 0) { s.wounds.RemoveAt(i); world.Message(s, Wound.Part(w.part) + " has healed."); }
            }
            Recover(s, hours, s.sleeping || s.ai == AiRole.Resident && s.speed < .2f && AtBase(s));
            Plague(s, hours); if (!s.alive) return;
            s.health = Math.Min(s.health, s.MaxHealth) - damage;
            s.healthSeen = s.health;
            if (s.health <= 0) Kill(s, s.Bleeding ? "Bled out." : "Succumbed to their wounds.");
        }

        /// <summary>Health regenerates toward MaxHealth (not while bleeding); injuries heal slowly, faster resting at
        /// base and much faster with an Infirmary. High morale speeds recovery, low morale slows it.</summary>
        void Recover(Survivor s, float hours, bool resting)
        {
            var c = world.community; bool atBase = s.faction == 0 && AtBase(s);
            float rate = Trait.Recovery(s) * (c.morale >= 70 ? 1.25f : c.morale < 30 ? .8f : 1);
            int infirmary = atBase ? c.Level(FacilityKind.Infirmary) : 0;
            float heal = .4f * (atBase ? 2.5f : 1) * (resting ? 2 : 1) * (infirmary > 0 ? (c.powered ? 3 : 2) + (infirmary - 1) * .5f : 1) * rate;
            s.injury = Math.Max(0, s.injury - hours * heal);
            if (!s.Bleeding) s.health = Math.Min(s.MaxHealth, s.health + hours * (resting ? 5 : 1.5f) * (atBase ? 1.5f : 1) * rate);
        }

        /// <summary>Blood plague rises once infected (about 100 over PlagueHours) and turns the survivor at 100.</summary>
        void Plague(Survivor s, float hours)
        {
            if (!s.plagueInfected) { s.plague = 0; return; }
            float before = s.plague;
            s.plague = Math.Min(100, s.plague + hours * 100 / PlagueHours * (s.HasTrait("hardy") ? .7f : 1));
            if (before < 50 && s.plague >= 50) world.Message(s, "The blood plague is spreading. Use a plague cure or get to the base infirmary.");
            if (before < 85 && s.plague >= 85) world.Message(s, "Fever burns behind your eyes. The plague will take you soon.");
            if (s.plague >= 100) Kill(s, "The blood plague took them.");
        }

        /// <summary>Gives a survivor the blood plague (plague zombie bites, heart attacks). Bestiary calls this.</summary>
        public void InfectPlague(Survivor s, float amount = 2)
        {
            if (s == null || !s.alive) return;
            if (!s.plagueInfected)
            {
                s.plagueInfected = true; world.Emit(EventKind.Plague, s.pos, s.id, "infected");
                world.Message(s, "You've caught the blood plague. Use a plague cure, or reach the base infirmary with Meds.");
            }
            s.plague = Math.Min(99, s.plague + amount);
        }

        /// <summary>Removes the blood plague (plague cure item or the infirmary).</summary>
        public void CurePlague(Survivor s, string how)
        {
            if (s == null || !s.plagueInfected) return;
            s.plagueInfected = false; s.plague = 0;
            world.Emit(EventKind.Plague, s.pos, s.id, "cured");
            if (how.Length > 0) world.Message(s, how);
        }

        public void Kill(Survivor s, string cause)
        {
            if (!s.alive) return;
            ReleaseVehicle(s);
            bool turns = s.plagueInfected && s.plague >= 100;
            s.alive = false; s.health = 0; s.diedAt = world.minutes; s.causeOfDeath = cause; s.action = null; s.sleeping = false;
            // The body drops everything it carried.
            var pile = world.PileAt((int)s.pos.x, (int)s.pos.y, true);
            foreach (var item in s.inventory.items) pile.inventory.Add(item);
            s.inventory.items.Clear(); s.primary = s.secondary = -1; s.worn.Clear();
            world.Emit(EventKind.Death, s.pos, s.id, cause);
            OnSurvivorKilled(s, cause);   // Humans.cs: kill feed, faction reactions
            CommunityOnDeath(s);          // SimCommunity.cs: morale, switching, "dead" outcome when the community is gone
            // The blood plague raises its victim as a plague zombie. Zomboid's zombie infection no longer kills or turns.
            if (turns)
            {
                var z = world.AddZombie(s.pos, world.rng); z.female = s.female; z.variant = 0; z.state = ZombieState.Idle; z.timer = 20; z.plague = true;
                world.Emit(EventKind.Plague, s.pos, s.id, "turned", z.id);
            }
        }

        void UpdateSleep(Survivor s, float hours)
        {
            var a = s.action; bool baseRest = a != null && a.extra == 1;
            var bed = a != null && a.target >= 0 && a.target < world.map.props.Count ? world.map.props[a.target] : null;
            float rate = baseRest ? 14 + 2 * world.community.Level(FacilityKind.Beds) : bed == null ? 7 : bed.kind == PropKind.Bed || bed.kind == PropKind.Bunk ? 13 : 10;
            s.fatigue = Math.Max(0, s.fatigue - hours * rate);
            s.endurance = StaminaCap(s);
            if (a != null) a.elapsed += hours;   // hours slept
            world.index.Query(s.pos, s.HasTrait("light_sleeper") ? 14 : 7, query);
            bool danger = query.Any(z => z.state == ZombieState.Chase || z.state == ZombieState.Thump || z.state == ZombieState.Attack);
            if (danger) { Wake(s, "You jolt awake. Something is right outside!"); return; }
            if (s.Bleeding) { Wake(s, "You wake up bleeding. Bandage the wound."); return; }
            if (s.plagueInfected && s.plague >= 85) { Wake(s, "You wake in a plague fever. Find a cure now."); return; }
            bool rested = s.fatigue <= 2 && (!baseRest || s.health >= s.MaxHealth - .5f || a.elapsed >= 16);
            if (rested) Wake(s, baseRest && s.injury > 1 ? "You get up rested. Your injuries still need time." : "You wake up rested.");
        }
        void Wake(Survivor s, string why) { s.sleeping = false; s.action = null; if (why.Length > 0) world.Message(s, why); }

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
            // Chopping, prying and barricading make noise while working.
            if ((a.kind == ActionKind.Chop || a.kind == ActionKind.Pry || a.kind == ActionKind.Barricade) && world.rng.Chance(dt * 1.2f)) world.MakeNoise(s.pos, a.kind == ActionKind.Barricade ? 14 : 9, s.id);
            if (a.elapsed < a.duration) return;
            s.action = null;
            Complete(s, a);
        }
    }
}
