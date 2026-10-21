using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Positional effects, ambience and music. Owned sound packs are used when restored privately;
    /// otherwise short synthesized stand-ins keep every cue audible.
    /// Music has three moods (SetMusicMood): "explore" (default), "base" and "combat"; the keys "music-combat",
    /// "music-base" and "music-explore" passed to Play or Ui switch mood instead of playing a one-shot.</summary>
    public sealed class AshfallAudio : MonoBehaviour
    {
        /// <summary>Owned clips are indexed by file name under these Resources roots (zomboid-selection and decay-selection).</summary>
        static readonly string[] Roots = { "LicensedFrontier/Portfolio/Zomboid/Audio", "LicensedFrontier/Portfolio/Decay/Audio" };
        AshfallGame game;
        readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource music, musicNext, wind, crickets, rain, river, ui, engine;
        /// <summary>Current engine loop volume and pitch, for diagnostics.</summary>
        public float EngineVolume { get { return engine == null ? 0 : engine.volume; } }
        public float EnginePitch { get { return engine == null ? 0 : engine.pitch; } }
        float groanBudget, stompBudget, mix = 1, combatHold;
        public int PurchasedCues { get; private set; }
        /// <summary>Keys that resolved to owned clips (others use synthesized stand-ins).</summary>
        public readonly List<string> OwnedKeys = new List<string>();
        /// <summary>Current music mood: "explore", "base" or "combat".</summary>
        public string MusicMood { get; private set; }

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
            { "deny", new[] { "Survival/Error" } },
            { "zombie-step", new[] { "Zombie/Footsteps_Zombie_Grass_001", "Zombie/Footsteps_Zombie_concret_001", "Zombie/Footsteps_Zombie_Wood_001" } },
            { "amb-wind", new[] { "Ambience/Ambience_Wind_Intensity_Soft_With_Leaves_Loop" } },
            { "amb-crickets", new[] { "Ambience/Ambience_Crickets_01_Loop" } },
            { "amb-river", new[] { "Ambience/Ambience_River_Water_Movement_Soft_Intensity_Loop" } },
            { "music", new[] { "Music/16.The_Aftermath" } },
            // No owned vehicle audio is restored; these cues are synthesized.
            { "engine-loop", new string[0] }, { "engine-start", new string[0] }, { "engine-stop", new string[0] }, { "car-door", new string[0] }, { "crash", new string[0] }, { "sputter", new string[0] },

            // ---- open county (decay-selection). Keys with no owned clip use the synthesized stand-in.
            { "scream", new[] { "Monsters/Distorted_Screams_08", "Monsters/Distorted_Screams_03" } },
            { "bloater-burst", new[] { "Monsters/FL_GFX_Voice_Monster_Zombie_Bile_01", "Monsters/Blood_Explosion_01" } },
            { "gas", new string[0] },
            { "feral-screech", new[] { "Monsters/FL_GFX_Voice_Monster_Zombie_Roar_06", "Monsters/FL_GFX_Voice_Monster_Zombie_Roar_02" } },
            { "pounce", new[] { "Zombie/Zombie002_Attack_A_002", "Weapons/FL_GFX_Swing_Dash_02" } },
            { "juggernaut-roar", new[] { "Monsters/FL_GFX_Voice_Monster_Zombie_Roar_07", "Monsters/FL_GFX_Voice_Monster_Zombie_Roar_09" } },
            { "juggernaut-step", new[] { "Monsters/Heavy_Step_01", "Monsters/Heavy_Step_02" } },
            { "charge", new[] { "Monsters/FL_GFX_Voice_Monster_Zombie_Roar_08" } },
            { "heart-pulse", new string[0] },
            { "heart-scream", new[] { "Monsters/Distorted_Screams_10" } },
            { "heart-destroyed", new[] { "Monsters/Hollow_Screams" } },
            { "explosion", new[] { "Explosions/Grenade_Bomb_Small_Dull_Explosion_01", "Explosions/Big_Boom" } },
            { "molotov", new[] { "World/A_Glass_Hard_001", "Events/Fire_Ignite_2" } },
            { "artillery-siren", new[] { "Events/FL_GFX_Alarm_Siren_01" } },
            { "artillery-whistle", new string[0] },
            { "artillery-impact", new[] { "Explosions/Big_Boom", "Explosions/Anime_Explosion_01" } },
            { "plane", new string[0] },
            { "helicopter", new string[0] },
            { "airdrop-land", new[] { "Weapons/Big_Hit", "Zombie/Foley_BodyFall_002" } },
            { "flare", new[] { "Events/Fire_Ignite_1" } },
            { "rifle", new[] { "Weapons/M16_Shoot_Auto_001", "Weapons/M16_Shoot_Auto_002" } },
            { "smg", new[] { "Weapons/Smg_MP5_Shoot_Auto_001", "Weapons/Smg_MP5_Shoot_Auto_002" } },
            { "sniper", new[] { "Weapons/Sniper_ArcticWarfare_Shoot_01" } },
            { "human-hurt", new[] { "Voices/Bryce_Pain_A", "Voices/Bryce_Pain_B", "Voices/DLaina_Pain_A", "Voices/DLaina_Pain_B", "Voices/Hannah_Pain_A" } },
            { "human-death", new[] { "Voices/Bryce_Pain_G", "Voices/DLaina_Pain_E" } },
            { "human-shout", new[] { "Voices/FL_GFX_Voice_War_Strategy_Cover_me", "Voices/FL_GFX_Voice_War_Strategy_Target_spotted", "Voices/FL_GFX_Voice_War_Strategy_Frag_out", "Voices/FL_GFX_Voice_War_Strategy_Hold_the_line", "Voices/FL_GFX_Voice_War_Strategy_I_see_one", "Voices/FL_GFX_Voice_War_Strategy_Need_a_medic_here" } },
            { "radio-chatter", new[] { "Radio/FL_GFX_Voice_Radio_Commands_Radio_check_over", "Radio/FL_GFX_Voice_Radio_Commands_Copy_that_Moving_out", "Radio/FL_GFX_Voice_Radio_Commands_Be_advised_hostile_reinforcements_incoming", "Radio/FL_GFX_Voice_Radio_Commands_Drop_zone_is_hot_Repeat_drop_zone_is_hot", "Radio/FL_GFX_Voice_Radio_Commands_Airstrike_inbound_Keep_your_heads_down" } },
            { "recruit", new[] { "UI/FL_GFX_Notification_Sfx_06" } },
            { "facility-built", new[] { "UI/FL_GFX_Checkpoint_Error_Riddle_Sfx_03", "UI/Hammer_Hit_1" } },
            { "mission", new[] { "UI/FL_GFX_Notification_Sfx_03" } },
            { "influence", new[] { "UI/FL_GFX_Notification_Sfx_02" } },
            { "morale-down", new[] { "UI/FL_GFX_Checkpoint_Error_Riddle_Sfx_05" } },
            { "kill-feed", new[] { "UI/FL_GFX_UI_Game_Interface_01" } },
            { "music-combat", new[] { "Music/Dawn_Of_Chaos" } },
            { "music-base", new[] { "Music/24.Fallout" } },
        };

        /// <summary>Audio key for a gunshot: the weapon item id or event text ("assault_rifle", "smg", "hunting_rifle", "shotgun", ...).</summary>
        public static string GunshotKey(string weapon)
        {
            if (string.IsNullOrEmpty(weapon)) return "gunshot";
            if (weapon.Contains("shotgun")) return "shotgun";
            if (weapon == "smg" || weapon.Contains("submachine")) return "smg";
            if (weapon.Contains("sniper") || weapon.Contains("hunting_rifle") || weapon == "rifle") return "sniper";
            if (weapon.Contains("assault") || weapon.Contains("rifle")) return "rifle";
            return "gunshot";
        }

        public void Initialize(AshfallGame game)
        {
            this.game = game;
            // Owned clips are indexed by file name, so pack sub-folders do not matter.
            var index = new Dictionary<string, AudioClip>();
            foreach (var root in Roots) foreach (var c in Resources.LoadAll<AudioClip>(root)) index[c.name] = c;
            foreach (var kv in Map)
            {
                AudioClip hit;
                var found = kv.Value.Select(p => index.TryGetValue(p.Substring(p.LastIndexOf('/') + 1), out hit) ? hit : null).Where(c => c != null).ToArray();
                if (found.Length > 0) { clips[kv.Key] = found; PurchasedCues++; OwnedKeys.Add(kv.Key); }
                else clips[kv.Key] = new[] { Synth(kv.Key) };
            }
            Debug.Log("ASHFALL_AUDIO purchasedCues=" + PurchasedCues + "/" + Map.Count + " synthesized=" + string.Join(",", Map.Keys.Where(k => !OwnedKeys.Contains(k))));
            for (int i = 0; i < 24; i++) { var s = gameObject.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0; pool.Add(s); }
            music = Loop("music", .0f); musicNext = Loop("music", 0); musicNext.Stop(); MusicMood = "explore";
            wind = Loop("amb-wind", 0); crickets = Loop("amb-crickets", 0); river = Loop("amb-river", 0);
            rain = Loop("rain", 0); engine = Loop("engine-loop", 0);
            ui = gameObject.AddComponent<AudioSource>(); ui.playOnAwake = false;
        }
        AudioSource Loop(string key, float volume)
        {
            var s = gameObject.AddComponent<AudioSource>(); s.loop = true; s.volume = volume; s.playOnAwake = false;
            s.clip = clips.ContainsKey(key) ? clips[key][0] : Synth(key); s.Play(); return s;
        }

        /// <summary>Switches the music bed with a crossfade: "explore" (county), "base" (calm home theme) or "combat"
        /// (sieges, freak fights, red zones). Combat relaxes back to explore after three minutes unless requested again.</summary>
        public void SetMusicMood(string mood)
        {
            if (music == null) return;
            mood = mood == "combat" || mood == "base" ? mood : "explore";
            if (mood == "combat") combatHold = 180;
            if (mood == MusicMood) return;
            MusicMood = mood;
            var key = mood == "combat" ? "music-combat" : mood == "base" ? "music-base" : "music";
            var clip = clips.ContainsKey(key) ? clips[key][0] : Synth(key);
            if (music.clip == clip) return;
            var old = music; music = musicNext; musicNext = old;
            music.clip = clip; music.volume = 0; music.Play(); mix = 0;
        }
        static bool MusicKey(string key, out string mood)
        {
            mood = null; if (key == null || !key.StartsWith("music-")) return false;
            mood = key.Substring(6); return true;
        }
        void Music(float target, float rate)
        {
            combatHold -= Time.deltaTime; if (MusicMood == "combat" && combatHold <= 0) SetMusicMood("explore");
            mix = Mathf.MoveTowards(mix, 1, Time.deltaTime / 3f);
            music.volume = Mathf.Lerp(music.volume, target * mix, Time.deltaTime * rate * 4);
            if (musicNext.isPlaying) { musicNext.volume = Mathf.MoveTowards(musicNext.volume, 0, Time.deltaTime / 3f * Mathf.Max(.05f, target)); if (musicNext.volume <= .001f) musicNext.Stop(); }
        }

        public void Ui(string key)
        {
            string mood; if (MusicKey(key, out mood)) { SetMusicMood(mood); return; }
            if (ui == null) return; var c = Pick(key); if (c != null) ui.PlayOneShot(c, .6f * game.Prefs.effects);
        }

        /// <summary>Plays a world cue with distance attenuation relative to the local survivor.</summary>
        public void Play(string key, Vec2 at, float volume = 1, float pitch = 1)
        {
            string mood; if (MusicKey(key, out mood)) { SetMusicMood(mood); return; }
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
        AudioClip Pick(string key)
        {
            AudioClip[] list; if (clips.TryGetValue(key, out list) && list.Length > 0) return list[UnityEngine.Random.Range(0, list.Length)];
            if (key == null) return null;
            var synth = Synth(key); clips[key] = new[] { synth }; return synth;   // unknown keys still sound (and never throw)
        }

        /// <summary>Default audio for events the presentation layer does not handle itself.</summary>
        public void Event(SimEvent e, Survivor me)
        {
            switch (e.kind)
            {
                case EventKind.Footstep:
                    if (e.text == "juggernaut") { if (stompBudget <= 0) { stompBudget = .25f; Play("juggernaut-step", e.pos, .9f, .8f); } return; }
                    if (me == null || e.survivor != me.id) return;
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
                case EventKind.Gunshot: Play(GunshotKey(e.text), e.pos); break;
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
                case EventKind.Helicopter: Play("helicopter", e.pos, .5f); break;
                case EventKind.Engine: Play(e.text == "start" ? "engine-start" : e.text == "door" ? "car-door" : e.text == "sputter" ? "sputter" : "engine-stop", e.pos, .8f); break;
                case EventKind.Crash: Play("crash", e.pos, e.text == "hard" ? 1 : .6f); break;
                // ---- open county
                case EventKind.Scream: Play("scream", e.pos, 1); break;
                case EventKind.Explosion: Play(e.text == "molotov" ? "molotov" : e.text == "bloater" || e.text == "burst" ? "bloater-burst" : e.text == "artillery" || e.text == "shell" ? "artillery-impact" : "explosion", e.pos, 1); break;
                case EventKind.Gas: Play(e.text == "burst" ? "bloater-burst" : "gas", e.pos, .8f); break;
                case EventKind.Pounce: Play(e.text == "hit" ? "pounce" : "feral-screech", e.pos, .9f); break;
                case EventKind.Charge: Play("charge", e.pos, 1); break;
                case EventKind.Grab: Play(e.text == "start" ? "juggernaut-roar" : "zombie-attack", e.pos, .9f); break;
                case EventKind.Heart: Play(e.text == "pulse" ? "heart-pulse" : e.text == "scream" ? "heart-scream" : "hit-blade", e.pos, .8f); break;
                case EventKind.HeartDestroyed: Play("heart-destroyed", e.pos, 1); break;
                case EventKind.Airdrop: if (e.text == "plane") Ui("plane"); else if (e.text == "land") Play("airdrop-land", e.pos); else if (e.text == "announce") Ui("radio-chatter"); else if (e.text == "open") Play("loot", e.pos); break;
                case EventKind.Artillery: if (e.text == "warn") Ui("artillery-siren"); else if (e.text == "shell") { Play("artillery-whistle", e.pos, .6f); Play("artillery-impact", e.pos, 1); } break;
                case EventKind.Recruit: Ui("recruit"); break;
                case EventKind.Facility: Ui(e.text == "start" ? "hammer" : "facility-built"); break;
                case EventKind.Mission: Ui("mission"); break;
                case EventKind.Influence: Ui("influence"); break;
                case EventKind.Morale: Ui(e.text == "down" ? "morale-down" : "levelup"); break;
                case EventKind.KillFeed: Ui("kill-feed"); break;
                case EventKind.Siege: SetMusicMood(e.text == "start" ? "combat" : "base"); break;
            }
        }

        public void Ambience(World w, Survivor s, bool indoors)
        {
            groanBudget -= Time.deltaTime; stompBudget -= Time.deltaTime;
            float fx = game.Prefs.effects, day = w.Daylight;
            wind.volume = Mathf.Lerp(wind.volume, (indoors ? .08f : .28f) * fx, Time.deltaTime);
            crickets.volume = Mathf.Lerp(crickets.volume, (day < .4f && !w.raining ? (indoors ? .05f : .25f) : 0) * fx, Time.deltaTime);
            rain.volume = Mathf.Lerp(rain.volume, (w.raining ? (indoors ? .18f : .5f) * w.rainIntensity : 0) * fx, Time.deltaTime);
            bool nearRiver = Mathf.Abs(s.pos.x - w.gen.evacuationPoint.x - 30) < 40 || w.map.IsWater((int)s.pos.x + 6, (int)s.pos.y) || w.map.IsWater((int)s.pos.x - 6, (int)s.pos.y);
            river.volume = Mathf.Lerp(river.volume, (nearRiver ? .3f : 0) * fx, Time.deltaTime);
            Music(game.Prefs.music * (MusicMood == "combat" ? .45f : .35f), .3f);
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
        void Update()
        {
            if (game != null && game.World == null && music != null)
            {
                if (MusicMood == "combat") SetMusicMood("explore");
                Music(game.Prefs.music * .5f, .5f);
                wind.volume = Mathf.Lerp(wind.volume, .15f * game.Prefs.effects, Time.deltaTime); rain.volume = Mathf.Lerp(rain.volume, 0, Time.deltaTime); engine.volume = 0;
            }
        }

        // ---------------------------------------------------------------- synthesized stand-ins
        /// <summary>Four-cylinder style rumble: firing pulses at 55 Hz with harmonics (whole cycles per second, so it loops).</summary>
        static float EngineWave(float t, float rev)
        {
            float b = 55 * rev, ph = t * b % 1;
            return Mathf.Sin(t * 2 * Mathf.PI * b) * .5f + Mathf.Sin(t * 2 * Mathf.PI * b * 2) * .3f + Mathf.Sin(t * 2 * Mathf.PI * b * 4) * .12f + (ph < .18f ? .35f : -.07f);
        }
        static float Noise(System.Random r) { return (float)(r.NextDouble() * 2 - 1); }
        static float Sine(float t, float f) { return Mathf.Sin(t * 2 * Mathf.PI * f); }
        /// <summary>Phase-continuous sweep: frequency moves linearly from f0 to f1 over the duration d.</summary>
        static float Sweep(float t, float f0, float f1, float d) { return Mathf.Sin(2 * Mathf.PI * (f0 * t + (f1 - f0) * t * t / (2 * d))); }
        static float Tones(float t, float[] notes, float step) { int i = Mathf.Min(notes.Length - 1, (int)(t / step)); float local = t - i * step; return (Sine(t, notes[i]) * .6f + Sine(t, notes[i] * 2) * .2f) * Mathf.Exp(-local * 6) * .45f; }
        static float Voice(float t, float pitch, System.Random r) { float p = t * pitch % 1; return ((p < .5f ? p * 2 : 2 - p * 2) - .5f) * .8f * (.7f + .3f * Sine(t, 3)) + Noise(r) * .05f; }

        /// <summary>Stand-in for a key without owned clips (deterministic per key). New open-county keys have their own shapes.</summary>
        static AudioClip Synth(string key)
        {
            int rate = 22050; float duration; Func<float, System.Random, float> f;
            var rnd = new System.Random(key.GetHashCode());
            float lp = 0;   // one-pole low-pass state for rumbles
            switch (key)
            {
                case "scream": case "heart-scream": duration = 1.3f; f = (t, r) => (Sweep(t, 700, 1300, 1.3f) * .5f + Sine(t, 1400 + Sine(t, 9) * 120) * .25f + Noise(r) * .2f) * Mathf.Sin(t / 1.3f * Mathf.PI) * .5f; break;
                case "feral-screech": duration = .6f; f = (t, r) => (Sweep(t, 1500, 900, .6f) * .5f + Noise(r) * .35f) * Mathf.Sin(t / .6f * Mathf.PI) * .5f; break;
                case "juggernaut-roar": case "charge": duration = 1.5f; f = (t, r) => { lp += (Noise(r) - lp) * .08f; return (Sine(t, 62 + Sine(t, 5) * 8) * .5f + Sine(t, 124) * .2f + lp * 1.4f) * Mathf.Sin(t / 1.5f * Mathf.PI) * .6f; }; break;
                case "juggernaut-step": case "airdrop-land": duration = .5f; f = (t, r) => { lp += (Noise(r) - lp) * .05f; return (Sine(t, 45) * .8f + lp * 1.5f) * Mathf.Exp(-t * 9) * .9f; }; break;
                case "bloater-burst": duration = .8f; f = (t, r) => { lp += (Noise(r) - lp) * .2f; return (lp * 1.6f + Sine(t, 70) * .5f) * Mathf.Exp(-t * 6) * .8f; }; break;
                case "gas": duration = 1.6f; f = (t, r) => { float n = Noise(r); lp += (n - lp) * .5f; return (n - lp) * .5f * Mathf.Min(1, t * 6) * Mathf.Exp(-t * 1.2f); }; break;
                case "pounce": duration = .35f; f = (t, r) => Noise(r) * Mathf.Sin(t / .35f * Mathf.PI) * .45f + Sine(t, 160) * Mathf.Exp(-t * 14) * .3f; break;
                case "heart-pulse": duration = .75f; f = (t, r) => (Sine(t, 52) + Sine(t, 104) * .3f) * (Mathf.Exp(-t * 18) + (t > .24f ? Mathf.Exp(-(t - .24f) * 16) * .8f : 0)) * .8f; break;
                case "heart-destroyed": duration = 2.2f; f = (t, r) => { lp += (Noise(r) - lp) * .1f; return (Sweep(t, 300, 60, 2.2f) * .4f + lp * 1.2f) * Mathf.Exp(-t * 1.4f) * .8f; }; break;
                case "explosion": case "artillery-impact": duration = 1.6f; f = (t, r) => { lp += (Noise(r) - lp) * .12f; return (lp * 2.2f + Sine(t, 38) * .6f * Mathf.Exp(-t * 3) + Noise(r) * .4f * Mathf.Exp(-t * 20)) * Mathf.Exp(-t * 2.4f); }; break;
                case "molotov": duration = .9f; f = (t, r) => Noise(r) * (Mathf.Exp(-t * 12) * (Sine(t, 3100) * .5f + .5f) * .6f + (t > .12f ? .18f * (.5f + .5f * Sine(t, 13)) * (1 - t / .9f) : 0)); break;
                case "artillery-siren": duration = 4; f = (t, r) => { float s = Sweep(t % 2, 380, 880, 2); return (s > 0 ? .35f : -.35f) * .5f + s * .25f; }; break;
                case "artillery-whistle": duration = 1.6f; f = (t, r) => (Sweep(t, 1900, 650, 1.6f) * .5f + Noise(r) * .04f) * Mathf.Min(1, t * 2) * .5f; break;
                case "plane": duration = 4; f = (t, r) => (EngineWave(t, 1.5f) * .3f * (.8f + .2f * Sine(t, 12)) + Noise(r) * .05f) * Mathf.Sin(t / 4 * Mathf.PI) * .7f; break;
                case "helicopter": duration = 3; f = (t, r) => { lp += (Noise(r) - lp) * .15f; float blade = Mathf.Pow(Mathf.Max(0, Sine(t, 17)), 6); return (lp * 1.5f * blade + Sine(t, 70) * .15f) * Mathf.Sin(t / 3 * Mathf.PI) * .8f; }; break;
                case "flare": duration = .9f; f = (t, r) => Noise(r) * (t < .03f ? 1 : .35f) * Mathf.Exp(-t * 2.5f) * .5f; break;
                case "rifle": duration = .45f; f = (t, r) => Noise(r) * Mathf.Exp(-t * 12) * (t < .006f ? 1 : .85f); break;
                case "smg": duration = .25f; f = (t, r) => Noise(r) * Mathf.Exp(-t * 20) * (t < .004f ? 1 : .7f); break;
                case "sniper": duration = 1.0f; f = (t, r) => { lp += (Noise(r) - lp) * .1f; return Noise(r) * Mathf.Exp(-t * 14) + lp * 1.5f * Mathf.Exp(-t * 3); }; break;
                case "human-hurt": duration = .32f; f = (t, r) => Voice(t, 190 - t * 120, r) * Mathf.Sin(t / .32f * Mathf.PI) * .4f; break;
                case "human-death": duration = .9f; f = (t, r) => Voice(t, 210 - t * 140, r) * Mathf.Sin(t / .9f * Mathf.PI) * .4f; break;
                case "human-shout": duration = .6f; f = (t, r) => Voice(t, 230 + Sine(t, 6) * 12, r) * Mathf.Sin(t / .6f * Mathf.PI) * .4f; break;
                case "radio-chatter": duration = 1.1f; f = (t, r) => { float n = Noise(r); lp += (n - lp) * .35f; return (lp * .8f + Voice(t, 160, r) * .25f) * (Sine(t, 7) > -.2f ? 1 : .1f) * .5f; }; break;
                case "recruit": duration = .7f; f = (t, r) => Tones(t, new[] { 523f, 659f, 784f }, .16f); break;
                case "facility-built": duration = .8f; f = (t, r) => Tones(t, new[] { 392f, 523f, 659f, 1046f }, .14f); break;
                case "mission": duration = .7f; f = (t, r) => Tones(t, new[] { 440f, 554f, 659f }, .18f); break;
                case "influence": duration = .45f; f = (t, r) => Tones(t, new[] { 1318f, 1760f }, .1f); break;
                case "morale-down": duration = .7f; f = (t, r) => Tones(t, new[] { 392f, 311f }, .25f); break;
                case "kill-feed": duration = .1f; f = (t, r) => Sine(t, 1500) * Mathf.Exp(-t * 40) * .3f; break;
                case "music-combat": duration = 16; f = (t, r) => { float beat = t * 2.4f % 1; int bar = (int)(t / 2) % 4; float[] root = { 73.4f, 73.4f, 82.4f, 65.4f }; return (Sine(t, root[bar]) * .5f + Sine(t, root[bar] * 2) * .2f) * .14f * (1 - beat * .6f) + Noise(r) * (beat < .05f ? .12f : 0); }; break;
                case "music-base": duration = 16; f = (t, r) => { int chord = (int)(t / 4) % 4; float[] notes = { 196, 174.6f, 164.8f, 174.6f }; return (Sine(t, notes[chord]) * .4f + Sine(t, notes[chord] * 1.26f) * .25f + Sine(t, notes[chord] * 1.5f) * .2f) * .09f * (.8f + .2f * Sine(t, .25f)); }; break;
                default: return Legacy(key);
            }
            return Build(key, duration, rate, f, rnd);
        }
        static AudioClip Legacy(string key)
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
            return Build(key, duration, rate, f, rnd);
        }
        static AudioClip Build(string key, float duration, int rate, Func<float, System.Random, float> f, System.Random rnd)
        {
            int n = (int)(duration * rate); var data = new float[n];
            bool loop = key.EndsWith("-loop") || key.StartsWith("music");   // loops wrap seamlessly, so they get no fade
            for (int i = 0; i < n; i++) { float t = (float)i / rate; data[i] = Mathf.Clamp(f(t, rnd), -1, 1) * (loop ? 1 : Mathf.Min(1, Mathf.Min(t / .005f, (duration - t) / .02f))); }
            var clip = AudioClip.Create("synth-" + key, n, 1, rate, false); clip.SetData(data, 0); return clip;
        }
    }
}
