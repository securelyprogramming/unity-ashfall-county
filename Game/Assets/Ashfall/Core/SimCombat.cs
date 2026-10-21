using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>The attacker side of combat: melee, firearms, shoves and throwables against zombies, survivors and plague hearts.</summary>
    public sealed partial class Simulation
    {
        /// <summary>Weapon damage (zombie hit points) to survivor health points. Tuned so a bat kills an unarmoured human in about 5 hits,
        /// a pistol in 3, an assault rifle in 3-4 and a hunting rifle in 1-2.</summary>
        public const float MeleeToHealth = 25, GunToHealth = 30;
        /// <summary>Melee and gun damage multipliers against plague hearts (60 hp).</summary>
        public const float MeleeToHeart = 3, GunToHeart = 2.5f;
        /// <summary>Longest throw in metres, and how long a molotov burns (in-game minutes).</summary>
        public const float MaxThrow = 18, MolotovBurnMinutes = 3;

        readonly List<Zombie> hits = new List<Zombie>();
        struct MeleeTarget { public float d; public Zombie z; public Survivor v; }
        readonly List<MeleeTarget> targets = new List<MeleeTarget>();

        /// <summary>Whether a hit from by can damage target. Never the same faction (explosives aside). The controlled player may
        /// strike anyone outside the community (with consequences); AI survivors only hurt those they are hostile to.</summary>
        public bool CanHarm(Survivor by, Survivor target)
        {
            if (by == null || target == null || !target.alive || target == by || target.faction == by.faction) return false;
            if (by.faction == 0 && by.ai == AiRole.None) return true;
            return world.Hostile(by, target);
        }

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
            float reach = armed ? def.range : .95f, range = reach + Zombie.Radius, arc = (armed ? def.arc : 50) * (float)Math.PI / 180;
            int maxTargets = !armed ? 1 : def.blade ? 1 : def.twoHanded ? 3 : 2;
            targets.Clear();
            world.index.Query(s.pos, range + .5f, query);
            foreach (var z in query)
            {
                var to = z.pos - s.pos; float d = to.Length;
                bool downed = z.state == ZombieState.Knocked;
                if (d > (downed ? range * .8f : range)) continue;
                float angle = Math.Abs(MathUtil.WrapAngle(to.Angle - s.facing));
                if (angle > arc * .5f + (d < .7f ? .6f : 0)) continue;
                if (!world.map.LineOfSight(s.pos, z.pos)) continue;
                targets.Add(new MeleeTarget { d = d, z = z });
            }
            foreach (var v in world.survivors)
            {
                if (!CanHarm(s, v) || v.inVehicle) continue;
                var to = v.pos - s.pos; float d = to.Length;
                if (d > reach + Ashfall.Core.Survivor.Radius) continue;
                if (Math.Abs(MathUtil.WrapAngle(to.Angle - s.facing)) > arc * .5f + (d < .7f ? .6f : 0)) continue;
                if (!world.map.LineOfSight(s.pos, v.pos)) continue;
                targets.Add(new MeleeTarget { d = d, v = v });
            }
            targets.Sort((a, b) => a.d.CompareTo(b.d));
            if (targets.Count > maxTargets) targets.RemoveRange(maxTargets, targets.Count - maxTargets);
            var heart = HeartInArc(s, reach, arc);
            if (targets.Count == 0 && heart == null) { BashOpening(s, armed ? def.damage : .15f, Vec2.FromAngle(s.facing)); return; }
            var c = Characters.Get(s.character); float scale = (c != null ? c.damageScale : 1) * FatigueDamage(s);
            foreach (var t in targets)
            {
                float damage = (armed ? def.damage : .18f) * (1 + s.Level(Skill.Melee) * .07f) * world.rng.Range(.8f, 1.2f);
                if (armed && weapon.condition <= 0) damage *= .3f;
                if (s.endurance < 10) damage *= .6f;
                if (s.HasTrait("strong")) damage *= 1.2f; else if (s.HasTrait("weak")) damage *= .8f;
                bool crit = world.rng.Chance(.06f + s.Level(Skill.Melee) * .012f);
                if (t.z != null)
                {
                    var z = t.z;
                    if (z.state == ZombieState.Knocked) damage *= 2.2f;   // stomp or strike a downed zombie
                    if (crit) damage *= 2;   // critical hit
                    HitZombie(z, s, damage, (armed ? def.knockdown : .2f) + s.Level(Skill.Melee) * .03f, armed && def.blade);
                }
                else
                {
                    var v = t.v;
                    if (v.knockedDown > 0) damage *= 1.6f;
                    if (world.rng.Chance(((armed ? def.knockdown : .2f) + s.Level(Skill.Melee) * .03f) * .4f)) v.knockedDown = Math.Max(v.knockedDown, .9f);
                    HurtSurvivor(v, s, damage * MeleeToHealth * scale, armed && def.blade ? "blade" : "melee", crit);
                }
                world.MakeNoise(s.pos, armed ? def.noise : 3, s.id);
                GainXp(s, armed && def.IsWeapon ? Skill.Melee : Skill.Fitness, 2.5f);
                if (!WearWeapon(s, weapon, armed)) return;
            }
            if (heart != null)
            {
                DamageHeart(heart, s, (armed ? def.damage : .18f) * MeleeToHeart * scale);
                world.MakeNoise(s.pos, armed ? def.noise : 3, s.id);
                WearWeapon(s, weapon, armed);
            }
        }

        /// <summary>Melee weapons wear with every hit; returns false when the weapon broke.</summary>
        bool WearWeapon(Survivor s, ItemStack weapon, bool armed)
        {
            if (!armed || weapon == null) return true;
            var def = weapon.Def;
            weapon.condition -= def.conditionLoss * (1 - s.Level(Skill.Melee) * .05f) * world.rng.Range(.5f, 1.5f);
            if (weapon.condition > 0) return true;
            world.Emit(EventKind.Break, s.pos, s.id, "Your " + def.name + " broke!");
            s.inventory.items.Remove(weapon); s.primary = -1; return false;
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
            float knock = .35f + s.Level(Skill.Fitness) * .03f + (s.HasTrait("strong") ? .15f : s.HasTrait("weak") ? -.1f : 0);
            foreach (var z in query)
            {
                var to = z.pos - s.pos; if (to.Length > 1.3f) continue;
                if (Math.Abs(MathUtil.WrapAngle(to.Angle - s.facing)) > 1.0f) continue;
                if (z.state == ZombieState.Knocked) continue;
                if (ShoveImmune(z)) continue;   // juggernauts cannot be shoved
                z.pos = world.map.Slide(z.pos, to.Normalized * .7f, Zombie.Radius); world.index.Moved(z);
                z.attackClock = 0; z.stagger = 1.0f;
                if (world.rng.Chance(knock)) Knock(z);
            }
            // People get pushed back too (and may fall); shoving someone counts as an attack.
            foreach (var v in world.survivors)
            {
                if (!CanHarm(s, v) || v.inVehicle) continue;
                var to = v.pos - s.pos; if (to.Length > 1.3f || Math.Abs(MathUtil.WrapAngle(to.Angle - s.facing)) > 1.0f) continue;
                v.pos = world.map.Slide(v.pos, to.Normalized * .5f, Ashfall.Core.Survivor.Radius);
                if (world.rng.Chance(knock * .5f)) v.knockedDown = Math.Max(v.knockedDown, 1f);
                Provoke(v, s);
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
            bool elevated = Elevated(s);
            float skill = 1 + s.Level(Skill.Aiming) * .06f, range = def.range * (elevated ? 1.5f : 1);
            var c = Characters.Get(s.character); float scale = (c != null ? c.damageScale : 1) * FatigueDamage(s);
            for (int i = 0; i < def.pellets; i++)
            {
                float spread = def.spread * (s.aiming ? .6f : 1.6f) * (1 + s.panic * .006f) / skill * (elevated ? .8f : 1);
                float angle = s.facing + world.rng.Range(-spread, spread) * (float)Math.PI / 180;
                var dir = Vec2.FromAngle(angle);
                Zombie best = null; Survivor person = null; PlagueHeart heart = null; float bestT = range;
                world.index.Query(s.pos, range, query);
                foreach (var z in query)
                {
                    var to = z.pos - s.pos; float t = Vec2.Dot(to, dir);
                    if (t <= 0 || t > bestT) continue;
                    var closest = s.pos + dir * t; if (Vec2.Distance(closest, z.pos) > .42f) continue;
                    if (!world.map.LineOfSight(s.pos, z.pos)) continue;
                    best = z; bestT = t;
                }
                foreach (var v in world.survivors)
                {
                    if (!CanHarm(s, v) || v.inVehicle) continue;
                    var to = v.pos - s.pos; float t = Vec2.Dot(to, dir);
                    if (t <= 0 || t > bestT) continue;
                    var closest = s.pos + dir * t; if (Vec2.Distance(closest, v.pos) > .4f) continue;
                    if (!world.map.LineOfSight(s.pos, v.pos)) continue;
                    person = v; best = null; bestT = t;
                }
                foreach (var h in world.hearts)
                {
                    if (h.destroyed) continue;
                    var to = h.pos - s.pos; float t = Vec2.Dot(to, dir);
                    if (t <= 0 || t > bestT) continue;
                    var closest = s.pos + dir * t; if (Vec2.Distance(closest, h.pos) > PlagueHeart.Radius) continue;
                    if (!world.map.LineOfSight(s.pos, h.pos)) continue;
                    heart = h; person = null; best = null; bestT = t;
                }
                bool any = best != null || person != null || heart != null;
                world.Emit(EventKind.Tracer, any ? s.pos + dir * bestT : WallStop(s.pos, dir, range), s.id, def.id);
                if (!any) continue;
                float chance = def.accuracy * skill * (s.aiming ? 1 : .65f) * (1 - bestT / (range * 1.6f)) * (elevated ? 1.15f : 1);
                if (heart != null) { DamageHeart(heart, s, def.damage * GunToHeart * (def.pellets > 1 ? .6f : 1) * scale); continue; }   // a big, still target
                if (!world.rng.Chance(chance)) continue;
                bool head = world.rng.Chance(.08f + s.Level(Skill.Aiming) * .015f);
                if (person != null)
                {
                    float damage = def.damage * GunToHealth * world.rng.Range(.8f, 1.25f) * (def.pellets > 1 ? .6f : 1) * scale * (person.knockedDown > 0 ? 1.2f : 1);
                    if (head) damage *= 2;
                    HurtSurvivor(person, s, damage, "shot", head);
                    GainXp(s, Skill.Aiming, 3);
                    continue;
                }
                float zd = def.damage * world.rng.Range(.8f, 1.25f) * (best.state == ZombieState.Knocked ? 1.5f : 1) * scale;
                if (head) zd *= 3;   // headshot
                HitZombie(best, s, zd, def.pellets > 1 ? .35f : .15f, false);
                GainXp(s, Skill.Aiming, 3);
            }
        }

        /// <summary>Where a shot that hit nothing stops: max range, or the first wall or closed door along the line.</summary>
        Vec2 WallStop(Vec2 from, Vec2 dir, float range)
        {
            var end = from + dir * range;
            if (world.map.LineOfSight(from, end)) return end;
            float lo = 0, hi = range;
            for (int i = 0; i < 6; i++) { float mid = (lo + hi) * .5f; if (world.map.LineOfSight(from, from + dir * mid)) lo = mid; else hi = mid; }
            return from + dir * lo;
        }

        /// <summary>Standing beside a watchtower: guns reach 50% further and shoot straighter.</summary>
        public bool Elevated(Survivor s)
        {
            int cx = (int)s.pos.x, cy = (int)s.pos.y;
            for (int x = cx - 3; x <= cx + 3; x++)
                for (int y = cy - 3; y <= cy + 3; y++)
                {
                    var p = world.map.PropAt(x, y); if (p == null) p = NonBlockingPropAt(x, y);
                    if (p == null || p.kind != PropKind.Watchtower || p.removed) continue;
                    float px = MathUtil.Clamp(s.pos.x, p.x, p.x + p.w), py = MathUtil.Clamp(s.pos.y, p.y, p.y + p.h);
                    if (Vec2.Distance(s.pos, new Vec2(px, py)) <= 2.5f) return true;
                }
            return false;
        }

        // ---------------------------------------------------------------- hits on survivors
        /// <summary>Fraction of damage an item absorbs when worn: ItemDef.armor, with fallbacks for the older police vest and riot helmet.</summary>
        public static float ArmorOf(ItemDef d)
        {
            if (d == null) return 0;
            if (d.armor > 0) return d.armor;
            return d.id == "police_vest" ? .3f : d.id == "helmet" ? .3f : 0;
        }

        /// <summary>Applies a weapon or blast hit: toughness of named characters, armour (helmet for headshots, vest otherwise), bleeding,
        /// interrupted actions, faction reactions and death with a kill credit.</summary>
        void HurtSurvivor(Survivor target, Survivor by, float damage, string cause, bool head)
        {
            if (target == null || !target.alive || damage <= 0) return;
            var ch = Characters.Get(target.character); if (ch != null && ch.health > 0) damage *= 100 / ch.health;
            var piece = target.Worn(head ? WearSlot.Head : WearSlot.Torso);
            float armor = piece == null ? 0 : ArmorOf(piece.Def) * MathUtil.Clamp01(piece.condition);
            if (armor > 0)
            {
                float absorbed = damage * armor; damage -= absorbed;
                piece.condition = Math.Max(0, piece.condition - absorbed / 120f);   // a vest soaks about 120 health points before it is spent
                if (piece.condition <= 0) world.Emit(EventKind.Break, target.pos, target.id, "The " + piece.Def.name + " is shredded.");
            }
            target.health -= damage; target.hurtFlash = .5f;
            if (by != null && by != target) { target.lastHitBy = by.id; target.lastHitAt = world.minutes; Provoke(target, by); }
            if (target.sleeping) Wake(target, "You wake to gunfire!");
            CancelAction(target, "");
            if (damage > 12 && world.rng.Chance(.35f))
                target.wounds.Add(new Wound { part = head ? BodyPart.Head : (BodyPart)world.rng.Range(1, 6), kind = WoundKind.Laceration, severity = 1, bleeding = true });
            world.Emit(EventKind.Hurt, target.pos, target.id, cause);
            if (target.health <= 0) Kill(target, by != null ? "Killed by " + world.DisplayName(by) + "." : cause);
        }

        /// <summary>A hit from another group: standing drops and Wary/Opportunist groups fight back for a while; AI groups feud.</summary>
        void Provoke(Survivor target, Survivor by)
        {
            if (by == null || target == null || by.faction == target.faction) return;
            var tf = world.FactionOf(target); if (tf == null) return;
            if (by.faction == 0 && tf.id != 0)
            {
                world.ChangeStanding(tf.id, tf.disposition == Disposition.Friendly ? -15 : -8);
                if (tf.disposition != Disposition.Friendly) tf.provokedUntil = Math.Max(tf.provokedUntil, world.minutes + 30);
            }
            else if (by.faction != 0 && tf.id != 0 && tf.disposition != Disposition.Hunter) { tf.feud = by.faction; tf.feudUntil = Math.Max(tf.feudUntil, world.minutes + 20); }
        }

        // ---------------------------------------------------------------- throwables
        public static bool IsThrowable(ItemDef d) { return d != null && (d.Has("throwable") || d.blastRadius > 0 || d.id == "grenade" || d.id == "molotov" || d.id == "pipe_bomb"); }
        public static float BlastRadiusOf(ItemDef d) { return d.blastRadius > 0 ? d.blastRadius : d.id == "molotov" ? 3 : d.id == "pipe_bomb" ? 7 : 6; }
        static float BlastDamageOf(ItemDef d) { return d.blastDamage > 0 ? d.blastDamage : d.id == "pipe_bomb" ? 3.5f : 3; }
        static float FuseOf(ItemDef d) { return d.id == "molotov" ? 0 : d.fuse > 0 ? d.fuse : d.id == "pipe_bomb" ? 6 : 3; }

        /// <summary>CommandKind.Throw: item = throwable uid, x/y = aim point (clamped to MaxThrow and stopped by walls).</summary>
        void Throw(Survivor s, Command c)
        {
            var item = s.inventory.ByUid(c.item); if (item == null || !IsThrowable(item.Def)) return;
            if (s.attackCooldown > 0 || s.knockedDown > 0 || s.inVehicle || s.action != null && !s.action.movable) return;
            if (float.IsNaN(c.x) || float.IsNaN(c.y) || float.IsInfinity(c.x) || float.IsInfinity(c.y)) return;
            var target = new Vec2(c.x, c.y); var to = target - s.pos; float d = to.Length;
            if (d < .5f) { to = Vec2.FromAngle(s.facing); d = 2; target = s.pos + to * 2; }
            if (d > MaxThrow) target = s.pos + to * (MaxThrow / d);
            // The arc clears low obstacles but not walls or closed doors: it lands short of them.
            var dir = (target - s.pos).Normalized; float reach = Vec2.Distance(target, s.pos);
            if (!world.map.LineOfSight(s.pos, target)) { reach = Vec2.Distance(WallStop(s.pos, dir, reach), s.pos) - .4f; target = s.pos + dir * Math.Max(.5f, reach); }
            if (s.IsEquipped(item.uid)) Unequip(s, item.uid);
            var thrown = s.inventory.Take(item.uid, 1); if (thrown == null) return;
            var def = thrown.Def;
            s.attackCooldown = .9f; s.swingClock = .5f; s.facing = dir.Angle;
            world.throwables.Add(new Throwable { id = world.nextThrowable++, item = def.id, from = s.pos, pos = target, by = s.id, fuse = FuseOf(def), flight = Throwable.FlightTime });
            world.Emit(EventKind.Swing, s.pos, s.id, "throw:" + def.id);
        }

        void UpdateThrowables(float dt)
        {
            var list = world.throwables;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                if (!ItemCatalog.Exists(t.item)) { list.RemoveAt(i); continue; }
                var def = ItemCatalog.Get(t.item); var by = Survivor(t.by);
                if (!t.landed)
                {
                    t.flight -= dt; if (t.flight > 0) continue;
                    t.landed = true; t.flight = 0;
                    world.MakeNoise(t.pos, 8, t.by);
                    if (def.id == "molotov" || def.Has("fire") && def.blastDamage <= 0)
                    {
                        // Molotov: a pool of burning fuel (Bestiary processes hazard damage).
                        world.hazards.Add(new Hazard { pos = t.pos, radius = BlastRadiusOf(def), until = (float)(world.minutes + MolotovBurnMinutes), fire = true, by = t.by });
                        world.Emit(EventKind.Fire, t.pos, t.by, "molotov"); world.MakeNoise(t.pos, 20, t.by);
                        list.RemoveAt(i); continue;
                    }
                }
                t.fuse -= dt;
                if (def.id == "pipe_bomb") { t.lure -= dt; if (t.lure <= 0) { t.lure = .5f; world.MakeNoise(t.pos, 45, t.by); } }   // beeping lure
                if (t.fuse > 0) continue;
                list.RemoveAt(i);
                Blast(t.pos, BlastRadiusOf(def), BlastDamageOf(def), by, def.id);
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
