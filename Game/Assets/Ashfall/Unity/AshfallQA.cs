using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;

namespace Ashfall.View
{
    /// <summary>Native acceptance probe (-ashfall-qa=report.json with an isolated -ashfall-user-dir). Drives the
    /// built player through real input devices and records evidence: drop map, community, freaks, rival humans,
    /// airdrops, red zones, plague hearts, driving and save/reload. QA-only staging (spawned freaks, granted items,
    /// teleports, kept-alive health) happens in the isolated QA save and is listed in the report. With
    /// -ashfall-qa-video=dir (under a GameWorkbench folder) staged scenes are also recorded frame by frame.</summary>
    public sealed class AshfallQA : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name; public bool pass; public string detail; }
        [Serializable] sealed class Report
        {
            public int schemaVersion = 1; public string status = "running", game = "ashfall-county", build = "", platform = "", unity = "", started, finished, failure = "";
            public int seed, mapSize, buildings, settlements, zombies, purchasedArtRoles, purchasedAudioCues; public long generationMs;
            public float averageFrameMs, worstFrameMs, simMs, viewMs, uiMs; public int activeZombies, loadedChunks, loadedBuildings;
            public List<Check> checks = new List<Check>(); public List<string> screenshots = new List<string>(); public List<string> qaSetup = new List<string>();
            public List<string> landmarks = new List<string>(), videos = new List<string>(); public int landingZones, hearts, humans, factions, videoFrames;
        }
        AshfallGame game; string reportPath, dir; Report report = new Report();
        Keyboard keyboard; Gamepad pad;

        public void Initialize(AshfallGame game, string reportPath)
        {
            this.game = game; this.reportPath = reportPath; dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            Directory.CreateDirectory(dir);
            Application.runInBackground = true;
            // The probe's virtual keyboard and gamepad must keep working if the window loses focus (the default resets and disables them).
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            report.started = DateTime.UtcNow.ToString("o"); report.platform = Application.platform.ToString(); report.unity = Application.unityVersion; report.build = Application.version;
            StartCoroutine(Run());
        }

        void Pass(string name, bool ok, string detail = "") { report.checks.Add(new Check { name = name, pass = ok, detail = detail }); Debug.Log("ASHFALL_QA " + (ok ? "PASS " : "FAIL ") + name + " " + detail); Save(); }
        void Save() { try { File.WriteAllText(reportPath, JsonUtility.ToJson(report, true)); } catch (Exception e) { Debug.LogError(e); } }
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(dir, name + ".png"); ScreenCapture.CaptureScreenshot(path); report.screenshots.Add(path);
            yield return new WaitForSecondsRealtime(.4f);
        }
        IEnumerator Press(Key key, float hold = .12f)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return new WaitForSecondsRealtime(hold);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
        }
        IEnumerator Hold(Key key, float seconds) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return new WaitForSecondsRealtime(seconds); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; }
        /// <summary>Right-stick direction that aims at a world point through the game's camera-relative mapping.</summary>
        Vector2 Stick(Vec2 at)
        {
            var s = game.Player; var d = new Vector3(at.x - s.pos.x, 0, at.y - s.pos.y).normalized;
            var fwd = game.Cam.transform.forward; fwd.y = 0; fwd.Normalize(); var right = game.Cam.transform.right; right.y = 0; right.Normalize();
            return new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, fwd));
        }
        IEnumerator Aim(Vec2 at, float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) { InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = Stick(at) }); yield return null; } }
        IEnumerator PadAttack(float seconds, Func<Vec2> at)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1, rightStick = Stick(at()) }); yield return new WaitForSecondsRealtime(.1f); InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = Stick(at()) }); yield return new WaitForSecondsRealtime(.05f); }
        }
        IEnumerator WaitFor(Func<bool> condition, float seconds) { float end = Time.realtimeSinceStartup + seconds; while (!condition() && Time.realtimeSinceStartup < end) yield return null; }

        // ---------------------------------------------------------------- event tap, staging and recording
        readonly List<SimEvent> seen = new List<SimEvent>();
        bool Saw(EventKind kind, string text = null) { return seen.Any(e => e.kind == kind && (text == null || e.text == text)); }
        int Count(EventKind kind, string text = null) { return seen.Count(e => e.kind == kind && (text == null || e.text == text)); }
        /// <summary>Keeps the controlled survivor alive through a staged scene (listed as QA setup).</summary>
        void KeepAlive(Survivor s) { if (s != null && s.alive) { s.health = s.MaxHealth; s.injury = 0; s.healthSeen = -1; s.wounds.Clear(); s.knockedDown = 0; s.plague = 0; s.plagueInfected = false; s.endurance = 100; s.fatigue = 0; } }
        void ClearAround(World w, Vec2 at, float radius) { foreach (var z in w.zombies) if (z.Alive && Vec2.Distance(z.pos, at) < radius) { z.state = ZombieState.Dead; } w.index = new ZombieIndex(w); }
        Zombie Stage(World w, Vec2 at, ZombieKind kind, bool hold = true)
        {
            var z = w.SpawnKind(at, kind, w.rng); z.state = ZombieState.Idle; z.timer = hold ? 99 : 2; z.facing = (game.Player.pos - at).Angle; return z;
        }

        string videoDir; int videoFrames;
        /// <summary>Records the real rendered frames of a staged scene at 30 fps (fixed capture time step) as JPEGs; the
        /// harness encodes them to MP4. Disabled unless -ashfall-qa-video=dir is given.</summary>
        IEnumerator Record(string scene, float seconds, Action each = null)
        {
            if (string.IsNullOrEmpty(videoDir))
            {
                float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) { if (each != null) each(); yield return null; }
                yield break;
            }
            string dir = Path.Combine(videoDir, scene); Directory.CreateDirectory(dir);
            int frames = (int)(seconds * 30); Time.captureFramerate = 30;
            for (int i = 0; i < frames; i++)
            {
                if (each != null) each();
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, "f" + i.ToString("00000") + ".jpg"), tex.EncodeToJPG(88));
                Destroy(tex); videoFrames++;
            }
            Time.captureFramerate = 0;
            report.videos.Add(scene);
        }

        IEnumerator Run()
        {
            keyboard = InputSystem.AddDevice<Keyboard>("AshfallQA Keyboard"); pad = InputSystem.AddDevice<Gamepad>("AshfallQA Gamepad");
            videoDir = AshfallGame.Arg("ashfall-qa-video");
            if (!string.IsNullOrEmpty(videoDir)) { videoDir = Path.GetFullPath(videoDir); if (videoDir.IndexOf("GameWorkbench", StringComparison.OrdinalIgnoreCase) < 0) videoDir = null; }
            yield return new WaitForSecondsRealtime(1.5f);
            Pass("title-menu-visible", game.Screen == Screen.Title, "screen=" + game.Screen);
            yield return Shot("01-title");
            int size = 4096; int.TryParse(AshfallGame.Arg("ashfall-qa-size"), out size); if (size < 512) size = 4096;
            report.seed = 777; report.mapSize = size;

            // New community: traits on the leader through the picker (within budget, over-budget refused, Back closes).
            game.Show(Screen.NewGame); yield return new WaitForSecondsRealtime(.6f);
            yield return Shot("02-new-community");
            game.UI.QaOpenTraits(); yield return new WaitForSecondsRealtime(.4f);
            bool pickerOpen = game.UI.TraitsOpen;
            game.UI.QaClearTraits();
            foreach (var id in new[] { "strong", "smoker" }) game.UI.QaClickTrait(id);
            var picked = game.UI.ChosenTraits;
            Pass("trait-picker", pickerOpen && picked.Contains("strong"), "picked " + string.Join(",", picked) + (game.UI.TraitWarning.Length > 0 ? "; note: " + game.UI.TraitWarning : ""));
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East)); yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return new WaitForSecondsRealtime(.3f);
            Pass("trait-picker-closes-with-back", !game.UI.TraitsOpen && game.Screen == Screen.NewGame, "screen=" + game.Screen);

            // PUBG-style drop map: the county is surveyed in the background and landing zones are offered.
            var settings = new SandboxSettings { seed = 777, mapSize = size, population = Population.Normal };
            game.OpenDropMap(settings);
            yield return WaitFor(() => game.Preview != null, 90);
            var preview = game.Preview;
            Pass("drop-map-landing-zones", game.Screen == Screen.DropMap && preview != null && preview.gen.landingZones.Count >= 8,
                preview == null ? "no preview" : preview.gen.landingZones.Count + " zones: " + string.Join(", ", preview.gen.landingZones.Select(z => z.name)));
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("03-drop-map");
            int zone = preview == null ? -1 : Math.Max(0, preview.gen.landingZones.FindIndex(z => z.name == "Cedar Fork"));
            settings.landingZone = zone;
            var watch = Stopwatch.StartNew();
            game.StartNewGame(settings, "QA Leader", "deputy", true, picked.ToArray());
            yield return WaitFor(() => game.Screen == Screen.Playing, 180);
            report.generationMs = watch.ElapsedMilliseconds;
            Pass("new-game-generates-county", game.Screen == Screen.Playing && game.World != null, "ms=" + report.generationMs + " landing zone " + zone);
            if (game.World == null) { Finish("county did not load"); yield break; }
            var w = game.World; var sim = game.Sim; var s = game.Player;
            game.EventTap = e => { if (seen.Count < 20000) seen.Add(e); };
            report.buildings = w.map.buildings.Count; report.settlements = w.gen.settlements.Count; report.zombies = w.LivingZombies;
            report.purchasedArtRoles = ArtLibrary.PurchasedRoles; report.purchasedAudioCues = game.Audio.PurchasedCues;
            report.landmarks = w.gen.landmarks.Where(l => l.kind != LandmarkKind.Town).Select(l => l.kind + ":" + l.name).ToList();
            report.landingZones = w.gen.landingZones.Count; report.hearts = w.hearts.Count; report.humans = w.survivors.Count(x => x.faction != 0); report.factions = w.factions.Count;
            Pass("county-scale", w.map.width >= size && w.map.buildings.Count > size / 2, w.map.width + " m, " + w.map.buildings.Count + " buildings, " + w.gen.settlements.Count + " towns, " + w.LivingZombies + " zombies");
            var kinds = new[] { LandmarkKind.MilitaryBase, LandmarkKind.Airfield, LandmarkKind.Hospital, LandmarkKind.RadioStation, LandmarkKind.Garage, LandmarkKind.RaiderCompound };
            Pass("landmarks", kinds.All(k => w.gen.landmarks.Any(l => l.kind == k)), string.Join(", ", report.landmarks.Take(14)));
            var c = w.community;
            Pass("community-founded", w.LivingMembers().Count >= 3 && c.baseBuilding >= 0 && c.Stock(Resource.Food) > 0 && c.members.Contains(w.playerId),
                w.LivingMembers().Count + " members (" + string.Join(", ", w.LivingMembers().Select(m => m.name)) + "), base " + c.baseBuilding + ", food " + c.Stock(Resource.Food) + ", influence " + c.influence);
            Pass("factions-and-ai-survivors", report.humans >= 30 && w.factions.Count >= 8 && Characters.All.All(d => w.survivors.Any(x => x.character == d.key)),
                report.humans + " AI survivors in " + w.factions.Count + " factions; characters " + string.Join(", ", Characters.All.Select(d => d.name + (w.survivors.Any(x => x.character == d.key && x.alive) ? "" : " (missing)"))));
            var kindCounts = Bestiary.All.Select(d => d.name + " " + w.zombies.Count(z => z.Alive && z.kind == d.kind)).ToList();
            Pass("bestiary-populated", Bestiary.All.All(d => w.zombies.Any(z => z.Alive && z.kind == d.kind)) && w.hearts.Count >= 6, string.Join(", ", kindCounts) + "; " + w.hearts.Count + " plague hearts");
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Shot("04-home-base");

            // Movement through the real keyboard binding.
            var before = s.pos;
            foreach (var key in new[] { Key.W, Key.D, Key.S, Key.A }) { yield return Hold(key, .7f); if (Vec2.Distance(before, s.pos) > 1) break; }
            Pass("keyboard-movement", Vec2.Distance(before, s.pos) > .8f, "moved " + Vec2.Distance(before, s.pos).ToString("0.00") + " m");

            // A base container opens with E.
            var home = w.map.buildings[c.baseBuilding];
            var box = w.map.containers.Where(k => k.prop >= 0 && home.rooms.Contains(w.map.props[k.prop].room)).Select(k => w.map.props[k.prop]).FirstOrDefault();
            if (box != null)
            {
                var spot = FreeBeside(w.map, box); if (spot.HasValue) { s.pos = spot.Value; report.qaSetup.Add("moved beside a container in the home base"); }
                yield return Aim(box.Center, .5f);
                yield return Press(Key.E); yield return new WaitForSecondsRealtime(.6f);
                Pass("interact-opens-container", w.map.containers[box.container].generated && game.UI.BlocksGameplay, "focus=" + (game.Focus == null ? "none" : game.Focus.label));
                yield return Shot("05-loot-panel");
                game.UI.CloseTopPanel(); yield return new WaitForSecondsRealtime(.3f);
            }
            else Pass("interact-opens-container", false, "no container in the base");

            // Rucksacks: carried home and deposited through the self actions (E on "Yourself" / Q menu).
            float food = c.Stock(Resource.Food);
            s.inventory.Add(ItemCatalog.Make("rucksack_food")); report.qaSetup.Add("granted one food rucksack at base");
            s.pos = c.basePos; s.pos = OpenGround(w, c.basePos); if (!sim.AtBase(s)) s.pos = c.basePos;
            yield return new WaitForSecondsRealtime(.5f);
            var self = game.Nearby.FirstOrDefault(n => n.actions.Any(a => a.label.StartsWith("Deposit")));
            var deposit = self == null ? null : self.actions.First(a => a.label.StartsWith("Deposit"));
            if (deposit != null) deposit.run();
            yield return WaitFor(() => c.Stock(Resource.Food) > food, 3);
            Pass("rucksack-deposit", c.Stock(Resource.Food) > food && Saw(EventKind.Influence), "food " + food + " -> " + c.Stock(Resource.Food) + (deposit == null ? " (no deposit action offered)" : ""));

            // HUD, community panel (K), base tab, missions (J), map (M).
            yield return Shot("06-hud");
            yield return Press(Key.K); yield return new WaitForSecondsRealtime(.8f);
            Pass("community-panel", game.UI.TopPanel == "community", "top=" + game.UI.TopPanel);
            yield return Shot("07-community-roster");
            game.UI.OpenCommunityTab("base"); yield return new WaitForSecondsRealtime(.6f);
            yield return Shot("08-community-base");
            game.UI.CloseTopPanel(); yield return new WaitForSecondsRealtime(.2f);
            sim.OfferMission(MissionKind.Rescue); sim.OfferMission(MissionKind.ClearInfestation); report.qaSetup.Add("asked the community for a rescue and a clear-infestation mission");
            yield return Press(Key.J); yield return new WaitForSecondsRealtime(.8f);
            Pass("missions-panel", game.UI.TopPanel == "missions" && c.missions.Count > 0, c.missions.Count + " missions: " + string.Join(" | ", c.missions.Select(m => m.title)));
            yield return Shot("09-missions");
            game.UI.CloseTopPanel();
            yield return Press(Key.M); yield return new WaitForSecondsRealtime(2f);
            Pass("map-panel", game.UI.TopPanel == "map", "");
            yield return Shot("10-county-map");
            game.UI.CloseTopPanel(); yield return new WaitForSecondsRealtime(.3f);

            // Companion: bring a member along and walk; they follow.
            var mate = w.LivingMembers().FirstOrDefault(m => m.id != w.playerId);
            if (mate != null)
            {
                game.Submit(Command.Of(CommandKind.SetCompanion, -1, mate.id)); yield return new WaitForSecondsRealtime(.3f);
                var start = s.pos; yield return Hold(Key.D, 2.5f); yield return Hold(Key.W, 1.5f); yield return new WaitForSecondsRealtime(2.5f);
                Pass("companion-follows", mate.ai == AiRole.Companion && c.companion == mate.id && Vec2.Distance(mate.pos, s.pos) < 6, mate.name + " " + Vec2.Distance(mate.pos, s.pos).ToString("0.0") + " m behind after walking " + Vec2.Distance(start, s.pos).ToString("0.0") + " m");
                // Switch control to another member at base and back again.
                s.pos = OpenGround(w, c.basePos); var other = w.LivingMembers().FirstOrDefault(m => m.id != w.playerId && m.id != mate.id) ?? mate;
                other.pos = OpenGround(w, c.basePos + new Vec2(2, 0)); report.qaSetup.Add("placed the leader and a resident at the base for a switch");
                string why; bool can = sim.CanSwitchTo(other.id, out why);
                int leader = w.playerId; game.Submit(Command.Of(CommandKind.SwitchCharacter, -1, other.id)); yield return new WaitForSecondsRealtime(1.2f);
                Pass("switch-member", w.playerId == other.id && game.Player == other && other.ai == AiRole.None, (can ? "" : "refused: " + why + "; ") + "now playing " + game.Player.name);
                yield return Shot("11-switched-member");
                game.Submit(Command.Of(CommandKind.SwitchCharacter, -1, leader)); yield return new WaitForSecondsRealtime(1f);
                s = game.Player;
            }
            else Pass("companion-follows", false, "no second member");

            // Melee with the gamepad against a walker.
            KeepAlive(s); s.pos = OpenGround(w, c.basePos + new Vec2(30, 30)); ClearAround(w, s.pos, 30);
            var walker = Stage(w, s.pos + new Vec2(1.3f, 0), ZombieKind.Walker); report.qaSetup.Add("spawned a walker beside the leader");
            int kills = s.kills;
            yield return PadAttack(6, () => walker.pos);
            Pass("gamepad-melee-kills-zombie", !walker.Alive && s.kills > kills, "kills=" + s.kills + " hp=" + walker.hp.ToString("0.00"));

            // Freaks: a screamer screams; a juggernaut is drawn huge; a gamepad-thrown grenade hits the pack.
            KeepAlive(s); s.inventory.Add(ItemCatalog.Make("grenade", 2)); s.inventory.Add(ItemCatalog.Make("assault_rifle")); s.inventory.Add(ItemCatalog.Make("ammo_556", 60));
            report.qaSetup.Add("granted 2 grenades and an assault rifle; staged a screamer, bloater, armored, feral and juggernaut near the leader with the companion");
            var arena = s.pos; ClearAround(w, arena, 40);
            var scream = Stage(w, arena + new Vec2(7, 2), ZombieKind.Screamer, false);
            var jug = Stage(w, arena + new Vec2(12, -3), ZombieKind.Juggernaut);
            Stage(w, arena + new Vec2(10, 3), ZombieKind.Bloater); Stage(w, arena + new Vec2(11, -6), ZombieKind.Armored); Stage(w, arena + new Vec2(13, 0), ZombieKind.Walker); Stage(w, arena + new Vec2(14, 2), ZombieKind.Walker);
            yield return Aim(scream.pos, .8f);
            yield return WaitFor(() => Saw(EventKind.Scream), 8);
            Pass("screamer-screams", Saw(EventKind.Scream), "scream events " + Count(EventKind.Scream));
            yield return Shot("12-freaks");
            float jugScale = game.DrawnScale(ZombieKind.Juggernaut);
            Pass("juggernaut-drawn-large", jugScale > 1.3f, "drawn size " + jugScale.ToString("0.00"));
            float jugHp = jug.hp;
            yield return Aim(jug.pos, .4f);
            InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = Stick(jug.pos) }.WithButton(GamepadButton.RightShoulder));
            yield return Record("freaks-grenade", 1.2f, () => KeepAlive(s));
            InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = Stick(jug.pos) });
            yield return Record("freaks-grenade-2", 6f, () => KeepAlive(s));
            Pass("gamepad-throws-grenade", Saw(EventKind.Explosion, "grenade") && (jug.hp < jugHp || !jug.Alive), "explosions " + Count(EventKind.Explosion) + ", juggernaut hp " + jugHp.ToString("0.0") + " -> " + jug.hp.ToString("0.0"));
            yield return Shot("13-after-grenade");
            // Finish the fight with the rifle (equip, aim, shoot) and record it.
            var rifle = s.inventory.items.FirstOrDefault(i => i.def == "assault_rifle");
            if (rifle != null) { game.Submit(new Command { kind = CommandKind.Equip, item = rifle.uid, slot = 0 }); rifle.loaded = rifle.Def.magazine; }
            yield return new WaitForSecondsRealtime(.3f);
            int tracers = Count(EventKind.Tracer);
            Func<Vec2> nearest = () => { var t = w.zombies.Where(z => z.Alive && Vec2.Distance(z.pos, s.pos) < 25).OrderBy(z => Vec2.Distance(z.pos, s.pos)).FirstOrDefault(); return t == null ? s.pos + new Vec2(5, 0) : t.pos; };
            yield return Record("freaks-rifle", 6f, () => { KeepAlive(s); var at = nearest(); InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = Time.frameCount % 4 < 2 ? 1 : 0, leftTrigger = 1, rightStick = Stick(at) }); });
            InputSystem.QueueStateEvent(pad, new GamepadState());
            Pass("rifle-fires-tracers", Count(EventKind.Tracer) > tracers, (Count(EventKind.Tracer) - tracers) + " tracers");
            yield return Shot("14-firefight");

            // Airdrop: scheduled now, its crate brought down beside the leader so the fly-over and smoke are on camera.
            KeepAlive(s); ClearAround(w, s.pos, 40);
            w.nextAirdropAt = w.minutes + 6; report.qaSetup.Add("scheduled the next airdrop for now and moved its landing point 14 m from the leader");
            yield return WaitFor(() => w.airdrops.Any(a => !a.landed), 5);
            var drop = w.airdrops.FirstOrDefault(a => !a.landed);
            if (drop != null) drop.pos = OpenGround(w, s.pos + new Vec2(14, 4));
            yield return Record("airdrop", 16f, () => KeepAlive(s));
            yield return WaitFor(() => drop != null && drop.landed, 25);
            bool rare = drop != null && drop.landed && drop.container >= 0 && w.map.containers[drop.container].inventory.items.Any(i => i.Def.tier >= 2);
            Pass("airdrop-lands", drop != null && drop.landed && Saw(EventKind.Airdrop, "plane") && rare, drop == null ? "no drop" : "landed=" + drop.landed + " at " + drop.pos + ", tier 2+ loot=" + rare);
            yield return Shot("15-airdrop-smoke");

            // Red zone: Guard artillery shells a circle next to the leader.
            KeepAlive(s);
            w.redZone = new RedZone { center = OpenGround(w, s.pos + new Vec2(32, 0)), radius = 22, warnAt = w.minutes, startsAt = w.minutes + 2, endsAt = w.minutes + 9, area = "the QA range" };
            report.qaSetup.Add("placed a red zone 32 m from the leader");
            yield return Record("red-zone", 14f, () => KeepAlive(s));
            Pass("red-zone-shells", Count(EventKind.Artillery, "shell") >= 3 && Saw(EventKind.Explosion, "artillery"), Count(EventKind.Artillery, "shell") + " shells");
            yield return Shot("16-red-zone");

            // Talk to an enclave: Ada Pike at the radio station.
            var ada = w.survivors.FirstOrDefault(x => x.character == "ada_pike" && x.alive) ?? w.survivors.FirstOrDefault(x => x.alive && x.ai == AiRole.Enclave);
            if (ada != null)
            {
                s.pos = Beside(w, ada.pos); ClearAround(w, s.pos, 30); report.qaSetup.Add("moved the leader beside " + ada.name + " (" + Vec2.Distance(s.pos, ada.pos).ToString("0.0") + " m)");
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Aim(ada.pos, .5f);
                var talk = game.Nearby.SelectMany(n => n.actions).FirstOrDefault(a => a.label == "Talk");
                if (talk != null) talk.run();
                yield return new WaitForSecondsRealtime(1f);
                Pass("talk-to-enclave", game.UI.TopPanel == "talk" && sim.Greeting(s, ada).Length > 0, ada.name + ": " + sim.Greeting(s, ada) + "; offers " + sim.TradeOffers(s, ada).Count);
                yield return Shot("17-talk-trade");
                game.UI.CloseTopPanel();
            }
            else Pass("talk-to-enclave", false, "no enclave survivor found");

            // Raiders: the Red Talons attack on sight.
            var talons = w.FactionByKey("red_talons");
            var raider = talons == null ? null : w.Members(talons.id).FirstOrDefault();
            if (raider != null)
            {
                KeepAlive(s); s.pos = OpenGround(w, raider.pos + new Vec2(10, 0)); ClearAround(w, s.pos, 30); report.qaSetup.Add("moved the leader about 10 m from a Red Talon raider; kept the leader alive while recording");
                int hostileTracers = seen.Count(e => e.kind == EventKind.Tracer && e.survivor >= 0 && w.survivors[e.survivor].faction == talons.id);
                yield return Record("raiders", 10f, () => KeepAlive(s));
                int after = seen.Count(e => e.kind == EventKind.Tracer && e.survivor >= 0 && w.survivors[e.survivor].faction == talons.id);
                bool attacked = after > hostileTracers || s.lastHitBy >= 0 && w.survivors[s.lastHitBy].faction == talons.id;
                Pass("raiders-attack-on-sight", attacked, "raider shots " + (after - hostileTracers) + ", last hit by " + (s.lastHitBy >= 0 ? w.survivors[s.lastHitBy].name : "nobody"));
                yield return Shot("18-raiders");
            }
            else Pass("raiders-attack-on-sight", false, "no Red Talon raider");

            // A plague heart: approach, see it, destroy it with grenades and melee.
            var heart = w.hearts.Where(h => !h.destroyed).OrderBy(h => Vec2.Distance(h.pos, c.basePos)).FirstOrDefault();
            if (heart != null)
            {
                KeepAlive(s); s.pos = OpenGround(w, heart.pos + new Vec2(2.4f, 0)); ClearAround(w, s.pos, 25);
                s.inventory.Add(ItemCatalog.Make("grenade", 3)); report.qaSetup.Add("moved beside the nearest plague heart, cleared its guards and granted 3 grenades");
                float influence = c.influence; int remaining = w.HeartsRemaining;
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot("19-plague-heart");
                yield return Record("plague-heart", 12f, () =>
                {
                    KeepAlive(s);
                    if (Time.frameCount % 45 == 0 && !heart.destroyed) { var g = s.inventory.items.FirstOrDefault(i => i.def == "grenade"); if (g != null) game.Submit(new Command { kind = CommandKind.Throw, item = g.uid, x = heart.pos.x, y = heart.pos.y, recipe = "" }); }
                });
                if (!heart.destroyed) yield return PadAttack(8, () => heart.pos);
                Pass("plague-heart-destroyed", heart.destroyed && w.HeartsRemaining == remaining - 1 && c.influence > influence, "hp " + heart.hp.ToString("0") + "/" + heart.maxHp + ", hearts left " + w.HeartsRemaining + ", influence " + influence + " -> " + c.influence);
            }
            else Pass("plague-heart-destroyed", false, "no heart");

            // Night: ferals in the dark; lighting drops.
            KeepAlive(s); s.pos = OpenGround(w, c.basePos + new Vec2(-30, 20)); ClearAround(w, s.pos, 40);
            float dayAmbient = RenderSettings.ambientLight.grayscale;
            w.minutes = Math.Floor(w.minutes / 1440) * 1440 + 23 * 60 + 1440; report.qaSetup.Add("set the clock to 23:00 and staged three ferals");
            for (int i = 0; i < 3; i++) Stage(w, s.pos + Vec2.FromAngle(i * 2.1f) * 9, ZombieKind.Feral, false);
            yield return Press(Key.F);
            yield return Record("night-ferals", 8f, () => KeepAlive(s));
            Pass("night-is-darker", RenderSettings.ambientLight.grayscale < dayAmbient * .6f, "ambient " + dayAmbient.ToString("0.00") + " -> " + RenderSettings.ambientLight.grayscale.ToString("0.00"));
            yield return Shot("20-night-ferals");
            yield return Press(Key.F);
            w.minutes = Math.Floor(w.minutes / 1440) * 1440 + 13 * 60 + 1440;

            // Frame timing in the county seat with its dead and the living.
            var town = w.gen.settlements[0];
            s.pos = OpenGround(w, new Vec2(town.x + 30, town.y + 30));
            report.qaSetup.Add("moved to Cedar Fork for the performance sample; kept the leader alive during it");
            float settle = Time.realtimeSinceStartup + 3; while (Time.realtimeSinceStartup < settle) { KeepAlive(s); yield return null; }
            var frames = new List<float>(); float until = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < until) { KeepAlive(s); frames.Add(Time.unscaledDeltaTime * 1000); yield return null; }
            report.averageFrameMs = frames.Average(); report.worstFrameMs = frames.Max(); report.activeZombies = game.Sim.ActiveZombies; report.loadedChunks = game.View.LoadedChunks; report.loadedBuildings = game.View.LoadedBuildings; report.simMs = game.SimMs; report.viewMs = game.ViewMs; report.uiMs = game.UiMs;
            Pass("frame-time-in-town", report.averageFrameMs < 50 && report.activeZombies > 30, "avg=" + report.averageFrameMs.ToString("0.0") + "ms worst=" + report.worstFrameMs.ToString("0.0") + " sim=" + report.simMs.ToString("0.0") + " view=" + report.viewMs.ToString("0.0") + " ui=" + report.uiMs.ToString("0.0") + " active zombies=" + report.activeZombies + " humans drawn=" + game.DrawnHumans);
            yield return Record("town", 6f, () => KeepAlive(s));
            yield return Shot("21-cedar-fork");

            // Drive a car with the keyboard.
            yield return Drive();
            yield return Shot("22-driving");

            // Save, quit to title, reload, compare community state.
            if (s.vehicle >= 0) { game.Submit(Command.Of(CommandKind.ExitVehicle)); yield return WaitFor(() => s.vehicle < 0, 3); }
            s = game.Player; KeepAlive(s);
            var savedPos = s.pos; int savedKills = s.kills, savedPlayer = w.playerId, savedMembers = w.LivingMembers().Count, savedHearts = w.HeartsRemaining, savedDrops = w.airdrops.Count;
            float savedFood = c.Stock(Resource.Food), savedInfluence = c.influence; int savedFactions = w.factions.Count;
            bool saved = game.SaveNow();
            game.QuitToTitle(false);
            yield return new WaitForSecondsRealtime(1);
            Pass("quit-to-title", game.Screen == Screen.Title, "");
            game.LoadGame(game.SaveFiles().FirstOrDefault());
            yield return WaitFor(() => game.Screen == Screen.Playing, 180);
            var l = game.World; var ls = game.Player;
            bool same = l != null && l.playerId == savedPlayer && Vec2.Distance(ls.pos, savedPos) < .01f && ls.kills == savedKills && l.LivingMembers().Count == savedMembers
                && l.HeartsRemaining == savedHearts && l.airdrops.Count == savedDrops && Math.Abs(l.community.Stock(Resource.Food) - savedFood) < .01f && Math.Abs(l.community.influence - savedInfluence) < .01f && l.factions.Count == savedFactions;
            Pass("save-and-reload", saved && same, l == null ? "no world" : "player " + l.playerId + "/" + savedPlayer + ", members " + l.LivingMembers().Count + "/" + savedMembers + ", hearts " + l.HeartsRemaining + "/" + savedHearts + ", drops " + l.airdrops.Count + "/" + savedDrops + ", food " + l.community.Stock(Resource.Food) + "/" + savedFood + ", influence " + l.community.influence + "/" + savedInfluence);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("23-reloaded");
            report.videoFrames = videoFrames;
            Finish("");
        }

        Vehicle qaCar; Vec2 qaCarPos; float qaCarFuel;
        IEnumerator Drive()
        {
            var w = game.World; var s = game.Player; var town = w.gen.settlements[0];
            Vec2 west; var v = DriveStrip(w, new Vec2(town.x, town.y), 90, out west);
            if (v == null) { Pass("vehicle-enter", false, "no open strip for a drive"); yield break; }
            foreach (var z in w.zombies.Where(z => z.Alive && Vec2.Distance(z.pos, west + new Vec2(45, 0)) < 110)) z.state = ZombieState.Dead;
            w.index = new ZombieIndex(w);
            report.qaSetup.Add("moved drivable car #" + v.id + " (" + v.model + ") to an open east-west strip at " + west + " with 30 L fuel and the key in the ignition; cleared zombies within 110 m");
            s.pos = v.pos + v.Right * -(Vehicle.Width / 2 + .5f); s.health = 100; s.wounds.Clear(); s.knockedDown = 0; s.action = null;
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Aim(v.pos, .4f);
            yield return Press(Key.E); yield return new WaitForSecondsRealtime(.5f);
            Pass("vehicle-enter", s.vehicle == v.id && v.driver == s.id, "pressed E beside the " + v.model + "; seated=" + (s.vehicle == v.id));
            if (s.vehicle != v.id) yield break;
            yield return Press(Key.E); yield return WaitFor(() => v.engineOn, 3);
            Pass("vehicle-engine-start", v.engineOn, "E with the key in the ignition; fuel " + v.fuel.ToString("0.0") + " L");
            float fuel = v.fuel, zoomBefore = game.Cam.orthographicSize; var start = v.pos;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return new WaitForSecondsRealtime(3);
            float driven = Vec2.Distance(start, v.pos), speed = v.speed;
            var view = game.View.Vehicles.ViewOf(v.id);
            float viewErr = view == null ? 99 : Vector2.Distance(new Vector2(view.position.x, view.position.z), new Vector2(v.pos.x, v.pos.y));
            float yawErr = view == null ? 999 : Mathf.Abs(Mathf.DeltaAngle(view.eulerAngles.y, AshfallGame.Yaw(v.heading)));
            Pass("vehicle-drives-with-keyboard", driven > 8 && speed > 4, "held W 3 s: " + driven.ToString("0.0") + " m, " + (speed * 3.6f).ToString("0") + " km/h");
            Pass("vehicle-model-follows", viewErr < 1.5f && yawErr < 12, "model offset " + viewErr.ToString("0.00") + " m, yaw error " + yawErr.ToString("0.0") + " deg");
            Pass("driving-camera-zooms-out", game.Cam.orthographicSize > zoomBefore + 2, "ortho size " + zoomBefore.ToString("0.0") + " -> " + game.Cam.orthographicSize.ToString("0.0"));
            Pass("engine-audio", game.Audio.EngineVolume > .05f, "engine loop volume " + game.Audio.EngineVolume.ToString("0.00") + " pitch " + game.Audio.EnginePitch.ToString("0.00"));
            yield return Shot("12-driving");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            yield return Hold(Key.Space, 2);
            Pass("vehicle-brakes", Mathf.Abs(v.speed) < .2f, "Space 2 s: " + (v.speed * 3.6f).ToString("0.0") + " km/h");
            // A zombie in the lane is knocked down (or killed) and the car takes damage.
            var z0 = w.AddZombie(v.pos + v.Forward * 11, new Rng(11, 11)); z0.state = ZombieState.Idle; z0.timer = 99; z0.sight = 0; z0.hearing = 0; z0.hp = 1.3f; z0.facing = v.heading + 3.14f;
            report.qaSetup.Add("spawned 1 deaf, blind zombie 11 m ahead of the car");
            float cond = v.condition; int kills = s.kills;
            yield return Hold(Key.W, 2.5f);
            Pass("car-knocks-down-zombie", !z0.Alive || z0.state == ZombieState.Knocked, "zombie " + (z0.Alive ? z0.state + " hp " + z0.hp.ToString("0.00") : "killed") + ", kills " + kills + " -> " + s.kills + ", condition " + cond.ToString("0.00") + " -> " + v.condition.ToString("0.00"));
            yield return Shot("13-hit");
            yield return Hold(Key.Space, 2.5f);
            float heading = v.heading;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D)); yield return new WaitForSecondsRealtime(1.2f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Pass("vehicle-steers", Mathf.Abs(Mathf.DeltaAngle(heading * Mathf.Rad2Deg, v.heading * Mathf.Rad2Deg)) > 10, "W+D 1.2 s turned " + Mathf.DeltaAngle(heading * Mathf.Rad2Deg, v.heading * Mathf.Rad2Deg).ToString("0") + " deg");
            yield return Hold(Key.Space, 2.5f);
            Pass("driving-uses-fuel", v.fuel < fuel, fuel.ToString("0.00") + " -> " + v.fuel.ToString("0.00") + " L");
            yield return Press(Key.E); yield return WaitFor(() => s.vehicle < 0, 2);
            Pass("vehicle-exit-parks", s.vehicle < 0 && !v.engineOn && !w.map.Collides(s.pos, Survivor.Radius) && w.map.PropAt((int)v.pos.x, (int)v.pos.y) == w.map.props[v.prop], "survivor at " + s.pos + ", car parked at " + v.pos);
            yield return new WaitForSecondsRealtime(.6f);
            yield return Shot("14-parked");
            qaCar = v; qaCarPos = v.pos; qaCarFuel = v.fuel;
        }

        /// <summary>A drivable car moved to the west end of a straight open strip near a point.</summary>
        static Vehicle DriveStrip(World w, Vec2 near, int length, out Vec2 west)
        {
            var m = w.map; west = near;
            for (int dy = 0; dy < 700; dy += 3)
                foreach (int sign in new[] { 1, -1 })
                {
                    int y = (int)near.y + dy * sign; if (y < 10 || y > m.height - 10) continue;
                    for (int x = Math.Max(10, (int)near.x - 500); x < Math.Min(m.width - length - 10, (int)near.x + 500); x += 5)
                    {
                        bool clear = true;
                        for (int i = -3; i <= length && clear; i++)
                            for (int j = -4; j <= 4 && clear; j++)
                            {
                                int tx = x + i, ty = y + j;
                                if (m.TileBlocked(tx, ty) || m.RoomAt(tx, ty) != null || m.H(tx, ty) != Edge.None || m.V(tx, ty) != Edge.None || m.H(tx, ty + 1) != Edge.None || m.V(tx + 1, ty) != Edge.None) clear = false;
                            }
                        if (!clear) continue;
                        west = new Vec2(x + .5f, y + .5f);
                        var v = w.vehicles.Where(c => c.driver < 0 && !c.Wrecked).OrderBy(c => Vec2.Distance(c.pos, near)).FirstOrDefault(); if (v == null) return null;
                        w.UnblockVehicle(v); v.pos = west + new Vec2(2.5f, 0); v.heading = 0; v.speed = 0; v.fuel = 30; v.condition = Math.Max(v.condition, .8f); v.keyInIgnition = true; w.BlockVehicle(v);
                        return v;
                    }
                }
            return null;
        }

        static Vec2? FreeBeside(Map map, Prop p)
        {
            foreach (var d in new[] { new Int2(0, -1), new Int2(0, 1), new Int2(-1, 0), new Int2(1, 0) })
            {
                var c = new Vec2(p.x + .5f + d.x, p.y + .5f + d.y);
                Opening o; var e = map.Between(p.x, p.y, p.x + d.x, p.y + d.y, out o);
                if (e == Edge.None && !map.Collides(c, Survivor.Radius)) return c;
            }
            return null;
        }
        /// <summary>A free standing spot within reach of a person (indoors allowed), nearest first.</summary>
        static Vec2 Beside(World w, Vec2 p)
        {
            for (float r = .9f; r < 2.4f; r += .3f)
                for (int a = 0; a < 12; a++)
                {
                    var c = p + Vec2.FromAngle(a * .5236f) * r;
                    if (!w.map.Collides(c, Survivor.Radius) && w.map.LineOfSight(c, p)) return c;
                }
            return OpenGround(w, p);
        }
        static Vec2 OpenGround(World w, Vec2 near)
        {
            for (int r = 0; r < 200; r += 2)
                for (int a = 0; a < 16; a++)
                {
                    var p = near + Vec2.FromAngle(a * .3927f) * r; int x = (int)p.x, y = (int)p.y; bool ok = true;
                    for (int dx = -4; dx <= 4 && ok; dx++) for (int dy = -4; dy <= 4 && ok; dy++) if (w.map.TileBlocked(x + dx, y + dy) || w.map.RoomAt(x + dx, y + dy) != null || w.map.H(x + dx, y + dy) != Edge.None || w.map.V(x + dx, y + dy) != Edge.None) ok = false;
                    if (ok) return new Vec2(x + .5f, y + .5f);
                }
            return near;
        }

        void Finish(string failure)
        {
            Time.captureFramerate = 0;
            report.failure = failure; report.finished = DateTime.UtcNow.ToString("o");
            report.status = failure.Length == 0 && report.checks.All(c => c.pass) ? "passed" : "failed";
            Save(); Debug.Log("ASHFALL_QA_DONE " + report.status + " " + reportPath);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (pad != null) InputSystem.RemoveDevice(pad);
            Application.Quit(report.status == "passed" ? 0 : 1);
        }
    }
}
