using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Presentation half of the game root: sim events to audio/effects, camera, lighting and actor views.</summary>
    public sealed partial class AshfallGame
    {
        /// <summary>Creates the views for a freshly entered session.</summary>
        void BeginPresentation(World w)
        {
            var s = Player;
            playerView = ActorView.Create(transform, false, 0, s.female, s.female ? "survivor_female" : "survivor_male");
            playerView.Snap(Pos(s.pos), Yaw(s.facing));
        }

        /// <summary>Destroys every session view (called when leaving a session).</summary>
        void ClearPresentation()
        {
            foreach (var z in zombieViews.Values) Destroy(z.gameObject); zombieViews.Clear();
            foreach (var z in zombiePool) Destroy(z.gameObject); zombiePool.Clear();
            foreach (var c in corpseViews.Values) Destroy(c.gameObject); corpseViews.Clear();
            if (playerView != null) Destroy(playerView.gameObject);
        }

        // ---------------------------------------------------------------- events, audio and messages
        void Events()
        {
            var s = Player;
            foreach (var e in World.events)
            {
                bool mine = e.survivor == s.id;
                switch (e.kind)
                {
                    case EventKind.Message: if (mine || e.survivor < 0) UI.Log(e.text); break;
                    case EventKind.Radio: if (mine) { Radio.Add(World.Clock + " — " + e.text); UI.Log("<color=#9fd3ff>[Radio] " + e.text + "</color>"); Audio.Play("radio", e.pos); } break;
                    case EventKind.LevelUp: if (mine) { UI.Log("<color=#ffe08a>" + e.text + "</color>"); Audio.Play("levelup", e.pos); } break;
                    case EventKind.Hurt: if (mine) { shake = .6f; Audio.Play(e.text == "blocked" ? "block" : "hurt", e.pos); if (e.text != "blocked" && e.text != "glass") UI.Log("<color=#ff8a7a>" + e.text + "</color>"); } break;
                    case EventKind.Bite: if (mine) { shake = 1.2f; Audio.Play("bite", e.pos); UI.Log("<color=#ff5050>" + e.text + "</color>"); } break;
                    case EventKind.Break: case EventKind.Craft: case EventKind.Barricade: if (!string.IsNullOrEmpty(e.text) && (mine || Vector2.Distance(new Vector2(e.pos.x, e.pos.y), new Vector2(s.pos.x, s.pos.y)) < 20)) UI.Log(e.kind == EventKind.Craft ? "Made: " + e.text : e.kind == EventKind.Barricade ? "Barricaded: " + e.text : e.text); Audio.Play(e.kind == EventKind.Break ? "break" : "hammer", e.pos); break;
                    case EventKind.Evacuated: UI.Log("<color=#9fffb0>" + e.text + "</color>"); break;
                    case EventKind.Death: break;
                    case EventKind.Crash: if (mine) shake = e.text == "hard" ? 1.2f : .5f; Audio.Event(e, s); break;
                    case EventKind.Gunshot: Audio.Play(e.text == "shotgun" ? "shotgun" : "gunshot", e.pos); if (mine) shake = .5f; break;
                    case EventKind.Hit: { ActorView zv; if (e.zombie >= 0 && zombieViews.TryGetValue(e.zombie, out zv)) zv.Hit(); Audio.Event(e, s); break; }
                    default: Audio.Event(e, s); break;
                }
            }
            World.events.Clear();
        }

        // ---------------------------------------------------------------- presentation
        void Present(float dt)
        {
            var s = Player; if (s == null) return;
            shake = Mathf.Max(0, shake - dt * 2.5f);
            var target = Pos(s.pos);
            var car = World.VehicleOf(s); float size = zoom; var camTarget = target;
            // Driving pulls the camera back and ahead of the car so there is time to react.
            if (car != null) { camTarget += new Vector3(Mathf.Cos(car.heading), 0, Mathf.Sin(car.heading)) * car.speed * .55f; size = zoom + 5 + Mathf.Abs(car.speed) * .45f; }
            camFocus = Vector3.Lerp(camFocus, camTarget, 1 - Mathf.Exp(-dt * 8));
            PlaceCamera(camFocus, size);
            View.Refresh(camFocus, -CameraForward(), 2);
            var primary = s.Primary;
            playerView.SetHeld(primary == null ? "" : primary.def);
            var shirt = s.Worn(WearSlot.Torso); var pants = s.Worn(WearSlot.Legs);
            playerView.SetClothes(shirt == null ? new Color(.75f, .72f, .66f) : ClothColor(shirt.def), pants == null ? new Color(.7f, .6f, .5f) : ClothColor(pants.def));
            playerView.SetVisible(car == null);
            if (car == null) playerView.Pose(target, Yaw(s.facing), s.speed, s.swingClock > 0, s.knockedDown > 0 || s.sleeping, !s.alive, s.aiming, dt);
            SyncZombies(s, dt);
            SyncCorpses(s);
            bool indoors = World.map.Indoors(s.pos);
            Lighting(World.Daylight, indoors, target);
            flashlight.enabled = s.lightOn && s.alive;
            if (flashlight.enabled) { var dir = new Vector3(Mathf.Cos(s.facing), -.25f, Mathf.Sin(s.facing)); flashlight.transform.SetPositionAndRotation(target + Vector3.up * 1.4f, Quaternion.LookRotation(dir)); }
            playerGlow.transform.position = target + Vector3.up * 2.5f;
            var emission = rain.emission; emission.rateOverTime = World.raining ? 2600 * World.rainIntensity : 0;
            if (World.raining && !rain.isPlaying) rain.Play(); if (!World.raining && rain.isPlaying) rain.Stop();
            rain.transform.position = target + Vector3.up * 16;
            Audio.Ambience(World, s, indoors);
        }

        static Color ClothColor(string id)
        {
            switch (id)
            {
                case "hoodie": return new Color(.32f, .36f, .3f);
                case "tshirt": return new Color(.7f, .68f, .6f);
                case "leather_jacket": return new Color(.22f, .15f, .1f);
                case "police_vest": return new Color(.15f, .17f, .22f);
                case "jeans": return new Color(.2f, .26f, .4f);
                default: return new Color(.4f, .4f, .4f);
            }
        }

        void Lighting(float daylight, bool indoors, Vector3 focus)
        {
            float h = World != null ? World.HourOfDay : 15;
            float sunAngle = (h - 6) / 15f * 180;
            sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(18, 62, Mathf.Sin(Mathf.Clamp01(daylight) * Mathf.PI * .5f)), sunAngle - 60, 0);
            var dayColor = new Color(1, .96f, .88f); var duskColor = new Color(1, .62f, .42f); var night = new Color(.35f, .42f, .62f);
            bool dusk = h > 17.5f && h < 21.5f || h > 5 && h < 7.5f;
            sun.color = daylight > .05f ? Color.Lerp(night, dusk ? duskColor : dayColor, daylight) : night;
            sun.intensity = Mathf.Lerp(.08f, 1.15f, daylight) * (World != null && World.raining ? .7f : 1);
            var ambient = Color.Lerp(new Color(.13f, .145f, .21f), new Color(.52f, .54f, .58f), daylight);
            if (indoors) ambient *= .78f;
            RenderSettings.ambientLight = ambient;
            float fog = World == null ? 0 : World.fog + (World.raining ? .25f * World.rainIntensity : 0);
            RenderSettings.fogColor = Color.Lerp(new Color(.04f, .05f, .07f), new Color(.62f, .66f, .7f), daylight);
            RenderSettings.fogStartDistance = Mathf.Lerp(100, 60, fog); RenderSettings.fogEndDistance = Mathf.Lerp(220, 120, fog);
            Cam.backgroundColor = RenderSettings.fogColor;
            playerGlow.intensity = Mathf.Lerp(.9f, 0, daylight * 1.6f);
        }

        void SyncZombies(Survivor s, float dt)
        {
            var vis = Sim.visible.ContainsKey(s.id) ? Sim.visible[s.id] : null;
            float now = Time.time;
            if (vis != null) foreach (int id in vis) lastSeen[id] = now;
            var keep = new HashSet<int>();
            foreach (var kv in lastSeen)
            {
                var z = World.zombies[kv.Key];
                if (!z.Alive || now - kv.Value > 1.2f) continue;
                if (Vector2.Distance(new Vector2(z.pos.x, z.pos.y), new Vector2(s.pos.x, s.pos.y)) > 60) continue;
                keep.Add(kv.Key);
                ActorView a;
                if (!zombieViews.TryGetValue(kv.Key, out a))
                {
                    a = zombiePool.Count > 0 ? zombiePool.Pop() : ActorView.Create(transform, true, z.variant, z.female, z.female ? "zombie_female" : "zombie");
                    a.SetVisible(true); a.Snap(Pos(z.pos), Yaw(z.facing)); zombieViews[kv.Key] = a;
                }
                float moving = z.state == ZombieState.Chase ? z.speed : z.state == ZombieState.Wander ? z.speed * .45f : z.state == ZombieState.Investigate ? z.speed * .8f : 0;
                a.Pose(Pos(z.pos), Yaw(z.facing), moving, z.state == ZombieState.Attack || z.state == ZombieState.Thump, z.state == ZombieState.Knocked || z.crawler, false, false, dt);
            }
            foreach (var id in zombieViews.Keys.Where(k => !keep.Contains(k)).ToList()) { var a = zombieViews[id]; a.SetVisible(false); zombiePool.Push(a); zombieViews.Remove(id); }
            foreach (var id in lastSeen.Keys.Where(k => !keep.Contains(k) && now - lastSeen[k] > 5).ToList()) lastSeen.Remove(id);
        }

        void SyncCorpses(Survivor s)
        {
            var keep = new HashSet<int>();
            foreach (var c in World.corpses)
            {
                if (Vector2.Distance(new Vector2(c.pos.x, c.pos.y), new Vector2(s.pos.x, s.pos.y)) > 45) continue;
                keep.Add(c.id);
                ActorView a;
                if (!corpseViews.TryGetValue(c.id, out a)) { a = ActorView.Create(transform, true, c.variant, c.female, c.female ? "zombie_female" : "zombie"); a.Snap(Pos(c.pos), Yaw(c.facing)); a.Settle(); corpseViews[c.id] = a; }
                a.Pose(Pos(c.pos), Yaw(c.facing), 0, false, true, true, false, 1);
            }
            foreach (var id in corpseViews.Keys.Where(k => !keep.Contains(k)).ToList()) { Destroy(corpseViews[id].gameObject); corpseViews.Remove(id); }
        }
    }
}
