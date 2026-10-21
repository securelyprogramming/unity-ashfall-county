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
    /// built player through real input devices and records evidence. QA-only setup (spawned zombies, granted tools)
    /// happens in the isolated QA save and is listed in the report.</summary>
    public sealed class AshfallQA : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name; public bool pass; public string detail; }
        [Serializable] sealed class Report
        {
            public int schemaVersion = 1; public string status = "running", game = "ashfall-county", build = "", platform = "", unity = "", started, finished, failure = "";
            public int seed, mapSize, buildings, settlements, zombies, purchasedArtRoles, purchasedAudioCues; public long generationMs;
            public float averageFrameMs, worstFrameMs, simMs, viewMs, uiMs; public int activeZombies, loadedChunks, loadedBuildings;
            public List<Check> checks = new List<Check>(); public List<string> screenshots = new List<string>(); public List<string> qaSetup = new List<string>();
        }
        AshfallGame game; string reportPath, dir; Report report = new Report();
        Keyboard keyboard; Gamepad pad;

        public void Initialize(AshfallGame game, string reportPath)
        {
            this.game = game; this.reportPath = reportPath; dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            Directory.CreateDirectory(dir);
            Application.runInBackground = true;
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

        IEnumerator Run()
        {
            keyboard = InputSystem.AddDevice<Keyboard>("AshfallQA Keyboard"); pad = InputSystem.AddDevice<Gamepad>("AshfallQA Gamepad");
            yield return new WaitForSecondsRealtime(1.5f);
            Pass("title-menu-visible", game.Screen == Screen.Title, "screen=" + game.Screen);
            yield return Shot("01-title");
            int size = 3072; int.TryParse(AshfallGame.Arg("ashfall-qa-size"), out size); if (size < 512) size = 3072;
            report.seed = 777; report.mapSize = size;
            // Traits on the New Game screen: open the picker, pick within budget, try an over-budget pick, close with the pad's Back.
            game.Show(Screen.NewGame); yield return new WaitForSecondsRealtime(.6f);
            game.UI.QaOpenTraits(); yield return new WaitForSecondsRealtime(.4f);
            bool pickerOpen = game.UI.TraitsOpen;
            game.UI.QaClearTraits();
            foreach (var id in new[] { "strong", "smoker", "green_thumb" }) game.UI.QaClickTrait(id);
            game.UI.QaClickTrait("eagle_eyed");   // costs 3 with 0 left: must be refused
            string refused = game.UI.TraitWarning; var picked = game.UI.ChosenTraits;
            yield return Shot("00-traits");
            Pass("trait-picker", pickerOpen && picked.SequenceEqual(new[] { "strong", "smoker", "green_thumb" }) && refused.Length > 0, "picked " + string.Join(",", picked) + "; over-budget pick refused: " + refused);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East)); yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return new WaitForSecondsRealtime(.3f);
            Pass("trait-picker-closes-with-back", !game.UI.TraitsOpen && game.Screen == Screen.NewGame, "screen=" + game.Screen);
            var watch = Stopwatch.StartNew();
            game.StartNewGame(new SandboxSettings { seed = 777, mapSize = size, population = Population.Normal }, "QA Survivor", "carpenter", true, picked.ToArray());
            yield return WaitFor(() => game.Screen == Screen.Playing, 120);
            report.generationMs = watch.ElapsedMilliseconds;
            Pass("new-game-generates-county", game.Screen == Screen.Playing && game.World != null, "ms=" + report.generationMs);
            if (game.World == null) { Finish("county did not load"); yield break; }
            var w = game.World; var s = game.Player;
            report.buildings = w.map.buildings.Count; report.settlements = w.gen.settlements.Count; report.zombies = w.LivingZombies;
            report.purchasedArtRoles = ArtLibrary.PurchasedRoles; report.purchasedAudioCues = game.Audio.PurchasedCues;
            Pass("county-scale", w.map.width >= size && w.map.buildings.Count > (size >= 3072 ? 800 : 100), w.map.width + "m, " + w.map.buildings.Count + " buildings, " + w.gen.settlements.Count + " settlements, " + w.LivingZombies + " zombies");
            yield return new WaitForSecondsRealtime(2);
            yield return Shot("02-start-house");

            // Movement through the real keyboard binding.
            var before = s.pos;
            foreach (var key in new[] { Key.W, Key.D, Key.S, Key.A }) { yield return Hold(key, .7f); if (Vec2.Distance(before, s.pos) > 1) break; }
            Pass("keyboard-movement", Vec2.Distance(before, s.pos) > .8f, "moved " + Vec2.Distance(before, s.pos).ToString("0.00") + " m");

            // Loot the nearest container with E (walk next to it first, QA teleport within the house).
            var home = w.map.BuildingAt(s.pos);
            var box = home == null ? null : w.map.containers.Where(c => c.prop >= 0 && home.rooms.Contains(w.map.props[c.prop].room)).Select(c => w.map.props[c.prop]).FirstOrDefault();
            if (box != null)
            {
                var spot = FreeBeside(w.map, box); if (spot.HasValue) { s.pos = spot.Value; report.qaSetup.Add("teleported beside a container in the start house"); }
                yield return Aim(box.Center, .5f);
                yield return Press(Key.E);
                yield return new WaitForSecondsRealtime(.6f);
                var c = w.map.containers[box.container];
                Pass("interact-opens-container", c.generated && game.UI.BlocksGameplay, "items=" + c.inventory.items.Count + " focus=" + (game.Focus == null ? "none" : game.Focus.label));
                yield return Shot("03-loot-panel");
                yield return Press(Key.Tab); yield return new WaitForSecondsRealtime(.3f);
                if (game.UI.BlocksGameplay) game.UI.CloseTopPanel();
            }
            else Pass("interact-opens-container", false, "no container in start house");

            // Eat and drink through commands the UI uses.
            s.hunger = 50; s.thirst = 50;
            var chips = s.inventory.Find("chips"); if (chips != null) game.Submit(new Command { kind = CommandKind.Use, item = chips.uid });
            yield return WaitFor(() => s.hunger < 45, 8);
            Pass("eat", s.hunger < 45, "hunger=" + s.hunger.ToString("0"));
            var bottle = s.inventory.Find("water_bottle"); if (bottle != null) game.Submit(new Command { kind = CommandKind.Use, item = bottle.uid });
            yield return WaitFor(() => s.thirst < 30, 8);
            Pass("drink", s.thirst < 30, "thirst=" + s.thirst.ToString("0"));

            // Melee with the controller trigger: QA grants a bat and spawns one zombie ahead (isolated QA save).
            var arena = OpenGround(w, s.pos);
            s.pos = arena; report.qaSetup.Add("moved to open ground at " + arena);
            foreach (var z in w.zombies.Where(z => z.Alive && Vec2.Distance(z.pos, s.pos) < 40)) z.state = ZombieState.Dead;
            w.index = new ZombieIndex(w);
            var bat = ItemCatalog.Make("baseball_bat"); s.inventory.Add(bat); s.primary = bat.uid; report.qaSetup.Add("granted baseball bat");
            yield return new WaitForSecondsRealtime(1);
            var target = w.AddZombie(s.pos + new Vec2(1.2f, 0), new Rng(5, 5)); target.state = ZombieState.Idle; target.timer = 99; target.facing = 3.14f; target.hp = 1.3f; report.qaSetup.Add("spawned 1 zombie in front");
            int kills = s.kills;
            for (int i = 0; i < 12 && target.Alive; i++) yield return PadAttack(.5f, () => target.pos);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            Pass("gamepad-melee-kills-zombie", !target.Alive && s.kills > kills, "kills=" + s.kills + " zombieHp=" + target.hp.ToString("0.00"));
            yield return Shot("04-combat");

            // A zombie attacks; the survivor is wounded, then bandages.
            foreach (var kv in s.worn.ToList()) s.worn.Remove(kv.Key);
            var attacker = w.AddZombie(s.pos + new Vec2(-4f, 0), new Rng(9, 9)); attacker.state = ZombieState.Idle; attacker.facing = 0; report.qaSetup.Add("removed clothing protection; spawned attacker behind");
            yield return WaitFor(() => s.wounds.Count > 0 || !s.alive, 30);
            Pass("zombie-perceives-and-wounds", s.wounds.Count > 0, "wounds=" + string.Join("; ", s.wounds.Select(x => x.Label)) + " zombieState=" + attacker.state);
            yield return Shot("05-wounded");
            attacker.state = ZombieState.Dead; w.index = new ZombieIndex(w);
            s.inventory.Add(ItemCatalog.Make("bandage", 3)); report.qaSetup.Add("granted bandages");
            var bleeding = s.wounds.FirstOrDefault(x => x.bleeding);
            var band = s.inventory.Find("bandage"); s.knockedDown = 0; game.Submit(new Command { kind = CommandKind.Use, item = band.uid });
            yield return WaitFor(() => bleeding == null || bleeding.bandaged, 10);
            s.health = 100; s.wounds.Clear(); s.zombieInfected = false; report.qaSetup.Add("after the wound test: health restored and infection cleared so later checks run on a live survivor");
            Pass("bandage-wound", bleeding == null || bleeding.bandaged && !bleeding.bleeding, bleeding == null ? "no bleeding wound" : bleeding.Label);

            // Barricade a window of a nearby building.
            var window = w.map.openings.Where(o => o.kind == Edge.Window && o.barricade == 0).OrderBy(o => Vec2.Distance(o.Center, s.pos)).First();
            s.pos = window.Center + (window.vertical ? new Vec2(-.55f, 0) : new Vec2(0, -.55f));
            if (w.map.Collides(s.pos, Survivor.Radius)) s.pos = window.Center + (window.vertical ? new Vec2(.55f, 0) : new Vec2(0, .55f));
            s.inventory.Add(ItemCatalog.Make("hammer")); s.inventory.Add(ItemCatalog.Make("plank")); s.inventory.Add(ItemCatalog.Make("nails", 10)); report.qaSetup.Add("granted hammer, plank, nails; moved to a window");
            game.Submit(new Command { kind = CommandKind.Barricade, target = window.id });
            yield return WaitFor(() => window.barricade > 0, 10);
            Pass("barricade-window", window.barricade == 1, "planks=" + window.barricade);
            yield return new WaitForSecondsRealtime(.5f);
            yield return Shot("06-barricade");

            // Night lighting and the map.
            w.minutes = Math.Floor(w.minutes / 1440) * 1440 + 23 * 60;
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("07-night");
            yield return Press(Key.F); yield return new WaitForSecondsRealtime(.6f);
            yield return Shot("08-night-flashlight");
            yield return Press(Key.M); yield return new WaitForSecondsRealtime(2.5f);
            Pass("map-panel", game.UI.BlocksGameplay, "");
            yield return Shot("09-map");
            game.UI.CloseTopPanel();
            w.minutes = Math.Floor(w.minutes / 1440) * 1440 + 13 * 60 + 1440;

            // Town traversal and frame timing near a busy settlement.
            var town = w.gen.settlements[0];
            s.pos = OpenGround(w, new Vec2(town.x + 30, town.y + 30));
            report.qaSetup.Add("moved to Cedar Fork for performance capture; restored survivor health so the sample measures a live session");
            Pass("survivor-alive-before-town-sample", s.alive, "health=" + s.health.ToString("0") + (s.alive ? "" : " cause=" + s.causeOfDeath));
            s.health = 100; s.wounds.Clear(); s.knockedDown = 0;
            // The sample measures performance among active zombies; keep the survivor alive through it so a chance horde
            // death does not invalidate the later checks (recorded as QA setup).
            report.qaSetup.Add("kept the survivor's health, wounds, infection and knockdown reset during the 9 s town sample");
            Action keepAlive = () => { if (s.alive) { s.health = 100; s.wounds.Clear(); s.knockedDown = 0; s.zombieInfected = false; s.infectionHours = 0; } };
            float settle = Time.realtimeSinceStartup + 3;
            while (Time.realtimeSinceStartup < settle) { keepAlive(); yield return null; }
            var frames = new List<float>(); float until = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < until) { keepAlive(); frames.Add(Time.unscaledDeltaTime * 1000); yield return null; }
            keepAlive();
            report.averageFrameMs = frames.Average(); report.worstFrameMs = frames.Max(); report.activeZombies = game.Sim.ActiveZombies; report.loadedChunks = game.View.LoadedChunks; report.loadedBuildings = game.View.LoadedBuildings; report.simMs = game.SimMs; report.viewMs = game.ViewMs; report.uiMs = game.UiMs;
            Pass("frame-time-in-town", report.averageFrameMs < 50 && report.activeZombies > 50, "avg=" + report.averageFrameMs.ToString("0.0") + "ms worst=" + report.worstFrameMs.ToString("0.0") + " sim=" + report.simMs.ToString("0.0") + " view=" + report.viewMs.ToString("0.0") + " ui=" + report.uiMs.ToString("0.0") + " active zombies=" + report.activeZombies);
            yield return Shot("10-town");

            // Drive a car with the keyboard: enter, start, accelerate, brake, run down a zombie, steer, park.
            yield return Drive();

            // Long-term survival: traits, farming, fishing, skill books and smoking.
            yield return Survival();

            // Save, quit to title, reload, compare.
            if (qaPlot >= 0 && w.plots.ContainsKey(qaPlot)) qaPlotGrowth = w.plots[qaPlot].growth;   // the crop keeps growing until the save
            var savedPos = s.pos; int savedKills = s.kills; int savedBarricade = window.barricade; int savedZombies = w.LivingZombies;
            bool saved = game.SaveNow();
            game.QuitToTitle(false);
            yield return new WaitForSecondsRealtime(1);
            Pass("quit-to-title", game.Screen == Screen.Title, "");
            var latest = game.SaveFiles().FirstOrDefault();
            game.LoadGame(latest);
            yield return WaitFor(() => game.Screen == Screen.Playing, 120);
            var l = game.World; var ls = game.Player;
            bool same = l != null && Vec2.Distance(ls.pos, savedPos) < .01f && ls.kills == savedKills && l.map.openings[window.id].barricade == savedBarricade && Math.Abs(l.LivingZombies - savedZombies) <= 0;
            Pass("save-and-reload", saved && same, l == null ? "no world" : "pos=" + ls.pos + " kills=" + ls.kills + " zombies=" + l.LivingZombies + "/" + savedZombies);
            yield return new WaitForSecondsRealtime(1.5f);
            if (qaCar != null && l != null)
            {
                var lv = l.vehicles[qaCar.id];
                Pass("vehicle-save-and-reload", Vec2.Distance(lv.pos, qaCarPos) < .01f && Math.Abs(lv.fuel - qaCarFuel) < .01f && l.map.PropAt((int)lv.pos.x, (int)lv.pos.y) == l.map.props[lv.prop], "car at " + lv.pos + " fuel " + lv.fuel.ToString("0.00") + " L (saved " + qaCarPos + ", " + qaCarFuel.ToString("0.00") + " L)");
            }
            if (l != null)
            {
                FarmPlot lp; bool plot = qaPlot >= 0 && l.plots.TryGetValue(qaPlot, out lp) && lp.crop == qaPlotCrop && Mathf.Abs(lp.growth - qaPlotGrowth) < .001f && l.map.props[qaPlot].kind == PropKind.FarmPlot;
                Pass("survival-save-and-reload", plot && ls.traits.SequenceEqual(qaTraits) && ls.booksRead.SequenceEqual(qaBooks) && ls.Level(Skill.Fishing) == qaFishing,
                    "plot " + qaPlot + " " + (l.plots.ContainsKey(qaPlot) ? l.plots[qaPlot].Label : "missing") + "; traits " + string.Join(",", ls.traits) + "; books " + string.Join(",", ls.booksRead) + "; fishing " + ls.Level(Skill.Fishing));
            }
            yield return Shot("11-reloaded");
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

        int qaPlot = -1, qaFishing; string qaPlotCrop = ""; float qaPlotGrowth; List<string> qaTraits = new List<string>(), qaBooks = new List<string>();
        IEnumerator Survival()
        {
            var w = game.World; var s = game.Player; var sim = game.Sim;
            if (s.vehicle >= 0) { game.Submit(Command.Of(CommandKind.ExitVehicle)); yield return WaitFor(() => s.vehicle < 0, 3); }
            s.health = 100; s.wounds.Clear(); s.knockedDown = 0; s.action = null; s.sleeping = false;
            Pass("traits-applied", s.HasTrait("strong") && s.HasTrait("smoker") && s.HasTrait("green_thumb") && s.Level(Skill.Farming) == 1 && s.inventory.Count("cigarettes") > 0,
                "traits " + string.Join(",", s.traits) + ", Farming " + s.Level(Skill.Farming) + ", cigarettes " + s.inventory.Count("cigarettes") + ", capacity " + s.Capacity.ToString("0.0"));

            // Farming on open grass: dig, plant, water, grow, harvest.
            Vec2 field;
            if (!GrassPatch(w, sim, s.pos, out field)) { Pass("farm-dig-plot", false, "no open grass found"); yield break; }
            s.pos = field; s.facing = 0;
            foreach (var z in w.zombies.Where(z => z.Alive && Vec2.Distance(z.pos, s.pos) < 60)) z.state = ZombieState.Dead;
            w.index = new ZombieIndex(w);
            s.inventory.Add(ItemCatalog.Make("shovel")); s.inventory.Add(ItemCatalog.Make("seeds_tomato", 3)); s.inventory.Add(ItemCatalog.Make("watering_can"));
            report.qaSetup.Add("moved to open grass at " + field + ", cleared zombies within 60 m; granted shovel, 3 tomato seed packets and a full watering can");
            yield return Aim(s.pos + new Vec2(3, 0), .5f);
            int fx, fy; sim.FrontTile(s, out fx, out fy);
            var self = game.Nearby.FirstOrDefault(n => n.label == "Yourself");
            var dig = self == null ? null : self.actions.FirstOrDefault(a => a.label.StartsWith("Dig a farm plot"));
            Pass("dig-action-offered", dig != null && dig.enabled, dig == null ? "no dig action" : dig.enabled ? "enabled" : dig.reason);
            if (dig != null) dig.run(); else game.Submit(Command.Of(CommandKind.DigPlot));
            yield return WaitFor(() => w.map.props.Any(p => p.kind == PropKind.FarmPlot && !p.removed), 12);
            var plotProp = w.map.props.LastOrDefault(p => p.kind == PropKind.FarmPlot);
            var plot = plotProp == null ? null : sim.PlotRef(plotProp.id);
            Pass("farm-dig-plot", plot != null, plotProp == null ? "no plot" : "plot at " + plotProp.x + "," + plotProp.y + " (faced " + fx + "," + fy + ")");
            if (plot == null) yield break;
            yield return new WaitForSecondsRealtime(.6f);
            var it = game.Nearby.FirstOrDefault(n => n.label.StartsWith("Farm plot"));
            var plant = it == null ? null : it.actions.FirstOrDefault(a => a.label.StartsWith("Plant tomatoes"));
            Pass("plant-action-offered", plant != null && plant.enabled, it == null ? "plot not in nearby interactions" : string.Join(" | ", it.actions.Select(a => a.label)));
            if (plant != null) plant.run(); else game.Submit(new Command { kind = CommandKind.Plant, target = plotProp.id, item = s.inventory.Find("seeds_tomato").uid });
            yield return WaitFor(() => plot.crop == "tomato", 8);
            Pass("farm-plant", plot.crop == "tomato", plot.Label);
            var can = s.inventory.Find("watering_can");
            game.Submit(new Command { kind = CommandKind.WaterPlot, target = plotProp.id, item = can.uid });
            yield return WaitFor(() => plot.water > .9f, 8);
            Pass("farm-water", plot.water > .9f, plot.Label + ", can " + can.water + "/" + can.Def.waterCapacity);
            yield return Shot("15-plot-planted");
            // Fast-forward the host simulation through in-game days (the real hourly growth rules), keeping the soil watered.
            double planted = w.minutes; int ticks = 0; bool growingShot = false;
            while (!plot.Ripe && w.minutes - planted < 6 * 1440)
            {
                sim.Tick(Simulation.Step, 1500); ticks++;
                s.hunger = s.thirst = 10; s.fatigue = 10; s.health = 100; s.wounds.Clear(); s.hoursSinceSmoke = 0;
                foreach (var z in w.zombies.Where(z => z.Alive && Vec2.Distance(z.pos, s.pos) < 60)) z.state = ZombieState.Dead;
                if (plot.water < .3f) plot.water = 1;
                if (!growingShot && plot.growth >= .5f) { growingShot = true; yield return new WaitForSecondsRealtime(.5f); yield return Shot("16-plot-growing"); }
                else if (ticks % 10 == 0) yield return null;
            }
            w.index = new ZombieIndex(w);
            report.qaSetup.Add("fast-forwarded " + ((w.minutes - planted) / 1440).ToString("0.0") + " in-game days with Simulation.Tick(boost), refilling the plot's soil moisture and the survivor's needs");
            Pass("farm-grows-over-days", plot.Ripe, plot.Label + " after " + ((w.minutes - planted) / 1440).ToString("0.00") + " days");
            yield return new WaitForSecondsRealtime(.8f);
            var plantsView = FindPlotView(plotProp.id);
            Pass("farm-plot-rendered", plantsView != null && plantsView.childCount > 0 && plantsView.GetChild(0).name.Contains("tomato 5"), plantsView == null ? "no plot view" : plantsView.GetChild(0).name);
            yield return Shot("17-plot-ripe");
            int tomatoes = s.inventory.Count("tomato");
            game.Submit(new Command { kind = CommandKind.Harvest, target = plotProp.id });
            yield return WaitFor(() => plot.Empty, 8);
            Pass("farm-harvest", plot.Empty && s.inventory.Count("tomato") > tomatoes, "tomatoes " + tomatoes + " -> " + s.inventory.Count("tomato"));
            // Replant so a growing crop is saved and reloaded.
            game.Submit(new Command { kind = CommandKind.Plant, target = plotProp.id, item = s.inventory.Find("seeds_tomato").uid });
            yield return WaitFor(() => !plot.Empty, 8);
            plot.growth = .37f; report.qaSetup.Add("replanted tomatoes and set growth to 37% to verify save/reload");
            qaPlot = plotProp.id; qaPlotCrop = plot.crop; qaPlotGrowth = plot.growth;

            // Fishing from a bank.
            Vec2 bank;
            if (!Bank(w, s.pos, out bank)) Pass("fishing-catch", false, "no river or pond bank found");
            else
            {
                s.pos = bank; foreach (var z in w.zombies.Where(z => z.Alive && Vec2.Distance(z.pos, s.pos) < 60)) z.state = ZombieState.Dead; w.index = new ZombieIndex(w);
                var rod = ItemCatalog.Make("fishing_rod"); s.inventory.Add(rod); s.inventory.Add(ItemCatalog.Make("worms", 10));
                report.qaSetup.Add("moved to a water bank at " + bank + "; granted a fishing rod and 10 worms");
                yield return new WaitForSecondsRealtime(.6f);
                var me = game.Nearby.FirstOrDefault(n => n.label == "Yourself"); var fishAction = me == null ? null : me.actions.FirstOrDefault(a => a.label == "Fish here");
                Pass("fish-action-offered", fishAction != null && fishAction.enabled, me == null ? "no self actions" : string.Join(" | ", me.actions.Select(a => a.label)));
                int casts = 0; Func<int> fish = () => s.inventory.items.Where(i => i.def.StartsWith("fish_") && !i.def.EndsWith("_cooked")).Sum(i => i.count);
                while (fish() == 0 && casts < 10 && s.inventory.Count("worms") > 0)
                {
                    casts++; if (fishAction != null) fishAction.run(); else game.Submit(new Command { kind = CommandKind.Fish, item = rod.uid });
                    yield return WaitFor(() => s.action != null && s.action.kind == ActionKind.Fish, 2);   // the host has started this cast
                    if (casts == 1) yield return Shot("18-fishing");
                    yield return WaitFor(() => s.action == null, 20);
                    s.hunger = s.thirst = 10; s.health = 100; s.hoursSinceSmoke = 0;
                }
                Pass("fishing-catch", fish() > 0, casts + " casts, worms left " + s.inventory.Count("worms") + ", Fishing xp " + s.xp[(int)Skill.Fishing].ToString("0") + " level " + s.Level(Skill.Fishing));
                var raw = s.inventory.items.FirstOrDefault(i => i.def.StartsWith("fish_") && !i.def.EndsWith("_cooked"));
                if (raw != null)
                {
                    var fire = PlaceBeside(w, sim, s, PropKind.Campfire); if (fire != null) { fire.lit = true; fire.fuel = 2; fire.modified = true; report.qaSetup.Add("placed a lit campfire beside the bank"); }
                    yield return new WaitForSecondsRealtime(.4f);
                    string cooked = raw.Def.cooksInto; game.Submit(new Command { kind = CommandKind.Cook, item = raw.uid });
                    yield return WaitFor(() => s.inventory.Count(cooked) > 0, 10);
                    Pass("fish-cooks", s.inventory.Count(cooked) > 0, raw.Def.name + " -> " + ItemCatalog.Get(cooked).name);
                    yield return Shot("19-fish-cooked");
                }
                qaFishing = s.Level(Skill.Fishing);
            }

            // Skill book: read through the inventory command, then check the multiplier and the Health/Skills panel.
            w.minutes = Math.Floor(w.minutes / 1440) * 1440 + 12 * 60;
            var book = ItemCatalog.Make("book_carpentry_1"); s.inventory.Add(book); report.qaSetup.Add("granted Carpentry Handbook, Vol. 1; set the clock to noon so it is light enough to read");
            game.Submit(new Command { kind = CommandKind.Use, item = book.uid });
            yield return WaitFor(() => s.booksRead.Contains(book.def), 26);
            Pass("skill-book-read", s.booksRead.Contains(book.def) && Simulation.BookMultiplier(s, Skill.Carpentry) == 3, "Carpentry level " + s.Level(Skill.Carpentry) + ", multiplier " + Simulation.BookMultiplier(s, Skill.Carpentry));
            yield return Press(Key.K); yield return new WaitForSecondsRealtime(.8f);
            Pass("health-panel-shows-traits-and-books", game.UI.BlocksGameplay, "");
            yield return Shot("20-skills-traits-books");
            game.UI.CloseTopPanel();
            qaBooks = new List<string>(s.booksRead); qaTraits = new List<string>(s.traits);

            // Smoking satisfies the smoker's craving.
            s.hoursSinceSmoke = 10; int cigs = s.inventory.Count("cigarettes"); report.qaSetup.Add("set hours since the last cigarette to 10");
            game.Submit(new Command { kind = CommandKind.Use, item = s.inventory.Find("cigarettes").uid });
            yield return WaitFor(() => s.inventory.Count("cigarettes") < cigs, 8);
            Pass("smoker-smokes", s.inventory.Count("cigarettes") == cigs - 1 && s.hoursSinceSmoke < .5f, "cigarettes " + cigs + " -> " + s.inventory.Count("cigarettes"));
        }

        Transform FindPlotView(int prop)
        {
            foreach (var t in game.View.GetComponentsInChildren<Transform>(true))
                if (t.name == "Plants" && t.childCount > 0 && t.parent != null)
                {
                    var p = game.World.map.props[prop]; var pos = t.parent.localPosition;
                    if (Mathf.Abs(pos.x - (p.x + .5f)) < .1f && Mathf.Abs(pos.z - (p.y + .5f)) < .1f) return t;
                }
            return null;
        }
        static bool GrassPatch(World w, Simulation sim, Vec2 near, out Vec2 at)
        {
            var m = w.map; at = near;
            for (int r = 0; r < 600; r += 3)
                for (int a = 0; a < 24; a++)
                {
                    var p = near + Vec2.FromAngle(a * .2618f) * r; int x = (int)p.x, y = (int)p.y; bool ok = true;
                    for (int dx = -3; dx <= 3 && ok; dx++)
                        for (int dy = -3; dy <= 3 && ok; dy++)
                        {
                            int tx = x + dx, ty = y + dy;
                            if (m.GroundAt(tx, ty) != Ground.Grass || m.TileBlocked(tx, ty) || m.RoomAt(tx, ty) != null || m.H(tx, ty) != Edge.None || m.V(tx, ty) != Edge.None || sim.NonBlockingPropAt(tx, ty) != null) ok = false;
                        }
                    if (ok) { at = new Vec2(x + .5f, y + .5f); return true; }
                }
            return false;
        }
        static bool Bank(World w, Vec2 near, out Vec2 at)
        {
            var m = w.map; at = near;
            for (int r = 0; r < 1500; r += 2)
                for (int a = 0; a < 32; a++)
                {
                    var p = near + Vec2.FromAngle(a * .19635f) * r; int x = (int)p.x, y = (int)p.y;
                    if (!m.InBounds(x, y) || m.TileBlocked(x, y) || m.RoomAt(x, y) != null || m.GroundAt(x, y) == Ground.Bridge) continue;
                    bool water = false; for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) if (m.IsWater(x + dx, y + dy)) water = true;
                    var c = new Vec2(x + .5f, y + .5f);
                    if (water && !m.Collides(c, Survivor.Radius)) { at = c; return true; }
                }
            return false;
        }
        static Prop PlaceBeside(World w, Simulation sim, Survivor s, PropKind kind)
        {
            int sx = (int)Math.Floor(s.pos.x), sy = (int)Math.Floor(s.pos.y);
            foreach (var d in new[] { new Int2(1, 0), new Int2(-1, 0), new Int2(0, 1), new Int2(0, -1), new Int2(1, 1), new Int2(-1, -1), new Int2(1, -1), new Int2(-1, 1) })
            {
                int x = sx + d.x, y = sy + d.y;
                if (w.map.TileBlocked(x, y) || w.map.RoomAt(x, y) != null || sim.NonBlockingPropAt(x, y) != null) continue;
                var p = w.map.AddProp(kind, x, y); p.modified = true; return p;
            }
            return null;
        }

        void Finish(string failure)
        {
            report.failure = failure; report.finished = DateTime.UtcNow.ToString("o");
            report.status = failure.Length == 0 && report.checks.All(c => c.pass) ? "passed" : "failed";
            Save(); Debug.Log("ASHFALL_QA_DONE " + report.status + " " + reportPath);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (pad != null) InputSystem.RemoveDevice(pad);
            Application.Quit(report.status == "passed" ? 0 : 1);
        }
    }
}
