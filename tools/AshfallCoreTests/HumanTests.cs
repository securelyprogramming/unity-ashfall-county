using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ashfall.Core;

/// <summary>Human checks. Register each case with run("name", () => ...); assert with check(condition, "what").</summary>
static class HumanTests
{
    static Action<bool, string> Check;

    public static void All(Action<string, Action> run, Action<bool, string> check)
    {
        Check = check;
        EnsureThrowables();
        run("humans-setup", Setup);
        run("humans-crew-loots", CrewLoots);
        run("humans-companion", CompanionFollowsAndFights);
        run("humans-dispositions", Dispositions);
        run("humans-friendly-fire", FriendlyFire);
        run("humans-armor", Armor);
        run("humans-grenade", Grenade);
        run("humans-molotov", Molotov);
        run("humans-lod", Lod);
        run("humans-trade", Trade);
        run("humans-kill-feed", KillFeed);
        run("humans-save-load", SaveLoad);
        run("humans-perf", Perf);
    }

    /// <summary>core-world owns the throwable ids; until they exist on this branch, register stand-ins with the contract fields.</summary>
    static void EnsureThrowables()
    {
        var add = typeof(ItemCatalog).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Static);
        if (add == null) return;
        if (!ItemCatalog.Exists("grenade")) add.Invoke(null, new object[] { "grenade", "Frag Grenade", ItemCategory.Weapon, .4f, (Action<ItemDef>)(d => { d.blastRadius = 6; d.blastDamage = 3; d.fuse = 3; d.tier = 3; d.tags = new[] { "throwable" }; }) });
        if (!ItemCatalog.Exists("molotov")) add.Invoke(null, new object[] { "molotov", "Molotov", ItemCategory.Weapon, .6f, (Action<ItemDef>)(d => { d.blastRadius = 3; d.tier = 2; d.tags = new[] { "throwable", "fire" }; }) });
    }

    static World NewWorld(int size = 1024, int seed = 4242) { return World.Create(new SandboxSettings { seed = seed, mapSize = size }, "Mara Vale", "paramedic", true); }

    /// <summary>Moves the leader to open grass with nothing (zombies or AI) near, and parks every AI survivor far away and offscreen.</summary>
    static Survivor Arena(World w)
    {
        var s = w.survivors[0];
        for (int x = 300; x < 900; x += 3)
            for (int y = 300; y < 900; y += 3)
            {
                bool clear = true;
                for (int dx = -8; dx <= 8 && clear; dx++) for (int dy = -8; dy <= 8 && clear; dy++) if (w.map.TileBlocked(x + dx, y + dy) || w.map.RoomAt(x + dx, y + dy) != null || w.map.H(x + dx, y + dy) != Edge.None || w.map.V(x + dx, y + dy) != Edge.None) clear = false;
                if (!clear) continue;
                s.pos = new Vec2(x + .5f, y + .5f);
                foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, s.pos) < 120) z.state = ZombieState.Dead;
                w.index = new ZombieIndex(w);
                foreach (var o in w.survivors) if (o != s && Vec2.Distance(o.pos, s.pos) < 200) { o.alive = false; }
                return s;
            }
        throw new Exception("No open arena tile found.");
    }

    static Survivor Npc(World w, string factionKey, Vec2 pos, string weapon = "")
    {
        var f = w.FactionByKey(factionKey);
        var s = w.AddSurvivor("Test " + factionKey, "unemployed", false, null);
        s.pos = pos; s.faction = f.id; s.ai = f.role; s.disposition = f.disposition; s.squad = -1;
        s.inventory.items.Clear(); s.worn.Clear(); s.primary = -1;
        if (weapon.Length > 0) { var g = ItemCatalog.Make(weapon); s.inventory.Add(g); s.primary = g.uid; if (g.Def.category == ItemCategory.Firearm) { g.loaded = g.Def.magazine; s.inventory.Add(ItemCatalog.Make(g.Def.ammo, 30)); } }
        return s;
    }

    static void Tick(Simulation sim, float seconds, Func<bool> until = null)
    {
        for (int i = 0; i < seconds * 20; i++) { sim.Tick(Simulation.Step); if (until != null && until()) return; }
    }

    static void Setup()
    {
        var w = NewWorld(); var basePos = w.community.basePos;
        Check(w.factions.Count >= 9 && w.factions[0].id == 0 && w.factions[0].key == "community", "factions created, community at index 0 (" + w.factions.Count + ")");
        foreach (var c in Characters.All) Check(w.survivors.Count(s => s.character == c.key && s.alive) == 1, "named character spawned: " + c.name);
        foreach (var key in new[] { "radio_station", "reeds_garage", "hospital" })
        {
            var f = w.FactionByKey(key); int n = w.Members(f.id).Count();
            Check(f.enclave && n >= 3 && n <= 5, key + " enclave has a leader and 2-4 members (" + n + ")");
            Check(f.homeBuilding >= 0 && f.homeBuilding != w.community.baseBuilding, key + " holds a building other than the base");
        }
        var crews = w.factions.Where(f => f.IsCrew).ToList();
        Check(crews.Count >= 2, "rival crews exist (" + crews.Count + ")");
        Check(crews.All(f => f.disposition == Disposition.Wary || f.disposition == Disposition.Opportunist), "crews are Wary or Opportunist");
        float nearest = w.survivors.Where(s => w.FactionOf(s) != null && w.FactionOf(s).IsCrew).Min(s => Vec2.Distance(s.pos, basePos));
        Check(nearest >= 250, "crews land away from the base (" + nearest.ToString("0") + " m)");
        Check(w.survivors.Where(s => s.ai != AiRole.None && s.faction != 0).All(s => Vec2.Distance(s.pos, basePos) > 100), "no AI spawns right at the base");
        var talons = w.FactionByKey("red_talons");
        Check(talons.disposition == Disposition.Hunter && w.Members(talons.id).All(s => s.Primary != null && s.Primary.Def.category == ItemCategory.Firearm), "raiders are hunters with guns");
        Check(w.Members(talons.id).All(s => s.Worn(WearSlot.Torso) != null && Simulation.ArmorOf(s.Worn(WearSlot.Torso).Def) > 0), "raiders wear vests");
        Check(w.survivors.Where(s => s.ai != AiRole.None).All(s => !w.map.TileBlocked((int)s.pos.x, (int)s.pos.y) && !w.map.IsWater((int)s.pos.x, (int)s.pos.y)), "AI survivors spawn on open land");
        Check(w.survivors.Count(s => s.ai != AiRole.None) >= 25, "the county has other people (" + w.survivors.Count(s => s.ai != AiRole.None) + ")");
        var w2 = NewWorld();
        Check(w.survivors.Count == w2.survivors.Count && w.survivors.Zip(w2.survivors, (a, b) => Vec2.Distance(a.pos, b.pos) < .001f && a.faction == b.faction).All(x => x), "setup is deterministic");
        var big = World.Create(new SandboxSettings { seed = 77, mapSize = 4096 }, "Mara Vale", "paramedic", true);
        int bigCrews = big.factions.Count(f => f.IsCrew), bigAi = big.survivors.Count(s => s.ai != AiRole.None);
        Check(bigCrews >= 10 && bigCrews <= 14, "about 12 crews at 4096 (" + bigCrews + ")");
        Check(bigAi >= 50 && bigAi <= 100, "50-100 AI survivors at 4096 (" + bigAi + ")");
    }

    static void CrewLoots()
    {
        var w = NewWorld(); var sim = new Simulation(w); var p = w.survivors[0];
        var c = w.map.containers.First(k => k.prop >= 0 && w.map.props[k.prop].building >= 0 && w.map.props[k.prop].building != w.community.baseBuilding
            && w.factions.All(f => f.homeBuilding != w.map.props[k.prop].building) && Vec2.Distance(w.map.props[k.prop].Center, w.community.basePos) > 260);
        var prop = w.map.props[c.prop];
        foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, prop.Center) < 60) z.state = ZombieState.Dead;
        w.index = new ZombieIndex(w);
        c.generated = true; c.inventory.items.Clear(); c.inventory.Add(ItemCatalog.Make("machete")); c.inventory.Add(ItemCatalog.Make("bandage"));
        // Stand inside, beside the container.
        Vec2 spot = prop.Center;
        for (int x = prop.x - 1; x <= prop.x + prop.w; x++) for (int y = prop.y - 1; y <= prop.y + prop.h; y++) { var r = w.map.RoomAt(x, y); if (r != null && r.id == prop.room && !w.map.TileBlocked(x, y) && !w.map.Collides(new Vec2(x + .5f, y + .5f), .3f)) spot = new Vec2(x + .5f + 3, y + .5f); }
        var crewKey = w.factions.First(f => f.IsCrew).key;
        var s = Npc(w, crewKey, spot); s.pos = new Vec2(spot.x - 3, spot.y); s.squad = -1;
        p.pos = s.pos + new Vec2(0, 30); foreach (var o in w.survivors) if (o != s && o != p && Vec2.Distance(o.pos, s.pos) < 60) o.alive = false;
        Tick(sim, 40, () => s.inventory.Count("machete") > 0);
        Check(s.inventory.Count("machete") == 1, "crew member loots a weapon from a container (state " + sim.BrainStateOf(s.id) + ", at " + s.pos + " box " + prop.Center + ")");
        Check(c.inventory.Count("machete") == 0, "the item left the container");
        Tick(sim, 3);
        Check(s.Primary != null && s.Primary.def == "machete", "and equips it");
    }

    static void CompanionFollowsAndFights()
    {
        var w = NewWorld(); var p = Arena(w);
        var bat = ItemCatalog.Make("baseball_bat");
        var m = w.AddSurvivor("Jo Avery", "firefighter", true, null);
        m.pos = p.pos + new Vec2(-7, 0); m.faction = 0; m.ai = AiRole.Companion; m.leader = p.id; m.inventory.Add(bat); m.primary = bat.uid;
        w.community.members.Add(m.id); w.community.companion = m.id;
        var sim = new Simulation(w);
        p.pos = p.pos + new Vec2(3, 0);
        Tick(sim, 6);
        Check(Vec2.Distance(m.pos, p.pos) < 4.5f, "companion follows the leader (" + Vec2.Distance(m.pos, p.pos).ToString("0.0") + " m)");
        var z = w.AddZombie(p.pos + new Vec2(-1.5f, 4), w.rng); z.state = ZombieState.Idle; z.timer = 99; z.hp = 1.2f;
        Tick(sim, 20, () => !z.Alive);
        Check(!z.Alive && m.kills >= 1, "companion kills a nearby zombie (state " + sim.BrainStateOf(m.id) + ")");
        sim.Submit(p.id, new Command { kind = CommandKind.CompanionOrder, slot = (int)CompanionOrder.Hold, recipe = "" });
        Tick(sim, .2f); var held = m.pos;
        p.pos = p.pos + new Vec2(10, 0);
        Tick(sim, 4);
        Check(m.order == CompanionOrder.Hold && Vec2.Distance(m.pos, held) < 1.5f, "Hold order keeps the companion in place");
    }

    static void Dispositions()
    {
        var w = NewWorld(); var p = Arena(w);
        var raider = Npc(w, "red_talons", p.pos + new Vec2(8, 0), "pistol");
        var sim = new Simulation(w);
        Check(w.Hostile(raider, p) && w.Hostile(p, raider), "raiders are hostile to the community");
        Tick(sim, 12, () => p.lastHitBy == raider.id);
        Check(p.lastHitBy == raider.id && p.health < 100, "a Hunter raider attacks the player on sight (health " + p.health.ToString("0") + ")");
        Check(w.events.Any(e => e.kind == EventKind.Tracer && e.survivor == raider.id), "shots emit tracers");

        var w2 = NewWorld(); var p2 = Arena(w2);
        var gun = ItemCatalog.Make("pistol"); gun.loaded = 15; p2.inventory.Add(gun); p2.primary = gun.uid;   // armed, but outside their home
        var mech = Npc(w2, "reeds_garage", p2.pos + new Vec2(5, 0), "shotgun");
        var sim2 = new Simulation(w2);
        Tick(sim2, 10);
        Check(p2.health >= 99 && p2.lastHitBy < 0 && !w2.Hostile(mech, p2), "a Wary enclave member does not attack");
        // Hitting them makes them fight back.
        var f = w2.FactionOf(mech); float standing = f.standing;
        sim2.DamageSurvivor(mech, p2, 5, "test");
        Check(w2.Hostile(mech, p2) && f.standing < standing, "attacking a Wary group provokes it and costs standing");
        // Friendly never attacks, even provoked (until standing collapses).
        var w3 = NewWorld(); var p3 = Arena(w3); var orderly = Npc(w3, "hospital", p3.pos + new Vec2(4, 0), "pistol");
        w3.FactionOf(orderly).provokedUntil = w3.minutes + 60;
        Check(!w3.Hostile(orderly, p3), "Friendly groups ignore provocation");
        w3.FactionOf(orderly).standing = -60;
        Check(w3.Hostile(orderly, p3), "very low standing turns any group hostile");
        // Opportunists roll on encounters: a lone, hurt survivor with a rucksack at night is likely to be robbed.
        var crew = w3.factions.First(x => x.IsCrew); crew.disposition = Disposition.Opportunist;
        int robbed = 0;
        for (int i = 0; i < 20; i++)
        {
            var wo = NewWorld(1024, 900 + i); var po = Arena(wo); po.health = 40;
            var cf = wo.factions.First(x => x.IsCrew); cf.disposition = Disposition.Opportunist;
            var thug = Npc(wo, cf.key, po.pos + new Vec2(10, 0), "pistol");
            wo.minutes = 2 * 1440 + 23 * 60;   // night
            var so = new Simulation(wo); Tick(so, .5f);
            if (wo.Hostile(thug, po)) robbed++;
        }
        Check(robbed > 0 && robbed < 20, "opportunists sometimes, not always, go for a weak target (" + robbed + "/20)");
    }

    static void FriendlyFire()
    {
        var w = NewWorld(); var p = Arena(w);
        var m = w.AddSurvivor("Jo Avery", "unemployed", true, null); m.pos = p.pos + new Vec2(3, 0); m.faction = 0; m.ai = AiRole.Resident;
        var sim = new Simulation(w);
        var gun = ItemCatalog.Make("shotgun"); gun.loaded = 6; p.inventory.Add(gun); p.primary = gun.uid;
        for (int i = 0; i < 20 * 3; i++) { sim.SetInput(p.id, new InputState { face = 0, attack = true, aim = true }); sim.Tick(Simulation.Step); }
        Check(gun.loaded < 6 && m.health >= 99, "shots never hurt your own community (" + m.health.ToString("0") + ")");
        var r1 = Npc(w, "red_talons", p.pos + new Vec2(0, 20), "pistol"); var r2 = Npc(w, "red_talons", p.pos + new Vec2(1.2f, 20));
        Check(!sim.CanHarm(r1, r2) && !w.Hostile(r1, r2), "raiders cannot hurt each other");
        var crew = Npc(w, w.factions.First(x => x.IsCrew).key, p.pos + new Vec2(0, -20));
        Check(sim.CanHarm(p, crew) && !sim.CanHarm(crew, p), "the player may start a fight; a neutral AI may not");
        Check(sim.CanHarm(r1, crew) && sim.CanHarm(crew, r1), "rival crews and raiders fight");
    }

    static void Armor()
    {
        var w = NewWorld(); var p = Arena(w); var sim = new Simulation(w);
        var a = Npc(w, "red_talons", p.pos + new Vec2(30, 0)); var b = Npc(w, "red_talons", p.pos + new Vec2(32, 0));
        string vestId = ItemCatalog.Exists("vest_2") ? "vest_2" : "police_vest";
        var vest = ItemCatalog.Make(vestId); b.inventory.Add(vest); b.worn[WearSlot.Torso] = vest.uid;
        sim.DamageSurvivor(a, p, 40, "test"); sim.DamageSurvivor(b, p, 40, "test");
        Check(b.health > a.health + 5, "armour absorbs damage (" + a.health.ToString("0") + " vs " + b.health.ToString("0") + ")");
        Check(vest.condition < 1, "armour wears down");
        var c = Npc(w, "red_talons", p.pos + new Vec2(34, 0));
        var gun = ItemCatalog.Make("pistol");
        float rifleHits = 100 / (gun.Def.damage * Simulation.GunToHealth);
        Check(rifleHits >= 2 && rifleHits <= 6, "a pistol kills an unarmoured human in a few hits (" + rifleHits.ToString("0.0") + ")");
    }

    static void Grenade()
    {
        var w = NewWorld(); var p = Arena(w); var sim = new Simulation(w);
        var g = ItemCatalog.Make("grenade"); p.inventory.Add(g);
        var at = p.pos + new Vec2(10, 0);
        var zs = new List<Zombie>(); for (int i = 0; i < 3; i++) { var z = w.AddZombie(at + Vec2.FromAngle(i * 2.1f) * 1.2f, w.rng); z.state = ZombieState.Idle; z.timer = 99; z.sight = 0; z.hearing = 0; zs.Add(z); }
        sim.Submit(p.id, new Command { kind = CommandKind.Throw, item = g.uid, x = at.x, y = at.y, recipe = "" });
        sim.Tick(Simulation.Step);
        Check(w.throwables.Count == 1 && p.inventory.Count("grenade") == 0, "the grenade is in the air");
        Tick(sim, 6, () => w.throwables.Count == 0 && zs.All(z => !z.Alive));
        Check(zs.All(z => !z.Alive), "a grenade kills nearby zombies");
        Check(w.events.Any(e => e.kind == EventKind.Explosion), "explosion event");
        Check(p.health >= 99, "the thrower is out of the blast");
        // Range clamps.
        var g2 = ItemCatalog.Make("grenade"); p.inventory.Add(g2);
        sim.Submit(p.id, new Command { kind = CommandKind.Throw, item = g2.uid, x = p.pos.x + 60, y = p.pos.y, recipe = "" });
        Tick(sim, 1.2f);
        var t = w.throwables.LastOrDefault();
        Check(t != null && Vec2.Distance(t.pos, p.pos) <= Simulation.MaxThrow + .1f, "throw range is clamped");
    }

    static void Molotov()
    {
        var w = NewWorld(); var p = Arena(w); var sim = new Simulation(w);
        var m = ItemCatalog.Make("molotov"); p.inventory.Add(m);
        var at = p.pos + new Vec2(0, 8);
        int before = w.hazards.Count;
        sim.Submit(p.id, new Command { kind = CommandKind.Throw, item = m.uid, x = at.x, y = at.y, recipe = "" });
        Tick(sim, 1.5f);
        var h = w.hazards.Skip(before).FirstOrDefault();
        Check(h != null && h.fire && Vec2.Distance(h.pos, at) < 1 && h.radius >= 2.5f && h.until > w.minutes, "a molotov leaves a fire hazard");
    }

    static void Lod()
    {
        var w = NewWorld(); var p = Arena(w);
        var s = Npc(w, w.factions.First(x => x.IsCrew).key, p.pos + new Vec2(20, 0), "baseball_bat");
        var sim = new Simulation(w);
        Tick(sim, .2f);
        Check(!s.offscreen, "nearby AI is simulated fully");
        p.pos = p.pos + new Vec2(0, 250);
        Tick(sim, .2f);
        Check(s.offscreen, "far AI goes offscreen");
        var before = s.pos;
        Tick(sim, 30);
        Check(s.alive && Vec2.Distance(before, s.pos) > 5, "offscreen AI still moves (" + Vec2.Distance(before, s.pos).ToString("0") + " m)");
        p.pos = s.pos + new Vec2(160, 0);
        Tick(sim, .2f);
        Check(s.offscreen, "hysteresis keeps it offscreen between 150 and 170 m");
        p.pos = s.pos + new Vec2(30, 0);
        Tick(sim, .2f);
        Check(!s.offscreen, "back in range it resumes full simulation");
        // Offscreen survivors fight zombies abstractly.
        p.pos = s.pos + new Vec2(0, 400); Tick(sim, .1f);
        var z = w.AddZombie(s.pos + new Vec2(2, 0), w.rng); z.state = ZombieState.Idle;
        Tick(sim, 15, () => !z.Alive || !s.alive);
        Check(!z.Alive || !s.alive, "offscreen zombie encounters resolve");
    }

    static void Trade()
    {
        var w = NewWorld(); var sim = new Simulation(w); var p = w.survivors[0];
        var doc = w.survivors.First(s => s.character == "doc");
        p.pos = doc.pos + new Vec2(.5f, 0); p.primary = -1;
        w.community.influence = 100;
        Check(sim.CanTalk(p, doc), "can talk to Doc Hollis");
        Check(sim.Greeting(p, doc).Length > 0, "Doc has something to say");
        var offers = sim.TradeOffers(p, doc);
        int slot = offers.FindIndex(o => o.enabled && o.label.StartsWith("Buy"));
        Check(slot >= 0, "an affordable offer exists (" + string.Join("; ", offers.Select(o => o.label + (o.enabled ? "" : " [" + o.reason + "]"))) + ")");
        int items = p.inventory.items.Sum(i => i.count);
        sim.Submit(p.id, new Command { kind = CommandKind.Trade, target = doc.id, slot = slot, recipe = "" }); sim.Tick(Simulation.Step);
        Check(w.community.influence < 100 && p.inventory.items.Sum(i => i.count) > items, "trading spends influence for an item (" + w.community.influence + ")");
        w.community.influence = 0;
        offers = sim.TradeOffers(p, doc);
        Check(offers.Where(o => o.label.StartsWith("Buy")).All(o => !o.enabled), "offers you can't afford are disabled");
        sim.Submit(p.id, new Command { kind = CommandKind.Trade, target = doc.id, slot = slot, recipe = "" }); sim.Tick(Simulation.Step);
        Check(w.community.influence == 0, "the host re-validates influence");
        p.pos = doc.pos + new Vec2(20, 0);
        sim.Submit(p.id, new Command { kind = CommandKind.Talk, target = doc.id, recipe = "" }); sim.Tick(Simulation.Step);
        Check(!w.events.Any(e => e.kind == EventKind.Talk && e.survivor == p.id), "talking needs to be close");
        var pilgrim = w.survivors.First(s => s.character == "pilgrim");
        Check(sim.TradeOffers(p, pilgrim).Count >= 4, "the Pilgrim sells rare goods");
    }

    static void KillFeed()
    {
        var w = NewWorld(); var p = Arena(w); var sim = new Simulation(w);
        var raider = Npc(w, "red_talons", p.pos + new Vec2(30, 0));
        var hospital = w.FactionByKey("hospital"); float standing = hospital.standing;
        sim.DamageSurvivor(raider, p, 500, "test");
        Check(!raider.alive, "raider dies");
        Check(w.events.Any(e => e.kind == EventKind.KillFeed && e.text == "Mara Vale killed a Red Talon raider"), "kill feed names killer and victim (" + string.Join("|", w.events.Where(e => e.kind == EventKind.KillFeed).Select(e => e.text)) + ")");
        Check(hospital.standing > standing, "killing raiders raises standing with the enclaves");
        var kade = w.survivors.First(s => s.character == "kade");
        sim.Kill(kade, "Killed by a Juggernaut.");
        Check(w.events.Any(e => e.kind == EventKind.KillFeed && e.text == "Marshal Kade was killed by a Juggernaut"), "zombie deaths reach the feed");
        Check(w.events.Any(e => e.kind == EventKind.Radio && e.text.Contains("Kade")), "named deaths reach the radio");
        var orderly = w.Members(hospital.id).First(s => s.character == "");
        sim.DamageSurvivor(orderly, p, 500, "test");
        Check(hospital.Hostile, "killing an enclave member makes that enclave hostile");
    }

    static void SaveLoad()
    {
        var w = NewWorld(); var sim = new Simulation(w); var p = w.survivors[0];
        Tick(sim, 2);
        var f = w.FactionByKey("radio_station"); f.standing = 37; f.provokedUntil = 1234;
        var m = w.AddSurvivor("Jo Avery", "unemployed", true, null); m.faction = 0; m.ai = AiRole.Companion; m.leader = p.id; m.order = CompanionOrder.Attack;
        w.throwables.Add(new Throwable { id = 5, item = "grenade", from = p.pos, pos = p.pos + new Vec2(4, 0), by = p.id, fuse = 2, landed = true });
        string path = Path.Combine(Path.GetTempPath(), "ashfall-humans-" + Guid.NewGuid().ToString("N") + ".sav");
        SaveCodec.Save(w, path);
        var l = SaveCodec.Load(path); File.Delete(path);
        Check(l.factions.Count == w.factions.Count && l.factions.Select(x => x.key + x.disposition + x.homeBuilding).SequenceEqual(w.factions.Select(x => x.key + x.disposition + x.homeBuilding)), "factions restored");
        var lf = l.FactionByKey("radio_station");
        Check(lf.standing == 37 && lf.provokedUntil == 1234 && lf.enclave, "faction standing restored");
        var lm = l.survivors[m.id];
        Check(lm.leader == p.id && lm.order == CompanionOrder.Attack && lm.ai == AiRole.Companion, "companion fields restored");
        Check(l.survivors.Zip(w.survivors, (a, b) => a.squad == b.squad && a.disposition == b.disposition && a.character == b.character && a.offscreen == b.offscreen).All(x => x), "per-survivor human fields restored");
        Check(l.throwables.Count == 1 && l.throwables[0].item == "grenade" && Math.Abs(l.throwables[0].fuse - 2) < .001f, "pending throwables restored");
        var lsim = new Simulation(l); Tick(lsim, 3);
        Check(l.throwables.Count == 0, "a loaded grenade still goes off");
    }

    static void Perf()
    {
        var w = NewWorld(); var sim = new Simulation(w);
        // Wake a few brains near the player so onscreen thinking is measured too.
        var p = w.survivors[0]; int moved = 0;
        foreach (var s in w.survivors) if (s.ai != AiRole.None && s.ai != AiRole.Enclave && moved < 8) { s.pos = new Vec2(p.pos.x + 30 + moved * 3, p.pos.y + 30); s.offscreen = false; moved++; }
        Tick(sim, 1);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Tick(sim, 20);
        double ms = watch.Elapsed.TotalMilliseconds / 400;
        Check(ms < 8, "humans keep ticks cheap (" + ms.ToString("0.00") + " ms/tick with " + w.survivors.Count(s => s.ai != AiRole.None && s.alive) + " AI)");
        Console.WriteLine("  humans-perf: " + ms.ToString("0.00") + " ms/tick, alive " + w.SurvivorsAlive + ", offscreen " + w.survivors.Count(s => s.offscreen));
    }
}
