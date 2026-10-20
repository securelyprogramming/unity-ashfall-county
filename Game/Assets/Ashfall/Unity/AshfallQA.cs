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
            var watch = Stopwatch.StartNew();
            game.StartNewGame(new SandboxSettings { seed = 777, mapSize = size, population = Population.Normal }, "QA Survivor", "carpenter", true);
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
            yield return new WaitForSecondsRealtime(3);
            var frames = new List<float>(); float until = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < until) { frames.Add(Time.unscaledDeltaTime * 1000); yield return null; }
            report.averageFrameMs = frames.Average(); report.worstFrameMs = frames.Max(); report.activeZombies = game.Sim.ActiveZombies; report.loadedChunks = game.View.LoadedChunks; report.loadedBuildings = game.View.LoadedBuildings; report.simMs = game.SimMs; report.viewMs = game.ViewMs; report.uiMs = game.UiMs;
            Pass("frame-time-in-town", report.averageFrameMs < 50 && report.activeZombies > 50, "avg=" + report.averageFrameMs.ToString("0.0") + "ms worst=" + report.worstFrameMs.ToString("0.0") + " sim=" + report.simMs.ToString("0.0") + " view=" + report.viewMs.ToString("0.0") + " ui=" + report.uiMs.ToString("0.0") + " active zombies=" + report.activeZombies);
            yield return Shot("10-town");

            // Save, quit to title, reload, compare.
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
            yield return Shot("11-reloaded");
            Finish("");
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
