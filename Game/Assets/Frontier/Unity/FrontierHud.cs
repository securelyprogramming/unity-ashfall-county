using System;
using System.Globalization;
using System.Text;

namespace Mayo.Frontier.Presentation
{
    // Read-only presentation of authoritative snapshots. No commands, predicted resources,
    // or duplicate progress state live here; the same view works for a host and its clients.
    public static class FrontierHud
    {
        public static bool HasBuilding(string mode)
        {
            return mode == "skyhold-siege" || mode == "rift-runners" || mode == "havenfall-coalition" || mode == "emberwake-rebuilders" || mode == "gatebreakers-guild";
        }
        public static bool IsService(string mode) { return mode == "market-day" || mode == "hearth-rush"; }
        public static string Friendly(string value)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase((value ?? "").Replace('-', ' ').Replace(':', ' '));
        }
        public static string Clock(float seconds)
        {
            int total = Math.Max(0, (int)Math.Ceiling(seconds));
            return (total / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }
        static int Completed(World world, string kind)
        {
            int count = 0; foreach (var task in world.tasks) if (task.kind == kind && task.status == "complete") count++; return count;
        }
        static int Threats(World world)
        {
            int count = 0; foreach (var enemy in world.enemies) if (enemy.alive && enemy.kind != "customer") count++; return count;
        }
        static string Ready(World world, string kind) { return Completed(world, kind) > 0 ? "ready" : "pending"; }
        static int Highest(World world, string kind)
        {
            int level = 0; foreach (var structure in world.structures) if (structure.hp > 0 && structure.kind == kind) level = Math.Max(level, structure.level); return level;
        }
        public static string Goal(World world)
        {
            switch (world.mode)
            {
                case "ashfall-county": return "Bring every neighbour home, then repair the evacuation beacon.";
                case "dead-orbit": return "Seal the breaches, restore the uplink, then launch the escape pod.";
                case "skyhold-siege": return "Raise a " + world.goal + "-level tower and protect the sky-core through every wave.";
                case "rift-runners": return "Outlast the rivals. Scavenge ammunition and stay inside the storm.";
                case "thornvale": return "Help the healer, restore the shrines, defeat the guardian and return to the village elder.";
                case "havenfall-coalition": return "Rescue residents, supply the refuge and survive the waves before reporting to council.";
                case "emberwake-rebuilders": return "Recover relics, rebuild homes near the hearth and defeat the dungeon guardian.";
                case "fjordbound": return "Raid with the longship, bank the plunder and upgrade the longhouse.";
                case "gatebreakers-guild": return "Build a defense, intercept the orcs and keep the guild portal standing.";
                case "market-day": return "Restock matching shelves and check out shoppers before they lose patience.";
                case "hearth-rush": return "Prepare the right dishes, serve before orders expire and wash up between meals.";
                case "stormline-express": return "Repair the train, rescue passengers and travel 100 km while managing fuel and steam.";
                default: return world.objective;
            }
        }
        public static string Progress(World world)
        {
            string ratio = world.progress + " / " + world.goal;
            switch (world.mode)
            {
                case "ashfall-county": return "Neighbours rescued " + ratio + "   |   Beacon " + Ready(world, "evacuation") + (world.chapter == 3 ? "   |   Antibiotics " + Ready(world, "medicine") : "");
                case "dead-orbit": return "Breaches sealed " + ratio + "   |   Uplink " + Ready(world, "uplink");
                case "skyhold-siege": return "Tower height " + Highest(world, "tower") + " / " + world.goal + "   |   Wave " + world.wave + " / " + (2 + world.chapter);
                case "rift-runners": return "Rivals defeated " + ratio + "   |   Storm radius " + (int)Math.Ceiling(world.zoneRadius) + " m";
                case "thornvale": return "Shrines restored " + ratio + "   |   Healer " + Ready(world, "healer");
                case "havenfall-coalition": return "Residents rescued " + ratio + "   |   Wave " + world.wave + " / " + (2 + world.chapter) + "   |   Morale " + (int)Math.Ceiling(world.morale);
                case "emberwake-rebuilders": return "Homes rebuilt " + ratio + "   |   Relics recovered " + Completed(world, "relic-chest");
                case "fjordbound": return "Plunder banked " + ratio + "   |   Longhouse " + Highest(world, "longhouse") + " / " + (1 + world.chapter);
                case "gatebreakers-guild": return "Wave " + world.wave + " / " + world.goal + "   |   Invaders remaining " + Threats(world);
                case "market-day": return "Customers served " + ratio + "   |   Complaints " + world.misses + " / 4";
                case "hearth-rush": return "Dishes served " + ratio + "   |   Missed orders " + world.misses + " / 3";
                case "stormline-express": return "Passengers rescued " + ratio + "   |   Route " + (int)Math.Floor(world.routeProgress) + " / 100 km";
                default: return "Progress " + ratio;
            }
        }
        public static string Vitals(World world, Player player)
        {
            if (world.mode == "market-day") return "Checkout target " + world.goal + "   |   Complaints " + world.misses + " / 4";
            if (world.mode == "hearth-rush") return "Service target " + world.goal + "   |   Missed " + world.misses + " / 3";
            string health = "HP " + (int)Math.Ceiling(player.hp);
            if (world.mode == "dead-orbit") return health + "   |   Oxygen " + (int)Math.Ceiling(player.oxygen) + "%";
            if (world.mode == "rift-runners" || world.mode == "thornvale" || world.mode == "emberwake-rebuilders" || world.mode == "fjordbound") return health + "   |   Weapon level " + player.level;
            string home = world.mode == "gatebreakers-guild" ? "Portal" : world.mode == "skyhold-siege" ? "Core" : world.mode == "stormline-express" ? "Train" : "Refuge";
            return health + "   |   " + home + " " + (int)Math.Ceiling(world.baseHealth) + "%";
        }
        public static string Inventory(World world, Player player)
        {
            if (world.mode == "market-day") return "Till " + player.coins + " coins   |   " + (player.carried == "" ? "Hands empty" : "Carrying " + player.carryCount + " " + Friendly(player.carried));
            if (world.mode == "hearth-rush") return player.carried == "" ? "Hands empty   |   Choose a recipe at the pantry" : "Carrying " + Friendly(player.carried);
            string supplies = "Food " + player.food + "   Timber " + player.wood + "   Metal " + player.metal;
            if (world.mode == "thornvale") return "Herbs " + player.herbs + "   |   " + supplies;
            if (world.mode == "emberwake-rebuilders") return "Relics " + player.relics + "   |   " + supplies;
            if (world.mode == "fjordbound") return "Unbanked plunder " + player.relics + "   |   " + supplies;
            return supplies + "   Ammo " + player.ammo;
        }
        public static string Situation(World world)
        {
            if (world.mode == "hearth-rush")
            {
                var text = new StringBuilder("Orders: "); int count = 0;
                foreach (var order in world.tasks) if (order.kind == "order" && order.status == "open")
                {
                    if (count++ > 0) text.Append("   /   ");
                    text.Append(Friendly(order.item)).Append(' ').Append(Clock(order.remaining));
                }
                return count == 0 ? "No open orders. Clear the stations and prepare for the next ticket." : text.ToString();
            }
            if (world.mode == "market-day")
            {
                var text = new StringBuilder("Shelf stock: ");
                foreach (var shelf in world.structures) if (shelf.kind.StartsWith("shelf-", StringComparison.Ordinal)) text.Append(Friendly(shelf.kind.Substring(6))).Append(' ').Append(shelf.stock).Append("   ");
                return text.ToString().TrimEnd();
            }
            if (world.mode == "stormline-express")
            {
                var engine = world.structures.Find(s => s.kind == "train-engine");
                return "Train " + Friendly(world.stage) + "   |   Fuel " + (engine == null ? 0 : engine.stock) + "   |   Steam pressure " + (int)Math.Ceiling(world.pressure) + "%" + (world.pressure > 85 ? " - vent at the steam valve" : "");
            }
            if (world.mode == "fjordbound") return "Longship: " + (world.stage == "raiding" ? "on the raid shore; return to bank carried plunder" : "in harbour; interact to launch your next raid");
            if (world.mode == "rift-runners") return "Keep inside the storm ring. Aim before firing; cover blocks shots.";
            return "Threats remaining " + Threats(world) + "   |   Journal has the full chapter objective and field guide";
        }
        public static string Context(World world, Player player, string interactionChoice, string binding)
        {
            if (player.dead) return "You are down. A teammate can revive you with 1 food.";
            // Match the production auto-target order, radius, strict ties and revive priority.
            foreach (var peer in world.players) if (peer.connected && peer.dead && peer.id != player.id && Simulation.Distance(player.x, player.z, peer.x, peer.z) <= Simulation.InteractionRange)
                return binding + ": Revive teammate - 1 food";
            float distance = Simulation.InteractionRange + .01f; string target = "";
            foreach (var item in world.pickups) if (item.active)
            {
                float next = Simulation.Distance(player.x, player.z, item.x, item.z);
                if (next < distance) { distance = next; target = "Collect " + item.amount + " " + Friendly(item.kind); }
            }
            foreach (var task in world.tasks) if (task.status == "open" && task.kind != "order")
            {
                float next = Simulation.Distance(player.x, player.z, task.x, task.z);
                if (next < distance) { distance = next; target = task.label; }
            }
            foreach (var structure in world.structures) if (structure.hp > 0)
            {
                float next = Simulation.Distance(player.x, player.z, structure.x, structure.z);
                if (next < distance) { distance = next; target = Station(world, player, structure, interactionChoice); }
            }
            return target == "" || distance > Simulation.InteractionRange ? "Move closer to a station, supply or marked objective to interact." : binding + ": " + target;
        }
        static string CarryNext(Player player)
        {
            if (player.carried.StartsWith("cooked:", StringComparison.Ordinal)) return "serve " + player.carried.Substring(7) + " at the serving counter";
            if (player.carried.StartsWith("chopped:", StringComparison.Ordinal)) return "take chopped " + player.carried.Substring(8) + " to an empty pan";
            if (player.carried.StartsWith("raw:", StringComparison.Ordinal)) return "take raw " + player.carried.Substring(4) + " to a chopping board";
            if (player.carried.StartsWith("stock:", StringComparison.Ordinal)) return "deliver " + player.carried.Substring(6) + " to its matching shelf";
            return player.carried == "" ? "collect ingredients at the pantry" : "clear your hands at the sink";
        }
        static string Station(World world, Player player, Structure station, string choice)
        {
            string kind = station.kind;
            if (kind == "pan" || kind == "chopping-board")
            {
                string name = Friendly(kind);
                if (station.item == "")
                {
                    string prefix = kind == "pan" ? "chopped:" : "raw:";
                    if (player.carried.StartsWith(prefix, StringComparison.Ordinal)) return (kind == "pan" ? "Cook " : "Chop ") + player.carried.Substring(prefix.Length) + (kind == "pan" ? " - ready in 6s; collect before 16s" : " - ready in 3s");
                    return name + " empty - " + CarryNext(player);
                }
                if (player.carried != "") return "Hands full - " + CarryNext(player);
                if (station.item.StartsWith("burnt:", StringComparison.Ordinal)) return "Clear " + Friendly(station.item) + "; discard at the sink";
                float ready = kind == "pan" ? 6 : 3;
                if (station.progress < ready) return name + ": " + Friendly(station.item) + " ready in " + Clock(ready - station.progress);
                return "Collect " + (kind == "pan" ? "cooked " : "chopped ") + station.item + (kind == "pan" ? " - burns in " + Clock(16 - station.progress) : " - ready");
            }
            if (kind == "pantry") return player.carried == "" ? "Take " + (choice == "" ? "ingredients for the oldest open order" : Friendly(choice) + " ingredients") : "Hands full - " + CarryNext(player);
            if (kind == "sink") return "Wash dirty dishes or discard ingredients to clear your hands";
            if (kind == "serving-counter") return player.carried.StartsWith("cooked:", StringComparison.Ordinal) ? "Serve " + player.carried.Substring(7) + " for a matching order" : "Serving counter - " + CarryNext(player);
            if (kind == "supplier") return player.carried == "" ? "Buy " + (choice == "" ? "groceries" : choice) + " stock - 3 coins" : "Hands full - " + CarryNext(player);
            if (kind.StartsWith("shelf-", StringComparison.Ordinal)) return "Restock " + Friendly(kind.Substring(6)) + " - stock " + station.stock + " / 9";
            if (kind == "checkout")
            {
                int waiting = 0; foreach (var customer in world.enemies) if (customer.alive && customer.kind == "customer" && customer.state == "checkout" && Simulation.Distance(customer.x, customer.z, station.x, station.z) < 1.2f) waiting++;
                return waiting > 0 ? "Check out shopper - earn 6 coins (" + waiting + " waiting)" : "Checkout - no shopper ready yet";
            }
            if (kind == "airlock") return station.active ? "Open airlock - increases oxygen loss" : "Seal airlock - conserves oxygen";
            if (kind == "oxygen-station") return "Refill oxygen - " + station.stock + " charges left";
            if (kind == "tower" || kind == "bolt-tower" || kind == "longhouse") return "Upgrade " + Friendly(kind) + " (level " + station.level + ") - 2 timber + 2 metal";
            if (kind == "longship") return world.stage == "harbour" ? "Launch the longship to the raid shore" : "Return to harbour and bank the crew's plunder";
            if (kind == "train-engine") return choice == "fuel" ? "Load fuel - 1 timber adds 8 fuel" : (station.active ? "Stop" : "Start") + " the relief train - fuel " + station.stock;
            if (kind == "steam-valve") return "Vent steam - pressure " + (int)Math.Ceiling(world.pressure) + "%";
            if (kind == "dungeon-gate") return station.active ? "Open the dungeon gate after clearing its guards" : "Dungeon gate is open";
            if (kind == "community" || kind == "safehouse" || kind == "clinic" || kind == "hearth" || kind == "cargo") return "Rest and supply " + Friendly(kind) + " - 1 food";
            if (kind == "spike-trap" || kind == "barrier" || kind == "barricade" || kind == "portal" || kind == "sky-core") return "Repair " + Friendly(kind) + " - 1 metal";
            return Friendly(kind) + " - scenery";
        }
    }
}
