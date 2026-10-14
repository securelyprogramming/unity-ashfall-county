using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace ProgrammingSecurely.Presentation
{
    [Serializable] public sealed class StudioIntroSettings
    {
        public int schemaVersion = 1;
        public bool enabled = true, showGameBehind = true;
        public string creator = "ProgrammingSecurely", website = "https://programmingsecurely.com/", gameTitle;
        public float durationSeconds = 3;
        public string[] preferredPanelResources = new string[0], skipQaArguments = new string[0];
    }

    // Independent presentation only: never changes game rules, saves, scenes or time scale.
    public sealed class StudioIntro : MonoBehaviour
    {
        static bool installed;
        StudioIntroSettings settings;
        Canvas canvas;
        CanvasGroup group;
        Sprite ownedSprite;
        readonly List<BaseInputModule> blockedModules = new List<BaseInputModule>();
        readonly List<EventSystem> seenSystems = new List<EventSystem>();
        bool closing, visible, probe;
        float began;
        string probeKind;
        InputDevice probeDevice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() { installed = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (installed) return;
            var source = Resources.Load<TextAsset>("StudioIntro");
            if (!source) return;
            StudioIntroSettings config;
            try { config = JsonUtility.FromJson<StudioIntroSettings>(source.text); }
            catch (Exception error) { Debug.LogWarning("Studio intro config could not be read: " + error.GetType().Name); return; }
            if (config == null || config.schemaVersion != 1 || !config.enabled) return;
            string[] args = Environment.GetCommandLineArgs();
            foreach (string flag in config.skipQaArguments ?? new string[0])
                if (!string.IsNullOrEmpty(flag)) foreach (string arg in args)
                    if (arg == flag || arg.StartsWith(flag + "=", StringComparison.Ordinal)) return;
            installed = true;
            var owner = new GameObject("ProgrammingSecurely Studio Intro");
            DontDestroyOnLoad(owner);
            var intro = owner.AddComponent<StudioIntro>();
            intro.settings = config;
            intro.probeKind = Arg(args, "--studio-intro-smoke");
            intro.probe = intro.probeKind != null;
            if (intro.probe) Application.runInBackground = true;
        }

        static string Arg(string[] args, string key)
        {
            foreach (string arg in args) { if (arg == key) return "keyboard"; if (arg.StartsWith(key + "=", StringComparison.Ordinal)) return arg.Substring(key.Length + 1); }
            return null;
        }

        IEnumerator Start()
        {
            // Let each game's own boot create its real camera/menu before placing the overlay.
            yield return null;
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            Build();
            if (probe) yield return Probe();
        }

        RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        UnityEngine.UI.Text Label(Transform parent, string name, string text, float y, int size, Color color)
        {
            var label = Rect(parent, name, 0, y, 880, 64).gameObject.AddComponent<UnityEngine.UI.Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = size;
            label.alignment = TextAnchor.MiddleCenter; label.text = text; label.color = color; label.raycastTarget = false;
            return label;
        }

        void Build()
        {
            var root = new GameObject("Studio Intro Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            group = root.GetComponent<CanvasGroup>(); group.alpha = 1; group.blocksRaycasts = true;
            var shade = Rect(root.transform, "Live Game Shade", 0, 0, 1280, 720).gameObject.AddComponent<UnityEngine.UI.Image>();
            var shadeRect = shade.rectTransform; shadeRect.anchorMin = Vector2.zero; shadeRect.anchorMax = Vector2.one; shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
            shade.color = new Color(.025f, .04f, .065f, settings.showGameBehind ? .38f : 1);
            var panel = Rect(root.transform, "Creator Panel", 0, 0, 980, 420).gameObject.AddComponent<UnityEngine.UI.Image>();
            panel.color = new Color(.035f, .06f, .095f, .96f);
            foreach (string resource in settings.preferredPanelResources ?? new string[0])
            {
                var existing = Resources.Load<Sprite>(resource);
                if (existing) { panel.sprite = existing; panel.color = Color.white; break; }
                var texture = Resources.Load<Texture2D>(resource);
                if (!texture) continue;
                ownedSprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, texture.width, texture.height), new Vector2(.5f,.5f), 100);
                panel.sprite = ownedSprite; panel.color = Color.white; break;
            }
            var readability = Rect(root.transform, "Credit Readability", 0, 0, 880, 340).gameObject.AddComponent<UnityEngine.UI.Image>();
            readability.color = new Color(.015f,.025f,.04f,.68f); readability.raycastTarget = false;
            Label(root.transform, "Creator Heading", "CREATED BY", 130, 19, new Color(.66f,.85f,.86f));
            Label(root.transform, "Creator Name", settings.creator ?? "ProgrammingSecurely", 65, 48, new Color(.93f,.97f,.93f));
            Label(root.transform, "Game Title", settings.gameTitle ?? Application.productName, -10, 29, new Color(.95f,.82f,.55f));
            Label(root.transform, "Creator Website", settings.website ?? "https://programmingsecurely.com/", -70, 22, new Color(.66f,.86f,.91f));
            Label(root.transform, "Skip Hint", "Skip · any key, mouse click or controller button", -145, 18, new Color(.84f,.88f,.89f));
            began = Time.realtimeSinceStartup; visible = true; BlockUi();
            Debug.Log("STUDIO_INTRO_VISIBLE " + settings.gameTitle);
        }

        void BlockUi()
        {
            var current = EventSystem.current;
            if (!current || seenSystems.Contains(current)) return;
            seenSystems.Add(current);
            foreach (var module in current.GetComponents<BaseInputModule>())
                if (module.enabled) { blockedModules.Add(module); module.enabled = false; }
        }

        void Update()
        {
            if (!visible || closing) return;
            BlockUi();
            bool key = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
            bool mouse = Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.middleButton.wasPressedThisFrame);
            var pad = Gamepad.current;
            bool gamepad = pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame || pad.leftTrigger.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame || pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame);
            if (key || mouse || gamepad || Time.realtimeSinceStartup - began >= Mathf.Clamp(settings.durationSeconds, .5f, 10)) StartCoroutine(Close());
        }

        IEnumerator Close()
        {
            closing = true;
            // Restore game UI after the skip press frame, preventing a submit/click leaking into its menu.
            yield return new WaitForEndOfFrame();
            visible = false; if (canvas) canvas.gameObject.SetActive(false); RestoreUi();
            Debug.Log("STUDIO_INTRO_CLOSED " + settings.gameTitle);
            if (!probe) Destroy(gameObject);
        }

        void RestoreUi() { foreach (var module in blockedModules) if (module) module.enabled = true; blockedModules.Clear(); }
        void OnDestroy() { RestoreUi(); if (ownedSprite) Destroy(ownedSprite); if (probeDevice != null && probeDevice.added) InputSystem.RemoveDevice(probeDevice); }

        [Serializable] sealed class IntroReport { public int schemaVersion=1; public string status, game, input, creator, website, scene, failure="", before, after; public bool visible, hidden, uiRestored, liveGameBehind, purchasedPanel; public float elapsedSeconds; }
        IEnumerator Probe()
        {
            var directory = Path.Combine(Application.temporaryCachePath, "GameWorkbenchQA", "StudioIntro", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var report = new IntroReport { game=settings.gameTitle, input=probeKind, creator=settings.creator, website=settings.website, scene=SceneManager.GetActiveScene().name, liveGameBehind=settings.showGameBehind, visible=visible && canvas.isActiveAndEnabled, purchasedPanel=canvas.transform.Find("Creator Panel").GetComponent<UnityEngine.UI.Image>().sprite!=null };
            report.before=Path.Combine(directory,"intro-visible.png");
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            ScreenCapture.CaptureScreenshot(report.before); yield return new WaitForSecondsRealtime(.5f);
            if (probeKind == "keyboard") { probeDevice=InputSystem.AddDevice<Keyboard>(); InputSystem.QueueStateEvent(probeDevice,new KeyboardState(Key.Space)); }
            else if (probeKind == "mouse") { probeDevice=InputSystem.AddDevice<Mouse>(); InputSystem.QueueStateEvent(probeDevice,new MouseState { buttons=1 }); }
            else if (probeKind == "gamepad") { probeDevice=InputSystem.AddDevice<Gamepad>(); InputSystem.QueueStateEvent(probeDevice,new GamepadState().WithButton(GamepadButton.South)); }
            else if (probeKind != "auto") { report.failure="Unknown intro probe input."; }
            float timeout=Time.realtimeSinceStartup+12;
            while (visible && Time.realtimeSinceStartup<timeout) yield return null;
            report.elapsedSeconds=Time.realtimeSinceStartup-began; report.hidden=!visible && !canvas.gameObject.activeSelf; report.uiRestored=blockedModules.Count==0;
            if (probeDevice!=null && probeDevice.added) { InputSystem.RemoveDevice(probeDevice); probeDevice=null; }
            yield return new WaitForSecondsRealtime(.2f); report.after=Path.Combine(directory,"intro-dismissed.png");ScreenCapture.CaptureScreenshot(report.after);yield return new WaitForSecondsRealtime(.5f);
            report.status=report.visible && report.hidden && report.uiRestored && report.failure=="" && File.Exists(report.before) && File.Exists(report.after)?"passed":"failed";
            File.WriteAllText(Path.Combine(directory,"report.json"),JsonUtility.ToJson(report,true)+"\n");
            Debug.Log("STUDIO_INTRO_REPORT " + directory); Application.Quit(report.status=="passed"?0:2);
        }
    }
}
