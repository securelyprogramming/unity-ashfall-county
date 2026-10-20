using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    public sealed partial class Simulation
    {
        readonly List<Zombie> hits = new List<Zombie>();

        void Attack(Survivor s)
        {
            var weapon = s.Primary; var def = weapon == null ? null : weapon.Def;
            if (def != null && def.category == ItemCategory.Firearm) { Fire(s, weapon); return; }
            bool armed = def != null && def.damage > 0;
            float swing = armed ? def.swingTime : .6f;
            swing *= 1 - s.Level(Skill.Melee) * .025f;
            if (s.endurance < 10) swing *= 1.4f;
            s.attackCooldown = swing; s.swingClock = swing;
            float cost = (armed ? def.endurance : 2) * (1 - s.Level(Skill.Fitness) * .04f);
            s.endurance = Math.Max(0, s.endurance - cost);
            world.Emit(EventKind.Swing, s.pos, s.id, armed ? def.id : "fist");
            float range = (armed ? def.range : .95f) + Zombie.Radius, arc = (armed ? def.arc : 50) * (float)Math.PI / 180;
            int maxTargets = !armed ? 1 : def.blade ? 1 : def.twoHanded ? 3 : 2;
            world.index.Query(s.pos, range + .5f, query);
            hits.Clear();
            var forward = Vec2.FromAngle(s.facing);
            foreach (var z in query.OrderBy(z => Vec2.Distance(z.pos, s.pos)))
            {
                var to = z.pos - s.pos; float d = to.Length;
                bool downed = z.state == ZombieState.Knocked;
                if (d > (downed ? range * .8f : range)) continue;
                float angle = Math.Abs(MathUtil.WrapAngle(to.Angle - s.facing));
                if (angle > arc * .5f + (d < .7f ? .6f : 0)) continue;
                if (!world.map.LineOfSight(s.pos, z.pos)) continue;
                hits.Add(z); if (hits.Count >= maxTargets) break;
            }
            if (hits.Count == 0) { BashOpening(s, armed ? def.damage : .15f, forward); return; }
            foreach (var z in hits)
            {
                float damage = (armed ? def.damage : .18f) * (1 + s.Level(Skill.Melee) * .07f) * world.rng.Range(.8f, 1.2f);
                if (armed && weapon.condition <= 0) damage *= .3f;
                if (s.endurance < 10) damage *= .6f;
                if (s.HasTrait("strong")) damage *= 1.2f; else if (s.HasTrait("weak")) damage *= .8f;
                if (z.state == ZombieState.Knocked) damage *= 2.2f;   // stomp or strike a downed zombie
                if (world.rng.Chance(.06f + s.Level(Skill.Melee) * .012f)) damage *= 2;   // critical hit
                HitZombie(z, s, damage, (armed ? def.knockdown : .2f) + s.Level(Skill.Melee) * .03f, armed && def.blade);
                world.MakeNoise(s.pos, armed ? def.noise : 3, s.id);
                GainXp(s, armed && def.IsWeapon ? Skill.Melee : Skill.Fitness, 2.5f);
                if (armed)
                {
                    weapon.condition -= def.conditionLoss * (1 - s.Level(Skill.Melee) * .05f) * world.rng.Range(.5f, 1.5f);
                    if (weapon.condition <= 0)
                    {
                        world.Emit(EventKind.Break, s.pos, s.id, "Your " + def.name + " broke!");
                        s.inventory.items.Remove(weapon); s.primary = -1; break;
                    }
                }
            }
        }

        /// <summary>Melee against a closed door or window in front of the survivor.</summary>
        void BashOpening(Survivor s, float damage, Vec2 forward)
        {
            var p = s.pos + forward * .7f;
            int tx = (int)Math.Floor(s.pos.x), ty = (int)Math.Floor(s.pos.y), px = (int)Math.Floor(p.x), py = (int)Math.Floor(p.y);
            if (tx == px && ty == py) return;
            if (tx != px && ty != py) py = ty;
            Opening o; world.map.Between(tx, ty, px, py, out o);
            if (o == null || !o.BlocksMovement) return;
            if (o.kind == Edge.Window && !o.broken && o.barricade == 0) { BreakGlass(o, s.pos); if (s.Primary == null) Cut(s, BodyPart.Hands, "You punch through the glass and cut your hand."); return; }
            if (o.barricade > 0) DamageBarricade(o, damage * .4f);
            else if (o.kind == Edge.Door) { o.hp -= damage * .12f; world.Emit(EventKind.Thump, o.Center, s.id); world.MakeNoise(o.Center, 12, s.id); if (o.hp <= 0) { o.broken = true; o.open = true; world.Emit(EventKind.Break, o.Center, s.id, "The door splinters off its hinges."); } }
        }

        void Shove(Survivor s)
        {
            s.shoveCooldown = .75f; s.attackCooldown = Math.Max(s.attackCooldown, .35f); s.swingClock = .4f;
            s.endurance = Math.Max(0, s.endurance - 3);
            world.Emit(EventKind.Shove, s.pos, s.id);
            world.index.Query(s.pos, 1.5f, query);
            foreach (var z in query)
            {
                var to = z.pos - s.pos; if (to.Length > 1.3f) continue;
                if (Math.Abs(MathUtil.WrapAngle(to.Angle - s.facing)) > 1.0f) continue;
                if (z.state == ZombieState.Knocked) continue;
                z.pos = world.map.Slide(z.pos, to.Normalized * .7f, Zombie.Radius); world.index.Moved(z);
                z.attackClock = 0; z.stagger = 1.0f;
                if (world.rng.Chance(.35f + s.Level(Skill.Fitness) * .03f + (s.HasTrait("strong") ? .15f : s.HasTrait("weak") ? -.1f : 0))) Knock(z);
            }
        }

        void Fire(Survivor s, ItemStack gun)
        {
            var def = gun.Def;
            s.attackCooldown = def.swingTime;
            if (gun.loaded <= 0) { world.Emit(EventKind.Empty, s.pos, s.id, def.name); world.Message(s, "*click* " + def.name + " is empty. Reload with R."); return; }
            gun.loaded--;
            s.swingClock = .2f;
            world.Emit(EventKind.Gunshot, s.pos, s.id, def.id);
            world.MakeNoise(s.pos, def.noise, s.id);
            GainXp(s, Skill.Aiming, 2);
            float skill = 1 + s.Level(Skill.Aiming) * .06f;
            for (int i = 0; i < def.pellets; i++)
            {
                float spread = def.spread * (s.aiming ? .6f : 1.6f) * (1 + s.panic * .006f) / skill;
                float angle = s.facing + world.rng.Range(-spread, spread) * (float)Math.PI / 180;
                var dir = Vec2.FromAngle(angle);
                Zombie best = null; float bestT = def.range;
                world.index.Query(s.pos, def.range, query);
                foreach (var z in query)
                {
                    var to = z.pos - s.pos; float t = Vec2.Dot(to, dir);
                    if (t <= 0 || t > bestT) continue;
                    var closest = s.pos + dir * t; if (Vec2.Distance(closest, z.pos) > .42f) continue;
                    if (!world.map.LineOfSight(s.pos, z.pos)) continue;
                    best = z; bestT = t;
                }
                if (best == null) continue;
                float chance = def.accuracy * skill * (s.aiming ? 1 : .65f) * (1 - bestT / (def.range * 1.6f));
                if (!world.rng.Chance(chance)) continue;
                float damage = def.damage * world.rng.Range(.8f, 1.25f) * (best.state == ZombieState.Knocked ? 1.5f : 1);
                if (world.rng.Chance(.08f + s.Level(Skill.Aiming) * .015f)) damage *= 3;   // headshot
                HitZombie(best, s, damage, def.pellets > 1 ? .35f : .15f, false);
                GainXp(s, Skill.Aiming, 3);
            }
        }

        void Cut(Survivor s, BodyPart part, string text)
        {
            s.wounds.Add(new Wound { part = part, kind = WoundKind.GlassCut, severity = .9f, bleeding = true });
            s.health -= 5; s.hurtFlash = .4f; world.Message(s, text); world.Emit(EventKind.Hurt, s.pos, s.id, "glass");
        }

        void BreakGlass(Opening o, Vec2 by)
        {
            o.broken = true; o.open = true; o.glassCleared = false;
            world.Emit(EventKind.Glass, o.Center); world.MakeNoise(o.Center, 18);
        }

        void DamageBarricade(Opening o, float damage)
        {
            o.barricadeHp -= damage;
            world.Emit(EventKind.Thump, o.Center);
            if (o.barricadeHp <= 0)
            {
                o.barricade--; o.barricadeHp = o.barricade > 0 ? 1 : 0;
                world.Emit(EventKind.Break, o.Center, -1, o.barricade > 0 ? "A barricade plank snaps." : "The last barricade plank breaks!");
            }
        }
    }
}
