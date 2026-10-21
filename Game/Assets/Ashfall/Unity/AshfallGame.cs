using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Ashfall.View
{
    [Serializable]
    public sealed class Preferences
    {
        public float master = .8f, music = .5f, effects = .9f, uiScale = 1, zoom = 13;
        public bool fullscreen, vsync = true, showTips = true;
        public string bindings = "";
        public string lastName = "Mara Vale", lastProfession = "paramedic";
        public bool lastFemale = true;
        public string lastTraits = "";   // comma-separated trait ids from the last New Game
    }

    /// <summary>Top-level screens. Evacuated is kept only for pre-refactor saves (shown as a neutral ending).</summary>
    public enum Screen { Title, NewGame, Load, Loading, Playing, Dead, Evacuated, DropMap, Legacy }

    /// <summary>The county layout generated for the New Game drop map: landing zones, landmarks and a rendered overview.</summary>
    public sealed class DropPreview
    {
        public int seed, size, mapWidth, mapHeight, texWidth, texHeight;
        public WorldGen gen; public Color32[] pixels;
    }

    public sealed class ContextAction { public string label; public Action run; public bool enabled = true; public string reason = ""; }
    public sealed class Interactable { public string label; public Vector3 pos; public float score; public readonly List<ContextAction> actions = new List<ContextAction>(); }

    /// <summary>Root of the native game: owns the host simulation, presentation, input and saves.</summary>
    public sealed partial class AshfallGame : MonoBehaviour
    {
        public static AshfallGame Instance { get; private set; }
        public World World { get; private set; }
        public Simulation Sim { get; private set; }
        public WorldView View { get; private set; }
        public AshfallUI UI { get; private set; }
        public AshfallAudio Audio { get; private set; }
        public Preferences Prefs { get; private set; }
        public Screen Screen { get; private set; }
        public bool Paused { get; set; }
        /// <summary>The controlled community member (World.playerId; it changes when the player switches or the member dies).</summary>
        public Survivor Player { get { return World == null ? null : World.Controlled; } }
        /// <summary>The community member the local owner controls (switching members changes it).</summary>
        public int LocalId { get { return World == null ? 0 : World.playerId; } }
        public Camera Cam { get; private set; }
        public string SaveDirectory { get; private set; }
        public string CurrentSave { get; private set; }
        public string LoadingText { get; private set; }
        public Interactable Focus { get; private set; }
        public readonly List<Interactable> Nearby = new List<Interactable>();
        public InputActionMap Gameplay { get; private set; }
        public bool QaActive { get; private set; }
        public float Zoom { get { return zoom; } }
        public string Notice = "";
        public float SimMs, ViewMs, UiMs;   // smoothed per-frame costs for diagnostics
        readonly System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
        static float Smooth(float a, float b) { return a * .9f + b * .1f; }
        public readonly List<string> Radio = new List<string>();

        InputAction move, look, run, sneak, aim, attack, shove, interact, context, inventory, community, mapKey, missions, companion, throwKey, pause, reload, light, zoomAxis, cancel;
        Light sun, playerGlow, flashlight;
        ParticleSystem rain;
        ActorView playerView;
        readonly Dictionary<int, ActorView> zombieViews = new Dictionary<int, ActorView>();
        readonly Dictionary<int, ActorView> corpseViews = new Dictionary<int, ActorView>();
        readonly Stack<ActorView> zombiePool = new Stack<ActorView>();
        readonly Dictionary<int, float> lastSeen = new Dictionary<int, float>();
        Task<World> loading;
        Action<World> loaded;
        float zoom = 13, interactClock, autosaveClock, shake, orbit, throwHeld = -1, loadingStarted;
        int lastControlled = -1;
        Vector3 camFocus;
        bool usingGamepadAim;
        int hotbarIndex;
        World backdrop; WorldView backdropView;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (FindAnyObjectByType<AshfallGame>() == null) new GameObject("Ashfall County").AddComponent<AshfallGame>(); }

        public static string Arg(string key)
        {
            string prefix = "-" + key + "=";
            return Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length) ?? "";
        }

        void Awake()
        {
            Instance = this; DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 120;
            string userDir = Arg("ashfall-user-dir");
            QaActive = !string.IsNullOrEmpty(Arg("ashfall-qa"));
            if (QaActive && (string.IsNullOrEmpty(userDir) || userDir.IndexOf("GameWorkbenchQA", StringComparison.Ordinal) < 0))
                throw new InvalidOperationException("Native diagnostics require an isolated GameWorkbenchQA user directory.");
            string baseDir = string.IsNullOrEmpty(userDir) ? Application.persistentDataPath : userDir;
            SaveDirectory = Path.Combine(baseDir, "Survival"); Directory.CreateDirectory(SaveDirectory);
            Prefs = LoadPrefs(baseDir);
            zoom = Prefs.zoom;
            ApplyDisplay();
            ArtLibrary.Load();
            SetupScene();
            ConfigureInput();
            Audio = gameObject.AddComponent<AshfallAudio>(); Audio.Initialize(this);
            UI = new GameObject("Ashfall UI").AddComponent<AshfallUI>(); UI.transform.SetParent(transform, false); UI.Initialize(this);
            BuildBackdrop();
            Show(Screen.Title);
            if (QaActive) gameObject.AddComponent<AshfallQA>().Initialize(this, Arg("ashfall-qa"));
        }

        // ---------------------------------------------------------------- preferences
        string prefsPath;
        Preferences LoadPrefs(string dir)
        {
            prefsPath = Path.Combine(dir, "ashfall-preferences.json");
            try { if (File.Exists(prefsPath)) return JsonUtility.FromJson<Preferences>(File.ReadAllText(prefsPath)) ?? new Preferences(); }
            catch (Exception) { Notice = "Preferences could not be read; defaults restored."; }
            return new Preferences();
        }
        public void SavePrefs()
        {
            try { Prefs.zoom = zoom; if (Gameplay != null) Prefs.bindings = Gameplay.SaveBindingOverridesAsJson(); File.WriteAllText(prefsPath + ".tmp", JsonUtility.ToJson(Prefs, true)); if (File.Exists(prefsPath)) File.Delete(prefsPath); File.Move(prefsPath + ".tmp", prefsPath); }
            catch (Exception e) { Debug.LogWarning("Preferences not saved: " + e.Message); }
        }
        public void ApplyDisplay()
        {
            QualitySettings.vSyncCount = Prefs.vsync ? 1 : 0;
            if (!QaActive) UnityEngine.Screen.fullScreenMode = Prefs.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            AudioListener.volume = Prefs.master;
        }

        // ---------------------------------------------------------------- scene
        void SetupScene()
        {
            var camGo = new GameObject("Camera"); camGo.transform.SetParent(transform, false);
            Cam = camGo.AddComponent<Camera>(); Cam.orthographic = true; Cam.orthographicSize = zoom; Cam.nearClipPlane = .1f; Cam.farClipPlane = 400;
            Cam.clearFlags = CameraClearFlags.SolidColor; Cam.backgroundColor = new Color(.06f, .07f, .08f);
            camGo.AddComponent<AudioListener>(); camGo.tag = "MainCamera";
            var sunGo = new GameObject("Sun"); sunGo.transform.SetParent(transform, false); sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = .65f;
            var glow = new GameObject("Survivor glow"); glow.transform.SetParent(transform, false); playerGlow = glow.AddComponent<Light>(); playerGlow.type = LightType.Point; playerGlow.range = 7; playerGlow.color = new Color(.6f, .66f, .8f); playerGlow.shadows = LightShadows.None;
            var fl = new GameObject("Flashlight"); fl.transform.SetParent(transform, false); flashlight = fl.AddComponent<Light>(); flashlight.type = LightType.Spot; flashlight.range = 18; flashlight.spotAngle = 55; flashlight.intensity = 2.4f; flashlight.color = new Color(1, .95f, .82f); flashlight.shadows = LightShadows.Soft; flashlight.enabled = false;
            QualitySettings.shadowDistance = 70; QualitySettings.shadowCascades = 2;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 70; RenderSettings.fogEndDistance = 160;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            var es = new GameObject("EventSystem"); es.transform.SetParent(transform, false); es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>();
            rain = BuildRain();
        }

        ParticleSystem BuildRain()
        {
            var go = new GameObject("Rain"); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop();
            var main = ps.main; main.startLifetime = .7f; main.startSpeed = 26; main.startSize = .06f; main.maxParticles = 4000; main.startColor = new Color(.7f, .78f, .9f, .45f); main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(60, 1, 60);
            var emission = ps.emission; emission.rateOverTime = 0;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 4; r.material = new Material(Shader.Find("Sprites/Default"));
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            return ps;
        }

        void BuildBackdrop()
        {
            // A small live county behind the title menu, generated from a fixed seed.
            backdrop = World.Create(new SandboxSettings { seed = 1031, mapSize = 640, population = Population.High }, "", "unemployed", true);
            backdropView = new GameObject("Backdrop").AddComponent<WorldView>(); backdropView.transform.SetParent(transform, false);
            backdropView.LoadRadius = 2; backdropView.Initialize(backdrop);
            var town = backdrop.gen.settlements[0];
            camFocus = new Vector3(town.x, 0, town.y);
            for (int i = 0; i < 40; i++) backdropView.Refresh(camFocus, -CameraForward(), 4);
            // Idle zombies dot the streets.
            var list = new List<Zombie>(); backdrop.index.Query(new Vec2(town.x, town.y), 50, list);
            foreach (var z in list.Take(60)) { var a = ActorView.Create(backdropView.transform, true, z.variant, z.female, "zombie"); a.Snap(new Vector3(z.pos.x, 0, z.pos.y), z.facing * Mathf.Rad2Deg); backdropActors.Add(a); backdropZombies.Add(z); }
        }
        readonly List<ActorView> backdropActors = new List<ActorView>(); readonly List<Zombie> backdropZombies = new List<Zombie>();
        void DestroyBackdrop()
        {
            if (backdropView == null) return;
            backdropView.Clear(); Destroy(backdropView.gameObject); backdropView = null; backdrop = null; backdropActors.Clear(); backdropZombies.Clear();
        }

        public void Show(Screen s)
        {
            Screen = s; Paused = false;
            if (s == Screen.Title && backdropView == null && World == null) BuildBackdrop();
            UI.ShowScreen(s);
        }

        // ---------------------------------------------------------------- input
        void ConfigureInput()
        {
            Gameplay = new InputActionMap("Gameplay");
            move = Gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            look = Gameplay.AddAction("Aim Stick", InputActionType.Value, "<Gamepad>/rightStick");
            run = Gameplay.AddAction("Run", InputActionType.Button, "<Keyboard>/leftShift"); run.AddBinding("<Gamepad>/leftStickPress");
            sneak = Gameplay.AddAction("Sneak", InputActionType.Button, "<Keyboard>/leftCtrl"); sneak.AddBinding("<Gamepad>/rightStickPress");
            aim = Gameplay.AddAction("Aim", InputActionType.Button, "<Mouse>/rightButton"); aim.AddBinding("<Gamepad>/leftTrigger");
            attack = Gameplay.AddAction("Attack", InputActionType.Button, "<Mouse>/leftButton"); attack.AddBinding("<Gamepad>/rightTrigger");
            shove = Gameplay.AddAction("Shove", InputActionType.Button, "<Keyboard>/space"); shove.AddBinding("<Gamepad>/leftShoulder");
            interact = Gameplay.AddAction("Interact", InputActionType.Button, "<Keyboard>/e"); interact.AddBinding("<Gamepad>/buttonSouth");
            context = Gameplay.AddAction("Actions Menu", InputActionType.Button, "<Keyboard>/q"); context.AddBinding("<Gamepad>/buttonNorth");
            // RB: tap cycles weapons, hold aims a throw and releases it (Docs/DESIGN.md controls).
            throwKey = Gameplay.AddAction("Throw", InputActionType.Button, "<Keyboard>/g"); throwKey.AddBinding("<Gamepad>/rightShoulder");
            companion = Gameplay.AddAction("Companion Order", InputActionType.Button, "<Keyboard>/c"); companion.AddBinding("<Gamepad>/dpad/up");
            inventory = Gameplay.AddAction("Inventory", InputActionType.Button, "<Keyboard>/tab"); inventory.AddBinding("<Keyboard>/i"); inventory.AddBinding("<Gamepad>/select");
            community = Gameplay.AddAction("Community", InputActionType.Button, "<Keyboard>/k"); community.AddBinding("<Gamepad>/dpad/left");
            mapKey = Gameplay.AddAction("Map", InputActionType.Button, "<Keyboard>/m"); mapKey.AddBinding("<Gamepad>/dpad/down");
            missions = Gameplay.AddAction("Missions", InputActionType.Button, "<Keyboard>/j"); missions.AddBinding("<Gamepad>/dpad/right");
            pause = Gameplay.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape"); pause.AddBinding("<Gamepad>/start");
            reload = Gameplay.AddAction("Reload", InputActionType.Button, "<Keyboard>/r"); reload.AddBinding("<Gamepad>/buttonWest");
            light = Gameplay.AddAction("Flashlight", InputActionType.Button, "<Keyboard>/f");
            cancel = Gameplay.AddAction("Back", InputActionType.Button, "<Gamepad>/buttonEast");
            zoomAxis = Gameplay.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            // Deterministic binding ids so saved overrides survive restarts; the salt changes whenever the binding set does (v2: open-county controls).
            using (var hash = SHA256.Create())
                foreach (var action in Gameplay.actions)
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        var b = action.bindings[i];
                        var digest = hash.ComputeHash(Encoding.UTF8.GetBytes("AshfallInput/v2/" + action.name + "/" + b.name + "/" + b.path));
                        b.id = new Guid(digest.Take(16).ToArray()); action.ChangeBinding(i).To(b);
                    }
            if (!string.IsNullOrEmpty(Prefs.bindings)) try { Gameplay.LoadBindingOverridesFromJson(Prefs.bindings); } catch (Exception) { Notice = "Saved key bindings could not be read; defaults restored."; }
            Gameplay.Enable();
        }
        public IEnumerable<InputAction> RebindableActions { get { return new[] { run, sneak, aim, attack, shove, interact, context, throwKey, companion, inventory, community, mapKey, missions, reload, light }; } }
        public bool PausePressed { get { return pause.WasPressedThisFrame(); } }
        public bool BackPressed { get { return cancel.WasPressedThisFrame(); } }
        public bool InventoryPressed { get { return inventory.WasPressedThisFrame(); } }

        // ---------------------------------------------------------------- new game, load, save
        public void StartNewGame(SandboxSettings settings, string name, string profession, bool female, string[] traits = null)
        {
            traits = traits ?? new string[0];
            Prefs.lastName = name; Prefs.lastProfession = profession; Prefs.lastFemale = female; Prefs.lastTraits = string.Join(",", traits); SavePrefs();
            CancelDropMap();
            var p = Preview; bool known = p != null && p.seed == settings.seed && p.size == settings.mapSize && settings.landingZone >= 0 && settings.landingZone < p.gen.landingZones.Count;
            string where = known ? "Dropping into " + p.gen.landingZones[settings.landingZone].name : "Dropping into Ashfall County";
            Preview = null;
            BeginLoading(where + " · generating " + settings.mapSize + " × " + settings.mapSize + " m", AfterSurvey(() => World.Create(settings, name, profession, female, traits)),
                w => { CurrentSave = Path.Combine(SaveDirectory, Slug(name) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sav"); Radio.Clear(); Enter(w); SaveNow(); });
        }

        // ---------------------------------------------------------------- drop map
        /// <summary>The county layout for the drop map, once generated (null while generating).</summary>
        public DropPreview Preview { get; private set; }
        public bool PreviewPending { get { return previewTask != null; } }
        public float PreviewSeconds { get { return previewTask == null ? 0 : Time.realtimeSinceStartup - previewStarted; } }
        Task<DropPreview> previewTask; float previewStarted; int wantSeed, wantSize;
        /// <summary>Generation runs on worker threads; a new session waits for any survey still running so the two never overlap.</summary>
        Task<World> AfterSurvey(Func<World> work)
        {
            var survey = previewTask;
            return Task.Run(() => { if (survey != null) try { survey.Wait(); } catch (Exception) { } return work(); });
        }

        /// <summary>Generates the county layout in the background (same seed and size World.Create will use) and opens the drop map.</summary>
        public void OpenDropMap(SandboxSettings settings)
        {
            int seed = settings.seed, size = settings.mapSize; wantSeed = seed; wantSize = size;
            if ((Preview == null || Preview.seed != seed || Preview.size != size) && previewTask == null) StartSurvey(seed, size);
            else if (previewTask != null) Preview = null;   // a survey for other settings is running: PollPreview starts the right one after it
            Show(Screen.DropMap);
        }
        void StartSurvey(int seed, int size)
        {
            {
                Preview = null; previewStarted = Time.realtimeSinceStartup;
                previewTask = Task.Run(() =>
                {
                    WorldGen gen; var map = WorldGen.Generate(seed, size, out gen);
                    var d = new DropPreview { seed = seed, size = size, gen = gen, mapWidth = map.width, mapHeight = map.height };
                    d.pixels = UiMapRender.Render(map, 1024, out d.texWidth, out d.texHeight);
                    return d;   // the Map itself is released here: World.Create regenerates it deterministically
                });
            }
        }
        void CancelDropMap() { wantSeed = wantSize = 0; }
        void PollPreview()
        {
            if (previewTask == null || !previewTask.IsCompleted) return;
            var task = previewTask; previewTask = null;
            if (task.IsFaulted) { Notice = "Could not survey the county: " + task.Exception.GetBaseException().Message; Debug.LogError(task.Exception); if (Screen == Screen.DropMap) Show(Screen.NewGame); return; }
            if (wantSize == 0) return;   // the player left the drop map
            if (task.Result.seed != wantSeed || task.Result.size != wantSize) { StartSurvey(wantSeed, wantSize); return; }
            Preview = task.Result; UI.PreviewReady();
        }
        public void LoadGame(string path)
        {
            BeginLoading("Restoring " + Path.GetFileNameWithoutExtension(path) + "...", AfterSurvey(() => SaveCodec.Load(path)), w => { CurrentSave = path; Radio.Clear(); Enter(w); });
        }
        void BeginLoading(string text, Task<World> task, Action<World> done)
        {
            LoadingText = text; loading = task; loaded = done; loadingStarted = Time.realtimeSinceStartup; Show(Screen.Loading);
        }
        static string Slug(string name) { var s = new string((name ?? "survivor").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-'); return s.Length == 0 ? "survivor" : s; }

        public float LoadingSeconds { get { return loading == null ? 0 : Time.realtimeSinceStartup - loadingStarted; } }

        void Enter(World w)
        {
            DestroyBackdrop(); ClearSession();
            World = w; Sim = new Simulation(w); lastControlled = w.playerId;
            View = new GameObject("County View").AddComponent<WorldView>(); View.transform.SetParent(transform, false); View.Initialize(w);
            var s = Player;
            BeginPresentation(w);   // AshfallGame.Present.cs
            camFocus = Pos(s.pos);
            for (int i = 0; i < 12; i++) View.Refresh(camFocus, -CameraForward(), 6);
            autosaveClock = 0;
            UI.BeginSession(w);
            Show(w.outcome == "dead" || CommunityGone ? Screen.Dead : Screen.Playing);
            Debug.Log("ASHFALL_SESSION seed=" + w.settings.seed + " size=" + w.settings.mapSize + " zombies=" + w.LivingZombies + " buildings=" + w.map.buildings.Count);
        }

        void ClearSession()
        {
            if (View != null) { View.Clear(); Destroy(View.gameObject); View = null; }
            ClearPresentation();    // AshfallGame.Present.cs
            World = null; Sim = null; Focus = null; Nearby.Clear(); lastSeen.Clear(); lastControlled = -1; throwHeld = -1;
        }

        public bool SaveNow()
        {
            if (World == null || string.IsNullOrEmpty(CurrentSave)) return false;
            try { SaveCodec.Save(World, CurrentSave); UI.Toast("Game saved."); return true; }
            catch (Exception e) { UI.Toast("Save failed: " + e.Message); Debug.LogError("Save failed: " + e); return false; }
        }
        /// <summary>True when no member of the player's community is alive (the session is over).</summary>
        public bool CommunityGone
        {
            get { if (World == null) return false; foreach (var m in World.survivors) if (m.faction == 0 && m.alive) return false; return true; }
        }
        public void QuitToTitle(bool save)
        {
            if (save && World != null && World.outcome != "dead") SaveNow();
            ClearSession(); Show(Screen.Title);
        }
        public IEnumerable<string> SaveFiles() { return Directory.Exists(SaveDirectory) ? Directory.GetFiles(SaveDirectory, "*.sav").OrderByDescending(File.GetLastWriteTimeUtc) : Enumerable.Empty<string>(); }
        public void DeleteSave(string path) { if (File.Exists(path) && Path.GetDirectoryName(Path.GetFullPath(path)) == Path.GetFullPath(SaveDirectory)) File.Delete(path); }

        void OnApplicationQuit() { if (World != null && Screen == Screen.Playing && World.outcome != "dead" && !QaActive) SaveNow(); SavePrefs(); }

        // ---------------------------------------------------------------- frame loop
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (Screen == Screen.Loading && loading != null && loading.IsCompleted)
            {
                var task = loading; loading = null;
                if (task.IsFaulted) { Notice = "Could not start: " + task.Exception.GetBaseException().Message; Debug.LogError(task.Exception); Show(Screen.Title); }
                else loaded(task.Result);
            }
            PollPreview();
            if (backdropView != null && World == null) UpdateBackdrop(dt);
            if (World == null) { UI.Tick(); return; }
            if (Screen == Screen.Playing)
            {
                if (PausePressed && !UI.CloseTopPanel()) { Paused = !Paused; UI.SetPause(Paused); }
                if (!Paused)
                {
                    PlayerInput(dt);
                    timer.Restart(); Sim.Advance(Time.deltaTime); SimMs = Smooth(SimMs, (float)timer.Elapsed.TotalMilliseconds);
                    Events();
                    autosaveClock += dt; if (autosaveClock > 300 && !QaActive) { autosaveClock = 0; SaveNow(); }
                }
                WatchControl();
                if (World.outcome == "dead" || CommunityGone) { if (Screen != Screen.Dead) { var p = Player; if (p != null) Audio.Play("death", p.pos); if (!QaActive) SaveNow(); Show(Screen.Dead); } }
                else if (World.outcome == "legacy" && World.flags.Add("legacy-shown")) { if (!QaActive) SaveNow(); Show(Screen.Legacy); }
                else if (World.outcome == "evacuated" && World.flags.Add("evac-shown")) { if (!QaActive) SaveNow(); Show(Screen.Evacuated); }
            }
            timer.Restart(); Present(dt); ViewMs = Smooth(ViewMs, (float)timer.Elapsed.TotalMilliseconds);
            timer.Restart(); UI.Tick(); UiMs = Smooth(UiMs, (float)timer.Elapsed.TotalMilliseconds);
        }

        void UpdateBackdrop(float dt)
        {
            orbit += dt * 2.5f;
            var town = backdrop.gen.settlements[0];
            camFocus = new Vector3(town.x + Mathf.Sin(orbit * .02f) * 18, 0, town.y + Mathf.Cos(orbit * .017f) * 14);
            PlaceCamera(camFocus, 15, orbit);
            backdropView.Refresh(camFocus, -CameraForward(), 1);
            for (int i = 0; i < backdropActors.Count; i++)
            {
                var z = backdropZombies[i]; var a = backdropActors[i];
                var drift = new Vector3(Mathf.Sin(Time.time * .2f + i), 0, Mathf.Cos(Time.time * .17f + i * 1.3f)) * .5f;
                a.Pose(new Vector3(z.pos.x, 0, z.pos.y) + drift, z.facing * Mathf.Rad2Deg + Mathf.Sin(Time.time * .3f + i) * 30, .4f, false, false, false, false, dt);
            }
            Lighting(.55f, false, Vector3.zero);
        }

        /// <summary>Notices when control passes to another member (a switch at base, or the controlled member dying).</summary>
        void WatchControl()
        {
            int id = World.playerId;
            if (id == lastControlled) return;
            var old = lastControlled >= 0 && lastControlled < World.survivors.Count ? World.survivors[lastControlled] : null;
            lastControlled = id; Focus = null; Nearby.Clear(); throwHeld = -1;
            UI.ControlChanged(old, Player);
        }

        public static Vector3 Pos(Vec2 p, float y = 0) { return new Vector3(p.x, y, p.y); }
        public static float Yaw(float facing) { return 90 - facing * Mathf.Rad2Deg; }
        float yawOffset;
        Vector3 CameraForward() { return Quaternion.Euler(35, 45 + yawOffset, 0) * Vector3.forward; }
        void PlaceCamera(Vector3 focus, float size, float spin = 0)
        {
            var rot = Quaternion.Euler(35, 45 + yawOffset + spin, 0);
            var shakeOffset = shake > 0 ? UnityEngine.Random.insideUnitSphere * shake * .15f : Vector3.zero;
            Cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * 120 + shakeOffset, rot);
            Cam.orthographicSize = Mathf.Lerp(Cam.orthographicSize, size, .12f);
        }

        void PlayerInput(float dt)
        {
            var s = Player; if (s == null || !s.alive) return;
            bool ui = UI.BlocksGameplay;
            var input = new InputState { face = s.facing };
            if (!ui)
            {
                var m = move.ReadValue<Vector2>();
                // Screen-relative movement on the isometric view.
                var fwd = Cam.transform.forward; fwd.y = 0; fwd.Normalize(); var right = Cam.transform.right; right.y = 0; right.Normalize();
                var w = fwd * m.y + right * m.x;
                input.moveX = w.x; input.moveY = w.z;
                input.run = run.IsPressed(); input.sneak = sneak.IsPressed(); input.aim = aim.IsPressed();
                bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                input.attack = attack.IsPressed() && !pointerOverUi; input.shove = shove.WasPressedThisFrame();
                var stick = look.ReadValue<Vector2>();
                if (stick.sqrMagnitude > .2f) usingGamepadAim = true;
                else if (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 1) usingGamepadAim = false;
                if (usingGamepadAim)
                {
                    if (stick.sqrMagnitude > .2f) { var d = fwd * stick.y + right * stick.x; input.face = Mathf.Atan2(d.z, d.x); }
                    else if (w.sqrMagnitude > .05f) input.face = Mathf.Atan2(w.z, w.x);
                }
                else if (Mouse.current != null)
                {
                    var ray = Cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                    var plane = new Plane(Vector3.up, new Vector3(0, .9f, 0)); float t;
                    if (plane.Raycast(ray, out t)) { var p = ray.GetPoint(t); var d = new Vector2(p.x - s.pos.x, p.z - s.pos.y); if (d.sqrMagnitude > .04f) input.face = Mathf.Atan2(d.y, d.x); }
                }
                float scroll = zoomAxis.ReadValue<float>();
                // The county is large: the camera pulls out further than the old town view.
                if (Mathf.Abs(scroll) > .01f && !pointerOverUi) zoom = Mathf.Clamp(zoom - Mathf.Sign(scroll) * 1.5f, 6, 36);
                if (interact.WasPressedThisFrame()) DoPrimary();
                if (context.WasPressedThisFrame()) UI.OpenContext();
                if (reload.WasPressedThisFrame()) Sim.Submit(s.id, Command.Of(CommandKind.Reload));
                if (light.WasPressedThisFrame()) Sim.Submit(s.id, Command.Of(CommandKind.ToggleLight));
                if (companion.WasPressedThisFrame()) CycleCompanionOrder();
                ThrowInput(s);
                if (Keyboard.current != null)
                {
                    for (int i = 0; i < 6; i++) if (Keyboard.current[Key.Digit1 + i].wasPressedThisFrame) UseHotbar(i);
                    if (Keyboard.current.zKey.wasPressedThisFrame) yawOffset -= 90; if (Keyboard.current.xKey.wasPressedThisFrame) yawOffset += 90;
                }
                if (s.sleeping && (interact.WasPressedThisFrame() || cancel.WasPressedThisFrame())) Sim.Submit(s.id, Command.Of(CommandKind.Wake));
            }
            if (!UI.TextEntryActive)
            {
                if (inventory.WasPressedThisFrame()) UI.Toggle("inventory");
                if (community.WasPressedThisFrame()) UI.Toggle("community");
                if (mapKey.WasPressedThisFrame()) UI.Toggle("map");
                if (missions.WasPressedThisFrame()) UI.Toggle("missions");
                if (cancel.WasPressedThisFrame()) UI.CloseTopPanel();
            }
            // Driving: the stick/WASD become throttle (y) and steering (x); Space brakes, R toggles the engine.
            if (World.VehicleOf(s) != null)
            {
                input = new InputState { face = s.facing };
                if (!ui) { var m = move.ReadValue<Vector2>(); input.moveX = m.x; input.moveY = m.y; input.shove = shove.IsPressed(); if (reload.WasPressedThisFrame()) Sim.Submit(s.id, Command.Of(CommandKind.ToggleEngine)); }
            }
            Sim.SetInput(s.id, input);
            interactClock -= dt; if (interactClock <= 0) { interactClock = .1f; GatherInteractions(); }
        }

        public List<ItemStack> Hotbar()
        {
            var s = Player; if (s == null) return new List<ItemStack>();
            return s.inventory.items.Where(i => i.Def.IsWeapon && i.Def.category != ItemCategory.Material || i.Def.category == ItemCategory.Light).OrderBy(i => i.Def.category == ItemCategory.Light ? 1 : 0).ThenByDescending(i => i.Def.damage).Take(6).ToList();
        }
        void UseHotbar(int i) { var bar = Hotbar(); if (i < bar.Count) Sim.Submit(LocalId, new Command { kind = bar[i].Def.category == ItemCategory.Light ? CommandKind.ToggleLight : CommandKind.Equip, item = bar[i].uid, slot = 0 }); }
        /// <summary>G throws at the mouse ground point. RB tapped cycles weapons; RB held aims about 12 m ahead and throws on release.</summary>
        void ThrowInput(Survivor s)
        {
            if (throwKey.WasPressedThisFrame())
            {
                bool pad = throwKey.activeControl != null && throwKey.activeControl.device is Gamepad;
                if (pad) throwHeld = 0; else ThrowAt(s, MouseGround());
            }
            if (throwHeld < 0) return;
            throwHeld += Time.unscaledDeltaTime;
            if (!throwKey.IsPressed())
            {
                if (throwHeld < .3f) CycleWeapon();
                else ThrowAt(s, ThrowAimPoint);
                throwHeld = -1;
            }
        }
        /// <summary>True while a controller throw is being aimed (the HUD shows the landing point).</summary>
        public bool ThrowAiming { get { return throwHeld >= .3f; } }
        public Vector3 ThrowAimPoint { get { var s = Player; if (s == null) return Vector3.zero; return Pos(s.pos) + new Vector3(Mathf.Cos(s.facing), 0, Mathf.Sin(s.facing)) * 12; } }
        Vector3 MouseGround()
        {
            if (Mouse.current == null) return ThrowAimPoint;
            var ray = Cam.ScreenPointToRay(Mouse.current.position.ReadValue()); float t;
            return new Plane(Vector3.up, Vector3.zero).Raycast(ray, out t) ? ray.GetPoint(t) : ThrowAimPoint;
        }
        /// <summary>The first throwable carried (grenade, molotov, pipe bomb), or null.</summary>
        public ItemStack Throwable()
        {
            var s = Player; if (s == null) return null;
            foreach (var i in s.inventory.items) if (i.Def.Has("throwable") || i.Def.blastRadius > 0) return i;
            return null;
        }
        public void ThrowAt(Survivor s, Vector3 at)
        {
            var item = Throwable();
            if (item == null) { UI.Toast("Nothing to throw. Grenades, molotovs and pipe bombs turn up in military loot and airdrops."); Audio.Ui("deny"); return; }
            Sim.Submit(s.id, new Command { kind = CommandKind.Throw, item = item.uid, x = at.x, y = at.z, recipe = "" });
        }
        /// <summary>C / d-pad up: the companion's order cycles Follow, Hold, Attack.</summary>
        void CycleCompanionOrder()
        {
            int c = World.community.companion; var mate = c >= 0 && c < World.survivors.Count ? World.survivors[c] : null;
            if (mate == null || !mate.alive) { UI.Toast("No companion. Pick one in the Community panel (K)."); Audio.Ui("deny"); return; }
            var next = (CompanionOrder)(((int)mate.order + 1) % 3);
            Submit(new Command { kind = CommandKind.CompanionOrder, target = mate.id, slot = (int)next, recipe = "" });
            UI.Toast(mate.name + ": " + AshfallUI.OrderName(next));
        }

        void CycleWeapon() { var bar = Hotbar().Where(i => i.Def.category != ItemCategory.Light).ToList(); if (bar.Count == 0) return; hotbarIndex = (hotbarIndex + 1) % bar.Count; Sim.Submit(LocalId, new Command { kind = CommandKind.Equip, item = bar[hotbarIndex].uid }); }

        public void Submit(Command c) { if (Sim != null) Sim.Submit(LocalId, c); }

        void DoPrimary()
        {
            var s = Player; if (s == null || s.sleeping) return;
            if (s.action != null && s.action.kind != ActionKind.Sleep) { Sim.Submit(s.id, Command.Of(CommandKind.Cancel)); return; }
            var f = Focus; if (f == null) return;
            var a = f.actions.FirstOrDefault(x => x.enabled); if (a != null) a.run();
        }
    }
}
