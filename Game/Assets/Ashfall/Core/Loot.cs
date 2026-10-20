using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    /// <summary>Weighted loot tables. Containers roll lazily, the first time anyone opens them, from the world seed.</summary>
    public static class Loot
    {
        struct Entry { public string id; public float weight; public int min, max; public Entry(string id, float weight, int min = 1, int max = 1) { this.id = id; this.weight = weight; this.min = min; this.max = max; } }
        sealed class Table { public int rollsMin, rollsMax; public float emptyChance; public Entry[] entries; }
        static readonly Dictionary<string, Table> tables = new Dictionary<string, Table>();
        static void T(string name, int rollsMin, int rollsMax, float empty, params Entry[] entries) { tables[name] = new Table { rollsMin = rollsMin, rollsMax = rollsMax, emptyChance = empty, entries = entries }; }
        static Entry E(string id, float w, int min = 1, int max = 1) { return new Entry(id, w, min, max); }

        static Loot()
        {
            T("kitchen", 1, 4, .25f, E("canned_beans", 6), E("canned_soup", 6), E("canned_corn", 5), E("canned_peaches", 4), E("cereal", 5), E("crackers", 5), E("peanut_butter", 3), E("rice", 4),
                E("kitchen_knife", 4), E("can_opener", 4), E("cooking_pot", 3), E("frying_pan", 3), E("matches", 4), E("lighter", 2), E("water_bottle", 5), E("coffee", 3), E("garbage_bag", 3, 1, 4), E("dog_food", 1));
            T("fridge", 1, 4, .2f, E("milk", 5), E("cheese", 5), E("eggs", 5), E("steak", 4), E("chicken", 4), E("apple", 4), E("carrots", 4), E("bread", 3), E("soda", 5), E("juice", 4), E("water_bottle", 3));
            T("stove", 0, 1, .7f, E("cooking_pot", 4), E("frying_pan", 3));
            T("bathroom", 1, 3, .3f, E("bandage", 6, 1, 3), E("disinfectant", 4), E("painkillers", 5), E("antibiotics", 1), E("rag", 3, 1, 3), E("suture_kit", 1), E("first_aid_kit", 1));
            T("bedroom", 1, 3, .3f, E("sheet", 6), E("tshirt", 6), E("hoodie", 5), E("jeans", 5), E("leather_jacket", 1), E("school_bag", 3), E("duffel_bag", 1), E("flashlight", 3), E("battery", 3, 1, 3),
                E("chocolate", 3), E("magazine", 4), E("pistol", .6f), E("ammo_9mm", .8f, 6, 24), E("baseball_bat", 1.5f), E("boots", 2), E("radio", 1));
            T("wardrobe", 1, 3, .2f, E("hoodie", 5), E("tshirt", 6), E("jeans", 6), E("leather_jacket", 1.5f), E("boots", 2), E("work_gloves", 1.5f), E("sheet", 4), E("hiking_bag", .6f), E("school_bag", 2), E("golf_club", 1), E("shotgun", .25f), E("shells", .5f, 4, 12));
            T("living", 0, 2, .45f, E("magazine", 5), E("chips", 4), E("chocolate", 3), E("battery", 3, 1, 4), E("lighter", 2), E("soda", 3), E("county_map", 2), E("flashlight", 1), E("duct_tape", 1));
            T("office", 0, 2, .5f, E("magazine", 3), E("granola", 3), E("chocolate", 2), E("soda", 2), E("screwdriver", 2), E("battery", 2, 1, 3), E("duct_tape", 2), E("county_map", 2), E("radio", .6f));
            T("tools", 1, 4, .15f, E("hammer", 5), E("saw", 4), E("nails", 6, 10, 60), E("screwdriver", 4), E("crowbar", 2), E("axe", 1.5f), E("shovel", 2), E("plank", 3, 1, 3), E("duct_tape", 3), E("work_gloves", 2), E("gas_can", 1.5f), E("garbage_bag", 2, 1, 5), E("flashlight", 2), E("pipe", 2));
            T("bin", 0, 2, .55f, E("garbage_bag", 3), E("water_bottle", 3), E("tshirt", 1), E("magazine", 2), E("plank", 1));
            T("grocery", 2, 5, .1f, E("canned_beans", 6), E("canned_soup", 6), E("canned_corn", 6), E("canned_peaches", 5), E("chips", 6), E("crackers", 6), E("cereal", 6), E("peanut_butter", 4), E("chocolate", 5), E("granola", 5),
                E("rice", 5), E("potato", 4), E("dog_food", 2), E("water_bottle", 6), E("water_jug", 2), E("soda", 5), E("juice", 4), E("can_opener", 2), E("matches", 2), E("garbage_bag", 2, 2, 6), E("battery", 2, 2, 6));
            T("grocery_cold", 2, 4, .1f, E("milk", 6), E("cheese", 6), E("eggs", 5), E("steak", 5), E("chicken", 5), E("soda", 6), E("juice", 6), E("bread", 4), E("apple", 4), E("banana", 4), E("carrots", 4));
            T("pharmacy", 2, 4, .1f, E("bandage", 6, 1, 4), E("disinfectant", 5), E("painkillers", 6), E("antibiotics", 4), E("suture_kit", 3), E("first_aid_kit", 2), E("rag", 2, 2, 4), E("chocolate", 2), E("water_bottle", 2), E("granola", 2));
            T("hardware", 2, 4, .1f, E("hammer", 5), E("saw", 5), E("nails", 8, 30, 100), E("plank", 5, 2, 4), E("axe", 3), E("crowbar", 3), E("shovel", 3), E("screwdriver", 4), E("duct_tape", 4), E("work_gloves", 4), E("flashlight", 4), E("battery", 4, 2, 8),
                E("garbage_bag", 3, 2, 10), E("gas_can", 2), E("pipe", 2), E("machete", 1), E("water_jug", 2));
            T("diner", 1, 3, .2f, E("bread", 4), E("steak", 3), E("chicken", 3), E("eggs", 3), E("potato", 4), E("cheese", 3), E("kitchen_knife", 3), E("cooking_pot", 2), E("frying_pan", 2), E("soda", 4), E("coffee", 4), E("canned_soup", 3));
            T("gas", 1, 3, .2f, E("chips", 6), E("chocolate", 6), E("granola", 4), E("soda", 6), E("water_bottle", 5), E("lighter", 4), E("matches", 3), E("county_map", 4), E("battery", 3, 2, 6), E("flashlight", 2), E("duct_tape", 2), E("gas_can", 2), E("magazine", 3));
            T("gas_cold", 2, 4, .1f, E("soda", 8), E("juice", 5), E("water_bottle", 6), E("milk", 2));
            T("cashier", 0, 2, .5f, E("chocolate", 3), E("lighter", 3), E("county_map", 2), E("battery", 2, 1, 4), E("revolver", .4f), E("ammo_38", .6f, 4, 12));
            T("police", 1, 3, .1f, E("pistol", 5), E("ammo_9mm", 7, 15, 45), E("shotgun", 3), E("shells", 5, 6, 24), E("police_vest", 4), E("helmet", 2), E("flashlight", 4), E("battery", 3, 2, 6), E("first_aid_kit", 2), E("bandage", 3, 1, 3), E("radio", 2), E("rifle", 1), E("ammo_308", 1.5f, 5, 15));
            T("clothing", 1, 4, .1f, E("hoodie", 6), E("tshirt", 7), E("jeans", 7), E("leather_jacket", 3), E("boots", 4), E("work_gloves", 3), E("school_bag", 4), E("duffel_bag", 3), E("hiking_bag", 2), E("helmet", .5f));
            T("barn", 1, 4, .2f, E("plank", 5, 1, 4), E("nails", 5, 10, 40), E("hammer", 3), E("saw", 3), E("axe", 2), E("shovel", 3), E("log", 3), E("potato", 3, 1, 4), E("carrots", 3, 1, 4), E("rifle", .6f), E("ammo_308", 1, 4, 10), E("garbage_bag", 2, 1, 4), E("work_gloves", 2), E("gas_can", 2));
            T("car", 0, 3, .4f, E("water_bottle", 4), E("chips", 3), E("county_map", 3), E("flashlight", 2), E("battery", 2, 1, 3), E("duffel_bag", 1.5f), E("school_bag", 1), E("first_aid_kit", 1), E("crowbar", 1.5f), E("gas_can", 1), E("tshirt", 1), E("hoodie", 1), E("revolver", .3f), E("ammo_38", .5f, 3, 12), E("soda", 2));
            T("corpse", 0, 2, .45f, E("chocolate", 2), E("lighter", 2), E("bandage", 1.5f), E("tshirt", 3), E("jeans", 3), E("hoodie", 2), E("water_bottle", 1.5f), E("kitchen_knife", 1), E("county_map", .5f), E("ammo_9mm", .5f, 2, 10), E("radio", .3f), E("school_bag", .6f));
        }

        public static void Roll(Container c, ulong seed, float abundance)
        {
            c.generated = true;
            Table t; if (!tables.TryGetValue(c.table ?? "", out t)) return;
            var rng = new Rng(seed ^ (ulong)(c.id * 2654435761L), 91);
            if (rng.Chance(t.emptyChance / Math.Max(.2f, abundance))) return;
            int rolls = rng.Range(t.rollsMin, t.rollsMax + 1);
            rolls = Math.Max(0, (int)Math.Round(rolls * abundance));
            for (int i = 0; i < rolls; i++) c.inventory.Add(RollEntry(t, rng));
        }
        public static void RollInto(Inventory inv, string table, Rng rng, float abundance)
        {
            Table t; if (!tables.TryGetValue(table, out t)) return;
            if (rng.Chance(t.emptyChance / Math.Max(.2f, abundance))) return;
            int rolls = Math.Max(0, (int)Math.Round(rng.Range(t.rollsMin, t.rollsMax + 1) * abundance));
            for (int i = 0; i < rolls; i++) inv.Add(RollEntry(t, rng));
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
        public static IEnumerable<string> ItemsIn(string name) { Table t; if (tables.TryGetValue(name, out t)) foreach (var e in t.entries) yield return e.id; }
    }
}
