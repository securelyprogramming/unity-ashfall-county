using System;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>
    /// Retired Zomboid survival systems. Personal farm plots (replaced by the base Garden), fishing, skill books and
    /// smoking no longer run: their commands answer with a clear message. The public queries stay for phase 1 so the
    /// view and QA keep compiling; they report nothing to do.
    /// </summary>
    public sealed partial class Simulation
    {
        const string NoFarming = "Personal plots are gone: build a Garden at the home base and it feeds the community.";
        const string NoFishing = "Fishing is gone: bring Food rucksacks home, build a Garden or claim a grocery outpost.";
        const string NoBooks = "Skill books are no longer studied. Skills improve as you use them.";

        // ---------------------------------------------------------------- queries shared with the view (retired)
        public FarmPlot PlotRef(int prop)
        {
            FarmPlot plot;
            if (prop < 0 || prop >= world.map.props.Count || world.map.props[prop].removed || !world.plots.TryGetValue(prop, out plot)) return null;
            return plot;
        }

        /// <summary>The tile a survivor faces, just ahead.</summary>
        public void FrontTile(Survivor s, out int x, out int y)
        {
            var p = s.pos + Vec2.FromAngle(s.facing) * 1.1f; x = (int)Math.Floor(p.x); y = (int)Math.Floor(p.y);
        }

        /// <summary>Digging plots is retired; always returns why.</summary>
        public string DigProblem(Survivor s, int x, int y) { return NoFarming; }

        /// <summary>Open water within a couple of steps, from outdoors (fishing itself is retired).</summary>
        public bool FishingSpot(Survivor s)
        {
            if (world.map.Indoors(s.pos)) return false;
            int x = (int)Math.Floor(s.pos.x), y = (int)Math.Floor(s.pos.y);
            for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) if (world.map.IsWater(x + dx, y + dy)) return true;
            return false;
        }

        /// <summary>Skill books are retired: experience is never multiplied by them.</summary>
        public static float BookMultiplier(Survivor s, Skill k) { return 1; }
        public static float TraitXpMultiplier(Survivor s) { return s.HasTrait("fast_learner") ? 1.3f : s.HasTrait("slow_learner") ? .7f : 1; }

        /// <summary>Hourly upkeep that is not the community's: campfires burn down. Migration lives in Bestiary.cs,
        /// daily community upkeep in SimCommunity.cs. Food no longer spoils.</summary>
        void HourlySurvival()
        {
            foreach (var p in world.map.props)
                if (p.kind == PropKind.Campfire && p.lit && !p.removed) { p.fuel -= 1; p.modified = true; if (p.fuel <= 0) { p.lit = false; p.fuel = 0; } }
        }

        /// <summary>Answers retired survival requests with a message. Returns false when the command belongs to the general handler.</summary>
        bool ExecuteSurvival(Survivor s, Command c)
        {
            switch (c.kind)
            {
                case CommandKind.DigPlot: case CommandKind.Plant: case CommandKind.WaterPlot: world.Message(s, NoFarming); return true;
                case CommandKind.Fish: world.Message(s, NoFishing); return true;
                case CommandKind.Harvest:
                    if (c.target < 0 || c.target >= world.map.props.Count || world.map.props[c.target].kind != PropKind.FarmPlot) return false;
                    world.Message(s, NoFarming); return true;
                case CommandKind.Use:
                    {
                        var item = s.inventory.ByUid(c.item); if (item == null) return false; var d = item.Def;
                        if (d.bookSkill >= 0) { world.Message(s, NoBooks); return true; }
                        if (d.Has("smoke")) { world.Message(s, "You put the cigarettes away. Nobody here smokes any more."); return true; }
                        if (d.Has("fishing") || d.Has("bait") || d.Has("lure")) { world.Message(s, NoFishing); return true; }
                        if (d.Has("watering") || d.Has("seeds")) { world.Message(s, NoFarming); return true; }
                        return false;
                    }
            }
            return false;
        }

        /// <summary>Old timed actions (from before the rebuild) finish without effect.</summary>
        bool CompleteSurvival(Survivor s, TimedAction a)
        {
            switch (a.kind)
            {
                case ActionKind.DigPlot: case ActionKind.Plant: case ActionKind.WaterPlot: case ActionKind.Fish: case ActionKind.ReadBook: case ActionKind.Smoke: return true;
                case ActionKind.Harvest: return a.target >= 0 && a.target < world.map.props.Count && world.map.props[a.target].kind == PropKind.FarmPlot;
            }
            return false;
        }
    }
}
