using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;

/// <summary>Bestiary checks. Register each case with run("name", () => ...); assert with check(condition, "what").</summary>
static class BestiaryTests
{
    static Action<bool, string> check;
    static readonly bool Verbose = Environment.GetEnvironmentVariable("ASHFALL_VERBOSE") == "1";
    static void Check(bool ok, string what) { if (Verbose) Console.WriteLine("  " + (ok ? "pass " : "FAIL ") + what); check(ok, what); }

    public static void All(Action<string, Action> run, Action<bool, string> check)
    {
        BestiaryTests.check = check;
        run("bestiary-kinds", Kinds);
        run("bestiary-screamer", Screamer);
        run("bestiary-bloater-gas", Bloater);
        run("bestiary-armored", Armored);
        run("bestiary-feral", Feral);
        run("bestiary-juggernaut", Juggernaut);
        run("bestiary-plague", Plague);
        run("bestiary-heart", Heart);
        run("bestiary-hazards", Hazards);
        run("bestiary-kill-feed", KillFeed);
        run("bestiary-horde", Hordes);
        run("bestiary-night-ferals", NightFerals);
        run("bestiary-save-load", SaveLoad);
    }

    static World NewWorld(int seed = 4242, float freaks = 1, int size = 1024)
    {
        return World.Create(new SandboxSettings { seed = seed, mapSize = size, freakDensity = freaks }, "Mara Vale", "paramedic", true);
    }

    /// <summary>Open outdoor ground with nothing within 120 m: no zombies, hearts or hordes. Survivor unclothed and healthy.</summary>
    static Survivor Arena(World w, out Simulation sim)
    {
        var s = w.survivors[0];
        for (int x = 300; x < 900; x += 3)
            for (int y = 300; y < 900; y += 3)
            {
                bool clear = true;
                for (int dx = -12; dx <= 12 && clear; dx++) for (int dy = -12; dy <= 12 && clear; dy++) if (w.map.TileBlocked(x + dx, y + dy) || w.map.IsWater(x + dx, y + dy) || w.map.RoomAt(x + dx, y + dy) != null || w.map.H(x + dx, y + dy) != Edge.None || w.map.V(x + dx, y + dy) != Edge.None) clear = false;
                if (!clear) continue;
                s.pos = new Vec2(x + .5f, y + .5f); s.facing = 0;
                foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, s.pos) < 150) z.state = ZombieState.Dead;
                w.hearts.Clear(); w.hordes.Clear(); foreach (var z in w.zombies) z.horde = -1;
                w.index = new ZombieIndex(w);
                foreach (var kv in s.worn.ToList()) s.worn.Remove(kv.Key);
                s.health = 100; s.wounds.Clear();
                sim = new Simulation(w);
                return s;
            }
        throw new Exception("No open arena found.");
    }

    static Zombie Spawn(World w, Vec2 p, ZombieKind kind, float facing)
    {
        var z = w.SpawnKind(p, kind, w.rng); z.state = ZombieState.Idle; z.timer = 99; z.facing = facing; z.ability = 0; return z;
    }
    static float Face(Vec2 from, Vec2 to) { return (to - from).Angle; }
    static void Tick(Simulation sim, float seconds, Func<bool> until = null, Action each = null)
    {
        for (int i = 0; i < (int)(seconds / Simulation.Step); i++) { if (each != null) each(); sim.Tick(Simulation.Step); if (until != null && until()) return; }
    }
    static bool Saw(World w, EventKind kind, string text = null) { return w.events.Any(e => e.kind == kind && (text == null || e.text == text)); }

    static void Kinds()
    {
        var w = NewWorld(); var start = w.survivors[0].pos;
        var alive = w.zombies.Where(z => z.Alive).ToList(); int n = alive.Count;
        Func<ZombieKind, int> count = k => alive.Count(z => z.kind == k);
        Func<ZombieKind, float> share = k => count(k) / (float)n;
        string dist = string.Join(" ", Bestiary.All.Select(d => d.id + "=" + count(d.kind) + " (" + (share(d.kind) * 100).ToString("0.0") + "%)"));
        Console.WriteLine("  kinds 1024: " + n + " zombies, " + dist + ", hearts " + w.hearts.Count + ", hordes " + w.hordes.Count);
        Check(share(ZombieKind.Walker) + share(ZombieKind.Crawler) > .84f, "walkers are the vast majority (" + dist + ")");
        Check(share(ZombieKind.Screamer) > .01f && share(ZombieKind.Screamer) < .045f, "screamers about 2-3%");
        Check(share(ZombieKind.Bloater) > .01f && share(ZombieKind.Bloater) < .05f, "bloaters about 2-3%");
        Check(share(ZombieKind.Armored) > .01f && share(ZombieKind.Armored) < .06f, "armored about 3-4%");
        Check(share(ZombieKind.Feral) > .002f && share(ZombieKind.Feral) < .035f, "ferals about 1-2%");
        Check(count(ZombieKind.Juggernaut) >= 1 && count(ZombieKind.Juggernaut) <= 4, "juggernauts are rare (" + count(ZombieKind.Juggernaut) + " on 1 km²)");
        Check(alive.All(z => z.crawler == (z.kind == ZombieKind.Crawler)), "crawler flag maps to the Crawler kind");
        Check(alive.Where(z => z.kind > ZombieKind.Crawler).All(z => Vec2.Distance(z.pos, start) >= Bestiary.FreakClearance), "no freaks near the start");
        Check(alive.All(z => z.maxHp > 0 && z.hp <= z.maxHp + .001f), "hp within maxHp");
        Check(alive.Where(z => z.kind == ZombieKind.Juggernaut).All(z => z.maxHp > 20), "juggernauts have huge HP");
        Check(alive.Where(z => z.kind == ZombieKind.Feral).All(z => z.speed >= 3.4f), "ferals are very fast");
        Check(alive.All(z => !w.map.IsWater((int)z.pos.x, (int)z.pos.y) && !w.map.TileBlocked((int)z.pos.x, (int)z.pos.y)), "freaks, guards and hordes stand on open land");
        Check(w.initialPopulation == w.zombies.Count, "initial population includes hordes and guards");
        Check(w.hearts.Count >= 1, "plague hearts placed (" + w.hearts.Count + ")");
        Check(w.hearts.All(h => Vec2.Distance(h.pos, start) >= Bestiary.HeartClearance && w.map.BuildingAt(h.pos) != null && !w.map.TileBlocked((int)h.pos.x, (int)h.pos.y)), "hearts sit inside buildings, far from the start");
        Check(w.hearts.All(h => alive.Count(z => z.plague && Vec2.Distance(z.pos, h.pos) < 12) >= 4), "each heart has a plague guard");
        Check(w.hordes.Count >= 1 && w.hordes.All(h => h.members.Count >= 12 && h.members.Count <= 40 && h.members.All(id => w.zombies[id].horde == h.id)), "roaming hordes of 15-40 walkers");
        var b = NewWorld();
        Check(b.zombies.Select(z => z.kind).SequenceEqual(w.zombies.Select(z => z.kind)) && b.hearts.Select(h => h.pos.x).SequenceEqual(w.hearts.Select(h => h.pos.x)), "kinds, hearts and hordes are deterministic");
        var none = NewWorld(4242, 0);
        Check(none.zombies.All(z => z.kind <= ZombieKind.Crawler) && none.hearts.Count == 0, "freak density 0: walkers only, no hearts");
        var dense = NewWorld(4242, 2);
        int freaks = w.zombies.Count(z => z.kind > ZombieKind.Crawler), freaks2 = dense.zombies.Count(z => z.kind > ZombieKind.Crawler);
        Check(freaks2 > freaks * 1.5f, "freak density scales freaks (" + freaks + " -> " + freaks2 + ")");
    }

    static void Screamer()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        var sc = Spawn(w, s.pos + new Vec2(6, 0), ZombieKind.Screamer, 3.14f);
        var walker = Spawn(w, s.pos + new Vec2(-30, 0), ZombieKind.Walker, 3.14f); walker.hearing = 1.3f; walker.sight = 0;
        float panic = s.panic;
        Tick(sim, 3, () => Saw(w, EventKind.Scream), () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(Saw(w, EventKind.Scream), "a screamer that spots a survivor screams");
        Check(s.panic > panic + 20, "the scream panics survivors nearby (" + s.panic.ToString("0") + ")");
        sim.Tick(Simulation.Step);
        Check(walker.state == ZombieState.Investigate && Vec2.Distance(walker.target, sc.pos) < 6, "the scream pulls distant zombies (" + walker.state + ")");
        int screams = w.events.Count(e => e.kind == EventKind.Scream);
        Tick(sim, 3, null, () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(w.events.Count(e => e.kind == EventKind.Scream) == screams, "screams have a cooldown");
        // It keeps its distance from an approaching survivor.
        sc.pos = s.pos + new Vec2(4, 0); w.index.Moved(sc);
        float before = Vec2.Distance(sc.pos, s.pos);
        Tick(sim, 1.5f, null, () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(Vec2.Distance(sc.pos, s.pos) > before + .5f, "a screamer backs away (" + before.ToString("0.0") + " -> " + Vec2.Distance(sc.pos, s.pos).ToString("0.0") + ")");
        // Point blank, it stuns.
        var close = Spawn(w, s.pos + new Vec2(0, 3), ZombieKind.Screamer, -1.57f); s.knockedDown = 0;
        Tick(sim, 1, () => s.knockedDown > 0, () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(s.knockedDown > 0, "a scream within 5 m staggers the survivor");
    }

    static void Bloater()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        var bat = ItemCatalog.Make("baseball_bat"); s.inventory.Add(bat); s.primary = bat.uid;
        var b = Spawn(w, s.pos + new Vec2(1.1f, 0), ZombieKind.Bloater, 3.14f); b.sight = 0;
        Check(b.speed < 1.1f, "bloaters are slow (" + b.speed.ToString("0.00") + ")");
        Tick(sim, 8, () => !b.Alive, () => { sim.SetInput(s.id, new InputState { face = 0, attack = true }); s.health = Math.Max(s.health, 60); b.stagger = 0; });
        Check(!b.Alive, "bat kills the bloater");
        Check(Saw(w, EventKind.Gas, "burst"), "a dead bloater bursts");
        var gas = w.hazards.FirstOrDefault(h => !h.fire);
        Check(gas != null && Math.Abs(gas.radius - Bestiary.GasRadius) < .01f && gas.until > w.minutes + 8, "it leaves a lingering gas cloud");
        Check(gas != null && Vec2.Distance(gas.pos, s.pos) < 1, "killed in melee, it bursts on the attacker");
        Check(w.events.Any(e => e.kind == EventKind.Hurt && e.survivor == s.id && e.text.Contains("gas")), "the burst hurts the attacker");
        // Zombies in the cloud are unharmed; the survivor standing in it is not.
        var z = Spawn(w, s.pos + new Vec2(-1, 1), ZombieKind.Walker, 0); z.sight = 0; z.hearing = 0; float zhp = z.hp;
        float health = s.health = 90;
        Tick(sim, 2, null, () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(s.health < health - 5, "gas hurts a survivor inside (" + health + " -> " + s.health.ToString("0.0") + ")");
        Check(z.hp == zhp, "gas does not hurt zombies");
        // Out of the cloud, no more damage.
        s.pos = s.pos + new Vec2(0, 8); health = s.health;
        Tick(sim, 2, null, () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(s.health >= health - .01f, "leaving the cloud stops the damage");
        // Shot from range: the shooter is not caught.
        var b2 = Spawn(w, s.pos + new Vec2(8, 0), ZombieKind.Bloater, 3.14f); health = s.health;
        sim.HitZombie(b2, s, 99, 0, false);
        Check(!b2.Alive && w.hazards.Any(h => Vec2.Distance(h.pos, b2.pos) < .1f) && s.health == health, "a bloater shot from range bursts where it stood");
    }

    static void Armored()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        var front = Spawn(w, s.pos + new Vec2(1, 0), ZombieKind.Armored, 3.14f); front.sight = 0;
        var back = Spawn(w, s.pos + new Vec2(1, 1), ZombieKind.Armored, Face(s.pos, s.pos + new Vec2(1, 1))); back.sight = 0;
        float f0 = front.hp, b0 = back.hp;
        sim.HitZombie(front, s, 1, 0, false); sim.HitZombie(back, s, 1, 0, false);
        float df = f0 - front.hp, db = b0 - back.hp;
        Check(Math.Abs(df - .2f) < .01f && Math.Abs(db - 1) < .01f, "riot gear turns frontal blows (front " + df.ToString("0.00") + ", back " + db.ToString("0.00") + ")");
        Check(Saw(w, EventKind.Hit, "armor"), "armoured hits are flagged for the view");
        front.state = ZombieState.Knocked; front.timer = 5; f0 = front.hp;
        sim.HitZombie(front, s, 1, 0, false);
        Check(Math.Abs(f0 - front.hp - 1) < .01f, "knocked down, it takes full damage");
        // Shoves rarely knock it down, compared with a walker.
        Func<ZombieKind, int> shoves = kind =>
        {
            var z = Spawn(w, s.pos + new Vec2(.9f, 0), kind, 3.14f); z.sight = 0; z.hearing = 0; z.hp = z.maxHp = 999; int downs = 0;
            for (int i = 0; i < 40; i++)
            {
                z.state = ZombieState.Idle; z.stagger = 0; z.pos = s.pos + new Vec2(.9f, 0); w.index.Moved(z); s.shoveCooldown = 0; s.attackCooldown = 0; s.endurance = 100;
                sim.SetInput(s.id, new InputState { face = 0, shove = true }); sim.Tick(Simulation.Step);
                if (z.state == ZombieState.Knocked) downs++;
            }
            z.state = ZombieState.Dead; w.index.Remove(z);
            return downs;
        };
        int walkerDowns = shoves(ZombieKind.Walker), armoredDowns = shoves(ZombieKind.Armored);
        Check(armoredDowns < walkerDowns / 2 + 1, "shoves rarely floor armored zombies (" + armoredDowns + " vs walker " + walkerDowns + " of 40)");
        // Explosions ignore the armour.
        var z2 = Spawn(w, s.pos + new Vec2(10, 0), ZombieKind.Armored, 3.14f); z2.hp = z2.maxHp = 50; float h2 = z2.hp;
        sim.Blast(z2.pos, 2, 1, s, "grenade");
        Check(h2 - z2.hp > .9f, "blasts ignore riot armour");
    }

    static void Feral()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        var f = Spawn(w, s.pos + new Vec2(5, 0), ZombieKind.Feral, 3.14f);
        Tick(sim, 3, () => Saw(w, EventKind.Pounce, "hit"), () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(Saw(w, EventKind.Pounce, "leap"), "a feral leaps from 3-7 m");
        Check(Saw(w, EventKind.Pounce, "hit") && s.knockedDown > 0, "the pounce knocks the survivor down");
        Check(s.health < 85 && s.wounds.Any(x => x.kind == WoundKind.Laceration), "the pounce does heavy damage (" + s.health.ToString("0") + ")");
        // Dodges some swings.
        f.state = ZombieState.Dead; w.index.Remove(f);
        var d = Spawn(w, s.pos + new Vec2(1, 0), ZombieKind.Feral, 3.14f); d.sight = 0; int hits = 0;
        for (int i = 0; i < 40; i++) { d.hp = d.maxHp = 999; d.act = FreakAct.None; d.state = ZombieState.Idle; d.pos = s.pos + new Vec2(1, 0); w.index.Moved(d); int before = w.events.Count(e => e.kind == EventKind.Hit); sim.HitZombie(d, s, .1f, 0, false); if (w.events.Count(e => e.kind == EventKind.Hit) > before) hits++; }
        Check(hits < 38 && hits > 20 && Saw(w, EventKind.Swing, "dodge"), "ferals dodge some melee swings (" + (40 - hits) + " of 40)");
        // Flees when hurt, then returns.
        d.state = ZombieState.Dead; w.index.Remove(d);
        var h = Spawn(w, s.pos + new Vec2(1.5f, 0), ZombieKind.Feral, 3.14f); h.hp = h.maxHp = 3;
        for (int i = 0; i < 20 && h.act != FreakAct.Flee && h.Alive; i++) { h.state = ZombieState.Idle; sim.HitZombie(h, s, .4f, 0, false); }
        Check(h.Alive && h.act == FreakAct.Flee, "a hurt feral flees");
        s.knockedDown = 0;
        Tick(sim, 5, () => h.act == FreakAct.None, () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(h.act == FreakAct.None && (h.state == ZombieState.Chase || h.state == ZombieState.Attack || h.state == ZombieState.Lunge || h.state == ZombieState.Investigate), "then comes back (" + h.state + ")");
    }

    static void Juggernaut()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        var j = Spawn(w, s.pos + new Vec2(1, 0), ZombieKind.Juggernaut, 3.14f); j.grabCooldown = 99;
        int downs = 0;
        for (int i = 0; i < 30; i++) { s.health = 100; s.injury = 0; s.knockedDown = 0; s.shoveCooldown = 0; sim.SetInput(s.id, new InputState { face = Face(s.pos, j.pos), shove = true }); sim.Tick(Simulation.Step); if (j.state == ZombieState.Knocked) downs++; }
        Check(downs == 0, "juggernauts ignore shoves");
        sim.HitZombie(j, s, 1, 1, false);
        Check(j.state != ZombieState.Knocked && j.stagger == 0, "melee hits neither floor nor stagger a juggernaut");
        // Grab: a weakened survivor is killed outright.
        s.health = 25; s.injury = 0; s.healthSeen = -1; s.knockedDown = 0; j.grabCooldown = 0; j.pos = s.pos + new Vec2(1, 0); w.index.Moved(j); j.state = ZombieState.Chase; j.targetSurvivor = s.id; j.attackClock = 0;
        Tick(sim, 6, () => !s.alive, () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(Saw(w, EventKind.Grab, "start"), "an adjacent juggernaut grabs");
        Check(!s.alive && Saw(w, EventKind.Grab, "kill"), "it kills a survivor below 35% health outright");
        // Another survivor's hit breaks the grab.
        var w2 = NewWorld(); Simulation sim2; var a = Arena(w2, out sim2);
        var b = w2.AddSurvivor("Doc Hollis", "paramedic", false, null); b.pos = a.pos + new Vec2(-1.5f, 0); b.ai = AiRole.Companion;
        var j2 = Spawn(w2, a.pos + new Vec2(1, 0), ZombieKind.Juggernaut, 3.14f); j2.state = ZombieState.Chase; j2.targetSurvivor = a.id; j2.attackClock = 0;
        a.health = 25; a.injury = 0; a.healthSeen = -1;
        Tick(sim2, 3, () => j2.act == FreakAct.Grab);
        Check(j2.act == FreakAct.Grab && a.knockedDown > 0, "the grabbed survivor is held");
        sim2.HitZombie(j2, b, .5f, 0, false);
        Check(j2.act != FreakAct.Grab && Saw(w2, EventKind.Grab, "break"), "another survivor's hit breaks the grab");
        Tick(sim2, Bestiary.GrabSeconds + .2f, () => !a.alive);
        Check(a.alive, "the rescued survivor lives");
        // Charge: from 6-16 m it runs the survivor over in a straight line.
        var w3 = NewWorld(); Simulation sim3; var c = Arena(w3, out sim3);
        var bystander = Spawn(w3, c.pos + new Vec2(5, .3f), ZombieKind.Walker, 0); bystander.sight = 0; bystander.hearing = 0;
        var j3 = Spawn(w3, c.pos + new Vec2(10, 0), ZombieKind.Juggernaut, 3.14f);
        Tick(sim3, 4, () => Saw(w3, EventKind.Charge, "hit"), () => sim3.SetInput(c.id, new InputState { face = 0 }));
        Check(Saw(w3, EventKind.Charge, "start"), "a juggernaut charges from 6-16 m");
        Check(Saw(w3, EventKind.Charge, "hit") && c.knockedDown > 0 && c.health < 100, "the charge knocks the survivor over");
        Check(w3.events.Any(e => e.kind == EventKind.Shove && e.zombie == bystander.id), "zombies in its path are bowled over");
        Check(Saw(w3, EventKind.Footstep, "juggernaut"), "heavy footsteps");
    }

    static void Plague()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        var z = Spawn(w, s.pos + new Vec2(1, 0), ZombieKind.Walker, 3.14f); z.plague = true;
        Tick(sim, 30, () => s.plagueInfected, () => { sim.SetInput(s.id, new InputState { face = 0 }); s.health = 100; s.knockedDown = 0; });
        Check(s.plagueInfected && s.plague > 0, "plague zombies spread blood plague (" + s.plague + ")");
        Check(Saw(w, EventKind.Plague, "infected"), "infection is announced");
        // Ordinary bites wound but never turn the survivor.
        var w2 = NewWorld(); Simulation sim2; var a = Arena(w2, out sim2);
        var walker = Spawn(w2, a.pos + new Vec2(1, 0), ZombieKind.Walker, 3.14f);
        Tick(sim2, 30, null, () => { sim2.SetInput(a.id, new InputState { face = 0 }); a.health = 100; a.knockedDown = 0; });
        Check(a.wounds.Count > 0 && !a.zombieInfected && !a.plagueInfected, "normal bites just wound (" + a.wounds.Count + " wounds)");
    }

    static void Heart()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        var h = new PlagueHeart { id = 0, pos = s.pos + new Vec2(8, 0) }; w.hearts.Add(h);
        Check(!h.discovered, "hearts start undiscovered");
        Tick(sim, 2, () => Saw(w, EventKind.Heart, "pulse"), () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(h.discovered, "seen within 30 m, a heart is discovered");
        Check(Saw(w, EventKind.Heart, "pulse") && w.zombies.Any(z => z.Alive && z.plague && Vec2.Distance(z.pos, h.pos) < 6), "a heart pulses and spawns plague zombies near a survivor");
        // The spawn cap holds.
        for (int i = 0; i < 40; i++) { h.spawnClock = 0; Tick(sim, .55f, null, () => { sim.SetInput(s.id, new InputState { face = 0 }); s.health = 100; s.injury = 0; s.plague = 0; s.plagueInfected = false; s.knockedDown = 0; }); }
        int plague = w.zombies.Count(z => z.Alive && z.plague && Vec2.Distance(z.pos, h.pos) < 30);
        Check(plague <= Bestiary.HeartSpawnCap, "spawning is capped (" + plague + ")");
        foreach (var z in w.zombies) if (z.Alive) { z.state = ZombieState.Dead; w.index.Remove(z); }
        // Solid: survivors cannot stand inside it.
        s.pos = h.pos + new Vec2(-.3f, 0); s.knockedDown = 0;
        sim.Tick(Simulation.Step);
        Check(Vec2.Distance(s.pos, h.pos) >= PlagueHeart.Radius + Survivor.Radius - .05f, "hearts are solid");
        // Hit: screams and calls defenders.
        float influence = w.community.influence; int before = w.zombies.Count;
        sim.DamageHeart(h, s, 10);
        Check(h.hp == h.maxHp - 10 && Saw(w, EventKind.Heart, "hit") && Saw(w, EventKind.Heart, "scream"), "a struck heart screams");
        Check(w.zombies.Count > before, "the scream spawns defenders");
        sim.DamageHeart(h, s, 999);
        Check(h.destroyed && w.HeartsRemaining == 0 && Saw(w, EventKind.HeartDestroyed), "a heart can be destroyed");
        Check(w.events.Any(e => e.kind == EventKind.KillFeed && e.text == "Mara Vale destroyed a plague heart"), "heart kills reach the kill feed");
        Check(Math.Abs(w.community.influence - influence - Bestiary.HeartInfluence) < .01f && Saw(w, EventKind.Influence, "+" + Bestiary.HeartInfluence), "destroying a heart earns influence");
        if (ItemCatalog.Exists("plague_sample"))
        {
            var pile = w.PileAt((int)h.pos.x, (int)h.pos.y, false);
            Check(pile != null && pile.inventory.Count("plague_sample") >= 2, "a destroyed heart drops plague samples");
        }
        else Console.WriteLine("  note: item plague_sample not defined yet (core-world); sample drop skipped");
        int events = w.events.Count(e => e.kind == EventKind.HeartDestroyed);
        sim.DamageHeart(h, s, 10);
        Check(w.events.Count(e => e.kind == EventKind.HeartDestroyed) == events, "a destroyed heart stays destroyed");
        // Blasts and artillery damage hearts too.
        var h2 = new PlagueHeart { id = 1, pos = s.pos + new Vec2(0, 12) }; w.hearts.Add(h2);
        sim.Blast(h2.pos, 3, 2, null, "artillery");
        Check(h2.hp < h2.maxHp, "blasts damage hearts");
    }

    static void Hazards()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        var z = Spawn(w, s.pos + new Vec2(6, 0), ZombieKind.Walker, 0); z.sight = 0; z.hearing = 0; z.hp = z.maxHp = 1.2f;
        w.hazards.Add(new Hazard { pos = z.pos, radius = 2, until = (float)w.minutes + 5, fire = true, by = s.id });
        w.hazards.Add(new Hazard { pos = s.pos, radius = 2, until = (float)w.minutes + 5, fire = true, by = -1 });
        float health = s.health;
        Tick(sim, 1.2f, null, () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(z.burning > 0 && z.hp < 1.2f, "fire sets zombies burning");
        Check(s.health < health - 3, "fire hurts survivors");
        Tick(sim, 6, () => !z.Alive, () => { sim.SetInput(s.id, new InputState { face = 0 }); s.health = 100; });
        Check(!z.Alive && s.kills >= 1, "burning kills a walker and credits the thrower");
        w.minutes += 6;
        Tick(sim, .6f);
        Check(w.hazards.Count == 0, "hazards expire");
        health = s.health;
        Tick(sim, 1, null, () => sim.SetInput(s.id, new InputState { face = 0 }));
        Check(s.health >= health, "expired fire no longer burns");
    }

    static void KillFeed()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        float influence = w.community.influence;
        var f = Spawn(w, s.pos + new Vec2(1, 0), ZombieKind.Feral, 3.14f); f.state = ZombieState.Knocked; f.timer = 9;
        sim.HitZombie(f, s, 99, 0, false);
        Check(w.events.Any(e => e.kind == EventKind.KillFeed && e.text == "Mara Vale killed a Feral" && e.survivor == s.id), "freak kills reach the kill feed");
        Check(Math.Abs(w.community.influence - influence - 15) < .01f && Saw(w, EventKind.Influence, "+15"), "faction 0 earns the kind's influence");
        var a = Spawn(w, s.pos + new Vec2(20, 0), ZombieKind.Armored, 0); a.hp = .1f;
        sim.Blast(a.pos, 2, 5, null, "artillery");
        Check(w.events.Any(e => e.kind == EventKind.KillFeed && e.text == "Artillery killed an Armored"), "artillery kills are named");
        var g = Spawn(w, s.pos + new Vec2(-20, 0), ZombieKind.Walker, 0);
        int feed = w.events.Count(e => e.kind == EventKind.KillFeed);
        sim.HitZombie(g, s, 99, 0, false);
        Check(w.events.Count(e => e.kind == EventKind.KillFeed) == feed, "walker kills stay off the feed");
        var rival = w.AddSurvivor("Tess Monroe", "unemployed", true, null); rival.faction = 3; rival.pos = s.pos + new Vec2(0, 20);
        var j = Spawn(w, rival.pos + new Vec2(1, 0), ZombieKind.Juggernaut, 0); influence = w.community.influence;
        sim.HitZombie(j, rival, 99, 0, false);
        Check(w.events.Any(e => e.kind == EventKind.KillFeed && e.text == "Tess Monroe killed a Juggernaut") && w.community.influence == influence, "other factions' kills are shown but earn no influence");
    }

    static void Hordes()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        var target = new Vec2(w.map.width - s.pos.x, w.map.height - s.pos.y);
        if (Vec2.Distance(target, s.pos) < 300) target = new Vec2(s.pos.x < 512 ? 900 : 120, s.pos.y < 512 ? 900 : 120);
        int id = sim.SendHorde(target, 20);
        var h = w.HordeById(id);
        Check(h != null && h.sent && h.members.Count >= 15, "SendHorde spawns a horde (" + (h == null ? 0 : h.members.Count) + ")");
        if (h == null) return;
        float start = Vec2.Distance(h.center, target);
        double until = w.minutes + 60;
        while (w.minutes < until) sim.Tick(Simulation.Step, 100);
        float end = Vec2.Distance(h.center, target);
        Check(end < start - 40, "the horde marches on its target (" + start.ToString("0") + " -> " + end.ToString("0") + " m)");
        Check(h.members.All(m => w.zombies[m].Alive && w.zombies[m].horde == h.id && Vec2.Distance(w.zombies[m].pos, h.center) < 25), "members keep together");
        int again = sim.SendHorde(target, 20);
        Check(again == id, "a second request gathers the same horde");
        sim.ReleaseHorde(id);
        Check(!h.sent, "a released horde roams again");
        // Near a survivor, members really walk.
        var w2 = NewWorld(); Simulation sim2; var a = Arena(w2, out sim2);
        int id2 = sim2.SendHorde(a.pos + new Vec2(0, 30), 15); var h2 = w2.HordeById(id2);
        foreach (int m in h2.members) { var z = w2.zombies[m]; z.pos = a.pos + new Vec2(40, 0) + new Vec2(m % 5, m % 3); z.sight = 0; z.hearing = 0; w2.index.Moved(z); }
        h2.center = a.pos + new Vec2(40, 0);
        Tick(sim2, 6, null, () => sim2.SetInput(a.id, new InputState { face = 0 }));
        Check(h2.members.Count(m => w2.zombies[m].state == ZombieState.Investigate) > 8, "near survivors, horde members walk toward the target");
    }

    static void NightFerals()
    {
        var w = NewWorld(); Simulation sim; var s = Arena(w, out sim);
        w.minutes = Math.Floor(w.minutes / 1440) * 1440 + 22 * 60;   // 10 PM
        double until = w.minutes + 6 * 60; int ferals = 0;
        while (w.minutes < until) { sim.Tick(Simulation.Step, 40); s.health = 100; }
        ferals = w.zombies.Count(z => z.Alive && z.kind == ZombieKind.Feral && Vec2.Distance(z.pos, s.pos) < 200);
        Check(ferals >= 1 && ferals <= 5, "ferals come out at night near the community (" + ferals + ")");
        Check(w.zombies.Where(z => z.Alive && z.kind == ZombieKind.Feral && Vec2.Distance(z.pos, s.pos) < 200).All(z => !w.map.IsWater((int)z.pos.x, (int)z.pos.y) && !w.map.TileBlocked((int)z.pos.x, (int)z.pos.y)), "night ferals spawn on open land");
        var day = NewWorld(); Simulation dsim; var d = Arena(day, out dsim);
        until = day.minutes + 6 * 60;
        while (day.minutes < until) { dsim.Tick(Simulation.Step, 40); d.health = 100; }
        Check(day.zombies.Count(z => z.Alive && z.kind == ZombieKind.Feral && Vec2.Distance(z.pos, d.pos) < 200) == 0, "no extra ferals by day");
    }

    static void SaveLoad()
    {
        var w = NewWorld(); var sim = new Simulation(w);
        for (int i = 0; i < 100; i++) sim.Tick(Simulation.Step);
        var z = w.zombies.First(x => x.Alive && x.kind == ZombieKind.Walker); z.plague = true;
        w.hazards.Add(new Hazard { pos = new Vec2(100, 100), radius = 3.5f, until = (float)w.minutes + 10 });
        if (w.hearts.Count > 0) { w.hearts[0].hp = 7; w.hearts[0].discovered = true; }
        int sent = sim.SendHorde(new Vec2(500, 500), 25);
        string path = Path.Combine(Path.GetTempPath(), "ashfall-bestiary-" + Guid.NewGuid().ToString("N") + ".sav");
        SaveCodec.Save(w, path);
        var l = SaveCodec.Load(path); File.Delete(path);
        var a = w.zombies.Where(x => x.Alive).ToList(); var b = l.zombies;
        Check(a.Count == b.Count && a.Select(x => x.kind).SequenceEqual(b.Select(x => x.kind)), "zombie kinds restored");
        Check(a.Select(x => x.maxHp).SequenceEqual(b.Select(x => x.maxHp)) && a.Select(x => x.plague).SequenceEqual(b.Select(x => x.plague)) && a.Select(x => x.horde).SequenceEqual(b.Select(x => x.horde)), "maxHp, plague and horde restored");
        Check(l.hearts.Count == w.hearts.Count && l.hearts.Zip(w.hearts, (p, q) => p.hp == q.hp && p.discovered == q.discovered && p.destroyed == q.destroyed && Vec2.Distance(p.pos, q.pos) < .001f).All(x => x), "plague hearts restored");
        Check(l.hazards.Count == w.hazards.Count && Math.Abs(l.hazards[0].until - w.hazards[0].until) < .01f, "hazards restored");
        var lh = l.HordeById(sent);
        Check(l.hordes.Count == w.hordes.Count && lh != null && lh.sent && lh.members.Count == w.HordeById(sent).members.Count(m => w.zombies[m].Alive), "hordes restored with their members");
        var lsim = new Simulation(l);
        for (int i = 0; i < 40; i++) lsim.Tick(Simulation.Step);
        Check(l.zombies.Any(x => x.Alive), "a loaded county keeps running");
    }
}
