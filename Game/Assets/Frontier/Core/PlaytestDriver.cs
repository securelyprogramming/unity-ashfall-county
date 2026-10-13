using System;
using System.Collections.Generic;

namespace Mayo.Frontier
{
    /// <summary>Diagnostic player. Reads snapshots and plays through production commands only.
    /// Does not alter health, positions, enemies, resources, timers, or victory conditions.</summary>
    public sealed class PlaytestDriver
    {
        private readonly Simulation simulation;
        private readonly int playerId;
        private readonly bool continueCampaign;
        private int sequence, chapter, frames, unchanged;
        private float clock, lastX, lastZ;
        private bool goalSet;
        public bool Done { get; private set; }
        public string FailureReason { get; private set; }
        public int CommandCount { get; private set; }
        public int CompletedChapters { get; private set; }
        public string LastAction { get; private set; }
        public PlaytestDriver(Simulation simulation, int playerId = 0, bool continueCampaign = true)
        {
            if (simulation == null) throw new ArgumentNullException("simulation");
            this.simulation = simulation; this.playerId = playerId; this.continueCampaign = continueCampaign;
            Player p = simulation.PlayerById(playerId); if (p == null) throw new ArgumentException("Player must already be admitted.");
            sequence = p.lastSequence + 1; chapter = simulation.State.chapter; FailureReason = ""; LastAction = "Starting";
        }
        public void Tick(float seconds)
        {
            if (Done || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0 || seconds > 2) return;
            clock += seconds;
            while (clock + .000001f >= .1f && !Done)
            {
                clock -= .1f; frames++;
                World w = simulation.State; Player p = simulation.PlayerById(playerId);
                if (w.phase == "lost") { Fail(w.message); break; }
                if (w.phase == "won")
                {
                    CompletedChapters++;
                    if (!continueCampaign || w.chapter == 3) { Done = true; break; }
                    Send("advance"); chapter = w.chapter; goalSet = false; unchanged = 0;
                }
                if (w.chapter != chapter) { chapter = w.chapter; goalSet = false; }
                if (frames > 18000) { Fail("Autoplay exceeded the bounded command budget."); break; }
                if (p.dead) { Fail("Autoplay player is down: " + w.message); break; }
                if (w.phase == "paused") Send("pause");
                if (Distance(p.x, p.z, lastX, lastZ) < .01f) unchanged++; else unchanged = 0;
                lastX = p.x; lastZ = p.z; goalSet = false;
                Decide(w, p);
                if (!goalSet) Send("move", 0, 0);
                simulation.Tick(.1f);
            }
        }
        private void Fail(string text) { Done = true; FailureReason = text + " [" + simulation.State.mode + " chapter " + simulation.State.chapter + "; " + LastAction + "]"; }
        private ActionResult Send(string kind, float x = 0, float z = 0, int target = 0, string option = "", float aimX = 0, float aimZ = 0)
        {
            CommandCount++; ActionResult result = simulation.Apply(playerId, new Command { sequence = sequence++, kind = kind, x = x, z = z, targetId = target, option = option, aimX = aimX, aimZ = aimZ });
            if (kind != "move" && kind != "aim") LastAction = kind + " " + option + ": " + result.reason;
            return result;
        }
        private static float Distance(float x, float z, float a, float b) { return Simulation.Distance(x, z, a, b); }
        private void Decide(World w, Player p)
        {
            if (w.mode == "market-day") { Market(w, p); return; }
            if (w.mode == "hearth-rush") { Kitchen(w, p); return; }
            if (p.hp < 70 && (p.food > 0 || p.herbs > 0)) { Send("craft", option: "bandage"); }
            if (w.mode == "dead-orbit" && p.oxygen < 40)
            {
                Pickup oxygen = NearestPickup(w, p, "oxygen"); if (oxygen != null) { Use(p, oxygen.id, oxygen.x, oxygen.z); return; }
                Structure station = w.structures.Find(s => s.kind == "oxygen-station" && s.stock > 0); if (station != null) { Use(p, station.id, station.x, station.z); return; }
            }
            bool ranged = w.mode == "rift-runners" || w.mode == "dead-orbit" || w.mode == "ashfall-county" || w.mode == "havenfall-coalition" || w.mode == "stormline-express";
            if (ranged && p.ammo < 3 && p.wood > 0 && p.metal > 0) Send("craft", option: "ammo");
            Enemy threat = NearestEnemy(w, p);
            if (threat != null && (Distance(p.x, p.z, threat.x, threat.z) < 11 || w.mode != "fjordbound" || w.stage == "raiding"))
            {
                Combat(w, p, threat, ranged && p.ammo > 0); return;
            }
            int foodNeeded = w.mode == "ashfall-county" || w.mode == "havenfall-coalition" ? w.goal - w.progress + 1 : 1;
            if (p.food < foodNeeded) { Pickup food = NearestPickup(w, p, "food"); if (food != null) { Use(p, food.id, food.x, food.z); return; } }
            if (w.mode == "ashfall-county")
            {
                TaskState survivor = OpenTask(w, "survivor"); if (survivor != null) Use(p, survivor.id, survivor.x, survivor.z);
                else { TaskState medicine = OpenTask(w, "medicine"); if (medicine != null) Use(p, medicine.id, medicine.x, medicine.z); else UseTask(w, p, "evacuation"); }
            }
            else if (w.mode == "dead-orbit")
            {
                TaskState leak = OpenTask(w, "hull-breach"); if (leak != null) { if (Ensure(w, p, "metal", 2)) Use(p, leak.id, leak.x, leak.z); }
                else { TaskState uplink = OpenTask(w, "uplink"); if (uplink != null) { if (Ensure(w, p, "metal", 2)) Use(p, uplink.id, uplink.x, uplink.z); } else UseTask(w, p, "escape-pod"); }
            }
            else if (w.mode == "skyhold-siege")
            {
                Structure tower = w.structures.Find(s => s.kind == "tower" && s.hp > 0);
                if (tower != null && tower.level < w.goal) { if (Ensure(w, p, "wood", 2) && Ensure(w, p, "metal", 2)) Use(p, tower.id, tower.x, tower.z); }
                else Go(p, 0, -4);
            }
            else if (w.mode == "rift-runners") Go(p, 0, 0);
            else if (w.mode == "thornvale")
            {
                if (OpenTask(w, "healer") != null)
                {
                    if (p.herbs < 3) { Pickup herb = NearestPickup(w, p, "herbs"); if (herb != null) Use(p, herb.id, herb.x, herb.z); }
                    else UseTask(w, p, "healer");
                }
                else if (OpenTask(w, "thorn-vine") != null) UseTask(w, p, "thorn-vine");
                else if (OpenTask(w, "shrine") != null) UseTask(w, p, "shrine"); else UseTask(w, p, "elder");
            }
            else if (w.mode == "havenfall-coalition")
            {
                TaskState resident = OpenTask(w, "resident");
                if (resident != null) Use(p, resident.id, resident.x, resident.z);
                else if (OpenTask(w, "water-pump") != null) { if (Ensure(w, p, "metal", 4)) UseTask(w, p, "water-pump"); }
                else UseTask(w, p, "council");
            }
            else if (w.mode == "emberwake-rebuilders")
            {
                Structure gate = w.structures.Find(s => s.kind == "dungeon-gate" && s.active);
                if (gate != null) Use(p, gate.id, gate.x, gate.z);
                else if (OpenTask(w, "relic-chest") != null) UseTask(w, p, "relic-chest");
                else if (w.progress < w.goal)
                {
                    float[] xs = { -17, -10, -17, -10 }, zs = { -10, -10, -17, -17 };
                    if (Ensure(w, p, "wood", 2) && Ensure(w, p, "metal", 1)) BuildAt(p, xs[w.progress], zs[w.progress], "home");
                }
                else UseTask(w, p, "hearth-council");
            }
            else if (w.mode == "fjordbound")
            {
                Structure ship = w.structures.Find(s => s.kind == "longship"), hall = w.structures.Find(s => s.kind == "longhouse");
                if (w.stage == "harbour" && w.progress < w.goal) Use(p, ship.id, ship.x, ship.z);
                else if (w.stage == "raiding" && OpenTask(w, "raid-cache") != null) UseTask(w, p, "raid-cache");
                else if (w.stage == "raiding") Use(p, ship.id, ship.x, ship.z);
                else if (Ensure(w, p, "wood", 2) && Ensure(w, p, "metal", 2)) Use(p, hall.id, hall.x, hall.z);
            }
            else if (w.mode == "gatebreakers-guild")
            {
                int traps = w.structures.FindAll(s => s.kind == "spike-trap" && s.hp > 0).Count;
                if (traps < 3 && p.wood > 0 && p.metal > 0) BuildAt(p, -3 + traps * 3, -1, "spike-trap");
                else Go(p, 0, -3);
            }
            else if (w.mode == "stormline-express") Train(w, p);
        }
        private void Combat(World w, Player p, Enemy e, bool ranged)
        {
            float dx = e.x - p.x, dz = e.z - p.z, distance = Distance(p.x, p.z, e.x, e.z);
            Send("aim", aimX: dx, aimZ: dz);
            if (p.attackCooldown <= 0 && distance <= (ranged ? 10 : 2.5f))
            {
                ActionResult shot = Send("attack", target: e.id, aimX: dx, aimZ: dz);
                if (!shot.accepted && shot.reason.Contains("cover")) { Go(p, e.x, e.z); return; }
            }
            // Ranged characters maintain separation, melee characters dodge during recovery.
            float preferred = ranged ? 8.4f : 1.9f;
            if (distance > preferred + .35f) Go(p, e.x, e.z);
            else if (distance < preferred - .35f || !ranged && p.attackCooldown > .15f)
            {
                float length = Math.Max(.01f, distance), x = p.x - dx / length * 2, z = p.z - dz / length * 2;
                if (w.mode == "rift-runners" && Math.Sqrt(x * x + z * z) > w.zoneRadius - 1) { x = p.x - dz / length * 2; z = p.z + dx / length * 2; }
                Go(p, Math.Max(-20, Math.Min(20, x)), Math.Max(-20, Math.Min(20, z)), dx, dz);
            }
        }
        private void Market(World w, Player p)
        {
            Structure checkout = w.structures.Find(s => s.kind == "checkout");
            Enemy waiting = w.enemies.Find(e => e.alive && e.kind == "customer" && e.state == "checkout" && Distance(e.x, e.z, checkout.x, checkout.z) < 1.2f);
            if (waiting != null) { Use(p, checkout.id, checkout.x, checkout.z); return; }
            if (p.carried.StartsWith("stock:", StringComparison.Ordinal)) { Structure shelf = w.structures.Find(s => s.kind == "shelf-" + p.carried.Substring(6)); Use(p, shelf.id, shelf.x, shelf.z); return; }
            Structure empty = w.structures.Find(s => s.kind.StartsWith("shelf-", StringComparison.Ordinal) && s.stock < 2);
            if (empty != null) { Structure supplier = w.structures.Find(s => s.kind == "supplier"); Use(p, supplier.id, supplier.x, supplier.z, empty.kind.Substring(6)); }
            else Go(p, checkout.x, checkout.z + 1);
        }
        private void Kitchen(World w, Player p)
        {
            Structure pantry = w.structures.Find(s => s.kind == "pantry"), sink = w.structures.Find(s => s.kind == "sink"), counter = w.structures.Find(s => s.kind == "serving-counter");
            Structure board = w.structures.Find(s => s.kind == "chopping-board" && (s.item == "" || s.item == Dish(p.carried)));
            Structure pan = w.structures.Find(s => s.kind == "pan" && (s.item == "" || s.item == Dish(p.carried)));
            if (p.carried == "dirty-dish" || p.carried.StartsWith("burnt:", StringComparison.Ordinal)) { Use(p, sink.id, sink.x, sink.z); return; }
            if (p.carried.StartsWith("raw:", StringComparison.Ordinal)) { if (board != null) Use(p, board.id, board.x, board.z); return; }
            if (p.carried.StartsWith("chopped:", StringComparison.Ordinal)) { if (pan != null) Use(p, pan.id, pan.x, pan.z); return; }
            if (p.carried.StartsWith("cooked:", StringComparison.Ordinal)) { if (!w.tasks.Exists(t => t.kind == "order" && t.status == "open" && t.item == Dish(p.carried))) Use(p, sink.id, sink.x, sink.z); else Use(p, counter.id, counter.x, counter.z); return; }
            Structure waitingPan = w.structures.Find(s => s.kind == "pan" && s.item != "");
            if (waitingPan != null) { if (waitingPan.progress >= 6) Use(p, waitingPan.id, waitingPan.x, waitingPan.z); else Go(p, waitingPan.x - 1.2f, waitingPan.z); return; }
            Structure waitingBoard = w.structures.Find(s => s.kind == "chopping-board" && s.item != "");
            if (waitingBoard != null) { if (waitingBoard.progress >= 3) Use(p, waitingBoard.id, waitingBoard.x, waitingBoard.z); else Go(p, waitingBoard.x + 1.2f, waitingBoard.z); return; }
            TaskState order = w.tasks.Find(t => t.kind == "order" && t.status == "open");
            if (order != null) Use(p, pantry.id, pantry.x, pantry.z, order.item);
        }
        private string Dish(string carried) { int colon = carried.IndexOf(':'); return colon >= 0 ? carried.Substring(colon + 1) : ""; }
        private void Train(World w, Player p)
        {
            foreach (string kind in new[] { "passenger", "rail-repair", "radio-repair", "boiler-leak" })
            {
                TaskState task = OpenTask(w, kind); if (task == null) continue;
                int metal = kind == "rail-repair" ? 3 : kind == "passenger" ? 0 : 2;
                if (Ensure(w, p, "metal", metal)) Use(p, task.id, task.x, task.z); return;
            }
            Structure engine = w.structures.Find(s => s.kind == "train-engine"), valve = w.structures.Find(s => s.kind == "steam-valve");
            if (w.pressure > 70) { Use(p, valve.id, valve.x, valve.z); return; }
            if (engine.stock < 3) { if (Ensure(w, p, "wood", 1)) Use(p, engine.id, engine.x, engine.z, "fuel"); return; }
            if (w.stage != "moving") Use(p, engine.id, engine.x, engine.z); else Go(p, valve.x - 1, valve.z);
        }
        private bool Ensure(World w, Player p, string kind, int required)
        {
            int available = kind == "metal" ? p.metal : kind == "wood" ? p.wood : p.food;
            if (available >= required) return true;
            Pickup pick = NearestPickup(w, p, kind); if (pick == null) { Fail("No remaining " + kind + " for required objective."); return false; }
            Use(p, pick.id, pick.x, pick.z); return false;
        }
        private Pickup NearestPickup(World w, Player p, string kind)
        {
            Pickup best = null; float distance = float.MaxValue;
            foreach (Pickup pick in w.pickups) { float d = Distance(p.x, p.z, pick.x, pick.z); if (pick.active && pick.kind == kind && d < distance) { best = pick; distance = d; } } return best;
        }
        private Enemy NearestEnemy(World w, Player p)
        {
            Enemy best = null; float distance = float.MaxValue;
            foreach (Enemy e in w.enemies) { float d = Distance(p.x, p.z, e.x, e.z); if (e.alive && e.kind != "customer" && d < distance) { best = e; distance = d; } } return best;
        }
        private TaskState OpenTask(World w, string kind) { return w.tasks.Find(t => t.kind == kind && t.status == "open"); }
        private void UseTask(World w, Player p, string kind) { TaskState t = OpenTask(w, kind); if (t != null) Use(p, t.id, t.x, t.z); }
        private void Use(Player p, int id, float x, float z, string option = "")
        {
            if (Distance(p.x, p.z, x, z) <= 2.3f) Send("interact", target: id, option: option); else Go(p, x, z);
        }
        private void BuildAt(Player p, float x, float z, string kind) { if (Distance(p.x, p.z, x, z) < 3.5f) Send("build", x, z, option: kind); else Go(p, x, z); }
        private void Go(Player p, float x, float z, float aimX = 0, float aimZ = 0)
        {
            goalSet = true;
            float dx = x - p.x, dz = z - p.z, len = Distance(p.x, p.z, x, z);
            if (len < .35f) { Send("move"); return; }
            // Grid navigation follows the same obstacle shapes as the authoritative simulation.
            float nx, nz; FindWaypoint(p.x, p.z, x, z, out nx, out nz);
            dx = nx - p.x; dz = nz - p.z; len = Math.Max(.01f, Distance(p.x, p.z, nx, nz));
            Send("move", dx / len, dz / len, aimX: aimX, aimZ: aimZ);
        }
        private bool Blocked(float x, float z)
        {
            foreach (Structure s in simulation.State.structures)
            {
                bool solid = s.hp > 0 && (s.kind == "barrier" || s.kind == "barricade" || s.kind == "rubble" || (s.kind == "airlock" || s.kind == "dungeon-gate") && s.active);
                if (solid && Distance(x, z, s.x, s.z) < 1.3f) return true;
            }
            return false;
        }
        private void FindWaypoint(float px, float pz, float tx, float tz, out float x, out float z)
        {
            x = tx; z = tz;
            float distance = Distance(px, pz, tx, tz); bool clear = true;
            for (float d = .3f; d < distance; d += .4f) if (Blocked(px + (tx - px) * d / distance, pz + (tz - pz) * d / distance)) { clear = false; break; }
            if (clear) return;
            const int side = 43; int sx = Math.Max(0, Math.Min(42, (int)Math.Round(px) + 21)), sz = Math.Max(0, Math.Min(42, (int)Math.Round(pz) + 21));
            int gx = Math.Max(0, Math.Min(42, (int)Math.Round(tx) + 21)), gz = Math.Max(0, Math.Min(42, (int)Math.Round(tz) + 21));
            int start = sz * side + sx, goal = gz * side + gx;
            // If the target itself is a closed door, use an adjacent reachable tile.
            if (Blocked(gx - 21, gz - 21)) { float best = float.MaxValue; for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) { int ax = gx + dx, az = gz + dz; float d = Distance(px, pz, ax - 21, az - 21); if (ax < 0 || az < 0 || ax >= side || az >= side || Distance(ax - 21, az - 21, tx, tz) > 2.3f || Blocked(ax - 21, az - 21) || d >= best) continue; best = d; goal = az * side + ax; } }
            int[] parent = new int[side * side]; for (int i = 0; i < parent.Length; i++) parent[i] = -1;
            Queue<int> queue = new Queue<int>(); queue.Enqueue(start); parent[start] = start;
            int[] offsets = { -1, 1, -side, side };
            while (queue.Count > 0 && parent[goal] < 0)
            {
                int current = queue.Dequeue(); foreach (int offset in offsets)
                {
                    int next = current + offset; if (next < 0 || next >= parent.Length || parent[next] >= 0 || Math.Abs(next % side - current % side) > 1 || Blocked(next % side - 21, next / side - 21)) continue;
                    parent[next] = current; queue.Enqueue(next);
                }
            }
            if (parent[goal] < 0) return;
            int step = goal; while (parent[step] != start && parent[step] != step) step = parent[step];
            x = step % side - 21; z = step / side - 21;
        }
    }
}
