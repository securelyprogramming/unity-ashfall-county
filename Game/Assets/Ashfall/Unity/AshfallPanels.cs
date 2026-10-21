using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ashfall.View
{
    /// <summary>In-game panels: framework (open/close/back), inventory with loot, the actions menu and the county map.
    /// Community, base and workshop live in AshfallPanelsCommunity; missions, radio and talk/trade in AshfallPanelsMissions.</summary>
    public sealed partial class AshfallUI
    {
        RectTransform invPlayer, invLoot, invTabs, invActions, contextList, mapImageRoot, mapLegend;
        TextMeshProUGUI invTitle, lootTitle, invDetail; RawImage mapImage, fogImage; RectTransform mapMarkers;
        int selectedItem = -1; bool selectedFromLoot; int lootIndex;

        RectTransform PanelFrame(string id, string heading, Vector2 min, Vector2 max)
        {
            var r = UIKit.Rect(root, id, min, max, Vector2.zero, Vector2.zero); UIKit.Background(r, UIKit.Panel, true);
            UIKit.Heading(r, heading, 32, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -64), new Vector2(-100, -16));
            UIKit.Button(UIKit.Box2(r, "Close", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -14), new Vector2(56, 46)), "X", () => CloseTopPanel(), 56, 46, 22);
            UIKit.Label(r, "Esc / B to close", 15, UIKit.Dim, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-300, -54), new Vector2(-82, -22), TextAlignmentOptions.MidlineRight);
            r.gameObject.SetActive(false); panels[id] = r; return r;
        }

        void BuildPanels()
        {
            // Inventory and loot side by side.
            var inv = PanelFrame("inventory", "INVENTORY", new Vector2(.06f, .08f), new Vector2(.94f, .92f));
            invTitle = UIKit.Label(inv, "", 21, UIKit.Dim, new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(28, -104), new Vector2(-10, -72));
            var leftArea = UIKit.Rect(inv, "Player", new Vector2(0, 0), new Vector2(.5f, 1), new Vector2(28, 236), new Vector2(-10, -108)); UIKit.Scroll(leftArea, "Items", out invPlayer);
            invTabs = UIKit.Strip(inv, "Tabs", new Vector2(.5f, 1), new Vector2(1, 1), new Vector2(10, -108), new Vector2(-28, -64), TextAnchor.MiddleLeft, 6);
            lootTitle = UIKit.Label(inv, "", 21, UIKit.Dim, new Vector2(.5f, 1), new Vector2(1, 1), new Vector2(10, -140), new Vector2(-28, -110));
            var rightArea = UIKit.Rect(inv, "Loot", new Vector2(.5f, 0), new Vector2(1, 1), new Vector2(10, 236), new Vector2(-28, -144)); UIKit.Scroll(rightArea, "Loot items", out invLoot);
            var detail = UIKit.Rect(inv, "Detail", new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 144), new Vector2(-28, 226)); UIKit.Card(detail, new Color(0, 0, 0, .3f));
            invDetail = UIKit.Label(detail, "", 21, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(14, 6), new Vector2(-14, -6), TextAlignmentOptions.MidlineLeft);
            invActions = UIKit.Rect(inv, "Actions", new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 22), new Vector2(-28, 134));
            var g = invActions.gameObject.AddComponent<GridLayoutGroup>(); g.cellSize = new Vector2(260, 50); g.spacing = new Vector2(8, 8);

            var context = PanelFrame("context", "ACTIONS", new Vector2(.3f, .16f), new Vector2(.7f, .84f));
            var cl = UIKit.Rect(context, "List", Vector2.zero, Vector2.one, new Vector2(24, 24), new Vector2(-24, -80)); UIKit.Scroll(cl, "Actions", out contextList);

            var map = PanelFrame("map", "ASHFALL COUNTY", new Vector2(.05f, .04f), new Vector2(.95f, .96f));
            mapImageRoot = UIKit.Rect(map, "Viewport", Vector2.zero, Vector2.one, new Vector2(24, 24), new Vector2(-300, -80)); mapImageRoot.gameObject.AddComponent<RectMask2D>(); UIKit.Card(mapImageRoot, new Color(.03f, .033f, .036f, 1), true);
            var mi = UIKit.Box2(mapImageRoot, "Map", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1000, 1000)); mapImage = mi.gameObject.AddComponent<RawImage>(); mapImage.raycastTarget = false;
            var fi = UIKit.Fill(mi, "Fog"); fogImage = fi.gameObject.AddComponent<RawImage>(); fogImage.raycastTarget = false;
            mapMarkers = UIKit.Fill(mi, "Markers");
            mapLegend = UIKit.Rect(map, "Legend", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-284, 24), new Vector2(-24, -80)); UIKit.Card(mapLegend, new Color(0, 0, 0, .3f)); UIKit.Column(mapLegend, 6, TextAnchor.UpperLeft, 14);
            UIKit.Label(map, "Wheel / triggers zoom · drag or stick to pan · unexplored areas are dark", 17, UIKit.Dim, new Vector2(0, 0), new Vector2(1, 0), new Vector2(30, 28), new Vector2(-310, 54), TextAlignmentOptions.BottomRight);
            BuildLegend();

            BuildCommunityPanel();   // AshfallPanelsCommunity.cs
            BuildMissionPanels();    // AshfallPanelsMissions.cs
        }

        public bool CloseTopPanel()
        {
            if (overlay != null) { CloseOverlay(); return true; }
            if (open.Count == 0) return false;
            var id = open.Pop(); panels[id].gameObject.SetActive(false);
            if (id == "inventory" && game.Player != null) game.Submit(Command.Of(CommandKind.CloseContainer));
            if (id == "talk") talkNpc = -1;
            game.Audio.Ui("close");
            if (open.Count > 0) Select(panels[open.Peek()]); else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            return true;
        }
        public void Toggle(string id)
        {
            if (!panels.ContainsKey(id)) return;
            if (open.Count > 0 && open.Peek() == id) { CloseTopPanel(); return; }
            while (open.Count > 0) CloseTopPanel();
            Open(id);
        }
        void Open(string id)
        {
            var r = panels[id]; r.gameObject.SetActive(true); r.SetAsLastSibling(); open.Push(id); game.Audio.Ui("open"); UIKit.FadeIn(r, .12f);
            switch (id)
            {
                case "inventory": selectedItem = -1; lootIndex = 0; RefreshInventory(); break;
                case "map": RefreshMap(); break;
                case "context": RefreshContext(); break;
                case "community": RefreshCommunity(); break;
                case "missions": RefreshMissions(); break;
                case "talk": RefreshTalk(); break;
            }
            Select(r);
            if (toastCard != null) toastCard.SetAsLastSibling();
        }
        public void OpenInventory() { if (open.Count > 0 && open.Peek() == "inventory") { RefreshInventory(); return; } while (open.Count > 0) CloseTopPanel(); Open("inventory"); }
        public void OpenContext() { if (game.Nearby.Count == 0) return; while (open.Count > 0) CloseTopPanel(); Open("context"); }
        /// <summary>Opens a panel directly (interactions: talk to an NPC, the workbench, the radio set).</summary>
        public void OpenPanel(string id) { if (!panels.ContainsKey(id)) return; while (open.Count > 0) CloseTopPanel(); Open(id); }

        float panelRefresh;
        void LateUpdate()
        {
            if (open.Count == 0 || game.World == null) return;
            panelRefresh -= Time.unscaledDeltaTime; if (panelRefresh > 0) return; panelRefresh = .35f;
            var top = open.Peek();
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            // Refresh content without stealing controller focus.
            if (top == "inventory") RefreshInventory(true);
            else if (top == "community") RefreshCommunity(true);
            else if (top == "missions") RefreshMissions(true);
            else if (top == "talk") RefreshTalk(true);
            else if (top == "map") { PanMap(); mapMarkerClock -= .35f; if (mapMarkerClock <= 0) { mapMarkerClock = 1; RefreshMapMarkers(); } }
            if (selected != null && selected.activeInHierarchy && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(selected);
            else if (selected == null || !selected.activeInHierarchy) Select(panels[top]);
        }

        // ---------------------------------------------------------------- inventory
        string lastInvSignature = "";
        void RefreshInventory(bool soft = false)
        {
            var s = game.Player; if (s == null) return;
            var sources = game.LootSources();
            if (lootIndex >= sources.Count) lootIndex = 0;
            string sig = string.Join(",", s.inventory.items.Select(i => i.uid + ":" + i.count + ":" + i.loaded + ":" + (s.IsEquipped(i.uid) ? 1 : 0))) + "|" + string.Join(";", sources.Select(x => x.label + x.inventory.items.Count + string.Join(",", x.inventory.items.Select(i => i.uid + ":" + i.count)))) + "|" + selectedItem + selectedFromLoot + lootIndex + game.Sim.AtBase(s);
            if (soft && sig == lastInvSignature) return; lastInvSignature = sig;
            invTitle.text = s.name + " · carrying " + s.CarriedWeight.ToString("0.0") + " / " + s.Capacity.ToString("0") + (s.Encumbrance > 1 ? " <color=#ff9a80>(over capacity: slower, tires faster)</color>" : "");
            UIKit.Clear(invPlayer);
            foreach (var item in s.inventory.items.OrderBy(i => UiData.IsRucksack(i.Def) ? 0 : s.IsEquipped(i.uid) ? 1 : 2).ThenByDescending(i => i.Def.tier).ThenBy(i => i.Def.category).ThenBy(i => i.Def.name)) ItemRow(invPlayer, item, false, s);
            if (s.inventory.items.Count == 0) UIKit.Line(invPlayer, "Empty pockets.", 22, UIKit.Dim, 40);
            UIKit.Clear(invTabs);
            for (int i = 0; i < sources.Count; i++) { int idx = i; var b = UIKit.Button(invTabs, sources[i].label + " (" + sources[i].inventory.items.Count + ")", () => { lootIndex = idx; selectedItem = -1; RefreshInventory(); }, 210, 40, 18); UIKit.SetActive(b, i == lootIndex); }
            UIKit.Clear(invLoot);
            if (sources.Count > 0)
            {
                var src = sources[lootIndex];
                lootTitle.text = src.label + " · " + src.inventory.items.Count + " items" + (src.where.kind == InvKind.Container ? " · " + src.inventory.Weight.ToString("0.0") + " / " + src.inventory.capacity.ToString("0") : "");
                foreach (var item in src.inventory.items.OrderByDescending(i => i.Def.tier).ThenBy(i => i.Def.category).ThenBy(i => i.Def.name)) ItemRow(invLoot, item, true, s);
                if (src.inventory.items.Count == 0) UIKit.Line(invLoot, "Nothing here.", 22, UIKit.Dim, 40);
            }
            // Selected item details and actions.
            UIKit.Clear(invActions);
            ItemStack sel = null; InvRef from = InvRef.Player; InvRef to = sources.Count > 0 ? sources[lootIndex].where : InvRef.Player;
            if (selectedItem >= 0)
            {
                if (selectedFromLoot && sources.Count > 0) { sel = sources[lootIndex].inventory.ByUid(selectedItem); from = sources[lootIndex].where; }
                else sel = s.inventory.ByUid(selectedItem);
            }
            bool atBase = game.Sim.AtBase(s);
            if (sel == null)
            {
                invDetail.text = "Select an item. Selecting it again moves it between your inventory and the open container." + (s.inventory.items.Any(i => UiData.IsRucksack(i.Def)) ? "\n<color=#f0b050>Rucksacks feed the whole community: deposit them at base.</color>" : "");
                if (sources.Count > 0 && sources[lootIndex].inventory.items.Count > 0) { var src = sources[lootIndex]; UIKit.Button(invActions, "Take everything", () => { foreach (var it in src.inventory.items.ToList()) game.Submit(new Command { kind = CommandKind.Transfer, item = it.uid, count = 0, from = src.where, to = InvRef.Player }); }, 260, 50, 20); }
                if (atBase && s.inventory.items.Any(i => UiData.IsRucksack(i.Def))) UIKit.Button(invActions, "Deposit all rucksacks", () => game.Submit(new Command { kind = CommandKind.DepositRucksack, item = -1, recipe = "" }), 260, 50, 20);
                return;
            }
            var d = sel.Def;
            invDetail.text = "<b><color=" + UIKit.TierHex(d.tier) + ">" + sel.Label + "</color></b>  <color=#aaa>" + UIKit.TierName(d.tier) + " · " + Category(d) + " · " + sel.Weight.ToString("0.00") + " kg" + Describe(sel) + "</color>";
            int uid = sel.uid;
            if (selectedFromLoot)
            {
                var src = from;
                UIKit.Button(invActions, "Take", () => game.Submit(new Command { kind = CommandKind.Transfer, item = uid, count = 0, from = src, to = InvRef.Player }), 260, 50, 20);
                if (sel.count > 1) UIKit.Button(invActions, "Take one", () => game.Submit(new Command { kind = CommandKind.Transfer, item = uid, count = 1, from = src, to = InvRef.Player }), 260, 50, 20);
                return;
            }
            if (UiData.IsRucksack(d)) { var b = UIKit.Button(invActions, "Deposit at base", () => game.Submit(new Command { kind = CommandKind.DepositRucksack, item = uid, recipe = "" }), 260, 50, 20); b.interactable = atBase; if (!atBase) UIKit.SetLabel(b, "Deposit (at base only)"); }
            if (d.Has("throwable") || d.blastRadius > 0) UIKit.Button(invActions, "Throw (12 m ahead)", () => { CloseTopPanel(); var p = game.Player; if (p != null) game.ThrowAt(p, AshfallGame.Pos(p.pos) + new Vector3(Mathf.Cos(p.facing), 0, Mathf.Sin(p.facing)) * 12); }, 260, 50, 20);
            if (d.Edible || d.category == ItemCategory.Medical || d.category == ItemCategory.Light || d.plagueCure > 0 || d.stamina > 0 || d.id == "battery")
                UIKit.Button(invActions, d.category == ItemCategory.Food ? "Eat" : d.category == ItemCategory.Drink ? "Drink" : d.category == ItemCategory.Light ? (s.lightOn && s.secondary == uid ? "Turn off" : "Turn on") : "Use", () => game.Submit(new Command { kind = CommandKind.Use, item = uid }), 260, 50, 20);
            if (d.IsWeapon && d.category != ItemCategory.Material && d.blastRadius <= 0 || d.category == ItemCategory.Material && d.damage > 0) UIKit.Button(invActions, s.primary == uid ? "Unequip" : "Equip (main hand)", () => game.Submit(new Command { kind = s.primary == uid ? CommandKind.Unequip : CommandKind.Equip, item = uid, slot = 0 }), 260, 50, 20);
            if (d.category == ItemCategory.Light || d.category == ItemCategory.Weapon && !d.twoHanded) UIKit.Button(invActions, s.secondary == uid ? "Unequip off hand" : "Equip off hand", () => game.Submit(new Command { kind = s.secondary == uid ? CommandKind.Unequip : CommandKind.Equip, item = uid, slot = 1 }), 260, 50, 20);
            if (d.slot != WearSlot.None) { bool worn = s.worn.ContainsValue(uid); UIKit.Button(invActions, worn ? "Take off" : "Wear", () => game.Submit(worn ? new Command { kind = CommandKind.Unwear, slot = (int)d.slot } : new Command { kind = CommandKind.Wear, item = uid }), 260, 50, 20); }
            if (sources.Count > 0) { var dest = to; UIKit.Button(invActions, "Put in " + sources[lootIndex].label.ToLowerInvariant(), () => game.Submit(new Command { kind = CommandKind.Transfer, item = uid, count = 0, from = InvRef.Player, to = dest }), 260, 50, 20); }
            UIKit.Button(invActions, "Drop", () => game.Submit(new Command { kind = CommandKind.DropAll, item = uid }), 260, 50, 20);
        }
        static string Category(ItemDef d) { return d.resource >= 0 ? "Rucksack" : d.blastRadius > 0 ? "Throwable" : d.armor > 0 ? "Armour" : d.category.ToString(); }
        static string Describe(ItemStack s)
        {
            var d = s.Def; var parts = new List<string>();
            if (d.resource >= 0) parts.Add("+" + d.resourceAmount.ToString("0") + " " + ((Resource)d.resource) + " for the community stock");
            if (d.armor > 0) parts.Add("armour " + Mathf.RoundToInt(d.armor * s.condition * 100) + "% (" + (d.slot == WearSlot.Head ? "head" : "torso") + ")");
            if (d.damage > 0 && d.blastRadius <= 0) parts.Add("damage " + d.damage.ToString("0.00") + (d.twoHanded ? ", two-handed" : "") + (d.category == ItemCategory.Firearm ? ", loud" : ""));
            if (d.blastRadius > 0) parts.Add("blast " + d.blastRadius.ToString("0.#") + " m" + (d.fuse > 0 ? ", " + d.fuse.ToString("0.#") + " s fuse" : ""));
            if (d.plagueCure > 0) parts.Add("cures blood plague");
            if (d.stamina > 0) parts.Add("+" + d.stamina.ToString("0") + " stamina");
            if (d.heal > 0) parts.Add("+" + d.heal.ToString("0") + " health");
            if (d.bandageQuality > 0) parts.Add("stops bleeding");
            if (d.capacity > 0) parts.Add("+" + d.capacity + " carry capacity");
            if (d.scratchProtection > 0 && d.armor <= 0) parts.Add("bite protection " + (int)(d.biteProtection * 100) + "%");
            if (d.perishDays > 0) parts.Add(s.Rotten ? "rotten" : s.Stale ? "stale" : "fresh");
            return parts.Count == 0 ? "" : " · " + string.Join(", ", parts);
        }
        void ItemRow(RectTransform list, ItemStack item, bool loot, Survivor s)
        {
            int uid = item.uid; var d = item.Def; bool ruck = UiData.IsRucksack(d);
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
            }, 700, 48, 20);
            var t = b.GetComponentInChildren<TextMeshProUGUI>(); t.alignment = TextAlignmentOptions.MidlineLeft; t.rectTransform.offsetMin = new Vector2(58, 0);
            string tag = s.primary == uid ? " <color=#f0b050>[main hand]</color>" : s.secondary == uid ? " <color=#f0b050>[off hand]</color>" : s.worn.ContainsValue(uid) ? " <color=#9fd3ff>[worn]</color>" : "";
            string extra = ruck ? "  <color=#f0b050><size=16>RUCKSACK · " + ((Resource)d.resource) + "</size></color>" : d.armor > 0 ? "  <color=#9fd3ff><size=16>armour " + Mathf.RoundToInt(d.armor * item.condition * 100) + "%</size></color>" : "";
            t.text = "<color=" + UIKit.TierHex(d.tier) + ">" + item.Label + "</color>" + (loot ? "" : tag) + extra + "  <color=#888><size=16>" + item.Weight.ToString("0.0") + " kg</size></color>";
            // Tier stripe on the left edge; rucksacks get a warm row tint.
            var stripe = UIKit.Rect(b.transform, "Tier", new Vector2(0, 0), new Vector2(0, 1), new Vector2(3, 6), new Vector2(7, -6)); var si = stripe.gameObject.AddComponent<Image>(); si.sprite = UIKit.White; si.color = UIKit.TierColor(d.tier); si.raycastTarget = false;
            var spr = UIKit.Icon(UIKit.ItemIcon(d));
            if (spr != null) UIKit.Img(b.transform, "Icon", spr, Color.white, new Vector2(0, .5f), new Vector2(32, 0), new Vector2(38, 38));
            if (selected) UIKit.SetActive(b, true); else if (ruck) UIKit.SetWarm(b);
        }

        // ---------------------------------------------------------------- actions menu
        void RefreshContext()
        {
            UIKit.Clear(contextList);
            foreach (var it in game.Nearby.Take(7))
            {
                UIKit.Line(contextList, it.label, 22, UIKit.Accent, 32);
                foreach (var a in it.actions)
                {
                    var action = a;
                    var b = UIKit.Button(contextList, a.label + (a.enabled ? "" : "  <color=#888><size=16>(" + a.reason + ")</size></color>"), () => { CloseTopPanel(); action.run(); }, 700, 48, 21);
                    b.interactable = a.enabled; b.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
                }
            }
        }

        // ---------------------------------------------------------------- map
        float mapZoom = 1, mapMarkerClock; Vector2 mapPan;
        void RefreshMap()
        {
            var w = game.World; var m = w.map;
            if (countyMap == null) countyMap = new UiCountyMap(m);
            countyMap.Poll(); countyMap.RefreshFog(m);
            mapImage.texture = countyMap.texture; fogImage.texture = countyMap.fog;
            var s = game.Player; mapPan = new Vector2(s.pos.x / m.width, s.pos.y / m.height);
            mapZoom = Mathf.Max(mapZoom, 1); mapMarkerClock = 1;
            RefreshMapMarkers();
            LayoutMap();
        }
        void BuildLegend()
        {
            void L(string text, Sprite icon, Color c)
            {
                var row = UIKit.Row(mapLegend, 30, 10);
                var ic = UIKit.Rect(row.transform, "Icon", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); var le = ic.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 24; le.preferredHeight = 24;
                var img = ic.gameObject.AddComponent<Image>(); img.sprite = icon; img.color = c; img.preserveAspect = true; img.raycastTarget = false;
                UIKit.RowLabel(row.transform, text, 200, 19);
            }
            UIKit.Line(mapLegend, "<b>Legend</b>", 22, UIKit.Accent, 32);
            L("You", UIKit.Arrow, UIKit.Accent);
            L("Home base", UIKit.IconOr("ICON_Map_Home", UIKit.Rounded), UIKit.Blue);
            L("Outpost", UIKit.IconOr("ICON_Map_Home", UIKit.Rounded), new Color(.6f, .8f, 1f));
            L("Companion", UIKit.Circle, UIKit.Blue);
            L("Mission target", UIKit.IconOr("ICON_Map_Quest", UIKit.Circle), UIKit.Accent);
            L("Supply drop", UIKit.IconOr("ICON_Map_Ammo", UIKit.Circle), UIKit.Bad);
            L("Red zone", UIKit.Ring, UIKit.Bad);
            L("Plague heart", UIKit.IconOr("ICON_Map_Biohazard", UIKit.Circle), UIKit.Plague);
            L("Enclave", UIKit.Circle, UIKit.Good);
            L("Landmark", UIKit.IconOr("ICON_Map_Unknown", UIKit.Circle), new Color(.86f, .84f, .78f));
            UIKit.Divider(mapLegend);
            mapStats = UIKit.Line(mapLegend, "", 18, UIKit.Dim, 140);
        }
        TextMeshProUGUI mapStats;

        void RefreshMapMarkers()
        {
            var w = game.World; var s = game.Player; if (w == null || s == null) return;
            UIKit.Clear(mapMarkers);
            foreach (var lm in w.gen.landmarks) Marker(lm.name, lm.Center, lm.kind == LandmarkKind.Town ? UIKit.Ink : LandmarkColor(lm.kind), lm.kind == LandmarkKind.Town ? 22 : 17, lm.kind == LandmarkKind.Town ? null : LandmarkIcon(lm.kind), null);
            foreach (var b in w.map.buildings) if (b.visited && b.kind != BuildingKind.House && b.kind != BuildingKind.Shed && b.kind != BuildingKind.Barn && mapZoom > 3) Marker(b.name, b.rect.Center, UIKit.Dim, 13, null, null);
            foreach (var f in w.factions) if (f.id != 0 && f.homeBuilding >= 0) Marker(f.name, UiData.BuildingCenter(w.map, f.homeBuilding), f.Hostile ? UIKit.Bad : f.role == AiRole.Enclave ? UIKit.Good : UIKit.Warn, 17, null, UIKit.Circle);
            var c = w.community;
            if (c.baseBuilding >= 0) Marker("HOME", c.basePos, UIKit.Blue, 20, null, UIKit.IconOr("ICON_Map_Home", UIKit.Rounded));
            foreach (var o in c.outposts) Marker("Outpost · " + o.bonus, o.pos, new Color(.6f, .8f, 1f), 15, null, UIKit.IconOr("ICON_Map_Home", UIKit.Rounded));
            foreach (var m in c.missions) if (m.status == MissionStatus.Active && Tracked(m.id) && (m.target.x != 0 || m.target.y != 0)) Marker(m.title, m.target, UIKit.Accent, 17, null, UIKit.IconOr("ICON_Map_Quest", UIKit.Circle));
            foreach (var d in w.airdrops) if (!d.opened) Marker(d.landed ? "SUPPLY DROP" : "Drop incoming", d.pos, d.landed ? UIKit.Bad : new Color(1, .5f, .45f), 17, null, UIKit.IconOr("ICON_Map_Ammo", UIKit.Circle));
            foreach (var h in w.hearts) if (h.discovered && !h.destroyed) Marker("Plague heart", h.pos, UIKit.Plague, 16, null, UIKit.IconOr("ICON_Map_Biohazard", UIKit.Circle));
            var z = w.redZone;
            if (z != null && w.settings.redZones && (z.Warning(w.minutes) || z.Active(w.minutes)))
            {
                var uv = new Vector2(z.center.x / w.map.width, z.center.y / w.map.height); var sz = new Vector2(z.radius * 2 / w.map.width, z.radius * 2 / w.map.height);
                var zr = UIKit.Rect(mapMarkers, "Red zone", uv - sz * .5f, uv + sz * .5f, Vector2.zero, Vector2.zero); var zi = zr.gameObject.AddComponent<Image>(); zi.sprite = UIKit.Circle; zi.color = new Color(1, .1f, .05f, .25f); zi.raycastTarget = false;
                var ring = UIKit.Fill(zr, "Ring"); var ri = ring.gameObject.AddComponent<Image>(); ri.sprite = UIKit.Ring; ri.color = UIKit.Bad; ri.raycastTarget = false;
                Marker(z.Active(w.minutes) ? "SHELLING" : "RED ZONE", z.center, UIKit.Bad, 18, null, null);
            }
            int mid = c.companion; if (mid >= 0 && mid < w.survivors.Count && w.survivors[mid].alive && mid != s.id) Marker(w.survivors[mid].name, w.survivors[mid].pos, UIKit.Blue, 15, null, UIKit.Circle);
            var you = Marker("YOU", s.pos, UIKit.Accent, 20, null, UIKit.Arrow); var arrow = you.Find("Icon"); if (arrow != null) arrow.localEulerAngles = new Vector3(0, 0, s.facing * Mathf.Rad2Deg - 90);
            foreach (Transform t in mapMarkers) if (t.name != "Red zone") t.localScale = Vector3.one / Mathf.Sqrt(mapZoom);
            mapStats.text = w.map.width + " × " + w.map.height + " m county\nSurvivors alive " + w.SurvivorsAlive + "\nPlague hearts " + w.HeartsRemaining + "\nOutposts " + c.outposts.Count;
        }
        RectTransform Marker(string text, Vec2 at, Color color, float size, string icon, Sprite sprite)
        {
            var m = game.World.map; var uv = new Vector2(at.x / m.width, at.y / m.height);
            var r = UIKit.Rect(mapMarkers, text, uv, uv, new Vector2(-130, -16), new Vector2(130, 16));
            var spr = sprite ?? UIKit.Icon(icon);
            if (spr != null) UIKit.Img(r, "Icon", spr, color, new Vector2(.5f, .5f), new Vector2(0, 22), new Vector2(26, 26));
            var t = UIKit.Label(r, text, size, color, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center); t.outlineWidth = .22f; t.outlineColor = new Color32(0, 0, 0, 255); t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
            return r;
        }
        void LayoutMap()
        {
            var view = mapImageRoot.rect; float side = Mathf.Min(view.width, view.height) * mapZoom;
            var r = mapImage.rectTransform; r.sizeDelta = new Vector2(side, side);
            r.anchoredPosition = new Vector2((.5f - mapPan.x) * side, (.5f - mapPan.y) * side);
        }
        void PanMap() { if (countyMap != null && countyMap.Poll() && mapImage.texture != countyMap.texture) mapImage.texture = countyMap.texture; }
        void Update()
        {
            if (open.Count == 0 || open.Peek() != "map" || game.World == null) return;
            float zoomIn = 0; Vector2 pan = Vector2.zero;
            if (Mouse.current != null) { float wheel = Mouse.current.scroll.ReadValue().y; if (Mathf.Abs(wheel) > .01f) zoomIn += Mathf.Sign(wheel) * .15f; if (Mouse.current.leftButton.isPressed) pan -= Mouse.current.delta.ReadValue() / Mathf.Max(200, mapImage.rectTransform.sizeDelta.x); }
            if (Gamepad.current != null) { zoomIn += (Gamepad.current.rightTrigger.ReadValue() - Gamepad.current.leftTrigger.ReadValue()) * Time.unscaledDeltaTime * 2; pan += Gamepad.current.leftStick.ReadValue() * Time.unscaledDeltaTime * .4f / mapZoom; }
            if (Keyboard.current != null) { var k = Keyboard.current; pan += new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0)) * Time.unscaledDeltaTime * .4f / mapZoom; }
            float before = mapZoom;
            mapZoom = Mathf.Clamp(mapZoom * (1 + zoomIn), 1, 16); mapPan += pan; mapPan = new Vector2(Mathf.Clamp01(mapPan.x), Mathf.Clamp01(mapPan.y));
            if (mapZoom != before) foreach (Transform t in mapMarkers) if (t.name != "Red zone") t.localScale = Vector3.one / Mathf.Sqrt(mapZoom);
            LayoutMap();
        }
    }

    static class VecExt { public static Vector2 ToV2(this Vec2 v) { return new Vector2(v.x, v.y); } }
}
