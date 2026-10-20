using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Ashfall.Core;

/// <summary>Drivable cars: seeding, enter/exit reach, keys and hotwiring, fuel, collisions and saves.</summary>
static class VehicleTests
{
    static Action<bool, string> check;
    static readonly bool Verbose = Environment.GetEnvironmentVariable("ASHFALL_VERBOSE") == "1";
    static void Check(bool ok, string what) { if (Verbose) Console.WriteLine("  " + (ok ? "pass " : "FAIL ") + what); check(ok, what); }

    static World NewWorld(int seed = 4242) { return World.Create(new SandboxSettings { seed = seed, mapSize = 1024 }, "Mara Vale", "paramedic", true); }

    /// <summary>Clears zombies near p and rebuilds the index.</summary>
    static Simulation Quiet(World w, Vec2 p)
    {
        foreach (var z in w.zombies) if (Vec2.Distance(z.pos, p) < 120) z.state = ZombieState.Dead;
        w.index = new ZombieIndex(w);
        return new Simulation(w);
    }

    /// <summary>A straight, open east-west strip (no blocked tiles or edges) and the vehicle parked at its west end.</summary>
    static Vehicle Strip(World w, out Vec2 west, int length = 60)
    {
        var m = w.map;
        for (int y = 40; y < m.height - 40; y += 3)
            for (int x = 40; x < m.width - length - 40; x += 5)
            {
                bool clear = true;
                for (int i = -3; i <= length && clear; i++)
                    for (int j = -3; j <= 3 && clear; j++)
                    {
                        int tx = x + i, ty = y + j;
                        if (m.TileBlocked(tx, ty) || m.RoomAt(tx, ty) != null || m.H(tx, ty) != Edge.None || m.V(tx, ty) != Edge.None || m.H(tx, ty + 1) != Edge.None || m.V(tx + 1, ty) != Edge.None) clear = false;
                    }
                if (!clear) continue;
                west = new Vec2(x + .5f, y + .5f);
                var v = w.vehicles.First(c => c.driver < 0);
                w.UnblockVehicle(v); v.pos = west + new Vec2(2, 0); v.heading = 0; v.speed = 0; v.condition = 1; v.fuel = 30; v.keyInIgnition = true; w.BlockVehicle(v);
                return v;
            }
        throw new Exception("No open strip found.");
    }

    static void Ticks(Simulation sim, float seconds) { for (int i = 0; i < (int)(seconds / Simulation.Step); i++) sim.Tick(Simulation.Step); }
    static Vec2 Beside(Vehicle v) { return v.pos + v.Right * -(Vehicle.Width / 2 + .45f); }

    public static void Setup(Action<bool, string> check)
    {
        VehicleTests.check = check;
        var w = NewWorld(); var m = w.map;
        int cars = m.props.Count(p => p.kind == PropKind.Car);
        Check(w.vehicles.Count > 10 && w.vehicles.Count < cars, "some, not all, abandoned cars run (" + w.vehicles.Count + "/" + cars + ")");
        Check(w.vehicles.All(v => m.PropAt((int)v.pos.x, (int)v.pos.y) == m.props[v.prop]), "parked cars block their tiles");
        Check(w.vehicles.All(v => v.fuel > 0 && v.fuel < Vehicle.TankLiters && v.condition > 0), "cars start with some fuel and condition");
        Check(w.vehicles.Any(v => v.keyInIgnition) && w.vehicles.Any(v => v.keyContainer >= 0 && m.containers[v.keyContainer].prop != v.prop) && w.vehicles.Any(v => !v.keyInIgnition && v.keyContainer < 0), "keys: ignition, nearby houses, or missing");
        var w2 = NewWorld();
        Check(w.vehicles.Select(v => v.prop + ":" + v.fuel + ":" + v.keyContainer).SequenceEqual(w2.vehicles.Select(v => v.prop + ":" + v.fuel + ":" + v.keyContainer)), "vehicle seeding is deterministic");
        // A key waits in its container until that container is first searched.
        var keyed = w.vehicles.First(v => v.keyContainer >= 0); int box = keyed.keyContainer;
        Check(m.containers[box].inventory.Find("car_key") == null, "key not placed before searching");
        w.RollContainer(m.containers[box]);
        Check(keyed.keyUid >= 0 && m.containers[box].inventory.ByUid(keyed.keyUid) != null, "searching the container reveals the car key");
    }

    public static void EnterExit(Action<bool, string> check)
    {
        VehicleTests.check = check;
        var w = NewWorld(); var s = w.survivors[0]; Vec2 west; var v = Strip(w, out west); var sim = Quiet(w, v.pos);
        // Out of reach: refused.
        s.pos = v.pos + new Vec2(0, 6); sim.Submit(s.id, new Command { kind = CommandKind.EnterVehicle, target = v.id }); sim.Tick(Simulation.Step);
        Check(s.vehicle < 0 && v.driver < 0, "cannot enter a car from 6 m away");
        s.pos = Beside(v);
        sim.Submit(s.id, new Command { kind = CommandKind.EnterVehicle, target = v.id }); sim.Tick(Simulation.Step);
        Check(s.vehicle == v.id && v.driver == s.id && s.inVehicle, "enter a car within reach");
        Check(v.tiles.Count == 0 && w.map.PropAt((int)v.pos.x, (int)v.pos.y) == null, "an occupied car releases its parked tiles");
        // Only seat-legal commands are accepted while seated.
        var crate = w.map.containers.First(); sim.Submit(s.id, new Command { kind = CommandKind.OpenContainer, target = crate.id }); sim.Tick(Simulation.Step);
        Check(s.lootContainer != crate.id, "world interactions refused while seated");
        // Keys: no key and no ignition key refuses; a carried key starts it.
        v.keyInIgnition = false; v.keyUid = -1;
        sim.Submit(s.id, Command.Of(CommandKind.ToggleEngine)); sim.Tick(Simulation.Step);
        Check(!v.engineOn, "no key, no start");
        var key = ItemCatalog.Make("car_key"); s.inventory.Add(key); v.keyUid = key.uid; v.condition = 1;
        sim.Submit(s.id, Command.Of(CommandKind.ToggleEngine)); sim.Tick(Simulation.Step);
        Check(v.engineOn, "carried key starts the engine");
        sim.Submit(s.id, Command.Of(CommandKind.ExitVehicle)); sim.Tick(Simulation.Step);
        Check(s.vehicle < 0 && v.driver < 0 && !s.inVehicle, "exit the car");
        Check(!w.map.Collides(s.pos, Survivor.Radius) && v.BoxDistance(s.pos) < 1.2f, "exit lands on free ground beside the car");
        Check(!v.engineOn && v.tiles.Count > 0 && w.map.PropAt((int)v.pos.x, (int)v.pos.y) == w.map.props[v.prop], "exiting parks the car again");
        // Hotwire with a screwdriver and good Mechanics.
        v.keyUid = -1; s.inventory.items.Remove(key);
        sim.Submit(s.id, new Command { kind = CommandKind.EnterVehicle, target = v.id }); sim.Tick(Simulation.Step);
        sim.Submit(s.id, Command.Of(CommandKind.Hotwire)); sim.Tick(Simulation.Step);
        Check(s.action == null, "hotwiring needs a screwdriver");
        s.inventory.Add(ItemCatalog.Make("screwdriver")); s.level[(int)Skill.Mechanics] = 6;
        sim.Submit(s.id, Command.Of(CommandKind.Hotwire)); Ticks(sim, 10);
        Check(v.hotwired && v.engineOn, "a skilled survivor hotwires the car (" + (s.action == null ? "done" : s.action.kind.ToString()) + ")");
    }

    public static void DriveAndFuel(Action<bool, string> check)
    {
        VehicleTests.check = check;
        var w = NewWorld(); var s = w.survivors[0]; Vec2 west; var v = Strip(w, out west); var sim = Quiet(w, v.pos);
        s.pos = Beside(v); sim.Submit(s.id, new Command { kind = CommandKind.EnterVehicle, target = v.id }); sim.Tick(Simulation.Step);
        var start = v.pos; float fuel = v.fuel;
        sim.SetInput(s.id, new InputState { moveY = 1 }); Ticks(sim, 2);
        Check(Vec2.Distance(start, v.pos) < .01f, "a car without a running engine does not move");
        sim.Submit(s.id, Command.Of(CommandKind.ToggleEngine)); sim.Tick(Simulation.Step);
        Check(v.engineOn, "engine starts with the ignition key");
        w.noises.Clear();
        sim.SetInput(s.id, new InputState { moveY = 1 }); Ticks(sim, 3);
        float driven = Vec2.Distance(start, v.pos);
        Check(driven > 8 && v.pos.x > start.x, "throttle drives the car forward (" + driven.ToString("0.0") + " m)");
        Check(Vec2.Distance(s.pos, v.pos) < .001f, "the driver rides with the car");
        Check(v.fuel < fuel && fuel - v.fuel > driven * Vehicle.BurnPerMeter * .8f, "driving burns fuel (" + (fuel - v.fuel).ToString("0.000") + " L)");
        // Engine noise draws zombies from beyond footstep range.
        var z = w.AddZombie(v.pos + new Vec2(0, 24), w.rng); z.state = ZombieState.Idle; z.timer = 99; z.hearing = 1; z.facing = 1.57f;
        Ticks(sim, 1);
        Check(z.state == ZombieState.Investigate || z.state == ZombieState.Chase, "engine noise attracts zombies 24 m away (" + z.state + ")");
        z.state = ZombieState.Dead; w.index = new ZombieIndex(w);
        // Steering turns the car while rolling.
        float heading = v.heading; sim.SetInput(s.id, new InputState { moveY = .5f, moveX = 1 }); Ticks(sim, .6f);
        Check(MathUtil.WrapAngle(v.heading - heading) < -.2f, "steering right turns clockwise");
        sim.SetInput(s.id, new InputState { shove = true }); Ticks(sim, 2);
        Check(Math.Abs(v.speed) < .01f, "brake stops the car");
        // Running dry stalls the engine.
        v.fuel = .01f; sim.SetInput(s.id, new InputState { moveY = 1 }); Ticks(sim, 2);
        Check(v.fuel == 0 && !v.engineOn, "an empty tank stalls the engine");
        // Refuel from a gas can outside the car.
        sim.SetInput(s.id, new InputState { shove = true }); Ticks(sim, 1.5f);
        sim.SetInput(s.id, new InputState());
        sim.Submit(s.id, Command.Of(CommandKind.ExitVehicle)); sim.Tick(Simulation.Step);
        Check(s.vehicle < 0, "exit after stopping");
        var can = ItemCatalog.Make("gas_can"); s.inventory.Add(can);
        sim.Submit(s.id, new Command { kind = CommandKind.Refuel, target = v.id, item = can.uid }); Ticks(sim, 6);
        Check(Math.Abs(v.fuel - Vehicle.GasCanLiters) < .01f && can.condition < .001f, "a gas can pours 10 L into the tank (" + v.fuel.ToString("0.0") + " L)");
        Check(can.Label.Contains("empty"), "empty gas can is labelled (" + can.Label + ")");
        sim.Submit(s.id, new Command { kind = CommandKind.Refuel, target = v.id, item = can.uid }); sim.Tick(Simulation.Step);
        Check(s.action == null, "an empty can cannot refuel");
        s.pos = v.pos + new Vec2(0, 7); var full = ItemCatalog.Make("gas_can"); s.inventory.Add(full);
        sim.Submit(s.id, new Command { kind = CommandKind.Refuel, target = v.id, item = full.uid }); sim.Tick(Simulation.Step);
        Check(s.action == null, "refuelling needs reach");
    }

    public static void Collisions(Action<bool, string> check)
    {
        VehicleTests.check = check;
        var w = NewWorld(); var s = w.survivors[0]; Vec2 west; var v = Strip(w, out west); var sim = Quiet(w, v.pos);
        var m = w.map; int y = (int)v.pos.y;
        // A wall across the strip 30 m ahead.
        int wallX = (int)v.pos.x + 30; for (int j = y - 3; j <= y + 3; j++) m.SetV(wallX, j, Edge.Wall);
        s.pos = Beside(v); sim.Submit(s.id, new Command { kind = CommandKind.EnterVehicle, target = v.id }); sim.Tick(Simulation.Step);
        sim.Submit(s.id, Command.Of(CommandKind.ToggleEngine)); sim.Tick(Simulation.Step);
        // A zombie standing in the lane first.
        var z = w.AddZombie(v.pos + new Vec2(12, 0), w.rng); z.state = ZombieState.Idle; z.timer = 99; z.hp = 1.3f; z.facing = 3.14f; z.sight = 0; z.hearing = 0;
        float cond = v.condition; int events = 0;
        sim.SetInput(s.id, new InputState { moveY = 1 });
        for (int i = 0; i < 20 * 8; i++) { sim.Tick(Simulation.Step); events += w.events.Count(e => e.kind == EventKind.Crash); w.events.Clear(); }
        Check(!z.Alive || z.state == ZombieState.Knocked, "a zombie hit at speed is knocked down or killed (" + z.state + ", hp " + z.hp.ToString("0.00") + ")");
        Check(!v.Contains(z.pos, 0) || !z.Alive, "the zombie ends up outside the car body");
        Check(v.pos.x + Vehicle.Length / 2 <= wallX + .05f, "the car stops at the wall (front " + (v.pos.x + 2).ToString("0.00") + ", wall " + wallX + ")");
        Check(events > 0 && v.condition < cond - .05f, "crashing damages the car (" + cond.ToString("0.00") + " -> " + v.condition.ToString("0.00") + ")");
        // A blocking prop (crate) behind the car stops reversing.
        int crateX = (int)v.pos.x - 10; var crate = m.AddProp(PropKind.Crate, crateX, y);
        sim.SetInput(s.id, new InputState { moveY = -1 }); Ticks(sim, 6);
        Check(v.pos.x - Vehicle.Length / 2 >= crateX + 1 - .05f, "reversing stops at a crate");
        // Zombies pound on an occupied, stopped car.
        sim.SetInput(s.id, new InputState()); Ticks(sim, 1);
        v.condition = .9f; float before = v.condition;
        var a = w.AddZombie(v.pos + new Vec2(0, 4), w.rng); a.state = ZombieState.Idle; a.facing = -1.57f; a.speed = 1.5f;
        Ticks(sim, 25);
        Check(v.condition < before, "zombies damage an occupied car (" + before.ToString("0.00") + " -> " + v.condition.ToString("0.000") + ", " + a.state + ")");
        Check(!v.Contains(a.pos, 0), "zombies stay outside the car body");
    }

    public static void SaveLoad(Action<bool, string> check)
    {
        VehicleTests.check = check;
        var w = NewWorld(); var s = w.survivors[0]; Vec2 west; var v = Strip(w, out west); var sim = Quiet(w, v.pos);
        var original = new Vec2(w.map.props[v.prop].x, w.map.props[v.prop].y);
        s.pos = Beside(v); sim.Submit(s.id, new Command { kind = CommandKind.EnterVehicle, target = v.id }); sim.Tick(Simulation.Step);
        sim.Submit(s.id, Command.Of(CommandKind.ToggleEngine)); sim.Tick(Simulation.Step);
        sim.SetInput(s.id, new InputState { moveY = 1, moveX = .3f }); Ticks(sim, 2.5f);
        sim.SetInput(s.id, new InputState { shove = true }); Ticks(sim, 2);
        sim.SetInput(s.id, new InputState());
        sim.Submit(s.id, Command.Of(CommandKind.ExitVehicle)); sim.Tick(Simulation.Step);
        var key = ItemCatalog.Make("car_key"); s.inventory.Add(key); v.keyUid = key.uid; v.keyInIgnition = false; v.condition = .63f;
        // Seat in a second car so the driver link is saved too.
        var second = w.vehicles.First(c => c != v && c.driver < 0);
        s.pos = Beside(second); w.UnblockVehicle(second); second.driver = s.id; s.vehicle = second.id; s.inVehicle = true; s.pos = second.pos;
        string path = Path.Combine(Path.GetTempPath(), "ashfall-vehicle-" + Guid.NewGuid().ToString("N") + ".sav");
        SaveCodec.Save(w, path);
        var l = SaveCodec.Load(path); var lv = l.vehicles[v.id]; var ls = l.survivors[0];
        Check(l.vehicles.Count == w.vehicles.Count, "vehicle count restored");
        Check(Vec2.Distance(lv.pos, v.pos) < .001f && Math.Abs(lv.heading - v.heading) < .0001f && Math.Abs(lv.fuel - v.fuel) < .0001f && Math.Abs(lv.condition - .63f) < .0001f, "moved car position, heading, fuel and condition restored");
        Check(l.map.PropAt((int)lv.pos.x, (int)lv.pos.y) == l.map.props[lv.prop], "moved car blocks its new tiles after loading");
        bool oldFree = Vec2.Distance(original + new Vec2(.5f, .5f), lv.pos) > 3 ? l.map.PropAt((int)original.x, (int)original.y) == null : true;
        Check(oldFree, "its generated spot is free after loading");
        Check(lv.keyUid == key.uid && ls.inventory.ByUid(lv.keyUid) != null, "car key link restored");
        Check(ls.vehicle == second.id && l.vehicles[second.id].driver == ls.id && ls.inVehicle, "seated driver restored in the car");
        var lsim = new Simulation(l); lsim.Submit(ls.id, Command.Of(CommandKind.ExitVehicle)); lsim.Tick(Simulation.Step);
        Check(ls.vehicle < 0, "loaded driver can get out");
        // Saves from before drivable cars have no vehicle section: cars are seeded on load.
        string legacy = path + ".legacy";
        byte[] raw; using (var f = File.OpenRead(path)) using (var z = new GZipStream(f, CompressionMode.Decompress)) using (var ms = new MemoryStream()) { z.CopyTo(ms); raw = ms.ToArray(); }
        var marker = new byte[] { 8 }.Concat(System.Text.Encoding.UTF8.GetBytes("vehicles")).ToArray();
        int at = -1; for (int i = raw.Length - marker.Length; i >= 0 && at < 0; i--) if (raw.Skip(i).Take(marker.Length).SequenceEqual(marker)) at = i;
        Check(at > 0, "vehicle section found in the save");
        using (var f = File.Create(legacy)) using (var z = new GZipStream(f, CompressionLevel.Fastest)) z.Write(raw, 0, at);
        var old = SaveCodec.Load(legacy);
        Check(old.vehicles.Count == NewWorld().vehicles.Count && old.survivors[0].vehicle < 0, "legacy save loads and seeds cars");
        File.Delete(path); File.Delete(legacy);
    }
}
