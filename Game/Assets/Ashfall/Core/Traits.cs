using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>
    /// Character traits picked on the New Game screen (and rolled for generated community members). Positive traits
    /// cost points; negative traits refund them. State of Decay flavour: stamina, fighting, shooting, recovery and
    /// community morale/upkeep. The host validates the selection; a client can never grant itself traits after creation.
    /// </summary>
    public sealed class Trait
    {
        public string id, name, description, exclusive = "";
        public int cost;                       // > 0 positive (spends points), < 0 negative (gives points)
        public Skill bonusSkill; public int bonusLevels;
        public float morale;                   // community morale target while this member lives (Community.cs)
        public float upkeep = 1;               // daily Food this member eats from the base stock
        public bool Positive { get { return cost > 0; } }

        /// <summary>Free points every survivor starts with before negative traits are taken.</summary>
        public const int Budget = 4;

        public static readonly Trait[] All =
        {
            new Trait { id = "strong", name = "Strong", cost = 4, exclusive = "weak", description = "+4 carry capacity, melee hits 20% harder and shoves knock down more often." },
            new Trait { id = "fast_learner", name = "Fast Learner", cost = 6, exclusive = "slow_learner", description = "Gains 30% more experience in every skill." },
            new Trait { id = "athletic", name = "Athletic", cost = 4, exclusive = "out_of_shape", description = "Runs 8% faster and loses stamina 25% slower." },
            new Trait { id = "light_sleeper", name = "Light Sleeper", cost = 2, description = "Wakes when zombies are 14 m away instead of 7 m." },
            new Trait { id = "eagle_eyed", name = "Eagle Eyed", cost = 3, exclusive = "short_sighted", description = "Sees zombies 20% further away." },
            new Trait { id = "graceful", name = "Graceful", cost = 3, exclusive = "clumsy", description = "Footsteps make 30% less noise." },
            new Trait { id = "light_eater", name = "Light Eater", cost = 2, exclusive = "hearty_appetite", upkeep = .5f, description = "Eats half a ration: the base uses 0.5 Food a day for them instead of 1." },
            new Trait { id = "green_thumb", name = "Green Thumb", cost = 2, description = "Tends the base Garden: +1 Food a day while they live at base." },
            new Trait { id = "angler", name = "Angler", cost = 2, description = "Fishes the creeks near base: +1 Food a day while they live at base." },
            new Trait { id = "marksman", name = "Marksman", cost = 3, bonusSkill = Skill.Aiming, bonusLevels = 2, description = "Shooting +2. A natural watchtower guard." },
            new Trait { id = "brawler", name = "Brawler", cost = 3, bonusSkill = Skill.Melee, bonusLevels = 2, description = "Fighting +2." },
            new Trait { id = "quick_healer", name = "Quick Healer", cost = 2, exclusive = "slow_healer", description = "Health and injuries recover 50% faster." },
            new Trait { id = "hardy", name = "Hardy", cost = 2, description = "Blood plague advances 30% slower." },
            new Trait { id = "cheerful", name = "Cheerful", cost = 2, exclusive = "gloomy", morale = 5, description = "Lifts everyone: community morale +5." },
            new Trait { id = "weak", name = "Weak", cost = -4, exclusive = "strong", description = "-4 carry capacity and melee hits 20% softer." },
            new Trait { id = "slow_learner", name = "Slow Learner", cost = -4, exclusive = "fast_learner", description = "Gains 30% less experience in every skill." },
            new Trait { id = "out_of_shape", name = "Out of Shape", cost = -3, exclusive = "athletic", description = "Loses stamina 35% faster when running." },
            new Trait { id = "smoker", name = "Smoker", cost = -2, description = "Short of breath: stamina recovers 20% slower." },
            new Trait { id = "short_sighted", name = "Short Sighted", cost = -2, exclusive = "eagle_eyed", description = "Sees zombies 30% less far and maps less of the county." },
            new Trait { id = "clumsy", name = "Clumsy", cost = -2, exclusive = "graceful", description = "Footsteps make 30% more noise." },
            new Trait { id = "hearty_appetite", name = "Hearty Appetite", cost = -2, exclusive = "light_eater", upkeep = 1.5f, description = "Eats for two: the base uses 1.5 Food a day for them." },
            new Trait { id = "slow_healer", name = "Slow Healer", cost = -2, exclusive = "quick_healer", description = "Health and injuries recover 35% slower." },
            new Trait { id = "gloomy", name = "Gloomy", cost = -2, exclusive = "cheerful", morale = -5, description = "Brings everyone down: community morale -5." },
        };

        /// <summary>Health and injury recovery multiplier from traits.</summary>
        public static float Recovery(Survivor s) { return s.HasTrait("quick_healer") ? 1.5f : s.HasTrait("slow_healer") ? .65f : 1; }
        /// <summary>Stamina recovery multiplier from traits.</summary>
        public static float StaminaRecovery(Survivor s) { return s.HasTrait("smoker") ? .8f : s.HasTrait("athletic") ? 1.15f : 1; }
        /// <summary>Daily Food this survivor eats from the community stock.</summary>
        public static float Upkeep(Survivor s) { float u = 1; foreach (var id in s.traits) { var t = Get(id); if (t != null && t.upkeep != 1) u = t.upkeep; } return u; }
        /// <summary>Morale this survivor adds to the community target.</summary>
        public static float Morale(Survivor s) { float m = 0; foreach (var id in s.traits) { var t = Get(id); if (t != null) m += t.morale; } return m; }

        public static Trait Get(string id) { return All.FirstOrDefault(t => t.id == id); }

        /// <summary>Points remaining after a selection; negative means over budget.</summary>
        public static int PointsLeft(IEnumerable<string> ids) { return Budget - ids.Select(Get).Where(t => t != null).Sum(t => t.cost); }

        /// <summary>Returns "" when the selection is legal, otherwise why it is not.</summary>
        public static string Validate(IList<string> ids)
        {
            if (ids == null) return "";
            if (ids.Distinct().Count() != ids.Count) return "A trait is selected twice.";
            foreach (var id in ids)
            {
                var t = Get(id); if (t == null) return "Unknown trait: " + id;
                if (t.exclusive.Length > 0 && ids.Contains(t.exclusive)) return t.name + " and " + Get(t.exclusive).name + " cannot be combined.";
            }
            int left = PointsLeft(ids);
            if (left < 0) return "The traits cost " + (-left) + " more points than you have.";
            return "";
        }
    }
}
