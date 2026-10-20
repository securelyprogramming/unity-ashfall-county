using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>A drivable abandoned car. It owns the car prop (trunk container, model variant) and, while parked,
    /// blocks the tiles under its body. Position and heading are continuous; heading uses the core angle convention.</summary>
    [Serializable]
    public sealed class Vehicle
    {
        public int id, prop = -1;
        public string model = "";
        public Vec2 pos; public float heading, speed, steer;   // speed in m/s, positive forward
        public float fuel, condition = 1;                      // litres; 0 = wrecked
        public bool engineOn, keyInIgnition, hotwired;
        public int driver = -1;
        public int keyUid = -1;        // the car key item, wherever it ends up
        public int keyContainer = -1;  // container that receives the key when first searched
        public float noiseClock;
        public readonly List<int> tiles = new List<int>();     // tile indices blocked while parked

        public const float Length = 4f, Width = 2f, TankLiters = 40, MaxSpeed = 15, ReverseSpeed = 4.5f, GasCanLiters = 10;
        public const float IdleBurn = .004f, BurnPerMeter = .008f;   // litres per second idling, per metre driven
        public Vec2 Forward { get { return Vec2.FromAngle(heading); } }
        public Vec2 Right { get { var f = Forward; return new Vec2(f.y, -f.x); } }
        public bool Wrecked { get { return condition <= 0; } }
        /// <summary>Engine noise radius in metres: loud at idle, louder with speed.</summary>
        public float NoiseRadius { get { return engineOn ? 16 + Math.Abs(speed) * 2.2f : 0; } }
        public float TopSpeed { get { return MaxSpeed * (.55f + .45f * MathUtil.Clamp01(condition)); } }
        public static float HeadingFor(int rotation) { return MathUtil.WrapAngle((float)(Math.PI / 2 - (rotation & 3) * Math.PI / 2)); }

        /// <summary>Distance from a point to the car body (0 inside).</summary>
        public float BoxDistance(Vec2 p)
        {
            var rel = p - pos; float a = Math.Abs(Vec2.Dot(rel, Forward)) - Length / 2, b = Math.Abs(Vec2.Dot(rel, Right)) - Width / 2;
            float dx = Math.Max(0, a), dy = Math.Max(0, b); return (float)Math.Sqrt(dx * dx + dy * dy);
        }
        public bool Contains(Vec2 p, float margin = 0)
        {
            var rel = p - pos; return Math.Abs(Vec2.Dot(rel, Forward)) <= Length / 2 + margin && Math.Abs(Vec2.Dot(rel, Right)) <= Width / 2 + margin;
        }
        /// <summary>Closest point on the body outline to p.</summary>
        public Vec2 Nearest(Vec2 p)
        {
            var rel = p - pos; var f = Forward; var r = Right;
            float a = MathUtil.Clamp(Vec2.Dot(rel, f), -Length / 2, Length / 2), b = MathUtil.Clamp(Vec2.Dot(rel, r), -Width / 2, Width / 2);
            return pos + f * a + r * b;
        }
    }

    public sealed partial class World
    {
        readonly Dictionary<int, int> vehicleByProp = new Dictionary<int, int>();
        public Vehicle VehicleOfProp(int prop) { int id; return vehicleByProp.TryGetValue(prop, out id) ? vehicles[id] : null; }
        public Vehicle VehicleOf(Survivor s) { return s.vehicle >= 0 && s.vehicle < vehicles.Count ? vehicles[s.vehicle] : null; }

        /// <summary>Seeds which abandoned cars still run, their fuel, condition and where their keys are. Independent
        /// of the world RNG so the generator and older saves stay deterministic.</summary>
        public void SetupVehicles()
        {
            vehicles.Clear(); vehicleByProp.Clear();
            var r = new Rng((ulong)settings.seed, 23);
            // Key spots: living-area containers of houses, grouped by building.
            var homeBoxes = new Dictionary<int, List<int>>();
            foreach (var c in map.containers)
            {
                if (c.room < 0 || c.table != "living" && c.table != "kitchen" && c.table != "bedroom" && c.table != "office") continue;
                var b = map.buildings[map.rooms[c.room].building];
                if (b.kind != BuildingKind.House && b.kind != BuildingKind.Farmhouse) continue;
                List<int> list; if (!homeBoxes.TryGetValue(b.id, out list)) homeBoxes[b.id] = list = new List<int>();
                list.Add(c.id);
            }
            var models = new[] { "Sedan", "Hatchback", "Pickup" };
            foreach (var p in map.props)
            {
                if (p.kind != PropKind.Car || p.removed) continue;
                float roll = r.Float(), fuel = r.Range(.08f, .6f), cond = r.Range(.45f, 1f), keyRoll = r.Float();
                if (p.hp < .2f || roll > .4f) continue;   // wrecks and most cars stay dead
                var v = new Vehicle { id = vehicles.Count, prop = p.id, pos = p.Center, heading = Vehicle.HeadingFor(p.rotation), fuel = Vehicle.TankLiters * fuel, condition = cond, model = models[(int)(p.hp * 997) % models.Length] };
                if (keyRoll < .25f) v.keyInIgnition = true;
                else if (keyRoll < .5f && p.container >= 0) v.keyContainer = p.container;
                else if (keyRoll < .85f)
                {
                    int best = -1; float bestD = 40;
                    foreach (var kv in homeBoxes) { float d = Vec2.Distance(map.buildings[kv.Key].rect.Center, v.pos); if (d < bestD) { bestD = d; best = kv.Key; } }
                    if (best >= 0) { var list = homeBoxes[best]; v.keyContainer = list[r.Range(0, list.Count)]; }
                }
                vehicles.Add(v); vehicleByProp[p.id] = v.id;
                if (v.keyContainer >= 0 && map.containers[v.keyContainer].generated) PlaceKey(v);
                ReleasePropTiles(p); BlockVehicle(v);
            }
        }

        /// <summary>Rolls a container's loot the first time it is searched and drops in any car key waiting there.</summary>
        public void RollContainer(Container c)
        {
            if (c.generated) return;
            Loot.Roll(c, (ulong)settings.seed, settings.lootAbundance);
            foreach (var v in vehicles) if (v.keyContainer == c.id) PlaceKey(v);
        }
        void PlaceKey(Vehicle v)
        {
            var key = ItemCatalog.Make("car_key"); v.keyUid = key.uid;
            map.containers[v.keyContainer].inventory.Add(key); v.keyContainer = -1;
        }

        void ReleasePropTiles(Prop p)
        {
            int owner;
            for (int i = 0; i < p.w; i++) for (int j = 0; j < p.h; j++)
                {
                    if (!map.InBounds(p.x + i, p.y + j)) continue; int idx = map.Index(p.x + i, p.y + j);
                    if (map.blockers.TryGetValue(idx, out owner) && owner == p.id) map.blockers.Remove(idx);
                }
        }

        /// <summary>Parks the car: blocks the free tiles whose centres lie under its body and moves the prop's
        /// footprint there so the trunk and interactions follow the car.</summary>
        public void BlockVehicle(Vehicle v)
        {
            UnblockVehicle(v);
            var p = map.props[v.prop]; var f = v.Forward; var rt = v.Right;
            float ex = Math.Abs(f.x) * Vehicle.Length / 2 + Math.Abs(rt.x) * Vehicle.Width / 2, ey = Math.Abs(f.y) * Vehicle.Length / 2 + Math.Abs(rt.y) * Vehicle.Width / 2;
            int x0 = (int)Math.Floor(v.pos.x - ex), x1 = (int)Math.Floor(v.pos.x + ex), y0 = (int)Math.Floor(v.pos.y - ey), y1 = (int)Math.Floor(v.pos.y + ey);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!map.InBounds(x, y) || !v.Contains(new Vec2(x + .5f, y + .5f), -.02f)) continue;
                    int idx = map.Index(x, y); if (map.blockers.ContainsKey(idx) || map.ground[idx] == Ground.Water) continue;
                    map.blockers[idx] = p.id; v.tiles.Add(idx);
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            if (v.tiles.Count > 0) { p.x = minX; p.y = minY; p.w = maxX - minX + 1; p.h = maxY - minY + 1; }
            p.rotation = (int)Math.Round(MathUtil.WrapAngle((float)(Math.PI / 2) - v.heading) / (Math.PI / 2)) & 3;
            Unstick(v);
        }
        public void UnblockVehicle(Vehicle v)
        {
            int owner;
            foreach (int idx in v.tiles) if (map.blockers.TryGetValue(idx, out owner) && owner == v.prop) map.blockers.Remove(idx);
            v.tiles.Clear();
        }

        /// <summary>Nudges zombies and survivors out of tiles a parking car now blocks.</summary>
        void Unstick(Vehicle v)
        {
            var near = new List<Zombie>(); if (index != null) index.Query(v.pos, 5, near);
            foreach (var z in near) { var free = FreeSpot(z.pos, Zombie.Radius); if (free.HasValue) { z.pos = free.Value; index.Moved(z); } }
            foreach (var s in survivors) if (s.alive && s.vehicle < 0 && Vec2.Distance(s.pos, v.pos) < 5) { var free = FreeSpot(s.pos, Survivor.Radius); if (free.HasValue) s.pos = free.Value; }
        }
        Vec2? FreeSpot(Vec2 p, float radius)
        {
            if (!map.Collides(p, radius)) return null;
            for (float d = .35f; d <= 2.8f; d += .35f)
                for (int a = 0; a < 12; a++) { var q = p + Vec2.FromAngle(a * (float)Math.PI / 6) * d; if (!map.Collides(q, radius)) return q; }
            return null;
        }

        // ---------------------------------------------------------------- save section
        internal void WriteVehicles(BinaryWriter b)
        {
            b.Write(vehicles.Count);
            foreach (var v in vehicles)
            {
                b.Write(v.prop); b.Write(v.model ?? ""); b.Write(v.pos.x); b.Write(v.pos.y); b.Write(v.heading); b.Write(v.speed); b.Write(v.fuel); b.Write(v.condition);
                b.Write(v.engineOn); b.Write(v.keyInIgnition); b.Write(v.hotwired); b.Write(v.driver); b.Write(v.keyUid); b.Write(v.keyContainer);
            }
        }
        internal void ReadVehicles(BinaryReader b)
        {
            vehicles.Clear(); vehicleByProp.Clear();
            int n = b.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var v = new Vehicle { id = i, prop = b.ReadInt32(), model = b.ReadString(), pos = new Vec2(b.ReadSingle(), b.ReadSingle()), heading = b.ReadSingle(), speed = b.ReadSingle(), fuel = b.ReadSingle(), condition = b.ReadSingle() };
                v.engineOn = b.ReadBoolean(); v.keyInIgnition = b.ReadBoolean(); v.hotwired = b.ReadBoolean(); v.driver = b.ReadInt32(); v.keyUid = b.ReadInt32(); v.keyContainer = b.ReadInt32();
                if (v.prop < 0 || v.prop >= map.props.Count || map.props[v.prop].kind != PropKind.Car) throw new InvalidDataException("Save vehicles do not match the county.");
                if (v.keyContainer >= map.containers.Count) v.keyContainer = -1;
                vehicles.Add(v); vehicleByProp[v.prop] = v.id;
                ReleasePropTiles(map.props[v.prop]);
                var s = v.driver >= 0 && v.driver < survivors.Count ? survivors[v.driver] : null;
                if (s != null && s.alive) { s.vehicle = v.id; s.inVehicle = true; s.pos = v.pos; }
                else { v.driver = -1; v.speed = 0; BlockVehicle(v); }
            }
        }
    }

    public sealed partial class Simulation
    {
        static float Finite(float f) { return float.IsNaN(f) || float.IsInfinity(f) ? 0 : f; }
        public bool HasKey(Survivor s, Vehicle v) { return v.keyInIgnition || v.keyUid >= 0 && s.inventory.ByUid(v.keyUid) != null; }
        Vehicle VehicleRef(int id) { return id >= 0 && id < world.vehicles.Count ? world.vehicles[id] : null; }
        /// <summary>Survivor stands close enough to the car body and nothing solid is between them.</summary>
        bool ReachVehicle(Survivor s, Vehicle v, float range = 1.1f)
        {
            if (v == null || v.BoxDistance(s.pos) > range) return false;
            var near = v.Nearest(s.pos); return world.map.LineOfSight(s.pos, near + (s.pos - near).Normalized * .05f);
        }

        static bool AllowedInVehicle(CommandKind k)
        {
            switch (k)
            {
                case CommandKind.Cancel: case CommandKind.Wake: case CommandKind.Equip: case CommandKind.Unequip: case CommandKind.Wear: case CommandKind.Unwear:
                case CommandKind.Use: case CommandKind.Treat: case CommandKind.Reload: case CommandKind.ToggleLight: case CommandKind.CloseContainer:
                case CommandKind.ExitVehicle: case CommandKind.ToggleEngine: case CommandKind.Hotwire: return true;
                default: return false;
            }
        }

        /// <summary>Validates vehicle requests. Returns false for commands that are not about vehicles.</summary>
        bool VehicleCommand(Survivor s, Command c)
        {
            switch (c.kind)
            {
                case CommandKind.EnterVehicle:
                    {
                        var v = VehicleRef(c.target);
                        if (v == null || s.vehicle >= 0 || v.driver >= 0 || !ReachVehicle(s, v)) return true;
                        CancelAction(s, ""); s.lootContainer = -1;
                        world.UnblockVehicle(v);
                        v.driver = s.id; v.speed = 0; v.steer = 0; s.vehicle = v.id; s.inVehicle = true; s.pos = v.pos; s.facing = v.heading; s.running = s.sneaking = s.aiming = false;
                        world.Emit(EventKind.Engine, v.pos, s.id, "door");
                        world.Message(s, v.Wrecked ? "You climb into the wrecked " + v.model.ToLowerInvariant() + ". It won't go anywhere."
                            : HasKey(s, v) || v.hotwired ? "You get into the " + v.model.ToLowerInvariant() + ". " + (v.keyInIgnition ? "The keys are in the ignition." : v.hotwired ? "The wires are already stripped." : "You have its key.") + " Press E to start."
                            : "You get into the " + v.model.ToLowerInvariant() + ". No key: find one nearby, or hotwire it with a screwdriver.");
                        return true;
                    }
                case CommandKind.ExitVehicle:
                    {
                        var v = world.VehicleOf(s); if (v == null) return true;
                        if (Math.Abs(v.speed) > 1.5f) { world.Message(s, "Slow down before getting out."); return true; }
                        LeaveVehicle(s, v, false);
                        return true;
                    }
                case CommandKind.ToggleEngine:
                    {
                        var v = world.VehicleOf(s); if (v == null) return true;
                        if (v.engineOn) { v.engineOn = false; world.Emit(EventKind.Engine, v.pos, s.id, "stop"); world.Message(s, "You switch the engine off."); return true; }
                        if (v.Wrecked) { world.Message(s, "This car is wrecked."); return true; }
                        if (!HasKey(s, v) && !v.hotwired) { world.Message(s, "No key. Find it in the car or a nearby house, or hotwire it with a screwdriver."); return true; }
                        if (v.fuel <= 0) { world.Emit(EventKind.Engine, v.pos, s.id, "sputter"); world.Message(s, "The engine turns over but the tank is empty."); return true; }
                        world.MakeNoise(v.pos, 20, s.id);
                        if (world.rng.Chance(.35f * (1 - v.condition))) { world.Emit(EventKind.Engine, v.pos, s.id, "sputter"); world.Message(s, "The engine coughs but won't catch. Try again."); return true; }
                        v.engineOn = true; world.Emit(EventKind.Engine, v.pos, s.id, "start"); world.Message(s, "The engine starts. W/S to drive, A/D to steer, Space to brake, E to get out.");
                        return true;
                    }
                case CommandKind.Hotwire:
                    {
                        var v = world.VehicleOf(s); if (v == null || v.engineOn || v.hotwired || v.Wrecked) return true;
                        if (s.inventory.FindTag("screwdriver") == null) { world.Message(s, "Hotwiring needs a screwdriver."); return true; }
                        Begin(s, ActionKind.Hotwire, 9 * (1 - s.Level(Skill.Mechanics) * .06f), v.id);
                        return true;
                    }
                case CommandKind.Refuel:
                    {
                        var v = VehicleRef(c.target); if (v == null || s.vehicle >= 0 || !ReachVehicle(s, v, 1.3f)) return true;
                        if (v.fuel >= Vehicle.TankLiters - .05f) { world.Message(s, "The tank is full."); return true; }
                        if (c.item >= 0)
                        {
                            var can = s.inventory.ByUid(c.item); if (can == null || can.def != "gas_can") return true;
                            if (can.condition <= .001f) { world.Message(s, "The gas can is empty."); return true; }
                        }
                        else if (Pump(s) == null) { world.Message(s, world.PowerOn ? "Park beside a gas pump." : "The pumps are dead without power."); return true; }
                        Begin(s, ActionKind.Refuel, 5, v.id, c.item);
                        return true;
                    }
            }
            return false;
        }

        /// <summary>A working gas pump within reach (pumps need grid power).</summary>
        public Prop Pump(Survivor s) { return world.PowerOn ? NearbyProp(s, p => p.kind == PropKind.GasPump) : null; }

        void CompleteVehicleAction(Survivor s, TimedAction a)
        {
            var v = VehicleRef(a.target); if (v == null) return;
            if (a.kind == ActionKind.Hotwire)
            {
                if (s.vehicle != v.id || s.inventory.FindTag("screwdriver") == null) return;
                float chance = .3f + s.Level(Skill.Mechanics) * .12f + s.Level(Skill.Carpentry) * .03f;
                world.MakeNoise(v.pos, 6, s.id);
                if (world.rng.Chance(chance))
                {
                    v.hotwired = true; GainXp(s, Skill.Mechanics, 8);
                    if (v.fuel > 0 && !v.Wrecked) { v.engineOn = true; world.Emit(EventKind.Engine, v.pos, s.id, "start"); world.MakeNoise(v.pos, 20, s.id); world.Message(s, "The wires spark and the engine roars to life."); }
                    else world.Message(s, "You strip the ignition wires, but the tank is empty.");
                }
                else
                {
                    GainXp(s, Skill.Mechanics, 3); world.Emit(EventKind.Engine, v.pos, s.id, "sputter");
                    if (world.rng.Chance(.12f)) { s.health -= 3; s.hurtFlash = .3f; world.Message(s, "A wire bites you with a jolt. Try again."); }
                    else world.Message(s, "Sparks, but nothing catches. Try again.");
                }
                return;
            }
            if (a.kind == ActionKind.Refuel)
            {
                if (s.vehicle >= 0 || !ReachVehicle(s, v, 1.3f)) return;
                float space = Vehicle.TankLiters - v.fuel;
                if (a.item >= 0)
                {
                    var can = s.inventory.ByUid(a.item); if (can == null || can.def != "gas_can") return;
                    float litres = Math.Min(space, can.condition * Vehicle.GasCanLiters);
                    can.condition = Math.Max(0, can.condition - litres / Vehicle.GasCanLiters); v.fuel += litres;
                    world.Message(s, "You pour " + litres.ToString("0.#") + " L into the tank (" + (int)(v.fuel / Vehicle.TankLiters * 100) + "% full).");
                }
                else
                {
                    if (Pump(s) == null) return;
                    v.fuel = Vehicle.TankLiters; world.Message(s, "You fill the tank at the pump.");
                }
                GainXp(s, Skill.Mechanics, 1);
            }
        }

        /// <summary>Steps out beside the car. Parks it (re-blocks its tiles) and switches the engine off.</summary>
        void LeaveVehicle(Survivor s, Vehicle v, bool force)
        {
            v.speed = 0; v.steer = 0;
            world.BlockVehicle(v);
            var f = v.Forward; var left = v.Right * -1;
            Vec2? spot = null;
            foreach (var side in new[] { left, left * -1 })
                foreach (float along in new[] { .3f, -.6f, 1.1f, -1.4f })
                {
                    var p = v.pos + side * (Vehicle.Width / 2 + .45f) + f * along;
                    if (!world.map.Collides(p, Ashfall.Core.Survivor.Radius) && world.map.LineOfSight(v.pos, p)) { spot = p; goto found; }
                }
            foreach (var end in new[] { f, f * -1 })
            {
                var p = v.pos + end * (Vehicle.Length / 2 + .45f);
                if (!world.map.Collides(p, Ashfall.Core.Survivor.Radius) && world.map.LineOfSight(v.pos, p)) { spot = p; break; }
            }
        found:
            if (!spot.HasValue && !force) { world.UnblockVehicle(v); world.Message(s, "There's no room to open a door."); return; }
            if (v.engineOn) { v.engineOn = false; world.Emit(EventKind.Engine, v.pos, s.id, "stop"); }
            v.driver = -1; s.vehicle = -1; s.inVehicle = false; s.speed = 0;
            if (spot.HasValue) s.pos = spot.Value;
            world.Emit(EventKind.Engine, v.pos, s.id, "door");
            if (!force) world.Message(s, "You get out of the " + v.model.ToLowerInvariant() + ".");
        }
        /// <summary>Called when a survivor dies: the car is parked where it stopped.</summary>
        void ReleaseVehicle(Survivor s) { var v = world.VehicleOf(s); if (v != null) LeaveVehicle(s, v, true); else { s.vehicle = -1; s.inVehicle = false; } }

        /// <summary>A seated survivor rides along: no walking, combat or stamina drain; needs and health still tick.</summary>
        void RideAlong(Survivor s, float dt, float hours)
        {
            var v = world.VehicleOf(s); if (v == null) { s.vehicle = -1; s.inVehicle = false; return; }
            s.pos = v.pos; s.facing = v.heading; s.speed = Math.Abs(v.speed); s.running = s.sneaking = s.aiming = false; s.lootContainer = -1;
            s.endurance = Math.Min(100, s.endurance + dt * 4);
            if (s.action != null) ProgressAction(s, dt);
            Needs(s, hours, dt, false);
            Health(s, hours);
        }

        void UpdateVehicles(float dt)
        {
            foreach (var v in world.vehicles)
            {
                if (v.driver < 0 && !v.engineOn && v.speed == 0) continue;
                var d = v.driver >= 0 ? Survivor(v.driver) : null;
                var input = default(InputState); if (d != null && d.alive && d.knockedDown <= 0) inputs.TryGetValue(d.id, out input);
                Drive(v, d, input, dt);
            }
        }

        /// <summary>Host-side car physics: the owner sends throttle (moveY), steering (moveX) and brake (shove) only.</summary>
        void Drive(Vehicle v, Survivor d, InputState input, float dt)
        {
            float throttle = MathUtil.Clamp(Finite(input.moveY), -1, 1), steerIn = MathUtil.Clamp(Finite(input.moveX), -1, 1);
            bool brake = input.shove;
            if (!v.engineOn) throttle = 0;
            // Fuel: idling burns a little, distance burns most. An empty tank stalls the engine.
            if (v.engineOn)
            {
                v.fuel -= (Vehicle.IdleBurn + Math.Abs(v.speed) * Vehicle.BurnPerMeter) * dt;
                if (v.fuel <= 0) { v.fuel = 0; v.engineOn = false; world.Emit(EventKind.Engine, v.pos, v.driver, "stall"); if (d != null) world.Message(d, "The engine sputters and dies. Out of gas."); }
                v.noiseClock -= dt;
                if (v.noiseClock <= 0) { v.noiseClock = .5f; world.MakeNoise(v.pos, v.NoiseRadius, v.driver); }
            }
            float top = v.TopSpeed;
            if (brake) v.speed = MathUtil.MoveTowards(v.speed, 0, 16 * dt);
            else if (throttle > .05f) v.speed = v.speed < -.3f ? MathUtil.MoveTowards(v.speed, 0, 12 * dt) : Math.Min(top, v.speed + throttle * 6.5f * dt);
            else if (throttle < -.05f) v.speed = v.speed > .3f ? MathUtil.MoveTowards(v.speed, 0, 12 * dt) : Math.Max(-Vehicle.ReverseSpeed, v.speed + throttle * 4 * dt);
            else v.speed = MathUtil.MoveTowards(v.speed, 0, (1.5f + Math.Abs(v.speed) * .08f) * dt);
            // Rough ground slows the car.
            var g = world.map.GroundAt((int)Math.Floor(v.pos.x), (int)Math.Floor(v.pos.y));
            bool paved = g == Ground.Road || g == Ground.Parking || g == Ground.Bridge || g == Ground.Sidewalk || g == Ground.Concrete;
            if (!paved && Math.Abs(v.speed) > top * .6f) v.speed = MathUtil.MoveTowards(v.speed, Math.Sign(v.speed) * top * .6f, 8 * dt);
            v.steer = MathUtil.MoveTowards(v.steer, steerIn, 3.5f * dt);
            // Turning needs rolling wheels; positive steer turns right (clockwise from above).
            float yaw = -v.steer * 1.7f * Math.Min(1, Math.Abs(v.speed) / 4) * Math.Sign(v.speed);
            float dist = v.speed * dt; int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(dist) / .2f));
            for (int i = 0; i < steps; i++)
            {
                float h = MathUtil.WrapAngle(v.heading + yaw * dt / steps);
                var p = v.pos + Vec2.FromAngle(h) * (dist / steps);
                if (BodyCollides(p, h)) { Crash(v, d); break; }
                v.pos = p; v.heading = h;
            }
            RunOver(v, d);
            if (d != null) { d.pos = v.pos; d.facing = v.heading; d.speed = Math.Abs(v.speed); }
            if (v.driver < 0 && !v.engineOn && Math.Abs(v.speed) < .05f) { v.speed = 0; world.BlockVehicle(v); }
        }

        /// <summary>Car body as three overlapping circles against walls, fences, closed doors, water and props.</summary>
        bool BodyCollides(Vec2 p, float heading)
        {
            var f = Vec2.FromAngle(heading);
            for (int i = -1; i <= 1; i++) if (world.map.Collides(p + f * (i * 1.1f), .92f)) return true;
            return false;
        }

        void Crash(Vehicle v, Survivor d)
        {
            float impact = Math.Abs(v.speed);
            if (impact > 2.5f)
            {
                v.condition = Math.Max(0, v.condition - (impact - 2.5f) * .02f);
                world.Emit(EventKind.Crash, v.pos, v.driver, impact > 9 ? "hard" : "soft");
                world.MakeNoise(v.pos, 12 + impact * 2, v.driver);
                if (d != null && impact > 9)
                {
                    d.health -= (impact - 9) * 3; d.hurtFlash = .5f; world.Emit(EventKind.Hurt, d.pos, d.id, "crash");
                    world.Message(d, "You slam into it hard.");
                    if (d.health <= 0) Kill(d, "Died in a car crash.");
                }
                if (v.Wrecked && v.engineOn) { v.engineOn = false; world.Emit(EventKind.Engine, v.pos, v.driver, "stall"); if (d != null) world.Message(d, "Steam pours from the hood. The car is wrecked."); }
            }
            v.speed = -v.speed * .15f;
        }

        /// <summary>Zombies under the car: knocked down and hurt at speed, shoved aside when slow. Impacts damage the car.</summary>
        void RunOver(Vehicle v, Survivor d)
        {
            if (v.driver < 0 && v.speed == 0) return;
            world.index.Query(v.pos, 3.4f, query);
            foreach (var z in query)
            {
                if (!z.Alive || z.state == ZombieState.Climb || !v.Contains(z.pos, Zombie.Radius)) continue;
                float impact = Math.Abs(v.speed);
                if (impact > 3)
                {
                    PushOut(v, z);
                    HitZombie(z, d, impact * .11f * world.rng.Range(.8f, 1.2f), 1, false);
                    v.condition = Math.Max(0, v.condition - .004f * impact); v.speed *= .82f;
                    world.MakeNoise(z.pos, 12, v.driver);
                    if (z.Alive) { z.state = ZombieState.Knocked; z.timer = world.rng.Range(2.5f, 4.5f); }
                }
                else { PushOut(v, z); v.speed *= .7f; }
            }
        }

        /// <summary>Moves a zombie out of the car body along the shallowest axis.</summary>
        void PushOut(Vehicle v, Zombie z)
        {
            var rel = z.pos - v.pos; var f = v.Forward; var r = v.Right;
            float a = Vec2.Dot(rel, f), b = Vec2.Dot(rel, r);
            float pa = Vehicle.Length / 2 + Zombie.Radius - Math.Abs(a), pb = Vehicle.Width / 2 + Zombie.Radius - Math.Abs(b);
            if (pa <= 0 || pb <= 0) return;
            var push = pb < pa ? r * ((b >= 0 ? 1 : -1) * (pb + .03f)) : f * ((a >= 0 ? 1 : -1) * (pa + .03f));
            z.pos = world.map.Slide(z.pos, push, Zombie.Radius); world.index.Moved(z);
        }

        /// <summary>Zombies keep out of occupied cars (parked cars block tiles instead).</summary>
        void SeparateFromVehicles(Zombie z)
        {
            foreach (var s in world.survivors)
            {
                if (s.vehicle < 0) continue; var v = world.vehicles[s.vehicle];
                if (Vec2.Distance(v.pos, z.pos) < 3.4f && v.Contains(z.pos, Zombie.Radius)) PushOut(v, z);
            }
        }

        /// <summary>Distance a zombie compares with its reach. A survivor in a car is reached through the body.</summary>
        float PreyDistance(Zombie z, Survivor prey)
        {
            var v = world.VehicleOf(prey); return v == null ? Vec2.Distance(z.pos, prey.pos) : v.BoxDistance(z.pos) + .58f;
        }

        /// <summary>A zombie pounds on an occupied car; once the windows give, it can reach the driver.</summary>
        void StrikeVehicle(Zombie z, Survivor prey)
        {
            var v = world.VehicleOf(prey);
            if (v == null) { ZombieStrike(z, prey); return; }
            v.condition = Math.Max(0, v.condition - .006f);
            world.Emit(EventKind.Thump, z.pos, -1, "car", z.id); world.MakeNoise(z.pos, 6);
            if (v.condition < .3f && world.rng.Chance(.35f)) ZombieStrike(z, prey);
        }
    }
}
