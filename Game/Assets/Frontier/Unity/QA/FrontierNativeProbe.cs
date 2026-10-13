using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace Mayo.Frontier.Presentation
{
    /// <summary>
    /// Opt-in native player probe. Campaign acceptance issues production commands only;
    /// local save corruption and invalid LAN requests are isolated, explicitly labelled fixtures.
    /// </summary>
    public sealed class FrontierNativeProbe : MonoBehaviour
    {
        [Serializable] public sealed class CheckRecord { public string name, detail; public bool passed; }
        [Serializable] public sealed class ChapterRecord { public int chapter, commands, tick, progress, goal; public float simulationSeconds; public bool completed; }
        [Serializable] public sealed class CaptureRecord { public string kind, path, sha256; public int chapter, tick, width, height; }
        [Serializable] public sealed class LogRecord { public string type, message, stack; }
        [Serializable] public sealed class NativeReport
        {
            public int schemaVersion = 1;
            public string gameId, mode, status = "running", unityVersion, platform, failure = "", scope;
            public bool physicalControllerTested, physicalLanTested, windowsLinuxExecutionTested, steamCertification;
            public int completedChapters, commandCount, snapshotsReceived, localPlayerId;
            public double wallSeconds;
            public List<CheckRecord> checks = new List<CheckRecord>();
            public List<ChapterRecord> chapters = new List<ChapterRecord>();
            public List<CaptureRecord> captures = new List<CaptureRecord>();
            public List<LogRecord> errors = new List<LogRecord>();
            public List<string> warnings = new List<string>();
        }

        FrontierGame game;
        NativeReport report;
        string reportPath, capturePath, mode;
        float timeout = 240;
        readonly Stopwatch clock = new Stopwatch();
        readonly object logGate = new object();
        Gamepad pad;
        bool initialized, finishing;
        PlaytestDriver driver;

        public void Initialize(FrontierGame owner)
        {
            if (initialized) throw new InvalidOperationException("Native probe cannot be initialized twice.");
            initialized = true; game = owner ?? throw new ArgumentNullException("owner");
            mode = FrontierGame.Arg("frontier-qa");
            if (mode == "") mode = FrontierGame.Arg("frontier-lan-role") == "host" ? "lan-host" : FrontierGame.Arg("frontier-lan-role") == "client" ? "lan-client" : "visual";
            reportPath = GuardedPath(FrontierGame.Arg("frontier-report"));
            capturePath = GuardedPath(FrontierGame.Arg("frontier-capture"));
            float parsed;
            if (float.TryParse(FrontierGame.Arg("frontier-timeout"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed)) timeout = Mathf.Clamp(parsed, 20, 600);
            report = new NativeReport { gameId = game.Profile.id, mode = mode, unityVersion = Application.unityVersion, platform = Application.platform.ToString(), scope = "Actual Unity standalone rendering and production UI/input/core; virtual gamepad; LAN roles use independent local player processes." };
            Application.logMessageReceivedThreaded += OnLog;
            clock.Start();
            StartCoroutine(Guarded(Run()));
        }

        static string GuardedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string absolute = Path.GetFullPath(path);
            if (absolute.IndexOf("GameWorkbenchQA", StringComparison.Ordinal) < 0) throw new InvalidOperationException("Native diagnostic output must be isolated under GameWorkbenchQA.");
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)); return absolute;
        }

        void OnLog(string condition, string stack, LogType type)
        {
            lock (logGate)
            {
                if (report == null) return;
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    if (report.errors.Count < 100) report.errors.Add(new LogRecord { type = type.ToString(), message = condition, stack = stack });
                }
                else if (type == LogType.Warning && report.warnings.Count < 100) report.warnings.Add(condition);
            }
        }

        IEnumerator Guarded(IEnumerator routine)
        {
            // Unity does not propagate nested coroutine exceptions to its caller. Explicitly
            // traverse nested iterators so every failure still produces a report and clean exit.
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            while (stack.Count > 0)
            {
                object next = null; bool hasNext = false; Exception caught = null;
                try
                {
                    if (clock.Elapsed.TotalSeconds > timeout) throw new TimeoutException("Native QA exceeded its bounded wall-clock budget.");
                    hasNext = stack.Peek().MoveNext();
                    if (hasNext) next = stack.Peek().Current;
                }
                catch (Exception ex) { caught = ex; }
                if (caught != null)
                {
                    report.failure = caught.ToString(); Debug.LogError("[FrontierQA] " + caught);
                    break;
                }
                if (!hasNext) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                var nested = next as IEnumerator;
                if (nested != null) stack.Push(nested); else yield return next;
            }
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            yield return Finish();
        }

        void Require(string name, bool condition, string detail = "")
        {
            report.checks.Add(new CheckRecord { name = name, passed = condition, detail = detail });
            if (!condition) throw new InvalidOperationException(name + (detail == "" ? "" : ": " + detail));
        }

        IEnumerator Run()
        {
            yield return null;
            Require("diagnostic executes in a standalone Unity player", !Application.isEditor);
            Require("native diagnostic isolation", game.QaActive && game.Store.SavePath.IndexOf("GameWorkbenchQA", StringComparison.Ordinal) >= 0);
            Require("real Unity camera and authored presentation", game.View != null && game.View.ViewCamera != null && game.View.ViewCamera.enabled);
            Require("title menu has production campaign, settings, controls and LAN actions", game.ScreenName == "title" && game.UI.ActiveMenuButtonCount >= 8);
            yield return Capture("menu");
            if (mode == "campaign") yield return Campaign();
            else if (mode == "read") yield return IndependentRead();
            else if (mode == "visual") yield return Visual();
            else if (mode == "ui-regression") yield return ResidentUiRegression();
            else if (mode == "lan-host") yield return LanHost();
            else if (mode == "lan-client") yield return LanClient();
            else throw new ArgumentException("Unknown -frontier-qa mode: " + mode);
        }

        Button FindButton(string prefix)
        {
            return game.UI.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.gameObject.activeInHierarchy && b.gameObject.name.StartsWith(prefix, StringComparison.Ordinal));
        }

        void Click(string prefix)
        {
            var button = FindButton(prefix);
            Require("production UI button available: " + prefix, button != null && button.interactable);
            button.onClick.Invoke();
        }

        TMPro.TMP_Text VisibleText(Func<TMPro.TMP_Text, bool> predicate)
        {
            return game.UI.GetComponentsInChildren<TMPro.TMP_Text>(false).FirstOrDefault(text => text.gameObject.activeInHierarchy && predicate(text));
        }

        IEnumerator MoveUiPlayer(float x, float z)
        {
            // Production directional intent and authoritative ticks only; no teleport or grants.
            int steps = 0;
            Player player = game.State.players.Find(p => p.id == game.LocalPlayerId);
            while (Simulation.Distance(player.x, player.z, x, z) > 1.4f && steps++ < 600)
            {
                float dx = x - player.x, dz = z - player.z, distance = Simulation.Distance(player.x, player.z, x, z);
                var moved = game.Send(new Command { kind = "move", x = dx / distance, z = dz / distance }, false);
                Require("UI reproduction movement remains authoritative", moved.accepted && !player.dead);
                game.Rules.Tick(.1f); game.RefreshNativeState();
                if (steps % 8 == 0) yield return null;
            }
            game.Send(new Command { kind = "move" }, false);
            Require("UI reproduction reaches the requested location through movement", Simulation.Distance(player.x, player.z, x, z) <= 1.4f);
            yield return null;
        }

        IEnumerator UiControllerBack(string expectedScreen, string expectedPhase)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return new WaitForSecondsRealtime(.2f);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East)); yield return null;
            Require("resident menu East resolves the real Cancel binding", game.Gameplay.FindAction("Cancel").WasPressedThisFrame());
            game.ReadNativeInput();
            Require("resident menu back returns to " + expectedScreen, game.ScreenName == expectedScreen && game.State.phase == expectedPhase);
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
        }

        IEnumerator OpenResidentChooser()
        {
            if (game.ScreenName == "play") { Click("Actions"); yield return null; }
            if (game.ScreenName == "actions") { Click("Resident roles"); yield return null; }
            Require("Actions opens a visible resident list", game.ScreenName == "residents" && FindButton("Resident 1") != null);
            Click("Resident 1"); yield return null;
            Require("resident list opens a reachable role chooser", game.ScreenName == "resident-role" && FindButton("Assign guard") != null && FindButton("Assign scavenger") != null);
        }

        IEnumerator ResidentUiRegression()
        {
            Require("resident UI regression runs the actual Havenfall project", game.Profile.id == "havenfall-coalition");
            Click(game.Progress.completedMask == 0 ? "Begin campaign" : "Continue campaign"); yield return null;
            Click("Enter chapter"); yield return null;
            Require("resident UI reproduction starts a real campaign chapter", game.SessionStarted && game.ScreenName == "play" && game.State.phase == "active");
            driver = new PlaytestDriver(game.Rules, continueCampaign: false);
            int steps = 0;
            while (!game.State.tasks.Any(task => task.kind == "resident" && task.status == "complete") && !driver.Done && steps++ < 3000)
            {
                driver.Tick(.1f); game.RefreshNativeState();
                if (steps % 8 == 0) yield return null;
            }
            Require("a resident is rescued through production gameplay before assignment", driver.FailureReason == "" && game.State.tasks.Any(task => task.kind == "resident" && task.status == "complete"), driver.FailureReason);
            TaskState resident = game.State.tasks.First(task => task.kind == "resident" && task.status == "complete");
            Structure centre = game.State.structures.First(structure => structure.kind == "community");
            yield return MoveUiPlayer(centre.x, centre.z);
            Player player = game.State.players.Find(p => p.id == game.LocalPlayerId);
            Require("assignment reproduction is inside the real community radius", Simulation.Distance(player.x, player.z, centre.x, centre.z) <= Simulation.InteractionRange);
            Click("Actions"); yield return null;
            Require("opening solo Actions uses the production paused state", game.ScreenName == "actions" && game.State.phase == "paused");
            yield return Capture("resident-actions-before-list", true);
            // This assertion is expected to fail in the unmodified production baseline.
            Require("Actions exposes a human-accessible Resident roles control", FindButton("Resident roles") != null);
            yield return OpenResidentChooser();
            float pausedAt = game.State.elapsed;
            Click("Assign guard"); yield return null; yield return new WaitForSecondsRealtime(.2f);
            Require("real guard button assigns the completed resident without closing or unpausing the chooser", resident.item == "guard" && game.ScreenName == "resident-role" && game.State.phase == "paused");
            Require("guard assignment does not advance the paused simulation clock", Mathf.Approximately(game.State.elapsed, pausedAt));
            Require("guard acknowledgement is visible in current-role TMP text", VisibleText(text => text.text.StartsWith("Current role:", StringComparison.Ordinal) && text.text.IndexOf("guard", StringComparison.OrdinalIgnoreCase) >= 0) != null);
            yield return Capture("resident-guard");
            pad = InputSystem.AddDevice<Gamepad>("FrontierQA Resident UI Gamepad");
            Require("resident chooser has the real uGUI input module", EventSystem.current != null && EventSystem.current.GetComponent<InputSystemUIInputModule>() != null);
            UnityEngine.UI.Button guard = FindButton("Assign guard");
            EventSystem.current.SetSelectedGameObject(guard.gameObject);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadDown));
            float deadline = Time.realtimeSinceStartup + .7f;
            while (EventSystem.current.currentSelectedGameObject == guard.gameObject && Time.realtimeSinceStartup < deadline) yield return null;
            Require("virtual D-pad reaches the actual scavenger choice", EventSystem.current.currentSelectedGameObject != null && EventSystem.current.currentSelectedGameObject.name.StartsWith("Assign scavenger", StringComparison.Ordinal));
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            deadline = Time.realtimeSinceStartup + .7f;
            while (resident.item != "scavenger" && Time.realtimeSinceStartup < deadline) yield return null;
            Require("virtual South submits the actual scavenger callback", resident.item == "scavenger" && game.ScreenName == "resident-role" && game.State.phase == "paused");
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return new WaitForSecondsRealtime(.2f);
            Require("scavenger acknowledgement updates visible current-role TMP text", VisibleText(text => text.text.StartsWith("Current role:", StringComparison.Ordinal) && text.text.IndexOf("scavenger", StringComparison.OrdinalIgnoreCase) >= 0) != null);
            yield return Capture("resident-scavenger");
            yield return UiControllerBack("residents", "paused");
            yield return UiControllerBack("actions", "paused");
            yield return UiControllerBack("play", "active");
            int wood = player.wood, metal = player.metal;
            for (int tick = 0; tick < 202; tick++) { game.Rules.Tick(.05f); game.RefreshNativeState(); if (tick % 16 == 0) yield return null; }
            Require("UI-assigned scavenger produces actual community resources", player.wood > wood && player.metal > metal);
            // A separate rejection edge uses legal movement away from the centre, not a fixture teleport.
            yield return MoveUiPlayer(centre.x + 6, centre.z);
            yield return OpenResidentChooser(); pausedAt = game.State.elapsed;
            Click("Assign guard"); yield return null; yield return new WaitForSecondsRealtime(.2f);
            Require("out-of-range assignment preserves role, chooser and solo pause", resident.item == "scavenger" && game.ScreenName == "resident-role" && game.State.phase == "paused" && Mathf.Approximately(game.State.elapsed, pausedAt));
            Require("authoritative rejection reason is visible to the player", game.Notice.Contains("community centre") && VisibleText(text => text.gameObject.name == "Notice" && text.text == game.Notice) != null, game.Notice);
            yield return Capture("resident-range-rejection");
            InputSystem.RemoveDevice(pad); pad = null;
            report.commandCount = driver.CommandCount + game.CommandsAccepted;
        }

        IEnumerator Campaign()
        {
            Click("Settings"); yield return null;
            Require("settings screen is reachable through its real menu button", game.ScreenName == "options" && game.UI.ActiveMenuButtonCount >= 8);
            float before = game.Progress.volume;
            Click("Master volume:"); yield return null;
            Require("volume control applies and persists", !Mathf.Approximately(before, game.Progress.volume) && Mathf.Approximately(AudioListener.volume, game.Progress.volume) && Mathf.Approximately(game.Store.Load().volume, game.Progress.volume));
            Click("Quality:"); yield return null;
            Require("quality control persists", game.Store.Load().quality == game.Progress.quality);
            game.Gameplay.FindAction("Interact").ApplyBindingOverride(0, "<Keyboard>/f");
            game.Progress.volume = .42f; game.Progress.quality = 1; game.Progress.fullScreen = false;
            game.ApplySettings(); game.SaveSettings();
            Require("input binding override is saved", game.Store.Load().bindings.Contains("<Keyboard>/f"));

            // The parent already required an isolated QA user directory. Preserve the corrupt
            // primary in that sandbox while proving backup recovery does not overwrite it.
            Require("isolated save has a valid backup", File.Exists(game.Store.SavePath + ".bak"));
            string damaged = "{\"schema\":1,\"data\":\"truncated native QA fixture";
            File.WriteAllText(game.Store.SavePath, damaged);
            ProgressData recovered = game.Store.Load();
            Require("corrupted isolated save recovers its last valid backup", recovered.gameId == game.Profile.id && game.Store.Notice.Contains("Recovered"));
            Require("loading preserves damaged primary for diagnosis", File.ReadAllText(game.Store.SavePath) == damaged);
            Require("valid local save repairs primary without deleting backup", game.Store.Save(game.Progress) && File.Exists(game.Store.SavePath + ".bak"));
            Require("settings survive repaired primary load", Mathf.Approximately(game.Store.Load().volume, .42f) && game.Store.Load().bindings.Contains("<Keyboard>/f"));
            CheckCorruptPair();
            game.Show("title"); yield return null; Click("Controls"); yield return null;
            Require("controls screen is reachable", game.ScreenName == "controls" && game.UI.ActiveMenuButtonCount >= 1);
            Require("controls screen reflects the saved keyboard override", VisibleText(text=>text.text.Contains("INTERACT: F / South"))!=null);
            game.Show("title"); yield return null; Click("Credits"); yield return null;
            Require("credits screen is reachable", game.ScreenName == "credits" && game.UI.ActiveMenuButtonCount >= 1);
            game.Show("title"); yield return null; Click(game.Progress.completedMask == 0 ? "Begin campaign" : "Continue campaign"); yield return null;
            Require("campaign menu enters authored story", game.ScreenName == "story" && game.Profile.chapters.Length == 3 && !string.IsNullOrEmpty(game.Profile.chapters[0].story));
            yield return Capture("story"); Click("Enter chapter"); yield return null;
            Require("story button starts gameplay", game.SessionStarted && game.ScreenName == "play");
            yield return VirtualController();

            // Start a fresh campaign through the same constructor as StartSolo. Only the
            // production driver issues commands from here; no state, grants or outcomes are set.
            var simulation = new Simulation(game.Profile.Rules(), 1);
            game.SetNativeState(simulation); game.Notice = ""; game.BeginChapter();
            driver = new PlaytestDriver(simulation);
            var captured = new HashSet<int>(); var finished = new HashSet<int>();
            int shownChapter = 1;
            yield return ServiceChoices();
            while (!driver.Done)
            {
                for (int step = 0; step < 8 && !driver.Done; step++)
                {
                    driver.Tick(.1f); game.RefreshNativeState();
                    if (game.State.phase == "won" || game.State.chapter != shownChapter) break;
                }
                game.UI.RefreshHUD();
                if (game.State.chapter != shownChapter)
                {
                    shownChapter = game.State.chapter; game.Show("story");
                    yield return Capture("story" + shownChapter); game.BeginChapter();
                    yield return ServiceChoices();
                }
                if (game.State.elapsed >= 12 && captured.Add(game.State.chapter))
                {
                    yield return Capture("chapter" + game.State.chapter);
                    if (game.State.chapter == 1) yield return Capture("gameplay", true);
                }
                if (game.State.phase == "won" && finished.Add(game.State.chapter))
                {
                    if (game.State.chapter == 1 && !captured.Contains(1)) yield return Capture("gameplay", true);
                    report.chapters.Add(new ChapterRecord { chapter = game.State.chapter, completed = true, commands = driver.CommandCount, tick = game.State.tick, simulationSeconds = game.State.elapsed, progress = game.State.progress, goal = game.State.goal });
                    Require("production commands complete chapter " + game.State.chapter, true, game.State.message);
                    // The root's next Update observes the actual win and persists progression.
                    yield return Capture("victory" + game.State.chapter); yield return null;
                }
                yield return null;
            }
            report.completedChapters = driver.CompletedChapters; report.commandCount = driver.CommandCount;
            Require("non-mutating production driver has no failure", driver.FailureReason == "", driver.FailureReason);
            Require("three complete chapters accepted in the native player", driver.CompletedChapters == 3 && finished.Count == 3);
            Require("presentation observed and unlocked all actual chapter wins", game.Progress.unlocked == 3 && game.Progress.completedMask == 7);
            // Explicitly persist the progression already established by the root's win observer.
            Require("completed campaign unlocks save atomically", game.Store.Save(game.Progress));
            ProgressData saved = game.Store.Load();
            Require("same-process progression round-trip", saved.unlocked == 3 && saved.completedMask == 7);
            game.Show("result"); yield return Capture("finale");
            Require("rendered world has player and gameplay entities", game.View.VisibleEntityCount > 1);
            if(game.Profile.id=="market-day"||game.Profile.id=="hearth-rush")
            {
                game.Show("actions");yield return null;
                Click(game.Profile.id=="market-day"?"Supplier stock: produce":"Pantry recipe: bread");
                Require("chapter-three service choice is selected through its real button",game.InteractionChoice==(game.Profile.id=="market-day"?"produce":"bread"));
                game.ChapterChoice=1;game.StartSolo();yield return null;
                Require("revisiting chapter one resets a later unlocked service choice",game.State.chapter==1&&game.InteractionChoice==""&&game.ScreenName=="story");
                Require("revisiting keeps earned campaign unlocks",game.Progress.unlocked==3&&game.Progress.completedMask==7);
            }
        }

        IEnumerator ServiceChoices()
        {
            if(game.Profile.id!="market-day"&&game.Profile.id!="hearth-rush")yield break;
            game.OpenPanel("actions");yield return null;
            string prefix=game.Profile.id=="market-day"?"Supplier stock: ":"Pantry recipe: ";
            int choices=game.UI.GetComponentsInChildren<Button>(false).Count(button=>button.gameObject.activeInHierarchy&&button.name.StartsWith(prefix,StringComparison.Ordinal));
            Require("service Actions offers only the chapter's unlocked choices",choices==game.State.chapter,"chapter="+game.State.chapter+" choices="+choices);
            game.Resume();yield return null;
        }

        IEnumerator VirtualController()
        {
            pad = InputSystem.AddDevice<Gamepad>("FrontierQA Virtual Gamepad");
            Require("virtual gamepad device is registered", pad != null && pad.added);
            yield return ControllerMenus();
            yield return new WaitForSecondsRealtime(.2f);
            Player player = game.Rules.PlayerById(0); float x = player.x, z = player.z;
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(1, 0), rightStick = new Vector2(0, 1) });
            float stopAt = Time.realtimeSinceStartup + .3f;
            while (Time.realtimeSinceStartup < stopAt)
            {
                yield return null; game.ReadNativeInput(); game.Rules.Tick(Mathf.Min(Time.unscaledDeltaTime, .1f)); game.RefreshNativeState();
            }
            Require("virtual left stick moves through actual gameplay input", Simulation.Distance(x, z, player.x, player.z) > .25f);
            Require("virtual right stick resolves the aim action", game.Gameplay.FindAction("Aim").ReadValue<Vector2>().sqrMagnitude > .5f);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            stopAt = Time.realtimeSinceStartup + .2f;
            while (Time.realtimeSinceStartup < stopAt) { yield return null; game.ReadNativeInput(); game.Rules.Tick(Mathf.Min(Time.unscaledDeltaTime, .1f)); }
            Require("releasing movement sends zero intent", Mathf.Abs(player.dx) < .001f && Mathf.Abs(player.dz) < .001f);
            yield return ControllerButton(GamepadButton.South, "Interact");
            yield return ControllerButton(GamepadButton.West, "Attack");
            yield return ControllerButton(GamepadButton.LeftShoulder, "Build");
            yield return ControllerButton(GamepadButton.RightShoulder, "Craft");
            InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1 }); yield return null;
            Require("right trigger resolves attack action", game.Gameplay.FindAction("Attack").IsPressed()); game.ReadNativeInput();
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.Start)); yield return null;
            Require("virtual Start resolves pause action", game.Gameplay.FindAction("Pause").WasPressedThisFrame()); game.ReadNativeInput();
            Require("Start opens the real solo pause panel", game.ScreenName == "pause" && game.State.phase == "paused");
            float pausedAt = game.State.elapsed; game.Rules.Tick(.5f);
            Require("solo pause stops actual simulation time", Mathf.Approximately(game.State.elapsed, pausedAt));
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return new WaitForSecondsRealtime(.2f);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.Start)); yield return null; game.ReadNativeInput();
            Require("Start resumes through production dispatch", game.ScreenName == "play" && game.State.phase == "active");
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return new WaitForSecondsRealtime(.2f);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.Select)); yield return null; game.ReadNativeInput();
            Require("Select opens authored journal through production dispatch", game.ScreenName == "journal" && game.State.phase == "paused");
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return new WaitForSecondsRealtime(.2f);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.Select)); yield return null; game.ReadNativeInput();
            Require("Select returns from journal to play", game.ScreenName == "play" && game.State.phase == "active");
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            InputSystem.RemoveDevice(pad); pad = null;
        }

        void CheckCorruptPair()
        {
            string directory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(game.Store.SavePath)), "CorruptPairFixture");
            var fixture = new FrontierProgress(directory, game.Profile.id);
            var initial = new ProgressData { gameId = game.Profile.id, unlocked = 2, completedMask = 1, volume = .3f };
            Require("separate corrupt-pair fixture creates a valid primary", fixture.Save(initial));
            initial.volume = .6f;
            Require("separate corrupt-pair fixture creates a valid backup", fixture.Save(initial) && File.Exists(fixture.SavePath + ".bak"));
            const string brokenMain = "broken isolated primary", brokenBackup = "broken isolated backup";
            File.WriteAllText(fixture.SavePath, brokenMain); File.WriteAllText(fixture.SavePath + ".bak", brokenBackup);
            ProgressData fallback = fixture.Load();
            Require("two damaged isolated saves fall back to chapter one", fallback.gameId == game.Profile.id && fallback.unlocked == 1 && fallback.completedMask == 0 && fixture.Notice.Contains("could not be read"));
            Require("fallback preserves both damaged isolated files", File.ReadAllText(fixture.SavePath) == brokenMain && File.ReadAllText(fixture.SavePath + ".bak") == brokenBackup);
            Require("fresh primary can recover while retaining damaged backup", fixture.Save(fallback) && fixture.Load().unlocked == 1 && File.ReadAllText(fixture.SavePath + ".bak") == brokenBackup);
            Require("corrupt-pair fixture leaves actual QA campaign intact", game.Store.Load().gameId == game.Profile.id && Mathf.Approximately(game.Store.Load().volume, .42f));
        }

        IEnumerator ControllerMenus()
        {
            game.OpenPanel("pause"); yield return null; yield return new WaitForSecondsRealtime(.2f);
            Require("uGUI input module and a selected menu button are active", EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null && EventSystem.current.GetComponent<InputSystemUIInputModule>() != null);
            GameObject before = EventSystem.current.currentSelectedGameObject;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadDown));
            float deadline = Time.realtimeSinceStartup + .5f;
            while (EventSystem.current.currentSelectedGameObject == before && Time.realtimeSinceStartup < deadline) yield return null;
            Require("virtual D-pad navigates actual uGUI selectable buttons", EventSystem.current.currentSelectedGameObject != null && EventSystem.current.currentSelectedGameObject != before);
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            Button settings = FindButton("Settings");
            Require("controller submit target is a real settings button", settings != null && settings.interactable);
            EventSystem.current.SetSelectedGameObject(settings.gameObject);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            deadline = Time.realtimeSinceStartup + .5f;
            while (game.ScreenName != "options" && Time.realtimeSinceStartup < deadline) yield return null;
            Require("virtual south submits through actual InputSystem uGUI module", game.ScreenName == "options");
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return new WaitForSecondsRealtime(.2f);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East)); yield return null;
            Require("virtual east resolves Cancel binding", game.Gameplay.FindAction("Cancel").WasPressedThisFrame()); game.ReadNativeInput();
            Require("east returns settings to its real pause-menu parent", game.ScreenName == "pause" && game.State.phase == "paused");
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return new WaitForSecondsRealtime(.2f);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East)); yield return null; game.ReadNativeInput();
            Require("east resumes from the actual pause menu", game.ScreenName == "play" && game.State.phase == "active");
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return new WaitForSecondsRealtime(.2f);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East)); yield return null; game.ReadNativeInput();
            Require("east during play does not abandon the campaign", game.ScreenName == "play" && game.State.phase == "active");
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
        }

        IEnumerator ControllerButton(GamepadButton button, string action)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button)); yield return null;
            Require("virtual " + button + " resolves " + action, game.Gameplay.FindAction(action).WasPressedThisFrame());
            game.ReadNativeInput();
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
        }

        IEnumerator IndependentRead()
        {
            Require("independent process loaded all three chapter unlocks", game.Progress.unlocked == 3 && game.Progress.completedMask == 7);
            Require("independent process loaded persisted settings", Mathf.Approximately(game.Progress.volume, .42f) && game.Progress.quality == 1);
            Require("independent process restored remapped keyboard action", game.Gameplay.FindAction("Interact").bindings[0].effectivePath == "<Keyboard>/f");
            Require("progression is stored only inside the diagnostic sandbox", File.Exists(game.Store.SavePath) && File.Exists(game.Store.SavePath + ".bak"));
            report.completedChapters = 3; yield return Capture("reloaded", true);
        }

        IEnumerator Visual()
        {
            game.ChapterChoice = 1; game.StartSolo(); yield return Capture("story");
            game.BeginChapter();
            float until = Time.realtimeSinceStartup + 1;
            while (Time.realtimeSinceStartup < until) { game.Rules.Tick(Mathf.Min(Time.unscaledDeltaTime, .1f)); game.RefreshNativeState(); yield return null; }
            game.UI.RefreshHUD();
            Require("visual capture uses unmodified production chapter", game.State.chapter == 1 && game.State.tick > 0 && game.State.phase == "active");
            Require("native visuals include gameplay entities", game.View.VisibleEntityCount > 1);
            yield return Capture("gameplay", true);
        }

        IEnumerator LanHost()
        {
            game.ChapterChoice = 1; game.Host();
            Require("native host starts its real LAN lobby", game.IsNetworked && !game.IsClient && game.ScreenName == "lobby");
            TMPro.TMP_Text lobbyStatus = VisibleText(text => text.text.StartsWith("LAN host", StringComparison.Ordinal));
            Require("initial visible lobby count matches the host", lobbyStatus != null && lobbyStatus.text == game.ConnectionLabel && lobbyStatus.text.Contains("0 / 3 guests"));
            yield return Capture("lobby-before-join");
            float deadline = Time.realtimeSinceStartup + 60;
            Player guest = null;
            while (guest == null && Time.realtimeSinceStartup < deadline)
            {
                // Hold only the diagnostic clock while waiting for the other process to start;
                // this keeps startup scheduling from becoming an unrelated enemy-survival test.
                game.RefreshNativeState(); guest = game.State.players.FirstOrDefault(p => p.id > 0 && p.connected); yield return null;
            }
            Require("independent native client joins authoritative host", guest != null && game.Rules.ConnectedCount() == 2);
            yield return new WaitForSecondsRealtime(.25f);
            yield return Capture("lobby-after-join");
            Require("visible lobby count updates after a guest joins without reopening the menu", lobbyStatus != null && lobbyStatus.gameObject.activeInHierarchy && lobbyStatus.text == game.ConnectionLabel && lobbyStatus.text.Contains("1 / 3 guests"), "rendered=" + (lobbyStatus == null ? "missing" : lobbyStatus.text) + "; expected=" + game.ConnectionLabel);
            game.BeginChapter();
            int guestId = guest.id; float startX = guest.x, startZ = guest.z;
            var pause = game.Send(new Command { kind = "pause" }, false);
            Require("host cannot pause a party with an admitted client", !pause.accepted && game.State.phase == "active");
            game.OpenPanel("pause"); float elapsed = game.State.elapsed;
            float menuUntil = Time.realtimeSinceStartup + .3f;
            while (Time.realtimeSinceStartup < menuUntil) { TickHost(); yield return null; }
            Require("host LAN menu leaves party simulation running", game.ScreenName == "pause" && game.State.phase == "active" && game.State.elapsed > elapsed);
            game.Resume();
            deadline = Time.realtimeSinceStartup + 25;
            while ((guest.lastSequence < 3 || Mathf.Abs(guest.dx) + Mathf.Abs(guest.dz) > .001f) && guest.connected && Time.realtimeSinceStartup < deadline) { TickHost(); yield return null; }
            Require("host receives real movement stop and interaction commands", guest.connected && guest.lastSequence >= 3);
            Require("authoritative guest position changes from client intent", Simulation.Distance(startX, startZ, guest.x, guest.z) > .5f);
            int coins = guest.coins, wood = guest.wood, metal = guest.metal, food = guest.food;
            var replay = game.Rules.Apply(guestId, new Command { sequence = guest.lastSequence, kind = "interact" });
            Require("authoritative native simulation rejects replayed guest command", !replay.accepted && replay.reason.Contains("sequence"));
            Require("replay cannot grant or spend resources", guest.coins == coins && guest.wood == wood && guest.metal == metal && guest.food == food);
            var unknown = game.Rules.Apply(guestId, new Command { sequence = guest.lastSequence + 1, kind = "qa-forbidden-resource-grant" });
            Require("native authority rejects an unknown resource-grant command", !unknown.accepted && unknown.reason.Contains("Unknown command"));
            Require("invalid native command cannot grant or spend resources", guest.coins == coins && guest.wood == wood && guest.metal == metal && guest.food == food);
            game.UI.RefreshHUD(); yield return Capture("lan-host", true);
            game.Show("lobby"); yield return null;
            TMPro.TMP_Text departureStatus = VisibleText(text => text.text.StartsWith("LAN host", StringComparison.Ordinal));
            Require("departure reproduction displays a live occupied lobby", guest.connected && departureStatus != null && departureStatus.text.Contains("1 / 3 guests"));
            deadline = Time.realtimeSinceStartup + 25;
            while (guest.connected && Time.realtimeSinceStartup < deadline) { TickHost(); yield return null; }
            Require("forbidden client command causes host disconnect", !guest.connected && game.Rules.ConnectedCount() == 1);
            yield return new WaitForSecondsRealtime(.25f);
            Require("visible lobby count updates after disconnect without reopening the menu", departureStatus != null && departureStatus.gameObject.activeInHierarchy && departureStatus.text == game.ConnectionLabel && departureStatus.text.Contains("0 / 3 guests"));
            yield return Capture("lobby-after-departure");
            report.commandCount = game.CommandsAccepted; report.localPlayerId = 0;
        }

        void TickHost() { game.Rules.Tick(Mathf.Min(Time.unscaledDeltaTime, .1f)); game.RefreshNativeState(); }

        IEnumerator LanClient()
        {
            game.HostAddress = "127.0.0.1"; game.Join();
            float deadline = Time.realtimeSinceStartup + 45;
            while ((game.LocalPlayerId < 1 || game.State == null || !game.SessionStarted || game.SnapshotsReceived < 2) && Time.realtimeSinceStartup < deadline) yield return null;
            Require("native client receives assigned identity and host snapshots", game.IsClient && game.LocalPlayerId > 0 && game.State != null && game.SnapshotsReceived >= 2 && game.SessionStarted);
            int id = game.LocalPlayerId; report.localPlayerId = id;
            Player player = game.State.players.Find(p => p.id == id);
            Require("host snapshot includes both actual peers", player != null && game.State.players.Count(p => p.connected) == 2);
            float startX = player.x, startZ = player.z;
            Require("client queues production movement command", game.Send(new Command { kind = "move", x = 1, z = 0 }, false).accepted);
            deadline = Time.realtimeSinceStartup + 10;
            while (Time.realtimeSinceStartup < deadline)
            {
                player = game.State.players.Find(p => p.id == id);
                if (Simulation.Distance(startX, startZ, player.x, player.z) > 1.2f) break;
                yield return null;
            }
            Require("client sees host-authoritative movement replication", Simulation.Distance(startX, startZ, player.x, player.z) > 1.2f);
            Require("client queues production stop command", game.Send(new Command { kind = "move" }, false).accepted);
            Require("client queues production interaction command", game.Send(new Command { kind = "interact" }, false).accepted);
            deadline = Time.realtimeSinceStartup + 10;
            while (game.State.players.Find(p => p.id == id).lastSequence < 3 && Time.realtimeSinceStartup < deadline) yield return null;
            Require("snapshot acknowledges consumed interaction sequence", game.State.players.Find(p => p.id == id).lastSequence >= 3);
            game.OpenPanel("pause"); int snapshots = game.SnapshotsReceived; float elapsed = game.State.elapsed;
            yield return new WaitForSecondsRealtime(.4f);
            Require("client LAN pause panel keeps receiving a running host", game.ScreenName == "pause" && game.State.phase == "active" && game.SnapshotsReceived > snapshots && game.State.elapsed > elapsed);
            game.Resume(); game.UI.RefreshHUD(); yield return Capture("lan-client", true);
            // This is an explicit invalid-command fixture, not an alternate state-setting API.
            // WireLan's whitelist must close this peer before the core can accept it.
            yield return new WaitForSecondsRealtime(2);
            Require("invalid client fixture is sent through the real transport", game.Send(new Command { kind = "qa-forbidden-resource-grant" }, false).accepted);
            deadline = Time.realtimeSinceStartup + 10;
            while (game.IsNetworked && game.LocalPlayerId >= 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Require("client observes host rejection and disconnect", !game.IsNetworked || game.LocalPlayerId < 0);
            report.snapshotsReceived = game.SnapshotsReceived; report.commandCount = 4;
        }

        string CaptureFile(string kind, bool primary)
        {
            if (capturePath == "") return "";
            if (primary) return capturePath;
            return Path.Combine(Path.GetDirectoryName(capturePath), Path.GetFileNameWithoutExtension(capturePath) + "-" + kind + ".png");
        }

        void CheckGameplayHud(string moment)
        {
            string[] modes = { "ashfall-county", "dead-orbit", "skyhold-siege", "rift-runners", "thornvale", "havenfall-coalition", "emberwake-rebuilders", "fjordbound", "gatebreakers-guild", "market-day", "hearth-rush", "stormline-express" };
            string[] words = { "Neighbours rescued", "Breaches sealed", "Tower height", "Rivals defeated", "Shrines restored", "Residents rescued", "Homes rebuilt", "Plunder banked", "Invaders remaining", "Customers served", "Dishes served", "Passengers rescued" };
            int index = Array.IndexOf(modes, game.Profile.id);
            var progress = VisibleText(text => text.name == "Progress");
            var context = VisibleText(text => text.name == "Context");
            var bindings = VisibleText(text => text.name == "Bindings");
            var inventory = VisibleText(text => text.name == "Resources");
            Require("native HUD exposes genre-specific live progress: " + moment, index >= 0 && progress != null && progress.text.Contains(words[index]));
            if (game.Profile.id != "skyhold-siege" && game.Profile.id != "gatebreakers-guild")
                Require("native HUD progress reflects authoritative counters: " + moment, progress.text.Contains(game.State.progress + " / " + game.State.goal));
            Require("native progress stays visible alongside interaction context: " + moment, context != null && progress.gameObject.activeInHierarchy && context.gameObject.activeInHierarchy);
            Require("native chapter timer stays visible: " + moment, VisibleText(text => text.name == "ChapterClock" && text.text.Contains("Chapter " + game.State.chapter + " / 3") && text.text.Contains(":")) != null);
            Require("native HUD uses the current saved interaction binding: " + moment, bindings != null && bindings.text.Contains(game.Gameplay.FindAction("Interact").GetBindingDisplayString(0) + " / South interact"));
            if (game.Profile.id == "market-day" || game.Profile.id == "hearth-rush")
                Require("service HUD shows hands instead of combat supplies: " + moment, inventory != null && !inventory.text.Contains("Ammo") && !inventory.text.Contains("Timber") && !bindings.text.Contains("attack") && !bindings.text.Contains("build") && !bindings.text.Contains("craft"));
            foreach (string name in new[] { "Title", "Objective", "Progress", "ChapterClock", "Vitals", "Resources", "Context", "Situation", "Bindings" })
            {
                var label = VisibleText(text => text.name == name);
                Require("native HUD label is visible: " + name + " / " + moment, label != null && !string.IsNullOrEmpty(label.text));
                label.ForceMeshUpdate();
                var corners = new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
                bool inside = corners.All(corner => corner.x >= -.5f && corner.y >= -.5f && corner.x <= Screen.width + .5f && corner.y <= Screen.height + .5f);
                Require("native HUD label fits its viewport: " + name + " / " + moment, inside && !label.isTextOverflowing, label.text);
            }
        }

        IEnumerator Capture(string kind, bool primary = false)
        {
            string path = CaptureFile(kind, primary); if (path == "") yield break;
            game.UI.RefreshHUD(); yield return new WaitForEndOfFrame();
            if(game.ScreenName=="play")CheckGameplayHud(kind);
            DateTime old = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path, 1);
            float end = Time.realtimeSinceStartup + 12; long previous = -1; int stable = 0;
            while (Time.realtimeSinceStartup < end)
            {
                long size = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (size > 128 && File.GetLastWriteTimeUtc(path) > old && size == previous) stable++; else stable = 0;
                if (stable >= 2) break;
                previous = size; yield return null;
            }
            Require("native screenshot was written: " + kind, stable >= 2);
            byte[] bytes = File.ReadAllBytes(path);
            Require("native screenshot is a complete PNG: " + kind, bytes.Length > 128 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71);
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            Require("native screenshot has rendered dimensions: " + kind, width >= 320 && height >= 200);
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            report.captures.Add(new CaptureRecord { kind = kind, path = path, sha256 = hash, chapter = game.State == null ? 0 : game.State.chapter, tick = game.State == null ? 0 : game.State.tick, width = width, height = height });
        }

        IEnumerator Finish()
        {
            if (finishing) yield break; finishing = true;
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad); pad = null;
            // Let screenshots, UI and root progression settle before writing the final report.
            yield return null;
            report.wallSeconds = clock.Elapsed.TotalSeconds;
            report.snapshotsReceived = game.SnapshotsReceived;
            if (driver != null) { report.commandCount = driver.CommandCount; report.completedChapters = driver.CompletedChapters; }
            lock (logGate) report.status = report.failure == "" && report.checks.All(c => c.passed) && report.errors.Count == 0 ? "passed" : "failed";
            try
            {
                if (reportPath != "") File.WriteAllText(reportPath, JsonUtility.ToJson(report, true) + "\n");
                Debug.Log("[FrontierQA] " + report.gameId + " " + report.mode + " " + report.status + " / " + report.checks.Count + " checks / " + report.completedChapters + " chapters");
            }
            catch (Exception ex) { Debug.LogError("[FrontierQA] Could not write report: " + ex); report.status = "failed"; }
            Application.Quit(report.status == "passed" ? 0 : 2);
        }

        void OnDestroy() { Application.logMessageReceivedThreaded -= OnLog; if (pad != null && pad.added) InputSystem.RemoveDevice(pad); }
    }
}
