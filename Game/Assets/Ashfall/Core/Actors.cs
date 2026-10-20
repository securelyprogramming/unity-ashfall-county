using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    public enum BodyPart { Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg, Hands }
    public enum WoundKind { Scratch, Laceration, Bite, GlassCut, Burn }
    public enum Skill { Melee, Aiming, Carpentry, Cooking, FirstAid, Foraging, Sneaking, Fitness, Mechanics, Farming, Fishing }

    [Serializable]
    public sealed class Wound
    {
        public BodyPart part; public WoundKind kind;
        public float severity = 1;     // heals to 0
        public bool bleeding, bandaged, dirtyBandage, disinfected, infected, sutured;
        public float bandageAge;       // hours on the current bandage
        public string Label
        {
            get
            {
                string k = kind == WoundKind.GlassCut ? "Glass cut" : kind.ToString();
                return Part(part) + ": " + k + (bleeding ? ", bleeding" : "") + (bandaged ? dirtyBandage ? ", dirty bandage" : ", bandaged" : "") + (infected ? ", infected" : "") + (sutured ? ", stitched" : "");
            }
        }
        public static string Part(BodyPart p) { switch (p) { case BodyPart.LeftArm: return "Left arm"; case BodyPart.RightArm: return "Right arm"; case BodyPart.LeftLeg: return "Left leg"; case BodyPart.RightLeg: return "Right leg"; default: return p.ToString(); } }
    }

    public sealed class Profession
    {
        public string id, name, description;
        public Dictionary<Skill, int> skills = new Dictionary<Skill, int>();
        public string[] kit = new string[0];
        public static readonly Profession[] All =
        {
            new Profession { id = "paramedic", name = "Paramedic", description = "First Aid +3. Bandages work faster and better.", skills = { { Skill.FirstAid, 3 } }, kit = new[] { "bandage", "bandage", "disinfectant" } },
            new Profession { id = "carpenter", name = "Carpenter", description = "Carpentry +3. Starts with a hammer and nails.", skills = { { Skill.Carpentry, 3 } }, kit = new[] { "hammer", "nails" } },
            new Profession { id = "deputy", name = "Sheriff's Deputy", description = "Aiming +3, Melee +1. Starts with a pistol.", skills = { { Skill.Aiming, 3 }, { Skill.Melee, 1 } }, kit = new[] { "pistol", "ammo_9mm" } },
            new Profession { id = "farmer", name = "Farmer", description = "Foraging +3, Farming +2, Cooking +1. Finds more in the wild and grows food faster.", skills = { { Skill.Foraging, 3 }, { Skill.Farming, 2 }, { Skill.Cooking, 1 } }, kit = new[] { "shovel", "potato", "seeds_carrot", "seeds_potato", "watering_can" } },
            new Profession { id = "firefighter", name = "Firefighter", description = "Fitness +2, Melee +1. Starts with an axe.", skills = { { Skill.Fitness, 2 }, { Skill.Melee, 1 } }, kit = new[] { "axe" } },
            new Profession { id = "cook", name = "Line Cook", description = "Cooking +3. Starts with a knife and a pot.", skills = { { Skill.Cooking, 3 } }, kit = new[] { "kitchen_knife", "cooking_pot" } },
            new Profession { id = "unemployed", name = "Unemployed", description = "No head start. Learns every skill 25% faster.", kit = new string[0] },
        };
        public static Profession Get(string id) { return All.FirstOrDefault(p => p.id == id) ?? All[0]; }
    }

    public enum ActionKind
    {
        None, Loot, Eat, Drink, Bandage, Disinfect, Suture, Barricade, Unbarricade, Climb, BreakWindow, ClearGlass, Craft, Sleep, Reload,
        Forage, Chop, Cook, Boil, FillWater, Pry, Read, Stomp, Wear, Unwear, Rest, Harvest, Light, AddFuel, Pills, Hotwire, Refuel,
        DigPlot, Plant, WaterPlot, Fish, ReadBook, Smoke
    }

    [Serializable]
    public sealed class TimedAction
    {
        public ActionKind kind; public float duration, elapsed;
        public int target = -1, item = -1, extra = -1;   // opening/prop/container ids, item uid
        public string recipe = "";
        public bool movable;
        public float Progress { get { return duration <= 0 ? 1 : MathUtil.Clamp01(elapsed / duration); } }
    }

    /// <summary>How a survivor is driven. None = an owner's input (the local player); every other role is a host AI brain
    /// that submits the same InputState/Command requests a player would (Humans.cs).</summary>
    public enum AiRole : byte { None, Companion, Resident, Rival, Enclave, Raider, Trader, Hunter }

    [Serializable]
    public sealed partial class Survivor
    {
        public int id; public string name = "Mara Vale", profession = "paramedic";
        // Community/faction contract (shared by Humans.cs, Community.cs and the views).
        public int faction;                 // 0 = the player's community; other ids index World.factions (Humans.cs)
        public AiRole ai = AiRole.None;     // None = driven by the owner's input
        public string character = "";       // named character key (Characters in Humans.cs), "" for generic survivors
        public bool offscreen;              // far from every player-controlled survivor: simulated abstractly by Humans.cs
        public int lastHitBy = -1;          // survivor id that last damaged this survivor (-1 = none/zombie)
        public bool female = true;
        public Vec2 pos; public float facing, speed;
        public bool running, sneaking, aiming, alive = true, sleeping, inVehicle;
        public float health = 100, hunger = 10, thirst = 10, fatigue = 10, endurance = 100, panic, pain, boredom = 10, sickness, wetness, stress;
        public bool zombieInfected; public float infectionHours;
        public float knockedDown, attackCooldown, shoveCooldown, footstepClock, hurtFlash, swingClock;
        public int kills, primary = -1, secondary = -1;   // item uids in inventory
        public bool lightOn;
        public Dictionary<WearSlot, int> worn = new Dictionary<WearSlot, int>();
        public Inventory inventory = new Inventory { capacity = 15 };
        public List<Wound> wounds = new List<Wound>();
        public float[] xp = new float[Enum.GetValues(typeof(Skill)).Length];
        public int[] level = new int[Enum.GetValues(typeof(Skill)).Length];
        public TimedAction action;
        public double spawnedAt, diedAt;
        public string causeOfDeath = "";
        public int lootContainer = -1;  // container the survivor has open
        public int vehicle = -1;        // vehicle the survivor is driving (inVehicle mirrors it)
        public float wellFedHours;
        public List<string> traits = new List<string>();      // chosen at creation, validated by the host
        public List<string> booksRead = new List<string>();   // skill book item ids
        public float hoursSinceSmoke;

        public const float Radius = .3f;
        public bool HasTrait(string id) { return traits.Contains(id); }
        public ItemStack Primary { get { return primary < 0 ? null : inventory.ByUid(primary); } }
        public ItemStack Secondary { get { return secondary < 0 ? null : inventory.ByUid(secondary); } }
        public ItemStack Worn(WearSlot slot) { int uid; return worn.TryGetValue(slot, out uid) ? inventory.ByUid(uid) : null; }
        public bool IsEquipped(int uid) { return primary == uid || secondary == uid || worn.ContainsValue(uid); }
        public int Level(Skill s) { return level[(int)s]; }
        public float Capacity
        {
            get { var bag = Worn(WearSlot.Back); return 12 + (bag == null ? 0 : bag.Def.capacity) + Level(Skill.Fitness) * .5f + (HasTrait("strong") ? 4 : HasTrait("weak") ? -4 : 0); }
        }
        /// <summary>Equipped clothing and bags weigh less than items carried loose.</summary>
        public float CarriedWeight
        {
            get { float w = 0; foreach (var s in inventory.items) w += IsEquipped(s.uid) && s.Def.slot != WearSlot.None ? s.Weight * .3f : s.Weight; return w; }
        }
        public float Encumbrance { get { return CarriedWeight / Math.Max(1, Capacity); } }
        public float Protection(bool bite)
        {
            float p = 0; foreach (var kv in worn) { var s = inventory.ByUid(kv.Value); if (s == null) continue; p = Math.Max(p, bite ? s.Def.biteProtection * s.condition : s.Def.scratchProtection * s.condition); }
            return p;
        }
        public bool Bleeding { get { return wounds.Any(w => w.bleeding); } }
        public bool HasTag(string tag) { return inventory.FindTag(tag) != null; }

        public static readonly float[] XpCurve = { 75, 150, 300, 600, 900, 1200, 1500, 1800, 2100, 2400 };
    }

    public enum ZombieState : byte { Idle, Wander, Investigate, Chase, Attack, Thump, Climb, Knocked, Dead, Lunge, Eating }

    [Serializable]
    public sealed partial class Zombie
    {
        public int id; public Vec2 pos, target; public float facing;
        // Bestiary contract (Bestiary.cs): kind decides stats/behaviour; plague zombies spread blood plague.
        public ZombieKind kind = ZombieKind.Walker; public float maxHp = 1.2f; public bool plague;
        public ZombieState state; public float hp = 1.2f, timer, speed, attackClock, repath, stagger, groan, lostSight;
        public int targetSurvivor = -1, thumping = -1, variant;
        public bool female, crawler, dormant = true;
        public float hearing = 1, sight = 1;
        public List<Int2> path = new List<Int2>(); public int pathIndex;
        public const float Radius = .32f;
        public bool Alive { get { return state != ZombieState.Dead; } }
    }

    [Serializable]
    public sealed class Corpse { public int id; public Vec2 pos; public float facing; public int variant; public bool female, zombie = true; public double time; public Inventory inventory = new Inventory(); public bool rolled; }

    [Serializable]
    public sealed class Pile { public int x, y; public Inventory inventory = new Inventory { capacity = 999 }; }
}
