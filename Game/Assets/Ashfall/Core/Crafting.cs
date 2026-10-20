using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    public sealed class Ingredient
    {
        public string item; public int count;   // item id, or "#tag" for any item with the tag
        public Ingredient(string item, int count) { this.item = item; this.count = count; }
        public bool IsTag { get { return item.StartsWith("#"); } }
        public string Tag { get { return item.Substring(1); } }
        public string Label { get { return (IsTag ? Recipe.TagName(Tag) : ItemCatalog.Get(item).name) + (count > 1 ? " ×" + count : ""); } }
    }

    public sealed class Recipe
    {
        public string id, name, description, result = ""; public int resultCount = 1;
        public PropKind place; public bool places, needsHeat;
        public Ingredient[] inputs = new Ingredient[0];
        public string[] tools = new string[0];
        public int water;                 // clean water units consumed from any water container
        public float time = 4; public Skill skill = Skill.Carpentry; public float xp = 4; public int minLevel;
        public string[] anyOf = new string[0]; public int anyCount;   // e.g. stew ingredients

        public static string TagName(string tag)
        {
            switch (tag)
            {
                case "hammer": return "Hammer"; case "saw": return "Saw"; case "knife": return "Knife"; case "fire": return "Lighter or Matches";
                case "pot": return "Cooking Pot"; case "chop": return "Axe"; case "can-opener": return "Can Opener"; case "rippable": return "Clothing or Sheet";
                default: return tag;
            }
        }

        public static readonly Recipe[] All =
        {
            new Recipe { id = "rip_sheet", name = "Rip Bed Sheet", description = "Tear a sheet into strips usable as bandages.", inputs = new[] { new Ingredient("sheet", 1) }, result = "rag", resultCount = 4, time = 3, skill = Skill.FirstAid, xp = 1 },
            new Recipe { id = "rip_tshirt", name = "Rip T-Shirt", description = "Two strips of cloth.", inputs = new[] { new Ingredient("tshirt", 1) }, result = "rag", resultCount = 2, time = 2, skill = Skill.FirstAid, xp = 1 },
            new Recipe { id = "rip_hoodie", name = "Rip Hoodie", description = "Three strips of cloth.", inputs = new[] { new Ingredient("hoodie", 1) }, result = "rag", resultCount = 3, time = 3, skill = Skill.FirstAid, xp = 1 },
            new Recipe { id = "saw_log", name = "Saw Planks", description = "Saw a log into three planks.", inputs = new[] { new Ingredient("log", 1) }, tools = new[] { "saw" }, result = "plank", resultCount = 3, time = 7, xp = 6 },
            new Recipe { id = "spear", name = "Crafted Spear", description = "Whittle branches into a long reach weapon.", inputs = new[] { new Ingredient("branch", 2) }, tools = new[] { "knife" }, result = "spear", time = 6, xp = 4 },
            new Recipe { id = "nailed_bat", name = "Spiked Bat", description = "Drive nails through a baseball bat.", inputs = new[] { new Ingredient("baseball_bat", 1), new Ingredient("nails", 6) }, tools = new[] { "hammer" }, result = "nailed_bat", time = 6, xp = 6 },
            new Recipe { id = "campfire", name = "Campfire", description = "Build a fire outdoors for cooking, boiling water and light.", inputs = new[] { new Ingredient("branch", 3) }, tools = new[] { "fire" }, places = true, place = PropKind.Campfire, time = 6, skill = Skill.Foraging, xp = 3 },
            new Recipe { id = "campfire_planks", name = "Campfire (planks)", description = "Build a fire from planks.", inputs = new[] { new Ingredient("plank", 2) }, tools = new[] { "fire" }, places = true, place = PropKind.Campfire, time = 6, skill = Skill.Foraging, xp = 3 },
            new Recipe { id = "rain_collector", name = "Rain Collector", description = "A barrel lined with a garbage bag. Collects clean rain water.", inputs = new[] { new Ingredient("plank", 4), new Ingredient("nails", 4), new Ingredient("garbage_bag", 1) }, tools = new[] { "hammer" }, places = true, place = PropKind.RainCollector, time = 12, xp = 12, minLevel = 1 },
            new Recipe { id = "wood_crate", name = "Wooden Crate", description = "Storage for your safehouse.", inputs = new[] { new Ingredient("plank", 3), new Ingredient("nails", 3) }, tools = new[] { "hammer" }, places = true, place = PropKind.WoodCrate, time = 10, xp = 10 },
            new Recipe { id = "stew", name = "Hearty Stew", description = "Pot, two cups of clean water and any two vegetables or meats, over a heat source.", tools = new[] { "pot" }, water = 2, needsHeat = true, result = "stew", time = 10, skill = Skill.Cooking, xp = 10,
                anyOf = new[] { "carrots", "potato", "steak", "chicken", "mushrooms", "eggs", "rice", "berries", "steak_cooked", "chicken_cooked", "potato_baked",
                    "tomato", "cabbage", "fish_perch", "fish_bass", "fish_catfish", "fish_perch_cooked", "fish_bass_cooked", "fish_catfish_cooked" }, anyCount = 2 },
            new Recipe { id = "first_aid_kit", name = "First Aid Kit", description = "Bundle bandages and disinfectant.", inputs = new[] { new Ingredient("bandage", 2), new Ingredient("disinfectant", 1), new Ingredient("duct_tape", 1) }, result = "first_aid_kit", time = 4, skill = Skill.FirstAid, xp = 4 },
        };
        public static Recipe Get(string id) { return All.FirstOrDefault(r => r.id == id); }

        public int CleanWater(Inventory inv) { return inv.items.Where(s => s.Def.waterCapacity > 0 && !s.tainted).Sum(s => s.water); }

        /// <summary>Returns a human-readable list of missing requirements, empty when craftable.</summary>
        public List<string> Missing(Survivor s, bool nearHeat)
        {
            var missing = new List<string>(); var inv = s.inventory;
            foreach (var i in inputs) { int have = i.IsTag ? inv.CountTag(i.Tag) : inv.Count(i.item); if (have < i.count) missing.Add(i.Label); }
            foreach (var t in tools) if (inv.FindTag(t) == null) missing.Add(TagName(t));
            if (water > 0 && CleanWater(inv) < water) missing.Add(water + " clean water");
            if (anyCount > 0 && inv.items.Where(x => anyOf.Contains(x.def)).Sum(x => x.count) < anyCount) missing.Add(anyCount + " vegetables or meat");
            if (needsHeat && !nearHeat) missing.Add("Lit stove or campfire nearby");
            if (s.Level(skill) < minLevel) missing.Add(skill + " level " + minLevel);
            return missing;
        }
    }
}
