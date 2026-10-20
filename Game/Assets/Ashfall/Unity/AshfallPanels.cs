using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ashfall.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ashfall.View
{
    public sealed partial class AshfallUI
    {
        // ---------------------------------------------------------------- HUD
        TextMeshProUGUI clock, weather, prompt, logText, weaponText, actionText, zombieCount, tipText;
        Image healthFill, enduranceFill, actionFill; TextMeshProUGUI healthLabel, enduranceLabel;
        RectTransform moodles, actionBar, hotbar;
        readonly List<string> log = new List<string>(); readonly List<float> logTimes = new List<float>();
        float hudClock;

        void BuildHud()
        {
            hud = UIKit.Fill(root, "HUD");
            var top = UIKit.Box2(hud, "Clock", new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), new Vector2(520, 100)); UIKit.Background(top, new Color(0, 0, 0, .45f), true).raycastTarget = false;
            clock = UIKit.Label(top, "", 30, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(16, 40), new Vector2(-16, -6)); clock.fontStyle = FontStyles.Bold;
            weather = UIKit.Label(top, "", 20, UIKit.Dim, Vector2.zero, Vector2.one, new Vector2(16, 6), new Vector2(-16, -56));
            moodles = UIKit.Rect(hud, "Moodles", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-300, -760), new Vector2(-20, -20)); UIKit.Column(moodles, 6, TextAnchor.UpperRight);
            healthFill = UIKit.Bar(hud, "Health", new Vector2(0, 0), new Vector2(24, 92), new Vector2(380, 34), new Color(.75f, .22f, .2f), out healthLabel);
            enduranceFill = UIKit.Bar(hud, "Endurance", new Vector2(0, 0), new Vector2(24, 50), new Vector2(380, 26), new Color(.36f, .62f, .8f), out enduranceLabel);
            weaponText = UIKit.Label(hud, "", 24, UIKit.Ink, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-620, 30), new Vector2(-24, 120), TextAlignmentOptions.BottomRight);
            hotbar = UIKit.Rect(hud, "Hotbar", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-370, 20), new Vector2(370, 104));
            var hb = hotbar.gameObject.AddComponent<HorizontalLayoutGroup>(); hb.spacing = 8; hb.childAlignment = TextAnchor.MiddleCenter; hb.childControlWidth = false; hb.childControlHeight = false;
            prompt = UIKit.Label(hud, "", 26, UIKit.Ink, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-600, 120), new Vector2(600, 200), TextAlignmentOptions.Bottom);
            logText = UIKit.Label(hud, "", 22, UIKit.Ink, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 140), new Vector2(820, 470), TextAlignmentOptions.BottomLeft);
            actionBar = UIKit.Box2(hud, "Action", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(320, 46));
            UIKit.Background(actionBar, new Color(0, 0, 0, .55f)).raycastTarget = false;
            var fill = UIKit.Rect(actionBar, "Fill", Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3)); actionFill = fill.gameObject.AddComponent<Image>(); actionFill.sprite = UIKit.White; actionFill.type = Image.Type.Filled; actionFill.fillMethod = Image.FillMethod.Horizontal; actionFill.color = UIKit.Accent;
            actionText = UIKit.Label(actionBar, "", 20, UIKit.Ink, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
            zombieCount = UIKit.Label(hud, "", 18, UIKit.Dim, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -150), new Vector2(520, -122));
            tipText = UIKit.Label(hud, "", 19, UIKit.Dim, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-760, 130), new Vector2(-24, 230), TextAlignmentOptions.BottomRight);
        }

        public void Log(string text)
        {
            log.Add(text); logTimes.Add(Time.unscaledTime);
            if (log.Count > 8) { log.RemoveAt(0); logTimes.RemoveAt(0); }
            history.Add("<color=#888>" + (game.World == null ? "" : game.World.Clock) + "</color>  " + text); if (history.Count > 200) history.RemoveAt(0);
        }
        readonly List<string> history = new List<string>();

        void TickHud()
        {
            var s = game.Player; var w = game.World; if (s == null) return;
            hudClock -= Time.unscaledDeltaTime; bool slow = hudClock <= 0; if (slow) hudClock = .15f;
            healthFill.fillAmount = s.health / 100; healthLabel.text = "Health " + Mathf.CeilToInt(s.health);
            enduranceFill.fillAmount = s.endurance / 100; enduranceLabel.text = "Endurance";
            var a = s.action;
            actionBar.gameObject.SetActive(a != null && (a.kind != ActionKind.Sleep));
            if (a != null && a.kind != ActionKind.Sleep) { actionFill.fillAmount = a.Progress; actionText.text = ActionName(a); }
            if (!slow) return;
            clock.text = w.Clock;
            weather.text = (w.raining ? "Rain" : w.fog > .2f ? "Fog" : w.Daylight > .5f ? "Clear" : "Night") + " · Power " + (w.PowerOn ? "on" : "off") + " · Water " + (w.WaterOn ? "on" : "off") + (s.sleeping ? " · <color=#9fd3ff>Sleeping (E to wake)</color>" : "");
            int near = 0; foreach (var id in game.Sim.visible.ContainsKey(s.id) ? game.Sim.visible[s.id] : new HashSet<int>()) near++;
            zombieCount.text = near > 0 ? "<color=#ff9a80>" + near + " zombie" + (near > 1 ? "s" : "") + " in sight</color>" : "";
            RefreshMoodles(s);
            var p = s.Primary; var sec = s.Secondary;
            string wtxt = p == null ? "Bare hands" : p.Label;
            if (p != null && p.Def.category == ItemCategory.Firearm) wtxt += "\n<size=20>" + s.inventory.Count(p.Def.ammo) + " spare " + ItemCatalog.Get(p.Def.ammo).name + "</size>";
            if (sec != null) wtxt += "\n<size=20>Off hand: " + sec.Label + (s.lightOn ? " (on)" : "") + "</size>";
            wtxt += "\n<size=20>Load " + s.CarriedWeight.ToString("0.0") + " / " + s.Capacity.ToString("0") + "</size>";
            var car = w.VehicleOf(s);
            if (car != null) wtxt = "<b>" + car.model + "</b>  " + Mathf.RoundToInt(Mathf.Abs(car.speed) * 3.6f) + " km/h" + (car.engineOn ? "" : "  <color=#ff9a80>engine off</color>")
                + "\n<size=20>Fuel " + Mathf.RoundToInt(car.fuel / Vehicle.TankLiters * 100) + "% · Condition " + Mathf.RoundToInt(car.condition * 100) + "%</size>\n<size=20>W/S drive · A/D steer · Space brake · E/R engine</size>";
            weaponText.text = wtxt;
            // Context prompt.
            var f = game.Focus;
            if (s.sleeping) prompt.text = "Sleeping... [E] wake up";
            else if (a != null && a.kind != ActionKind.Sleep) prompt.text = "[E] cancel";
            else if (f != null) { var first = f.actions.FirstOrDefault(x => x.enabled); prompt.text = f.label + (first != null ? "   <color=#f0b050>[E]</color> " + first.label : "") + (f.actions.Count > 1 ? "   <color=#f0b050>[Q]</color> more" : ""); }
            else prompt.text = "";
            // Message log fades.
            var lines = new List<string>();
            for (int i = 0; i < log.Count; i++) { float age = Time.unscaledTime - logTimes[i]; if (age < 12) lines.Add(age > 9 ? "<alpha=#77>" + log[i] : log[i]); }
            logText.text = string.Join("\n", lines);
            RefreshHotbar();
            tipText.text = game.Prefs.showTips ? Tip(s, w) : "";
        }

        string Tip(Survivor s, World w)
        {
            if (w.HoursSurvived < .4) return "Search the house: press <color=#f0b050>E</color> at cupboards and the fridge. <color=#f0b050>Tab</color> opens your inventory.";
            if (s.Primary == null) return "Find a weapon. A plank, a bat or a kitchen knife beats bare hands. <color=#f0b050>Space</color> shoves zombies back.";
            if (s.hunger > 45 || s.thirst > 45) return "Eat and drink from your inventory (<color=#f0b050>Tab</color>, select an item, Use). Tap water runs until the utilities fail.";
            if (s.Bleeding) return "You are bleeding! Open <color=#f0b050>H</color> (health) and bandage the wound, or rip a sheet for rags (<color=#f0b050>B</color>).";
            if (w.Daylight < .3f && !s.lightOn) return "It's dark. Find a flashlight and batteries; zombies see you less in the dark, but you see less too.";
            if (w.settings.evacuationDay > 0 && w.Day >= w.settings.evacuationDay - 1 && w.outcome == "") return "The convoy waits at the Ash River highway bridge (west bank), Day " + w.settings.evacuationDay + " 8 AM – 2 PM. Open the map with <color=#f0b050>M</color>.";
            return "";
        }

        static string ActionName(TimedAction a)
        {
            switch (a.kind)
            {
                case ActionKind.Craft: var r = Recipe.Get(a.recipe); return "Crafting " + (r == null ? "" : r.name);
                case ActionKind.Barricade: return "Barricading"; case ActionKind.FillWater: return "Filling water"; case ActionKind.ClearGlass: return "Clearing glass";
                case ActionKind.Hotwire: return "Hotwiring"; case ActionKind.Refuel: return "Refuelling";
                case ActionKind.DigPlot: return "Digging a plot"; case ActionKind.Plant: return "Planting"; case ActionKind.WaterPlot: return "Watering";
                case ActionKind.Fish: return "Fishing"; case ActionKind.ReadBook: return "Studying"; case ActionKind.Smoke: return "Smoking";
                default: return a.kind.ToString() + "ing".Replace("eing", "ing");
            }
        }

        void RefreshMoodles(Survivor s)
        {
            UIKit.Clear(moodles);
            void M(string text, Color c, string icon = null)
            {
                var r = UIKit.Rect(moodles, text, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 40; le.preferredWidth = 280;
                UIKit.Background(r, new Color(c.r * .25f, c.g * .25f, c.b * .25f, .8f)).raycastTarget = false;
                var spr = icon == null ? null : UIKit.Icon(icon);
                if (spr != null) { var ic = UIKit.Box2(r, "Icon", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(6, 0), new Vector2(32, 32)); var im = ic.gameObject.AddComponent<Image>(); im.sprite = spr; im.color = c; im.raycastTarget = false; }
                UIKit.Label(r, text, 21, c, Vector2.zero, Vector2.one, new Vector2(spr != null ? 44 : 12, 0), new Vector2(-8, 0), TextAlignmentOptions.MidlineLeft);
            }
            Color Lv(float v, float a, float b, float c) { return v > c ? UIKit.Bad : v > b ? new Color(1, .55f, .25f) : UIKit.Warn; }
            if (s.hunger > 25) M(s.hunger > 90 ? "Starving" : s.hunger > 70 ? "Very hungry" : s.hunger > 45 ? "Hungry" : "Peckish", Lv(s.hunger, 25, 45, 70), "ICON_Status_Hunger");
            if (s.thirst > 25) M(s.thirst > 90 ? "Dehydrated" : s.thirst > 70 ? "Parched" : s.thirst > 45 ? "Thirsty" : "Dry mouth", Lv(s.thirst, 25, 45, 70), "ICON_Status_Thirst");
            if (s.fatigue > 50) M(s.fatigue > 90 ? "Exhausted" : s.fatigue > 75 ? "Very tired" : "Tired", Lv(s.fatigue, 50, 75, 90));
            if (s.endurance < 40) M(s.endurance < 15 ? "Out of breath" : "Winded", s.endurance < 15 ? UIKit.Bad : UIKit.Warn);
            if (s.Bleeding) M("Bleeding", UIKit.Bad, "ICON_Status_Bleeding");
            if (s.health < 80) M(s.health < 30 ? "Critically injured" : s.health < 55 ? "Badly injured" : "Injured", Lv(100 - s.health, 20, 45, 70), "ICON_Status_Health02");
            if (s.pain > 15) M(s.pain > 50 ? "Agony" : s.pain > 30 ? "Severe pain" : "Pain", Lv(s.pain, 15, 30, 50));
            if (s.panic > 20) M(s.panic > 60 ? "Panicked" : s.panic > 40 ? "Scared" : "Anxious", Lv(s.panic, 20, 40, 60));
            if (s.sickness > 25) M(s.sickness > 75 ? "Fever" : s.sickness > 50 ? "Nauseous" : "Queasy", Lv(s.sickness, 25, 50, 75), "ICON_Status_Radiation");
            if (s.wetness > 30) M(s.wetness > 70 ? "Soaked" : "Damp", UIKit.Warn, "ICON_Status_Cold");
            if (s.Encumbrance > 1) M(s.Encumbrance > 1.5f ? "Overloaded" : "Heavy load", s.Encumbrance > 1.5f ? UIKit.Bad : UIKit.Warn, "ICON_Inventory_Backpack");
            if (s.boredom > 60) M("Bored", UIKit.Warn);
            if (s.HasTrait("smoker") && s.hoursSinceSmoke >= 8) M(s.hoursSinceSmoke >= 16 ? "Desperate for a smoke" : "Craving a cigarette", s.hoursSinceSmoke >= 16 ? new Color(1, .55f, .25f) : UIKit.Warn);
            if (s.wellFedHours > 3 && s.health > 90) M("Well fed", UIKit.Good);
            if (s.sneaking) M("Sneaking", new Color(.6f, .8f, 1));
        }

        void RefreshHotbar()
        {
            var bar = game.Hotbar(); var s = game.Player;
            if (hotbar.childCount != 6) { UIKit.Clear(hotbar); for (int i = 0; i < 6; i++) { var slot = UIKit.Box2(hotbar, "Slot " + i, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(112, 84)); UIKit.Background(slot, new Color(0, 0, 0, .5f), true).raycastTarget = false; var ic = UIKit.Box2(slot, "Icon", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 8), new Vector2(54, 54)); var img = ic.gameObject.AddComponent<Image>(); img.raycastTarget = false; img.preserveAspect = true; UIKit.Label(slot, "", 15, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(4, 2), new Vector2(-4, -2), TextAlignmentOptions.Bottom); UIKit.Label(slot, (i + 1).ToString(), 15, UIKit.Dim, Vector2.zero, Vector2.one, new Vector2(6, 0), new Vector2(0, -4), TextAlignmentOptions.TopLeft); } }
            for (int i = 0; i < 6; i++)
            {
                var slot = hotbar.GetChild(i); var img = slot.Find("Icon").GetComponent<Image>(); var texts = slot.GetComponentsInChildren<TextMeshProUGUI>();
                var bg = slot.GetComponent<Image>();
                if (i < bar.Count)
                {
                    var item = bar[i]; var spr = UIKit.Icon(UIKit.ItemIcon(item.Def));
                    img.enabled = spr != null; img.sprite = spr; texts[0].text = item.Def.name;
                    bool eq = s.primary == item.uid || s.secondary == item.uid; bg.color = eq ? new Color(.5f, .3f, .15f, .8f) : new Color(0, 0, 0, .5f);
                }
                else { img.enabled = false; texts[0].text = ""; bg.color = new Color(0, 0, 0, .3f); }
            }
        }

        // ---------------------------------------------------------------- panels
        RectTransform invPlayer, invLoot, invTabs, invActions, craftList, healthBody, healthSkills, journalList, contextList, mapImageRoot;
        TextMeshProUGUI invTitle, lootTitle, invDetail, craftDetail; RawImage mapImage, fogImage; RectTransform mapMarkers;
        int selectedItem = -1; bool selectedFromLoot; int lootIndex; string selectedRecipe;

        RectTransform PanelFrame(string id, string heading, Vector2 min, Vector2 max)
        {
            var r = UIKit.Rect(root, id, min, max, Vector2.zero, Vector2.zero); UIKit.Background(r, UIKit.Panel, true);
            UIKit.Label(r, heading, 34, UIKit.Rust, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -60), new Vector2(-24, -10)).fontStyle = FontStyles.Bold;
            UIKit.Button(UIKit.Box2(r, "Close", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -12), new Vector2(60, 48)), "X", () => CloseTopPanel(), 60, 48, 24);
            r.gameObject.SetActive(false); panels[id] = r; return r;
        }

        void BuildPanels()
        {
            // Inventory and loot side by side.
            var inv = PanelFrame("inventory", "INVENTORY", new Vector2(.06f, .1f), new Vector2(.94f, .92f));
            invTitle = UIKit.Label(inv, "", 22, UIKit.Dim, new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(24, -96), new Vector2(-10, -60));
            var leftArea = UIKit.Rect(inv, "Player", new Vector2(0, 0), new Vector2(.5f, 1), new Vector2(24, 230), new Vector2(-10, -100)); UIKit.Scroll(leftArea, "Items", out invPlayer);
            invTabs = UIKit.Rect(inv, "Tabs", new Vector2(.5f, 1), new Vector2(1, 1), new Vector2(10, -100), new Vector2(-24, -56));
            var tabs = invTabs.gameObject.AddComponent<HorizontalLayoutGroup>(); tabs.spacing = 6; tabs.childControlWidth = false; tabs.childControlHeight = false; tabs.childAlignment = TextAnchor.MiddleLeft;
            lootTitle = UIKit.Label(inv, "", 22, UIKit.Dim, new Vector2(.5f, 1), new Vector2(1, 1), new Vector2(10, -132), new Vector2(-24, -102));
            var rightArea = UIKit.Rect(inv, "Loot", new Vector2(.5f, 0), new Vector2(1, 1), new Vector2(10, 230), new Vector2(-24, -136)); UIKit.Scroll(rightArea, "Loot items", out invLoot);
            invDetail = UIKit.Label(inv, "", 21, UIKit.Ink, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 140), new Vector2(-24, 222));
            invActions = UIKit.Rect(inv, "Actions", new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 20), new Vector2(-24, 132));
            var g = invActions.gameObject.AddComponent<GridLayoutGroup>(); g.cellSize = new Vector2(250, 50); g.spacing = new Vector2(8, 8);

            var craft = PanelFrame("crafting", "CRAFTING", new Vector2(.12f, .1f), new Vector2(.88f, .92f));
            var ca = UIKit.Rect(craft, "Recipes", new Vector2(0, 0), new Vector2(.45f, 1), new Vector2(24, 24), new Vector2(-10, -80)); UIKit.Scroll(ca, "List", out craftList);
            craftDetail = UIKit.Label(craft, "", 24, UIKit.Ink, new Vector2(.45f, 0), new Vector2(1, 1), new Vector2(20, 120), new Vector2(-24, -90));
            var craftButton = UIKit.Button(UIKit.Box2(craft, "CraftButton", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(320, 64)), "Craft", () => { if (selectedRecipe != null) game.Submit(new Command { kind = CommandKind.Craft, recipe = selectedRecipe }); }, 320, 64, 28);

            var health = PanelFrame("health", "HEALTH & SKILLS", new Vector2(.12f, .1f), new Vector2(.88f, .92f));
            var ha = UIKit.Rect(health, "Body", new Vector2(0, 0), new Vector2(.55f, 1), new Vector2(24, 24), new Vector2(-10, -80)); UIKit.Scroll(ha, "Wounds", out healthBody);
            var hs = UIKit.Rect(health, "Skills", new Vector2(.55f, 0), new Vector2(1, 1), new Vector2(10, 24), new Vector2(-24, -80)); UIKit.Scroll(hs, "SkillList", out healthSkills);

            var journal = PanelFrame("journal", "RADIO LOG & GOALS", new Vector2(.15f, .1f), new Vector2(.85f, .92f));
            var ja = UIKit.Rect(journal, "Log", Vector2.zero, Vector2.one, new Vector2(24, 24), new Vector2(-24, -80)); UIKit.Scroll(ja, "Entries", out journalList);

            var context = PanelFrame("context", "ACTIONS", new Vector2(.32f, .18f), new Vector2(.68f, .82f));
            var cl = UIKit.Rect(context, "List", Vector2.zero, Vector2.one, new Vector2(24, 24), new Vector2(-24, -80)); UIKit.Scroll(cl, "Actions", out contextList);

            var map = PanelFrame("map", "ASHFALL COUNTY MAP", new Vector2(.08f, .04f), new Vector2(.92f, .96f));
            mapImageRoot = UIKit.Rect(map, "Viewport", Vector2.zero, Vector2.one, new Vector2(24, 24), new Vector2(-24, -80)); mapImageRoot.gameObject.AddComponent<RectMask2D>(); UIKit.Background(mapImageRoot, new Color(.05f, .05f, .05f, 1));
            var mi = UIKit.Box2(mapImageRoot, "Map", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1000, 1000)); mapImage = mi.gameObject.AddComponent<RawImage>(); mapImage.raycastTarget = false;
            var fi = UIKit.Fill(mi, "Fog"); fogImage = fi.gameObject.AddComponent<RawImage>(); fogImage.raycastTarget = false;
            mapMarkers = UIKit.Fill(mi, "Markers");
            UIKit.Label(map, "Wheel / triggers zoom · drag or stick to pan · unexplored areas are dark", 18, UIKit.Dim, new Vector2(0, 0), new Vector2(1, 0), new Vector2(30, 28), new Vector2(-30, 56), TextAlignmentOptions.BottomRight);
        }

        public bool CloseTopPanel()
        {
            if (overlay != null) { CloseOverlay(); return true; }
            if (open.Count == 0) return false;
            var id = open.Pop(); panels[id].gameObject.SetActive(false);
            if (id == "inventory" && game.Player != null) game.Submit(Command.Of(CommandKind.CloseContainer));
            game.Audio.Ui("close");
            if (open.Count > 0) Select(panels[open.Peek()]); else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            return true;
        }
        public void Toggle(string id)
        {
            if (open.Count > 0 && open.Peek() == id) { CloseTopPanel(); return; }
            while (open.Count > 0) CloseTopPanel();
            Open(id);
        }
        void Open(string id)
        {
            var r = panels[id]; r.gameObject.SetActive(true); r.SetAsLastSibling(); open.Push(id); game.Audio.Ui("open");
            switch (id)
            {
                case "inventory": selectedItem = -1; lootIndex = 0; RefreshInventory(); break;
                case "crafting": RefreshCrafting(); break;
                case "health": RefreshHealth(); break;
                case "journal": RefreshJournal(); break;
                case "map": RefreshMap(); break;
                case "context": RefreshContext(); break;
            }
            Select(r);
            if (toast != null) toast.transform.SetAsLastSibling();
        }
        public void OpenInventory() { if (open.Count > 0 && open.Peek() == "inventory") { RefreshInventory(); return; } while (open.Count > 0) CloseTopPanel(); Open("inventory"); }
        public void OpenContext() { if (game.Nearby.Count == 0) return; while (open.Count > 0) CloseTopPanel(); Open("context"); }

        float panelRefresh;
        void LateUpdate()
        {
            if (open.Count == 0 || game.World == null) return;
            panelRefresh -= Time.unscaledDeltaTime; if (panelRefresh > 0) return; panelRefresh = .35f;
            var top = open.Peek();
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            // Refresh content without stealing controller focus.
            if (top == "inventory") RefreshInventory(true);
            else if (top == "health") RefreshHealth(true);
            else if (top == "crafting") RefreshCraftDetail();
            if (top == "map") PanMap();
            if (selected != null && selected.activeInHierarchy && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(selected);
        }

        // ---------------------------------------------------------------- inventory
        string lastInvSignature = "";
        void RefreshInventory(bool soft = false)
        {
            var s = game.Player; if (s == null) return;
            var sources = game.LootSources();
            if (lootIndex >= sources.Count) lootIndex = 0;
            string sig = string.Join(",", s.inventory.items.Select(i => i.uid + ":" + i.count + ":" + i.water + ":" + i.loaded + ":" + (s.IsEquipped(i.uid) ? 1 : 0))) + "|" + string.Join(";", sources.Select(x => x.label + x.inventory.items.Count + string.Join(",", x.inventory.items.Select(i => i.uid + ":" + i.count)))) + "|" + selectedItem + selectedFromLoot + lootIndex;
            if (soft && sig == lastInvSignature) return; lastInvSignature = sig;
            invTitle.text = s.name + " · carrying " + s.CarriedWeight.ToString("0.0") + " / " + s.Capacity.ToString("0") + (s.Encumbrance > 1 ? " <color=#ff9a80>(over capacity: slower, tires faster)</color>" : "");
            UIKit.Clear(invPlayer);
            foreach (var item in s.inventory.items.OrderBy(i => s.IsEquipped(i.uid) ? 0 : 1).ThenBy(i => i.Def.category).ThenBy(i => i.Def.name)) ItemRow(invPlayer, item, false, s);
            if (s.inventory.items.Count == 0) UIKit.Text(invPlayer, "Empty pockets.", 22, UIKit.Dim).gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            UIKit.Clear(invTabs);
            for (int i = 0; i < sources.Count; i++) { int idx = i; var b = UIKit.Button(invTabs, sources[i].label + " (" + sources[i].inventory.items.Count + ")", () => { lootIndex = idx; selectedItem = -1; RefreshInventory(); }, 210, 40, 18); if (i == lootIndex) b.GetComponent<Image>().color = new Color(1.4f, 1.1f, .8f); }
            UIKit.Clear(invLoot);
            if (sources.Count > 0)
            {
                var src = sources[lootIndex];
                lootTitle.text = src.label + " · " + src.inventory.items.Count + " items" + (src.where.kind == InvKind.Container ? " · " + src.inventory.Weight.ToString("0.0") + " / " + src.inventory.capacity.ToString("0") : "");
                foreach (var item in src.inventory.items.OrderBy(i => i.Def.category).ThenBy(i => i.Def.name)) ItemRow(invLoot, item, true, s);
                if (src.inventory.items.Count == 0) UIKit.Text(invLoot, "Nothing here.", 22, UIKit.Dim).gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            }
            // Selected item details and actions.
            UIKit.Clear(invActions);
            ItemStack sel = null; InvRef from = InvRef.Player; InvRef to = sources.Count > 0 ? sources[lootIndex].where : InvRef.Player;
            if (selectedItem >= 0)
            {
                if (selectedFromLoot && sources.Count > 0) { sel = sources[lootIndex].inventory.ByUid(selectedItem); from = sources[lootIndex].where; }
                else sel = s.inventory.ByUid(selectedItem);
            }
            if (sel == null) { invDetail.text = "Select an item. Selecting it again moves it between your inventory and the open container."; if (sources.Count > 0 && sources[lootIndex].inventory.items.Count > 0) { var src = sources[lootIndex]; UIKit.Button(invActions, "Take everything", () => { foreach (var it in src.inventory.items.ToList()) game.Submit(new Command { kind = CommandKind.Transfer, item = it.uid, count = 0, from = src.where, to = InvRef.Player }); }, 250, 50, 20); } return; }
            var d = sel.Def;
            invDetail.text = "<b>" + sel.Label + "</b>  <color=#aaa>" + d.category + " · " + sel.Weight.ToString("0.00") + " kg" + Describe(sel) + "</color>";
            int uid = sel.uid;
            if (selectedFromLoot)
            {
                var src = from;
                UIKit.Button(invActions, "Take", () => game.Submit(new Command { kind = CommandKind.Transfer, item = uid, count = 0, from = src, to = InvRef.Player }), 250, 50, 20);
                if (sel.count > 1) UIKit.Button(invActions, "Take one", () => game.Submit(new Command { kind = CommandKind.Transfer, item = uid, count = 1, from = src, to = InvRef.Player }), 250, 50, 20);
                return;
            }
            if (d.bookSkill >= 0 || d.Has("smoke") || d.Has("fishing")) UIKit.Button(invActions, d.bookSkill >= 0 ? (s.booksRead.Contains(d.id) ? "Read (already studied)" : "Read") : d.Has("smoke") ? "Smoke" : "Fish here", () => game.Submit(new Command { kind = CommandKind.Use, item = uid }), 250, 50, 20);
            if (d.Edible || d.category == ItemCategory.Medical || d.category == ItemCategory.Light || d.id == "magazine" || d.id == "battery") UIKit.Button(invActions, d.category == ItemCategory.Food ? "Eat" : d.category == ItemCategory.Drink ? "Drink" : d.category == ItemCategory.Light ? (s.lightOn && s.secondary == uid ? "Turn off" : "Turn on") : d.id == "magazine" ? "Read" : "Use", () => game.Submit(new Command { kind = CommandKind.Use, item = uid }), 250, 50, 20);
            if (d.IsWeapon && d.category != ItemCategory.Material || d.category == ItemCategory.Material && d.damage > 0) UIKit.Button(invActions, s.primary == uid ? "Unequip" : "Equip (main hand)", () => game.Submit(new Command { kind = s.primary == uid ? CommandKind.Unequip : CommandKind.Equip, item = uid, slot = 0 }), 250, 50, 20);
            if (d.category == ItemCategory.Light || d.category == ItemCategory.Tool || d.category == ItemCategory.Weapon && !d.twoHanded) UIKit.Button(invActions, s.secondary == uid ? "Unequip off hand" : "Equip off hand", () => game.Submit(new Command { kind = s.secondary == uid ? CommandKind.Unequip : CommandKind.Equip, item = uid, slot = 1 }), 250, 50, 20);
            if (d.slot != WearSlot.None) { bool worn = s.worn.ContainsValue(uid); UIKit.Button(invActions, worn ? "Take off" : "Wear", () => game.Submit(worn ? new Command { kind = CommandKind.Unwear, slot = (int)d.slot } : new Command { kind = CommandKind.Wear, item = uid }), 250, 50, 20); }
            if (!string.IsNullOrEmpty(d.cooksInto) && !sel.cooked && game.Sim.NearHeat(s)) UIKit.Button(invActions, "Cook", () => game.Submit(new Command { kind = CommandKind.Cook, item = uid }), 250, 50, 20);
            if (d.waterCapacity > 0 && sel.tainted && sel.water > 0 && game.Sim.NearHeat(s)) UIKit.Button(invActions, "Boil water", () => game.Submit(new Command { kind = CommandKind.Boil, item = uid }), 250, 50, 20);
            if (sources.Count > 0) { var dest = to; UIKit.Button(invActions, "Put in " + sources[lootIndex].label.ToLowerInvariant(), () => game.Submit(new Command { kind = CommandKind.Transfer, item = uid, count = 0, from = InvRef.Player, to = dest }), 250, 50, 20); }
            UIKit.Button(invActions, "Drop", () => game.Submit(new Command { kind = CommandKind.DropAll, item = uid }), 250, 50, 20);
        }
        static string Describe(ItemStack s)
        {
            var d = s.Def; var parts = new List<string>();
            if (d.hunger < 0) parts.Add("hunger " + d.hunger.ToString("0")); if (d.thirst < 0) parts.Add("thirst " + d.thirst.ToString("0"));
            if (d.perishDays > 0) parts.Add(s.Rotten ? "rotten" : s.Stale ? "stale" : "fresh " + (int)(s.Freshness * 100) + "%");
            if (!string.IsNullOrEmpty(d.needsTool)) parts.Add("needs a can opener");
            if (d.raw && !s.cooked) parts.Add("raw: cook first");
            if (d.damage > 0) parts.Add("damage " + d.damage.ToString("0.00") + (d.twoHanded ? ", two-handed" : "") + (d.category == ItemCategory.Firearm ? ", very loud" : ""));
            if (d.scratchProtection > 0) parts.Add("scratch " + (int)(d.scratchProtection * 100) + "% / bite " + (int)(d.biteProtection * 100) + "%");
            if (d.capacity > 0) parts.Add("+" + d.capacity + " capacity");
            if (d.bandageQuality > 0) parts.Add("treats bleeding wounds");
            if (d.bookSkill >= 0) parts.Add((Skill)d.bookSkill + " experience ×" + d.bookMultiplier.ToString("0") + " for levels " + d.bookMinLevel + "–" + (d.bookMaxLevel - 1) + " once read");
            var crop = CropDef.ForSeed(d.id); if (crop != null) parts.Add("plant in a dug plot; ripens in about " + crop.growDays.ToString("0") + " days if watered");
            if (d.Has("watering")) parts.Add("fill at a tap, rain collector or river; waters farm plots");
            if (d.Has("fishing")) parts.Add("fish at a river or pond edge with worms or a lure");
            if (d.Has("bait") || d.Has("lure")) parts.Add("fishing bait");
            if (d.Has("smoke")) parts.Add("calms smokers; needs a lighter or matches");
            return parts.Count == 0 ? "" : " · " + string.Join(", ", parts);
        }
        void ItemRow(RectTransform list, ItemStack item, bool loot, Survivor s)
        {
            int uid = item.uid;
            bool selected = selectedItem == uid && selectedFromLoot == loot;
            var b = UIKit.Button(list, "", () =>
            {
                if (selectedItem == uid && selectedFromLoot == loot)
                {
                    // Second click moves the item.
                    var sources = game.LootSources(); if (sources.Count == 0) return; var other = sources[Mathf.Min(lootIndex, sources.Count - 1)].where;
                    game.Submit(new Command { kind = CommandKind.Transfer, item = uid, count = 0, from = loot ? other : InvRef.Player, to = loot ? InvRef.Player : other });
                    selectedItem = -1;
                }
                else { selectedItem = uid; selectedFromLoot = loot; }
                RefreshInventory();
            }, 700, 46, 20);
            var t = b.GetComponentInChildren<TextMeshProUGUI>(); t.alignment = TextAlignmentOptions.MidlineLeft; t.rectTransform.offsetMin = new Vector2(56, 0);
            string tag = s.primary == uid ? " <color=#f0b050>[main hand]</color>" : s.secondary == uid ? " <color=#f0b050>[off hand]</color>" : s.worn.ContainsValue(uid) ? " <color=#9fd3ff>[worn]</color>" : "";
            t.text = item.Label + (loot ? "" : tag) + "  <color=#888><size=16>" + item.Weight.ToString("0.0") + "</size></color>";
            var spr = UIKit.Icon(UIKit.ItemIcon(item.Def));
            if (spr != null) { var ic = UIKit.Box2(b.transform, "Icon", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(8, 0), new Vector2(38, 38)); var img = ic.gameObject.AddComponent<Image>(); img.sprite = spr; img.preserveAspect = true; img.raycastTarget = false; }
            if (selected) b.GetComponent<Image>().color = new Color(1.6f, 1.2f, .8f);
        }

        // ---------------------------------------------------------------- crafting
        void RefreshCrafting()
        {
            UIKit.Clear(craftList); var s = game.Player; bool heat = game.Sim.NearHeat(s);
            foreach (var r in Recipe.All.OrderBy(r => r.Missing(s, heat).Count > 0))
            {
                var rec = r; bool can = r.Missing(s, heat).Count == 0;
                var b = UIKit.Button(craftList, (can ? "<color=#9fe08a>●</color> " : "<color=#777>○</color> ") + r.name, () => { selectedRecipe = rec.id; RefreshCraftDetail(); }, 600, 50, 22);
                b.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
            }
            if (selectedRecipe == null) selectedRecipe = Recipe.All[0].id;
            RefreshCraftDetail();
        }
        void RefreshCraftDetail()
        {
            var r = Recipe.Get(selectedRecipe); var s = game.Player; if (r == null || s == null) return;
            var missing = r.Missing(s, game.Sim.NearHeat(s));
            var needs = r.inputs.Select(i => i.Label).Concat(r.tools.Select(t => Recipe.TagName(t) + " (tool, kept)")).ToList();
            if (r.water > 0) needs.Add(r.water + " clean water"); if (r.anyCount > 0) needs.Add(r.anyCount + " of: vegetables, meat, eggs or rice"); if (r.needsHeat) needs.Add("A lit campfire or working stove nearby");
            craftDetail.text = "<b><size=30>" + r.name + "</size></b>\n" + r.description + "\n\n<color=#aaa>Needs:</color> " + string.Join(", ", needs) + "\n<color=#aaa>Skill:</color> " + r.skill + (r.minLevel > 0 ? " " + r.minLevel : "") + " · " + r.time.ToString("0") + " s" +
                (r.places ? "\n<color=#aaa>Builds in front of you.</color>" : "\n<color=#aaa>Makes:</color> " + ItemCatalog.Get(r.result).name + (r.resultCount > 1 ? " ×" + r.resultCount : "")) +
                (missing.Count == 0 ? "\n\n<color=#9fe08a>Ready to craft.</color>" : "\n\n<color=#ff9a80>Missing: " + string.Join(", ", missing) + "</color>");
        }

        // ---------------------------------------------------------------- health
        string lastHealthSig = "";
        void RefreshHealth(bool soft = false)
        {
            var s = game.Player; if (s == null) return;
            string sig = string.Join(";", s.wounds.Select(w => w.Label)) + s.inventory.items.Count + (int)s.health + string.Join(",", s.level) + s.booksRead.Count;
            if (soft && sig == lastHealthSig) return; lastHealthSig = sig;
            UIKit.Clear(healthBody);
            UIKit.Text(healthBody, "Health " + Mathf.CeilToInt(s.health) + " · Hunger " + (int)s.hunger + " · Thirst " + (int)s.thirst + " · Fatigue " + (int)s.fatigue + " · Pain " + (int)s.pain + " · Sickness " + (int)s.sickness, 22, UIKit.Ink).gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
            if (s.wounds.Count == 0) UIKit.Text(healthBody, "No wounds.", 24, UIKit.Good).gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            var meds = s.inventory.items.Where(i => i.Def.bandageQuality > 0 || i.Def.disinfect > 0 || i.def == "suture_kit").ToList();
            for (int i = 0; i < s.wounds.Count; i++)
            {
                var w = s.wounds[i]; int index = i;
                UIKit.Text(healthBody, (w.bleeding ? "<color=#ff6050>" : w.infected ? "<color=#e0c050>" : "") + w.Label + " · healing " + (int)(100 - Mathf.Clamp01(w.severity / 1.4f) * 100) + "%", 22, UIKit.Ink).gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
                var row = UIKit.Row(healthBody, 46, 6);
                foreach (var m in meds.Take(4)) { int uid = m.uid; UIKit.Button(row.transform, "Apply " + m.Def.name, () => game.Submit(new Command { kind = CommandKind.Treat, item = uid, target = index }), 230, 42, 18); }
                if (meds.Count == 0) UIKit.RowLabel(row.transform, "No bandages, rags or disinfectant. Rip a sheet or clothing in Crafting (B).", 0, 18, UIKit.Dim);
            }
            foreach (var pill in s.inventory.items.Where(i => i.def == "painkillers" || i.def == "antibiotics").Take(2)) { int uid = pill.uid; UIKit.Button(healthBody, "Take " + pill.Def.name, () => game.Submit(new Command { kind = CommandKind.Use, item = uid }), 400, 46, 20); }
            UIKit.Clear(healthSkills);
            UIKit.Text(healthSkills, Profession.Get(s.profession).name + " · " + s.kills + " zombies killed", 24, UIKit.Accent).gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            if (s.traits.Count > 0) UIKit.Text(healthSkills, "Traits: " + string.Join(", ", s.traits.Select(t => Trait.Get(t) == null ? t : Trait.Get(t).name)), 20, UIKit.Ink).gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            if (s.HasTrait("smoker")) UIKit.Text(healthSkills, s.hoursSinceSmoke >= 8 ? "<color=#e0c050>Craving a cigarette (" + (int)s.hoursSinceSmoke + " h)</color>" : "Last cigarette " + (int)s.hoursSinceSmoke + " h ago", 18, UIKit.Dim).gameObject.AddComponent<LayoutElement>().preferredHeight = 26;
            foreach (Skill k in Enum.GetValues(typeof(Skill)))
            {
                int lv = s.Level(k); float next = lv >= 10 ? 1 : s.xp[(int)k] / Survivor.XpCurve[lv];
                float book = Simulation.BookMultiplier(s, k);
                var row = UIKit.Row(healthSkills, 40, 8); UIKit.RowLabel(row.transform, k == Skill.FirstAid ? "First Aid" : k.ToString(), 200, 22);
                UIKit.RowLabel(row.transform, new string('■', lv) + "<color=#444>" + new string('■', 10 - lv) + "</color>", 260, 22, UIKit.Accent);
                UIKit.RowLabel(row.transform, (lv >= 10 ? "max" : (int)(next * 100) + "%") + (book > 1 ? "  <color=#9fd3ff>book ×" + book.ToString("0") + "</color>" : ""), 160, 18, UIKit.Dim);
            }
        }

        // ---------------------------------------------------------------- journal and context
        void RefreshJournal()
        {
            UIKit.Clear(journalList); var w = game.World;
            string goal = w.settings.evacuationDay > 0 ? (w.outcome == "evacuated" ? "You were evacuated. Free play continues." : w.Day > w.settings.evacuationDay || w.Day == w.settings.evacuationDay && w.HourOfDay > 14 ? "The convoy has left. Survive as long as you can." : "Reach the Ash River highway bridge (west bank) on Day " + w.settings.evacuationDay + " between 8 AM and 2 PM.") : "Endless survival: there is no rescue. Survive as long as you can.";
            UIKit.Text(journalList, "<b>Goal</b>\n" + goal + "\n\n<b>County</b>\n" + w.gen.settlements.Count + " settlements · " + w.map.buildings.Count + " buildings · " + w.map.width + " × " + w.map.height + " m · Population " + w.settings.population, 24, UIKit.Ink).gameObject.AddComponent<LayoutElement>().preferredHeight = 200;
            UIKit.Text(journalList, "<b>Emergency broadcasts</b>", 24, UIKit.Accent).gameObject.AddComponent<LayoutElement>().preferredHeight = 36;
            foreach (var r in game.Radio.AsEnumerable().Reverse()) { var t = UIKit.Text(journalList, r, 21, new Color(.62f, .83f, 1)); t.gameObject.AddComponent<LayoutElement>().preferredHeight = 64; }
            if (game.Radio.Count == 0) UIKit.Text(journalList, "Static. Carry the hand radio to hear broadcasts.", 21, UIKit.Dim).gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            UIKit.Text(journalList, "<b>Recent events</b>", 24, UIKit.Accent).gameObject.AddComponent<LayoutElement>().preferredHeight = 36;
            foreach (var h in history.AsEnumerable().Reverse().Take(40)) UIKit.Text(journalList, h, 19, UIKit.Ink).gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
        }
        void RefreshContext()
        {
            UIKit.Clear(contextList);
            foreach (var it in game.Nearby.Take(6))
            {
                UIKit.Text(contextList, it.label, 22, UIKit.Accent).gameObject.AddComponent<LayoutElement>().preferredHeight = 32;
                foreach (var a in it.actions)
                {
                    var action = a;
                    var b = UIKit.Button(contextList, a.label + (a.enabled ? "" : "  <color=#888><size=16>(" + a.reason + ")</size></color>"), () => { CloseTopPanel(); action.run(); }, 700, 48, 21);
                    b.interactable = a.enabled; b.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
                }
            }
        }

        // ---------------------------------------------------------------- map
        Texture2D mapTexture, fogTexture; Task<Color32[]> mapTask; int mapScale = 1; float mapZoom = 1; Vector2 mapPan;
        void RefreshMap()
        {
            var w = game.World; var m = w.map;
            int size = Mathf.Min(1024, m.width); int step = Mathf.Max(1, m.width / size);
            if (mapTexture == null || mapTexture.width != m.width / step)
            {
                mapTexture = new Texture2D(m.width / step, m.height / step, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                int tw = mapTexture.width, th = mapTexture.height; mapScale = step;
                mapTask = Task.Run(() =>
                {
                    var px = new Color32[tw * th];
                    for (int y = 0; y < th; y++)
                        for (int x = 0; x < tw; x++)
                        {
                            int tx = x * step, ty = y * step; var g = m.ground[m.Index(tx, ty)];
                            Color32 c = g == Ground.Water ? new Color32(46, 78, 96, 255) : g == Ground.Road || g == Ground.Bridge ? new Color32(70, 70, 72, 255) : g == Ground.Gravel ? new Color32(120, 110, 90, 255) : g == Ground.Sidewalk || g == Ground.Parking ? new Color32(120, 120, 116, 255) : g == Ground.Field ? new Color32(150, 128, 80, 255) : g == Ground.Sand ? new Color32(180, 166, 120, 255) : new Color32(84, 108, 64, 255);
                            if (m.roomAt[m.Index(tx, ty)] >= 0) c = new Color32(196, 180, 150, 255);
                            if (m.blockers.ContainsKey(m.Index(tx, ty)) && g == Ground.Grass) c = new Color32(46, 72, 44, 255);
                            px[x + y * tw] = c;
                        }
                    return px;
                });
            }
            fogTexture = fogTexture ?? new Texture2D(m.CellsX, m.CellsY, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var fog = new Color32[m.CellsX * m.CellsY];
            for (int i = 0; i < fog.Length; i++) fog[i] = m.explored[i] ? new Color32(0, 0, 0, 0) : new Color32(6, 6, 8, 215);
            fogTexture.SetPixels32(fog); fogTexture.Apply(); fogImage.texture = fogTexture;
            mapImage.texture = mapTexture;
            var s = game.Player; mapPan = new Vector2(s.pos.x / m.width, s.pos.y / m.height);
            mapZoom = Mathf.Max(mapZoom, 1);
            UIKit.Clear(mapMarkers);
            foreach (var t in w.gen.settlements) Marker(t.name, new Vector2(t.x, t.y), UIKit.Ink, 22, null);
            Marker("EVACUATION (Day " + w.settings.evacuationDay + ")", new Vector2(w.gen.evacuationPoint.x, w.gen.evacuationPoint.y), UIKit.Good, 20, "ICON_Map_Quest");
            foreach (var b in w.map.buildings.Where(b => b.visited && b.kind != BuildingKind.House && b.kind != BuildingKind.Shed).Take(80)) Marker(b.name, b.rect.Center.ToV2(), UIKit.Dim, 14, b.kind == BuildingKind.Pharmacy ? "ICON_Map_Healing" : b.kind == BuildingKind.GasStation ? "ICON_Map_Petrol" : b.kind == BuildingKind.Police ? "ICON_Map_GunShop" : null);
            youMarker = Marker("YOU", new Vector2(s.pos.x, s.pos.y), UIKit.Accent, 22, "ICON_Map_HUD_Player_Grunge");
            LayoutMap();
        }
        RectTransform youMarker;
        RectTransform Marker(string text, Vector2 tile, Color color, float size, string icon)
        {
            var m = game.World.map;
            var r = UIKit.Rect(mapMarkers, text, new Vector2(tile.x / m.width, tile.y / m.height), new Vector2(tile.x / m.width, tile.y / m.height), new Vector2(-120, -16), new Vector2(120, 16));
            var spr = icon == null ? null : UIKit.Icon(icon);
            if (spr != null) { var ic = UIKit.Box2(r, "Icon", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 22), new Vector2(28, 28)); var img = ic.gameObject.AddComponent<Image>(); img.sprite = spr; img.color = color; }
            else if (text == "YOU") { }
            var t = UIKit.Label(r, text, size, color, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center); t.outlineWidth = .2f; t.outlineColor = Color.black;
            return r;
        }
        void LayoutMap()
        {
            var view = mapImageRoot.rect; float side = Mathf.Min(view.width, view.height) * mapZoom;
            var r = mapImage.rectTransform; r.sizeDelta = new Vector2(side, side);
            r.anchoredPosition = new Vector2((.5f - mapPan.x) * side, (.5f - mapPan.y) * side);
        }
        void PanMap()
        {
            if (mapTask != null && mapTask.IsCompleted) { mapTexture.SetPixels32(mapTask.Result); mapTexture.Apply(); mapTask = null; }
        }
        void Update()
        {
            if (open.Count == 0 || open.Peek() != "map" || game.World == null) return;
            float zoomIn = 0; Vector2 pan = Vector2.zero;
            if (Mouse.current != null) { zoomIn += Mathf.Sign(Mouse.current.scroll.ReadValue().y) * (Mathf.Abs(Mouse.current.scroll.ReadValue().y) > .01f ? 1 : 0) * .15f; if (Mouse.current.leftButton.isPressed) pan -= Mouse.current.delta.ReadValue() / Mathf.Max(200, mapImage.rectTransform.sizeDelta.x); }
            if (Gamepad.current != null) { zoomIn += (Gamepad.current.rightTrigger.ReadValue() - Gamepad.current.leftTrigger.ReadValue()) * Time.unscaledDeltaTime * 2; pan += Gamepad.current.leftStick.ReadValue() * Time.unscaledDeltaTime * .4f / mapZoom; }
            if (Keyboard.current != null) { var k = Keyboard.current; pan += new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0)) * Time.unscaledDeltaTime * .4f / mapZoom; }
            mapZoom = Mathf.Clamp(mapZoom * (1 + zoomIn), 1, 16); mapPan += pan; mapPan = new Vector2(Mathf.Clamp01(mapPan.x), Mathf.Clamp01(mapPan.y));
            foreach (Transform t in mapMarkers) t.localScale = Vector3.one / Mathf.Sqrt(mapZoom);
            LayoutMap();
        }
    }

    static class VecExt { public static Vector2 ToV2(this Vec2 v) { return new Vector2(v.x, v.y); } }
}
