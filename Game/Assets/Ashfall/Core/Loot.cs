using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    /// <summary>Weighted loot tables. Containers roll lazily, the first time anyone opens them, from the world seed.
    /// Besides its item rolls, a table can carry rucksack chances: community stock found by building type (Community.cs).</summary>
    public static class Loot
    {
        struct Entry { public string id; public float weight; public int min, max; public Entry(string id, float weight, int min = 1, int max = 1) { this.id = id; this.weight = weight; this.min = min; this.max = max; } }
        sealed class Table { public int rollsMin, rollsMax; public float emptyChance; public Entry[] entries; public Entry[] rucksacks = new Entry[0]; }
        static readonly Dictionary<string, Table> tables = new Dictionary<string, Table>();
        static void T(string name, int rollsMin, int rollsMax, float empty, params Entry[] entries) { tables[name] = new Table { rollsMin = rollsMin, rollsMax = rollsMax, emptyChance = empty, entries = entries }; }
        static Entry E(string id, float w, int min = 1, int max = 1) { return new Entry(id, w, min, max); }
        /// <summary>Rucksack chances per container of a table (weight = probability).</summary>
        static void R(string name, params Entry[] chances) { tables[name].rucksacks = chances; }
        static Entry Food(float p) { return E("rucksack_food", p); }
        static Entry Meds(float p) { return E("rucksack_meds", p); }
        static Entry Ammo(float p) { return E("rucksack_ammo", p); }
        static Entry Materials(float p) { return E("rucksack_materials", p); }
        static Entry Fuel(float p) { return E("rucksack_fuel", p); }
        static Entry[] Books(float weight)
        {
            var skills = new[] { Skill.Carpentry, Skill.Cooking, Skill.FirstAid, Skill.Foraging, Skill.Farming, Skill.Fishing }; var e = new Entry[skills.Length * 2];
            for (int i = 0; i < skills.Length; i++) { e[i * 2] = E(ItemCatalog.BookId(skills[i], 1), weight); e[i * 2 + 1] = E(ItemCatalog.BookId(skills[i], 2), weight * .5f); }
            return e;
        }
        static Entry[] With(Entry[] a, params Entry[] b) { var all = new Entry[a.Length + b.Length]; a.CopyTo(all, 0); b.CopyTo(all, a.Length); return all; }

        static Loot()
        {
            // Homes. Food restores stamina and health; Zomboid-only items (books, fishing, seeds, water jugs) are rare.
            T("kitchen", 1, 3, .3f, E("canned_beans", 6), E("canned_soup", 6), E("canned_corn", 5), E("canned_peaches", 4), E("cereal", 5), E("crackers", 5), E("peanut_butter", 3), E("rice", 2),
                E("kitchen_knife", 4), E("can_opener", 2), E("frying_pan", 2), E("matches", 2), E("lighter", 2), E("water_bottle", 2), E("coffee", 2), E("energy_drink", 1), E("garbage_bag", 1, 1, 3));
            T("fridge", 1, 3, .3f, E("milk", 4), E("cheese", 5), E("eggs", 4), E("steak", 3), E("chicken", 3), E("apple", 4), E("carrots", 3), E("bread", 3), E("soda", 5), E("juice", 4), E("energy_drink", 1.5f));
            T("stove", 0, 1, .7f, E("cooking_pot", 3), E("frying_pan", 3));
            T("bathroom", 1, 2, .35f, E("bandage", 6, 1, 3), E("disinfectant", 4), E("painkillers", 5), E("antibiotics", 1), E("rag", 2, 1, 3), E("suture_kit", 1), E("first_aid_kit", 1));
            T("bedroom", 1, 3, .35f, E("tshirt", 4), E("hoodie", 4), E("jeans", 4), E("leather_jacket", 1), E("backpack_1", 2.5f), E("school_bag", 1), E("duffel_bag", 1), E("flashlight", 3), E("battery", 2, 1, 3),
                E("chocolate", 3), E("pistol", .8f), E("ammo_9mm", 1.2f, 6, 24), E("revolver", .4f), E("ammo_38", .6f, 4, 12), E("baseball_bat", 2), E("boots", 2), E("radio", 1), E("helmet_1", .5f));
            T("wardrobe", 1, 3, .25f, E("hoodie", 4), E("tshirt", 4), E("jeans", 4), E("leather_jacket", 1.5f), E("boots", 2), E("work_gloves", 1.5f), E("backpack_2", .8f), E("backpack_1", 2), E("golf_club", 1),
                E("shotgun", .4f), E("shells", .8f, 4, 12), E("hunting_rifle", .15f), E("ammo_308", .4f, 4, 10), E("vest_1", .4f));
            T("living", 0, 2, .5f, E("chips", 4), E("chocolate", 3), E("battery", 3, 1, 4), E("lighter", 2), E("soda", 3), E("county_map", 2), E("flashlight", 1), E("duct_tape", 1), E("energy_drink", 1), E("magazine", 1));
            T("office", 0, 2, .5f, With(new[] { E("granola", 3), E("chocolate", 2), E("soda", 2), E("screwdriver", 2), E("battery", 2, 1, 3), E("duct_tape", 2), E("county_map", 2), E("radio", .6f), E("energy_drink", 1) }, Books(.06f)));
            T("tools", 1, 3, .2f, E("hammer", 5), E("saw", 4), E("nails", 6, 10, 60), E("screwdriver", 4), E("crowbar", 2), E("axe", 1.5f), E("shovel", 2), E("plank", 3, 1, 3), E("duct_tape", 3), E("work_gloves", 2),
                E("gas_can", 1.5f), E("flashlight", 2), E("pipe", 2), E("watering_can", .2f));
            T("bin", 0, 2, .6f, E("garbage_bag", 3), E("water_bottle", 1), E("tshirt", 1), E("plank", 1), E("rag", 1));
            // Shops.
            T("grocery", 2, 4, .15f, E("canned_beans", 6), E("canned_soup", 6), E("canned_corn", 6), E("canned_peaches", 5), E("chips", 6), E("crackers", 6), E("cereal", 6), E("peanut_butter", 4), E("chocolate", 5), E("granola", 5),
                E("rice", 3), E("potato", 3), E("dog_food", 1), E("water_bottle", 3), E("soda", 5), E("juice", 4), E("energy_drink", 3), E("can_opener", 2), E("matches", 2), E("battery", 2, 2, 6), E("cigarettes", .5f, 5, 20));
            T("grocery_cold", 2, 4, .15f, E("milk", 5), E("cheese", 6), E("eggs", 5), E("steak", 5), E("chicken", 5), E("soda", 6), E("juice", 6), E("bread", 4), E("apple", 4), E("banana", 4), E("carrots", 4), E("tomato", 2), E("energy_drink", 3));
            T("pharmacy", 2, 4, .1f, E("bandage", 6, 1, 4), E("disinfectant", 5), E("painkillers", 6), E("antibiotics", 4), E("suture_kit", 3), E("first_aid_kit", 2.5f), E("rag", 2, 2, 4), E("energy_drink", 2), E("granola", 2), E("book_firstaid_1", .2f));
            T("hardware", 2, 4, .1f, E("hammer", 5), E("saw", 5), E("nails", 8, 30, 100), E("plank", 5, 2, 4), E("axe", 3), E("crowbar", 3), E("shovel", 3), E("screwdriver", 4), E("duct_tape", 4), E("work_gloves", 4), E("flashlight", 4),
                E("battery", 4, 2, 8), E("garbage_bag", 2, 2, 10), E("gas_can", 2.5f), E("pipe", 2), E("machete", 1.5f), E("backpack_2", 1), E("seeds_tomato", .3f), E("book_carpentry_1", .2f));
            T("diner", 1, 3, .25f, E("bread", 4), E("steak", 3), E("chicken", 3), E("eggs", 3), E("potato", 3), E("cheese", 3), E("kitchen_knife", 3), E("frying_pan", 2), E("soda", 4), E("coffee", 4), E("canned_soup", 3), E("energy_drink", 1));
            T("gas", 1, 3, .2f, E("chips", 6), E("chocolate", 6), E("granola", 4), E("soda", 6), E("energy_drink", 4), E("lighter", 4), E("county_map", 4), E("battery", 3, 2, 6), E("flashlight", 2), E("duct_tape", 2), E("gas_can", 3), E("cigarettes", 1, 5, 20));
            T("gas_cold", 2, 4, .15f, E("soda", 8), E("juice", 5), E("energy_drink", 6), E("water_bottle", 2), E("milk", 1));
            T("cashier", 0, 2, .5f, E("chocolate", 3), E("lighter", 3), E("county_map", 2), E("battery", 2, 1, 4), E("revolver", .5f), E("ammo_38", .8f, 4, 12), E("cigarettes", 1, 5, 20));
            T("police", 1, 3, .1f, E("pistol", 5), E("ammo_9mm", 7, 15, 45), E("shotgun", 3), E("shells", 5, 6, 24), E("police_vest", 3), E("vest_2", 1.5f), E("helmet", 2), E("helmet_2", 1), E("smg", .6f), E("flashlight", 3),
                E("first_aid_kit", 2), E("bandage", 3, 1, 3), E("radio", 2), E("rifle", 1), E("ammo_308", 1.5f, 5, 15));
            T("clothing", 1, 3, .1f, E("hoodie", 5), E("tshirt", 6), E("jeans", 6), E("leather_jacket", 3), E("boots", 4), E("work_gloves", 3), E("backpack_1", 4), E("backpack_2", 2), E("duffel_bag", 1.5f), E("hiking_bag", 1), E("helmet_1", 1), E("vest_1", .8f));
            // Farms and vehicles.
            T("barn", 1, 3, .25f, E("plank", 5, 1, 4), E("nails", 5, 10, 40), E("hammer", 3), E("saw", 3), E("axe", 2), E("shovel", 3), E("log", 2), E("potato", 3, 1, 4), E("carrots", 3, 1, 4), E("rifle", .8f), E("hunting_rifle", .3f),
                E("ammo_308", 1.2f, 4, 10), E("work_gloves", 2), E("gas_can", 2.5f), E("watering_can", .3f), E("seeds_potato", .3f, 1, 3));
            T("car", 0, 2, .45f, E("water_bottle", 2), E("chips", 3), E("county_map", 3), E("flashlight", 2), E("battery", 2, 1, 3), E("backpack_1", 1.5f), E("first_aid_kit", 1), E("crowbar", 1.5f), E("gas_can", 1.5f),
                E("hoodie", 1), E("revolver", .3f), E("ammo_38", .5f, 3, 12), E("soda", 2), E("energy_drink", 1.5f));
            T("corpse", 0, 2, .5f, E("chocolate", 2), E("lighter", 2), E("bandage", 1.5f), E("tshirt", 2), E("jeans", 2), E("hoodie", 2), E("water_bottle", 1), E("kitchen_knife", 1), E("county_map", .5f), E("ammo_9mm", .6f, 2, 10),
                E("radio", .3f), E("backpack_1", .5f), E("energy_drink", .6f), E("cigarettes", .5f, 1, 12));
            // Landmarks.
            T("military", 1, 4, .15f, E("assault_rifle", 1.5f), E("smg", 2), E("ammo_556", 5, 15, 60), E("vest_2", 2.5f), E("vest_3", 1), E("helmet_2", 2.5f), E("helmet_3", 1), E("grenade", 2, 1, 2), E("backpack_3", 1),
                E("pistol", 1.5f), E("ammo_9mm", 2, 15, 45), E("first_aid_kit", 1.5f), E("mre", 2, 1, 3), E("energy_drink", 1.5f), E("hunting_knife", 1), E("flashlight", .5f), E("radio", .5f), E("battery", .5f, 2, 6));
            T("armory", 2, 4, .05f, E("assault_rifle", 3), E("smg", 2.5f), E("hunting_rifle", 1.5f), E("pistol", 2), E("ammo_556", 6, 30, 90), E("ammo_9mm", 4, 30, 60), E("ammo_308", 2, 10, 30), E("grenade", 3, 1, 3),
                E("vest_3", 1.5f), E("helmet_3", 1.5f), E("vest_2", 2), E("helmet_2", 2));
            // Airdrops hold rare and military gear only (tier 2-3).
            T("airdrop", 4, 6, 0, E("assault_rifle", 3), E("hunting_rifle", 2), E("smg", 2), E("ammo_556", 5, 30, 90), E("vest_2", 3), E("vest_3", 2), E("helmet_2", 3), E("helmet_3", 2), E("plague_cure", 2.5f),
                E("grenade", 4, 1, 3), E("backpack_3", 1.5f), E("plague_sample", .5f));
            T("raider", 1, 3, .25f, E("pistol", 2), E("revolver", 1.5f), E("shotgun", 2), E("smg", .8f), E("ammo_9mm", 3, 8, 30), E("shells", 3, 4, 16), E("ammo_38", 1.5f, 4, 12), E("molotov", 2.5f, 1, 2), E("pipe_bomb", 1.5f),
                E("vest_1", 2), E("helmet_1", 1.5f), E("machete", 2), E("nailed_bat", 2), E("canned_beans", 2), E("chips", 1), E("energy_drink", 1.5f), E("cigarettes", 1, 5, 20), E("painkillers", 1), E("gas_can", 1));
            T("hospital", 1, 3, .15f, E("bandage", 6, 1, 4), E("disinfectant", 4), E("painkillers", 5), E("antibiotics", 4), E("suture_kit", 3), E("first_aid_kit", 3), E("rag", 1, 1, 3), E("plague_sample", .6f), E("plague_cure", .25f), E("energy_drink", .5f));
            T("warehouse", 1, 3, .25f, E("nails", 6, 20, 100), E("plank", 5, 1, 4), E("duct_tape", 4), E("hammer", 2), E("saw", 2), E("crowbar", 1.5f), E("gas_can", 2), E("garbage_bag", 2, 2, 8), E("battery", 2, 2, 6),
                E("flashlight", 1.5f), E("work_gloves", 2), E("canned_beans", 2, 1, 3), E("canned_soup", 2, 1, 3), E("backpack_2", 1), E("axe", .8f));
            T("garage", 1, 3, .2f, E("gas_can", 4), E("crowbar", 2), E("hammer", 2), E("screwdriver", 3), E("duct_tape", 2), E("battery", 2, 2, 6), E("flashlight", 1.5f), E("work_gloves", 2), E("pipe", 1.5f), E("energy_drink", 1));
            T("hangar", 1, 3, .25f, E("gas_can", 3), E("screwdriver", 2), E("crowbar", 1.5f), E("duct_tape", 2), E("flashlight", 1.5f), E("battery", 2, 2, 6), E("backpack_2", .6f), E("energy_drink", 1), E("first_aid_kit", .5f));
            T("school", 0, 2, .45f, With(new[] { E("backpack_1", 3), E("school_bag", 1), E("chocolate", 3), E("granola", 2), E("juice", 2), E("battery", 1), E("flashlight", .5f), E("first_aid_kit", .4f) }, Books(.08f)));
            T("church", 1, 3, .3f, E("canned_beans", 3), E("canned_soup", 3), E("canned_corn", 3), E("crackers", 2), E("water_bottle", 1), E("bandage", 1.5f), E("painkillers", 1), E("lighter", 1));
            T("camp", 0, 3, .3f, E("canned_beans", 2), E("granola", 2), E("water_bottle", 2), E("lighter", 2), E("matches", 2), E("hunting_knife", 1.5f), E("flashlight", 1), E("backpack_2", 1), E("hunting_rifle", .3f),
                E("ammo_308", .6f, 3, 10), E("sheet", 1), E("fishing_rod", .3f), E("lure", .2f), E("worms", .2f, 2, 6));
            T("radio", 1, 3, .25f, E("radio", 4), E("battery", 4, 2, 8), E("flashlight", 2), E("duct_tape", 2), E("screwdriver", 2), E("energy_drink", 1), E("coffee", 1));
            // Body of an armed survivor (Humans.cs may roll it for dead NPCs).
            T("human", 1, 3, .1f, E("pistol", 2), E("revolver", 1), E("shotgun", 1), E("smg", .6f), E("hunting_rifle", .4f), E("ammo_9mm", 3, 5, 20), E("shells", 2, 3, 10), E("ammo_308", 1, 3, 10), E("bandage", 2, 1, 2),
                E("first_aid_kit", .8f), E("vest_1", 1), E("helmet_1", .6f), E("energy_drink", 1.5f), E("granola", 1.5f), E("canned_beans", 1), E("molotov", .6f), E("hunting_knife", 1), E("backpack_2", .6f), E("radio", .5f));

            // Rucksacks by building type (per searched container): roughly one per two to four searched buildings, more in landmarks.
            R("kitchen", Food(.09f)); R("fridge", Food(.05f)); R("church", Food(.12f)); R("camp", Food(.05f));
            R("grocery", Food(.05f)); R("grocery_cold", Food(.05f)); R("diner", Food(.12f));
            R("barn", Materials(.12f), Food(.08f));
            R("pharmacy", Meds(.08f)); R("hospital", Meds(.08f)); R("bathroom", Meds(.06f));
            R("police", Ammo(.12f)); R("military", Ammo(.12f)); R("armory", Ammo(.25f));
            R("hardware", Materials(.07f)); R("warehouse", Materials(.12f)); R("tools", Materials(.08f));
            R("gas", Fuel(.1f)); R("garage", Fuel(.15f)); R("hangar", Fuel(.12f)); R("car", Fuel(.03f));
            R("raider", Ammo(.05f), Food(.05f), Fuel(.04f));
            R("human", Food(.04f), Ammo(.04f), Meds(.03f));
        }

        public static void Roll(Container c, ulong seed, float abundance)
        {
            c.generated = true;
            Table t; if (!tables.TryGetValue(c.table ?? "", out t)) return;
            var rng = new Rng(seed ^ (ulong)(c.id * 2654435761L), 91);
            RollRucksacks(c.inventory, t, rng, abundance);
            if (rng.Chance(t.emptyChance / Math.Max(.2f, abundance))) return;
            int rolls = rng.Range(t.rollsMin, t.rollsMax + 1);
            rolls = Math.Max(0, (int)Math.Round(rolls * abundance));
            for (int i = 0; i < rolls; i++) c.inventory.Add(RollEntry(t, rng));
        }
        public static void RollInto(Inventory inv, string table, Rng rng, float abundance)
        {
            Table t; if (!tables.TryGetValue(table, out t)) return;
            RollRucksacks(inv, t, rng, abundance);
            if (rng.Chance(t.emptyChance / Math.Max(.2f, abundance))) return;
            int rolls = Math.Max(0, (int)Math.Round(rng.Range(t.rollsMin, t.rollsMax + 1) * abundance));
            for (int i = 0; i < rolls; i++) inv.Add(RollEntry(t, rng));
        }
        static void RollRucksacks(Inventory inv, Table t, Rng rng, float abundance)
        {
            foreach (var e in t.rucksacks) if (rng.Chance(e.weight * MathUtil.Clamp(abundance, .25f, 3))) inv.Add(ItemCatalog.Make(e.id, 1, rng));
        }
        static ItemStack RollEntry(Table t, Rng rng)
        {
            float total = 0; foreach (var e in t.entries) total += e.weight;
            float pick = rng.Float() * total;
            foreach (var e in t.entries) { pick -= e.weight; if (pick <= 0) return ItemCatalog.Make(e.id, rng.Range(e.min, e.max + 1), rng); }
            var last = t.entries[t.entries.Length - 1]; return ItemCatalog.Make(last.id, last.min, rng);
        }
        public static bool HasTable(string name) { return tables.ContainsKey(name); }
        public static IEnumerable<string> TableNames { get { return tables.Keys; } }
        /// <summary>Every item a table can produce, including its rucksacks.</summary>
        public static IEnumerable<string> ItemsIn(string name)
        {
            Table t; if (!tables.TryGetValue(name, out t)) yield break;
            foreach (var e in t.entries) yield return e.id;
            foreach (var e in t.rucksacks) yield return e.id;
        }
        /// <summary>Probability that one container of the table holds a rucksack (at abundance 1).</summary>
        public static float RucksackChance(string name)
        {
            Table t; if (!tables.TryGetValue(name ?? "", out t)) return 0;
            float none = 1; foreach (var e in t.rucksacks) none *= 1 - e.weight; return 1 - none;
        }
    }
}
