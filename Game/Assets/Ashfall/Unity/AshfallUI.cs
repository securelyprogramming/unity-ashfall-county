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
    /// <summary>Menus, HUD and in-game panels (uGUI + TextMeshPro + Input System).
    /// Partial files: AshfallHud (HUD), UiEvents (every sim event to text), UiNewGame (community, world options, drop map),
    /// AshfallPanels (inventory, actions, map), AshfallPanelsCommunity (roster, base, workshop), AshfallPanelsMissions (missions, radio, talk/trade).</summary>
    public sealed partial class AshfallUI : MonoBehaviour
    {
        AshfallGame game;
        Canvas canvas; RectTransform root;
        RectTransform title, newGame, load, loading, hud, pause, settings, controls, ending;
        readonly Dictionary<string, RectTransform> panels = new Dictionary<string, RectTransform>();
        readonly Stack<string> open = new Stack<string>();
        TextMeshProUGUI loadingText, loadingTime, loadingTip, toast, noticeText; RectTransform toastCard; CanvasGroup toastGroup; float toastUntil;
        public bool TextEntryActive { get { var go = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject; return go != null && go.GetComponent<TMP_InputField>() != null; } }
        public bool BlocksGameplay { get { return open.Count > 0 || (pause != null && pause.gameObject.activeSelf) || game.Paused || (ending != null && ending.gameObject.activeSelf); } }

        public void Initialize(AshfallGame game)
        {
            this.game = game;
            canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            root = UIKit.Fill(transform, "Root");
            ApplyScale();
            BuildHud();
            BuildTitle(); BuildTraits(); BuildNewGame(); BuildDropMap(); BuildLoad(); BuildLoading(); BuildPause(); BuildSettings(); BuildControls(); BuildEnding();
            BuildPanels();
            toastCard = UIKit.Box2(root, "Toast", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -238), new Vector2(760, 50)); UIKit.Card(toastCard, new Color(.04f, .045f, .05f, .9f));
            toastGroup = toastCard.gameObject.AddComponent<CanvasGroup>(); toastGroup.blocksRaycasts = false; toastGroup.alpha = 0;
            toast = UIKit.Label(toastCard, "", 23, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(18, 0), new Vector2(-18, 0), TextAlignmentOptions.Center);
        }
        /// <summary>Interface scale: a smaller reference resolution makes every element larger (1280×720 to 4K stay readable).</summary>
        public void ApplyScale() { var s = GetComponent<CanvasScaler>(); s.referenceResolution = new Vector2(1920, 1080) / Mathf.Clamp(game.Prefs.uiScale, .75f, 1.6f); }

        public void Toast(string text) { if (toast == null) return; toast.text = text; toastUntil = Time.unscaledTime + 2.8f; toastCard.SetAsLastSibling(); }

        void Hide()
        {
            foreach (var r in new[] { title, newGame, dropMap, load, loading, hud, pause, settings, controls, ending, traitsPanel }) r.gameObject.SetActive(false);
            foreach (var p in panels.Values) p.gameObject.SetActive(false); open.Clear(); overlay = null;
        }

        public void ShowScreen(Screen s)
        {
            Hide();
            switch (s)
            {
                case Screen.Title: title.gameObject.SetActive(true); RefreshTitle(); UIKit.FadeIn(title, .3f); Select(title); break;
                case Screen.NewGame: newGame.gameObject.SetActive(true); UIKit.FadeIn(newGame); Select(newGame); break;
                case Screen.DropMap: dropMap.gameObject.SetActive(true); UIKit.FadeIn(dropMap); RefreshDropMap(); break;
                case Screen.Load: load.gameObject.SetActive(true); RefreshLoad(); UIKit.FadeIn(load); Select(load); break;
                case Screen.Loading: loading.gameObject.SetActive(true); loadingText.text = game.LoadingText; break;
                case Screen.Playing: hud.gameObject.SetActive(true); UIKit.FadeIn(hud, .4f); if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); break;
                case Screen.Dead: case Screen.Legacy: case Screen.Evacuated: hud.gameObject.SetActive(true); ShowEnding(s); break;
            }
            if (toastCard != null) toastCard.SetAsLastSibling();
        }
        static void Select(RectTransform r)
        {
            var b = r.GetComponentsInChildren<Selectable>().FirstOrDefault(x => x.interactable && x.gameObject.activeInHierarchy);
            if (b != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
        }

        /// <summary>A full-screen page with the dark menu backdrop and a heading.</summary>
        RectTransform Page(string name, string heading, float alpha = .82f)
        {
            var r = UIKit.Fill(root, name); UIKit.Background(r, new Color(.02f, .024f, .03f, alpha));
            if (heading != null) UIKit.Heading(r, heading, 54, new Vector2(0, 1), new Vector2(1, 1), new Vector2(90, -130), new Vector2(-90, -50));
            return r;
        }
        static RectTransform ActionsBar(RectTransform page, TextAnchor align = TextAnchor.MiddleRight)
        {
            return UIKit.Strip(page, "Actions", new Vector2(0, 0), new Vector2(1, 0), new Vector2(90, 40), new Vector2(-90, 112), align, 18);
        }

        // ---------------------------------------------------------------- title
        Button continueButton; TextMeshProUGUI continueInfo;
        void BuildTitle()
        {
            title = UIKit.Fill(root, "Title");
            var shade = UIKit.Rect(title, "Shade", Vector2.zero, new Vector2(.44f, 1), Vector2.zero, Vector2.zero); UIKit.Background(shade, new Color(.015f, .018f, .022f, .8f)).raycastTarget = false;
            var edge = UIKit.Rect(shade, "Edge", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-3, 0), Vector2.zero); UIKit.Background(edge, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, .5f)).raycastTarget = false;
            var name = UIKit.Label(title, "ASHFALL", 128, UIKit.Ink, new Vector2(0, 1), new Vector2(.44f, 1), new Vector2(92, -250), new Vector2(0, -90)); name.fontStyle = FontStyles.Bold; name.characterSpacing = 14;
            var county = UIKit.Label(title, "COUNTY", 54, UIKit.Accent, new Vector2(0, 1), new Vector2(.44f, 1), new Vector2(98, -322), new Vector2(0, -246)); county.fontStyle = FontStyles.Bold; county.characterSpacing = 40;
            UIKit.Label(title, "Lead your survivors. Cleanse the county.", 30, UIKit.Dim, new Vector2(0, 1), new Vector2(.44f, 1), new Vector2(98, -380), new Vector2(0, -330)).fontStyle = FontStyles.Italic;
            var col = UIKit.Rect(title, "Buttons", new Vector2(0, 0), new Vector2(0, 1), new Vector2(98, 150), new Vector2(540, -420)); UIKit.Column(col, 12);
            continueButton = UIKit.Primary(col, "Continue", () => { var latest = game.SaveFiles().FirstOrDefault(); if (latest != null) game.LoadGame(latest); }, 440, 62, 28);
            continueInfo = UIKit.Line(col, "", 20, UIKit.Dim, 30);
            UIKit.Button(col, "New Game", () => game.Show(Screen.NewGame), 440, 58, 27);
            UIKit.Button(col, "Load Game", () => game.Show(Screen.Load), 440, 58, 27);
            UIKit.Button(col, "Settings", () => OpenOverlay(settings), 440, 58, 27);
            UIKit.Button(col, "Controls", () => OpenOverlay(controls), 440, 58, 27);
            UIKit.Button(col, "Quit", () => { game.SavePrefs(); Application.Quit(); }, 440, 58, 27);
            noticeText = UIKit.Label(title, "", 20, UIKit.Warn, new Vector2(0, 0), new Vector2(.44f, 0), new Vector2(98, 64), new Vector2(-20, 130));
            UIKit.Label(title, "Development build · an original open-county zombie survival game · ProgrammingSecurely", 18, UIKit.Dim, new Vector2(0, 0), new Vector2(1, 0), new Vector2(98, 18), new Vector2(-40, 50));
        }
        void RefreshTitle()
        {
            var latest = game.SaveFiles().FirstOrDefault(); bool can = false;
            if (latest != null)
            {
                try { var s = SaveCodec.Peek(latest); can = s.alive && s.outcome != "dead"; continueInfo.text = s.name + "'s community · Day " + s.day + " · " + Math.Max(0, s.hours).ToString("0.0") + " h" + (s.outcome == "legacy" ? " · county cleansed" : can ? "" : " · lost"); }
                catch (Exception e) { continueInfo.text = "Latest save unreadable: " + e.Message; }
            }
            else continueInfo.text = "No community yet.";
            continueButton.interactable = can;
            noticeText.text = game.Notice;
        }

        // ---------------------------------------------------------------- load
        RectTransform loadList; string pendingDelete;
        void BuildLoad()
        {
            load = Page("Load", "LOAD COMMUNITY");
            var area = UIKit.Rect(load, "List", new Vector2(0, 0), new Vector2(1, 1), new Vector2(90, 140), new Vector2(-90, -160));
            UIKit.Scroll(area, "Saves", out loadList);
            UIKit.Button(ActionsBar(load), "Back", () => game.Show(Screen.Title), 240, 60, 26);
        }
        void RefreshLoad()
        {
            UIKit.Clear(loadList); pendingDelete = null;
            var files = game.SaveFiles().ToList();
            if (files.Count == 0) UIKit.Line(loadList, "No saved communities.", 26, UIKit.Dim, 50);
            foreach (var f in files)
            {
                string label; bool alive = false;
                try
                {
                    var s = SaveCodec.Peek(f); alive = s.alive && s.outcome != "dead";
                    string state = s.outcome == "legacy" ? "  ·  <color=#9fe08a>COUNTY CLEANSED</color>" : s.outcome == "dead" ? "  ·  <color=#ff8a7a>COMMUNITY LOST</color>" : s.outcome == "evacuated" ? "  ·  STORY ENDED" : s.alive ? "" : "  ·  <color=#ff8a7a>LOST</color>";
                    label = "<b>" + s.name + "</b>  ·  Day " + s.day + "  ·  " + Math.Max(0, s.hours).ToString("0.0") + " h  ·  " + s.kills + " kills  ·  " + s.mapSize + " m county" + state + "\n<size=18><color=#999>" + new DateTime(s.savedAtTicks, DateTimeKind.Utc).ToLocalTime().ToString("g") + " · " + Path.GetFileName(f) + "</color></size>";
                }
                catch (Exception e) { label = Path.GetFileName(f) + " (unreadable: " + e.Message + ")"; }
                var row = UIKit.Row(loadList, 76, 12);
                var b = UIKit.Button(row.transform, "", () => game.LoadGame(f), 1300, 72, 22); var t = b.GetComponentInChildren<TextMeshProUGUI>(); t.text = label; t.alignment = TextAlignmentOptions.MidlineLeft;
                b.interactable = alive;
                string path = f;
                Button del = null;
                del = UIKit.Button(row.transform, "Delete", () => { if (pendingDelete == path) { game.DeleteSave(path); RefreshLoad(); Select(load); } else { pendingDelete = path; UIKit.SetLabel(del, "Confirm?"); } }, 200, 72, 22);
            }
        }

        // ---------------------------------------------------------------- loading
        RectTransform spinner; float tipClock; int tipIndex;
        static readonly string[] Tips =
        {
            "Noise travels. Gunshots and breaking glass call the dead from blocks away.",
            "Skulls show how dangerous a freak is. Three or more: bring a friend, a car or explosives.",
            "Rucksacks feed the whole community. Carry them home and deposit them at base.",
            "Tired or hurt? Switch to a fresh member at base and let the others rest.",
            "A red smoke plume means a supply drop. Everyone nearby wants it too.",
            "When the Guard warns of artillery, leave the red zone. Shells do not care who you are.",
            "Never melee a bloater. Its gas cloud hurts you, not it.",
            "Plague hearts spawn plague zombies. Destroy every heart to cleanse the county.",
        };
        void BuildLoading()
        {
            loading = UIKit.Fill(root, "Loading"); UIKit.Background(loading, new Color(.025f, .028f, .034f, 1));
            UIKit.Label(loading, "ASHFALL COUNTY", 30, UIKit.Accent, new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, 100), new Vector2(0, 150), TextAlignmentOptions.Center).characterSpacing = 20;
            loadingText = UIKit.Label(loading, "", 36, UIKit.Ink, new Vector2(.1f, .5f), new Vector2(.9f, .5f), new Vector2(0, -10), new Vector2(0, 60), TextAlignmentOptions.Center);
            loadingTime = UIKit.Label(loading, "", 22, UIKit.Dim, new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, -60), new Vector2(0, -16), TextAlignmentOptions.Center);
            loadingTip = UIKit.Label(loading, "", 22, UIKit.Dim, new Vector2(.18f, 0), new Vector2(.82f, 0), new Vector2(0, 80), new Vector2(0, 160), TextAlignmentOptions.Center);
            spinner = UIKit.Box2(loading, "Spinner", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -150), new Vector2(64, 64));
            var img = spinner.gameObject.AddComponent<Image>(); img.sprite = UIKit.Icon("SPR_HUD_LoadingRing") ?? UIKit.Ring; img.color = UIKit.Accent; img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Radial360; img.fillAmount = UIKit.Icon("SPR_HUD_LoadingRing") == null ? .7f : 1;
        }

        // ---------------------------------------------------------------- overlays
        RectTransform overlay;
        void OpenOverlay(RectTransform r) { if (overlay != null) overlay.gameObject.SetActive(false); overlay = r; r.gameObject.SetActive(true); r.SetAsLastSibling(); UIKit.FadeIn(r); Select(r); }
        void CloseOverlay()
        {
            if (overlay == null) return; overlay.gameObject.SetActive(false); overlay = null;
            if (newGame.gameObject.activeSelf) { RefreshTraitRow(); Select(newGame); } else if (title.gameObject.activeSelf) Select(title); else if (pause.gameObject.activeSelf) Select(pause);
        }

        void BuildPause()
        {
            pause = UIKit.Fill(root, "Pause"); UIKit.Background(pause, new Color(0, 0, 0, .66f));
            var card = UIKit.Box2(pause, "Card", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(520, 600)); UIKit.Background(card, UIKit.Panel, true);
            UIKit.Label(card, "PAUSED", 48, UIKit.Ink, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -96), new Vector2(0, -28), TextAlignmentOptions.Center).characterSpacing = 18;
            var col = UIKit.Rect(card, "Buttons", Vector2.zero, Vector2.one, new Vector2(40, 30), new Vector2(-40, -120)); UIKit.Column(col, 12, TextAnchor.UpperCenter);
            UIKit.Primary(col, "Resume", () => { game.Paused = false; SetPause(false); }, 440, 60, 27);
            UIKit.Button(col, "Save", () => game.SaveNow(), 440, 56, 25);
            UIKit.Button(col, "Settings", () => OpenOverlay(settings), 440, 56, 25);
            UIKit.Button(col, "Controls", () => OpenOverlay(controls), 440, 56, 25);
            UIKit.Button(col, "Save & Quit to Menu", () => game.QuitToTitle(true), 440, 56, 25);
            UIKit.Button(col, "Save & Quit to Desktop", () => { game.SaveNow(); game.SavePrefs(); Application.Quit(); }, 440, 56, 25);
        }
        public void SetPause(bool paused)
        {
            pause.gameObject.SetActive(paused); if (!paused && overlay != null) CloseOverlay();
            if (paused) { pause.SetAsLastSibling(); UIKit.FadeIn(pause, .1f); Select(pause); } else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            game.Audio.Ui(paused ? "open" : "close");
        }

        void BuildSettings()
        {
            settings = Page("Settings", null, .9f);
            var card = UIKit.Box2(settings, "Card", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(900, 700)); UIKit.Background(card, UIKit.Panel, true);
            UIKit.Heading(card, "SETTINGS", 44, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -90), new Vector2(-40, -24));
            var col = UIKit.Rect(card, "Options", Vector2.zero, Vector2.one, new Vector2(40, 30), new Vector2(-40, -120)); UIKit.Column(col, 10);
            var p = game.Prefs;
            UIKit.Slider(col, "Master volume", p.master, v => { p.master = v; AudioListener.volume = v; });
            UIKit.Slider(col, "Music", p.music, v => p.music = v);
            UIKit.Slider(col, "Effects", p.effects, v => p.effects = v);
            UIKit.Picker(col, "Display", new[] { "Windowed", "Fullscreen" }, p.fullscreen ? 1 : 0, i => { p.fullscreen = i == 1; game.ApplyDisplay(); });
            UIKit.Picker(col, "V-Sync", new[] { "On", "Off" }, p.vsync ? 0 : 1, i => { p.vsync = i == 0; game.ApplyDisplay(); });
            var scales = new[] { 1f, 1.15f, 1.3f, 1.5f, .85f };
            int si = Array.IndexOf(scales, p.uiScale);
            UIKit.Picker(col, "Interface scale", new[] { "100%", "115%", "130%", "150%", "85%" }, si < 0 ? 0 : si, i => { p.uiScale = scales[i]; ApplyScale(); });
            UIKit.Picker(col, "Tips", new[] { "Shown", "Hidden" }, p.showTips ? 0 : 1, i => p.showTips = i == 0);
            var row = UIKit.Row(col, 70); UIKit.Primary(row.transform, "Done", () => { game.SavePrefs(); CloseOverlay(); }, 300, 60, 27);
        }

        TextMeshProUGUI controlsList; InputActionRebindingExtensions.RebindingOperation rebinding;
        void BuildControls()
        {
            controls = Page("Controls", "CONTROLS", .94f);
            var area = UIKit.Rect(controls, "List", new Vector2(.1f, 0), new Vector2(.9f, 1), new Vector2(0, 140), new Vector2(0, -150));
            RectTransform content; UIKit.Scroll(area, "Bindings", out content);
            UIKit.Line(content, "<b>Keyboard and mouse</b>  Move WASD · Aim mouse · Attack LMB · Aim RMB · Zoom wheel · Rotate view Z / X · Hotbar 1–6\n" +
                "<b>Controller</b>  A interact · Y actions · X reload · B back · LB shove · RB tap next weapon, hold to throw · LT aim · RT attack · L3 run · R3 sneak\n" +
                "D-pad: up companion order · left community · right missions · down map · Select inventory · Start pause", 21, UIKit.Dim, 116);
            foreach (var action in game.RebindableActions)
            {
                var a = action; var row = UIKit.Row(content, 52, 12); UIKit.RowLabel(row.transform, a.name, 360, 24);
                var current = UIKit.RowLabel(row.transform, a.GetBindingDisplayString(), 380, 24, UIKit.Accent);
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
            var bottom = ActionsBar(controls, TextAnchor.MiddleCenter);
            UIKit.Button(bottom, "Reset to defaults", () => { game.Gameplay.RemoveAllBindingOverrides(); game.SavePrefs(); CloseOverlay(); }, 320, 60, 24);
            UIKit.Primary(bottom, "Done", () => CloseOverlay(), 240, 60, 26);
        }

        // ---------------------------------------------------------------- endings
        TextMeshProUGUI endingTitle, endingSub, endingStats; RectTransform endingButtons; Image endingShade;
        void BuildEnding()
        {
            ending = UIKit.Fill(root, "Ending"); endingShade = UIKit.Background(ending, new Color(.1f, 0, 0, .72f));
            endingTitle = UIKit.Label(ending, "", 92, UIKit.Bad, new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, 200), new Vector2(0, 330), TextAlignmentOptions.Center); endingTitle.fontStyle = FontStyles.Bold; endingTitle.characterSpacing = 10;
            endingSub = UIKit.Label(ending, "", 30, UIKit.Ink, new Vector2(.15f, .5f), new Vector2(.85f, .5f), new Vector2(0, 120), new Vector2(0, 200), TextAlignmentOptions.Center);
            var stats = UIKit.Box2(ending, "Stats", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 20), new Vector2(980, 170)); UIKit.Card(stats, new Color(0, 0, 0, .5f));
            endingStats = UIKit.Label(stats, "", 26, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(20, 10), new Vector2(-20, -10), TextAlignmentOptions.Center);
            endingButtons = UIKit.Box2(ending, "Buttons", new Vector2(.5f, .5f), new Vector2(.5f, 1), new Vector2(0, -100), new Vector2(480, 240)); UIKit.Column(endingButtons, 12, TextAnchor.UpperCenter);
        }
        void ShowEnding(Screen s)
        {
            ending.gameObject.SetActive(true); ending.SetAsLastSibling(); UIKit.FadeIn(ending, .6f);
            UIKit.Clear(endingButtons);
            var w = game.World; var c = w.community;
            int lost = 0, kills = 0, alive = 0;
            foreach (var m in w.survivors) if (m.faction == 0) { kills += m.kills; if (m.alive) alive++; else lost++; }
            int hearts = 0; foreach (var h in w.hearts) if (h.destroyed) hearts++;
            double days = Math.Max(0, w.HoursSurvived) / 24;
            endingStats.text = "Days survived <b>" + ((int)days) + "</b>  (" + (Math.Max(0, w.HoursSurvived)).ToString("0.0") + " h)     Members lost <b>" + lost + "</b>     Kills <b>" + kills + "</b>     Plague hearts destroyed <b>" + hearts + "</b>"
                + "\n<size=22><color=#aaa>Influence " + (int)c.influence + " · Morale " + (int)c.morale + " · Outposts " + c.outposts.Count + " · " + w.settings.mapSize + " m county, seed " + w.settings.seed + "</color></size>";
            if (s == Screen.Dead)
            {
                endingShade.color = new Color(.12f, 0, 0, .74f); endingTitle.color = UIKit.Bad; endingTitle.text = "COMMUNITY LOST";
                var last = game.Player;
                endingSub.text = (last != null && !string.IsNullOrEmpty(last.causeOfDeath) ? last.name + ": " + last.causeOfDeath + "\n" : "") + "<size=24><color=#bbb>No one is left to carry on. The county keeps its dead.</color></size>";
                UIKit.Primary(endingButtons, "New Community", () => { game.QuitToTitle(false); game.Show(Screen.NewGame); }, 440, 60, 26);
                UIKit.Button(endingButtons, "Main Menu", () => game.QuitToTitle(false), 440, 58, 26);
            }
            else if (s == Screen.Legacy)
            {
                endingShade.color = new Color(0, .05f, .03f, .74f); endingTitle.color = UIKit.Good; endingTitle.text = "LEGACY";
                endingSub.text = "Ashfall County cleansed. Every plague heart is destroyed.\n<size=24><color=#bbb>" + alive + " member" + (alive == 1 ? "" : "s") + " of your community saw it through. You can keep playing this county.</color></size>";
                UIKit.Primary(endingButtons, "Keep playing", () => game.Show(Screen.Playing), 440, 60, 26);
                UIKit.Button(endingButtons, "Main Menu", () => game.QuitToTitle(true), 440, 58, 26);
            }
            else
            {
                // Pre-refactor saves that ended with the old evacuation convoy: a neutral ending, then free play.
                endingShade.color = new Color(.02f, .03f, .04f, .78f); endingTitle.color = UIKit.Ink; endingTitle.text = "STORY ENDED";
                endingSub.text = "This save finished under the old evacuation rules.\n<size=24><color=#bbb>The county is still there. Your community can keep going.</color></size>";
                UIKit.Primary(endingButtons, "Keep playing", () => game.Show(Screen.Playing), 440, 60, 26);
                UIKit.Button(endingButtons, "Main Menu", () => game.QuitToTitle(true), 440, 58, 26);
            }
            Select(endingButtons);
        }

        // ---------------------------------------------------------------- session hooks
        /// <summary>Called when a session starts (new game or load): resets the HUD, logs and the county map.</summary>
        public void BeginSession(World w)
        {
            log.Clear(); logTimes.Clear(); history.Clear(); feedCount = 0; banners.Clear(); bannerUntil = 0; untracked.Clear();
            if (countyMap != null) countyMap.Destroy();
            countyMap = new UiCountyMap(w.map);
            ResetHud();
        }
        /// <summary>Control passed to another member: a switch, or the auto-switch after the controlled member died.</summary>
        public void ControlChanged(Survivor old, Survivor now)
        {
            if (now == null) return;
            if (old != null && !old.alive) { Banner(old.name.ToUpperInvariant() + " IS DEAD", "You now control " + now.name + ".", UIKit.Bad, 5); Log("<color=#ff8a7a>" + old.name + " died. Control passes to " + now.name + ".</color>"); }
            else Banner("NOW CONTROLLING", now.name + (string.IsNullOrEmpty(now.profession) ? "" : " · " + Profession.Get(now.profession).name), UIKit.Blue, 3);
            ResetHud();
            if (open.Count > 0 && open.Peek() == "community") RefreshCommunity();
        }
        public static string OrderName(CompanionOrder o) { switch (o) { case CompanionOrder.Hold: return "Hold position"; case CompanionOrder.Attack: return "Attack"; default: return "Follow me"; } }

        // ---------------------------------------------------------------- frame
        public void Tick()
        {
            float now = Time.unscaledTime;
            if (toastGroup != null) { float a = Mathf.Clamp01((toastUntil - now) / .35f); if (Mathf.Abs(toastGroup.alpha - a) > .001f) toastGroup.alpha = a; }
            if (loading.gameObject.activeSelf)
            {
                spinner.Rotate(0, 0, -240 * Time.unscaledDeltaTime); loadingText.text = game.LoadingText;
                loadingTime.SetText("{0:1} s", game.LoadingSeconds);
                tipClock -= Time.unscaledDeltaTime; if (tipClock <= 0) { tipClock = 6; loadingTip.text = "Tip: " + Tips[tipIndex++ % Tips.Length]; }
            }
            // Back: B on a controller everywhere; Esc too while in the menus (in a session Esc is the game's pause key).
            bool back = game.BackPressed || game.World == null && game.PausePressed && rebinding == null;
            bool overlayOpen = overlay != null;
            if (overlayOpen && back) CloseOverlay();
            if (newGame.gameObject.activeSelf && !overlayOpen && back && !TextEntryActive) game.Show(Screen.Title);
            else if (dropMap.gameObject.activeSelf && !overlayOpen) TickDropMap(back);
            else if (load.gameObject.activeSelf && back) game.Show(Screen.Title);
            if (title.gameObject.activeSelf && overlay == null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && (Gamepad.current != null && Gamepad.current.leftStick.ReadValue().sqrMagnitude > .3f || Keyboard.current != null && Keyboard.current.downArrowKey.wasPressedThisFrame)) Select(title);
            if (hud.gameObject.activeSelf && game.World != null) TickHud();
        }
    }
}
