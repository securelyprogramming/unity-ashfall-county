using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;

namespace Ashfall.View
{
    /// <summary>One workshop recipe as the Base tab shows it.</summary>
    public sealed class WorkshopEntry { public string id = "", name = "", description = "", cost = "", facility = "Workshop"; public bool enabled; public string reason = ""; }

    /// <summary>Fallback workshop list for the contract commit: reads the old public <see cref="Recipe.All"/> list.
    /// On main, UiCommunityApi.Workshop switches to WorkshopRecipe.Recipes and this class can be deleted.</summary>
    public static class UiWorkshop
    {
        public static List<WorkshopEntry> Recipes(World w, Simulation sim, Survivor s)
        {
            var list = new List<WorkshopEntry>();
            var shop = w.community.Get(FacilityKind.Workshop);
            bool built = shop != null && shop.buildHours <= 0, atBase = s != null && sim.AtBase(s);
            foreach (var r in Recipe.All)
            {
                if (r.places || r.needsHeat) continue;   // campfires, crates and stews belonged to the retired personal crafting
                var e = new WorkshopEntry { id = r.id, name = r.name, description = r.description ?? "" };
                var parts = r.inputs.Select(i => i.Label).ToList();
                parts.AddRange(r.tools.Select(t => Recipe.TagName(t) + " (kept)"));
                e.cost = parts.Count == 0 ? "No materials" : string.Join(", ", parts);
                var missing = s == null ? new List<string>() : r.Missing(s, false);
                e.enabled = built && atBase && missing.Count == 0;
                e.reason = !built ? "Build a Workshop first" : !atBase ? "Only at base" : missing.Count > 0 ? "Missing: " + string.Join(", ", missing) : "";
                list.Add(e);
            }
            return list;
        }
    }
}
