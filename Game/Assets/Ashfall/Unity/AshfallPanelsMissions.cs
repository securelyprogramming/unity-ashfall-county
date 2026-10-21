using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ashfall.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashfall.View
{
    /// <summary>Missions panel (J) with the radio log, and the Talk/Trade dialog opened by interacting with an NPC.</summary>
    public sealed partial class AshfallUI
    {
        RectTransform missionList, radioList, missionPage, radioPage, talkBody;
        TextMeshProUGUI talkTitle, talkSub;
        readonly Dictionary<string, Button> missionTabButtons = new Dictionary<string, Button>();
        readonly HashSet<int> untracked = new HashSet<int>();   // active missions hidden from the map and minimap
        readonly List<string> talkLines = new List<string>();
        string missionTab = "missions", lastMissionSig = "", lastTalkSig = ""; int talkNpc = -1;

        /// <summary>Active missions show map markers unless the player switched them off.</summary>
        bool Tracked(int id) { return !untracked.Contains(id); }

        void BuildMissionPanels()
        {
            var p = PanelFrame("missions", "MISSIONS", new Vector2(.12f, .06f), new Vector2(.88f, .94f));
            var tabs = UIKit.Strip(p, "Tabs", new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -124), new Vector2(-28, -76), TextAnchor.MiddleLeft, 8);
            foreach (var t in new[] { "missions", "radio" }) { string id = t; missionTabButtons[id] = UIKit.Button(tabs, id == "missions" ? "Missions" : "Radio log", () => { missionTab = id; lastMissionSig = ""; RefreshMissions(); }, 220, 44, 21); }
            var body = UIKit.Rect(p, "Body", Vector2.zero, Vector2.one, new Vector2(28, 24), new Vector2(-28, -136));
            missionPage = UIKit.Fill(body, "Missions"); UIKit.Scroll(missionPage, "List", out missionList);
            radioPage = UIKit.Fill(body, "Radio"); UIKit.Scroll(radioPage, "List", out radioList);

            var talk = PanelFrame("talk", "TALK", new Vector2(.24f, .12f), new Vector2(.76f, .88f));
            talkTitle = UIKit.Label(talk, "", 30, UIKit.Ink, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -116), new Vector2(-28, -70)); talkTitle.fontStyle = FontStyles.Bold;
            talkSub = UIKit.Label(talk, "", 19, UIKit.Dim, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -144), new Vector2(-28, -114));
            var tb = UIKit.Rect(talk, "Body", Vector2.zero, Vector2.one, new Vector2(24, 24), new Vector2(-24, -150)); UIKit.Scroll(tb, "List", out talkBody);
        }

        void RefreshOpenMissions()
        {
            if (open.Count == 0) return;
            if (open.Peek() == "missions") { lastMissionSig = ""; RefreshMissions(true); }
            else if (open.Peek() == "talk") { lastTalkSig = ""; RefreshTalk(true); }
        }

        void RefreshMissions(bool soft = false)
        {
            var w = game.World; var s = game.Player; if (w == null || s == null) return;
            var ms = w.community.missions;
            var sig = new StringBuilder(missionTab).Append(game.Radio.Count).Append('|').Append(history.Count);
            foreach (var m in ms) sig.Append(m.id).Append((int)m.status).Append(Tracked(m.id)).Append((int)((m.expiresAt - w.minutes) / 30));
            string key = sig.ToString(); if (soft && key == lastMissionSig) return; lastMissionSig = key;
            foreach (var kv in missionTabButtons) UIKit.SetActive(kv.Value, kv.Key == missionTab);
            missionPage.gameObject.SetActive(missionTab == "missions"); radioPage.gameObject.SetActive(missionTab == "radio");
            if (missionTab == "radio") { RefreshRadio(w); return; }
            UIKit.Clear(missionList);
            var active = ms.Where(m => m.status == MissionStatus.Active).ToList();
            var offered = ms.Where(m => m.status == MissionStatus.Offered).ToList();
            var done = ms.Where(m => m.status != MissionStatus.Active && m.status != MissionStatus.Offered).Reverse().Take(6).ToList();
            UIKit.Line(missionList, "<b>Active</b>", 24, UIKit.Accent, 36);
            if (active.Count == 0) UIKit.Line(missionList, "No active missions. Accept one below, or listen to the radio.", 19, UIKit.Dim, 30);
            foreach (var m in active) MissionCard(missionList, w, s, m, false);
            UIKit.Line(missionList, "<b>Offered</b>", 24, UIKit.Accent, 36);
            if (offered.Count == 0) UIKit.Line(missionList, "Nothing on offer right now. Enclaves and your own people post new work over the radio.", 19, UIKit.Dim, 30);
            foreach (var m in offered) MissionCard(missionList, w, s, m, false);
            if (done.Count > 0)
            {
                UIKit.Line(missionList, "<b>Recent</b>", 24, UIKit.Accent, 36);
                foreach (var m in done) UIKit.Line(missionList, (m.status == MissionStatus.Completed ? "<color=#9fe08a>Completed</color>  " : m.status == MissionStatus.Failed ? "<color=#ff8a7a>Failed</color>  " : "<color=#999>Expired</color>  ") + m.title + "  <color=#888>· " + UiCommunityApi.GiverName(m) + "</color>", 19, UIKit.Ink, 28);
            }
        }

        void MissionCard(RectTransform list, World w, Survivor s, Mission m, bool compact)
        {
            var card = UIKit.Rect(list, "Mission " + m.id, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var le = card.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = compact ? 120 : 150;
            UIKit.Card(card, new Color(1, 1, 1, .04f));
            bool hasTarget = m.target.x != 0 || m.target.y != 0;
            string where = hasTarget ? Mathf.RoundToInt(Vec2.Distance(s.pos, m.target)) + " m " + UiCompass.Bearing(new Vector2(m.target.x - s.pos.x, m.target.y - s.pos.y)) : "";
            double left = UiCommunityApi.MissionHoursLeft(game.Sim, w, m) * 60;
            string reward = (m.rewardInfluence > 0 ? "+" + m.rewardInfluence + " influence" : "") + (string.IsNullOrEmpty(m.rewardItem) ? "" : (m.rewardInfluence > 0 ? ", " : "") + (ItemCatalog.Get(m.rewardItem) != null ? ItemCatalog.Get(m.rewardItem).name : m.rewardItem));
            UIKit.Label(card, "<b>" + (m.title.Length > 0 ? m.title : UiData.MissionKindName(m.kind)) + "</b>  <color=#aaa><size=17>" + UiData.MissionKindName(m.kind) + " · " + UiCommunityApi.GiverName(m) + (where.Length > 0 ? " · " + where : "") + "</size></color>\n" +
                "<size=18>" + m.description + "</size>\n<size=17><color=#9fe08a>" + (reward.Length > 0 ? "Reward: " + reward : "") + "</color>" + (m.expiresAt > 0 ? "   <color=" + (left < 120 ? "#ff9a80" : "#aaa") + ">expires in " + UiData.Hours(left) + "</color>" : "") + "</size>",
                21, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(18, 8), new Vector2(-300, -8), TextAlignmentOptions.MidlineLeft);
            var buttons = UIKit.Rect(card, "Buttons", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-284, 10), new Vector2(-14, -10)); UIKit.Column(buttons, 8, TextAnchor.MiddleCenter);
            int id = m.id;
            if (m.status == MissionStatus.Offered) UIKit.Primary(buttons, "Accept", () => game.Submit(new Command { kind = CommandKind.AcceptMission, target = id, recipe = "" }), 260, 50, 22);
            else if (m.status == MissionStatus.Active)
            {
                UIKit.Button(buttons, Tracked(id) ? "Map marker: on" : "Map marker: off", () => { if (!untracked.Remove(id)) untracked.Add(id); lastMissionSig = ""; RefreshMissions(); }, 260, 46, 20);
                UIKit.Button(buttons, "Abandon", () => game.Submit(new Command { kind = CommandKind.AbandonMission, target = id, recipe = "" }), 260, 46, 20);
            }
        }

        void RefreshRadio(World w)
        {
            UIKit.Clear(radioList);
            UIKit.Line(radioList, "<b>Broadcasts</b>", 24, UIKit.Accent, 36);
            for (int i = game.Radio.Count - 1; i >= 0 && i >= game.Radio.Count - 60; i--) UIKit.Line(radioList, game.Radio[i], 20, new Color(.62f, .83f, 1), 56);
            if (game.Radio.Count == 0) UIKit.Line(radioList, "Static. Broadcasts from the Guard, enclaves and your own people show up here.", 20, UIKit.Dim, 40);
            UIKit.Line(radioList, "<b>Recent events</b>", 24, UIKit.Accent, 36);
            for (int i = history.Count - 1; i >= 0 && i >= history.Count - 60; i--) UIKit.Line(radioList, history[i], 18, UIKit.Ink, 28);
        }

        // ---------------------------------------------------------------- talk and trade
        /// <summary>Starts a conversation: asks the host (Talk command) and opens the dialog with greeting, trades, recruiting and missions.</summary>
        public void OpenTalk(int npc)
        {
            talkNpc = npc; talkLines.Clear(); lastTalkSig = "";
            game.Submit(new Command { kind = CommandKind.Talk, target = npc, recipe = "" });
            OpenPanel("talk");
        }
        /// <summary>A reply from the host (Talk or Trade event text) shown in the dialog transcript.</summary>
        void TalkLine(string text)
        {
            talkLines.Add(text); if (talkLines.Count > 8) talkLines.RemoveAt(0);
            if (open.Count > 0 && open.Peek() == "talk") { lastTalkSig = ""; RefreshTalk(true); }
        }

        void RefreshTalk(bool soft = false)
        {
            var w = game.World; var s = game.Player; if (w == null || s == null) return;
            var npc = talkNpc >= 0 && talkNpc < w.survivors.Count ? w.survivors[talkNpc] : null;
            if (npc == null || !npc.alive || Vec2.Distance(npc.pos, s.pos) > 6) { if (open.Count > 0 && open.Peek() == "talk") CloseTopPanel(); return; }
            var sim = game.Sim;
            var offers = sim.TradeOffers(s, npc) ?? new List<TradeOffer>();
            string recruitWhy; bool canRecruit = sim.CanRecruit(s, npc, out recruitWhy);
            var theirs = w.community.missions.Where(m => (m.status == MissionStatus.Offered || m.status == MissionStatus.Active) && UiData.GivenBy(w, m, npc)).ToList();
            var sig = new StringBuilder().Append(npc.id).Append(canRecruit).Append(recruitWhy).Append((int)w.community.influence).Append(talkLines.Count).Append(npc.faction);
            foreach (var o in offers) sig.Append(o.label).Append(o.enabled).Append(o.reason);
            foreach (var m in theirs) sig.Append(m.id).Append((int)m.status);
            string key = sig.ToString(); if (soft && key == lastTalkSig) return; lastTalkSig = key;

            var c = UiData.Stance(w, s, npc);
            talkTitle.text = UiData.DisplayName(npc); talkTitle.color = c;
            var def = string.IsNullOrEmpty(npc.character) ? null : Characters.Get(npc.character);
            talkSub.text = UiData.Subtitle(w, npc) + " · " + UiData.StanceName(w, s, npc) + (w.FactionOf(npc) != null && npc.faction != 0 ? " · standing " + Mathf.RoundToInt(w.FactionOf(npc).standing) : "");
            UIKit.Clear(talkBody);
            if (def != null && !string.IsNullOrEmpty(def.description)) UIKit.Line(talkBody, "<color=#aaa><i>" + def.description + "</i></color>", 18, UIKit.Dim, 50);
            UIKit.Line(talkBody, "“" + sim.Greeting(s, npc) + "”", 23, UIKit.Ink, 70);
            foreach (var l in talkLines) UIKit.Line(talkBody, "<color=#cfc6b0>" + l + "</color>", 19, UIKit.Ink, 46);
            UIKit.Divider(talkBody);
            UIKit.Line(talkBody, "<b>Trade</b>  <color=#999><size=17>influence " + (int)w.community.influence + "</size></color>", 22, UIKit.Accent, 34);
            if (offers.Count == 0) UIKit.Line(talkBody, "Nothing to trade.", 19, UIKit.Dim, 28);
            for (int i = 0; i < offers.Count; i++)
            {
                var o = offers[i]; int slot = i; int target = npc.id;
                var b = UIKit.Button(talkBody, o.label + (o.detail.Length > 0 ? "  <size=16><color=#aaa>" + o.detail + "</color></size>" : "") + (!o.enabled && o.reason.Length > 0 ? "  <size=16><color=#888>(" + o.reason + ")</color></size>" : ""),
                    () => game.Submit(new Command { kind = CommandKind.Trade, target = target, slot = slot, recipe = "" }), 900, 50, 20);
                b.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft; b.interactable = o.enabled;
            }
            if (npc.faction != 0)
            {
                UIKit.Line(talkBody, "<b>Recruit</b>", 22, UIKit.Accent, 34);
                int target = npc.id;
                var rb = UIKit.Primary(talkBody, "Invite " + npc.name + " to join" + (def != null && def.recruitInfluence > 0 ? "  <size=16>(" + def.recruitInfluence + " influence)</size>" : ""), () => game.Submit(new Command { kind = CommandKind.Recruit, target = target, recipe = "" }), 900, 52, 21);
                rb.interactable = canRecruit;
                if (!canRecruit && !string.IsNullOrEmpty(recruitWhy)) UIKit.Line(talkBody, "<color=#999>" + recruitWhy + "</color>", 18, UIKit.Dim, 28);
            }
            if (theirs.Count > 0)
            {
                UIKit.Line(talkBody, "<b>Work from " + npc.name + "</b>", 22, UIKit.Accent, 34);
                foreach (var m in theirs) MissionCard(talkBody, w, s, m, true);
            }
        }
    }
}
