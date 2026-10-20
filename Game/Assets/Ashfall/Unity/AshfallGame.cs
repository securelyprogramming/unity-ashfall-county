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

    public enum Screen { Title, NewGame, Load, Loading, Playing, Dead, Evacuated }

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
        public Survivor Player { get { return World == null || World.survivors.Count == 0 ? null : World.survivors[LocalId]; } }
        public int LocalId { get { return 0; } }
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

        InputAction move, look, run, sneak, aim, attack, shove, interact, context, inventory, crafting, health, mapKey, journal, pause, reload, light, hotbarNext, zoomAxis, forage, cancel;
        Light sun, playerGlow, flashlight;
        ParticleSystem rain;
        ActorView playerView;
        readonly Dictionary<int, ActorView> zombieViews = new Dictionary<int, ActorView>();
        readonly Dictionary<int, ActorView> corpseViews = new Dictionary<int, ActorView>();
        readonly Stack<ActorView> zombiePool = new Stack<ActorView>();
        readonly Dictionary<int, float> lastSeen = new Dictionary<int, float>();
        Task<World> loading;
        Action<World> loaded;
        float zoom = 13, interactClock, autosaveClock, shake, orbit;
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
            inventory = Gameplay.AddAction("Inventory", InputActionType.Button, "<Keyboard>/tab"); inventory.AddBinding("<Keyboard>/i"); inventory.AddBinding("<Gamepad>/select");
            crafting = Gameplay.AddAction("Crafting", InputActionType.Button, "<Keyboard>/b"); crafting.AddBinding("<Gamepad>/dpad/left");
            health = Gameplay.AddAction("Health", InputActionType.Button, "<Keyboard>/h"); health.AddBinding("<Gamepad>/dpad/right");
            mapKey = Gameplay.AddAction("Map", InputActionType.Button, "<Keyboard>/m"); mapKey.AddBinding("<Gamepad>/dpad/down");
            journal = Gameplay.AddAction("Radio Log", InputActionType.Button, "<Keyboard>/j");
            pause = Gameplay.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape"); pause.AddBinding("<Gamepad>/start");
            reload = Gameplay.AddAction("Reload", InputActionType.Button, "<Keyboard>/r"); reload.AddBinding("<Gamepad>/buttonWest");
            light = Gameplay.AddAction("Flashlight", InputActionType.Button, "<Keyboard>/f"); light.AddBinding("<Gamepad>/dpad/up");
            hotbarNext = Gameplay.AddAction("Next Weapon", InputActionType.Button, "<Gamepad>/rightShoulder");
            forage = Gameplay.AddAction("Forage", InputActionType.Button, "<Keyboard>/g");
            cancel = Gameplay.AddAction("Back", InputActionType.Button, "<Gamepad>/buttonEast");
            zoomAxis = Gameplay.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            using (var hash = SHA256.Create())
                foreach (var action in Gameplay.actions)
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        var b = action.bindings[i];
                        var digest = hash.ComputeHash(Encoding.UTF8.GetBytes("AshfallInput/v1/" + action.name + "/" + b.name + "/" + b.path));
                        b.id = new Guid(digest.Take(16).ToArray()); action.ChangeBinding(i).To(b);
                    }
            if (!string.IsNullOrEmpty(Prefs.bindings)) try { Gameplay.LoadBindingOverridesFromJson(Prefs.bindings); } catch (Exception) { Notice = "Saved key bindings could not be read; defaults restored."; }
            Gameplay.Enable();
        }
        public IEnumerable<InputAction> RebindableActions { get { return new[] { run, sneak, aim, attack, shove, interact, context, inventory, crafting, health, mapKey, journal, reload, light, forage }; } }
        public bool PausePressed { get { return pause.WasPressedThisFrame(); } }
        public bool BackPressed { get { return cancel.WasPressedThisFrame(); } }
        public bool InventoryPressed { get { return inventory.WasPressedThisFrame(); } }

        // ---------------------------------------------------------------- new game, load, save
        public void StartNewGame(SandboxSettings settings, string name, string profession, bool female, string[] traits = null)
        {
            traits = traits ?? new string[0];
            Prefs.lastName = name; Prefs.lastProfession = profession; Prefs.lastFemale = female; Prefs.lastTraits = string.Join(",", traits); SavePrefs();
            BeginLoading("Generating Ashfall County (" + settings.mapSize + " × " + settings.mapSize + " m)...", Task.Run(() => World.Create(settings, name, profession, female, traits)),
                w => { CurrentSave = Path.Combine(SaveDirectory, Slug(name) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sav"); Radio.Clear(); Enter(w); SaveNow(); });
        }
        public void LoadGame(string path)
        {
            BeginLoading("Restoring " + Path.GetFileNameWithoutExtension(path) + "...", Task.Run(() => SaveCodec.Load(path)), w => { CurrentSave = path; Radio.Clear(); Enter(w); });
        }
        void BeginLoading(string text, Task<World> task, Action<World> done)
        {
            LoadingText = text; loading = task; loaded = done; Show(Screen.Loading);
        }
        static string Slug(string name) { var s = new string((name ?? "survivor").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-'); return s.Length == 0 ? "survivor" : s; }

        void Enter(World w)
        {
            DestroyBackdrop(); ClearSession();
            World = w; Sim = new Simulation(w);
            View = new GameObject("County View").AddComponent<WorldView>(); View.transform.SetParent(transform, false); View.Initialize(w);
            var s = Player;
            playerView = ActorView.Create(transform, false, 0, s.female, s.female ? "survivor_female" : "survivor_male");
            playerView.Snap(Pos(s.pos), Yaw(s.facing));
            camFocus = Pos(s.pos);
            for (int i = 0; i < 12; i++) View.Refresh(camFocus, -CameraForward(), 6);
            autosaveClock = 0;
            Show(w.outcome == "dead" || !s.alive ? Screen.Dead : Screen.Playing);
            Debug.Log("ASHFALL_SESSION seed=" + w.settings.seed + " size=" + w.settings.mapSize + " zombies=" + w.LivingZombies + " buildings=" + w.map.buildings.Count);
        }

        void ClearSession()
        {
            if (View != null) { View.Clear(); Destroy(View.gameObject); View = null; }
            foreach (var z in zombieViews.Values) Destroy(z.gameObject); zombieViews.Clear();
            foreach (var z in zombiePool) Destroy(z.gameObject); zombiePool.Clear();
            foreach (var c in corpseViews.Values) Destroy(c.gameObject); corpseViews.Clear();
            if (playerView != null) Destroy(playerView.gameObject);
            World = null; Sim = null; Focus = null; Nearby.Clear(); lastSeen.Clear();
        }

        public bool SaveNow()
        {
            if (World == null || string.IsNullOrEmpty(CurrentSave)) return false;
            try { SaveCodec.Save(World, CurrentSave); UI.Toast("Game saved."); return true; }
            catch (Exception e) { UI.Toast("Save failed: " + e.Message); Debug.LogError("Save failed: " + e); return false; }
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
                if (World.outcome == "dead" || !Player.alive) { if (Screen != Screen.Dead) { Audio.Play("death", Player.pos); if (!QaActive) SaveNow(); Show(Screen.Dead); } }
                else if (World.outcome == "evacuated" && !World.flags.Contains("evac-shown")) { World.flags.Add("evac-shown"); SaveNow(); Show(Screen.Evacuated); }
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
                if (Mathf.Abs(scroll) > .01f && !pointerOverUi) zoom = Mathf.Clamp(zoom - Mathf.Sign(scroll) * 1.2f, 6, 30);
                if (interact.WasPressedThisFrame()) DoPrimary();
                if (context.WasPressedThisFrame()) UI.OpenContext();
                if (reload.WasPressedThisFrame()) Sim.Submit(s.id, Command.Of(CommandKind.Reload));
                if (light.WasPressedThisFrame()) Sim.Submit(s.id, Command.Of(CommandKind.ToggleLight));
                if (forage.WasPressedThisFrame()) Sim.Submit(s.id, Command.Of(CommandKind.Forage));
                if (hotbarNext.WasPressedThisFrame()) CycleWeapon();
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
                if (crafting.WasPressedThisFrame()) UI.Toggle("crafting");
                if (health.WasPressedThisFrame()) UI.Toggle("health");
                if (mapKey.WasPressedThisFrame()) UI.Toggle("map");
                if (journal.WasPressedThisFrame()) UI.Toggle("journal");
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
        void CycleWeapon() { var bar = Hotbar().Where(i => i.Def.category != ItemCategory.Light).ToList(); if (bar.Count == 0) return; hotbarIndex = (hotbarIndex + 1) % bar.Count; Sim.Submit(LocalId, new Command { kind = CommandKind.Equip, item = bar[hotbarIndex].uid }); }

        public void Submit(Command c) { if (Sim != null) Sim.Submit(LocalId, c); }

        void DoPrimary()
        {
            var s = Player; if (s == null || s.sleeping) return;
            if (s.action != null && s.action.kind != ActionKind.Sleep) { Sim.Submit(s.id, Command.Of(CommandKind.Cancel)); return; }
            var f = Focus; if (f == null) return;
            var a = f.actions.FirstOrDefault(x => x.enabled); if (a != null) a.run();
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
