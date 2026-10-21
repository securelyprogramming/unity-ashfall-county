using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ashfall.Core;

/// <summary>Community checks. Register each case with run("name", () => ...); assert with check(condition, "what").</summary>
static class CommunityTests
{
    public static void All(Action<string, Action> run, Action<bool, string> check)
    {
        EnsureContractItems();
        run("community-setup", () => Setup(check));
        run("community-upkeep", () => Upkeep(check));
        run("community-rucksacks", () => Rucksacks(check));
        run("community-facilities", () => Facilities(check));
        run("community-workshop", () => Workshop(check));
        run("community-switching", () => Switching(check));
        run("community-companion", () => Companion(check));
        run("community-recruit", () => Recruiting(check));
        run("community-outposts", () => Outposts(check));
        run("community-missions", () => Missions(check));
        run("community-plague", () => Plague(check));
        run("community-injury", () => Injury(check));
        run("community-death", () => Death(check));
        run("community-legacy", () => Legacy(check));
        run("community-save-load", () => SaveLoad(check));
    }

    // ---------------------------------------------------------------- helpers
    /// <summary>The contract items core-world adds (rucksacks, plague cure...). Registered here only when this branch
    /// does not define them yet, so the community rules can be tested before the merge.</summary>
    static void EnsureContractItems()
    {
        Ensure("rucksack_food", "Food Rucksack", ItemCategory.Misc, 6, d => { d.resource = (int)Resource.Food; d.resourceAmount = 4; });
        Ensure("rucksack_meds", "Medical Rucksack", ItemCategory.Misc, 6, d => { d.resource = (int)Resource.Meds; d.resourceAmount = 3; });
        Ensure("rucksack_materials", "Materials Rucksack", ItemCategory.Misc, 8, d => { d.resource = (int)Resource.Materials; d.resourceAmount = 5; });
        Ensure("plague_sample", "Plague Sample", ItemCategory.Misc, .2f, d => { d.stack = 10; });
        Ensure("plague_cure", "Plague Cure", ItemCategory.Medical, .2f, d => { d.plagueCure = 100; });
        Ensure("molotov", "Molotov Cocktail", ItemCategory.Weapon, .8f, d => { d.tags = new[] { "throwable" }; d.blastRadius = 3; d.stack = 4; });
        Ensure("energy_drink", "Energy Drink", ItemCategory.Drink, .3f, d => { d.stamina = 40; });
    }
    static void Ensure(string id, string name, ItemCategory category, float weight, Action<ItemDef> configure)
    {
        if (ItemCatalog.Exists(id)) return;
        var defs = (Dictionary<string, ItemDef>)typeof(ItemCatalog).GetField("defs", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var d = new ItemDef { id = id, name = name, category = category, weight = weight, icon = id }; configure(d); defs[id] = d;
    }

    static World NewWorld(int members = 3, int seed = 4242)
    {
        return World.Create(new SandboxSettings { seed = seed, mapSize = 1024, startingMembers = members }, "Mara Vale", "paramedic", true);
    }
    static Simulation Quiet(World w)
    {
        foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, w.community.basePos) < 200) z.state = ZombieState.Dead;
        w.index = new ZombieIndex(w);
        return new Simulation(w);
    }
    static void Calm(World w)
    {
        foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, w.community.basePos) < 120) { z.state = ZombieState.Dead; w.index.Remove(z); }
    }
    static void Tick(Simulation sim, int n = 1) { for (int i = 0; i < n; i++) sim.Tick(Simulation.Step); }
    /// <summary>Skips in-game hours quickly (under an hour per tick so hourly and daily rules run).</summary>
    static void Hours(Simulation sim, float hours, bool calm = true)
    {
        double end = sim.world.minutes + hours * 60;
        while (sim.world.minutes < end) { if (calm) Calm(sim.world); sim.Tick(Simulation.Step, 1500); }
    }
    static void Submit(Simulation sim, Command c) { if (c.recipe == null) c.recipe = ""; sim.Submit(sim.world.playerId, c); }
    static Survivor Member(World w, int i) { return w.survivors[w.community.members[i]]; }
    static bool Has(World w, EventKind kind, string text = null) { return w.events.Any(e => e.kind == kind && (text == null || e.text == text)); }
    static Vec2 Away(World w, float distance)
    {
        // An open outdoor tile about `distance` metres from the base.
        var c = w.community.basePos;
        for (int a = 0; a < 64; a++)
        {
            var p = c + Vec2.FromAngle(a * .3f) * distance; int x = (int)p.x, y = (int)p.y;
            if (w.map.InBounds(x, y) && !w.map.TileBlocked(x, y) && w.map.RoomAt(x, y) == null && !w.map.Collides(new Vec2(x + .5f, y + .5f), Survivor.Radius)) return new Vec2(x + .5f, y + .5f);
        }
        throw new Exception("No open tile " + distance + " m from base.");
    }

    // ---------------------------------------------------------------- cases
    static void Setup(Action<bool, string> check)
    {
        var w = NewWorld(); var c = w.community; var sim = new Simulation(w);
        var lead = w.survivors[0];
        check(c.members.Count == 3 && c.members[0] == lead.id, "leader plus two generated members (" + c.members.Count + ")");
        check(w.playerId == lead.id && lead.ai == AiRole.None && lead.faction == 0, "the leader is controlled");
        check(c.members.Skip(1).All(id => w.survivors[id].ai == AiRole.Resident && w.survivors[id].faction == 0), "other members are residents of faction 0");
        check(c.members.All(id => sim.AtBase(w.survivors[id]) && w.map.BuildingAt(w.survivors[id].pos) != null && w.map.BuildingAt(w.survivors[id].pos).id == c.baseBuilding), "every member starts inside the base");
        check(c.members.Select(id => w.survivors[id].pos).Distinct().Count() == 3 && c.members.All(id => !w.map.Collides(w.survivors[id].pos, Survivor.Radius)), "members stand on separate free tiles");
        check(c.members.Skip(1).Select(id => w.survivors[id].name).Distinct().Count() == 2 && c.members.Skip(1).All(id => w.survivors[id].name != lead.name), "generated members have their own names");
        check(c.members.Skip(1).All(id => Trait.Validate(w.survivors[id].traits) == ""), "generated traits are legal");
        check(c.Stock(Resource.Food) == 12 && c.Stock(Resource.Meds) == 6 && c.Stock(Resource.Ammo) == 6 && c.Stock(Resource.Materials) == 10 && c.Stock(Resource.Fuel) == 4, "starting stock");
        check(c.influence == 100 && c.morale == 60, "starting influence and morale");
        check(c.Level(FacilityKind.Storage) == 1 && c.Level(FacilityKind.Beds) == 1 && c.facilities.Count == 2, "Storage and Beds at level 1");
        check(sim.BaseSlots >= 3 && sim.BaseSlots <= 7, "base slots from the building (" + sim.BaseSlots + ")");
        var added = w.map.props.Skip(w.generatedProps).ToList();
        check(added.Count(p => p.kind == PropKind.Bunk) >= 1 && added.Any(p => p.kind == PropKind.RadioSet) && added.Any(p => p.kind == PropKind.SupplyCrate), "base props placed (" + string.Join(",", added.Select(p => p.kind)) + ")");
        check(added.All(p => p.modified && w.map.BuildingAt(p.Center) != null && w.map.BuildingAt(p.Center).id == c.baseBuilding), "base props are inside the base and saved");
        check(c.members.All(id => !added.Any(p => p.x == (int)w.survivors[id].pos.x && p.y == (int)w.survivors[id].pos.y)), "props are not placed on members");
        var big = NewWorld(5); check(big.community.members.Count == 5, "startingMembers sets the community size");
        var again = NewWorld(); check(again.community.members.Select(id => again.survivors[id].name).SequenceEqual(c.members.Select(id => w.survivors[id].name)), "members are seeded");
        check(w.LivingMembers().Count == 3, "living members query");
    }

    static void Upkeep(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community;
        Hours(sim, 1);
        check(c.Stock(Resource.Food) == 12, "no upkeep before 06:00");
        double toSix = (w.Day * 1440 + 6 * 60 + 5) - w.minutes; Hours(sim, (float)(toSix / 60));
        check(w.Day == 2 && c.lastUpkeepDay == 2, "upkeep ran at 06:00 on day 2");
        float eaten = c.members.Sum(id => Trait.Upkeep(w.survivors[id]));
        check(Math.Abs(c.Stock(Resource.Food) - (12 - eaten)) < .01f, "each member eats its ration (" + c.Stock(Resource.Food) + ")");
        check(w.events.Any(e => e.kind == EventKind.Radio && e.text.StartsWith("Day 2 report")), "a radio summary goes to the controlled survivor");
        check(c.lastReport.Contains("Food"), "the report is kept for the UI");
        // Shortage: no food hurts morale and tires everyone.
        c.stock[(int)Resource.Food] = 0; c.morale = 60; float fatigue = Member(w, 1).fatigue;
        w.events.Clear();
        Hours(sim, 24);
        check(c.morale < 55, "a Food shortfall hurts morale (" + c.morale.ToString("0") + ")");
        check(Has(w, EventKind.Morale, "down"), "morale events are emitted");
        check(c.lastReport.Contains("not enough Food"), "the shortfall is reported");
        // A Garden feeds the community.
        c.facilities.Add(new Facility { kind = FacilityKind.Garden, level = 2 }); c.stock[(int)Resource.Food] = 0;
        Hours(sim, 24);
        check(c.Stock(Resource.Food) > 0, "a level 2 Garden grows Food (" + c.Stock(Resource.Food) + ")");
        // Storage caps stock.
        c.stock[(int)Resource.Ammo] = 999; Hours(sim, 24);
        check(c.Stock(Resource.Ammo) <= c.StorageCap, "Storage caps stock (" + c.Stock(Resource.Ammo) + "/" + c.StorageCap + ")");
        // Watchtower uses Fuel.
        c.facilities.Add(new Facility { kind = FacilityKind.Watchtower }); float fuel = c.Stock(Resource.Fuel); Hours(sim, 24);
        check(c.Stock(Resource.Fuel) == fuel - 1, "the watchtower burns 1 Fuel a day");
        // Very low morale makes someone leave (eventually).
        int before = w.LivingMembers().Count; c.facilities.RemoveAll(f => f.kind == FacilityKind.Garden);
        for (int i = 0; i < 12 && w.LivingMembers().Count == before; i++) { c.morale = 0; c.stock[(int)Resource.Food] = 0; Hours(sim, 24); }
        check(w.LivingMembers().Count == before - 1, "a member leaves when morale collapses");
        var left = w.survivors.FirstOrDefault(s => s.alive && s.faction != 0 && s.ai == AiRole.Enclave);
        check(left != null && !c.members.Contains(left.id) && left.id != w.playerId, "the leaver is no longer a member or the controlled survivor");
        check(Has(w, EventKind.KillFeed) && w.events.Any(e => e.kind == EventKind.Message && e.text.Contains("leaves the community")), "leaving is announced");
    }

    static void Rucksacks(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; var s = w.Controlled;
        var sack = ItemCatalog.Make("rucksack_food"); s.inventory.Add(sack);
        var home = s.pos; s.pos = Away(w, 120);
        Submit(sim, new Command { kind = CommandKind.DepositRucksack, item = sack.uid }); Tick(sim);
        check(s.inventory.ByUid(sack.uid) != null && c.Stock(Resource.Food) == 12, "deposits only at base");
        s.pos = home;
        Submit(sim, new Command { kind = CommandKind.DepositRucksack, item = sack.uid }); Tick(sim);
        check(s.inventory.ByUid(sack.uid) == null && c.Stock(Resource.Food) == 12 + sack.Def.resourceAmount, "a carried rucksack adds its stock (" + c.Stock(Resource.Food) + ")");
        check(c.influence == 110 && Has(w, EventKind.Influence, "+10"), "a deposit earns 10 influence");
        // Every rucksack carried plus a car trunk at the base.
        s.inventory.Add(ItemCatalog.Make("rucksack_materials")); s.inventory.Add(ItemCatalog.Make("rucksack_meds"));
        var v = w.vehicles.OrderBy(x => Vec2.Distance(x.pos, c.basePos)).FirstOrDefault();
        check(v != null, "the county has a drivable car");
        if (v == null) return;
        w.UnblockVehicle(v); v.pos = Away(w, 10); v.heading = 0; w.BlockVehicle(v);
        var trunk = w.map.containers[w.map.props[v.prop].container]; w.RollContainer(trunk);
        trunk.inventory.Add(ItemCatalog.Make("rucksack_materials"));
        float materials = c.Stock(Resource.Materials), meds = c.Stock(Resource.Meds);
        Submit(sim, Command.Of(CommandKind.DepositRucksack, -1)); Tick(sim);
        check(c.Stock(Resource.Materials) == materials + 10 && c.Stock(Resource.Meds) == meds + 3, "deposit-all takes carried and trunk rucksacks (" + c.Stock(Resource.Materials) + ", " + c.Stock(Resource.Meds) + ")");
        check(!trunk.inventory.items.Any(i => i.Def.resource >= 0) && !s.inventory.items.Any(i => i.Def.resource >= 0), "the rucksacks are used up");
        check(c.influence == 140, "three more deposits, 30 more influence (" + c.influence + ")");
        // A trunk far from base is ignored.
        w.UnblockVehicle(v); v.pos = Away(w, 90); w.BlockVehicle(v); trunk.inventory.Add(ItemCatalog.Make("rucksack_food"));
        Submit(sim, Command.Of(CommandKind.DepositRucksack, -1)); Tick(sim);
        check(trunk.inventory.items.Any(i => i.def == "rucksack_food"), "a car parked far away is not unloaded");
        // Full storage refuses rather than wasting the rucksack.
        c.stock[(int)Resource.Food] = c.StorageCap; var extra = ItemCatalog.Make("rucksack_food"); s.inventory.Add(extra);
        Submit(sim, new Command { kind = CommandKind.DepositRucksack, item = extra.uid }); Tick(sim);
        check(s.inventory.ByUid(extra.uid) != null, "full storage keeps the rucksack");
    }

    static void Facilities(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; string why;
        check(!sim.CanBuild(FacilityKind.Infirmary, out why) && why.Contains("Materials"), "an Infirmary needs 20 Materials (" + why + ")");
        check(!sim.CanBuild(FacilityKind.Storage, out why), "Storage is already built");
        c.stock[(int)Resource.Materials] = 40;
        check(sim.CanBuild(FacilityKind.Workshop, out why), "a Workshop can be built (" + why + ")");
        Submit(sim, Command.Of(CommandKind.BuildFacility, -1, (int)FacilityKind.Workshop)); Tick(sim);
        var f = c.Get(FacilityKind.Workshop);
        check(f != null && f.Busy && c.Level(FacilityKind.Workshop) == 0, "construction starts and is not usable yet");
        check(c.Stock(Resource.Materials) == 40 - Simulation.FacilityMaterials(FacilityKind.Workshop, 1), "materials are spent");
        check(Has(w, EventKind.Facility, "start"), "construction start is announced");
        check(!sim.CanBuild(FacilityKind.Garden, out why) && why.Contains("first"), "one construction at a time");
        Hours(sim, Simulation.FacilityHours(FacilityKind.Workshop, 1) * .5f);
        check(f.Busy, "construction takes hours");
        Hours(sim, Simulation.FacilityHours(FacilityKind.Workshop, 1) * .6f + .1f);
        check(!f.Busy && c.Level(FacilityKind.Workshop) == 1 && Has(w, EventKind.Facility, "built"), "the Workshop is built");
        var prop = f.prop >= 0 ? w.map.props[f.prop] : null;
        check(prop != null && prop.kind == PropKind.Workbench && prop.modified && w.map.BuildingAt(prop.Center) != null && w.map.BuildingAt(prop.Center).id == c.baseBuilding, "a workbench appears inside the base");
        // Upgrade keeps the level working while it runs.
        c.stock[(int)Resource.Materials] = 40;
        Submit(sim, Command.Of(CommandKind.UpgradeFacility, -1, (int)FacilityKind.Workshop)); Tick(sim);
        check(f.Busy && f.upgrading && c.Level(FacilityKind.Workshop) == 1, "the upgrade is underway and level 1 still works");
        Hours(sim, Simulation.FacilityHours(FacilityKind.Workshop, 2) + .2f);
        check(c.Level(FacilityKind.Workshop) == 2 && Has(w, EventKind.Facility, "upgraded"), "the Workshop reaches level 2");
        check(c.generatorProp >= 0 && w.map.props[c.generatorProp].kind == PropKind.Generator, "a generator is placed for the first level-2 facility");
        // An outdoor facility goes outside.
        c.stock[(int)Resource.Materials] = 40;
        Submit(sim, Command.Of(CommandKind.BuildFacility, -1, (int)FacilityKind.Watchtower)); Hours(sim, 4);
        var tower = c.Get(FacilityKind.Watchtower); var tp = tower != null && tower.prop >= 0 ? w.map.props[tower.prop] : null;
        check(tp != null && tp.kind == PropKind.Watchtower && w.map.RoomAt(tp.x, tp.y) == null, "the watchtower stands outside the base");
        var guard = c.garrison >= 0 ? w.survivors[c.garrison] : null;
        check(guard != null && guard.ai == AiRole.Resident && guard.id != w.playerId && Vec2.Distance(guard.pos, sim.GarrisonPost) < .1f, "a resident is posted at the watchtower");
        // Slots run out.
        c.stock[(int)Resource.Materials] = 500;
        foreach (FacilityKind k in new[] { FacilityKind.Infirmary, FacilityKind.Garden, FacilityKind.Kitchen, FacilityKind.RadioRoom })
            if (sim.CanBuild(k, out why)) { Submit(sim, Command.Of(CommandKind.BuildFacility, -1, (int)k)); Hours(sim, 5); }
        check(sim.UsedSlots <= sim.BaseSlots, "facilities never exceed the base slots (" + sim.UsedSlots + "/" + sim.BaseSlots + ")");
        check(sim.UsedSlots == Math.Min(sim.BaseSlots, 6), "every facility that fits was built (" + sim.UsedSlots + ")");
        // Building away from base is refused.
        var s = w.Controlled; s.pos = Away(w, 120); c.stock[(int)Resource.Materials] = 500;
        Submit(sim, Command.Of(CommandKind.UpgradeFacility, -1, (int)FacilityKind.Beds)); Tick(sim);
        check(c.Get(FacilityKind.Beds).level == 1 && !c.Get(FacilityKind.Beds).Busy, "facilities are built from the base");
    }

    static void Workshop(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; var s = w.Controlled;
        check(WorkshopRecipe.Recipes.Count >= 6 && WorkshopRecipe.Recipes.All(r => r.CostLabel.Length > 0), "workshop recipes are listed for the UI");
        check(sim.WorkshopProblem(s, "molotov").Contains("Workshop"), "a molotov needs a Workshop");
        c.facilities.Add(new Facility { kind = FacilityKind.Workshop, level = 1 });
        float fuel = c.Stock(Resource.Fuel), mats = c.Stock(Resource.Materials);
        Submit(sim, new Command { kind = CommandKind.CraftAtWorkshop, recipe = "molotov" }); Tick(sim);
        check(s.inventory.Count("molotov") == 2 && c.Stock(Resource.Fuel) == fuel - 1 && c.Stock(Resource.Materials) == mats - 1, "a molotov costs Fuel and Materials");
        check(sim.WorkshopProblem(s, "grenade").Contains("level 2"), "grenades need a level 2 Workshop");
        var bat = ItemCatalog.Make("baseball_bat"); bat.condition = .3f; s.inventory.Add(bat);
        Submit(sim, new Command { kind = CommandKind.CraftAtWorkshop, recipe = "repair_weapon" }); Tick(sim);
        check(bat.condition == 1, "weapons are repaired");
        int bandages = s.inventory.Count("bandage");
        Submit(sim, new Command { kind = CommandKind.CraftAtWorkshop, recipe = "bandage" }); Tick(sim);
        check(s.inventory.Count("bandage") == bandages + 3, "bandages come from Meds");
        Submit(sim, new Command { kind = CommandKind.CraftAtWorkshop, recipe = "draw_ammo" }); Tick(sim);
        check(s.inventory.Count("ammo_9mm") >= 15, "ammo is drawn from the stock");
        check(sim.WorkshopProblem(s, "plague_cure").Contains("Infirmary"), "plague cures need an Infirmary");
        c.facilities.Add(new Facility { kind = FacilityKind.Infirmary, level = 1 });
        check(sim.WorkshopProblem(s, "plague_cure").Contains("Plague Sample"), "plague cures need samples");
        s.inventory.Add(ItemCatalog.Make("plague_sample", 3));
        Submit(sim, new Command { kind = CommandKind.CraftAtWorkshop, recipe = "plague_cure" }); Tick(sim);
        check(s.inventory.Count("plague_cure") == 1 && s.inventory.Count("plague_sample") == 0, "3 plague samples and 1 Meds make a cure");
        s.pos = Away(w, 120);
        check(sim.WorkshopProblem(s, "bandage").Contains("base"), "crafting happens at the base");
        // Old field crafting is retired.
        check(Recipe.Get("stew") != null && Recipe.Get("stew").Missing(s, true).Count > 0 && Recipe.All.All(r => r.retired == ""), "Zomboid recipes are retired");
    }

    static void Switching(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; string why;
        var lead = w.Controlled; var other = Member(w, 1);
        check(sim.CanSwitchTo(other.id, out why), "can switch to a member at base (" + why + ")");
        check(!sim.CanSwitchTo(lead.id, out why), "cannot switch to yourself");
        var home = other.pos; other.pos = Away(w, 120);
        check(!sim.CanSwitchTo(other.id, out why) && why.Contains("away"), "a member away from base cannot be taken over");
        other.pos = home; var leadHome = lead.pos; lead.pos = Away(w, 120);
        check(!sim.CanSwitchTo(other.id, out why) && why.Contains("Return"), "the controlled survivor must be at base");
        lead.pos = leadHome;
        Submit(sim, Command.Of(CommandKind.SwitchCharacter, -1, other.id)); Tick(sim);
        check(w.playerId == other.id && other.ai == AiRole.None && lead.ai == AiRole.Resident, "switching hands control over and the old member becomes a resident");
        check(Has(w, EventKind.Switch), "Switch is emitted");
        var outsider = w.AddSurvivor("Stranger", "unemployed", false, null); outsider.faction = 3; outsider.pos = other.pos;
        check(!sim.CanSwitchTo(outsider.id, out why), "cannot switch to an outsider");
        sim.Kill(lead, "test");
        check(!sim.CanSwitchTo(lead.id, out why), "cannot switch to the dead");
    }

    static void Companion(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community;
        var lead = w.Controlled; var a = Member(w, 1); var b = Member(w, 2);
        Submit(sim, Command.Of(CommandKind.SetCompanion, -1, a.id)); Tick(sim);
        check(c.companion == a.id && a.ai == AiRole.Companion && a.leader == lead.id, "a member becomes the companion");
        Submit(sim, Command.Of(CommandKind.SwitchCharacter, -1, b.id)); Tick(sim);
        check(w.playerId == b.id && a.leader == b.id && c.companion == a.id, "the companion follows the newly controlled survivor");
        Submit(sim, Command.Of(CommandKind.SetCompanion, -1, lead.id)); Tick(sim);
        check(c.companion == lead.id && a.ai == AiRole.Resident && a.leader == -1 && lead.ai == AiRole.Companion, "choosing another companion dismisses the first");
        Submit(sim, Command.Of(CommandKind.SetCompanion, -1, -1)); Tick(sim);
        check(c.companion == -1 && lead.ai == AiRole.Resident, "the companion is dismissed");
        Submit(sim, Command.Of(CommandKind.SetCompanion, -1, b.id)); Tick(sim);
        check(c.companion == -1, "the controlled survivor cannot be the companion");
        // Switching to the companion makes them controlled, not following.
        Submit(sim, Command.Of(CommandKind.SetCompanion, -1, a.id)); Tick(sim);
        Submit(sim, Command.Of(CommandKind.SwitchCharacter, -1, a.id)); Tick(sim);
        check(w.playerId == a.id && a.ai == AiRole.None && c.companion == -1, "taking over the companion ends the follow");
    }

    static Survivor Npc(World w, Vec2 at, int faction, AiRole role)
    {
        var n = w.AddSurvivor("Nell Cotter", "farmer", true, null); n.pos = at; n.faction = faction; n.ai = role; return n;
    }

    static void Recruiting(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; var s = w.Controlled; string why;
        w.factions.Add(new Faction { id = w.factions.Count, name = "Cedar Fork Enclave", role = AiRole.Enclave, disposition = Disposition.Friendly, standing = 10 });
        int enclave = w.factions.Count - 1;
        var npc = Npc(w, s.pos + new Vec2(1, 0), enclave, AiRole.Enclave);
        check(!sim.CanRecruit(s, npc, out why) && why.Contains("trust"), "low standing refuses (" + why + ")");
        w.factions[enclave].standing = 30;
        check(sim.CanRecruit(s, npc, out why), "a friendly enclave member can be recruited (" + why + ")");
        npc.pos = s.pos + new Vec2(8, 0);
        check(!sim.CanRecruit(s, npc, out why) && why.Contains("closer"), "recruiting needs to be face to face");
        npc.pos = s.pos + new Vec2(1, 0);
        Submit(sim, Command.Of(CommandKind.Recruit, -1, npc.id)); Tick(sim);
        check(npc.faction == 0 && npc.ai == AiRole.Resident && c.members.Contains(npc.id), "the recruit joins as a resident");
        check(c.influence == 0, "recruiting costs 100 influence (" + c.influence + ")");
        check(Has(w, EventKind.Recruit) && w.events.Any(e => e.kind == EventKind.KillFeed && e.text.Contains("joined")), "Recruit and a kill-feed line are emitted");
        var second = Npc(w, s.pos + new Vec2(-1, 0), enclave, AiRole.Enclave);
        check(!sim.CanRecruit(s, second, out why) && why.Contains("influence"), "not enough influence (" + why + ")");
        c.influence = 500;
        check(!sim.CanRecruit(s, second, out why) && why.Contains("bed"), "Beds level 1 holds four (" + why + ")");
        var raider = Npc(w, s.pos + new Vec2(0, 1), enclave, AiRole.Raider); w.factions[enclave].standing = 30;
        c.facilities[1].level = 2;
        check(!sim.CanRecruit(s, raider, out why), "raiders will not join");
        check(sim.CanRecruit(s, second, out why), "more beds allow another recruit (" + why + ")");
        w.factions[enclave].standing = -80;
        check(!sim.CanRecruit(s, second, out why) && why.Contains("hostile"), "hostile groups cannot be recruited");
    }

    static Building OutpostSite(World w, Simulation sim)
    {
        var c = w.community;
        return w.map.buildings.Where(b => b.id != c.baseBuilding && b.rooms.Count > 0).OrderBy(b => Math.Abs(Vec2.Distance(b.rect.Center, c.basePos) - 80)).First();
    }

    static void Outposts(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; var s = w.Controlled; string why;
        var b = OutpostSite(w, sim);
        var door = w.map.openings.First(o => o.building == b.id && o.exterior && o.kind == Edge.Door);
        s.pos = door.Center + (door.vertical ? new Vec2(w.map.RoomAt(door.x, door.y) == null ? -.6f : .6f, 0) : new Vec2(0, w.map.RoomAt(door.x, door.y) == null ? -.6f : .6f));
        foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, b.rect.Center) < 60) { z.state = ZombieState.Dead; }
        w.index = new ZombieIndex(w);
        b.visited = false;
        check(!sim.CanClaimOutpost(s, b, out why) && why.Contains("Search"), "an unsearched building cannot be claimed (" + why + ")");
        b.visited = true;
        var z0 = w.AddZombie(b.rect.Center + new Vec2(0, 2), w.rng); z0.state = ZombieState.Idle;
        check(!sim.CanClaimOutpost(s, b, out why) && why.Contains("zombies"), "zombies nearby block the claim (" + why + ")");
        z0.state = ZombieState.Dead; w.index.Remove(z0);
        check(sim.CanClaimOutpost(s, b, out why), "a cleared, searched building can be claimed (" + why + ")");
        Submit(sim, Command.Of(CommandKind.ClaimOutpost, -1, b.id)); Tick(sim);
        var o = c.OutpostAt(b.id);
        check(o != null && c.influence == 0, "claiming costs 100 influence");
        check(o != null && o.prop >= 0 && w.map.props[o.prop].kind == PropKind.OutpostFlag && w.map.RoomAt(w.map.props[o.prop].x, w.map.props[o.prop].y) == null, "a flag goes up outside");
        float amount; var bonus = Community.OutpostBonus(b.kind, out amount);
        check(o != null && o.bonus == bonus, "the bonus follows the building kind (" + b.kind + " -> " + bonus + ")");
        check(!sim.CanClaimOutpost(s, b, out why), "it cannot be claimed twice");
        check(sim.MaxOutposts >= 2 && sim.MaxOutposts <= 4, "2-4 outposts (" + sim.MaxOutposts + ")");
        float before = c.Stock(bonus); s.pos = w.survivors[c.members[1]].pos;
        double toSix = (w.Day * 1440 + 6 * 60 + 5) - w.minutes; Hours(sim, (float)(toSix / 60));
        float eat = bonus == Resource.Food ? c.members.Sum(id => Trait.Upkeep(w.survivors[id])) : 0;
        check(Math.Abs(c.Stock(bonus) - Math.Min(c.StorageCap, before + amount - eat)) < .01f, "the outpost delivers its daily bonus (" + before + " -> " + c.Stock(bonus) + ")");
        Submit(sim, Command.Of(CommandKind.AbandonOutpost, -1, b.id)); Tick(sim);
        check(c.outposts.Count == 0 && o != null && w.map.props[o.prop].removed, "abandoning removes the outpost and its flag");
    }

    static void Missions(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; var s = w.Controlled;
        // Offers come over the radio every few hours.
        Hours(sim, 10);
        check(c.missions.Count(m => m.status == MissionStatus.Offered) >= 1 && c.missions.Count(m => m.status == MissionStatus.Offered) <= sim.MissionOfferLimit, "missions are offered over time (" + c.missions.Count + ")");
        check(Has(w, EventKind.Mission, "offered"), "offers are announced");
        c.missions.Clear();
        // Rescue: reach the survivor and they join for free.
        var rescue = sim.OfferMission(MissionKind.Rescue);
        check(rescue != null && rescue.targetId >= 0 && rescue.expiresAt > w.minutes + 5 * 60 && rescue.expiresAt <= w.minutes + 25 * 60, "a rescue is offered with an expiry");
        if (rescue == null) return;
        var lost = w.survivors[rescue.targetId];
        check(lost.alive && lost.faction != 0 && Vec2.Distance(lost.pos, c.basePos) > 50, "the survivor waits away from base");
        check(w.zombies.Count(z => z.Alive && Vec2.Distance(z.pos, lost.pos) < 25) >= 3, "the dead surround them");
        Submit(sim, Command.Of(CommandKind.AcceptMission, -1, rescue.id)); Tick(sim);
        check(rescue.status == MissionStatus.Active && Has(w, EventKind.Mission, "accepted"), "the mission is accepted");
        foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, lost.pos) < 40) z.state = ZombieState.Dead;
        w.index = new ZombieIndex(w);
        s.pos = lost.pos + new Vec2(.8f, 0); if (w.map.Collides(s.pos, Survivor.Radius)) s.pos = lost.pos;
        float influence = c.influence; Tick(sim, 12);
        check(rescue.status == MissionStatus.Completed && lost.faction == 0 && c.members.Contains(lost.id), "reaching them completes the rescue and they join");
        check(c.influence == influence + rescue.rewardInfluence && Has(w, EventKind.Mission, "completed"), "the reward is paid");
        // Clear infestation: destroy the heart.
        w.hearts.Clear(); w.hearts.Add(new PlagueHeart { id = 0, pos = Away(w, 150) });
        var clear = sim.OfferMission(MissionKind.ClearInfestation);
        check(clear != null && clear.targetId == 0 && w.hearts[0].discovered, "a clear-infestation mission marks the heart");
        Submit(sim, Command.Of(CommandKind.AcceptMission, -1, clear.id)); Tick(sim);
        sim.DamageHeart(w.hearts[0], s, 999); Tick(sim, 12);
        check(clear.status == MissionStatus.Completed, "destroying the heart completes it");
        // Kill a freak: offered only when one exists; expires if ignored.
        foreach (var z in w.zombies) if (z.Alive && Bestiary.Get(z.kind).threat >= 3) { z.state = ZombieState.Dead; w.index.Remove(z); }
        check(sim.OfferMission(MissionKind.KillFreak) == null, "no freak, no freak mission");
        var feral = w.AddZombie(Away(w, 100), w.rng); feral.kind = ZombieKind.Feral; feral.state = ZombieState.Idle; feral.timer = 999;
        var hunt = sim.OfferMission(MissionKind.KillFreak);
        check(hunt != null && hunt.targetId == feral.id, "a freak mission targets the feral");
        sim.world.minutes = hunt.expiresAt + 1; Tick(sim, 12);
        check(hunt.status == MissionStatus.Expired, "an ignored offer expires");
        // Defend: accepting brings the horde; killing most of it wins.
        var defend = sim.OfferMission(MissionKind.Defend) ?? c.missions.FirstOrDefault(x => x.status == MissionStatus.Offered && x.kind == MissionKind.Defend);
        check(defend != null, "a siege can be offered after the first half day");
        if (defend == null) return;
        Submit(sim, Command.Of(CommandKind.AcceptMission, -1, defend.id)); Tick(sim);
        check(defend.targets.Count >= 8 && Has(w, EventKind.Siege, "start"), "the horde spawns near the base (" + defend.targets.Count + ")");
        check(defend.targets.All(id => w.zombies[id].state == ZombieState.Investigate && Vec2.Distance(w.zombies[id].target, c.basePos) < 6), "the horde heads for the base");
        foreach (int id in defend.targets) { var z = w.zombies[id]; z.state = ZombieState.Dead; w.index.Remove(z); }
        Tick(sim, 12);
        check(defend.status == MissionStatus.Completed && Has(w, EventKind.Siege, "end"), "breaking the siege completes the defence");
        // Abandoning an active mission fails it.
        var again = sim.OfferMission(MissionKind.Rescue);
        if (again != null)
        {
            Submit(sim, Command.Of(CommandKind.AcceptMission, -1, again.id)); Tick(sim);
            Submit(sim, Command.Of(CommandKind.AbandonMission, -1, again.id)); Tick(sim);
            check(again.status == MissionStatus.Failed && Has(w, EventKind.Mission, "failed"), "abandoning fails the mission");
        }
        check(sim.MissionBoard().Count == c.missions.Count, "the mission board lists every mission");
    }

    static void Plague(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; var s = w.Controlled;
        sim.InfectPlague(s);
        check(s.plagueInfected && Has(w, EventKind.Plague, "infected"), "infection is announced");
        double start = w.minutes; Hours(sim, 10);
        check(s.plague > 40 && s.plague < 60 && s.alive, "the plague rises about 100 over 18-24 hours (" + s.plague.ToString("0") + " after 10 h)");
        check(Simulation.StaminaCap(s) < 100, "the plague wears stamina down");
        var cure = ItemCatalog.Make("plague_cure"); s.inventory.Add(cure);
        Submit(sim, Command.Of(CommandKind.Use, cure.uid)); Tick(sim, 50);
        check(!s.plagueInfected && s.plague == 0 && s.inventory.Count("plague_cure") == 0 && Has(w, EventKind.Plague, "cured"), "a plague cure ends it");
        var spare = ItemCatalog.Make("plague_cure"); s.inventory.Add(spare);
        Submit(sim, Command.Of(CommandKind.Use, spare.uid)); Tick(sim, 50);
        check(s.inventory.Count("plague_cure") == 1, "a healthy survivor keeps the cure");
        // The infirmary cures a member at base with Meds.
        var m = Member(w, 1); sim.InfectPlague(m);
        c.facilities.Add(new Facility { kind = FacilityKind.Infirmary, level = 1 });
        float meds = c.Stock(Resource.Meds); Tick(sim, 12);
        check(!m.plagueInfected && c.Stock(Resource.Meds) == meds - Simulation.InfirmaryPlagueMeds, "the infirmary treats the plague with Meds");
        // Untreated, it turns: death and a plague zombie.
        var victim = Member(w, 2); c.facilities.Clear(); victim.pos = Away(w, 120);
        sim.InfectPlague(victim);
        double t0 = w.minutes; Hours(sim, 30);
        check(!victim.alive && victim.causeOfDeath.Contains("plague"), "untreated blood plague kills (" + victim.causeOfDeath + ")");
        var turned = w.events.FirstOrDefault(e => e.kind == EventKind.Plague && e.text == "turned");
        check(turned.kind == EventKind.Plague && turned.zombie >= 0 && w.zombies[turned.zombie].plague && Vec2.Distance(w.zombies[turned.zombie].pos, victim.pos) < .1f, "the victim rises as a plague zombie");
        // Zomboid's zombie infection no longer kills.
        var w2 = NewWorld(); var sim2 = Quiet(w2); var b = w2.Controlled; b.zombieInfected = true; Hours(sim2, 48);
        check(b.alive && b.health > 90, "the old zombie infection is harmless");
    }

    static void Injury(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; var s = w.Controlled;
        Tick(sim);
        s.health -= 20; Tick(sim);
        check(s.injury > 10 && s.MaxHealth < 100, "a serious hit leaves an injury (" + s.injury.ToString("0") + ")");
        s.health -= 3; float injury = s.injury; Tick(sim);
        check(Math.Abs(s.injury - injury) < .05f, "small knocks do not");
        // Away from base, injuries heal slowly; at base with an infirmary much faster.
        var home = s.pos; s.pos = Away(w, 120); s.injury = 30; Hours(sim, 4); float field = 30 - s.injury;
        s.pos = home; s.injury = 30; c.facilities.Add(new Facility { kind = FacilityKind.Infirmary }); Hours(sim, 4); float infirmary = 30 - s.injury;
        check(field > 0 && infirmary > field * 5, "the infirmary at base heals injuries much faster (" + field.ToString("0.0") + " vs " + infirmary.ToString("0.0") + ")");
        check(s.health <= s.MaxHealth + .01f, "health never exceeds 100 - injury");
        // Health regenerates toward the maximum.
        s.injury = 0; s.health = 50; Hours(sim, 6);
        check(s.health > 60, "health regenerates (" + s.health.ToString("0") + ")");
        // Resting at base: fast-forward until rested.
        s.fatigue = 80; s.health = 70; s.injury = 10;
        Submit(sim, Command.Of(CommandKind.Rest)); Tick(sim);
        check(s.sleeping && sim.FastForward, "Rest at base fast-forwards time");
        double t = w.minutes; for (int i = 0; i < 400 && s.sleeping; i++) sim.Advance(.25f);
        check(!s.sleeping && s.fatigue < 5, "rest ends when the member is rested (" + ((w.minutes - t) / 60).ToString("0.0") + " h)");
        s.pos = Away(w, 120); s.fatigue = 50;
        Submit(sim, Command.Of(CommandKind.Rest)); Tick(sim);
        check(!s.sleeping, "Rest needs the base");
        // Fatigue: rises awake, slows and caps stamina when high.
        s.pos = home; s.fatigue = 10; Hours(sim, 5);
        check(s.fatigue > 15, "fatigue rises with time awake (" + s.fatigue.ToString("0") + ")");
        s.fatigue = 95; check(Simulation.StaminaCap(s) < 50 && Simulation.FatigueDamage(s) < 1, "a worn-out member has less stamina and hits softer");
        // Food and energy drinks restore stamina; hunger and thirst stay retired.
        s.fatigue = 10; s.endurance = 20; var drink = ItemCatalog.Make("energy_drink"); s.inventory.Add(drink);
        Submit(sim, Command.Of(CommandKind.Use, drink.uid)); Tick(sim, 40);
        check(s.endurance >= 55 && s.inventory.Count("energy_drink") == 0, "an energy drink restores stamina (" + s.endurance.ToString("0") + ")");
        s.endurance = 20; s.health = 60; var bar = ItemCatalog.Make("granola"); s.inventory.Add(bar);
        Submit(sim, Command.Of(CommandKind.Use, bar.uid)); Tick(sim, 60);
        check(s.endurance > 30 && s.health > 60 && s.hunger == 0 && s.thirst == 0, "food restores a little stamina and health");
        var kit = ItemCatalog.Make("first_aid_kit"); s.inventory.Add(kit); s.health = 50; Tick(sim); s.injury = 20;
        Submit(sim, Command.Of(CommandKind.Use, kit.uid)); Tick(sim, 60);
        check(s.inventory.Count("first_aid_kit") == 0 && s.injury < 15 && s.health > 55, "meds heal");
    }

    static void Death(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community;
        var lead = w.Controlled; var a = Member(w, 1); var b = Member(w, 2);
        Submit(sim, Command.Of(CommandKind.SetCompanion, -1, b.id)); Tick(sim);
        a.health = 100; b.health = 40;
        float morale = c.morale;
        sim.Kill(lead, "test");
        check(w.playerId == b.id && b.ai == AiRole.None, "control passes to the companion first");
        check(c.morale < morale && Has(w, EventKind.Switch), "a death costs morale and the switch is announced");
        sim.Kill(b, "test");
        check(w.playerId == a.id && w.outcome == "", "then to the healthiest member");
        sim.Kill(a, "test");
        check(w.outcome == "dead", "the community is gone: outcome dead");
    }

    static void Legacy(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community;
        Hours(sim, .5f);
        check(w.outcome == "", "no legacy without hearts");
        w.hearts.Clear(); w.hearts.Add(new PlagueHeart { id = 0, pos = Away(w, 200) }); w.hearts.Add(new PlagueHeart { id = 1, pos = Away(w, 220) });
        sim.DamageHeart(w.hearts[0], w.Controlled, 999); float influence = c.influence; Tick(sim, 12);
        check(w.outcome == "" && c.heartsDestroyed == 1 && c.influence >= influence, "one heart down, one to go");
        check(w.events.Any(e => e.kind == EventKind.Radio && e.text.Contains("1 remains")), "legacy progress on the radio");
        sim.DamageHeart(w.hearts[1], w.Controlled, 999); Tick(sim, 2);
        check(w.outcome == "legacy" && Has(w, EventKind.Legacy), "every heart destroyed: outcome legacy");
        check(w.flags.Contains("radio-1"), "Ada Pike's emergency band introduces the community");
        check(!w.events.Any(e => e.text != null && e.text.Contains("convoy")), "the evacuation convoy is gone");
    }

    static void SaveLoad(Action<bool, string> check)
    {
        var w = NewWorld(); var sim = Quiet(w); var c = w.community; var s = w.Controlled;
        c.stock[(int)Resource.Materials] = 40; c.influence = 345; c.morale = 72;
        Submit(sim, Command.Of(CommandKind.BuildFacility, -1, (int)FacilityKind.Infirmary)); Hours(sim, 1);
        var a = Member(w, 1); Submit(sim, Command.Of(CommandKind.SetCompanion, -1, a.id)); Tick(sim);
        var m = sim.OfferMission(MissionKind.Rescue); Submit(sim, Command.Of(CommandKind.AcceptMission, -1, m.id)); Tick(sim);
        var b = OutpostSite(w, sim); c.outposts.Add(new Outpost { building = b.id, bonus = Resource.Ammo, amount = 2, pos = b.rect.Center });
        var victim = Member(w, 2); victim.plagueInfected = true; victim.plague = 33; victim.injury = 21;
        string path = Path.Combine(Path.GetTempPath(), "ashfall-community-" + Guid.NewGuid().ToString("N") + ".sav");
        try
        {
            SaveCodec.Save(w, path);
            var l = SaveCodec.Load(path); var lc = l.community;
            check(lc.members.SequenceEqual(c.members) && lc.baseBuilding == c.baseBuilding && Vec2.Distance(lc.basePos, c.basePos) < .01f, "members and base restored");
            check(lc.stock.SequenceEqual(c.stock) && lc.influence == 345 && Math.Abs(lc.morale - c.morale) < .01f, "stock, influence and morale restored");
            var lf = lc.Get(FacilityKind.Infirmary); var f = c.Get(FacilityKind.Infirmary);
            check(lf != null && lf.Busy && Math.Abs(lf.buildHours - f.buildHours) < .01f && lc.facilities.Count == c.facilities.Count, "construction in progress restored");
            check(lc.companion == a.id && l.survivors[a.id].leader == s.id && l.survivors[a.id].ai == AiRole.Companion, "companion restored");
            var lm = lc.MissionById(m.id);
            check(lm != null && lm.status == MissionStatus.Active && lm.kind == MissionKind.Rescue && lm.targetId == m.targetId && lm.title == m.title && Math.Abs(lm.expiresAt - m.expiresAt) < .01 && lc.nextMission == c.nextMission, "missions restored");
            check(lc.outposts.Count == 1 && lc.outposts[0].building == b.id && lc.outposts[0].bonus == Resource.Ammo, "outposts restored");
            var lv = l.survivors[victim.id];
            check(lv.plagueInfected && Math.Abs(lv.plague - 33) < .01f && Math.Abs(lv.injury - 21) < .01f, "per-survivor plague and injury restored");
            check(lc.lastUpkeepDay == c.lastUpkeepDay && Math.Abs(lc.nextOfferAt - c.nextOfferAt) < .01, "schedules restored");
            var props = l.map.props.Skip(l.generatedProps).Select(p => p.kind).ToList();
            check(props.Contains(PropKind.Bunk) && props.Contains(PropKind.RadioSet), "base props restored");
            // The restored community keeps working.
            var lsim = new Simulation(l); Hours(lsim, 6);
            check(lc.Level(FacilityKind.Infirmary) == 1, "construction finishes after loading");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
