using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Draws drivable cars near the focus. The car model follows the host vehicle's position and heading
    /// (smoothed between 20 Hz simulation steps); headlights come on with the engine after dark. Presentation only.</summary>
    public sealed class VehicleView
    {
        public const float DrawRadius = 80;
        readonly World world; readonly Transform root;
        readonly Dictionary<int, Car> cars = new Dictionary<int, Car>();
        sealed class Car { public Transform body; public Light lights; public float lastSpeed, pitch, roll; }

        public VehicleView(World world, Transform parent)
        {
            this.world = world; root = new GameObject("Vehicles").transform; root.SetParent(parent, false);
        }
        public int Drawn { get { return cars.Count; } }
        public Transform ViewOf(int id) { Car c; return cars.TryGetValue(id, out c) ? c.body : null; }

        public void Sync(Vector3 focus, float dt)
        {
            var keep = new HashSet<int>();
            foreach (var v in world.vehicles)
            {
                if (Mathf.Abs(v.pos.x - focus.x) > DrawRadius || Mathf.Abs(v.pos.y - focus.z) > DrawRadius) continue;
                keep.Add(v.id);
                Car c; bool fresh = !cars.TryGetValue(v.id, out c);
                if (fresh) { c = Spawn(v); cars[v.id] = c; }
                var target = new Vector3(v.pos.x, WorldView.GroundHeight(world.map.GroundAt(Mathf.FloorToInt(v.pos.x), Mathf.FloorToInt(v.pos.y))), v.pos.y);
                // Nose dips under braking and lifts under throttle; the body leans into turns.
                float accel = dt > 0 ? (v.speed - c.lastSpeed) / dt : 0; c.lastSpeed = v.speed;
                c.pitch = Mathf.Lerp(c.pitch, Mathf.Clamp(-accel * .35f, -3, 3), 1 - Mathf.Exp(-dt * 6));
                c.roll = Mathf.Lerp(c.roll, Mathf.Clamp(v.steer * v.speed * .25f, -3, 3), 1 - Mathf.Exp(-dt * 6));
                var rot = Quaternion.Euler(c.pitch, AshfallGame.Yaw(v.heading), c.roll);
                if (fresh || v.driver < 0 && v.speed == 0) c.body.SetPositionAndRotation(target, Quaternion.Euler(0, AshfallGame.Yaw(v.heading), 0));
                else
                {
                    float k = 1 - Mathf.Exp(-dt * 18);
                    c.body.SetPositionAndRotation(Vector3.Lerp(c.body.position, target, k), Quaternion.Slerp(c.body.rotation, rot, k));
                }
                bool lit = v.engineOn && world.Daylight < .55f;
                if (lit && c.lights == null) c.lights = Headlights(c.body);
                if (c.lights != null) c.lights.enabled = lit;
            }
            foreach (var id in cars.Keys.Where(k => !keep.Contains(k)).ToList()) { Object.Destroy(cars[id].body.gameObject); cars.Remove(id); }
        }

        Car Spawn(Vehicle v)
        {
            var p = world.map.props[v.prop];
            var body = new GameObject("Vehicle " + v.id).transform; body.SetParent(root, false);
            var variant = ArtLibrary.Variant("vehicle", (int)(p.hp * 997));
            var model = (variant == null ? null : ArtLibrary.Spawn(variant, body)) ?? WorldView.CarModel(body, p);
            model.transform.localPosition = Vector3.zero;
            return new Car { body = body, lastSpeed = v.speed };
        }

        static Light Headlights(Transform body)
        {
            var go = new GameObject("Headlights"); go.transform.SetParent(body, false);
            go.transform.localPosition = new Vector3(0, .8f, 2.1f); go.transform.localRotation = Quaternion.Euler(12, 0, 0);
            var l = go.AddComponent<Light>(); l.type = LightType.Spot; l.range = 26; l.spotAngle = 70; l.intensity = 2.6f; l.color = new Color(1, .94f, .8f); l.shadows = LightShadows.None;
            return l;
        }

        public void Clear() { if (root != null) Object.Destroy(root.gameObject); cars.Clear(); }
    }
}
