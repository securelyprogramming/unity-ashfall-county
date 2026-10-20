using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    public enum ItemCategory { Food, Drink, Medical, Weapon, Firearm, Ammo, Tool, Material, Clothing, Bag, Light, Misc }
    public enum WearSlot { None, Torso, Legs, Hands, Feet, Head, Back }

    /// <summary>Static item definition. Values are game-owned balance data, never client supplied.</summary>
    public sealed class ItemDef
    {
        public string id, name, icon = "";
        public ItemCategory category;
        public float weight = .2f;
        // Food and drink: negative values reduce the need, in 0..100 need units.
        public float hunger, thirst, boredom, fatigue;
        public float perishDays;            // 0 = shelf stable
        public bool raw;                    // eating raw risks food poisoning
        public string cooksInto, needsTool = "";
        // Water containers.
        public int waterCapacity;
        // Medical.
        public float heal, bandageQuality, disinfect, painRelief, antibiotic;
        // Weapons.
        public float damage, range = 1.2f, arc = 60, swingTime = .9f, endurance = 4, knockdown = .25f, conditionLoss = .02f;
        public bool blade, twoHanded;
        public string ammo = ""; public int magazine, pellets = 1; public float noise = 6, spread = 4, accuracy = .7f;
        // Tool tags such as "hammer", "saw", "chop", "can-opener", "pot", "fire", "knife".
        public string[] tags = new string[0];
        // Clothing and bags.
        public WearSlot slot;
        public float scratchProtection, biteProtection, warmth, capacity;
        // Lights.
        public float lightRange, batteryHours;
        // Skill books: reading multiplies experience for a skill while its level is in [bookMinLevel, bookMaxLevel).
        public int bookSkill = -1, bookMinLevel, bookMaxLevel; public float bookMultiplier = 1;
        public int stack = 1;
        // Open-county contract: loot rarity, consumable effects, armour, rucksacks and throwables.
        public int tier;                    // 0 common, 1 uncommon, 2 rare, 3 military/airdrop
        public float stamina;               // consumable: endurance restored
        public float armor;                 // worn: fraction of damage absorbed (vests, helmets)
        public int resource = -1;           // rucksack: (int)Resource delivered to the community stock, -1 = not a rucksack
        public float resourceAmount;        // rucksack: stock units delivered
        public float blastRadius, blastDamage; public float fuse;   // throwables (grenade, molotov, pipe bomb)
        public float plagueCure;            // consumable: blood plague removed (0..100)
        public bool Has(string tag) { return Array.IndexOf(tags, tag) >= 0; }
        public bool Edible { get { return category == ItemCategory.Food || category == ItemCategory.Drink; } }
        public bool IsWeapon { get { return category == ItemCategory.Weapon || category == ItemCategory.Firearm || damage > 0; } }
    }

    [Serializable]
    public sealed class ItemStack
    {
        public int uid;
        public string def;
        public int count = 1;
        public float condition = 1;   // weapons, clothing, flashlights' battery
        public int water;             // water container units
        public bool tainted;          // untreated water
        public int loaded;            // rounds in a firearm
        public float ageHours;        // food age
        public bool cooked;
        public ItemDef Def { get { return ItemCatalog.Get(def); } }
        public float Weight { get { var d = Def; return d.weight * count + water * .1f; } }
        public float Freshness { get { var d = Def; return d.perishDays <= 0 ? 1 : MathUtil.Clamp01(1 - ageHours / (d.perishDays * 24)); } }
        public bool Rotten { get { var d = Def; return d.perishDays > 0 && ageHours > d.perishDays * 24 * 1.5f; } }
        public bool Stale { get { var d = Def; return d.perishDays > 0 && ageHours > d.perishDays * 24; } }
        public string Label
        {
            get
            {
                var d = Def; string label = d.name;
                if (count > 1) label += " ×" + count;
                if (d.waterCapacity > 0) label += water == 0 ? " (empty)" : " (" + water + "/" + d.waterCapacity + (tainted ? ", tainted" : "") + ")";
                if (d.magazine > 0) label += " [" + loaded + "/" + d.magazine + "]";
                if (Rotten) label = "Rotten " + label; else if (Stale) label = "Stale " + label;
                if (d.IsWeapon && d.category != ItemCategory.Firearm && condition < .995f) label += " " + (int)(condition * 100) + "%";
                if (d.category == ItemCategory.Light) label += " " + (int)(condition * 100) + "%";
                if (d.Has("fishing") && condition < .995f) label += " " + (int)(condition * 100) + "%";
                if (d.id == "gas_can") label += condition <= .001f ? " (empty)" : " (" + (condition * Vehicle.GasCanLiters).ToString("0.#") + " L)";
                return label;
            }
        }
        public ItemStack Clone() { return (ItemStack)MemberwiseClone(); }
    }

    public static class ItemCatalog
    {
        static readonly Dictionary<string, ItemDef> defs = new Dictionary<string, ItemDef>();
        public static IEnumerable<ItemDef> All { get { return defs.Values; } }
        public static ItemDef Get(string id)
        {
            ItemDef d; if (id != null && defs.TryGetValue(id, out d)) return d;
            throw new KeyNotFoundException("Unknown item: " + id);
        }
        public static bool Exists(string id) { return id != null && defs.ContainsKey(id); }
        static ItemDef Add(string id, string name, ItemCategory category, float weight, Action<ItemDef> configure = null)
        {
            var d = new ItemDef { id = id, name = name, category = category, weight = weight, icon = id };
            if (configure != null) configure(d);
            defs.Add(id, d); return d;
        }
        static ItemDef Food(string id, string name, float weight, float hunger, float thirst = 0, float perish = 0, Action<ItemDef> more = null)
        {
            return Add(id, name, ItemCategory.Food, weight, d => { d.hunger = -hunger; d.thirst = thirst; d.perishDays = perish; if (more != null) more(d); });
        }
        static ItemDef Melee(string id, string name, float weight, float damage, float range, float swing, float endurance, bool blade, float conditionLoss, Action<ItemDef> more = null)
        {
            return Add(id, name, ItemCategory.Weapon, weight, d => { d.damage = damage; d.range = range; d.swingTime = swing; d.endurance = endurance; d.blade = blade; d.conditionLoss = conditionLoss; d.noise = 5; if (more != null) more(d); });
        }
        static ItemDef Gun(string id, string name, float weight, float damage, string ammo, int magazine, float range, float noise, float accuracy, Action<ItemDef> more = null)
        {
            return Add(id, name, ItemCategory.Firearm, weight, d => { d.damage = damage; d.ammo = ammo; d.magazine = magazine; d.range = range; d.noise = noise; d.accuracy = accuracy; d.swingTime = .35f; d.endurance = .5f; d.arc = 10; if (more != null) more(d); });
        }
        static ItemDef Wear(string id, string name, WearSlot slot, float weight, float scratch, float bite, float warmth)
        {
            return Add(id, name, ItemCategory.Clothing, weight, d => { d.slot = slot; d.scratchProtection = scratch; d.biteProtection = bite; d.warmth = warmth; });
        }

        static ItemCatalog()
        {
            // Shelf-stable food. Canned food needs a can opener (or a knife, slowly).
            Food("canned_beans", "Canned Beans", .6f, 30, 0, 0, d => { d.needsTool = "can-opener"; });
            Food("canned_soup", "Canned Soup", .6f, 25, -5, 0, d => { d.needsTool = "can-opener"; });
            Food("canned_corn", "Canned Corn", .6f, 20, 0, 0, d => { d.needsTool = "can-opener"; });
            Food("canned_peaches", "Canned Peaches", .6f, 18, -6, 0, d => { d.needsTool = "can-opener"; d.boredom = -5; });
            Food("dog_food", "Dog Food", .6f, 25, 0, 0, d => { d.needsTool = "can-opener"; d.boredom = 15; });
            Food("chips", "Bag of Chips", .2f, 10, 5);
            Food("crackers", "Crackers", .3f, 12, 4);
            Food("cereal", "Box of Cereal", .4f, 18, 2);
            Food("peanut_butter", "Peanut Butter", .5f, 30, 6);
            Food("chocolate", "Chocolate Bar", .1f, 8, 2, 0, d => { d.boredom = -15; });
            Food("granola", "Granola Bar", .1f, 10);
            Food("rice", "Bag of Rice", .5f, 6, 8, 0, d => { d.raw = true; d.cooksInto = "rice_cooked"; });
            Food("rice_cooked", "Cooked Rice", .5f, 35, 0, 3);
            // Perishables; refrigerators slow spoilage while the grid is up.
            Food("apple", "Apple", .2f, 10, -4, 6);
            Food("banana", "Banana", .2f, 12, -2, 4);
            Food("carrots", "Carrots", .2f, 8, -2, 8);
            Food("potato", "Potato", .3f, 12, 0, 14, d => { d.raw = true; d.cooksInto = "potato_baked"; });
            Food("potato_baked", "Baked Potato", .3f, 22, 0, 4);
            Food("bread", "Loaf of Bread", .4f, 25, 3, 5);
            Food("cheese", "Cheese", .3f, 18, 2, 10);
            Food("eggs", "Eggs", .3f, 10, 0, 10, d => { d.raw = true; d.cooksInto = "eggs_cooked"; });
            Food("eggs_cooked", "Scrambled Eggs", .3f, 18, 0, 3);
            Food("steak", "Raw Steak", .4f, 20, 0, 2, d => { d.raw = true; d.cooksInto = "steak_cooked"; });
            Food("steak_cooked", "Grilled Steak", .4f, 38, 2, 3);
            Food("chicken", "Raw Chicken", .4f, 18, 0, 2, d => { d.raw = true; d.cooksInto = "chicken_cooked"; });
            Food("chicken_cooked", "Roast Chicken", .4f, 34, 2, 3);
            Food("berries", "Wild Berries", .1f, 6, -3, 3);
            Food("mushrooms", "Wild Mushrooms", .1f, 6, 0, 3);
            Food("stew", "Hearty Stew", .9f, 55, -10, 3, d => { d.boredom = -20; });
            Add("milk", "Carton of Milk", ItemCategory.Drink, .6f, d => { d.thirst = -25; d.hunger = -8; d.perishDays = 4; });
            Add("soda", "Can of Soda", ItemCategory.Drink, .3f, d => { d.thirst = -18; d.boredom = -5; });
            Add("juice", "Juice Box", ItemCategory.Drink, .2f, d => { d.thirst = -15; d.hunger = -3; });
            Add("water_bottle", "Water Bottle", ItemCategory.Drink, .1f, d => { d.waterCapacity = 10; d.tags = new[] { "water" }; });
            Add("water_jug", "Water Jug", ItemCategory.Drink, .3f, d => { d.waterCapacity = 30; d.tags = new[] { "water" }; });
            Add("coffee", "Instant Coffee", ItemCategory.Drink, .1f, d => { d.thirst = -4; d.fatigue = -20; });

            // Medical.
            Add("bandage", "Bandage", ItemCategory.Medical, .1f, d => { d.bandageQuality = 1; d.stack = 6; });
            Add("rag", "Ripped Sheet", ItemCategory.Medical, .1f, d => { d.bandageQuality = .55f; d.stack = 10; d.tags = new[] { "rag" }; });
            Add("disinfectant", "Disinfectant", ItemCategory.Medical, .3f, d => { d.disinfect = 1; });
            Add("painkillers", "Painkillers", ItemCategory.Medical, .1f, d => { d.painRelief = 45; });
            Add("antibiotics", "Antibiotics", ItemCategory.Medical, .1f, d => { d.antibiotic = 1; });
            Add("suture_kit", "Suture Kit", ItemCategory.Medical, .1f, d => { d.heal = 1; });
            Add("first_aid_kit", "First Aid Kit", ItemCategory.Medical, .6f, d => { d.bandageQuality = 1.2f; d.disinfect = 1; d.heal = 12; });

            // Melee weapons: damage is in zombie hit points (zombies have ~1.0..1.6 hp).
            Melee("kitchen_knife", "Kitchen Knife", .3f, .45f, 1.0f, .55f, 2, true, .06f, d => { d.tags = new[] { "knife" }; d.arc = 40; d.knockdown = .05f; });
            Melee("hunting_knife", "Hunting Knife", .4f, .6f, 1.0f, .55f, 2, true, .03f, d => { d.tags = new[] { "knife" }; d.arc = 40; d.knockdown = .05f; });
            Melee("baseball_bat", "Baseball Bat", 1.4f, .8f, 1.55f, 1.0f, 5, false, .025f, d => { d.knockdown = .45f; d.twoHanded = true; });
            Melee("nailed_bat", "Spiked Bat", 1.6f, 1.05f, 1.55f, 1.0f, 5, false, .03f, d => { d.knockdown = .5f; d.twoHanded = true; });
            Melee("crowbar", "Crowbar", 1.6f, .9f, 1.35f, 1.0f, 5, false, .006f, d => { d.knockdown = .4f; d.tags = new[] { "pry" }; });
            Melee("hammer", "Hammer", .8f, .6f, 1.1f, .8f, 3, false, .02f, d => { d.knockdown = .3f; d.tags = new[] { "hammer" }; });
            Melee("axe", "Axe", 2.2f, 1.35f, 1.5f, 1.25f, 7, true, .015f, d => { d.knockdown = .55f; d.twoHanded = true; d.tags = new[] { "chop" }; });
            Melee("machete", "Machete", 1.1f, 1.1f, 1.4f, .9f, 4, true, .02f, d => { d.knockdown = .2f; });
            Melee("golf_club", "Golf Club", 1.0f, .65f, 1.6f, .95f, 4, false, .05f, d => { d.knockdown = .35f; d.twoHanded = true; });
            Melee("frying_pan", "Frying Pan", 1.2f, .6f, 1.1f, .9f, 4, false, .02f, d => { d.knockdown = .45f; d.tags = new[] { "pot" }; });
            Melee("pipe", "Lead Pipe", 1.5f, .8f, 1.35f, 1.0f, 5, false, .012f, d => { d.knockdown = .4f; });
            Melee("shovel", "Shovel", 2.0f, .9f, 1.75f, 1.2f, 6, false, .02f, d => { d.knockdown = .55f; d.twoHanded = true; d.tags = new[] { "dig" }; });
            Melee("spear", "Crafted Spear", 1.0f, .85f, 2.1f, 1.0f, 4, true, .07f, d => { d.knockdown = .1f; d.twoHanded = true; d.arc = 25; });
            Melee("plank", "Plank", 1.0f, .45f, 1.5f, 1.0f, 5, false, .09f, d => { d.knockdown = .35f; d.twoHanded = true; d.category = ItemCategory.Material; d.stack = 1; d.tags = new[] { "plank" }; });

            // Firearms and ammunition (count = rounds).
            Gun("pistol", "M9 Pistol", 1.0f, 1.25f, "ammo_9mm", 15, 14, 38, .62f);
            Gun("revolver", "Revolver", 1.1f, 1.7f, "ammo_38", 6, 15, 42, .66f);
            Gun("shotgun", "Pump Shotgun", 3.2f, .75f, "shells", 6, 9, 55, .8f, d => { d.pellets = 6; d.spread = 18; d.twoHanded = true; d.swingTime = .9f; });
            Gun("rifle", "Hunting Rifle", 3.4f, 3.0f, "ammo_308", 5, 26, 60, .8f, d => { d.twoHanded = true; d.swingTime = 1.2f; d.spread = 1.5f; });
            Add("ammo_9mm", "9mm Rounds", ItemCategory.Ammo, .012f, d => { d.stack = 200; });
            Add("ammo_38", ".38 Rounds", ItemCategory.Ammo, .012f, d => { d.stack = 200; });
            Add("shells", "Shotgun Shells", ItemCategory.Ammo, .03f, d => { d.stack = 100; });
            Add("ammo_308", ".308 Rounds", ItemCategory.Ammo, .025f, d => { d.stack = 100; });

            // Tools and materials.
            Add("saw", "Saw", ItemCategory.Tool, .8f, d => { d.tags = new[] { "saw" }; });
            Add("screwdriver", "Screwdriver", ItemCategory.Tool, .2f, d => { d.tags = new[] { "screwdriver" }; d.damage = .3f; d.range = 1; d.blade = true; d.swingTime = .55f; });
            Add("can_opener", "Can Opener", ItemCategory.Tool, .1f, d => { d.tags = new[] { "can-opener" }; });
            Add("cooking_pot", "Cooking Pot", ItemCategory.Tool, .8f, d => { d.tags = new[] { "pot" }; });
            Add("lighter", "Lighter", ItemCategory.Tool, .05f, d => { d.tags = new[] { "fire" }; });
            Add("matches", "Matches", ItemCategory.Tool, .05f, d => { d.tags = new[] { "fire" }; });
            Add("nails", "Nails", ItemCategory.Material, .01f, d => { d.stack = 200; d.tags = new[] { "nails" }; });
            Add("log", "Log", ItemCategory.Material, 4f, d => { d.tags = new[] { "log" }; });
            Add("branch", "Tree Branch", ItemCategory.Material, .5f, d => { d.stack = 8; d.tags = new[] { "fuel" }; });
            Add("sheet", "Bed Sheet", ItemCategory.Material, .4f, d => { d.tags = new[] { "rippable" }; });
            Add("garbage_bag", "Garbage Bag", ItemCategory.Material, .05f, d => { d.stack = 10; });
            Add("duct_tape", "Duct Tape", ItemCategory.Material, .1f);
            Add("gas_can", "Gas Can", ItemCategory.Material, 1.5f, d => { d.tags = new[] { "fuel" }; });   // condition = fill level, 1 = 10 L
            Add("car_key", "Car Key", ItemCategory.Misc, .02f, d => { d.tags = new[] { "car-key" }; });
            Add("battery", "Battery", ItemCategory.Material, .05f, d => { d.stack = 12; });
            Add("radio", "Hand Radio", ItemCategory.Misc, .5f, d => { d.tags = new[] { "radio" }; });
            Add("county_map", "Ashfall County Map", ItemCategory.Misc, .05f, d => { d.tags = new[] { "map" }; });
            Add("magazine", "Magazine", ItemCategory.Misc, .1f, d => { d.boredom = -30; });
            Add("flashlight", "Flashlight", ItemCategory.Light, .4f, d => { d.lightRange = 12; d.batteryHours = 10; });

            // Clothing and bags.
            Wear("hoodie", "Hoodie", WearSlot.Torso, .6f, .15f, 0, .5f).tags = new[] { "rippable" };
            Wear("tshirt", "T-Shirt", WearSlot.Torso, .2f, .05f, 0, .15f).tags = new[] { "rippable" };
            Wear("leather_jacket", "Leather Jacket", WearSlot.Torso, 1.2f, .5f, .25f, .7f);
            Wear("police_vest", "Police Vest", WearSlot.Torso, 1.5f, .55f, .3f, .3f);
            Wear("jeans", "Jeans", WearSlot.Legs, .5f, .25f, .05f, .4f);
            Wear("work_gloves", "Work Gloves", WearSlot.Hands, .1f, .4f, .1f, .2f);
            Wear("boots", "Work Boots", WearSlot.Feet, .6f, .3f, .2f, .3f);
            Wear("helmet", "Riot Helmet", WearSlot.Head, .8f, .6f, .5f, .1f);
            Add("school_bag", "School Backpack", ItemCategory.Bag, .4f, d => { d.slot = WearSlot.Back; d.capacity = 10; });
            Add("duffel_bag", "Duffel Bag", ItemCategory.Bag, .7f, d => { d.slot = WearSlot.Back; d.capacity = 16; });
            Add("hiking_bag", "Hiking Bag", ItemCategory.Bag, .8f, d => { d.slot = WearSlot.Back; d.capacity = 20; });

            // Farming: seed packets plant one plot each; produce grows over several in-game days.
            Food("tomato", "Tomato", .2f, 8, -5, 6);
            Food("cabbage", "Cabbage", .8f, 20, -3, 12);
            foreach (var crop in new[] { new[] { "carrot", "Carrot" }, new[] { "potato", "Potato" }, new[] { "tomato", "Tomato" }, new[] { "cabbage", "Cabbage" } })
                Add("seeds_" + crop[0], crop[1] + " Seeds", ItemCategory.Misc, .02f, d => { d.stack = 20; d.tags = new[] { "seeds" }; });
            Add("watering_can", "Watering Can", ItemCategory.Tool, .8f, d => { d.waterCapacity = 20; d.tags = new[] { "watering" }; });

            // Fishing: a rod plus worms (consumed) or a lure (sometimes lost). Fish must be cooked.
            Add("fishing_rod", "Fishing Rod", ItemCategory.Tool, 1.0f, d => { d.tags = new[] { "fishing" }; });
            Add("worms", "Worms", ItemCategory.Misc, .01f, d => { d.stack = 30; d.tags = new[] { "bait" }; });
            Add("lure", "Fishing Lure", ItemCategory.Misc, .02f, d => { d.stack = 10; d.tags = new[] { "lure" }; });
            Food("fish_perch", "Raw Perch", .3f, 8, 0, 2, d => { d.raw = true; d.cooksInto = "fish_perch_cooked"; });
            Food("fish_perch_cooked", "Grilled Perch", .3f, 18, 2, 3);
            Food("fish_bass", "Raw Bass", .6f, 14, 0, 2, d => { d.raw = true; d.cooksInto = "fish_bass_cooked"; });
            Food("fish_bass_cooked", "Grilled Bass", .6f, 30, 2, 3);
            Food("fish_catfish", "Raw Catfish", 1.2f, 22, 0, 2, d => { d.raw = true; d.cooksInto = "fish_catfish_cooked"; });
            Food("fish_catfish_cooked", "Grilled Catfish", 1.2f, 46, 2, 3);

            Add("cigarettes", "Cigarettes", ItemCategory.Misc, .01f, d => { d.stack = 20; d.tags = new[] { "smoke" }; });

            // Skill books: Vol. 1 triples experience for levels 0-4, Vol. 2 quadruples it for levels 5-9.
            foreach (var skill in new[] { Skill.Carpentry, Skill.Cooking, Skill.FirstAid, Skill.Foraging, Skill.Farming, Skill.Fishing })
            {
                var sk = skill; string title = sk == Skill.FirstAid ? "First Aid" : sk.ToString();
                Add(BookId(sk, 1), title + " Handbook, Vol. 1", ItemCategory.Misc, .4f, d => { d.bookSkill = (int)sk; d.bookMinLevel = 0; d.bookMaxLevel = 5; d.bookMultiplier = 3; d.tags = new[] { "book" }; });
                Add(BookId(sk, 2), title + " Handbook, Vol. 2", ItemCategory.Misc, .5f, d => { d.bookSkill = (int)sk; d.bookMinLevel = 5; d.bookMaxLevel = 10; d.bookMultiplier = 4; d.tags = new[] { "book" }; });
            }
        }
        public static string BookId(Skill skill, int volume) { return "book_" + skill.ToString().ToLowerInvariant() + "_" + volume; }

        public static int Uid;
        public static ItemStack Make(string id, int count = 1, Rng rng = null)
        {
            var d = Get(id);
            var s = new ItemStack { uid = ++Uid, def = id, count = Math.Max(1, Math.Min(count, d.stack)) };
            if (d.waterCapacity > 0) s.water = rng == null ? d.waterCapacity : rng.Range(0, d.waterCapacity + 1);
            if (d.category == ItemCategory.Firearm && rng != null) s.loaded = rng.Chance(.4f) ? rng.Range(1, d.magazine + 1) : 0;
            if (d.IsWeapon && d.category != ItemCategory.Firearm && rng != null) s.condition = rng.Range(.55f, 1f);
            if (d.category == ItemCategory.Light && rng != null) s.condition = rng.Range(.3f, 1f);
            if (d.perishDays > 0 && rng != null) s.ageHours = rng.Range(0, d.perishDays * 24 * .4f);
            return s;
        }
    }

    /// <summary>A list of stacks with a weight capacity, used by survivors, containers, corpses and the floor.</summary>
    [Serializable]
    public sealed class Inventory
    {
        public List<ItemStack> items = new List<ItemStack>();
        public float capacity = 50;
        public float Weight { get { float w = 0; foreach (var s in items) w += s.Weight; return w; } }
        public ItemStack Find(string def) { return items.FirstOrDefault(s => s.def == def); }
        public ItemStack FindTag(string tag) { return items.FirstOrDefault(s => s.Def.Has(tag)); }
        public ItemStack ByUid(int uid) { return items.FirstOrDefault(s => s.uid == uid); }
        public int Count(string def) { int n = 0; foreach (var s in items) if (s.def == def) n += s.count; return n; }
        public int CountTag(string tag) { int n = 0; foreach (var s in items) if (s.Def.Has(tag)) n += s.count; return n; }
        public void Add(ItemStack stack)
        {
            var d = stack.Def;
            if (d.stack > 1)
                foreach (var s in items)
                    if (s.def == stack.def && s.count < d.stack && Math.Abs(s.ageHours - stack.ageHours) < 12 && !s.cooked && !stack.cooked)
                    {
                        int moved = Math.Min(stack.count, d.stack - s.count); s.count += moved; stack.count -= moved;
                        if (stack.count == 0) return;
                    }
            items.Add(stack);
        }
        /// <summary>Removes count units of an item definition; returns false (and removes nothing) if unavailable.</summary>
        public bool Consume(string def, int count)
        {
            if (Count(def) < count) return false;
            for (int i = items.Count - 1; i >= 0 && count > 0; i--)
            {
                var s = items[i]; if (s.def != def) continue;
                int used = Math.Min(count, s.count); s.count -= used; count -= used;
                if (s.count == 0) items.RemoveAt(i);
            }
            return true;
        }
        public bool ConsumeTag(string tag, int count)
        {
            if (CountTag(tag) < count) return false;
            for (int i = items.Count - 1; i >= 0 && count > 0; i--)
            {
                var s = items[i]; if (!s.Def.Has(tag)) continue;
                int used = Math.Min(count, s.count); s.count -= used; count -= used;
                if (s.count == 0) items.RemoveAt(i);
            }
            return true;
        }
        public ItemStack Take(int uid, int count)
        {
            var s = ByUid(uid); if (s == null) return null;
            if (count >= s.count) { items.Remove(s); return s; }
            var split = s.Clone(); split.uid = ++ItemCatalog.Uid; split.count = count; s.count -= count; return split;
        }
    }
}
