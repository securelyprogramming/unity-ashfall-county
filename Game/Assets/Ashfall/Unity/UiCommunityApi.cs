using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>The one place the UI touches community APIs that landed on main after this branch's contract commit (e3278ca).
    /// Every method runs on the contract stubs today; each "MERGE:" comment gives the exact replacement line for main
    /// (core-community: Community.cs, SimCommunity.cs, CommunityMissions.cs, Crafting.cs, SimSurvivor.cs).</summary>
    public static class UiCommunityApi
    {
        /// <summary>Stock cap per resource (the same cap for every resource).</summary>
        public static float StockCap(World w, Resource r)
        {
            // MERGE: return w.community.StorageCap;
            var storage = w.community.Get(FacilityKind.Storage);
            return 20 + 15 * Mathf.Max(1, storage == null ? 0 : storage.level);
        }

        /// <summary>Facility slots in use (built-in Storage and Beds do not count).</summary>
        public static int UsedSlots(Simulation sim, World w)
        {
            // MERGE: return sim.UsedSlots;
            int n = 0; foreach (var f in w.community.facilities) if (f.kind != FacilityKind.Storage && f.kind != FacilityKind.Beds) n++; return n;
        }

        public static string FacilityName(FacilityKind k)
        {
            // MERGE: return Community.FacilityName(k);
            return k == FacilityKind.RadioRoom ? "Radio Room" : k.ToString();
        }
        public static string FacilityDescription(FacilityKind k)
        {
            // MERGE: return Community.FacilityDescription(k);
            return UiData.FacilityBlurb(k);
        }

        /// <summary>Whether an existing facility can be upgraded, with the reason when not.</summary>
        public static bool CanUpgrade(Simulation sim, FacilityKind k, out string reason)
        {
            // MERGE: return sim.CanUpgrade(k, out reason);
            return sim.CanBuild(k, out reason);
        }

        /// <summary>0..1 progress of a facility being built or upgraded (1 when idle).</summary>
        public static float Progress(Facility f)
        {
            if (f.buildHours <= 0) return 1;
            // MERGE: float total = Simulation.FacilityHours(f.kind, f.upgrading ? f.level + 1 : f.level);
            float total = Simulation.FacilityHours(f.kind, f.level);
            return total <= 0 ? 0 : Mathf.Clamp01(1 - f.buildHours / total);
        }

        /// <summary>The resident posted at the Watchtower, or -1.</summary>
        public static int Garrison(World w)
        {
            // MERGE: return w.community.garrison;
            return -1;
        }

        /// <summary>The last daily radio summary from the community ("" when none).</summary>
        public static string LastReport(World w)
        {
            // MERGE: return w.community.lastReport;
            return "";
        }

        /// <summary>In-game hours before an offered or active mission expires.</summary>
        public static float MissionHoursLeft(Simulation sim, World w, Mission m)
        {
            // MERGE: return sim.MissionHoursLeft(m);
            return (float)((m.expiresAt - w.minutes) / 60);
        }

        public static string GiverName(Mission m)
        {
            // MERGE: if (!string.IsNullOrEmpty(m.giverName)) return m.giverName;
            return UiData.GiverName(m.giver);
        }

        /// <summary>Maximum stamina after fatigue and plague (the HUD greys out the rest of the bar).</summary>
        public static float StaminaCap(Survivor s)
        {
            // MERGE: return Simulation.StaminaCap(s);
            return 100;
        }

        /// <summary>Workshop recipes for the Community panel. The panel needs id (sent as CraftAtWorkshop recipe), name,
        /// description, cost text, the facility needed and an enabled/reason pair.</summary>
        public static List<WorkshopEntry> Workshop(World w, Simulation sim, Survivor s)
        {
            /* MERGE: replace the body with
            var list = new List<WorkshopEntry>();
            foreach (var r in WorkshopRecipe.Recipes)
            {
                string why = s == null ? "No survivor" : sim.WorkshopProblem(s, r.id);
                list.Add(new WorkshopEntry { id = r.id, name = r.name, description = r.description, cost = r.CostLabel, facility = r.FacilityLabel, enabled = why.Length == 0, reason = why });
            }
            return list;
            */
            return UiWorkshop.Recipes(w, sim, s);
        }
    }
}
