using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>One workshop recipe as the Base tab shows it.</summary>
    public sealed class WorkshopEntry { public string id = "", name = "", description = "", cost = "", facility = "Workshop"; public bool enabled; public string reason = ""; }

    /// <summary>Community queries the UI reads (Community.cs, SimCommunity.cs, CommunityMissions.cs, Crafting.cs, SimSurvivor.cs).</summary>
    public static class UiCommunityApi
    {
        /// <summary>Stock cap per resource (the same cap for every resource).</summary>
        public static float StockCap(World w, Resource r) { return w.community.StorageCap; }

        /// <summary>Facility slots in use (built-in Storage and Beds do not count).</summary>
        public static int UsedSlots(Simulation sim, World w) { return sim.UsedSlots; }

        public static string FacilityName(FacilityKind k) { return Community.FacilityName(k); }
        public static string FacilityDescription(FacilityKind k) { return Community.FacilityDescription(k); }

        /// <summary>Whether an existing facility can be upgraded, with the reason when not.</summary>
        public static bool CanUpgrade(Simulation sim, FacilityKind k, out string reason) { return sim.CanUpgrade(k, out reason); }

        /// <summary>0..1 progress of a facility being built or upgraded (1 when idle).</summary>
        public static float Progress(Facility f)
        {
            if (f.buildHours <= 0) return 1;
            float total = Simulation.FacilityHours(f.kind, f.upgrading ? f.level + 1 : f.level);
            return total <= 0 ? 0 : Mathf.Clamp01(1 - f.buildHours / total);
        }

        /// <summary>The resident posted at the Watchtower, or -1.</summary>
        public static int Garrison(World w) { return w.community.garrison; }

        /// <summary>The last daily radio summary from the community ("" when none).</summary>
        public static string LastReport(World w) { return w.community.lastReport ?? ""; }

        /// <summary>In-game hours before an offered or active mission expires.</summary>
        public static float MissionHoursLeft(Simulation sim, World w, Mission m) { return sim.MissionHoursLeft(m); }

        public static string GiverName(Mission m) { return !string.IsNullOrEmpty(m.giverName) ? m.giverName : UiData.GiverName(m.giver); }

        /// <summary>Maximum stamina after fatigue and plague (the HUD greys out the rest of the bar).</summary>
        public static float StaminaCap(Survivor s) { return Simulation.StaminaCap(s); }

        /// <summary>Workshop recipes for the Community panel: id (sent as CraftAtWorkshop recipe), name, description,
        /// cost text, the facility needed and an enabled/reason pair.</summary>
        public static List<WorkshopEntry> Workshop(World w, Simulation sim, Survivor s)
        {
            var list = new List<WorkshopEntry>();
            foreach (var r in WorkshopRecipe.Recipes)
            {
                string why = s == null ? "No survivor" : sim.WorkshopProblem(s, r.id);
                list.Add(new WorkshopEntry { id = r.id, name = r.name, description = r.description, cost = r.CostLabel, facility = r.FacilityLabel, enabled = why.Length == 0, reason = why });
            }
            return list;
        }
    }
}
