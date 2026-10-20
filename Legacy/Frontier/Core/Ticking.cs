using System;

namespace Mayo.Frontier
{
    public sealed partial class Simulation
    {
        private void FixedTick()
        {
            State.tick++; State.elapsed = State.tick * Step; State.remaining = Math.Max(0, State.remaining - Step);
            foreach (Player p in State.players)
            {
                if (!p.connected || p.dead) continue;
                p.attackCooldown = Math.Max(0, p.attackCooldown - Step);
                float speed = p.carried == "" ? 4.8f : 4.1f;
                MovePlayer(p, p.dx * speed * Step, p.dz * speed * Step);
                if (State.mode == "dead-orbit")
                {
                    int leaks = State.goal - State.progress, closed = 0; foreach (Structure s in State.structures) if (s.kind == "airlock" && s.active) closed++;
                    p.oxygen = Math.Max(0, p.oxygen - Step * Math.Max(.25f, .65f + leaks * .23f - closed * .25f));
                    if (p.oxygen <= 0) Damage(p, Step * 8);
                }
                if (State.mode == "rift-runners" && Length(p.x, p.z) > State.zoneRadius) Damage(p, Step * (4 + State.chapter));
            }
            if (State.mode == "market-day") TickMarket();
            else
            {
                TickEnemies(); TickDefenses();
                if (State.mode == "skyhold-siege" || State.mode == "havenfall-coalition" || State.mode == "gatebreakers-guild") TickWaves();
                if (State.mode == "hearth-rush") TickKitchen();
                if (State.mode == "havenfall-coalition") TickCommunity();
                if (State.mode == "stormline-express") TickTrain();
                if (State.mode == "rift-runners") { State.zoneRadius = Math.Max(5, 24 - State.elapsed * .11f); if (LivingEnemies() == 0) Win("Your squad is the last one standing in the rift arena."); }
                if (State.mode == "ashfall-county" && State.tick % 800 == 0 && LivingEnemies() < 20) SpawnEnemy("zombie", 18, -15 + NextFloat() * 30, 35 + State.chapter * 8);
            }
            if (State.phase != "active") return;
            int living = 0; foreach (Player p in State.players) if (p.connected && !p.dead) living++;
            if (living == 0) Lose("The whole squad is down. Retry the chapter to regroup.");
            else if (State.baseHealth <= 0) Lose(State.mode == "stormline-express" ? "The relief train is lost." : "The settlement defense has fallen.");
            else if (State.morale <= 0) Lose("The community can no longer hold together.");
            else if (State.remaining <= 0) Lose("Time ran out before the chapter objective was completed.");
        }
        private void MovePlayer(Player p, float dx, float dz)
        {
            float x = Clamp(p.x + dx, -MapRadius, MapRadius), z = Clamp(p.z + dz, -MapRadius, MapRadius);
            if (Walkable(x, p.z)) p.x = x;
            if (Walkable(p.x, z)) p.z = z;
        }
        private bool Walkable(float x, float z)
        {
            foreach (Structure s in State.structures) if (IsSolid(s) && Distance(x, z, s.x, s.z) < 1.05f) return false;
            return true;
        }
        private Player NearestPlayer(float x, float z)
        {
            Player best = null; float distance = float.MaxValue;
            foreach (Player p in State.players) { if (!p.connected || p.dead) continue; float d = Distance(x, z, p.x, p.z); if (d < distance) { distance = d; best = p; } }
            return best;
        }
        private void TickEnemies()
        {
            foreach (Enemy e in State.enemies)
            {
                if (!e.alive || e.kind == "customer") continue;
                e.attackCooldown = Math.Max(0, e.attackCooldown - Step);
                if (State.mode == "rift-runners" && Length(e.x, e.z) > State.zoneRadius) { HurtEnemy(e, Step * 6); if (!e.alive) continue; }
                Player p = NearestPlayer(e.x, e.z); if (p == null) continue;
                float tx = p.x, tz = p.z, playerDistance = Distance(e.x, e.z, p.x, p.z);
                bool baseAttack = false;
                if ((State.mode == "skyhold-siege" || State.mode == "gatebreakers-guild" || State.mode == "havenfall-coalition") && playerDistance > 4)
                {
                    Structure target = State.structures.Find(s => s.kind == "sky-core" || s.kind == "portal" || s.kind == "community");
                    if (target != null) { tx = target.x; tz = target.z; baseAttack = true; }
                }
                if (State.mode == "rift-runners" && Length(e.x, e.z) > State.zoneRadius - 1) { tx = 0; tz = 0; baseAttack = false; }
                float distance = Distance(e.x, e.z, tx, tz);
                bool ranged = e.kind == "bot" || e.kind == "raider-archer";
                float reach = ranged && !baseAttack ? 7 : 1.6f;
                if (distance > reach || !ClearLine(e.x, e.z, tx, tz))
                {
                    float speed = e.speed * (1 + (Configuration.difficulty - 1) * .12f);
                    float nx = e.x + (distance > .01f ? (tx - e.x) / distance * speed * Step : 0), nz = e.z + (distance > .01f ? (tz - e.z) / distance * speed * Step : 0);
                    Structure blocker = State.structures.Find(s => IsSolid(s) && Distance(nx, nz, s.x, s.z) < 1.1f);
                    if (blocker != null)
                    {
                        if (e.attackCooldown <= 0) { blocker.hp = Math.Max(0, blocker.hp - (e.kind.Contains("brute") ? 20 : 10)); e.attackCooldown = 1; }
                        // A small tangent path prevents a permanent lock when a wall is indestructible scenery.
                        if (blocker.kind == "rubble") { e.x = Clamp(e.x + (e.id % 2 == 0 ? 1 : -1) * speed * Step, -MapRadius, MapRadius); }
                    }
                    else { e.x = Clamp(nx, -MapRadius, MapRadius); e.z = Clamp(nz, -MapRadius, MapRadius); }
                    e.state = "approach";
                }
                else if (e.attackCooldown <= 0)
                {
                    float damage = (e.kind.Contains("brute") || e.kind == "elder-treant" || e.kind == "ember-lord" ? 15 : ranged ? 8 : 9) * (1 + (Configuration.difficulty - 1) * .2f);
                    if (baseAttack) State.baseHealth = Math.Max(0, State.baseHealth - damage); else Damage(p, damage);
                    e.attackCooldown = ranged ? 1.6f : 1.2f; e.state = "attack";
                }
            }
        }
        private void TickDefenses()
        {
            foreach (Structure s in State.structures)
            {
                if (s.hp <= 0) continue;
                s.cooldown = Math.Max(0, s.cooldown - Step);
                if (s.kind != "tower" && s.kind != "bolt-tower" && s.kind != "spike-trap" || s.cooldown > 0) continue;
                float reach = s.kind == "spike-trap" ? 1.4f : 5 + s.level * 1.8f;
                Enemy target = null; float best = reach;
                foreach (Enemy e in State.enemies) { if (!e.alive || e.kind == "customer") continue; float distance = Distance(s.x, s.z, e.x, e.z); if (distance <= best && (s.kind == "spike-trap" || ClearLine(s.x, s.z, e.x, e.z))) { target = e; best = distance; } }
                if (target == null) continue;
                HurtEnemy(target, s.kind == "spike-trap" ? 38 : 12 + s.level * 5);
                s.cooldown = s.kind == "spike-trap" ? 2 : .9f;
                if (s.kind == "spike-trap") s.hp = Math.Max(0, s.hp - 4);
            }
        }
        private void TickWaves()
        {
            int targetWaves = 2 + State.chapter;
            if (State.wave > wavesFinished && LivingEnemies() == 0)
            {
                wavesFinished = State.wave; State.score += 25; waveClock = 8;
                foreach (Player p in State.players) if (p.connected && !p.dead) { p.wood += 3; p.metal += 3; p.food++; }
                State.message = "Wave " + State.wave + " cleared. Supplies awarded; prepare the next defense.";
            }
            if (State.wave >= targetWaves)
            {
                if (wavesFinished >= targetWaves && State.mode == "gatebreakers-guild") { State.progress = wavesFinished; Win("The guild portal survives every invading wave."); }
                else if (wavesFinished >= targetWaves && State.mode == "skyhold-siege")
                {
                    int tallest = 0; foreach (Structure s in State.structures) if (s.kind == "tower" && s.hp > 0) tallest = Math.Max(tallest, s.level);
                    State.progress = tallest; if (tallest >= State.goal) Win("Skyhold rises above the final siege.");
                }
                return;
            }
            if (State.wave > wavesFinished) return;
            waveClock -= Step;
            if (waveClock > 0) return;
            State.wave++; int count = 1 + State.chapter + State.wave;
            for (int i = 0; i < count; i++)
            {
                float x = State.mode == "skyhold-siege" ? (float)Math.Cos(i * Math.PI * 2 / count) * 18 : -12 + i * 24f / Math.Max(1, count - 1), z = State.mode == "skyhold-siege" ? (float)Math.Sin(i * Math.PI * 2 / count) * 18 : 18;
                string kind = State.mode == "gatebreakers-guild" ? (State.wave >= 2 && i == 0 ? "orc-brute" : "orc") : State.mode == "skyhold-siege" ? "raider" : State.wave == targetWaves && i == 0 ? "brute" : "zombie";
                SpawnEnemy(kind, x, z, 26 + State.chapter * 5 + State.wave * 5 + (kind.Contains("brute") ? 30 : 0));
            }
            State.message = "Wave " + State.wave + " approaches.";
        }
        private void TickCommunity()
        {
            communityClock += Step; if (communityClock < 10) return; communityClock -= 10; communityPulses++;
            Player host = PlayerById(0);
            foreach (TaskState resident in State.tasks)
            {
                if (resident.kind != "resident" || resident.status != "complete") continue;
                if (resident.item == "scavenger") { host.wood++; host.metal++; if (communityPulses % 3 == 0) host.food++; }
                if (resident.item == "guard") { Enemy e = State.enemies.Find(v => v.alive && Distance(v.x, v.z, -11, -11) < 12); if (e != null) HurtEnemy(e, 30); }
            }
            if (communityPulses % 3 == 0)
            {
                if (host.food > 0) host.food--;
                else { State.morale = Math.Max(0, State.morale - (CompleteTasks("water-pump") > 0 ? 5 : 12)); State.baseHealth = Math.Max(0, State.baseHealth - 4); }
            }
            foreach (Structure s in State.structures) if (s.kind == "clinic" && s.hp > 0) foreach (Player p in State.players) if (p.connected && !p.dead && Distance(p.x, p.z, s.x, s.z) < 5) p.hp = Math.Min(p.maxHp, p.hp + 5);
        }
        private void TickMarket()
        {
            spawnClock -= Step;
            int active = 0; foreach (Enemy e in State.enemies) if (e.alive && e.kind == "customer") active++;
            if (spawnClock <= 0 && active < 6 && State.progress + active < State.goal)
            {
                string[] departments = State.chapter == 1 ? new[] { "groceries" } : State.chapter == 2 ? new[] { "groceries", "tools" } : new[] { "groceries", "tools", "produce" };
                Enemy customer = SpawnEnemy("customer", 0, 16, 1); customer.need = departments[spawned % departments.Length]; customer.speed = 2; customer.patience = 65 - State.chapter * 3; customer.state = "shopping"; spawned++; spawnClock = 7;
            }
            foreach (Enemy e in State.enemies)
            {
                if (!e.alive || e.kind != "customer") continue;
                e.patience -= Step;
                if (e.patience <= 0) { e.alive = false; e.state = "complaint"; State.misses++; State.morale = Math.Max(0, State.morale - 20); continue; }
                Structure destination = State.structures.Find(s => s.kind == (e.state == "shopping" ? "shelf-" + e.need : "checkout"));
                if (destination == null) continue;
                float distance = Distance(e.x, e.z, destination.x, destination.z);
                if (distance > .7f) { e.x += (destination.x - e.x) / distance * e.speed * Step; e.z += (destination.z - e.z) / distance * e.speed * Step; }
                else if (e.state == "shopping" && destination.stock > 0) { destination.stock--; e.state = "checkout"; }
            }
            if (State.misses >= 4) Lose("Four shoppers left unhappy; reopen with stocked shelves and a faster checkout.");
        }
        private void TickKitchen()
        {
            foreach (Structure s in State.structures)
            {
                if (s.item == "" || s.kind != "pan" && s.kind != "chopping-board") continue;
                s.progress += Step;
                if (s.kind == "pan" && s.progress >= 16 && !s.item.StartsWith("burnt:", StringComparison.Ordinal)) { s.item = "burnt:" + s.item; State.baseHealth -= 15; State.message = "Food burned. Clear the pan and discard it at the sink."; }
            }
            foreach (TaskState order in State.tasks)
            {
                if (order.kind != "order" || order.status != "open") continue;
                order.remaining -= Step;
                if (order.remaining <= 0) { order.status = "missed"; State.misses++; State.message = "An order expired."; }
            }
            spawnClock -= Step;
            if (spawnClock <= 0 && OpenOrders() < 3) { AddOrder(State.chapter); spawnClock = 18; }
            if (State.misses >= 3) Lose("Three orders were missed. Regroup and try the service again.");
        }
        private void TickTrain()
        {
            Structure engine = State.structures.Find(s => s.kind == "train-engine");
            if (engine == null || State.stage != "moving" || !engine.active) return;
            if (engine.stock <= 0) { engine.active = false; State.stage = "stopped"; State.message = "The boiler is out of fuel. Load wood using the engine's fuel action."; return; }
            State.routeProgress += Step * (1.3f + State.chapter * .05f); State.pressure += Step * (1 + State.chapter * .15f);
            if (State.tick % 80 == 0) engine.stock--;
            if (State.pressure > 85) State.baseHealth = Math.Max(0, State.baseHealth - Step * (State.pressure > 100 ? 7 : 1));
            if (State.tick % 400 == 0 && LivingEnemies() < 10) SpawnEnemy("raider-archer", -4, -15, 35 + State.chapter * 10);
            if (State.routeProgress >= 100) Win("The relief train reaches its destination with the stranded passengers aboard.");
        }
    }
}
