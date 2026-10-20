using System.Linq;
using Ashfall.Core;

namespace Ashfall.View
{
    /// <summary>Context actions for farm plots, digging and fishing. Each one is a host command the simulation re-validates.</summary>
    public sealed partial class AshfallGame
    {
        void PlotActions(Interactable it, Survivor s, Prop p)
        {
            var plot = Sim.PlotRef(p.id); if (plot == null) return;
            var inv = s.inventory; int id = p.id;
            it.label = plot.Label;
            if (plot.Empty)
            {
                var seeds = inv.items.Where(i => CropDef.ForSeed(i.def) != null).GroupBy(i => i.def).Select(g => g.First()).Take(4).ToList();
                foreach (var seed in seeds) { int uid = seed.uid; Add(it, "Plant " + CropDef.ForSeed(seed.def).name.ToLowerInvariant(), () => Submit(new Command { kind = CommandKind.Plant, target = id, item = uid })); }
                if (seeds.Count == 0) Add(it, "Plant seeds", () => { }, false, "No seed packets");
            }
            if (plot.Ripe) Add(it, "Harvest " + plot.Crop.name.ToLowerInvariant(), () => Submit(new Command { kind = CommandKind.Harvest, target = id }));
            if (plot.Dead) Add(it, "Clear dead plants", () => Submit(new Command { kind = CommandKind.Harvest, target = id }));
            if (!plot.Dead && plot.water < .95f)
            {
                // Prefer the watering can, then any container that holds water.
                var cans = inv.items.Where(i => i.Def.waterCapacity > 0 && i.water > 0).OrderByDescending(i => i.Def.Has("watering")).Take(2).ToList();
                foreach (var can in cans) { int uid = can.uid; Add(it, "Water with " + can.Def.name + " (" + can.water + ")", () => Submit(new Command { kind = CommandKind.WaterPlot, target = id, item = uid })); }
                if (cans.Count == 0) Add(it, "Water the plot", () => { }, false, "Fill a watering can or bottle first");
            }
        }

        void SurvivalSelfActions(Interactable self, Survivor s)
        {
            if (World.map.Indoors(s.pos)) return;
            if (s.inventory.FindTag("dig") != null)
            {
                int x, y; Sim.FrontTile(s, out x, out y); string why = Sim.DigProblem(s, x, y);
                Add(self, "Dig a farm plot ahead", () => Submit(Command.Of(CommandKind.DigPlot)), why.Length == 0, why);
            }
            var rod = s.inventory.FindTag("fishing");
            if (rod != null && Sim.FishingSpot(s))
            {
                bool bait = s.inventory.FindTag("bait") != null || s.inventory.FindTag("lure") != null; int uid = rod.uid;
                Add(self, "Fish here", () => Submit(new Command { kind = CommandKind.Fish, item = uid }), bait, "Needs worms or a lure");
            }
        }
    }
}
