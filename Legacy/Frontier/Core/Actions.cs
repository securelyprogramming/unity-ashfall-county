using System;

namespace Mayo.Frontier
{
    public sealed partial class Simulation
    {
        private ActionResult Attack(Player p, Command command)
        {
            if (p.attackCooldown > 0) return Reject("Attack is cooling down.");
            bool ranged = (State.mode == "rift-runners" || State.mode == "dead-orbit" || State.mode == "ashfall-county" || State.mode == "havenfall-coalition" || State.mode == "stormline-express") && p.ammo > 0 && command.option != "melee";
            float range = ranged ? 10 : 2.5f, best = range + 1; Enemy target = null;
            SetAim(p, command.aimX, command.aimZ);
            foreach (Enemy e in State.enemies)
            {
                if (!e.alive || e.kind == "customer" || command.targetId != 0 && command.targetId != e.id) continue;
                float distance = Distance(p.x, p.z, e.x, e.z);
                float dot = distance < .1f ? 1 : ((e.x - p.x) * p.faceX + (e.z - p.z) * p.faceZ) / distance;
                if (distance <= range && distance < best && dot >= (ranged ? .8f : .1f) && ClearLine(p.x, p.z, e.x, e.z)) { best = distance; target = e; }
            }
            if (command.targetId != 0 && target == null) return Reject("Target is out of range, behind cover, or outside your aim.");
            p.attackCooldown = ranged ? .4f : .55f;
            if (ranged) p.ammo--;
            if (target == null) return Accept("Missed.");
            HurtEnemy(target, (ranged ? 30 : 26) + (p.level - 1) * 8);
            return Accept(target.alive ? "Hit " + target.kind + "." : "Defeated " + target.kind + ".");
        }
        private void HurtEnemy(Enemy e, float amount)
        {
            if (!e.alive) return;
            e.hp = Math.Max(0, e.hp - amount);
            if (e.hp > 0) return;
            e.alive = false; State.score += 5;
            if (State.mode == "rift-runners") State.progress++;
            if (e.kind != "customer") Pickup(e.kind == "bot" || e.kind == "raider-archer" ? "ammo" : "metal", e.x, e.z, e.kind == "bot" ? 6 : 2);
        }
        private ActionResult Interact(Player p, Command command)
        {
            // Reviving is a production action, not a client-supplied health update.
            foreach (Player other in State.players)
            {
                if (!other.connected || !other.dead || other.id == p.id || !Near(p, other.x, other.z) || command.targetId != 0 && command.targetId != other.id) continue;
                if (p.food < 1) return Reject("Reviving a teammate requires 1 food.");
                p.food--; other.hp = 45; other.dead = false; other.oxygen = Math.Max(30, other.oxygen); return Accept("Teammate revived.");
            }
            int target = command.targetId;
            if (target == 0)
            {
                float distance = InteractionRange + .01f;
                foreach (Pickup pick in State.pickups) { float d = Distance(p.x, p.z, pick.x, pick.z); if (pick.active && d < distance) { target = pick.id; distance = d; } }
                foreach (TaskState t in State.tasks) { float d = Distance(p.x, p.z, t.x, t.z); if (t.status == "open" && t.kind != "order" && d < distance) { target = t.id; distance = d; } }
                foreach (Structure s in State.structures) { float d = Distance(p.x, p.z, s.x, s.z); if (s.hp > 0 && d < distance) { target = s.id; distance = d; } }
            }
            Pickup pickup = PickupById(target);
            if (pickup != null)
            {
                if (!pickup.active || !Near(p, pickup.x, pickup.z)) return Reject("Pickup is unavailable or out of reach.");
                if (pickup.kind == "wood") p.wood += pickup.amount;
                else if (pickup.kind == "metal") p.metal += pickup.amount;
                else if (pickup.kind == "food") p.food += pickup.amount;
                else if (pickup.kind == "ammo") p.ammo += pickup.amount;
                else if (pickup.kind == "herbs") p.herbs += pickup.amount;
                else if (pickup.kind == "oxygen") p.oxygen = Math.Min(100, p.oxygen + 40);
                else return Reject("Unsupported pickup.");
                pickup.active = false; return Accept("Collected " + pickup.kind + ".");
            }
            TaskState task = TaskById(target);
            if (task != null) return InteractTask(p, task);
            Structure structure = StructureById(target);
            if (structure != null) return InteractStructure(p, structure, command.option ?? "");
            return Reject("Move closer to an item, task, teammate, or station.");
        }
        private ActionResult InteractTask(Player p, TaskState t)
        {
            if (!Near(p, t.x, t.z) || t.status != "open") return Reject("Task is completed or out of reach.");
            string kind = t.kind;
            bool guarded = kind == "survivor" || kind == "resident" || kind == "passenger" || kind == "raid-cache" || kind == "relic-chest" || kind == "shrine" || kind == "uplink";
            if (guarded && HostileNear(t.x, t.z, 4.5f)) return Reject("Clear nearby enemies first.");
            if (kind == "survivor" || kind == "resident" || kind == "passenger")
            {
                if (kind != "passenger" && p.food < 1) return Reject("This rescue requires 1 food.");
                if (kind != "passenger") p.food--;
                Complete(t); State.progress++; State.morale = Math.Min(100, State.morale + 8); return Accept("Rescued " + (kind == "passenger" ? "passenger" : "neighbour") + ".");
            }
            if (kind == "evacuation")
            {
                if (State.progress < State.goal || State.chapter == 3 && CompleteTasks("medicine") == 0) return Reject("Rescue every neighbour and collect any required antibiotics first.");
                if (p.wood < 3 || p.metal < 3) return Reject("The evacuation beacon needs 3 wood + 3 metal.");
                p.wood -= 3; p.metal -= 3; Complete(t); Win("The county convoy escapes with your neighbours."); return Accept(State.message);
            }
            if (kind == "medicine") { Complete(t); p.herbs += 2; return Accept("Clinic antibiotics recovered."); }
            if (kind == "hull-breach" || kind == "uplink" || kind == "radio-repair" || kind == "boiler-leak")
            {
                if (p.metal < 2) return Reject("Repair requires 2 metal.");
                p.metal -= 2; Complete(t); if (kind == "hull-breach") State.progress++; return Accept("Repaired " + kind + ".");
            }
            if (kind == "escape-pod")
            {
                if (State.progress < State.goal || CompleteTasks("uplink") == 0) return Reject("Seal all breaches and restore the uplink before launching.");
                Complete(t); Win("Your crew escapes the infected orbital station."); return Accept(State.message);
            }
            if (kind == "supply-crate") { Complete(t); p.wood += 8; p.metal += 8; return Accept("Construction supplies recovered."); }
            if (kind == "healer")
            {
                if (p.herbs < 3) return Reject("Mira needs 3 forest herbs.");
                p.herbs -= 3; p.hp = p.maxHp; Complete(t); return Accept("Mira grants the woodland blessing and heals you.");
            }
            if (kind == "thorn-vine") { if (CompleteTasks("healer") == 0) return Reject("Receive the healer's blessing before cutting the thorns."); Complete(t); p.wood += 3; return Accept("The thorn path opens."); }
            if (kind == "shrine")
            {
                if (CompleteTasks("healer") == 0 || State.chapter >= 2 && CompleteTasks("thorn-vine") == 0) return Reject("Restore the healer's blessing and clear the thorn path first.");
                Complete(t); State.progress++; p.hp = Math.Min(p.maxHp, p.hp + 20); return Accept("A forest shrine awakens.");
            }
            if (kind == "elder")
            {
                if (State.progress < State.goal || LivingEnemies() > 0) return Reject("Awaken all shrines and defeat the corrupted guardian first.");
                Complete(t); Win("Rowan welcomes the living forest back to Thornvale."); return Accept(State.message);
            }
            if (kind == "council")
            {
                if (State.progress < State.goal || wavesFinished < 2 + State.chapter || LivingEnemies() > 0 || State.chapter >= 2 && CompleteTasks("water-pump") == 0) return Reject("Rescue all residents, restore required water supplies, and survive the day's waves first.");
                Complete(t); Win("The coalition survives another day together."); return Accept(State.message);
            }
            if (kind == "water-pump") { if (p.metal < 4) return Reject("Water pump repair needs 4 metal."); p.metal -= 4; Complete(t); State.morale = 100; return Accept("Clean water returns to the settlement."); }
            if (kind == "relic-chest")
            {
                if (State.chapter >= 2 && State.structures.Exists(s => s.kind == "dungeon-gate" && s.active)) return Reject("Open the dungeon gate before recovering these relics.");
                Complete(t); p.relics++; return Accept("A settlement relic has been recovered.");
            }
            if (kind == "hearth-council") { if (State.progress < State.goal || LivingEnemies() > 0) return Reject("Rebuild the required homes and defeat the dungeon guardian first."); Complete(t); Win("The rebuilt hearth shelters a new community."); return Accept(State.message); }
            if (kind == "raid-cache") { if (State.stage != "raiding") return Reject("Launch the longship before taking raid plunder."); Complete(t); p.relics++; return Accept("Plunder loaded. Return to the longship to bank it."); }
            if (kind == "rail-repair") { if (p.metal < 3) return Reject("Track repair needs 3 metal."); p.metal -= 3; Complete(t); return Accept("The broken rail is repaired."); }
            return Reject("Use the matching station for this task.");
        }
        private ActionResult InteractStructure(Player p, Structure s, string option)
        {
            if (!Near(p, s.x, s.z) || s.hp <= 0) return Reject("Structure is destroyed or out of reach.");
            if (s.kind == "airlock") { s.active = !s.active; return Accept(s.active ? "Airlock sealed: oxygen loss reduced." : "Airlock opened."); }
            if (s.kind == "oxygen-station") { if (s.stock <= 0) return Reject("Oxygen station is empty; collect emergency canisters."); if (p.oxygen >= 95) return Reject("Oxygen is already full."); s.stock--; p.oxygen = 100; return Accept("Oxygen refilled."); }
            if (s.kind == "dungeon-gate") { if (!s.active) return Reject("The dungeon gate is already open."); if (HostileNear(s.x, s.z, 4)) return Reject("Clear gate guards first."); s.active = false; return Accept("Dungeon gate opened."); }
            if (s.kind == "tower" || s.kind == "longhouse" || s.kind == "bolt-tower")
            {
                if (s.level >= 6) return Reject("Structure is at maximum level.");
                if (p.wood < 2 || p.metal < 2) return Reject("Upgrading needs 2 wood + 2 metal.");
                p.wood -= 2; p.metal -= 2; s.level++; s.hp = s.maxHp += 20; if (State.mode == "skyhold-siege") State.progress = Math.Max(State.progress, s.level); CheckFjordWin(); return Accept("Upgraded " + s.kind + " to level " + s.level + ".");
            }
            if (s.kind == "longship")
            {
                if (State.stage == "harbour") { State.stage = "raiding"; p.x = 0; p.z = -2; return Accept("Longship reaches the raid shore."); }
                foreach (Player crew in State.players) { State.progress += crew.relics; crew.relics = 0; if (crew.connected) { crew.x = -13 + crew.id % 3; crew.z = -12; } }
                State.stage = "harbour"; CheckFjordWin(); return Accept(State.phase == "won" ? State.message : "Plunder banked at the harbour. Upgrade the longhouse or raid again.");
            }
            if (s.kind == "supplier")
            {
                if (p.carried != "") return Reject("Put down your current stock first.");
                string goods = option == "tools" || option == "produce" ? option : "groceries";
                if (!State.structures.Exists(v => v.kind == "shelf-" + goods)) return Reject("This department is not open in the current chapter.");
                if (p.coins < 3) return Reject("A wholesale crate costs 3 coins.");
                p.coins -= 3; p.carried = "stock:" + goods; p.carryCount = 3; return Accept("Bought " + goods + " stock.");
            }
            if (s.kind.StartsWith("shelf-", StringComparison.Ordinal))
            {
                string goods = s.kind.Substring(6);
                if (p.carried != "stock:" + goods || p.carryCount < 1) return Reject("Bring matching " + goods + " stock from the supplier.");
                if (s.stock >= 9) return Reject("Shelf is full.");
                int amount = Math.Min(9 - s.stock, p.carryCount); s.stock += amount; p.carryCount -= amount; if (p.carryCount == 0) p.carried = ""; return Accept("Shelf restocked.");
            }
            if (s.kind == "checkout")
            {
                Enemy customer = null; foreach (Enemy e in State.enemies) if (e.alive && e.kind == "customer" && e.state == "checkout" && Distance(e.x, e.z, s.x, s.z) < 1.2f) { customer = e; break; }
                if (customer == null) return Reject("No customer is ready for checkout.");
                customer.alive = false; customer.state = "served"; p.coins += 6; State.progress++; State.score += 20;
                if (State.progress >= State.goal) Win("Your shop closes with happy customers and a healthy till."); return Accept(State.phase == "won" ? State.message : "Sale completed: +6 coins.");
            }
            if (s.kind == "pantry" || s.kind == "chopping-board" || s.kind == "pan" || s.kind == "serving-counter" || s.kind == "sink") return KitchenInteract(p, s, option);
            if (s.kind == "train-engine")
            {
                if (option == "fuel") { if (p.wood < 1) return Reject("Fuel needs 1 wood."); p.wood--; s.stock += 8; return Accept("Engine fuel loaded."); }
                if (CompleteTasks("rail-repair") == 0 || CompleteTasks("radio-repair") == 0 || State.chapter >= 2 && CompleteTasks("boiler-leak") == 0) return Reject("Repair track, radio, and any boiler leak before departure.");
                if (State.progress < State.goal) return Reject("Rescue all stranded passengers before departing.");
                s.active = !s.active; State.stage = s.active ? "moving" : "stopped"; return Accept(s.active ? "The relief train departs." : "Train stopped for repairs.");
            }
            if (s.kind == "steam-valve") { State.pressure = Math.Max(15, State.pressure - 35); State.baseHealth = Math.Max(0, State.baseHealth - 1); return Accept("Steam vented safely."); }
            if (s.kind == "community" || s.kind == "safehouse" || s.kind == "clinic" || s.kind == "hearth" || s.kind == "cargo")
            {
                if (p.food < 1) return Reject("Resting and community supplies require 1 food.");
                p.food--; p.hp = Math.Min(p.maxHp, p.hp + 40); State.baseHealth = Math.Min(100, State.baseHealth + 15); State.morale = Math.Min(100, State.morale + 15); return Accept("Rested and supplied the community.");
            }
            if (s.kind == "spike-trap" || s.kind == "barrier" || s.kind == "barricade" || s.kind == "portal" || s.kind == "sky-core")
            {
                if (p.metal < 1) return Reject("Repairs need 1 metal.");
                if (s.hp >= s.maxHp && s.kind != "portal" && s.kind != "sky-core") return Reject("This defense is already repaired.");
                p.metal--; s.hp = Math.Min(s.maxHp, s.hp + 45); if (s.kind == "portal" || s.kind == "sky-core") State.baseHealth = Math.Min(100, State.baseHealth + 25); return Accept("Defense repaired.");
            }
            return Reject("This object has no interaction.");
        }
        private ActionResult KitchenInteract(Player p, Structure s, string option)
        {
            if (s.kind == "pantry")
            {
                if (p.carried != "") return Reject("Your hands are full.");
                string dish = option;
                if (dish != "stew" && dish != "fish" && dish != "bread") { TaskState order = State.tasks.Find(t => t.kind == "order" && t.status == "open"); if (order == null) return Reject("No open order."); dish = order.item; }
                if (State.chapter == 1 && dish != "stew" || State.chapter == 2 && dish == "bread") return Reject("That recipe unlocks in a later chapter.");
                p.carried = "raw:" + dish; p.carryCount = 1; return Accept("Collected ingredients for " + dish + ".");
            }
            if (s.kind == "sink") { if (p.carried == "") return Reject("Bring a dirty dish or spoiled ingredients to the sink."); p.carried = ""; p.carryCount = 0; return Accept("Washed and cleared your hands."); }
            if (s.kind == "serving-counter")
            {
                if (!p.carried.StartsWith("cooked:", StringComparison.Ordinal)) return Reject("Bring a cooked dish to the serving counter.");
                string dish = p.carried.Substring(7); TaskState order = State.tasks.Find(t => t.kind == "order" && t.status == "open" && t.item == dish);
                if (order == null) return Reject("No customer ordered that dish; discard it or wait for a matching order.");
                Complete(order); State.progress++; p.carried = "dirty-dish"; p.carryCount = 1; State.score += (int)order.remaining;
                if (State.progress >= State.goal) Win("The hearth kitchen finishes a successful service."); return Accept(State.phase == "won" ? State.message : "Order served. Wash the dish before preparing another.");
            }
            if (s.item != "")
            {
                if (p.carried != "") return Reject("Your hands are full.");
                if (s.kind == "chopping-board" && s.progress < 3 || s.kind == "pan" && s.progress < 6) return Reject("Preparation is still underway.");
                p.carried = s.item.StartsWith("burnt:", StringComparison.Ordinal) ? s.item : (s.kind == "pan" ? "cooked:" : "chopped:") + s.item;
                p.carryCount = 1; s.item = ""; s.progress = 0; return Accept("Picked up " + p.carried + ".");
            }
            string prefix = s.kind == "pan" ? "chopped:" : "raw:";
            if (!p.carried.StartsWith(prefix, StringComparison.Ordinal)) return Reject(s.kind == "pan" ? "Chop ingredients before cooking them." : "Bring raw ingredients from the pantry.");
            s.item = p.carried.Substring(prefix.Length); s.progress = 0; p.carried = ""; p.carryCount = 0; return Accept(s.kind == "pan" ? "Cooking started; collect after 6 seconds, before 16 seconds." : "Chopping started; collect after 3 seconds.");
        }
        private ActionResult Build(Player p, Command command)
        {
            string kind = command.option ?? "";
            if (kind == "") kind = State.mode == "skyhold-siege" ? "tower" : State.mode == "gatebreakers-guild" ? "spike-trap" : State.mode == "emberwake-rebuilders" ? "home" : State.mode == "havenfall-coalition" ? "barricade" : "barrier";
            bool allowed = State.mode == "skyhold-siege" && (kind == "tower" || kind == "barricade") || State.mode == "gatebreakers-guild" && (kind == "spike-trap" || kind == "bolt-tower" || kind == "barricade") || State.mode == "rift-runners" && (kind == "barrier" || kind == "ramp") || State.mode == "havenfall-coalition" && (kind == "barricade" || kind == "clinic") || State.mode == "emberwake-rebuilders" && kind == "home";
            if (!allowed) return Reject("That structure is unavailable in this game.");
            float x = (float)Math.Round(command.x * 2) / 2, z = (float)Math.Round(command.z * 2) / 2;
            if (Math.Abs(x) > 18 || Math.Abs(z) > 18 || Distance(p.x, p.z, x, z) > 4) return Reject("Build on the map within 4 metres of your character.");
            foreach (Structure existing in State.structures)
            {
                if (existing.hp <= 0 || Distance(x, z, existing.x, existing.z) >= 1.5f) continue;
                if (kind == "tower" && existing.kind == "tower" && Near(p, existing.x, existing.z)) return InteractStructure(p, existing, "");
                return Reject("Another structure occupies this location.");
            }
            foreach (TaskState task in State.tasks) if (task.status == "open" && Distance(x, z, task.x, task.z) < 1.2f) return Reject("Do not block a task location.");
            int wood = kind == "spike-trap" || kind == "barrier" ? 1 : 2, metal = kind == "home" ? 1 : kind == "spike-trap" || kind == "barrier" ? 1 : 2;
            if (p.wood < wood || p.metal < metal) return Reject("Insufficient wood or metal.");
            if (kind == "home")
            {
                Structure hearth = State.structures.Find(s => s.kind == "hearth");
                if (p.relics < 1 || hearth == null || Distance(x, z, hearth.x, hearth.z) > 9) return Reject("A home needs 1 recovered relic and a site within 9 metres of the hearth.");
            }
            p.wood -= wood; p.metal -= metal;
            Structure created = Structure(kind, x, z); created.ownerId = p.id;
            if (kind == "home") { p.relics--; State.progress++; }
            if (kind == "tower") State.progress = Math.Max(State.progress, 1);
            if (kind == "clinic") State.morale = Math.Min(100, State.morale + 10);
            return Accept("Built " + kind + ".");
        }
        private ActionResult Craft(Player p, string option)
        {
            if (option == "bandage" || option == "heal" || option == "")
            {
                if (p.hp >= p.maxHp) return Reject("Health is already full.");
                if (p.herbs > 0) p.herbs--; else if (p.food > 0) p.food--; else return Reject("Healing needs 1 herb or 1 food.");
                p.hp = Math.Min(p.maxHp, p.hp + 45); return Accept("Wounds treated.");
            }
            if (option == "ammo") { if (p.wood < 1 || p.metal < 1) return Reject("Ammunition needs 1 wood + 1 metal."); p.wood--; p.metal--; p.ammo += 8; return Accept("Crafted 8 rounds."); }
            if (option == "upgrade") { if (p.level >= 4) return Reject("Weapon is fully upgraded."); if (p.wood < 4 || p.metal < 4) return Reject("Weapon upgrade needs 4 wood + 4 metal."); p.wood -= 4; p.metal -= 4; p.level++; return Accept("Weapon upgraded."); }
            return Reject("Unknown recipe.");
        }
        private ActionResult Assign(Player p, Command command)
        {
            if (State.mode != "havenfall-coalition") return Reject("Resident roles are available in Havenfall only.");
            TaskState resident = TaskById(command.targetId);
            if (resident == null || resident.kind != "resident" || resident.status != "complete") return Reject("Rescue this resident before assigning a role.");
            if (command.option != "guard" && command.option != "scavenger") return Reject("Choose guard or scavenger.");
            if (!Near(p, -11, -11)) return Reject("Assign residents at the community centre.");
            resident.item = command.option; return Accept("Resident assigned as " + command.option + ".");
        }
        private void CheckFjordWin()
        {
            if (State.mode == "fjordbound" && State.stage == "harbour" && State.progress >= State.goal && State.structures.Exists(s => s.kind == "longhouse" && s.level >= 1 + State.chapter)) Win("The longship returns; a stronger settlement awaits the next winter.");
        }
        private bool IsSolid(Structure s) { return s.hp > 0 && (s.kind == "barrier" || s.kind == "barricade" || s.kind == "rubble" || (s.kind == "airlock" || s.kind == "dungeon-gate") && s.active); }
        private bool ClearLine(float ax, float az, float bx, float bz)
        {
            float length = Distance(ax, az, bx, bz); if (length < .1f) return true;
            foreach (Structure s in State.structures)
            {
                if (!IsSolid(s)) continue;
                float t = ((s.x - ax) * (bx - ax) + (s.z - az) * (bz - az)) / (length * length);
                if (t > .05f && t < .95f && Distance(ax + (bx - ax) * t, az + (bz - az) * t, s.x, s.z) < .8f) return false;
            }
            return true;
        }
    }
}
