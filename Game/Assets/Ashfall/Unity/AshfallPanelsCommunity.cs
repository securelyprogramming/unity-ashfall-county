using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ashfall.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashfall.View
{
    /// <summary>Community panel (K): the roster with switching and companions, the Base tab (facilities, stock, outposts)
    /// and the Workshop tab. Every button is a host command; availability comes from the Community contract queries.</summary>
    public sealed partial class AshfallUI
    {
        RectTransform commTabs, commRoster, commDetail, commBase, commWorkshop, commMembersPage, commBasePage, commWorkshopPage;
        TextMeshProUGUI commSummary;
        string commTab = "members"; int commSelected = -1; string lastCommSig = "";
        readonly List<Survivor> roster = new List<Survivor>(); readonly List<string> injuryLines = new List<string>();
        readonly Dictionary<string, Button> commTabButtons = new Dictionary<string, Button>();

        void BuildCommunityPanel()
        {
            var p = PanelFrame("community", "COMMUNITY", new Vector2(.06f, .06f), new Vector2(.94f, .94f));
            commSummary = UIKit.Label(p, "", 20, UIKit.Dim, new Vector2(0, 1), new Vector2(1, 1), new Vector2(330, -60), new Vector2(-320, -22), TextAlignmentOptions.MidlineLeft);
            commTabs = UIKit.Strip(p, "Tabs", new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -124), new Vector2(-28, -76), TextAnchor.MiddleLeft, 8);
            foreach (var t in new[] { "members", "base", "workshop" })
            {
                string id = t;
                commTabButtons[id] = UIKit.Button(commTabs, id == "members" ? "Members" : id == "base" ? "Base" : "Workshop", () => { commTab = id; RefreshCommunity(); }, 200, 44, 21);
            }
            var body = UIKit.Rect(p, "Body", Vector2.zero, Vector2.one, new Vector2(28, 24), new Vector2(-28, -136));
            commMembersPage = UIKit.Fill(body, "Members");
            var rl = UIKit.Rect(commMembersPage, "Roster", new Vector2(0, 0), new Vector2(.36f, 1), Vector2.zero, new Vector2(-10, 0)); UIKit.Scroll(rl, "List", out commRoster);
            var dl = UIKit.Rect(commMembersPage, "Detail", new Vector2(.36f, 0), new Vector2(1, 1), new Vector2(10, 0), Vector2.zero); UIKit.Scroll(dl, "Card", out commDetail);
            commBasePage = UIKit.Fill(body, "Base"); UIKit.Scroll(commBasePage, "List", out commBase);
            commWorkshopPage = UIKit.Fill(body, "Workshop"); UIKit.Scroll(commWorkshopPage, "List", out commWorkshop);
        }

        /// <summary>Opens the Community panel on a tab (interactions at base: switch survivor, facilities, workshop).</summary>
        public void OpenCommunityTab(string tab) { commTab = tab; lastCommSig = ""; OpenPanel("community"); }
        void RefreshOpenCommunity() { if (open.Count > 0 && open.Peek() == "community") { lastCommSig = ""; RefreshCommunity(true); } }

        void RefreshCommunity(bool soft = false)
        {
            var w = game.World; var s = game.Player; if (w == null || s == null) return;
            UiData.Members(w, roster);
            if (commSelected < 0 || roster.All(m => m.id != commSelected)) commSelected = s.id;
            string sig = Signature(w, s);
            if (soft && sig == lastCommSig) return; lastCommSig = sig;
            var c = w.community;
            int alive = roster.Count(m => m.alive);
            commSummary.text = alive + " member" + (alive == 1 ? "" : "s") + " · Morale <b>" + (int)c.morale + "</b> · Influence <b>" + (int)c.influence + "</b> · " + (game.Sim.AtBase(s) ? "<color=#8fb8ff>at base</color>" : "away from base");
            foreach (var kv in commTabButtons) UIKit.SetActive(kv.Value, kv.Key == commTab);
            commMembersPage.gameObject.SetActive(commTab == "members"); commBasePage.gameObject.SetActive(commTab == "base"); commWorkshopPage.gameObject.SetActive(commTab == "workshop");
            if (commTab == "members") { RefreshRoster(w, s); RefreshMember(w, s); }
            else if (commTab == "base") RefreshBase(w, s);
            else RefreshWorkshop(w, s);
        }

        string Signature(World w, Survivor s)
        {
            var sb = new StringBuilder(commTab).Append(commSelected).Append('|').Append(w.playerId).Append(w.community.companion).Append(game.Sim.AtBase(s));
            foreach (var m in roster) sb.Append(m.id).Append(m.alive).Append((int)m.health).Append((int)m.injury).Append((int)m.fatigue).Append((int)m.plague).Append((int)m.order).Append(UiData.Role(w, m));
            var c = w.community;
            for (int i = 0; i < 5; i++) sb.Append((int)c.stock[i]).Append(',');
            sb.Append((int)c.influence).Append((int)c.morale);
            foreach (var f in c.facilities) sb.Append(f.kind).Append(f.level).Append((int)(UiData.Progress(f) * 100));
            foreach (var o in c.outposts) sb.Append(o.building);
            if (commTab == "workshop") sb.Append(s.inventory.items.Count);
            return sb.ToString();
        }

        void RefreshRoster(World w, Survivor s)
        {
            UIKit.Clear(commRoster);
            foreach (var m in roster.OrderBy(m => m.alive ? 0 : 1).ThenBy(m => m.id == w.playerId ? 0 : 1))
            {
                var mem = m; string role = UiData.Role(w, m);
                var b = UIKit.Button(commRoster, "", () => { commSelected = mem.id; RefreshCommunity(); }, 560, 70, 20);
                var t = b.GetComponentInChildren<TextMeshProUGUI>(); t.alignment = TextAlignmentOptions.MidlineLeft;
                t.text = "<b>" + m.name + "</b>  <color=" + UIKit.Hex(UiData.RoleColor(role)) + "><size=16>" + role.ToUpperInvariant() + "</size></color>\n<size=17><color=#aaa>" + Profession.Get(m.profession).name +
                    (m.alive ? " · health " + Mathf.CeilToInt(m.health) + (m.fatigue > 75 ? " · <color=#e0c070>exhausted</color>" : "") + (m.plagueInfected ? " · <color=#c48cff>plague</color>" : "") : "") + "</color></size>";
                if (!m.alive) t.alpha = .55f;
                UIKit.SetActive(b, m.id == commSelected);
            }
            if (roster.Count == 0) UIKit.Line(commRoster, "No members.", 22, UIKit.Dim, 40);
        }

        void RefreshMember(World w, Survivor s)
        {
            UIKit.Clear(commDetail);
            var m = roster.FirstOrDefault(x => x.id == commSelected); if (m == null) return;
            string role = UiData.Role(w, m);
            UIKit.Line(commDetail, "<b><size=34>" + m.name + "</size></b>   <color=" + UIKit.Hex(UiData.RoleColor(role)) + ">" + role + "</color>", 26, UIKit.Ink, 50);
            UIKit.Line(commDetail, Profession.Get(m.profession).name + (m.traits.Count > 0 ? " · " + string.Join(", ", m.traits.Select(UiData.TraitName)) : " · no traits") + " · " + m.kills + " kills", 20, UIKit.Dim, 30);
            if (!m.alive) { UIKit.Line(commDetail, "<color=#ff8a7a>" + (string.IsNullOrEmpty(m.causeOfDeath) ? "Dead." : m.causeOfDeath) + "</color>", 22, UIKit.Ink, 36); return; }
            // Actions first so a controller reaches them quickly.
            var row = UIKit.Row(commDetail, 56, 10);
            string why = ""; bool canSwitch = m.id != w.playerId && game.Sim.CanSwitchTo(m.id, out why);
            if (m.id == w.playerId) why = "You control " + m.name + ".";
            int mid = m.id;
            var sw = UIKit.Primary(row.transform, "Switch to", () => game.Submit(new Command { kind = CommandKind.SwitchCharacter, target = mid, recipe = "" }), 220, 52, 22); sw.interactable = canSwitch;
            bool isMate = w.community.companion == m.id;
            var mate = UIKit.Button(row.transform, isMate ? "Dismiss companion" : "Make companion", () => game.Submit(new Command { kind = CommandKind.SetCompanion, target = isMate ? -1 : mid, recipe = "" }), 260, 52, 22);
            mate.interactable = m.id != w.playerId;
            if (!canSwitch && !string.IsNullOrEmpty(why)) UIKit.Line(commDetail, "<color=#999>" + why + "</color>", 18, UIKit.Dim, 28);
            if (isMate) UIKit.Line(commDetail, "Companion order: <b>" + OrderName(m.order) + "</b>  <color=#999>(C / d-pad up cycles Follow, Hold, Attack)</color>", 19, UIKit.Blue, 30);
            UIKit.Divider(commDetail);
            StatRow(commDetail, "Health", m.health / 100, Mathf.CeilToInt(m.health) + " / " + Mathf.RoundToInt(m.MaxHealth), UIKit.Health, 1 - m.MaxHealth / 100);
            StatRow(commDetail, "Stamina", m.endurance / 100, Mathf.RoundToInt(m.endurance) + "%", UIKit.Stamina, 0);
            StatRow(commDetail, "Rested", 1 - m.fatigue / 100, m.fatigue > 75 ? "exhausted" : m.fatigue > 50 ? "tired" : "rested", UIKit.Fatigue, 0);
            if (m.plagueInfected || m.plague > 0) StatRow(commDetail, "Blood plague", m.plague / 100, Mathf.RoundToInt(m.plague) + "%", UIKit.Plague, 0);
            UiData.Injuries(m, injuryLines);
            UIKit.Line(commDetail, "<b>Injuries</b>", 22, UIKit.Accent, 34);
            if (injuryLines.Count == 0) UIKit.Line(commDetail, "None.", 19, UIKit.Good, 28);
            foreach (var l in injuryLines) UIKit.Line(commDetail, l, 19, UIKit.Ink, 28);
            UIKit.Line(commDetail, "<b>Skills</b>", 22, UIKit.Accent, 34);
            foreach (var k in UiData.CoreSkills) SkillRow(m, k);
            foreach (var k in UiData.Specialties) if (m.Level(k) > 0) SkillRow(m, k);
        }

        void StatRow(RectTransform parent, string label, float fill, string value, Color color, float capped)
        {
            var row = UIKit.Row(parent, 32, 10); UIKit.RowLabel(row.transform, label, 170, 19, UIKit.Dim);
            var holder = UIKit.Rect(row.transform, "Bar", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); var le = holder.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 420; le.preferredHeight = 16;
            UIKit.Card(holder, new Color(0, 0, 0, .6f));
            if (capped > .005f) { var inj = UIKit.Rect(holder, "Injury", new Vector2(1 - capped, 0), Vector2.one, new Vector2(0, 2), new Vector2(-2, -2)); var ii = inj.gameObject.AddComponent<Image>(); ii.sprite = UIKit.White; ii.color = UIKit.Injury; ii.raycastTarget = false; }
            UIKit.FillBar(holder, color, 2).fillAmount = Mathf.Clamp01(fill);
            UIKit.RowLabel(row.transform, value, 200, 18, UIKit.Ink);
        }
        void SkillRow(Survivor m, Skill k)
        {
            int lv = m.Level(k);
            var row = UIKit.Row(commDetail, 30, 8); UIKit.RowLabel(row.transform, UiData.SkillName(k), 200, 20);
            UIKit.RowLabel(row.transform, "<color=#f0a040>" + new string('■', Mathf.Clamp(lv, 0, 10)) + "</color><color=#3a3a3a>" + new string('■', 10 - Mathf.Clamp(lv, 0, 10)) + "</color>", 260, 20);
            UIKit.RowLabel(row.transform, lv >= 10 ? "max" : "level " + lv, 120, 17, UIKit.Dim);
        }

        // ---------------------------------------------------------------- base
        void RefreshBase(World w, Survivor s)
        {
            UIKit.Clear(commBase); var c = w.community; var sim = game.Sim;
            string baseName = c.baseBuilding >= 0 ? UiData.BuildingName(w.map, c.baseBuilding) : "No home base";
            int used = UiCommunityApi.UsedSlots(sim, w);
            UIKit.Line(commBase, "<b><size=30>" + baseName + "</size></b>   <color=#aaa>facility slots " + used + " / " + sim.BaseSlots + "</color>" + (sim.AtBase(s) ? "" : "   <color=#e0c070>(you are away: some actions need you at base)</color>"), 24, UIKit.Ink, 46);
            // Stock against capacity.
            var stockRow = UIKit.Row(commBase, 60, 12);
            foreach (var r in UiData.Resources)
            {
                int v = Mathf.FloorToInt(c.Stock(r)), cap = Mathf.RoundToInt(UiData.Capacity(w, r));
                var cell = UIKit.Rect(stockRow.transform, r.ToString(), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); var le = cell.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 200; le.preferredHeight = 56;
                UIKit.Card(cell, new Color(0, 0, 0, .35f));
                UIKit.Label(cell, "<size=14><color=#999>" + r.ToString().ToUpperInvariant() + "</color></size>\n<b>" + v + "</b><color=#888> / " + cap + "</color>", 22, UiData.ResourceColor(r), Vector2.zero, Vector2.one, new Vector2(14, 4), new Vector2(-8, -4), TextAlignmentOptions.MidlineLeft);
            }
            string report = UiCommunityApi.LastReport(w);
            UIKit.Line(commBase, report.Length > 0 ? "<color=#9fd3ff>" + report + "</color>" : "Each member eats 1 Food at 06:00; the injured use Meds. Shortfalls cost morale.", 18, UIKit.Dim, 30);
            UIKit.Divider(commBase);
            UIKit.Line(commBase, "<b>Facilities</b>", 24, UIKit.Accent, 36);
            foreach (var kind in UiData.Facilities)
            {
                var f = c.Get(kind); var k = kind;
                var row = UIKit.Row(commBase, 62, 12);
                string state;
                if (f == null) state = "<color=#888>not built</color>";
                else if (f.buildHours > 0) state = "<color=#f0c060>building · " + Mathf.RoundToInt(UiData.Progress(f) * 100) + "% · " + f.buildHours.ToString("0.#") + " h left</color>";
                else state = "<color=#9fe08a>level " + f.level + "</color>";
                UIKit.RowLabel(row.transform, "<b>" + UiData.FacilityName(kind) + "</b>  " + state + "\n<size=16><color=#aaa>" + UiCommunityApi.FacilityDescription(kind) + "</color></size>", 760, 21);
                int next = f == null ? 1 : f.level + 1;
                string why; bool can = (f == null ? sim.CanBuild(kind, out why) : UiCommunityApi.CanUpgrade(sim, kind, out why)) && (f == null || f.buildHours <= 0);
                if (f != null && f.buildHours > 0) why = "Under construction";
                string cost = Simulation.FacilityMaterials(kind, next).ToString("0") + " materials · " + Simulation.FacilityHours(kind, next).ToString("0.#") + " h";
                var b = UIKit.Button(row.transform, (f == null ? "Build" : "Upgrade") + "  <size=16><color=#ccc>" + cost + "</color></size>", () => game.Submit(new Command { kind = f == null ? CommandKind.BuildFacility : CommandKind.UpgradeFacility, target = (int)k, recipe = "" }), 360, 54, 20);
                b.interactable = can;
                if (!can && !string.IsNullOrEmpty(why)) UIKit.RowLabel(row.transform, "<color=#999>" + why + "</color>", 260, 16, UIKit.Dim);
            }
            UIKit.Divider(commBase);
            UIKit.Line(commBase, "<b>Outposts</b>  <color=#999><size=18>claim a cleared, visited building with E/Q for a daily bonus and a safe point</size></color>", 24, UIKit.Accent, 36);
            if (c.outposts.Count == 0) UIKit.Line(commBase, "No outposts yet.", 19, UIKit.Dim, 28);
            foreach (var o in c.outposts)
            {
                var row = UIKit.Row(commBase, 52, 12); int bid = o.building;
                UIKit.RowLabel(row.transform, "<b>" + UiData.BuildingName(w.map, o.building) + "</b>  <color=#aaa>+" + o.bonus + " daily · " + Mathf.RoundToInt(Vec2.Distance(o.pos, s.pos)) + " m " + UiCompass.Bearing(new Vector2(o.pos.x - s.pos.x, o.pos.y - s.pos.y)) + "</color>", 760, 20);
                UIKit.Button(row.transform, "Abandon", () => game.Submit(new Command { kind = CommandKind.AbandonOutpost, target = bid, recipe = "" }), 200, 46, 20);
            }
        }

        void RefreshWorkshop(World w, Survivor s)
        {
            UIKit.Clear(commWorkshop);
            UIKit.Line(commWorkshop, "<b><size=28>Workshop</size></b>   <color=#aaa>repairs, explosives and gear made from community stock and carried parts</color>", 22, UIKit.Ink, 44);
            var list = UiCommunityApi.Workshop(w, game.Sim, s);
            if (list.Count == 0) UIKit.Line(commWorkshop, "No workshop recipes are available yet.", 20, UIKit.Dim, 34);
            foreach (var r in list)
            {
                var e = r; var row = UIKit.Row(commWorkshop, 64, 12);
                UIKit.RowLabel(row.transform, "<b>" + r.name + "</b>  <size=16><color=#aaa>" + r.cost + " · " + r.facility + "</color></size>\n<size=16><color=#999>" + r.description + "</color></size>", 900, 20);
                var b = UIKit.Button(row.transform, "Craft", () => game.Submit(new Command { kind = CommandKind.CraftAtWorkshop, recipe = e.id }), 180, 50, 21); b.interactable = r.enabled;
                if (!r.enabled && r.reason.Length > 0) UIKit.RowLabel(row.transform, "<color=#999>" + r.reason + "</color>", 300, 16, UIKit.Dim);
            }
        }
    }
}
