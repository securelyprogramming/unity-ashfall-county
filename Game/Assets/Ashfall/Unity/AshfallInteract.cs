using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Finds what the survivor can act on and offers context actions. Every action is a host command.</summary>
    public sealed partial class AshfallGame
    {
        public sealed class LootSource { public string label; public InvRef where; public Inventory inventory; }

        void GatherInteractions()
        {
            Nearby.Clear(); Focus = null;
            var s = Player; if (s == null || !s.alive || s.sleeping) return;
            var seat = World.VehicleOf(s); if (seat != null) { SeatInteractions(s, seat); return; }
            var map = World.map; var inv = s.inventory;
            var forward = new Vector2(Mathf.Cos(s.facing), Mathf.Sin(s.facing));
            bool hammer = inv.FindTag("hammer") != null, planks = inv.Count("plank") > 0, nails = inv.Count("nails") >= 2, pry = inv.FindTag("pry") != null;
            // Doors, windows and fences on nearby edges.
            int px = Mathf.FloorToInt(s.pos.x), py = Mathf.FloorToInt(s.pos.y);
            var seen = new HashSet<int>();
            for (int x = px - 1; x <= px + 2; x++)
                for (int y = py - 1; y <= py + 2; y++)
                    foreach (bool vertical in new[] { true, false })
                    {
                        var o = map.OpeningAt(x, y, vertical);
                        if (o == null || !seen.Add(o.id)) continue;
                        float d = Vector2.Distance(new Vector2(o.Center.x, o.Center.y), new Vector2(s.pos.x, s.pos.y)); if (d > 1.35f) continue;
                        var it = new Interactable { label = o.kind == Edge.Door ? (o.broken ? "Broken door" : o.locked && !o.open ? "Locked door" : "Door") : (o.broken ? "Smashed window" : "Window"), pos = Pos(o.Center, 1.2f) };
                        if (o.barricade > 0) it.label += " (barricaded ×" + o.barricade + ")";
                        int id = o.id;
                        if (o.kind == Edge.Door)
                        {
                            if (!o.broken && o.barricade == 0) Add(it, o.open ? "Close door" : o.locked ? "Unlock / open door" : "Open door", () => Submit(new Command { kind = CommandKind.ToggleDoor, target = id }));
                            if (o.locked && !o.open && pry) Add(it, "Pry open with crowbar", () => Submit(new Command { kind = CommandKind.Pry, target = id }));
                        }
                        else
                        {
                            if (o.Climbable) Add(it, "Climb through window", () => Submit(new Command { kind = CommandKind.Climb, target = id }));
                            if (!o.broken && o.barricade == 0) Add(it, o.open ? "Close window" : "Open window", () => Submit(new Command { kind = CommandKind.ToggleDoor, target = id }));
                            if (!o.broken && o.barricade == 0) Add(it, "Smash window", () => Submit(new Command { kind = CommandKind.BreakWindow, target = id }));
                            if (o.broken && !o.glassCleared) Add(it, "Remove broken glass", () => Submit(new Command { kind = CommandKind.ClearGlass, target = id }));
                        }
                        if (o.barricade < 4) Add(it, "Barricade (hammer, plank, 2 nails)", () => Submit(new Command { kind = CommandKind.Barricade, target = id }), hammer && planks && nails, "Needs a hammer, a plank and 2 nails");
                        if (o.barricade > 0) Add(it, "Remove barricade plank", () => Submit(new Command { kind = CommandKind.Unbarricade, target = id }), hammer || pry, "Needs a hammer or crowbar");
                        Score(it, s, forward); Nearby.Add(it);
                    }
            // Fences can be hopped.
            foreach (var fe in FenceEdges(px, py))
            {
                var c = fe.Key; if (Vector2.Distance(c, new Vector2(s.pos.x, s.pos.y)) > 1.0f) continue;
                var it = new Interactable { label = "Fence", pos = new Vector3(c.x, 1, c.y) };
                var target = fe.Value; Add(it, "Climb over fence", () => ClimbFence(target));
                Score(it, s, forward); Nearby.Add(it);
            }
            // Furniture, containers, water, heat, beds, trees and crops.
            for (int x = px - 2; x <= px + 2; x++)
                for (int y = py - 2; y <= py + 2; y++)
                {
                    var p = map.PropAt(x, y) ?? Sim.NonBlockingPropAt(x, y);
                    if (p == null || !seen.Add(100000000 + p.id)) continue;
                    if (!PropReach(s, p)) continue;
                    var it = PropInteraction(s, p); if (it == null || it.actions.Count == 0) continue;
                    Score(it, s, forward); Nearby.Add(it);
                }
            // Corpses and floor piles.
            foreach (var c in World.corpses)
            {
                if (Vector2.Distance(new Vector2(c.pos.x, c.pos.y), new Vector2(s.pos.x, s.pos.y)) > 1.6f) continue;
                var it = new Interactable { label = "Zombie corpse", pos = Pos(c.pos, .3f) }; Add(it, "Search corpse", () => UI.OpenInventory());
                Score(it, s, forward); it.score += .4f; Nearby.Add(it);
            }
            var pile = World.PileAt(px, py, false);
            if (pile != null && pile.inventory.items.Count > 0) { var it = new Interactable { label = "Items on the floor", pos = Pos(s.pos, .2f) }; Add(it, "Pick up items", () => UI.OpenInventory()); it.score = .5f; Nearby.Add(it); }
            // The survivor's own context: foraging, sleeping rough, drinking from the river.
            var self = new Interactable { label = "Yourself", pos = Pos(s.pos, 1.8f), score = 99 };
            if (!map.Indoors(s.pos)) Add(self, "Forage here (G)", () => Submit(Command.Of(CommandKind.Forage)));
            SurvivalSelfActions(self, s);
            if (Sim.NearWater(s)) foreach (var bottle in inv.items.Where(i => i.Def.waterCapacity > 0 && i.water < i.Def.waterCapacity).Take(2)) { int uid = bottle.uid; Add(self, "Fill " + bottle.Def.name + " from the river", () => Submit(new Command { kind = CommandKind.FillWater, item = uid, target = -1 })); }
            Add(self, "Sleep on the ground", () => Submit(new Command { kind = CommandKind.Sleep, target = -1 }), s.fatigue >= 30, "Not tired enough");
            if (inv.items.Any(i => i.Def.category == ItemCategory.Light)) Add(self, s.lightOn ? "Turn flashlight off (F)" : "Turn flashlight on (F)", () => Submit(Command.Of(CommandKind.ToggleLight)));
            Nearby.Add(self);
            Nearby.Sort((a, b) => a.score.CompareTo(b.score));
            Focus = Nearby.FirstOrDefault(n => n != self && n.actions.Any(a => a.enabled));
        }

        bool PropReach(Survivor s, Prop p)
        {
            float cx = Mathf.Clamp(s.pos.x, p.x, p.x + p.w), cy = Mathf.Clamp(s.pos.y, p.y, p.y + p.h);
            var near = new Vec2(cx, cy); if (Vec2.Distance(s.pos, near) > 1.25f) return false;
            var face = near + (s.pos - near).Normalized * .05f; return World.map.LineOfSight(s.pos, face);
        }

        Interactable PropInteraction(Survivor s, Prop p)
        {
            var inv = s.inventory; var it = new Interactable { label = PropName(p.kind), pos = new Vector3(p.x + p.w * .5f, 1, p.y + p.h * .5f) };
            int id = p.id;
            var car = p.kind == PropKind.Car ? World.VehicleOfProp(p.id) : null;
            if (car != null) CarInteractions(it, s, car);
            if (p.container >= 0) { int cid = p.container; Add(it, car != null ? "Search trunk" : "Search " + it.label.ToLowerInvariant(), () => { Submit(new Command { kind = CommandKind.OpenContainer, target = cid }); UI.OpenInventory(); }); }
            if (p.CanSleep) Add(it, "Sleep", () => Submit(new Command { kind = CommandKind.Sleep, target = id }), s.fatigue >= 30, "Not tired enough");
            if (p.IsWaterSource)
            {
                bool flowing = (p.kind == PropKind.Sink || p.kind == PropKind.Toilet) && World.WaterOn;
                string state = flowing ? "running" : p.water >= 1 ? (int)p.water + " units left" : "dry";
                it.label += " (" + state + ")";
                foreach (var bottle in inv.items.Where(i => i.Def.waterCapacity > 0 && i.water < i.Def.waterCapacity).Take(3)) { int uid = bottle.uid; Add(it, "Fill " + bottle.Def.name, () => Submit(new Command { kind = CommandKind.FillWater, item = uid, target = id }), flowing || p.water >= 1, "No water"); }
            }
            if (p.IsHeat)
            {
                bool hot = p.kind == PropKind.Campfire ? p.lit : World.PowerOn;
                if (p.kind == PropKind.Stove) it.label += hot ? " (working)" : " (no power)";
                if (p.kind == PropKind.Campfire) { it.label += p.lit ? " (burning, " + p.fuel.ToString("0.0") + " h)" : " (cold)"; if (!p.lit) Add(it, "Light campfire", () => Submit(new Command { kind = CommandKind.LightFire, target = id }), inv.FindTag("fire") != null && p.fuel > 0, p.fuel <= 0 ? "Add fuel first" : "Needs a lighter or matches"); foreach (var f in inv.items.Where(i => i.def == "branch" || i.def == "plank" || i.def == "log" || i.def == "magazine").Take(3)) { int uid = f.uid; Add(it, "Add " + f.Def.name + " as fuel", () => Submit(new Command { kind = CommandKind.AddFuel, target = id, item = uid })); } }
                foreach (var food in inv.items.Where(i => !string.IsNullOrEmpty(i.Def.cooksInto) && !i.cooked).Take(4)) { int uid = food.uid; Add(it, "Cook " + food.Def.name, () => Submit(new Command { kind = CommandKind.Cook, item = uid }), hot, "Not lit"); }
                foreach (var water in inv.items.Where(i => i.Def.waterCapacity > 0 && i.tainted && i.water > 0).Take(2)) { int uid = water.uid; Add(it, "Boil " + water.Def.name, () => Submit(new Command { kind = CommandKind.Boil, item = uid }), hot, "Not lit"); }
                if (Recipe.Get("stew").Missing(s, hot).Count == 0) Add(it, "Cook hearty stew", () => Submit(new Command { kind = CommandKind.Craft, recipe = "stew" }));
            }
            if (p.kind == PropKind.Tree) { var axe = s.Primary; Add(it, "Chop down tree", () => Submit(new Command { kind = CommandKind.Chop, target = id }), axe != null && axe.Def.Has("chop"), "Equip an axe"); }
            if (p.kind == PropKind.Crops) Add(it, "Harvest crops", () => Submit(new Command { kind = CommandKind.Harvest, target = id }));
            if (p.kind == PropKind.FarmPlot) PlotActions(it, s, p);
            return it;
        }

        static string CarState(Vehicle v) { return v.model + " (fuel " + Mathf.RoundToInt(v.fuel / Vehicle.TankLiters * 100) + "%, condition " + Mathf.RoundToInt(v.condition * 100) + "%)"; }

        /// <summary>Standing beside a drivable car: get in, refuel from a gas can or a powered pump.</summary>
        void CarInteractions(Interactable it, Survivor s, Vehicle v)
        {
            it.label = CarState(v); it.pos = Pos(v.pos, 1); int vid = v.id;
            Add(it, "Get in and drive", () => Submit(new Command { kind = CommandKind.EnterVehicle, target = vid }), v.driver < 0, "Occupied");
            bool room = v.fuel < Vehicle.TankLiters - .05f;
            foreach (var can in s.inventory.items.Where(i => i.def == "gas_can" && i.condition > .001f).Take(2)) { int uid = can.uid; Add(it, "Refuel from " + can.Label, () => Submit(new Command { kind = CommandKind.Refuel, target = vid, item = uid }), room, "Tank is full"); }
            if (Sim.Pump(s) != null) Add(it, "Fill up at the gas pump", () => Submit(new Command { kind = CommandKind.Refuel, target = vid, item = -1 }), room, "Tank is full");
        }

        /// <summary>Seated in a car: engine, hotwiring and getting out.</summary>
        void SeatInteractions(Survivor s, Vehicle v)
        {
            var it = new Interactable { label = CarState(v) + (v.engineOn ? " · running" : ""), pos = Pos(v.pos, 1.6f) };
            bool canStart = Sim.HasKey(s, v) || v.hotwired;
            System.Action engine = () => Submit(Command.Of(CommandKind.ToggleEngine)), exit = () => Submit(Command.Of(CommandKind.ExitVehicle));
            if (v.engineOn) { Add(it, "Get out", exit, Mathf.Abs(v.speed) < 1.5f, "Stop first"); Add(it, "Turn the engine off (R)", engine); }
            else
            {
                if (canStart) Add(it, "Start the engine (R)", engine, v.fuel > 0 && !v.Wrecked, v.Wrecked ? "Wrecked" : "Out of gas");
                else Add(it, "Hotwire the ignition", () => Submit(Command.Of(CommandKind.Hotwire)), s.inventory.FindTag("screwdriver") != null && !v.Wrecked, v.Wrecked ? "Wrecked" : "Needs a screwdriver");
                Add(it, "Get out", exit);
            }
            Nearby.Add(it); Focus = it;
        }

        static string PropName(PropKind k)
        {
            switch (k)
            {
                case PropKind.MedicineCabinet: return "Medicine cabinet"; case PropKind.WoodCrate: return "Wooden crate"; case PropKind.RainCollector: return "Rain collector";
                case PropKind.GasPump: return "Gas pump"; case PropKind.HayBale: return "Hay bale"; case PropKind.Bin: return "Garbage bin"; case PropKind.Cashier: return "Register";
                case PropKind.Car: return "Abandoned car (trunk)"; case PropKind.Crops: return "Crops"; case PropKind.FarmPlot: return "Farm plot";
                default: return k.ToString();
            }
        }

        void Add(Interactable it, string label, System.Action run, bool enabled = true, string reason = "") { it.actions.Add(new ContextAction { label = label, run = run, enabled = enabled, reason = reason }); }
        void Score(Interactable it, Survivor s, Vector2 forward)
        {
            var to = new Vector2(it.pos.x - s.pos.x, it.pos.z - s.pos.y); float d = to.magnitude;
            float facing = d < .01f ? 1 : Vector2.Dot(to / d, forward);
            it.score = d - facing * .8f;
        }

        IEnumerable<KeyValuePair<Vector2, Vector2>> FenceEdges(int px, int py)
        {
            var map = World.map;
            for (int x = px - 1; x <= px + 1; x++)
                for (int y = py - 1; y <= py + 1; y++)
                {
                    if (map.H(x, y) == Edge.Fence) yield return new KeyValuePair<Vector2, Vector2>(new Vector2(x + .5f, y), new Vector2(0, 1));
                    if (map.V(x, y) == Edge.Fence) yield return new KeyValuePair<Vector2, Vector2>(new Vector2(x, y + .5f), new Vector2(1, 0));
                }
        }
        void ClimbFence(Vector2 axis)
        {
            // Fences are simple edges without opening state; hopping is a short host-side step validated by collision.
            var s = Player; var dir = axis * Mathf.Sign(Vector2.Dot(new Vector2(Mathf.Cos(s.facing), Mathf.Sin(s.facing)), axis) + .0001f);
            Submit(new Command { kind = CommandKind.Climb, target = -1 - (dir.x > 0 ? 0 : dir.x < 0 ? 1 : dir.y > 0 ? 2 : 3) });
        }

        /// <summary>Every inventory the survivor can reach right now, for the loot panel.</summary>
        public List<LootSource> LootSources()
        {
            var list = new List<LootSource>(); var s = Player; if (s == null || Sim == null) return list;
            var map = World.map; int px = Mathf.FloorToInt(s.pos.x), py = Mathf.FloorToInt(s.pos.y);
            var props = new HashSet<int>();
            for (int x = px - 2; x <= px + 2; x++)
                for (int y = py - 2; y <= py + 2; y++)
                {
                    var p = map.PropAt(x, y); if (p == null || p.container < 0 || !props.Add(p.id) || !PropReach(s, p)) continue;
                    var inv = Sim.Peek(s, new InvRef(InvKind.Container, p.container));
                    if (inv != null) list.Add(new LootSource { label = PropName(p.kind), where = new InvRef(InvKind.Container, p.container), inventory = inv });
                }
            // The opened container first.
            if (s.lootContainer >= 0) list.Sort((a, b) => (b.where.kind == InvKind.Container && b.where.id == s.lootContainer).CompareTo(a.where.kind == InvKind.Container && a.where.id == s.lootContainer));
            foreach (var c in World.corpses)
                if (Vector2.Distance(new Vector2(c.pos.x, c.pos.y), new Vector2(s.pos.x, s.pos.y)) <= 1.6f)
                {
                    var inv = Sim.Peek(s, new InvRef(InvKind.Corpse, c.id)); if (inv != null) list.Add(new LootSource { label = "Corpse", where = new InvRef(InvKind.Corpse, c.id), inventory = inv });
                }
            int key = map.Index(px, py);
            var pile = World.PileAt(px, py, false);
            list.Add(new LootSource { label = "Floor", where = new InvRef(InvKind.Pile, key), inventory = pile == null ? new Inventory() : pile.inventory });
            return list;
        }
    }
}
