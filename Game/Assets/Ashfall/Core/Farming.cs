using System;
using System.Linq;

namespace Ashfall.Core
{
    // Personal farm plots are retired: the base Garden (Community.cs) produces Food for the whole community. These
    // types stay for phase 1 because the view, the save codec and QA still reference them; nothing creates plots now.

    /// <summary>Static crop balance data (retired). Growth is in in-game days and only advanced while the plot was watered.</summary>
    public sealed class CropDef
    {
        public string id, name, seed, produce;
        public float growDays; public int yieldMin, yieldMax;

        public static readonly CropDef[] All =
        {
            new CropDef { id = "carrot", name = "Carrots", seed = "seeds_carrot", produce = "carrots", growDays = 3, yieldMin = 2, yieldMax = 4 },
            new CropDef { id = "potato", name = "Potatoes", seed = "seeds_potato", produce = "potato", growDays = 4, yieldMin = 3, yieldMax = 5 },
            new CropDef { id = "tomato", name = "Tomatoes", seed = "seeds_tomato", produce = "tomato", growDays = 3, yieldMin = 3, yieldMax = 6 },
            new CropDef { id = "cabbage", name = "Cabbage", seed = "seeds_cabbage", produce = "cabbage", growDays = 5, yieldMin = 1, yieldMax = 3 },
        };
        public static CropDef Get(string id) { return All.FirstOrDefault(c => c.id == id); }
        public static CropDef ForSeed(string item) { return All.FirstOrDefault(c => c.seed == item); }

        /// <summary>Hours a ripe crop stays good before it starts to rot in the ground.</summary>
        public const float RipeHours = 72;
        /// <summary>Water lost per in-game hour; a full watering lasts a little over a day.</summary>
        public const float DryPerHour = 1f / 30;
        /// <summary>Plants wilt when the soil is dry and die after this many dry hours.</summary>
        public const float WiltHours = 30;
    }

    /// <summary>A dug plot on a FarmPlot prop (retired; old plots no longer grow). Saves still store every plot.</summary>
    [Serializable]
    public sealed class FarmPlot
    {
        public int prop;
        public string crop = "";
        public float growth;          // 0..1, ripe at 1
        public float water;           // 0..1 soil moisture
        public float health = 1;      // 0 = dead
        public float ripeHours;
        public float care = 1;        // growth rate from the planter's Farming skill and traits
        public void Clear() { crop = ""; growth = 0; health = 1; ripeHours = 0; care = 1; }
        public bool Empty { get { return string.IsNullOrEmpty(crop); } }
        public bool Dead { get { return !Empty && health <= 0; } }
        public bool Ripe { get { return !Empty && !Dead && growth >= 1; } }
        public CropDef Crop { get { return CropDef.Get(crop); } }
        public string Stage
        {
            get
            {
                if (Empty) return "empty";
                if (Dead) return growth >= 1 ? "rotted" : "withered";
                if (Ripe) return ripeHours > CropDef.RipeHours * .7f ? "ripe, starting to spoil" : "ripe";
                return growth < .25f ? "seedling" : growth < .7f ? "growing" : "nearly ripe";
            }
        }
        public string Moisture { get { return water <= 0 ? "dry" : water < .35f ? "damp" : "wet"; } }
        public string Label
        {
            get
            {
                if (Empty) return "Farm plot (empty, " + Moisture + ")";
                var c = Crop; string name = c == null ? crop : c.name;
                return "Farm plot: " + name + " (" + Stage + (Ripe || Dead ? "" : " " + (int)(growth * 100) + "%") + ", " + Moisture + ")";
            }
        }
    }
}
