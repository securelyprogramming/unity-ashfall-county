using System;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>Long-term survival rules: farm plots, fishing, skill books, smoking and trait effects.</summary>
    public sealed partial class Simulation
    {
        // ---------------------------------------------------------------- queries shared with the view
        public FarmPlot PlotRef(int prop)
        {
            FarmPlot plot;
            if (prop < 0 || prop >= world.map.props.Count || world.map.props[prop].removed || !world.plots.TryGetValue(prop, out plot)) return null;
            return plot;
        }

        /// <summary>The tile a survivor would dig: the one they face, just ahead.</summary>
        public void FrontTile(Survivor s, out int x, out int y)
        {
            var p = s.pos + Vec2.FromAngle(s.facing) * 1.1f; x = (int)Math.Floor(p.x); y = (int)Math.Floor(p.y);
        }

        /// <summary>Returns "" when the survivor can dig a plot on that tile, otherwise the reason.</summary>
        public string DigProblem(Survivor s, int x, int y)
        {
            var map = world.map;
            if (s.inventory.FindTag("dig") == null) return "You need a shovel to dig a plot.";
            if (map.Indoors(s.pos) || map.RoomAt(x, y) != null) return "Dig farm plots outdoors.";
            var g = map.GroundAt(x, y);
            if (g != Ground.Grass && g != Ground.Field && g != Ground.Dirt) return "The ground here is too hard. Find grass or a field.";
            if (map.TileBlocked(x, y) || NonBlockingPropAt(x, y) != null) return "Something is in the way. Face an open patch of ground.";
            var centre = new Vec2(x + .5f, y + .5f); float d = Vec2.Distance(s.pos, centre);
            if (d < .7f) return "Step back from where you want to dig.";
            if (d > 1.8f || !map.LineOfSight(s.pos, centre)) return "You can't reach there.";
            return "";
        }

        /// <summary>Open water within a couple of steps, from outdoors.</summary>
        public bool FishingSpot(Survivor s)
        {
            if (world.map.Indoors(s.pos)) return false;
            int x = (int)Math.Floor(s.pos.x), y = (int)Math.Floor(s.pos.y);
            for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) if (world.map.IsWater(x + dx, y + dy)) return true;
            return false;
        }

        bool TooDarkToRead(Survivor s) { return world.Daylight < .3f && !s.lightOn && !(world.map.Indoors(s.pos) && world.PowerOn); }

        /// <summary>Experience multiplier from read skill books that cover the survivor's current level.</summary>
        public static float BookMultiplier(Survivor s, Skill k)
        {
            float m = 1; int lvl = s.Level(k);
            foreach (var id in s.booksRead)
            {
                if (!ItemCatalog.Exists(id)) continue; var d = ItemCatalog.Get(id);
                if (d.bookSkill == (int)k && lvl >= d.bookMinLevel && lvl < d.bookMaxLevel) m = Math.Max(m, d.bookMultiplier);
            }
            return m;
        }
        public static float TraitXpMultiplier(Survivor s) { return s.HasTrait("fast_learner") ? 1.3f : s.HasTrait("slow_learner") ? .7f : 1; }

        // ---------------------------------------------------------------- commands
        /// <summary>Validates and starts survival requests. Returns false when the command belongs to the general handler.</summary>
        /// <summary>Hourly community/survival upkeep (food ageing, campfires, crops). Migration lives in Bestiary.cs.</summary>
        void HourlySurvival()
        {
            var w = world;
            foreach (var s in w.survivors) foreach (var i in s.inventory.items) if (i.Def.perishDays > 0) i.ageHours += 1;
            foreach (var c in w.map.containers)
                if (c.generated) foreach (var i in c.inventory.items) if (i.Def.perishDays > 0) i.ageHours += c.cold && w.PowerOn ? .2f : 1;
            foreach (var p in w.piles.Values) foreach (var i in p.inventory.items) if (i.Def.perishDays > 0) i.ageHours += 1;
            foreach (var p in w.map.props)
                if (p.kind == PropKind.Campfire && p.lit && !p.removed) { p.fuel -= 1; p.modified = true; if (p.fuel <= 0) { p.lit = false; p.fuel = 0; } }
            GrowPlots();
        }

        bool ExecuteSurvival(Survivor s, Command c)
        {
            var inv = s.inventory;
            switch (c.kind)
            {
                case CommandKind.DigPlot:
                    {
                        int x, y; FrontTile(s, out x, out y);
                        string why = DigProblem(s, x, y); if (why.Length > 0) { world.Message(s, why); return true; }
                        Begin(s, ActionKind.DigPlot, 7 * (1 - s.Level(Skill.Farming) * .04f), -1, -1, "", false, world.map.Index(x, y));
                        return true;
                    }
                case CommandKind.Plant:
                    {
                        var plot = PlotRef(c.target); if (plot == null || !ReachProp(s, world.map.props[plot.prop])) return true;
                        var seed = inv.ByUid(c.item); var crop = seed == null ? null : CropDef.ForSeed(seed.def);
                        if (crop == null) { world.Message(s, "Plant seeds from a seed packet."); return true; }
                        if (!plot.Empty) { world.Message(s, plot.Dead ? "Clear the dead plants first." : "Something is already growing here."); return true; }
                        Begin(s, ActionKind.Plant, 3.5f, plot.prop, seed.uid); return true;
                    }
                case CommandKind.WaterPlot:
                    {
                        var plot = PlotRef(c.target); if (plot == null || !ReachProp(s, world.map.props[plot.prop])) return true;
                        var can = inv.ByUid(c.item);
                        if (can == null || can.Def.waterCapacity == 0 || can.water <= 0) { world.Message(s, "You need a container with water in it."); return true; }
                        if (plot.water >= .95f) { world.Message(s, "The soil is already soaked."); return true; }
                        Begin(s, ActionKind.WaterPlot, 2.5f, plot.prop, can.uid); return true;
                    }
                case CommandKind.Harvest:
                    {
                        if (c.target < 0 || c.target >= world.map.props.Count || world.map.props[c.target].kind != PropKind.FarmPlot) return false;
                        var plot = PlotRef(c.target); if (plot == null || !ReachProp(s, world.map.props[plot.prop])) return true;
                        if (plot.Empty) world.Message(s, "Nothing is planted here.");
                        else if (!plot.Ripe && !plot.Dead) world.Message(s, "The " + plot.Crop.name.ToLowerInvariant() + " aren't ripe yet (" + (int)(plot.growth * 100) + "%).");
                        else Begin(s, ActionKind.Harvest, plot.Dead ? 2 : 4, plot.prop);
                        return true;
                    }
                case CommandKind.Fish:
                    {
                        var rod = c.item >= 0 ? inv.ByUid(c.item) : inv.FindTag("fishing");
                        if (rod == null || !rod.Def.Has("fishing")) { world.Message(s, "You need a fishing rod."); return true; }
                        if (!FishingSpot(s)) { world.Message(s, "Stand at the edge of the river or a pond to fish."); return true; }
                        if (inv.FindTag("bait") == null && inv.FindTag("lure") == null) { world.Message(s, "You need bait: worms (forage or dig for them) or a fishing lure."); return true; }
                        Begin(s, ActionKind.Fish, 14 * (1 - s.Level(Skill.Fishing) * .04f), -1, rod.uid); return true;
                    }
                case CommandKind.Use:
                    {
                        var item = inv.ByUid(c.item); if (item == null) return false; var d = item.Def;
                        if (d.bookSkill >= 0)
                        {
                            if (s.booksRead.Contains(d.id)) { world.Message(s, "You already know everything in " + d.name + "."); return true; }
                            if (s.Level((Skill)d.bookSkill) >= d.bookMaxLevel) { world.Message(s, "There's nothing left in " + d.name + " for you to learn."); return true; }
                            if (TooDarkToRead(s)) { world.Message(s, "It's too dark to read. Turn on a light."); return true; }
                            Begin(s, ActionKind.ReadBook, 20, -1, item.uid); return true;
                        }
                        if (d.Has("smoke"))
                        {
                            if (inv.FindTag("fire") == null) { world.Message(s, "You need a lighter or matches."); return true; }
                            Begin(s, ActionKind.Smoke, 4, -1, item.uid); return true;
                        }
                        if (d.Has("fishing")) return ExecuteSurvival(s, new Command { kind = CommandKind.Fish, item = item.uid });
                        if (d.Has("watering")) { world.Message(s, "Stand next to a farm plot to water it."); return true; }
                        if (d.Has("seeds")) { world.Message(s, "Dig a plot with a shovel, then plant the seeds in it."); return true; }
                        return false;
                    }
            }
            return false;
        }

        // ---------------------------------------------------------------- completion
        bool CompleteSurvival(Survivor s, TimedAction a)
        {
            var inv = s.inventory; var item = a.item >= 0 ? inv.ByUid(a.item) : null;
            switch (a.kind)
            {
                case ActionKind.DigPlot:
                    {
                        int x = a.extra % world.map.width, y = a.extra / world.map.width;
                        string why = DigProblem(s, x, y); if (why.Length > 0) { world.Message(s, why); return true; }
                        var prop = world.map.AddProp(PropKind.FarmPlot, x, y); prop.modified = true;
                        world.plots[prop.id] = new FarmPlot { prop = prop.id, water = world.raining ? .5f : .1f };
                        s.endurance = Math.Max(0, s.endurance - 12); world.MakeNoise(s.pos, 5, s.id);
                        if (world.rng.Chance(.3f)) { inv.Add(ItemCatalog.Make("worms", world.rng.Range(1, 3))); world.Message(s, "You dig a farm plot and turn up some worms."); }
                        else world.Message(s, "You dig a farm plot. Plant seeds in it and keep it watered.");
                        GainXp(s, Skill.Farming, 4); world.Emit(EventKind.Craft, prop.Center, s.id, "Farm plot");
                        return true;
                    }
                case ActionKind.Plant:
                    {
                        var plot = PlotRef(a.target); if (plot == null || item == null || !plot.Empty || !ReachProp(s, world.map.props[plot.prop])) return true;
                        var crop = CropDef.ForSeed(item.def); if (crop == null) return true;
                        inv.Take(item.uid, 1);
                        plot.crop = crop.id; plot.growth = 0; plot.health = 1; plot.ripeHours = 0;
                        plot.care = 1 + s.Level(Skill.Farming) * .04f + (s.HasTrait("green_thumb") ? .15f : 0);
                        GainXp(s, Skill.Farming, 3); world.Message(s, "You plant " + crop.name.ToLowerInvariant() + ". Keep the soil watered; rain helps.");
                        return true;
                    }
                case ActionKind.WaterPlot:
                    {
                        var plot = PlotRef(a.target); if (plot == null || item == null || item.water <= 0) return true;
                        int need = (int)Math.Ceiling((1 - plot.water) / .1f), units = Math.Min(need, item.water);
                        if (units <= 0) { world.Message(s, "The soil is already soaked."); return true; }
                        item.water -= units; if (item.water == 0) item.tainted = false;
                        plot.water = Math.Min(1, plot.water + units * .1f);
                        GainXp(s, Skill.Farming, 1); world.Message(s, "You water the plot (" + units + " unit" + (units > 1 ? "s" : "") + " of water).");
                        return true;
                    }
                case ActionKind.Harvest:
                    {
                        if (a.target < 0 || a.target >= world.map.props.Count || world.map.props[a.target].kind != PropKind.FarmPlot) return false;
                        var plot = PlotRef(a.target); if (plot == null) return true;
                        if (plot.Dead) { plot.Clear(); GainXp(s, Skill.Farming, 1); world.Message(s, "You pull up the dead plants."); return true; }
                        if (!plot.Ripe) return true;
                        var crop = plot.Crop; int lvl = s.Level(Skill.Farming);
                        int n = Math.Max(1, (int)Math.Round((world.rng.Range(crop.yieldMin, crop.yieldMax + 1) + lvl / 3) * (.5f + .5f * plot.health)));
                        for (int i = 0; i < n; i++) inv.Add(ItemCatalog.Make(crop.produce));
                        int seeds = world.rng.Chance(.45f + lvl * .05f) ? world.rng.Range(1, 3) : 0;
                        if (seeds > 0) inv.Add(ItemCatalog.Make(crop.seed, seeds));
                        plot.Clear();
                        GainXp(s, Skill.Farming, 6 + n);
                        world.Message(s, "You harvest " + n + " " + ItemCatalog.Get(crop.produce).name.ToLowerInvariant() + (seeds > 0 ? " and save " + seeds + " seed packet" + (seeds > 1 ? "s" : "") : "") + ".");
                        return true;
                    }
                case ActionKind.Fish:
                    {
                        var rod = item; if (rod == null || !FishingSpot(s)) return true;
                        var worm = inv.FindTag("bait"); var lure = worm == null ? inv.FindTag("lure") : null;
                        if (worm == null && lure == null) { world.Message(s, "You're out of bait."); return true; }
                        int lvl = s.Level(Skill.Fishing); float h = world.HourOfDay;
                        float chance = .3f + lvl * .045f + (worm != null ? .08f : 0) + ((h >= 5 && h <= 8.5f) || (h >= 18 && h <= 21) ? .12f : 0) + (world.raining ? .05f : 0) + (s.HasTrait("angler") ? .1f : 0);
                        bool caught = world.rng.Chance(Math.Min(.9f, chance));
                        if (worm != null) inv.Take(worm.uid, 1);
                        else if (caught && world.rng.Chance(.12f)) { inv.Take(lure.uid, 1); world.Message(s, "A fish snaps the line and takes your lure."); }
                        if (caught)
                        {
                            float roll = world.rng.Float() + lvl * .04f;
                            string fish = roll > .9f ? "fish_catfish" : roll > .55f ? "fish_bass" : "fish_perch";
                            inv.Add(ItemCatalog.Make(fish)); GainXp(s, Skill.Fishing, 7);
                            world.Message(s, "You land a " + (fish == "fish_catfish" ? "big catfish" : fish == "fish_bass" ? "bass" : "perch") + "! Cook it before eating.");
                            world.Emit(EventKind.Craft, s.pos, s.id, ItemCatalog.Get(fish).name);
                        }
                        else { world.Message(s, "Nothing bites."); GainXp(s, Skill.Fishing, 3); }
                        rod.condition -= .015f;
                        if (rod.condition <= 0) { Unequip(s, rod.uid); inv.Take(rod.uid, rod.count); world.Message(s, "The fishing rod snaps."); }
                        world.MakeNoise(s.pos, 2, s.id);
                        return true;
                    }
                case ActionKind.ReadBook:
                    {
                        if (item == null || item.Def.bookSkill < 0 || s.booksRead.Contains(item.def)) return true;
                        var d = item.Def; s.booksRead.Add(d.id); s.boredom = Math.Max(0, s.boredom - 15);
                        world.Message(s, "You finish " + d.name + ". " + (Skill)d.bookSkill + " experience ×" + d.bookMultiplier.ToString("0") + " until level " + d.bookMaxLevel + ".");
                        return true;
                    }
                case ActionKind.Smoke:
                    {
                        if (item == null || !item.Def.Has("smoke")) return true;
                        inv.Take(item.uid, 1); s.hoursSinceSmoke = 0;
                        s.panic = Math.Max(0, s.panic - 25); s.boredom = Math.Max(0, s.boredom - 10);
                        if (!s.HasTrait("smoker")) { s.sickness = Math.Min(100, s.sickness + 4); world.Message(s, "You cough on the smoke."); }
                        else world.Message(s, "The cigarette steadies your nerves.");
                        return true;
                    }
            }
            return false;
        }

        // ---------------------------------------------------------------- hourly and per-tick effects
        void GrowPlots()
        {
            foreach (var plot in world.plots.Values)
            {
                if (world.map.props[plot.prop].removed) continue;
                plot.water = world.raining ? Math.Min(1, plot.water + .3f * world.rainIntensity) : Math.Max(0, plot.water - CropDef.DryPerHour);
                if (plot.Empty || plot.Dead) continue;
                var crop = plot.Crop; if (crop == null) { plot.health = 0; continue; }
                if (plot.Ripe)
                {
                    plot.ripeHours += 1;
                    if (plot.ripeHours > CropDef.RipeHours) plot.health = Math.Max(0, plot.health - 1f / 24);
                    continue;
                }
                if (plot.water <= 0) { plot.health = Math.Max(0, plot.health - 1f / CropDef.WiltHours); continue; }
                plot.health = Math.Min(1, plot.health + .02f);
                plot.growth = Math.Min(1, plot.growth + plot.care / (crop.growDays * 24));
                if (plot.growth >= 1)
                {
                    var at = world.map.props[plot.prop].Center;
                    foreach (var s in world.survivors) if (s.alive && Vec2.Distance(s.pos, at) < 60) world.Message(s, "The " + crop.name.ToLowerInvariant() + " in your plot are ripe.");
                }
            }
        }

        void SmokerNeeds(Survivor s, float hours)
        {
            float before = s.hoursSinceSmoke; s.hoursSinceSmoke += hours;
            if (!s.HasTrait("smoker")) return;
            if (before < 8 && s.hoursSinceSmoke >= 8) world.Message(s, "You're craving a cigarette.");
            if (s.hoursSinceSmoke < 8) return;
            float over = Math.Min(1, (s.hoursSinceSmoke - 8) / 16);
            s.boredom = MathUtil.Clamp(s.boredom + hours * (2 + 4 * over), 0, 100);
            s.panic = Math.Max(s.panic, 10 + 30 * over);
        }
    }
}
