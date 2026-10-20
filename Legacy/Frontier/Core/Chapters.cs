using System;

namespace Mayo.Frontier
{
    public sealed partial class Simulation
    {
        private void BuildChapter()
        {
            int c = State.chapter;
            State.phase = "active"; State.stage = ""; State.elapsed = 0; State.tick = 0; State.remaining = 240;
            State.baseHealth = 100; State.morale = 100; State.zoneRadius = 24; State.pressure = 45; State.routeProgress = 0;
            State.progress = State.goal = State.wave = State.score = State.misses = 0;
            State.enemies.Clear(); State.structures.Clear(); State.pickups.Clear(); State.tasks.Clear();
            random = unchecked((uint)(Configuration.seed * 397 + c * 7919)); if (random == 0) random = 0x9e3779b9;
            nextId = 100; accumulator = 0; waveClock = 12; spawnClock = 2; communityClock = 0; spawned = wavesFinished = communityPulses = 0;
            foreach (Player p in State.players)
            {
                p.x = -15 + (p.id % 4) * 1.3f; p.z = -14; p.hp = p.maxHp = 100; p.oxygen = 100;
                p.food = 2; p.wood = 8; p.metal = 8; p.coins = 20; p.ammo = 12; p.herbs = p.relics = p.carryCount = 0;
                p.level = 1; p.carried = ""; p.dead = false; p.attackCooldown = p.dx = p.dz = 0; p.faceX = 1; p.faceZ = 0;
            }
            string mode = State.mode;
            if (mode == "ashfall-county") SetupAshfall(c);
            else if (mode == "dead-orbit") SetupOrbit(c);
            else if (mode == "skyhold-siege") SetupSkyhold(c);
            else if (mode == "rift-runners") SetupRift(c);
            else if (mode == "thornvale") SetupThornvale(c);
            else if (mode == "havenfall-coalition") SetupHaven(c);
            else if (mode == "emberwake-rebuilders") SetupEmberwake(c);
            else if (mode == "fjordbound") SetupFjord(c);
            else if (mode == "gatebreakers-guild") SetupGatebreakers(c);
            else if (mode == "market-day") SetupMarket(c);
            else if (mode == "hearth-rush") SetupKitchen(c);
            else if (mode == "stormline-express") SetupTrain(c);
            State.message = "Chapter " + c + ": " + State.objective;
        }
        private float Side(int chapter) { return chapter == 2 ? -1 : 1; }
        private void Supplies(int count, bool oxygen = false)
        {
            for (int i = 0; i < count; i++)
            {
                float x = -11 + (i % 5) * 5.4f, z = -8 + (i / 5) * 9;
                string kind = oxygen && i % 4 == 0 ? "oxygen" : i % 4 == 0 ? "food" : i % 4 == 1 ? "wood" : i % 4 == 2 ? "metal" : "ammo";
                Pickup(kind, x, z, kind == "ammo" ? 8 : 4);
            }
        }
        private void SetupAshfall(int c)
        {
            State.goal = 2 + c; State.remaining = 220 + c * 20;
            State.objective = "Scavenge food, clear infected, rescue " + State.goal + " neighbours, then repair the evacuation beacon.";
            Structure("safehouse", -15, -14); Task("evacuation", "Repair evacuation beacon: 3 wood + 3 metal", -15, -10);
            Supplies(15);
            for (int i = 0; i < State.goal; i++)
            {
                float x = (-5 + (i % 3) * 9) * Side(c), z = 3 + (i / 3) * 11;
                Task("survivor", "Stranded neighbour: 1 food; clear nearby infected", x, z);
                SpawnEnemy(i == State.goal - 1 && c >= 2 ? "brute" : "zombie", x + 2, z + 1, 30 + c * 8);
            }
            if (c == 3) Task("medicine", "Recover clinic antibiotics before evacuation", 15, -3);
        }
        private void SetupOrbit(int c)
        {
            State.goal = 2 + c; State.remaining = 210; State.stage = "depressurized";
            State.objective = "Seal " + State.goal + " hull breaches (2 metal each), restore the uplink, and reach the escape pod. Closed airlocks conserve oxygen.";
            Structure("oxygen-station", -15, -12, 6); Structure("airlock", -2 * Side(c), 0).active = false;
            Structure("airlock", 10 * Side(c), 8).active = c == 1;
            Task("escape-pod", "Launch escape pod after repairs and uplink", -15, -17);
            Task("uplink", "Restore communications: 2 metal; clear station security", 14 * Side(c), 14);
            Supplies(15, true);
            for (int i = 0; i < State.goal; i++)
            {
                float x = (-6 + i % 3 * 10) * Side(c), z = -5 + i / 3 * 17;
                Task("hull-breach", "Seal hull breach: 2 metal", x, z); SpawnEnemy(c == 3 && i % 2 == 0 ? "space-brute" : "space-zombie", x + 2, z + 2, 30 + c * 10);
            }
        }
        private void SetupSkyhold(int c)
        {
            State.goal = 2 + c; State.remaining = 220; State.stage = "construction";
            State.objective = "Build a tower at least " + State.goal + " levels high and survive " + (2 + c) + " siege waves. Towers fire automatically; each level costs 2 wood + 2 metal.";
            Structure("sky-core", 0, 0); Structure("tower", -3, 0).ownerId = 0;
            Supplies(20); waveClock = 14;
            if (c >= 2) Task("supply-crate", "Supply winch: retrieve 8 wood and 8 metal", 12 * Side(c), -10);
            if (c == 3) Structure("rubble", 5, 1).hp = 200;
        }
        private void SetupRift(int c)
        {
            State.goal = 4 + c * 2; State.remaining = 180; State.stage = "arena";
            State.objective = "Outlast " + State.goal + " rivals. Aim and fire, scavenge ammunition, build cover, and stay inside the shrinking storm.";
            Supplies(20);
            for (int i = 0; i < State.goal; i++)
            {
                double angle = i * Math.PI * 2 / State.goal + c * .3;
                SpawnEnemy("bot", (float)Math.Cos(angle) * 15, (float)Math.Sin(angle) * 15, 40 + c * 8);
            }
            for (int i = 0; i < 3 + c; i++) Structure("rubble", -7 + i * 3, c == 2 ? 5 : -2).hp = 180;
            foreach (Player p in State.players) { p.ammo = 24; p.x = -12; p.z = -10; }
        }
        private void SetupThornvale(int c)
        {
            State.goal = 1 + c; State.remaining = 300;
            State.objective = "Gather 3 herbs for the healer, awaken " + State.goal + " forest shrines, defeat the corrupted guardian, then return to Elder Rowan.";
            Task("healer", "Mira the healer: deliver 3 herbs", -15, -10); Task("elder", "Elder Rowan: complete the forest restoration", -15, -16);
            for (int i = 0; i < 7; i++) Pickup("herbs", -8 + i % 4 * 7, -5 + i / 4 * 14, 1);
            Supplies(10);
            for (int i = 0; i < State.goal; i++) { float x = (-8 + i * 7) * Side(c), z = 7 + i % 2 * 7; Task("shrine", "Awaken forest shrine after clearing wolves", x, z); SpawnEnemy("wolf", x + 2, z, 32 + c * 8); }
            SpawnEnemy(c == 3 ? "elder-treant" : "treant", 15 * Side(c), 16, 100 + c * 25);
            if (c >= 2) Task("thorn-vine", "Cut thorn barrier: interact after healer's blessing", 0, 0);
        }
        private void SetupHaven(int c)
        {
            State.goal = 2 + c; State.remaining = 260; State.stage = "settlement";
            State.objective = "Rescue " + State.goal + " residents, supply and defend the community for " + (2 + c) + " waves, then report to the council. Assign rescued residents as guards or scavengers.";
            Structure("community", -11, -11); Task("council", "Council: conclude this settlement day", -13, -12);
            Supplies(20); waveClock = 20;
            for (int i = 0; i < State.goal; i++) { float x = (-4 + i % 3 * 9) * Side(c), z = 3 + i / 3 * 10; Task("resident", "Rescue resident: clear infected and spend 1 food", x, z); SpawnEnemy("zombie", x + 2, z, 36 + c * 5); }
            if (c >= 2) Task("water-pump", "Restore water pump: 4 metal; improves community morale", 12, -8);
        }
        private void SetupEmberwake(int c)
        {
            State.goal = 1 + c; State.remaining = 280; State.stage = "ruins";
            State.objective = "Recover dungeon relics, defeat the guardian, and rebuild " + State.goal + " homes around the hearth. Each home needs 1 relic, 2 wood and 1 metal.";
            Structure("hearth", -14, -13); Task("hearth-council", "Return to the hearth once homes are rebuilt", -14, -16); Supplies(20);
            for (int i = 0; i < State.goal; i++) { float x = (-7 + i * 7) * Side(c), z = 10 + i % 2 * 5; Task("relic-chest", "Recover guarded relic", x, z); SpawnEnemy("sentinel", x + 2, z - 1, 40 + c * 8); }
            SpawnEnemy(c == 3 ? "ember-lord" : "dungeon-guardian", 17 * Side(c), 17, 95 + c * 20);
            Structure("dungeon-gate", 2 * Side(c), 3).active = c >= 2;
        }
        private void SetupFjord(int c)
        {
            State.goal = 2 + c; State.remaining = 260; State.stage = "harbour";
            State.objective = "Launch the longship, capture " + State.goal + " guarded raid caches, return their plunder to harbour, and raise the longhouse to level " + (1 + c) + ".";
            Structure("longship", -14, -10); Structure("longhouse", -14, -16); Supplies(15);
            for (int i = 0; i < State.goal; i++) { float x = (-5 + i % 3 * 9) * Side(c), z = 5 + i / 3 * 10; Task("raid-cache", "Seize cache while raiding; clear defenders", x, z); SpawnEnemy(i % 2 == 0 && c >= 2 ? "raider-archer" : "raider", x + 2, z, 36 + c * 9); }
        }
        private void SetupGatebreakers(int c)
        {
            State.goal = 2 + c; State.remaining = 260; State.stage = "defense";
            State.objective = "Protect the guild portal through " + State.goal + " waves. Place spike traps and bolt towers, repair defenses, and intercept the brutes.";
            Structure("portal", 0, -11); Supplies(20); waveClock = 13;
            if (c == 1) Structure("spike-trap", -3, 0).ownerId = 0;
            if (c >= 2) Structure("barricade", 2, -4).hp = 160;
            if (c == 3) Task("supply-crate", "Open guild armory for 8 wood + 8 metal", 13, -12);
        }
        private void SetupMarket(int c)
        {
            State.goal = 5 + c * 3; State.remaining = 180 + c * 20; State.stage = "open";
            State.objective = "Serve " + State.goal + " customers. Buy stock at the supplier (3 coins), refill the matching shelves, then check out waiting customers. Keep complaints below 4.";
            Structure("supplier", -8, -6); Structure("checkout", 5, -6);
            Structure("shelf-groceries", -7, 3, 3); if (c >= 2) Structure("shelf-tools", 0, 3, 2); if (c == 3) Structure("shelf-produce", 7, 3, 2);
            foreach (Player p in State.players) { p.x = -3; p.z = -6; p.wood = p.metal = 0; }
            spawnClock = 1;
        }
        private void SetupKitchen(int c)
        {
            State.goal = 3 + c * 2; State.remaining = 200 + c * 20; State.stage = "service";
            State.objective = "Serve " + State.goal + " correct dishes. Pantry → chopping board → pan → serving counter; wash the dirty dish at the sink. Food burns if left in the pan; three missed orders end service.";
            Structure("pantry", -7, -5); Structure("chopping-board", -7, 1); Structure("pan", 0, 1); Structure("serving-counter", 7, -5); Structure("sink", 7, 1);
            if (c >= 2) Structure("pan", 0, 7); if (c == 3) Structure("chopping-board", -7, 7);
            foreach (Player p in State.players) { p.x = -3; p.z = -4; p.wood = p.metal = 0; }
            AddOrder(c); spawnClock = 18;
        }
        private void SetupTrain(int c)
        {
            State.goal = 2 + c; State.remaining = 260; State.stage = "stopped";
            State.objective = "Repair the rail and radio, rescue " + State.goal + " stranded passengers, fuel the engine, and carry everyone 100 km. Vent steam when pressure rises; defend against raiders.";
            Structure("train-engine", -12, -10, 20).active = false; Structure("steam-valve", -9, -10); Structure("cargo", -15, -10);
            Task("rail-repair", "Repair broken track: 3 metal", 0, 0); Task("radio-repair", "Fix the radio: 2 metal", -12, -13); Supplies(20);
            for (int i = 0; i < State.goal; i++) { float x = (-5 + i % 3 * 9) * Side(c), z = 5 + i / 3 * 10; Task("passenger", "Rescue passenger after clearing raiders", x, z); SpawnEnemy(c >= 2 ? "raider-archer" : "raider", x + 2, z, 34 + c * 7); }
            if (c >= 2) Task("boiler-leak", "Patch boiler leak: 2 metal", -10, -7);
        }
        private void AddOrder(int chapter)
        {
            if (CompleteTasks("order") + OpenOrders() >= State.goal) return;
            string[] dishes = chapter == 1 ? new[] { "stew" } : chapter == 2 ? new[] { "stew", "fish" } : new[] { "stew", "fish", "bread" };
            TaskState t = Task("order", "Order: " + dishes[spawned % dishes.Length], 7, -5);
            t.item = dishes[spawned % dishes.Length]; t.remaining = 70 - chapter * 5; spawned++;
        }
        private int OpenOrders() { int count = 0; foreach (TaskState t in State.tasks) if (t.kind == "order" && t.status == "open") count++; return count; }
    }
}
