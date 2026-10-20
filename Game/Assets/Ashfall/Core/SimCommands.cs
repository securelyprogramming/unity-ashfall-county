using System;
using System.Linq;

namespace Ashfall.Core
{
    public sealed partial class Simulation
    {
        /// <summary>Validates and starts a survivor's request. Clients never mutate state directly.</summary>
        void Execute(Survivor s, Command c)
        {
            var inv = s.inventory;
            if (s.knockedDown > 0 && c.kind != CommandKind.Cancel) return;
            if (s.sleeping && c.kind != CommandKind.Wake) return;
            if (s.vehicle >= 0 && !AllowedInVehicle(c.kind)) return;
            if (VehicleCommand(s, c)) return;
            if (ExecuteSurvival(s, c)) return;
            switch (c.kind)
            {
                case CommandKind.Cancel: CancelAction(s, ""); break;
                case CommandKind.Wake: if (s.sleeping) Wake(s, "You get up."); break;
                case CommandKind.OpenContainer:
                    {
                        if (c.target < 0 || c.target >= world.map.containers.Count) return;
                        var box = world.map.containers[c.target];
                        if (box.prop < 0 || !ReachProp(s, world.map.props[box.prop])) return;
                        world.RollContainer(box);
                        s.lootContainer = box.id; break;
                    }
                case CommandKind.CloseContainer: s.lootContainer = -1; break;
                case CommandKind.Transfer:
                    {
                        var from = Resolve(s, c.from, false); var to = Resolve(s, c.to, true);
                        if (from == null || to == null || from == to) return;
                        var item = from.ByUid(c.item); if (item == null) return;
                        if (c.from.kind == InvKind.Player && s.IsEquipped(item.uid)) Unequip(s, item.uid);
                        int count = c.count <= 0 ? item.count : Math.Min(c.count, item.count);
                        if (c.to.kind == InvKind.Player && s.CarriedWeight + item.Def.weight * count > s.Capacity * 1.9f) { world.Message(s, "That's too heavy to carry."); return; }
                        if (c.to.kind != InvKind.Player && c.to.kind != InvKind.Pile && to.Weight + item.Def.weight * count > to.capacity) { world.Message(s, "It won't fit."); return; }
                        var moved = from.Take(item.uid, count); if (moved != null) to.Add(moved);
                        break;
                    }
                case CommandKind.Drop: case CommandKind.DropAll:
                    {
                        var item = inv.ByUid(c.item); if (item == null) return;
                        if (s.IsEquipped(item.uid)) Unequip(s, item.uid);
                        var moved = inv.Take(item.uid, c.kind == CommandKind.DropAll || c.count <= 0 ? item.count : c.count);
                        world.PileAt((int)s.pos.x, (int)s.pos.y, true).inventory.Add(moved);
                        break;
                    }
                case CommandKind.Equip:
                    {
                        var item = inv.ByUid(c.item); if (item == null) return;
                        if (c.slot == 1) { if (s.primary == item.uid) s.primary = -1; s.secondary = item.uid; }
                        else
                        {
                            if (s.secondary == item.uid) s.secondary = -1;
                            s.primary = item.uid;
                            if (item.Def.twoHanded && s.secondary >= 0 && s.Secondary != null && s.Secondary.Def.category != ItemCategory.Light) s.secondary = -1;
                        }
                        world.Message(s, "Equipped " + item.Def.name + (c.slot == 1 ? " in your off hand." : "."));
                        break;
                    }
                case CommandKind.Unequip: Unequip(s, c.item); break;
                case CommandKind.Wear:
                    {
                        var item = inv.ByUid(c.item); if (item == null || item.Def.slot == WearSlot.None) return;
                        Begin(s, ActionKind.Wear, 1.6f, -1, item.uid); break;
                    }
                case CommandKind.Unwear:
                    {
                        int uid; if (!s.worn.TryGetValue((WearSlot)c.slot, out uid)) return;
                        Begin(s, ActionKind.Unwear, 1.2f, c.slot, uid); break;
                    }
                case CommandKind.Use: Use(s, c.item); break;
                case CommandKind.Treat:
                    {
                        var item = inv.ByUid(c.item); if (item == null || c.target < 0 || c.target >= s.wounds.Count) return;
                        var d = item.Def; var w = s.wounds[c.target];
                        float speed = 1 - s.Level(Skill.FirstAid) * .07f;
                        if (d.bandageQuality > 0) Begin(s, ActionKind.Bandage, 3.5f * speed, c.target, item.uid);
                        else if (d.disinfect > 0) Begin(s, ActionKind.Disinfect, 2.5f * speed, c.target, item.uid);
                        else if (d.heal > 0 && d.id == "suture_kit") { if (w.kind == WoundKind.Laceration || w.kind == WoundKind.GlassCut || w.kind == WoundKind.Bite) Begin(s, ActionKind.Suture, 6 * speed, c.target, item.uid); else world.Message(s, "That wound doesn't need stitches."); }
                        break;
                    }
                case CommandKind.Craft:
                    {
                        var r = Recipe.Get(c.recipe); if (r == null) return;
                        var missing = r.Missing(s, NearHeat(s));
                        if (missing.Count > 0) { world.Message(s, "Can't craft " + r.name + ": need " + string.Join(", ", missing) + "."); return; }
                        if (r.places && !CanPlace(s, r.place)) return;
                        Begin(s, ActionKind.Craft, r.time * (1 - s.Level(r.skill) * .05f), -1, -1, r.id);
                        break;
                    }
                case CommandKind.ToggleDoor:
                    {
                        var o = OpeningRef(c.target); if (!ReachOpening(s, o)) return;
                        if (o.barricade > 0) { world.Message(s, "It's barricaded."); return; }
                        if (o.broken && o.kind == Edge.Door) { world.Message(s, "The door is broken."); return; }
                        if (o.kind == Edge.Window && o.broken) { world.Message(s, "The window is smashed. Climb through it or barricade it."); return; }
                        if (!o.open && o.locked)
                        {
                            // Locks open from inside the building; from outside the survivor must pry, bash or use a window.
                            var inside = world.map.BuildingAt(s.pos);
                            if (inside != null && inside.id == o.building) { o.locked = false; world.Message(s, "You unlock it."); }
                            else { world.Message(s, o.kind == Edge.Door ? "Locked. Pry it with a crowbar, bash it, or find a window." : "The window is locked. Smash it to get through."); world.Emit(EventKind.Door, o.Center, s.id, "locked"); return; }
                        }
                        o.open = !o.open; world.Emit(EventKind.Door, o.Center, s.id, o.open ? "open" : "close"); world.MakeNoise(o.Center, 2.5f, s.id);
                        if (!o.open && o.kind == Edge.Door && world.map.Collides(s.pos, Ashfall.Core.Survivor.Radius)) o.open = true;
                        break;
                    }
                case CommandKind.Climb:
                    {
                        if (c.target < 0 && c.target >= -4)
                        {
                            // Hop a fence: target encodes the direction (+x, -x, +y, -y).
                            int dir = -1 - c.target; var step = FenceStep(dir);
                            int ax = (int)Math.Floor(s.pos.x), ay = (int)Math.Floor(s.pos.y);
                            Opening none; var e = world.map.Between(ax, ay, ax + (int)step.x, ay + (int)step.y, out none);
                            if (e != Edge.Fence) { world.Message(s, "Stand right at the fence to climb it."); return; }
                            Begin(s, ActionKind.Climb, 1.1f, -1, -1, "", false, dir); return;
                        }
                        var o = OpeningRef(c.target); if (!ReachOpening(s, o)) return;
                        if (!o.Climbable && !(o.kind == Edge.Fence)) { world.Message(s, o.kind == Edge.Window ? "Open or smash the window first." : "You can't climb that."); return; }
                        Begin(s, ActionKind.Climb, o.kind == Edge.Fence ? 1.1f : 1.6f, o.id);
                        break;
                    }
                case CommandKind.BreakWindow:
                    {
                        var o = OpeningRef(c.target); if (!ReachOpening(s, o) || o.kind != Edge.Window || o.broken || o.barricade > 0) return;
                        BreakGlass(o, s.pos);
                        if (s.Primary == null && world.rng.Chance(.6f)) Cut(s, BodyPart.Hands, "You smash the glass with your hand and cut yourself.");
                        break;
                    }
                case CommandKind.ClearGlass:
                    {
                        var o = OpeningRef(c.target); if (!ReachOpening(s, o) || !o.broken || o.glassCleared) return;
                        Begin(s, ActionKind.ClearGlass, 2.5f, o.id); break;
                    }
                case CommandKind.Barricade:
                    {
                        var o = OpeningRef(c.target); if (!ReachOpening(s, o) || o.kind == Edge.Fence) return;
                        if (o.barricade >= 4) { world.Message(s, "It can't take more planks."); return; }
                        if (inv.FindTag("hammer") == null || inv.Count("plank") < 1 || inv.Count("nails") < 2) { world.Message(s, "Barricading needs a hammer, a plank and two nails."); return; }
                        if (o.kind == Edge.Door && o.open) o.open = false;
                        Begin(s, ActionKind.Barricade, 3.5f * (1 - s.Level(Skill.Carpentry) * .06f), o.id);
                        break;
                    }
                case CommandKind.Unbarricade:
                    {
                        var o = OpeningRef(c.target); if (!ReachOpening(s, o) || o.barricade <= 0) return;
                        if (inv.FindTag("hammer") == null && inv.FindTag("pry") == null) { world.Message(s, "You need a hammer or crowbar to pull planks."); return; }
                        Begin(s, ActionKind.Unbarricade, 4, o.id); break;
                    }
                case CommandKind.Pry:
                    {
                        var o = OpeningRef(c.target); if (!ReachOpening(s, o) || !o.locked || o.open) return;
                        if (inv.FindTag("pry") == null) { world.Message(s, "You need a crowbar."); return; }
                        Begin(s, ActionKind.Pry, 4.5f, o.id); break;
                    }
                case CommandKind.Sleep:
                    {
                        if (s.fatigue < 30) { world.Message(s, "You're not tired enough to sleep."); return; }
                        world.index.Query(s.pos, 14, query);
                        if (query.Any(z => z.state == ZombieState.Chase || z.state == ZombieState.Thump || IsVisible(s.id, z.id))) { world.Message(s, "You can't sleep with zombies this close."); return; }
                        var bed = c.target >= 0 && c.target < world.map.props.Count ? world.map.props[c.target] : null;
                        if (bed != null && (!bed.CanSleep || !ReachProp(s, bed))) bed = null;
                        s.sleeping = true; s.action = new TimedAction { kind = ActionKind.Sleep, duration = 1e9f, target = bed == null ? -1 : bed.id };
                        world.Message(s, bed == null ? "You curl up on the floor." : "You lie down on the " + (bed.kind == PropKind.Bed ? "bed." : "couch."));
                        break;
                    }
                case CommandKind.Reload:
                    {
                        var gun = s.Primary; if (gun == null || gun.Def.category != ItemCategory.Firearm) return;
                        if (gun.loaded >= gun.Def.magazine) return;
                        if (inv.Count(gun.Def.ammo) == 0) { world.Message(s, "No " + ItemCatalog.Get(gun.Def.ammo).name + "."); return; }
                        Begin(s, ActionKind.Reload, gun.Def.pellets > 1 ? 3 : 2.2f, -1, gun.uid, "", true);
                        break;
                    }
                case CommandKind.ToggleLight:
                    {
                        var l = s.Secondary;
                        if (l == null || l.Def.category != ItemCategory.Light) { var f = inv.items.FirstOrDefault(i => i.Def.category == ItemCategory.Light); if (f == null) { world.Message(s, "You have no light."); return; } s.secondary = f.uid; l = f; }
                        if (l.condition <= 0) { world.Message(s, "The batteries are dead."); s.lightOn = false; return; }
                        s.lightOn = !s.lightOn; break;
                    }
                case CommandKind.Forage:
                    {
                        if (world.map.Indoors(s.pos)) { world.Message(s, "Forage outdoors, ideally in the woods."); return; }
                        Begin(s, ActionKind.Forage, 8 * (1 - s.Level(Skill.Foraging) * .05f)); break;
                    }
                case CommandKind.Chop:
                    {
                        var tree = c.target >= 0 && c.target < world.map.props.Count ? world.map.props[c.target] : null;
                        if (tree == null || tree.kind != PropKind.Tree || !ReachProp(s, tree)) return;
                        var axe = s.Primary; if (axe == null || !axe.Def.Has("chop")) { world.Message(s, "Equip an axe to chop trees."); return; }
                        Begin(s, ActionKind.Chop, 9 * (1 - s.Level(Skill.Fitness) * .05f), tree.id);
                        break;
                    }
                case CommandKind.Harvest:
                    {
                        var crop = c.target >= 0 && c.target < world.map.props.Count ? world.map.props[c.target] : null;
                        if (crop == null || crop.kind != PropKind.Crops || !ReachProp(s, crop)) return;
                        Begin(s, ActionKind.Harvest, 3, crop.id); break;
                    }
                case CommandKind.Cook: case CommandKind.Boil:
                    {
                        var item = inv.ByUid(c.item); if (item == null) return;
                        if (!NearHeat(s)) { world.Message(s, "You need a lit campfire or a working stove nearby."); return; }
                        if (c.kind == CommandKind.Cook && (string.IsNullOrEmpty(item.Def.cooksInto) || item.cooked)) return;
                        if (c.kind == CommandKind.Boil && (item.Def.waterCapacity == 0 || !item.tainted || item.water == 0)) return;
                        Begin(s, c.kind == CommandKind.Cook ? ActionKind.Cook : ActionKind.Boil, (c.kind == CommandKind.Cook ? 6 : 8) * (1 - s.Level(Skill.Cooking) * .05f), -1, item.uid);
                        break;
                    }
                case CommandKind.FillWater:
                    {
                        var item = inv.ByUid(c.item); if (item == null || item.Def.waterCapacity == 0) return;
                        var src = c.target >= 0 && c.target < world.map.props.Count ? world.map.props[c.target] : null;
                        if (src != null && (!src.IsWaterSource || !ReachProp(s, src))) return;
                        if (src == null && !NearWater(s)) { world.Message(s, "No water source here."); return; }
                        Begin(s, ActionKind.FillWater, 2.5f, src == null ? -1 : src.id, item.uid);
                        break;
                    }
                case CommandKind.LightFire:
                    {
                        var fire = c.target >= 0 && c.target < world.map.props.Count ? world.map.props[c.target] : null;
                        if (fire == null || fire.kind != PropKind.Campfire || !ReachProp(s, fire) || fire.lit) return;
                        if (inv.FindTag("fire") == null) { world.Message(s, "You need a lighter or matches."); return; }
                        if (fire.fuel <= 0) { world.Message(s, "Add fuel first: branches, planks or logs."); return; }
                        Begin(s, ActionKind.Light, 2, fire.id); break;
                    }
                case CommandKind.AddFuel:
                    {
                        var fire = c.target >= 0 && c.target < world.map.props.Count ? world.map.props[c.target] : null;
                        var item = inv.ByUid(c.item);
                        if (fire == null || item == null || fire.kind != PropKind.Campfire || !ReachProp(s, fire)) return;
                        float hours = item.def == "log" ? 3 : item.def == "plank" ? 1.2f : item.def == "branch" ? .7f : item.def == "magazine" || item.def == "sheet" ? .3f : 0;
                        if (hours <= 0) return;
                        inv.Take(item.uid, 1); fire.fuel += hours; fire.modified = true; world.Message(s, "The fire has " + fire.fuel.ToString("0.0") + " hours of fuel.");
                        break;
                    }
            }
        }

        static Vec2 FenceStep(int dir) { return dir == 0 ? new Vec2(1, 0) : dir == 1 ? new Vec2(-1, 0) : dir == 2 ? new Vec2(0, 1) : new Vec2(0, -1); }
        Opening OpeningRef(int id) { return id >= 0 && id < world.map.openings.Count ? world.map.openings[id] : null; }

        void Unequip(Survivor s, int uid)
        {
            if (s.primary == uid) s.primary = -1;
            if (s.secondary == uid) { s.secondary = -1; s.lightOn = false; }
            foreach (var kv in s.worn.Where(kv => kv.Value == uid).ToList()) s.worn.Remove(kv.Key);
        }

        bool CanPlace(Survivor s, PropKind kind)
        {
            var p = s.pos + Vec2.FromAngle(s.facing) * 1.1f; int x = (int)Math.Floor(p.x), y = (int)Math.Floor(p.y);
            if (world.map.TileBlocked(x, y) || NonBlockingPropAt(x, y) != null) { world.Message(s, "No room there. Face an empty spot."); return false; }
            if (kind == PropKind.Campfire && world.map.RoomAt(x, y) != null) { world.Message(s, "Build fires outdoors."); return false; }
            if (Vec2.Distance(s.pos, new Vec2(x + .5f, y + .5f)) < .7f) { world.Message(s, "Step back from where you want to build."); return false; }
            return true;
        }

        void Use(Survivor s, int uid)
        {
            var item = s.inventory.ByUid(uid); if (item == null) return;
            var d = item.Def;
            if (d.category == ItemCategory.Food || d.category == ItemCategory.Drink && d.waterCapacity == 0)
            {
                if (!string.IsNullOrEmpty(d.needsTool) && s.inventory.FindTag(d.needsTool) == null)
                {
                    if (s.inventory.FindTag("knife") == null && s.inventory.FindTag("screwdriver") == null) { world.Message(s, "You need a can opener (or a knife) to open this."); return; }
                    Begin(s, ActionKind.Eat, 9, -1, uid); world.Message(s, "You work the can open with a blade..."); return;
                }
                Begin(s, d.category == ItemCategory.Drink ? ActionKind.Drink : ActionKind.Eat, d.category == ItemCategory.Drink ? 2 : 3.5f, -1, uid); return;
            }
            if (d.waterCapacity > 0) { if (item.water == 0) { world.Message(s, "It's empty."); return; } Begin(s, ActionKind.Drink, 2.5f, -1, uid); return; }
            if (d.id == "painkillers") { Begin(s, ActionKind.Pills, 1.5f, -1, uid); return; }
            if (d.id == "antibiotics") { Begin(s, ActionKind.Pills, 1.5f, -1, uid); return; }
            if (d.id == "magazine") { Begin(s, ActionKind.Read, 6, -1, uid); return; }
            if (d.id == "battery")
            {
                var light = s.inventory.items.FirstOrDefault(i => i.Def.category == ItemCategory.Light && i.condition < .99f);
                if (light == null) { world.Message(s, "No flashlight needs batteries."); return; }
                light.condition = 1; s.inventory.Take(uid, 1); world.Message(s, "You swap in a fresh battery."); return;
            }
            if (d.category == ItemCategory.Light) { s.secondary = uid; Execute(s, Command.Of(CommandKind.ToggleLight)); return; }
            if (d.category == ItemCategory.Clothing || d.category == ItemCategory.Bag) { Execute(s, new Command { kind = CommandKind.Wear, item = uid }); return; }
            if (d.bandageQuality > 0 || d.disinfect > 0 || d.id == "suture_kit")
            {
                // Treat the most urgent wound that this item helps.
                int best = -1;
                for (int i = 0; i < s.wounds.Count; i++)
                {
                    var w = s.wounds[i];
                    bool helps = d.bandageQuality > 0 ? !w.bandaged || w.dirtyBandage : d.disinfect > 0 ? !w.disinfected : !w.sutured && w.kind != WoundKind.Scratch;
                    if (helps && (best < 0 || w.bleeding && !s.wounds[best].bleeding)) best = i;
                }
                if (best < 0) { world.Message(s, "No wound needs that."); return; }
                Execute(s, new Command { kind = CommandKind.Treat, item = uid, target = best }); return;
            }
            if (d.IsWeapon) { Execute(s, new Command { kind = CommandKind.Equip, item = uid }); return; }
            if (d.Has("map")) { world.Message(s, "Press M to read your map."); return; }
            if (d.Has("radio")) { world.Message(s, "The radio hisses. Emergency broadcasts will come through while you carry it."); return; }
        }

        void Complete(Survivor s, TimedAction a)
        {
            var inv = s.inventory; var item = a.item >= 0 ? inv.ByUid(a.item) : null;
            if (CompleteSurvival(s, a)) return;
            switch (a.kind)
            {
                case ActionKind.Eat:
                    {
                        if (item == null) return; var d = item.Def;
                        float quality = item.Rotten ? .3f : item.Stale ? .6f : 1;
                        if (item.cooked || d.id == "stew" || d.id.EndsWith("_cooked") || d.id == "potato_baked") quality *= 1 + s.Level(Skill.Cooking) * .04f;
                        s.hunger = MathUtil.Clamp(s.hunger + d.hunger * quality, 0, 100);
                        s.thirst = MathUtil.Clamp(s.thirst + d.thirst, 0, 100);
                        s.boredom = MathUtil.Clamp(s.boredom + d.boredom, 0, 100);
                        if (d.raw && !item.cooked) { s.sickness = Math.Min(100, s.sickness + (d.id == "potato" || d.id == "rice" ? 8 : 30)); world.Message(s, "That was raw. Your stomach turns."); }
                        if (item.Rotten) { s.sickness = Math.Min(100, s.sickness + 35); world.Message(s, "That was rotten..."); }
                        inv.Take(item.uid, 1); world.Emit(EventKind.Eat, s.pos, s.id, d.name);
                        break;
                    }
                case ActionKind.Drink:
                    {
                        if (item == null) return; var d = item.Def;
                        if (d.waterCapacity > 0)
                        {
                            int sips = 0;
                            while (item.water > 0 && s.thirst > 3 && sips < 10) { item.water--; s.thirst = Math.Max(0, s.thirst - 9); sips++; if (item.tainted) s.sickness = Math.Min(100, s.sickness + 9); }
                            if (item.water == 0) item.tainted = false;
                            if (sips > 0 && item.tainted) world.Message(s, "The water tasted foul. You should have boiled it.");
                            if (sips == 0) world.Message(s, "You're not thirsty.");
                        }
                        else { s.thirst = MathUtil.Clamp(s.thirst + d.thirst, 0, 100); s.hunger = MathUtil.Clamp(s.hunger + d.hunger, 0, 100); s.fatigue = MathUtil.Clamp(s.fatigue + d.fatigue, 0, 100); s.boredom = MathUtil.Clamp(s.boredom + d.boredom, 0, 100); inv.Take(item.uid, 1); }
                        world.Emit(EventKind.Drink, s.pos, s.id, d.name);
                        break;
                    }
                case ActionKind.Pills:
                    if (item == null) return;
                    if (item.def == "painkillers") { s.stress += 40; world.Message(s, "The painkillers dull the ache."); }
                    else { foreach (var w in s.wounds) { w.infected = false; } s.sickness = Math.Max(0, s.sickness - 20); world.Message(s, "You take the antibiotics. Wound infections will clear." + (s.zombieInfected ? " You feel no better." : "")); }
                    inv.Take(item.uid, 1); break;
                case ActionKind.Read: if (item == null) return; s.boredom = Math.Max(0, s.boredom - 35); inv.Take(item.uid, 1); world.Message(s, "You read the magazine cover to cover."); break;
                case ActionKind.Bandage:
                    {
                        if (item == null || a.target >= s.wounds.Count) return; var w = s.wounds[a.target];
                        w.bandaged = true; w.dirtyBandage = false; w.bandageAge = -s.Level(Skill.FirstAid) * 1.5f; w.bleeding = false;
                        w.severity -= .1f * item.Def.bandageQuality * (1 + s.Level(Skill.FirstAid) * .08f);
                        if (item.Def.disinfect > 0) w.disinfected = true;
                        if (item.Def.heal > 0) s.health = Math.Min(100, s.health + item.Def.heal);
                        if (item.def == "rag" && world.rng.Chance(.08f) && !w.disinfected) w.infected = true;
                        inv.Take(item.uid, 1); GainXp(s, Skill.FirstAid, 6); world.Message(s, "You bandage your " + Wound.Part(w.part).ToLowerInvariant() + ".");
                        break;
                    }
                case ActionKind.Disinfect:
                    {
                        if (item == null || a.target >= s.wounds.Count) return; var w = s.wounds[a.target];
                        w.disinfected = true; w.infected = false; s.stress = Math.Max(0, s.stress - 5);
                        if (world.rng.Chance(.25f)) inv.Take(item.uid, 1);
                        GainXp(s, Skill.FirstAid, 4); world.Message(s, "You clean the wound. It stings."); break;
                    }
                case ActionKind.Suture:
                    {
                        if (item == null || a.target >= s.wounds.Count) return; var w = s.wounds[a.target];
                        w.sutured = true; w.bleeding = false; w.severity *= .7f; inv.Take(item.uid, 1); GainXp(s, Skill.FirstAid, 10); world.Message(s, "You stitch the wound closed."); break;
                    }
                case ActionKind.Wear:
                    {
                        if (item == null) return; var slot = item.Def.slot;
                        s.worn[slot] = item.uid; if (s.primary == item.uid) s.primary = -1; if (s.secondary == item.uid) s.secondary = -1;
                        world.Message(s, "You put on the " + item.Def.name + "."); break;
                    }
                case ActionKind.Unwear: s.worn.Remove((WearSlot)a.target); break;
                case ActionKind.Climb:
                    {
                        if (a.target < 0 && a.extra >= 0)
                        {
                            var step = FenceStep(a.extra);
                            var tile = new Vec2((float)Math.Floor(s.pos.x) + .5f, (float)Math.Floor(s.pos.y) + .5f);
                            var hop = tile + step;
                            if (world.map.Collides(hop, Ashfall.Core.Survivor.Radius)) { world.Message(s, "Something blocks the other side."); return; }
                            s.pos = hop; GainXp(s, Skill.Fitness, 1); return;
                        }
                        var o = OpeningRef(a.target); if (o == null || !ReachOpening(s, o)) return;
                        if (!o.Climbable && o.kind != Edge.Fence) return;
                        // Step to the far side of the edge.
                        var c = o.Center; var side = o.vertical ? new Vec2(Math.Sign(c.x - s.pos.x), 0) : new Vec2(0, Math.Sign(c.y - s.pos.y));
                        var dest = c + side * .55f;
                        if (world.map.Collides(dest, Ashfall.Core.Survivor.Radius)) { world.Message(s, "Something blocks the other side."); return; }
                        s.pos = dest;
                        if (o.broken && !o.glassCleared && world.rng.Chance(.55f - s.Level(Skill.Fitness) * .04f)) Cut(s, world.rng.Chance(.5f) ? BodyPart.LeftLeg : BodyPart.Torso, "You cut yourself on the broken glass.");
                        GainXp(s, Skill.Fitness, 2);
                        break;
                    }
                case ActionKind.ClearGlass: { var o = OpeningRef(a.target); if (o != null) o.glassCleared = true; world.Message(s, "You knock the glass shards out of the frame."); break; }
                case ActionKind.Barricade:
                    {
                        var o = OpeningRef(a.target); if (o == null || !ReachOpening(s, o)) return;
                        if (inv.Count("plank") < 1 || inv.Count("nails") < 2 || inv.FindTag("hammer") == null) return;
                        inv.Consume("plank", 1); inv.Consume("nails", 2);
                        o.barricade++; o.barricadeHp = 1 + s.Level(Skill.Carpentry) * .1f;
                        GainXp(s, Skill.Carpentry, 5); world.Emit(EventKind.Barricade, o.Center, s.id, o.barricade + " plank" + (o.barricade > 1 ? "s" : ""));
                        world.MakeNoise(o.Center, 14, s.id);
                        break;
                    }
                case ActionKind.Unbarricade:
                    {
                        var o = OpeningRef(a.target); if (o == null || o.barricade <= 0) return;
                        o.barricade--; o.barricadeHp = o.barricade > 0 ? 1 : 0; inv.Add(ItemCatalog.Make("plank")); world.MakeNoise(o.Center, 8, s.id);
                        break;
                    }
                case ActionKind.Pry:
                    {
                        var o = OpeningRef(a.target); if (o == null) return;
                        if (world.rng.Chance(.75f)) { o.locked = false; o.open = true; world.Emit(EventKind.Door, o.Center, s.id, "open"); world.Message(s, "The lock gives with a crack."); }
                        else world.Message(s, "The crowbar slips. Try again.");
                        world.MakeNoise(o.Center, 10, s.id); break;
                    }
                case ActionKind.Reload:
                    {
                        var gun = item; if (gun == null) return; var d = gun.Def;
                        int need = d.magazine - gun.loaded, have = inv.Count(d.ammo), load = Math.Min(need, have);
                        inv.Consume(d.ammo, load); gun.loaded += load; world.Message(s, "Reloaded " + d.name + " [" + gun.loaded + "/" + d.magazine + "].");
                        break;
                    }
                case ActionKind.Forage:
                    {
                        var z = world.map.ZoneAt((int)s.pos.x, (int)s.pos.y); int lvl = s.Level(Skill.Foraging);
                        float bonus = (z == ZoneKind.Forest ? 1.5f : z == ZoneKind.Farm || z == ZoneKind.Wild ? 1 : .4f) * (1 + lvl * .12f);
                        int found = 0;
                        if (world.rng.Chance(.55f * bonus)) { inv.Add(ItemCatalog.Make("branch", world.rng.Range(1, 3))); found++; }
                        if (world.rng.Chance(.3f * bonus)) { inv.Add(ItemCatalog.Make("berries", 1)); found++; }
                        if (world.rng.Chance(.18f * bonus)) { inv.Add(ItemCatalog.Make("mushrooms", 1)); found++; }
                        if (world.rng.Chance(.22f * bonus)) { inv.Add(ItemCatalog.Make("worms", world.rng.Range(1, 4))); found++; }
                        world.Message(s, found == 0 ? "You find nothing useful." : "You forage " + found + " useful thing" + (found > 1 ? "s." : "."));
                        GainXp(s, Skill.Foraging, 3 + found * 2); break;
                    }
                case ActionKind.Chop:
                    {
                        var tree = a.target >= 0 ? world.map.props[a.target] : null; if (tree == null || tree.removed) return;
                        world.map.RemoveProp(tree); tree.modified = true;
                        var pile = world.PileAt(tree.x, tree.y, true); pile.inventory.Add(ItemCatalog.Make("log")); pile.inventory.Add(ItemCatalog.Make("log")); pile.inventory.Add(ItemCatalog.Make("branch", world.rng.Range(2, 5)));
                        var axe = s.Primary; if (axe != null) axe.condition = Math.Max(.05f, axe.condition - .03f);
                        s.endurance = Math.Max(0, s.endurance - 25); GainXp(s, Skill.Fitness, 6);
                        world.Emit(EventKind.Break, new Vec2(tree.x + .5f, tree.y + .5f), s.id, "The tree comes down."); world.MakeNoise(s.pos, 16, s.id);
                        break;
                    }
                case ActionKind.Harvest:
                    {
                        var crop = a.target >= 0 ? world.map.props[a.target] : null; if (crop == null || crop.removed) return;
                        crop.removed = true; crop.modified = true;
                        bool potato = world.rng.Chance(.5f);
                        inv.Add(ItemCatalog.Make(potato ? "potato" : "carrots", world.rng.Range(1, 3)));
                        // Gone-to-seed field crops are the main way to start a garden without looting seed packets.
                        if (world.rng.Chance(.3f + s.Level(Skill.Farming) * .04f)) { inv.Add(ItemCatalog.Make(potato ? "seeds_potato" : "seeds_carrot")); world.Message(s, "Some of the plants have gone to seed. You save a packet."); }
                        GainXp(s, Skill.Foraging, 3); GainXp(s, Skill.Farming, 2); break;
                    }
                case ActionKind.Cook:
                    {
                        if (item == null || string.IsNullOrEmpty(item.Def.cooksInto)) return;
                        var cooked = ItemCatalog.Make(item.Def.cooksInto); cooked.cooked = true;
                        inv.Take(item.uid, 1); inv.Add(cooked); GainXp(s, Skill.Cooking, 5); world.Message(s, "Cooked: " + cooked.Def.name + ".");
                        break;
                    }
                case ActionKind.Boil: if (item != null) { item.tainted = false; GainXp(s, Skill.Cooking, 3); world.Message(s, "The water is safe to drink now."); } break;
                case ActionKind.FillWater:
                    {
                        if (item == null) return; var d = item.Def;
                        var src = a.target >= 0 ? world.map.props[a.target] : null;
                        int space = d.waterCapacity - item.water; if (space <= 0) { world.Message(s, "It's already full."); return; }
                        bool piped = src != null && (src.kind == PropKind.Sink || src.kind == PropKind.Toilet);
                        if (src == null) { if (item.water == 0 || item.tainted) item.tainted = true; item.water = d.waterCapacity; item.tainted = true; world.Message(s, "You fill it from the river. Boil before drinking."); }
                        else if (piped && world.WaterOn) { item.water = d.waterCapacity; world.Message(s, "You fill the " + d.name + " from the tap."); }
                        else
                        {
                            int take = Math.Min(space, (int)src.water);
                            if (take <= 0) { world.Message(s, "It's dry."); return; }
                            src.water -= take; src.modified = true; bool foul = piped;   // standing water in pipes goes bad after the shutoff
                            if (foul) item.tainted = true; else if (item.water == 0) item.tainted = false;
                            item.water += take; world.Message(s, foul ? "You drain what's left in the pipes. Boil it first." : "You collect fresh rain water.");
                        }
                        break;
                    }
                case ActionKind.Hotwire: case ActionKind.Refuel: CompleteVehicleAction(s, a); break;
                case ActionKind.Light: { var f = world.map.props[a.target]; f.lit = true; f.modified = true; world.Emit(EventKind.Fire, f.Center, s.id); break; }
                case ActionKind.Craft:
                    {
                        var r = Recipe.Get(a.recipe); if (r == null) return;
                        if (r.Missing(s, NearHeat(s)).Count > 0) { world.Message(s, "You no longer have what " + r.name + " needs."); return; }
                        foreach (var i in r.inputs) { if (i.IsTag) inv.ConsumeTag(i.Tag, i.count); else inv.Consume(i.item, i.count); }
                        if (r.water > 0) { int need = r.water; foreach (var w in inv.items.Where(x => x.Def.waterCapacity > 0 && !x.tainted)) { int take = Math.Min(need, w.water); w.water -= take; need -= take; if (need == 0) break; } }
                        if (r.anyCount > 0) { int need = r.anyCount; foreach (var id in r.anyOf) { while (need > 0 && inv.Consume(id, 1)) need--; } }
                        if (r.places)
                        {
                            if (!CanPlace(s, r.place)) return;
                            var p = s.pos + Vec2.FromAngle(s.facing) * 1.1f;
                            var prop = world.map.AddProp(r.place, (int)Math.Floor(p.x), (int)Math.Floor(p.y)); prop.modified = true;
                            if (r.place == PropKind.Campfire) prop.fuel = 1.5f;
                            if (r.place == PropKind.WoodCrate) world.map.AddContainer(prop, "").generated = true;
                        }
                        else
                        {
                            var made = ItemCatalog.Make(r.result, r.resultCount); if (r.result == "stew") made.cooked = true;
                            inv.Add(made);
                        }
                        GainXp(s, r.skill, r.xp); world.Emit(EventKind.Craft, s.pos, s.id, r.name);
                        break;
                    }
            }
        }
    }
}
