using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Positional effects, ambience and music. Owned sound packs are used when restored privately;
    /// otherwise short synthesized stand-ins keep every cue audible.</summary>
    public sealed class AshfallAudio : MonoBehaviour
    {
        const string Root = "LicensedFrontier/Portfolio/Zomboid/Audio/";
        AshfallGame game;
        readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource music, wind, crickets, rain, river, ui, engine;
        /// <summary>Current engine loop volume and pitch, for diagnostics.</summary>
        public float EngineVolume { get { return engine == null ? 0 : engine.volume; } }
        public float EnginePitch { get { return engine == null ? 0 : engine.pitch; } }
        float groanBudget;
        public int PurchasedCues { get; private set; }

        static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
        {
            { "step-grass", new[] { "Footsteps/Footstep Grass Walking 1_01", "Footsteps/Footstep Grass Walking 1_02", "Footsteps/Footstep Grass Walking 1_03", "Footsteps/Footstep Grass Walking 1_04" } },
            { "step-wood", new[] { "Footsteps/Footstep Wood Walking 1_01", "Footsteps/Footstep Wood Walking 1_02", "Footsteps/Footstep Wood Walking 1_03", "Footsteps/Footstep Wood Walking 1_04" } },
            { "step-concrete", new[] { "Footsteps/Footstep Concrete Walking 1_01", "Footsteps/Footstep Concrete Walking 1_02", "Footsteps/Footstep Concrete Walking 1_03", "Footsteps/Footstep Concrete Walking 1_04" } },
            { "step-gravel", new[] { "Footsteps/Footstep Gravel Walking 1_01", "Footsteps/Footstep Gravel Walking 1_02", "Footsteps/Footstep Gravel Walking 1_03", "Footsteps/Footstep Gravel Walking 1_04" } },
            { "step-carpet", new[] { "Footsteps/Footstep Carpet Walking 1_01", "Footsteps/Footstep Carpet Walking 1_02", "Footsteps/Footstep Carpet Walking 1_03" } },
            { "run-grass", new[] { "Footsteps/Footstep Grass Running 1_01", "Footsteps/Footstep Grass Running 1_02" } },
            { "run-concrete", new[] { "Footsteps/Footstep Concrete Running 1_01", "Footsteps/Footstep Concrete Running 1_02" } },
            { "swing", new[] { "Weapons/FL_GFX_Swing_Dash_01", "Weapons/FL_GFX_Swing_Dash_02", "Weapons/FL_GFX_Swing_Dash_03", "Weapons/FL_GFX_Swing_Dash_05" } },
            { "hit-blunt", new[] { "Zombie/Impact_Flesh_001", "Zombie/Impact_Flesh_002", "Zombie/Impact_Flesh_003", "Zombie/Impact_Flesh_004", "Weapons/Impact_01", "Weapons/Impact_02" } },
            { "hit-blade", new[] { "Weapons/Hit_With_Blood_01", "Weapons/Hit_With_Blood_02", "Weapons/Hit_With_Blood_03", "Zombie/Impact_Flesh_Gory_Medium_001" } },
            { "kill", new[] { "Weapons/Bone_Crush", "Weapons/Break_Bone_01", "Zombie/Foley_BodyFall_001", "Zombie/Foley_BodyFall_Gory_001" } },
            { "shove", new[] { "Weapons/Hard_Punch_01", "Survival/Punch_1" } },
            { "down", new[] { "Zombie/Foley_BodyFall_002", "Weapons/Falling_On_The_Floor_01" } },
            { "gunshot", new[] { "Weapons/Glock17_Shoot_001", "Survival/Gun shot single_1", "Survival/Gun shot single_2" } },
            { "shotgun", new[] { "Weapons/Shotgun_BenelliM3_Shoot_001" } },
            { "empty", new[] { "Weapons/Glock17_Empty_01" } },
            { "reload", new[] { "Weapons/Glock17_ClipIn_001", "Survival/Gun reload_1" } },
            { "groan", new[] { "Zombie/FL_GFX_Voice_Monster_Zombie_Wheezing_01" } },
            { "chase", new[] { "Zombie/FL_GFX_Voice_Monster_Zombie_Roar_01", "Zombie/FL_GFX_Voice_Monster_Zombie_Roar_03" } },
            { "zombie-attack", new[] { "Zombie/Zombie001_Attack_A_001", "Zombie/Zombie001_Attack_A_002" } },
            { "hurt", new[] { "Zombie/Zombie001_Hurt_A_001", "Zombie/Blood_Splash_A_001" } },
            { "bite", new[] { "Zombie/Blood_Splash_A_002", "Zombie/Zombie001_Attack_A_002" } },
            { "block", new[] { "Weapons/Impact_02" } },
            { "death", new[] { "Zombie/Zombie001_Die_A_001" } },
            { "door-open", new[] { "World/FL_GFX_Home_Fx_Door_Small_01", "World/FL_GFX_Home_Fx_Door_Small_02" } },
            { "door-close", new[] { "World/FL_GFX_Home_Fx_Door_Small_03", "World/FL_GFX_Home_Fx_Door_Close_Lock" } },
            { "door-locked", new[] { "World/FL_GFX_Home_Fx_Door_Lock_01", "World/FL_GFX_Home_Foley_Keys" } },
            { "thump", new[] { "World/FL_GFX_Home_Fx_Door_Smack_01" } },
            { "glass", new[] { "World/A_Glass_Hard_001", "World/A_Glass_Mid_001", "World/A_Glass_Soft_001" } },
            { "break", new[] { "Weapons/Big_Hit", "World/FL_GFX_Home_Fx_Door_Smack_01" } },
            { "hammer", new[] { "Survival/Wood item drop", "World/FL_GFX_Home_Foley_Object_Pull" } },
            { "loot", new[] { "World/FL_GFX_Household_Foley_Devices_Latch_01", "World/FL_GFX_Home_Foley_Metal_Lid_Opening", "World/FL_GFX_Household_Foley_Devices_Fridge" } },
            { "eat", new[] { "World/FL_GFX_Home_Foley_Trash" } },
            { "drink", new[] { "World/FL_GFX_Home_Foley_Drop_01" } },
            { "heal", new[] { "Survival/First aid_1" } },
            { "craft", new[] { "Survival/Wood item pick up", "Survival/Metal item pick up" } },
            { "levelup", new[] { "Survival/Button press_1" } },
            { "radio", new[] { "World/FL_GFX_Household_Foley_Devices_Toggle_Switch_01" } },
            { "click", new[] { "UI/FL_GFX_UI_Click_01", "UI/FL_GFX_UI_Click_02", "UI/FL_GFX_UI_Click_03" } },
            { "open", new[] { "Survival/Open menu_1" } },
            { "close", new[] { "Survival/Close menu_1" } },
            { "error", new[] { "Survival/Error" } },
            { "zombie-step", new[] { "Zombie/Footsteps_Zombie_Grass_001", "Zombie/Footsteps_Zombie_concret_001", "Zombie/Footsteps_Zombie_Wood_001" } },
            { "amb-wind", new[] { "Ambience/Ambience_Wind_Intensity_Soft_With_Leaves_Loop" } },
            { "amb-crickets", new[] { "Ambience/Ambience_Crickets_01_Loop" } },
            { "amb-river", new[] { "Ambience/Ambience_River_Water_Movement_Soft_Intensity_Loop" } },
            { "music", new[] { "Music/16.The_Aftermath" } },
            // No owned vehicle audio is restored; these cues are synthesized.
            { "engine-loop", new string[0] }, { "engine-start", new string[0] }, { "engine-stop", new string[0] }, { "car-door", new string[0] }, { "crash", new string[0] }, { "sputter", new string[0] },
        };

        public void Initialize(AshfallGame game)
        {
            this.game = game;
            // Owned clips are indexed by file name, so pack sub-folders do not matter.
            var index = new Dictionary<string, AudioClip>();
            foreach (var c in Resources.LoadAll<AudioClip>(Root.TrimEnd('/'))) index[c.name] = c;
            foreach (var kv in Map)
            {
                AudioClip hit;
                var found = kv.Value.Select(p => index.TryGetValue(p.Substring(p.LastIndexOf('/') + 1), out hit) ? hit : null).Where(c => c != null).ToArray();
                if (found.Length > 0) { clips[kv.Key] = found; PurchasedCues++; }
                else clips[kv.Key] = new[] { Synth(kv.Key) };
            }
            Debug.Log("ASHFALL_AUDIO purchasedCues=" + PurchasedCues + "/" + Map.Count);
            for (int i = 0; i < 24; i++) { var s = gameObject.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0; pool.Add(s); }
            music = Loop("music", .0f); wind = Loop("amb-wind", 0); crickets = Loop("amb-crickets", 0); river = Loop("amb-river", 0);
            rain = Loop("rain", 0); engine = Loop("engine-loop", 0);
            ui = gameObject.AddComponent<AudioSource>(); ui.playOnAwake = false;
        }
        AudioSource Loop(string key, float volume)
        {
            var s = gameObject.AddComponent<AudioSource>(); s.loop = true; s.volume = volume; s.playOnAwake = false;
            s.clip = clips.ContainsKey(key) ? clips[key][0] : Synth(key); s.Play(); return s;
        }

        public void Ui(string key) { if (ui == null) return; var c = Pick(key); if (c != null) ui.PlayOneShot(c, .6f * game.Prefs.effects); }

        /// <summary>Plays a world cue with distance attenuation relative to the local survivor.</summary>
        public void Play(string key, Vec2 at, float volume = 1, float pitch = 1)
        {
            var clip = Pick(key); if (clip == null) return;
            var s = game.Player; float d = s == null ? 0 : Vec2.Distance(at, s.pos);
            float att = Mathf.Clamp01(1 - d / 38f); if (att <= .02f) return;
            var src = pool.FirstOrDefault(p => !p.isPlaying) ?? pool[UnityEngine.Random.Range(0, pool.Count)];
            src.pitch = pitch * UnityEngine.Random.Range(.93f, 1.07f);
            src.panStereo = s == null ? 0 : Mathf.Clamp((PanFor(at, s.pos)), -.8f, .8f);
            src.PlayOneShot(clip, volume * att * game.Prefs.effects);
        }
        float PanFor(Vec2 at, Vec2 listener)
        {
            var cam = game.Cam.transform.right; var d = new Vector3(at.x - listener.x, 0, at.y - listener.y);
            return Vector3.Dot(d.normalized, cam) * Mathf.Clamp01(d.magnitude / 6);
        }
        AudioClip Pick(string key) { AudioClip[] list; return clips.TryGetValue(key, out list) && list.Length > 0 ? list[UnityEngine.Random.Range(0, list.Length)] : null; }

        public void Event(SimEvent e, Survivor me)
        {
            switch (e.kind)
            {
                case EventKind.Footstep:
                    if (e.survivor != me.id) return;
                    var g = game.World.map.GroundAt((int)e.pos.x, (int)e.pos.y);
                    string surface = g == Ground.WoodFloor ? "wood" : g == Ground.TileFloor || g == Ground.Concrete || g == Ground.Road || g == Ground.Sidewalk || g == Ground.Parking || g == Ground.Bridge ? "concrete" : g == Ground.Gravel || g == Ground.Dirt || g == Ground.Sand ? "gravel" : "grass";
                    bool running = e.text == "run";
                    Play(running ? (surface == "grass" || surface == "gravel" ? "run-grass" : "run-concrete") : "step-" + surface, e.pos, e.text == "sneak" ? .25f : running ? .75f : .5f);
                    break;
                case EventKind.Swing: Play(e.text == "miss" ? "zombie-attack" : "swing", e.pos, .7f); break;
                case EventKind.Hit: Play(e.text == "blade" ? "hit-blade" : "hit-blunt", e.pos, .9f); break;
                case EventKind.Kill: Play("kill", e.pos, .9f); break;
                case EventKind.Shove: Play(e.text == "down" ? "down" : "shove", e.pos, .8f); break;
                case EventKind.Empty: Play("empty", e.pos); break;
                case EventKind.Groan:
                    if (groanBudget > 0) return; groanBudget = e.text == "chase" ? .6f : 1.5f;
                    Play(e.text == "spot" || e.text == "chase" ? "chase" : "groan", e.pos, e.text == "idle" ? .45f : .8f, UnityEngine.Random.Range(.85f, 1.1f));
                    break;
                case EventKind.Door: Play(e.text == "open" ? "door-open" : e.text == "locked" ? "door-locked" : "door-close", e.pos, .8f); break;
                case EventKind.Glass: Play("glass", e.pos, 1); break;
                case EventKind.Thump: Play("thump", e.pos, .8f); break;
                case EventKind.Eat: Play("eat", e.pos, .6f); break;
                case EventKind.Drink: Play("drink", e.pos, .6f); break;
                case EventKind.Fire: Play("craft", e.pos, .6f); break;
                case EventKind.Helicopter: Play("chase", e.pos, .3f, .5f); break;
                case EventKind.Engine: Play(e.text == "start" ? "engine-start" : e.text == "door" ? "car-door" : e.text == "sputter" ? "sputter" : "engine-stop", e.pos, .8f); break;
                case EventKind.Crash: Play("crash", e.pos, e.text == "hard" ? 1 : .6f); break;
            }
        }

        public void Ambience(World w, Survivor s, bool indoors)
        {
            groanBudget -= Time.deltaTime;
            float fx = game.Prefs.effects, day = w.Daylight;
            wind.volume = Mathf.Lerp(wind.volume, (indoors ? .08f : .28f) * fx, Time.deltaTime);
            crickets.volume = Mathf.Lerp(crickets.volume, (day < .4f && !w.raining ? (indoors ? .05f : .25f) : 0) * fx, Time.deltaTime);
            rain.volume = Mathf.Lerp(rain.volume, (w.raining ? (indoors ? .18f : .5f) * w.rainIntensity : 0) * fx, Time.deltaTime);
            bool nearRiver = Mathf.Abs(s.pos.x - w.gen.evacuationPoint.x - 30) < 40 || w.map.IsWater((int)s.pos.x + 6, (int)s.pos.y) || w.map.IsWater((int)s.pos.x - 6, (int)s.pos.y);
            river.volume = Mathf.Lerp(river.volume, (nearRiver ? .3f : 0) * fx, Time.deltaTime);
            music.volume = Mathf.Lerp(music.volume, game.Prefs.music * .35f, Time.deltaTime * .3f);
            Engine(w, s, fx);
        }

        /// <summary>Engine loop for the survivor's own car, or the loudest running car nearby; pitch follows speed.</summary>
        void Engine(World w, Survivor s, float fx)
        {
            Vehicle loudest = w.VehicleOf(s); float level = 0, pitch = .7f;
            if (loudest != null && loudest.engineOn) level = .55f;
            else
            {
                loudest = null;
                foreach (var v in w.vehicles)
                {
                    if (!v.engineOn) continue; float l = .5f * (1 - Vec2.Distance(v.pos, s.pos) / 40f);
                    if (l > level) { level = l; loudest = v; }
                }
            }
            if (loudest != null) { pitch = .7f + Mathf.Abs(loudest.speed) / Vehicle.MaxSpeed * 1.1f; engine.panStereo = loudest.driver == s.id ? 0 : Mathf.Clamp(PanFor(loudest.pos, s.pos), -.8f, .8f); }
            engine.volume = Mathf.Lerp(engine.volume, level * fx, 1 - Mathf.Exp(-Time.deltaTime * 8));
            engine.pitch = Mathf.Lerp(engine.pitch, pitch, 1 - Mathf.Exp(-Time.deltaTime * 5));
        }
        void Update() { if (game != null && game.World == null && music != null) { music.volume = Mathf.Lerp(music.volume, game.Prefs.music * .5f, Time.deltaTime * .5f); wind.volume = Mathf.Lerp(wind.volume, .15f * game.Prefs.effects, Time.deltaTime); rain.volume = Mathf.Lerp(rain.volume, 0, Time.deltaTime); engine.volume = 0; } }

        // ---------------------------------------------------------------- synthesized stand-ins
        /// <summary>Four-cylinder style rumble: firing pulses at 55 Hz with harmonics (whole cycles per second, so it loops).</summary>
        static float EngineWave(float t, float rev)
        {
            float b = 55 * rev, ph = t * b % 1;
            return Mathf.Sin(t * 2 * Mathf.PI * b) * .5f + Mathf.Sin(t * 2 * Mathf.PI * b * 2) * .3f + Mathf.Sin(t * 2 * Mathf.PI * b * 4) * .12f + (ph < .18f ? .35f : -.07f);
        }
        static AudioClip Synth(string key)
        {
            int rate = 22050; float duration; Func<float, System.Random, float> f;
            var rnd = new System.Random(key.GetHashCode());
            switch (key.Split('-')[0])
            {
                case "step": case "run": duration = .12f; f = (t, r) => (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 60) * .5f; break;
                case "swing": duration = .25f; f = (t, r) => (float)(r.NextDouble() * 2 - 1) * Mathf.Sin(t / .25f * Mathf.PI) * .3f; break;
                case "hit": case "kill": case "shove": case "down": case "thump": case "break": case "block": case "hammer": duration = .25f; f = (t, r) => (Mathf.Sin(t * 2 * Mathf.PI * 90) * .7f + (float)(r.NextDouble() * 2 - 1) * .3f) * Mathf.Exp(-t * 18) * .8f; break;
                case "gunshot": case "shotgun": duration = .6f; f = (t, r) => (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 9) * (t < .01f ? 1 : .8f); break;
                case "glass": duration = .7f; f = (t, r) => (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 5) * (Mathf.Sin(t * 2 * Mathf.PI * 3200) * .5f + .5f) * .6f; break;
                case "groan": case "chase": case "zombie": duration = 1.2f; f = (t, r) => { float b = 70 + Mathf.Sin(t * 6) * 15; return (Mathf.Sin(t * 2 * Mathf.PI * b) + .5f * Mathf.Sin(t * 2 * Mathf.PI * b * 2.01f) + .2f * (float)(r.NextDouble() * 2 - 1)) * Mathf.Sin(t / 1.2f * Mathf.PI) * .35f; }; break;
                case "engine":
                    if (key == "engine-loop") { duration = 1; f = (t, r) => EngineWave(t, 1) * .32f + (float)(r.NextDouble() * 2 - 1) * .015f; }
                    else if (key == "engine-start") { duration = 1.4f; f = (t, r) => t < .6f ? (Mathf.Sin(t * 2 * Mathf.PI * 34) > .3f ? 1 : 0) * Mathf.Sin(t * 2 * Mathf.PI * 160) * .25f + (float)(r.NextDouble() * 2 - 1) * .05f : EngineWave(t, 1.3f - (t - .6f) * .35f) * .36f * Mathf.Min(1, (t - .6f) * 8); }
                    else { duration = .8f; f = (t, r) => EngineWave(t, 1 - t * .5f) * .3f * (1 - t / .8f); }
                    break;
                case "sputter": duration = .9f; f = (t, r) => EngineWave(t, .8f) * .3f * (Mathf.Sin(t * 2 * Mathf.PI * 4) > 0 ? 1 : .1f) * (1 - t / .9f); break;
                case "car": duration = .3f; f = (t, r) => (Mathf.Sin(t * 2 * Mathf.PI * 70) * .8f + (float)(r.NextDouble() * 2 - 1) * .25f) * Mathf.Exp(-t * 22) * .8f; break;
                case "crash": duration = .9f; f = (t, r) => ((float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 6) * .7f + Mathf.Sin(t * 2 * Mathf.PI * 55) * Mathf.Exp(-t * 10) * .6f + (float)(r.NextDouble() * 2 - 1) * Mathf.Sin(t * 2 * Mathf.PI * 3000) * Mathf.Exp(-t * 4) * .2f) * .9f; break;
                case "rain": duration = 2; f = (t, r) => (float)(r.NextDouble() * 2 - 1) * .25f; break;
                case "amb": duration = 3; f = (t, r) => (float)(r.NextDouble() * 2 - 1) * .06f * (.6f + .4f * Mathf.Sin(t * 2)); break;
                case "music": duration = 16; f = (t, r) => { int beat = (int)(t / 2) % 8; float[] notes = { 110, 130.8f, 146.8f, 130.8f, 98, 116.5f, 130.8f, 98 }; return (Mathf.Sin(t * 2 * Mathf.PI * notes[beat]) * .5f + Mathf.Sin(t * 2 * Mathf.PI * notes[beat] * 1.5f) * .2f) * .12f; }; break;
                default: duration = .12f; f = (t, r) => Mathf.Sin(t * 2 * Mathf.PI * 880) * Mathf.Exp(-t * 30) * .3f; break;
            }
            int n = (int)(duration * rate); var data = new float[n];
            bool loop = key.EndsWith("-loop");   // loops wrap seamlessly, so they get no fade
            for (int i = 0; i < n; i++) { float t = (float)i / rate; data[i] = f(t, rnd) * (loop ? 1 : Mathf.Min(1, Mathf.Min(t / .005f, (duration - t) / .02f))); }
            var clip = AudioClip.Create("synth-" + key, n, 1, rate, false); clip.SetData(data, 0); return clip;
        }
    }
}
