using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ashfall.View
{
    /// <summary>Menus, HUD and in-game panels (uGUI + TextMeshPro + Input System).</summary>
    public sealed partial class AshfallUI : MonoBehaviour
    {
        AshfallGame game;
        Canvas canvas; RectTransform root;
        RectTransform title, newGame, load, loading, hud, pause, settings, controls, dead, evac;
        readonly Dictionary<string, RectTransform> panels = new Dictionary<string, RectTransform>();
        readonly Stack<string> open = new Stack<string>();
        TextMeshProUGUI loadingText, toast, noticeText; float toastUntil;
        public bool TextEntryActive { get { var go = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject; return go != null && go.GetComponent<TMP_InputField>() != null; } }
        public bool BlocksGameplay { get { return open.Count > 0 || (pause != null && pause.gameObject.activeSelf) || game.Paused; } }

        public void Initialize(AshfallGame game)
        {
            this.game = game;
            canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            root = UIKit.Fill(transform, "Root");
            ApplyScale();
            BuildTitle(); BuildNewGame(); BuildLoad(); BuildLoading(); BuildHud(); BuildPause(); BuildSettings(); BuildControls(); BuildDead(); BuildEvac();
            BuildPanels();
            toast = UIKit.Label(root, "", 26, UIKit.Ink, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-500, -120), new Vector2(500, -70), TextAlignmentOptions.Center);
        }
        public void ApplyScale() { var s = GetComponent<CanvasScaler>(); s.scaleFactor = game.Prefs.uiScale; s.referenceResolution = new Vector2(1920, 1080) / Mathf.Clamp(game.Prefs.uiScale, .75f, 1.5f); }

        public void Toast(string text) { toast.text = text; toastUntil = Time.unscaledTime + 2.5f; }

        void Hide() { foreach (var r in new[] { title, newGame, load, loading, hud, pause, settings, controls, dead, evac }) r.gameObject.SetActive(false); foreach (var p in panels.Values) p.gameObject.SetActive(false); open.Clear(); }

        public void ShowScreen(Screen s)
        {
            Hide();
            switch (s)
            {
                case Screen.Title: title.gameObject.SetActive(true); RefreshTitle(); Select(title); break;
                case Screen.NewGame: newGame.gameObject.SetActive(true); Select(newGame); break;
                case Screen.Load: load.gameObject.SetActive(true); RefreshLoad(); Select(load); break;
                case Screen.Loading: loading.gameObject.SetActive(true); loadingText.text = game.LoadingText; break;
                case Screen.Playing: hud.gameObject.SetActive(true); EventSystem.current.SetSelectedGameObject(null); break;
                case Screen.Dead: hud.gameObject.SetActive(true); dead.gameObject.SetActive(true); RefreshDead(); Select(dead); break;
                case Screen.Evacuated: hud.gameObject.SetActive(true); evac.gameObject.SetActive(true); RefreshEvac(); Select(evac); break;
            }
        }
        static void Select(RectTransform r)
        {
            var b = r.GetComponentsInChildren<Selectable>().FirstOrDefault(x => x.interactable && x.gameObject.activeInHierarchy);
            if (b != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
        }

        // ---------------------------------------------------------------- title
        Button continueButton; TextMeshProUGUI continueInfo;
        void BuildTitle()
        {
            title = UIKit.Fill(root, "Title");
            var shade = UIKit.Rect(title, "Shade", Vector2.zero, new Vector2(.48f, 1), Vector2.zero, Vector2.zero); UIKit.Background(shade, new Color(0, 0, 0, .62f)).raycastTarget = false;
            UIKit.Label(title, "ASHFALL COUNTY", 104, UIKit.Rust, new Vector2(0, 1), new Vector2(.6f, 1), new Vector2(90, -260), new Vector2(0, -90)).fontStyle = FontStyles.Bold;
            UIKit.Label(title, "This is how you survived.", 34, UIKit.Ink, new Vector2(0, 1), new Vector2(.6f, 1), new Vector2(96, -320), new Vector2(0, -262)).fontStyle = FontStyles.Italic;
            var col = UIKit.Rect(title, "Buttons", new Vector2(0, 0), new Vector2(0, 1), new Vector2(96, 140), new Vector2(520, -380)); UIKit.Column(col, 14);
            continueButton = UIKit.Button(col, "Continue", () => { var latest = game.SaveFiles().FirstOrDefault(); if (latest != null) game.LoadGame(latest); }, 420, 62, 30);
            continueInfo = UIKit.Text(col, "", 20, UIKit.Dim); continueInfo.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
            UIKit.Button(col, "New Game", () => game.Show(Screen.NewGame), 420, 62, 30);
            UIKit.Button(col, "Load Game", () => game.Show(Screen.Load), 420, 62, 30);
            UIKit.Button(col, "Settings", () => OpenOverlay(settings), 420, 62, 30);
            UIKit.Button(col, "Controls", () => OpenOverlay(controls), 420, 62, 30);
            UIKit.Button(col, "Quit", () => { game.SavePrefs(); Application.Quit(); }, 420, 62, 30);
            noticeText = UIKit.Label(title, "", 20, UIKit.Warn, new Vector2(0, 0), new Vector2(.6f, 0), new Vector2(96, 60), new Vector2(0, 120));
            UIKit.Label(title, "Development build · an original open-county zombie survival game · ProgrammingSecurely", 18, UIKit.Dim, new Vector2(0, 0), new Vector2(1, 0), new Vector2(96, 18), new Vector2(-40, 50));
        }
        void RefreshTitle()
        {
            var latest = game.SaveFiles().FirstOrDefault(); bool can = false;
            if (latest != null)
            {
                try { var s = SaveCodec.Peek(latest); can = s.alive && s.outcome != "dead"; continueInfo.text = s.name + " · " + Profession.Get(s.profession).name + " · Day " + s.day + " · " + Math.Max(0, s.hours).ToString("0.0") + " h survived" + (s.alive ? "" : " (dead)"); }
                catch (Exception e) { continueInfo.text = "Latest save unreadable: " + e.Message; }
            }
            else continueInfo.text = "No survivors yet.";
            continueButton.interactable = can;
            noticeText.text = game.Notice;
        }

        // ---------------------------------------------------------------- new game
        TMP_InputField nameField, seedField; UIKit.Selector sexPick, profPick; TextMeshProUGUI profText;
        readonly SandboxSettings draft = new SandboxSettings();
        void BuildNewGame()
        {
            newGame = UIKit.Fill(root, "New Game"); UIKit.Background(newGame, new Color(0, 0, 0, .78f));
            UIKit.Label(newGame, "NEW SURVIVOR", 56, UIKit.Rust, new Vector2(0, 1), new Vector2(1, 1), new Vector2(90, -120), new Vector2(0, -40)).fontStyle = FontStyles.Bold;
            var left = UIKit.Rect(newGame, "Survivor", new Vector2(0, 0), new Vector2(.45f, 1), new Vector2(90, 150), new Vector2(-20, -150)); UIKit.Column(left, 12);
            UIKit.Text(left, "Survivor", 32, UIKit.Accent).gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            var nameRow = UIKit.Row(left, 54); UIKit.RowLabel(nameRow.transform, "Name", 260, 24, UIKit.Dim); nameField = UIKit.Input(nameRow.transform, game.Prefs.lastName, 420);
            sexPick = UIKit.Picker(left, "Sex", new[] { "Female", "Male" }, game.Prefs.lastFemale ? 0 : 1, i => { });
            int prof = Array.FindIndex(Profession.All, p => p.id == game.Prefs.lastProfession); if (prof < 0) prof = 0;
            profPick = UIKit.Picker(left, "Profession", Profession.All.Select(p => p.name).ToArray(), prof, i => profText.text = Profession.All[i].description + "\nStarts with: " + Kit(Profession.All[i]));
            profText = UIKit.Text(left, "", 22, UIKit.Ink); profText.gameObject.AddComponent<LayoutElement>().preferredHeight = 90;
            profPick.Set(prof);
            UIKit.Text(left, "Every survivor carries a hand radio, a county map, a water bottle and a snack.", 20, UIKit.Dim).gameObject.AddComponent<LayoutElement>().preferredHeight = 60;

            var right = UIKit.Rect(newGame, "Sandbox", new Vector2(.47f, 0), new Vector2(1, 1), new Vector2(0, 150), new Vector2(-90, -150)); UIKit.Column(right, 8);
            UIKit.Text(right, "County settings", 32, UIKit.Accent).gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            UIKit.Picker(right, "County size", new[] { "Large · 3 km × 3 km", "Medium · 2 km × 2 km", "Small · 1 km × 1 km" }, 0, i => draft.mapSize = new[] { 3072, 2048, 1024 }[i]);
            UIKit.Picker(right, "Zombie population", new[] { "Low", "Normal", "High", "Insane" }, 1, i => draft.population = (Population)i);
            UIKit.Picker(right, "Zombie speed", new[] { "Shamblers", "Fast shamblers", "Sprinters" }, 0, i => draft.speed = (ZombieSpeed)i);
            UIKit.Picker(right, "Day length", new[] { "45 minutes", "60 minutes", "90 minutes", "30 minutes" }, 0, i => draft.dayLengthMinutes = new[] { 45, 60, 90, 30 }[i]);
            UIKit.Picker(right, "Power & water fail", new[] { "Day 4 – 9", "Day 2 – 4", "Day 7 – 14", "Never" }, 0, i => { var r = new[] { new[] { 4, 9 }, new[] { 2, 4 }, new[] { 7, 14 }, new[] { 0, 0 } }[i]; draft.utilitiesMinDay = r[0]; draft.utilitiesMaxDay = r[1]; });
            UIKit.Picker(right, "Evacuation convoy", new[] { "Day 5", "Day 8", "Day 3", "None (endless survival)" }, 0, i => draft.evacuationDay = new[] { 5, 8, 3, 0 }[i]);
            UIKit.Picker(right, "Loot", new[] { "Normal", "Plentiful", "Scarce" }, 0, i => draft.lootAbundance = new[] { 1f, 1.6f, .55f }[i]);
            UIKit.Picker(right, "Zombie infection", new[] { "On (bites kill)", "Off" }, 0, i => draft.infection = i == 0);
            UIKit.Picker(right, "Zombie migration", new[] { "On", "Off" }, 0, i => draft.respawn = i == 0);
            var seedRow = UIKit.Row(right, 54); UIKit.RowLabel(seedRow.transform, "Seed", 260, 24, UIKit.Dim);
            seedField = UIKit.Input(seedRow.transform, UnityEngine.Random.Range(100000, 999999).ToString(), 220); seedField.contentType = TMP_InputField.ContentType.IntegerNumber; seedField.characterLimit = 9;
            UIKit.Button(seedRow.transform, "Random", () => seedField.text = UnityEngine.Random.Range(100000, 999999).ToString(), 160, 48, 22);

            var bottom = UIKit.Rect(newGame, "Actions", new Vector2(0, 0), new Vector2(1, 0), new Vector2(90, 50), new Vector2(-90, 120));
            var h = bottom.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 20; h.childAlignment = TextAnchor.MiddleRight; h.childControlWidth = false; h.childControlHeight = false;
            UIKit.Button(bottom, "Back", () => game.Show(Screen.Title), 240, 62, 28);
            UIKit.Button(bottom, "Begin Survival", () =>
            {
                int seed; if (!int.TryParse(seedField.text, out seed)) seed = UnityEngine.Random.Range(100000, 999999);
                var settings = new SandboxSettings { seed = seed, mapSize = draft.mapSize, population = draft.population, speed = draft.speed, dayLengthMinutes = draft.dayLengthMinutes, utilitiesMinDay = draft.utilitiesMinDay, utilitiesMaxDay = draft.utilitiesMaxDay, evacuationDay = draft.evacuationDay, lootAbundance = draft.lootAbundance, infection = draft.infection, respawn = draft.respawn };
                string n = string.IsNullOrWhiteSpace(nameField.text) ? "Mara Vale" : nameField.text.Trim();
                game.StartNewGame(settings, n, Profession.All[profPick.index].id, sexPick.index == 0);
            }, 340, 62, 28);
        }
        static string Kit(Profession p)
        {
            var names = p.kit.Select(id => ItemCatalog.Get(id).name).ToList();
            return names.Count == 0 ? "nothing extra" : string.Join(", ", names);
        }

        // ---------------------------------------------------------------- load
        RectTransform loadList; string pendingDelete;
        void BuildLoad()
        {
            load = UIKit.Fill(root, "Load"); UIKit.Background(load, new Color(0, 0, 0, .8f));
            UIKit.Label(load, "LOAD SURVIVOR", 56, UIKit.Rust, new Vector2(0, 1), new Vector2(1, 1), new Vector2(90, -120), new Vector2(0, -40)).fontStyle = FontStyles.Bold;
            var area = UIKit.Rect(load, "List", new Vector2(0, 0), new Vector2(1, 1), new Vector2(90, 150), new Vector2(-90, -150));
            UIKit.Scroll(area, "Saves", out loadList);
            var bottom = UIKit.Rect(load, "Actions", new Vector2(0, 0), new Vector2(1, 0), new Vector2(90, 50), new Vector2(-90, 120));
            var h = bottom.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 20; h.childAlignment = TextAnchor.MiddleRight; h.childControlWidth = false; h.childControlHeight = false;
            UIKit.Button(bottom, "Back", () => game.Show(Screen.Title), 240, 62, 28);
        }
        void RefreshLoad()
        {
            UIKit.Clear(loadList); pendingDelete = null;
            var files = game.SaveFiles().ToList();
            if (files.Count == 0) UIKit.Text(loadList, "No saved survivors.", 26, UIKit.Dim).gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
            foreach (var f in files)
            {
                string label; bool alive = false;
                try { var s = SaveCodec.Peek(f); alive = s.alive; label = s.name + "  ·  " + Profession.Get(s.profession).name + "  ·  Day " + s.day + "  ·  " + Math.Max(0, s.hours).ToString("0.0") + " h  ·  " + s.kills + " kills  ·  " + s.mapSize + " m county" + (s.outcome == "evacuated" ? "  ·  EVACUATED" : s.alive ? "" : "  ·  DEAD") + "\n<size=18><color=#999>" + new DateTime(s.savedAtTicks, DateTimeKind.Utc).ToLocalTime().ToString("g") + " · " + Path.GetFileName(f) + "</color></size>"; }
                catch (Exception e) { label = Path.GetFileName(f) + " (unreadable: " + e.Message + ")"; }
                var row = UIKit.Row(loadList, 76, 12);
                var b = UIKit.Button(row.transform, "", () => game.LoadGame(f), 1300, 72, 22); b.GetComponentInChildren<TextMeshProUGUI>().text = label; b.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
                b.interactable = alive;
                string path = f;
                Button del = null;
                del = UIKit.Button(row.transform, "Delete", () => { if (pendingDelete == path) { game.DeleteSave(path); RefreshLoad(); Select(load); } else { pendingDelete = path; UIKit.SetLabel(del, "Confirm?"); } }, 200, 72, 22);
            }
        }

        // ---------------------------------------------------------------- loading
        RectTransform spinner;
        void BuildLoading()
        {
            loading = UIKit.Fill(root, "Loading"); UIKit.Background(loading, new Color(.03f, .035f, .04f, 1));
            loadingText = UIKit.Label(loading, "", 36, UIKit.Ink, new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, -20), new Vector2(0, 40), TextAlignmentOptions.Center);
            UIKit.Label(loading, "Tip: noise travels. Gunshots and breaking glass call the dead from blocks away. Walk, don't run, when you can.", 22, UIKit.Dim, new Vector2(.15f, .5f), new Vector2(.85f, .5f), new Vector2(0, -120), new Vector2(0, -40), TextAlignmentOptions.Center);
            spinner = UIKit.Box2(loading, "Spinner", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(80, 80));
            var img = spinner.gameObject.AddComponent<Image>(); img.sprite = UIKit.Icon("SPR_HUD_LoadingRing") ?? UIKit.White; img.color = UIKit.Accent;
        }

        // ---------------------------------------------------------------- overlays
        RectTransform overlay;
        void OpenOverlay(RectTransform r) { if (overlay != null) overlay.gameObject.SetActive(false); overlay = r; r.gameObject.SetActive(true); r.SetAsLastSibling(); Select(r); }
        void CloseOverlay() { if (overlay == null) return; overlay.gameObject.SetActive(false); overlay = null; if (title.gameObject.activeSelf) Select(title); else if (pause.gameObject.activeSelf) Select(pause); }

        void BuildPause()
        {
            pause = UIKit.Fill(root, "Pause"); UIKit.Background(pause, new Color(0, 0, 0, .7f));
            UIKit.Label(pause, "PAUSED", 64, UIKit.Rust, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -200), new Vector2(0, -100), TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            var col = UIKit.Box2(pause, "Buttons", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(440, 520)); UIKit.Column(col, 14, TextAnchor.UpperCenter);
            UIKit.Button(col, "Resume", () => { game.Paused = false; SetPause(false); }, 420, 62, 28);
            UIKit.Button(col, "Save", () => game.SaveNow(), 420, 62, 28);
            UIKit.Button(col, "Settings", () => OpenOverlay(settings), 420, 62, 28);
            UIKit.Button(col, "Controls", () => OpenOverlay(controls), 420, 62, 28);
            UIKit.Button(col, "Save & Quit to Menu", () => game.QuitToTitle(true), 420, 62, 28);
            UIKit.Button(col, "Save & Quit to Desktop", () => { game.SaveNow(); game.SavePrefs(); Application.Quit(); }, 420, 62, 28);
        }
        public void SetPause(bool paused)
        {
            pause.gameObject.SetActive(paused); if (!paused && overlay != null) CloseOverlay();
            if (paused) { pause.SetAsLastSibling(); Select(pause); } else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            game.Audio.Ui(paused ? "open" : "close");
        }

        void BuildSettings()
        {
            settings = UIKit.Fill(root, "Settings"); UIKit.Background(settings, new Color(0, 0, 0, .86f));
            UIKit.Label(settings, "SETTINGS", 56, UIKit.Rust, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -150), new Vector2(0, -60), TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            var col = UIKit.Box2(settings, "Options", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -20), new Vector2(820, 600)); UIKit.Column(col, 12);
            var p = game.Prefs;
            UIKit.Slider(col, "Master volume", p.master, v => { p.master = v; AudioListener.volume = v; });
            UIKit.Slider(col, "Music", p.music, v => p.music = v);
            UIKit.Slider(col, "Effects", p.effects, v => p.effects = v);
            UIKit.Picker(col, "Display", new[] { "Windowed", "Fullscreen" }, p.fullscreen ? 1 : 0, i => { p.fullscreen = i == 1; game.ApplyDisplay(); });
            UIKit.Picker(col, "V-Sync", new[] { "On", "Off" }, p.vsync ? 0 : 1, i => { p.vsync = i == 0; game.ApplyDisplay(); });
            UIKit.Picker(col, "Interface scale", new[] { "100%", "115%", "130%", "85%" }, Array.IndexOf(new[] { 1f, 1.15f, 1.3f, .85f }, p.uiScale) < 0 ? 0 : Array.IndexOf(new[] { 1f, 1.15f, 1.3f, .85f }, p.uiScale), i => { p.uiScale = new[] { 1f, 1.15f, 1.3f, .85f }[i]; ApplyScale(); });
            UIKit.Picker(col, "Tips", new[] { "Shown", "Hidden" }, p.showTips ? 0 : 1, i => p.showTips = i == 0);
            var row = UIKit.Row(col, 70); UIKit.Button(row.transform, "Done", () => { game.SavePrefs(); CloseOverlay(); }, 300, 62, 28);
        }

        TextMeshProUGUI controlsList; InputActionRebindingExtensions.RebindingOperation rebinding;
        void BuildControls()
        {
            controls = UIKit.Fill(root, "Controls"); UIKit.Background(controls, new Color(0, 0, 0, .9f));
            UIKit.Label(controls, "CONTROLS", 56, UIKit.Rust, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -130), new Vector2(0, -40), TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            var area = UIKit.Rect(controls, "List", new Vector2(.12f, 0), new Vector2(.88f, 1), new Vector2(0, 140), new Vector2(0, -150));
            RectTransform content; UIKit.Scroll(area, "Bindings", out content);
            UIKit.Text(content, "Move: WASD / left stick · Aim: mouse / right stick · Zoom: mouse wheel · Rotate view: Z / X · Hotbar: 1–6\nControllers: A interact, Y actions menu, X reload, B back, LB shove, RB next weapon, LT aim, RT attack, L3 run, R3 sneak, d-pad up light, left crafting, right health, down map, Select inventory, Start pause.", 22, UIKit.Dim).gameObject.AddComponent<LayoutElement>().preferredHeight = 110;
            foreach (var action in game.RebindableActions)
            {
                var a = action; var row = UIKit.Row(content, 52, 12); UIKit.RowLabel(row.transform, a.name, 360, 24);
                var current = UIKit.RowLabel(row.transform, a.GetBindingDisplayString(), 360, 24, UIKit.Accent);
                UIKit.Button(row.transform, "Rebind key", () =>
                {
                    if (rebinding != null) return;
                    int index = -1;
                    for (int i = 0; i < a.bindings.Count; i++) { var b = a.bindings[i]; if (!b.isComposite && !b.isPartOfComposite && (b.effectivePath.StartsWith("<Keyboard>") || b.effectivePath.StartsWith("<Mouse>"))) { index = i; break; } }
                    if (index < 0) return;
                    current.text = "Press a key...";
                    a.Disable();
                    rebinding = a.PerformInteractiveRebinding(index).WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta").WithCancelingThrough("<Keyboard>/escape").OnComplete(op => { op.Dispose(); rebinding = null; a.Enable(); current.text = a.GetBindingDisplayString(); game.SavePrefs(); }).OnCancel(op => { op.Dispose(); rebinding = null; a.Enable(); current.text = a.GetBindingDisplayString(); }).Start();
                }, 220, 46, 22);
            }
            var bottom = UIKit.Rect(controls, "Actions", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 40), new Vector2(0, 110));
            var h = bottom.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 20; h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = false; h.childControlHeight = false;
            UIKit.Button(bottom, "Reset to defaults", () => { game.Gameplay.RemoveAllBindingOverrides(); game.SavePrefs(); CloseOverlay(); }, 320, 62, 24);
            UIKit.Button(bottom, "Done", () => CloseOverlay(), 240, 62, 26);
        }

        // ---------------------------------------------------------------- endings
        TextMeshProUGUI deadText, evacText;
        void BuildDead()
        {
            dead = UIKit.Fill(root, "Dead"); UIKit.Background(dead, new Color(.12f, 0, 0, .7f));
            UIKit.Label(dead, "YOU DIED", 96, new Color(.85f, .2f, .16f), new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, 160), new Vector2(0, 300), TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            deadText = UIKit.Label(dead, "", 30, UIKit.Ink, new Vector2(.2f, .5f), new Vector2(.8f, .5f), new Vector2(0, -60), new Vector2(0, 150), TextAlignmentOptions.Center);
            var col = UIKit.Box2(dead, "Buttons", new Vector2(.5f, .5f), new Vector2(.5f, 1), new Vector2(0, -90), new Vector2(420, 220)); UIKit.Column(col, 14, TextAnchor.UpperCenter);
            UIKit.Button(col, "New Survivor", () => { game.QuitToTitle(false); game.Show(Screen.NewGame); }, 400, 62, 28);
            UIKit.Button(col, "Main Menu", () => game.QuitToTitle(false), 400, 62, 28);
        }
        void RefreshDead()
        {
            var s = game.Player; if (s == null) return;
            double hours = (s.diedAt > 0 ? s.diedAt : game.World.minutes) - s.spawnedAt; hours /= 60;
            deadText.text = s.name + ", " + Profession.Get(s.profession).name + "\n" + (string.IsNullOrEmpty(s.causeOfDeath) ? "" : s.causeOfDeath + "\n") + "Survived " + ((int)(hours / 24)) + " days and " + ((int)(hours % 24)) + " hours.  Zombies killed: " + s.kills + "\n<size=22><color=#bbb>This is how they died.</color></size>";
        }
        void BuildEvac()
        {
            evac = UIKit.Fill(root, "Evacuated"); UIKit.Background(evac, new Color(0, .06f, .03f, .72f));
            UIKit.Label(evac, "EVACUATED", 96, UIKit.Good, new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, 160), new Vector2(0, 300), TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            evacText = UIKit.Label(evac, "", 30, UIKit.Ink, new Vector2(.2f, .5f), new Vector2(.8f, .5f), new Vector2(0, -60), new Vector2(0, 150), TextAlignmentOptions.Center);
            var col = UIKit.Box2(evac, "Buttons", new Vector2(.5f, .5f), new Vector2(.5f, 1), new Vector2(0, -90), new Vector2(460, 220)); UIKit.Column(col, 14, TextAnchor.UpperCenter);
            UIKit.Button(col, "Stay behind and keep surviving", () => game.Show(Screen.Playing), 440, 62, 24);
            UIKit.Button(col, "Main Menu", () => game.QuitToTitle(true), 440, 62, 28);
        }
        void RefreshEvac()
        {
            var s = game.Player; double hours = game.World.HoursSurvived;
            evacText.text = s.name + " climbed aboard Jonah Reed's bus at the Ash River bridge.\nSurvived " + ((int)(hours / 24)) + " days and " + ((int)(hours % 24)) + " hours.  Zombies killed: " + s.kills + "\n<size=22><color=#bbb>The county will remember you. You can stay and keep playing this world.</color></size>";
        }

        // ---------------------------------------------------------------- frame
        public void Tick()
        {
            if (toast != null) toast.gameObject.SetActive(Time.unscaledTime < toastUntil);
            if (loading.gameObject.activeSelf) { spinner.Rotate(0, 0, -240 * Time.unscaledDeltaTime); loadingText.text = game.LoadingText; }
            if (overlay != null && game.BackPressed) CloseOverlay();
            if (newGame.gameObject.activeSelf && game.BackPressed && !TextEntryActive) game.Show(Screen.Title);
            if (load.gameObject.activeSelf && game.BackPressed) game.Show(Screen.Title);
            if (title.gameObject.activeSelf && overlay == null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && (Gamepad.current != null && Gamepad.current.leftStick.ReadValue().sqrMagnitude > .3f || Keyboard.current != null && Keyboard.current.downArrowKey.wasPressedThisFrame)) Select(title);
            if (hud.gameObject.activeSelf && game.World != null) TickHud();
        }
    }
}
