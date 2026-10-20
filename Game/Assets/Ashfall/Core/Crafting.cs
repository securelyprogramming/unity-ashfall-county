using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>
    /// Base crafting, State of Decay style: recipes run at the home base from the shared stock (Food, Meds, Ammo,
    /// Materials, Fuel) plus a few carried items, and need a facility (Workshop, Infirmary or Storage). The host
    /// validates every request (<see cref="Simulation.WorkshopProblem"/>); the UI lists <see cref="Recipes"/>.
    /// </summary>
    public sealed class WorkshopRecipe
    {
        public string id = "", name = "", description = "";
        public int[] cost = new int[5];                         // community stock used, indexed by (int)Resource
        public Ingredient[] items = new Ingredient[0];          // carried items consumed (e.g. plague samples)
        public FacilityKind facility = FacilityKind.Workshop; public int level = 1;
        public string result = ""; public int resultCount = 1;  // item made; "" for services (repair, draw ammo)
        public float xp = 4; public Skill skill = Skill.Carpentry;

        public int Cost(Resource r) { return cost[(int)r]; }
        /// <summary>"2 Materials, 1 Fuel" style cost label (stock and items).</summary>
        public string CostLabel
        {
            get
            {
                var parts = new List<string>();
                for (int i = 0; i < cost.Length; i++) if (cost[i] > 0) parts.Add(cost[i] + " " + (Resource)i);
                foreach (var i in items) parts.Add(ItemCatalog.Exists(i.item) ? i.Label : i.item + (i.count > 1 ? " ×" + i.count : ""));
                return parts.Count == 0 ? "free" : string.Join(", ", parts);
            }
        }
        public string FacilityLabel { get { return Community.FacilityName(facility) + (level > 1 ? " level " + level : ""); } }

        static WorkshopRecipe R(string id, string name, string description, FacilityKind facility, int level, string result, int count, int food = 0, int meds = 0, int ammo = 0, int materials = 0, int fuel = 0, Ingredient[] items = null, Skill skill = Skill.Carpentry)
        {
            return new WorkshopRecipe { id = id, name = name, description = description, facility = facility, level = level, result = result, resultCount = count, cost = new[] { food, meds, ammo, materials, fuel }, items = items ?? new Ingredient[0], skill = skill };
        }

        static readonly WorkshopRecipe[] all =
        {
            R("repair_weapon", "Repair Weapons", "Restore every melee weapon you carry to full condition.", FacilityKind.Workshop, 1, "", 0, materials: 2),
            R("bandage", "Bandages", "Three clean bandages from the medical stock.", FacilityKind.Workshop, 1, "bandage", 3, meds: 1, skill: Skill.FirstAid),
            R("molotov", "Molotov Cocktail", "A bottle of fuel and a rag. Sets an area on fire.", FacilityKind.Workshop, 1, "molotov", 2, materials: 1, fuel: 1),
            R("pipe_bomb", "Pipe Bomb", "Beeps to lure the dead, then explodes.", FacilityKind.Workshop, 1, "pipe_bomb", 1, ammo: 1, materials: 2),
            R("grenade", "Frag Grenade", "Military-grade blast. Needs a level 2 Workshop.", FacilityKind.Workshop, 2, "grenade", 1, ammo: 2, materials: 2),
            R("draw_ammo", "Draw Ammunition", "Rounds for the firearm you are holding (or a pistol) from the ammo stock.", FacilityKind.Storage, 1, "", 0, ammo: 1),
            R("plague_cure", "Plague Cure", "Synthesize a blood plague cure from plague samples.", FacilityKind.Infirmary, 1, "plague_cure", 1, meds: 1, items: new[] { new Ingredient("plague_sample", 3) }, skill: Skill.FirstAid),
        };
        /// <summary>Every base recipe, in display order.</summary>
        public static IReadOnlyList<WorkshopRecipe> Recipes { get { return all; } }
        public static WorkshopRecipe Get(string id) { return all.FirstOrDefault(r => r.id == id); }
        /// <summary>Rounds drawn per Ammo unit, by ammo item.</summary>
        public static int RoundsPerAmmo(string ammo) { return ammo == "shells" ? 8 : ammo == "ammo_308" ? 10 : ammo == "ammo_556" ? 20 : 15; }
    }

    public sealed class Ingredient
    {
        public string item; public int count;   // item id, or "#tag" for any item with the tag
        public Ingredient(string item, int count) { this.item = item; this.count = count; }
        public bool IsTag { get { return item.StartsWith("#"); } }
        public string Tag { get { return item.Substring(1); } }
        public string Label { get { return (IsTag ? Recipe.TagName(Tag) : ItemCatalog.Get(item).name) + (count > 1 ? " ×" + count : ""); } }
    }

    /// <summary>
    /// Field crafting from carried items (Command.Craft). Only improvised dressings remain; the Zomboid recipes
    /// (cooking, campfires, rain collectors, carpentry) were replaced by <see cref="WorkshopRecipe"/> at the base.
    /// Retired ids still resolve through <see cref="Get"/> but always report why they are unavailable.
    /// </summary>
    public sealed class Recipe
    {
        public string id, name, description, result = ""; public int resultCount = 1;
        public PropKind place; public bool places, needsHeat;
        public Ingredient[] inputs = new Ingredient[0];
        public string[] tools = new string[0];
        public int water;                 // clean water units consumed from any water container
        public float time = 4; public Skill skill = Skill.Carpentry; public float xp = 4; public int minLevel;
        public string[] anyOf = new string[0]; public int anyCount;
        public string retired = "";       // non-empty: no longer craftable, with the reason shown to the player

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
        };
        static readonly Recipe[] retiredRecipes =
        {
            Retired("stew", "Hearty Stew", "The base Kitchen feeds the community now; eat food as you find it."),
            Retired("campfire", "Campfire", "Camp cooking is gone; rest and eat at the home base."),
            Retired("campfire_planks", "Campfire (planks)", "Camp cooking is gone; rest and eat at the home base."),
            Retired("rain_collector", "Rain Collector", "Water no longer needs collecting."),
            Retired("wood_crate", "Wooden Crate", "Base storage is a facility now (Storage)."),
            Retired("saw_log", "Saw Planks", "Bring Materials rucksacks to base instead."),
            Retired("spear", "Crafted Spear", "Weapons are repaired and made at the base Workshop."),
            Retired("nailed_bat", "Spiked Bat", "Weapons are repaired and made at the base Workshop."),
            Retired("first_aid_kit", "First Aid Kit", "Medical supplies come from the base Infirmary and Workshop."),
        };
        static Recipe Retired(string id, string name, string why) { return new Recipe { id = id, name = name, description = why, retired = why }; }
        public static Recipe Get(string id) { return All.FirstOrDefault(r => r.id == id) ?? retiredRecipes.FirstOrDefault(r => r.id == id); }

        public int CleanWater(Inventory inv) { return inv.items.Where(s => s.Def.waterCapacity > 0 && !s.tainted).Sum(s => s.water); }

        /// <summary>Returns a human-readable list of missing requirements, empty when craftable.</summary>
        public List<string> Missing(Survivor s, bool nearHeat)
        {
            var missing = new List<string>(); var inv = s.inventory;
            if (retired.Length > 0) { missing.Add(retired); return missing; }
            foreach (var i in inputs) { int have = i.IsTag ? inv.CountTag(i.Tag) : inv.Count(i.item); if (have < i.count) missing.Add(i.Label); }
            foreach (var t in tools) if (inv.FindTag(t) == null) missing.Add(TagName(t));
            if (s.Level(skill) < minLevel) missing.Add(skill + " level " + minLevel);
            return missing;
        }
    }
}
