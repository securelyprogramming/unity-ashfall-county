using System.Collections.Generic;
using Ashfall.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashfall.View
{
    /// <summary>In-game HUD: controlled-survivor card, companion, community strip, clock and counters, kill feed, banners,
    /// minimap, log, hotbar, prompt and threat nameplates. Built once; per-frame updates write fills and positions and touch
    /// text only when a value changes.</summary>
    public sealed partial class AshfallUI
    {
        // ---------------------------------------------------------------- log (bottom left) and history (radio panel)
        readonly List<string> log = new List<string>(); readonly List<float> logTimes = new List<float>();
        readonly List<string> history = new List<string>();
        const int LogLines = 6; readonly TextMeshProUGUI[] logLabels = new TextMeshProUGUI[LogLines]; bool logDirty;

        /// <summary>Adds a line to the HUD log and the event history (shown in the Missions panel's radio tab).</summary>
        public void Log(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            log.Add(text); logTimes.Add(Time.unscaledTime);
            if (log.Count > LogLines) { log.RemoveAt(0); logTimes.RemoveAt(0); }
            history.Add("<color=#888>" + (game.World == null ? "" : game.World.Clock) + "</color>  " + text); if (history.Count > 300) history.RemoveAt(0);
            logDirty = true;
        }

        UiCountyMap countyMap;

        // ---------------------------------------------------------------- widgets
        RectTransform card, plagueRow, mateCard, chipRow, zoneCard, bannerRoot, mini, miniMarkers, plateLayer, hotbar, actionBar, deathNotice, reticle;
        TextMeshProUGUI cardName, cardSub, healthText, plagueText, armorText, mateName, mateOrder, moraleText, influenceText, clock, weather, counters, zoneText, bannerTitle, bannerSub;
        TextMeshProUGUI prompt, weaponText, actionText, tipText, deathText;
        Image staminaCap, healthFill, injuryFill, staminaFill, fatigueFill, plagueFill, mateHealth, bannerRule, zoneRule, actionFill;
        GameObject chipBleed, chipHeavy, chipSneak, chipTired, chipArmor;
        readonly TextMeshProUGUI[] stockText = new TextMeshProUGUI[5];
        const int FeedLines = 5; readonly TextMeshProUGUI[] feedLabels = new TextMeshProUGUI[FeedLines]; readonly string[] feed = new string[FeedLines]; readonly float[] feedTimes = new float[FeedLines]; int feedCount; bool feedDirty;
        CanvasGroup bannerGroup;
        RawImage miniMap, miniFog; Image miniPlayer, miniZoneRing, miniZoneFill; readonly List<Image> miniPool = new List<Image>(); int miniUsed;
        const float MiniRadius = 160, MiniSize = 270;

        struct BannerMsg { public string title, sub; public Color color; public float seconds; }
        readonly Queue<BannerMsg> banners = new Queue<BannerMsg>(); float bannerUntil, bannerShown; string lastBannerTitle = ""; float lastBannerAt;

        void BuildHud()
        {
            hud = UIKit.Fill(root, "HUD");
            plateLayer = UIKit.Fill(hud, "Nameplates");
            BuildPlates();
            reticle = UIKit.Box2(hud, "Throw reticle", Vector2.zero, new Vector2(.5f, .5f), Vector2.zero, new Vector2(70, 70)); var ri = reticle.gameObject.AddComponent<Image>(); ri.sprite = UIKit.IconOr("SPR_HUD_Reticle_Circle_Sml", UIKit.Ring); ri.color = UIKit.Warn; ri.raycastTarget = false; reticle.gameObject.SetActive(false);

            // Controlled survivor (top left).
            card = UIKit.Box2(hud, "Survivor", new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), new Vector2(450, 172)); UIKit.Card(card, new Color(.03f, .035f, .04f, .78f));
            cardName = UIKit.Label(card, "", 27, UIKit.Ink, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -46), new Vector2(-18, -10)); cardName.fontStyle = FontStyles.Bold; cardName.textWrappingMode = TextWrappingModes.NoWrap;
            cardSub = UIKit.Label(card, "", 18, UIKit.Dim, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -70), new Vector2(-18, -44));
            var hb = UIKit.Box2(card, "Health", new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -80), new Vector2(414, 26)); UIKit.Card(hb, new Color(0, 0, 0, .65f));
            var inj = UIKit.Rect(hb, "Injury", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-3, 3), new Vector2(-3, -3)); injuryFill = inj.gameObject.AddComponent<Image>(); injuryFill.sprite = UIKit.White; injuryFill.color = UIKit.Injury; injuryFill.raycastTarget = false;
            healthFill = UIKit.FillBar(hb, UIKit.Health);
            healthText = UIKit.Label(hb, "", 18, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0), TextAlignmentOptions.MidlineRight); healthText.fontStyle = FontStyles.Bold;
            UIKit.Label(hb, "HEALTH", 15, new Color(1, 1, 1, .7f), Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0), TextAlignmentOptions.MidlineLeft).characterSpacing = 4;
            staminaFill = SmallBar(card, "Stamina", -112, UIKit.Stamina);
            // Fatigue and plague cap stamina: the unreachable part of the bar is shaded like the injury segment.
            var sc = UIKit.Rect(staminaFill.rectTransform.parent, "Cap", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2, 2), new Vector2(-2, -2)); staminaCap = sc.gameObject.AddComponent<Image>(); staminaCap.sprite = UIKit.White; staminaCap.color = new Color(.08f, .16f, .22f, .95f); staminaCap.raycastTarget = false;
            fatigueFill = SmallBar(card, "Rested", -132, UIKit.Fatigue);
            plagueRow = UIKit.Box2(card, "Plague", new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -146), new Vector2(414, 22));
            var pb = UIKit.Box2(plagueRow, "Bar", new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(250, 12)); UIKit.Card(pb, new Color(0, 0, 0, .65f)); plagueFill = UIKit.FillBar(pb, UIKit.Plague, 2);
            plagueText = UIKit.Label(plagueRow, "", 17, UIKit.Plague, Vector2.zero, Vector2.one, new Vector2(262, 0), Vector2.zero, TextAlignmentOptions.MidlineLeft);
            chipRow = UIKit.Strip(card, "Status", new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 6), new Vector2(-14, 34), TextAnchor.MiddleLeft, 6);
            chipBleed = Chip("Bleeding", "ICON_Status_Bleeding", UIKit.Bad); chipHeavy = Chip("Overloaded", "ICON_Inventory_Backpack", UIKit.Warn);
            chipTired = Chip("Exhausted", null, UIKit.Fatigue); chipSneak = Chip("Sneaking", null, UIKit.Blue); chipArmor = Chip("Armour 0%", "ICON_Status_Armor", UIKit.Ink); armorText = chipArmor.GetComponentInChildren<TextMeshProUGUI>();

            // Companion (under the card).
            mateCard = UIKit.Box2(hud, "Companion", new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -200), new Vector2(320, 62)); UIKit.Card(mateCard, new Color(.03f, .035f, .04f, .7f));
            mateName = UIKit.Label(mateCard, "", 20, UIKit.Blue, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -32), new Vector2(-14, -6)); mateName.textWrappingMode = TextWrappingModes.NoWrap;
            mateOrder = UIKit.Label(mateCard, "", 16, UIKit.Dim, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -32), new Vector2(-14, -8), TextAlignmentOptions.TopRight);
            var mb = UIKit.Box2(mateCard, "Health", new Vector2(0, 0), new Vector2(0, 0), new Vector2(14, 10), new Vector2(292, 10)); mb.pivot = Vector2.zero; UIKit.Card(mb, new Color(0, 0, 0, .65f)); mateHealth = UIKit.FillBar(mb, UIKit.Health, 2);
            mateCard.gameObject.SetActive(false);

            // Community strip (top centre).
            var strip = UIKit.Box2(hud, "Community", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -16), new Vector2(860, 54)); UIKit.Card(strip, new Color(.03f, .035f, .04f, .78f));
            var cells = UIKit.Strip(strip, "Cells", Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0), TextAnchor.MiddleCenter, 4);
            for (int i = 0; i < 5; i++) stockText[i] = StripCell(cells, UiData.ResourceIcon(UiData.Resources[i]), UiData.ResourceColor(UiData.Resources[i]), 116, UiData.ResourceName(UiData.Resources[i]));
            moraleText = StripCell(cells, null, UIKit.Good, 128, "Morale"); influenceText = StripCell(cells, null, UIKit.Accent, 140, "Influence");

            // Red zone warning (under the strip) and banners.
            zoneCard = UIKit.Box2(hud, "Red zone", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -80), new Vector2(760, 56)); UIKit.Card(zoneCard, new Color(.22f, .03f, .03f, .86f));
            zoneRule = UIKit.Img(zoneCard, "Rule", UIKit.White, UIKit.Bad, new Vector2(0, .5f), new Vector2(3, 0), new Vector2(6, 46)); zoneRule.preserveAspect = false;
            zoneText = UIKit.Label(zoneCard, "", 22, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(20, 0), new Vector2(-16, 0), TextAlignmentOptions.Center);
            zoneCard.gameObject.SetActive(false);
            bannerRoot = UIKit.Box2(hud, "Banner", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -146), new Vector2(980, 96));
            bannerGroup = bannerRoot.gameObject.AddComponent<CanvasGroup>(); bannerGroup.alpha = 0; bannerGroup.blocksRaycasts = false;
            UIKit.Card(bannerRoot, new Color(.02f, .022f, .026f, .72f));
            bannerRule = UIKit.Img(bannerRoot, "Rule", UIKit.White, UIKit.Accent, new Vector2(.5f, 1), new Vector2(0, -2), new Vector2(240, 4)); bannerRule.preserveAspect = false;
            bannerTitle = UIKit.Label(bannerRoot, "", 36, UIKit.Ink, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -54), new Vector2(-20, -8), TextAlignmentOptions.Center); bannerTitle.fontStyle = FontStyles.Bold; bannerTitle.characterSpacing = 6;
            bannerSub = UIKit.Label(bannerRoot, "", 22, UIKit.Ink, new Vector2(0, 0), new Vector2(1, 1), new Vector2(20, 8), new Vector2(-20, -54), TextAlignmentOptions.Center);

            // Clock, counters and kill feed (top right).
            var tr = UIKit.Box2(hud, "Clock", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -16), new Vector2(380, 92)); UIKit.Card(tr, new Color(.03f, .035f, .04f, .78f));
            clock = UIKit.Label(tr, "", 27, UIKit.Ink, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -44), new Vector2(-16, -8), TextAlignmentOptions.TopRight); clock.fontStyle = FontStyles.Bold;
            weather = UIKit.Label(tr, "", 18, UIKit.Dim, new Vector2(0, 0), new Vector2(1, 1), new Vector2(16, 30), new Vector2(-16, -46), TextAlignmentOptions.TopRight);
            counters = UIKit.Label(tr, "", 18, UIKit.Ink, new Vector2(0, 0), new Vector2(1, 0), new Vector2(16, 6), new Vector2(-16, 32), TextAlignmentOptions.BottomRight);
            for (int i = 0; i < FeedLines; i++)
            {
                feedLabels[i] = UIKit.Label(hud, "", 20, UIKit.Ink, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-640, -150 - i * 30), new Vector2(-24, -122 - i * 30), TextAlignmentOptions.TopRight);
                feedLabels[i].textWrappingMode = TextWrappingModes.NoWrap; feedLabels[i].outlineWidth = .2f; feedLabels[i].outlineColor = new Color32(0, 0, 0, 200);
            }

            // Minimap (bottom right).
            mini = UIKit.Box2(hud, "Minimap", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(MiniSize + 12, MiniSize + 12)); UIKit.Card(mini, new Color(.03f, .035f, .04f, .85f));
            var view = UIKit.Rect(mini, "View", Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6)); view.gameObject.AddComponent<RectMask2D>();
            miniMap = UIKit.Fill(view, "County").gameObject.AddComponent<RawImage>(); miniMap.raycastTarget = false; miniMap.color = new Color(.8f, .8f, .8f);
            miniFog = UIKit.Fill(view, "Fog").gameObject.AddComponent<RawImage>(); miniFog.raycastTarget = false;
            miniZoneFill = UIKit.Img(view, "Zone", UIKit.Circle, new Color(1, .1f, .05f, .22f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(10, 10));
            miniZoneRing = UIKit.Img(view, "Zone ring", UIKit.Ring, new Color(1, .25f, .2f, .95f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(10, 10));
            miniMarkers = UIKit.Fill(view, "Markers");
            miniPlayer = UIKit.Img(view, "You", UIKit.Arrow, UIKit.Accent, new Vector2(.5f, .5f), Vector2.zero, new Vector2(22, 22));
            UIKit.Label(mini, "N", 18, UIKit.Ink, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-20, -30), new Vector2(20, -6), TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;

            // Weapon (left of the minimap), hotbar, prompt, action progress, log and tips.
            weaponText = UIKit.Label(hud, "", 24, UIKit.Ink, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-920, 26), new Vector2(-322, 150), TextAlignmentOptions.BottomRight);
            hotbar = UIKit.Rect(hud, "Hotbar", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-370, 20), new Vector2(370, 104));
            var hbl = hotbar.gameObject.AddComponent<HorizontalLayoutGroup>(); hbl.spacing = 8; hbl.childAlignment = TextAnchor.MiddleCenter; hbl.childControlWidth = false; hbl.childControlHeight = false;
            for (int i = 0; i < 6; i++)
            {
                var slot = UIKit.Box2(hotbar, "Slot " + i, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(112, 84)); UIKit.Card(slot, new Color(0, 0, 0, .5f));
                var ic = UIKit.Box2(slot, "Icon", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 8), new Vector2(54, 54)); var img = ic.gameObject.AddComponent<Image>(); img.raycastTarget = false; img.preserveAspect = true;
                UIKit.Label(slot, "", 15, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(4, 2), new Vector2(-4, -2), TextAlignmentOptions.Bottom);
                UIKit.Label(slot, (i + 1).ToString(), 15, UIKit.Dim, Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(0, -5), TextAlignmentOptions.TopLeft);
            }
            prompt = UIKit.Label(hud, "", 26, UIKit.Ink, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-600, 124), new Vector2(600, 200), TextAlignmentOptions.Bottom); prompt.outlineWidth = .18f; prompt.outlineColor = new Color32(0, 0, 0, 220);
            actionBar = UIKit.Box2(hud, "Action", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(320, 44)); UIKit.Card(actionBar, new Color(0, 0, 0, .6f));
            actionFill = UIKit.FillBar(actionBar, UIKit.Accent, 4);
            actionText = UIKit.Label(actionBar, "", 20, UIKit.Ink, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
            for (int i = 0; i < LogLines; i++)
            {
                logLabels[i] = UIKit.Label(hud, "", 21, UIKit.Ink, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 30 + (LogLines - 1 - i) * 30), new Vector2(840, 60 + (LogLines - 1 - i) * 30), TextAlignmentOptions.BottomLeft);
                logLabels[i].textWrappingMode = TextWrappingModes.NoWrap; logLabels[i].outlineWidth = .18f; logLabels[i].outlineColor = new Color32(0, 0, 0, 200);
            }
            tipText = UIKit.Label(hud, "", 19, UIKit.Dim, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-920, 160), new Vector2(-322, 232), TextAlignmentOptions.BottomRight);
            deathNotice = UIKit.Box2(hud, "Member down", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 60), new Vector2(760, 110)); UIKit.Card(deathNotice, new Color(.15f, .02f, .02f, .85f));
            deathText = UIKit.Label(deathNotice, "", 26, UIKit.Ink, Vector2.zero, Vector2.one, new Vector2(20, 0), new Vector2(-20, 0), TextAlignmentOptions.Center);
            deathNotice.gameObject.SetActive(false);
        }

        Image SmallBar(RectTransform parent, string label, float y, Color color)
        {
            var r = UIKit.Box2(parent, label, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, y), new Vector2(414, 14)); r.pivot = new Vector2(0, 1);
            var bar = UIKit.Box2(r, "Bar", new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(330, 10)); UIKit.Card(bar, new Color(0, 0, 0, .65f));
            var fill = UIKit.FillBar(bar, color, 2);
            UIKit.Label(r, label.ToUpperInvariant(), 13, UIKit.Dim, Vector2.zero, Vector2.one, new Vector2(338, -2), new Vector2(0, 2), TextAlignmentOptions.MidlineLeft).characterSpacing = 3;
            return fill;
        }
        GameObject Chip(string text, string icon, Color color)
        {
            var r = UIKit.Rect(chipRow, text, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); r.sizeDelta = new Vector2(0, 26);
            UIKit.Card(r, new Color(color.r * .22f, color.g * .22f, color.b * .22f, .9f));
            var spr = UIKit.Icon(icon); float pad = 10;
            if (spr != null) { UIKit.Img(r, "Icon", spr, color, new Vector2(0, .5f), new Vector2(15, 0), new Vector2(20, 20)); pad = 28; }
            var t = UIKit.Label(r, text, 16, color, Vector2.zero, Vector2.one, new Vector2(pad, 0), new Vector2(-8, 0), TextAlignmentOptions.MidlineLeft); t.textWrappingMode = TextWrappingModes.NoWrap;
            r.sizeDelta = new Vector2(pad + 12 + text.Length * 8.6f, 26);
            r.gameObject.SetActive(false); return r.gameObject;
        }
        TextMeshProUGUI StripCell(RectTransform parent, string icon, Color color, float width, string name)
        {
            var r = UIKit.Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); r.sizeDelta = new Vector2(width, 48);
            var spr = UIKit.Icon(icon);
            if (spr != null) UIKit.Img(r, "Icon", spr, color, new Vector2(0, .5f), new Vector2(14, 0), new Vector2(24, 24));
            else UIKit.Img(r, "Dot", UIKit.Circle, color, new Vector2(0, .5f), new Vector2(10, 0), new Vector2(10, 10));
            UIKit.Label(r, name.ToUpperInvariant(), 11, UIKit.Dim, new Vector2(0, .5f), new Vector2(1, 1), new Vector2(spr != null ? 30 : 20, -2), new Vector2(0, -4)).characterSpacing = 2;
            var t = UIKit.Label(r, "", 20, UIKit.Ink, new Vector2(0, 0), new Vector2(1, .5f), new Vector2(spr != null ? 30 : 20, 2), new Vector2(0, 4), TextAlignmentOptions.MidlineLeft); t.fontStyle = FontStyles.Bold; t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        /// <summary>Queues a centred banner (airdrops, red zones, sieges, missions, member switched or died).</summary>
        public void Banner(string title, string sub, Color color, float seconds = 4)
        {
            if (title == lastBannerTitle && Time.unscaledTime - lastBannerAt < 3) return;
            lastBannerTitle = title; lastBannerAt = Time.unscaledTime;
            if (banners.Count > 6) banners.Dequeue();
            banners.Enqueue(new BannerMsg { title = title, sub = sub, color = color, seconds = seconds });
        }
        /// <summary>Adds a PUBG-style kill-feed line (newest at the top, fading after a few seconds).</summary>
        public void KillFeed(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = FeedLines - 1; i > 0; i--) { feed[i] = feed[i - 1]; feedTimes[i] = feedTimes[i - 1]; }
            feed[0] = text; feedTimes[0] = Time.unscaledTime; feedCount = Mathf.Min(FeedLines, feedCount + 1); feedDirty = true;
        }

        // ---------------------------------------------------------------- frame
        float hudClock, miniFogClock; int cHealth = -1, cMax = -1, cPlague = -1, cArmor = -1, cMinute = -1, cAlive = -1, cHearts = -1, cMorale = -1, cInfluence = -1, cMate = -2, cMateOrder = -1, cPlayer = -1, cHotbar = int.MinValue;
        readonly int[] cStock = { -1, -1, -1, -1, -1 }, cCap = { -1, -1, -1, -1, -1 };
        string cPromptLabel, cPromptAction, cWeapon = ""; int cPromptCount = -1;

        /// <summary>Forgets every cached HUD value so the next tick redraws (new session or another member took control).</summary>
        void ResetHud()
        {
            cHealth = cMax = cPlague = cArmor = cMinute = cAlive = cHearts = cMorale = cInfluence = cMateOrder = cPlayer = -1; cMate = -2; cHotbar = int.MinValue;
            for (int i = 0; i < 5; i++) cStock[i] = cCap[i] = -1;
            cPromptLabel = cPromptAction = null; cPromptCount = -1; cWeapon = ""; logDirty = feedDirty = true; plateClock = 0;
            if (zoneCard != null) zoneCard.gameObject.SetActive(false);
        }

        void TickHud()
        {
            var w = game.World; var s = game.Player; if (s == null) return;
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            hudClock -= dt; bool slow = hudClock <= 0; if (slow) hudClock = .2f;
            TickCard(w, s);
            TickBanners(now);
            TickFeedAndLog(now);
            TickMinimap(w, s, dt);
            TickPlates(w, s, dt);
            var a = s.action; bool acting = a != null && a.kind != ActionKind.Sleep;
            if (actionBar.gameObject.activeSelf != acting) actionBar.gameObject.SetActive(acting);
            if (acting) { actionFill.fillAmount = a.Progress; }
            if (game.ThrowAiming)
            {
                Vector2 local; var sp = game.Cam.WorldToScreenPoint(game.ThrowAimPoint);
                if (!reticle.gameObject.activeSelf) reticle.gameObject.SetActive(true);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(hud, sp, null, out local)) reticle.anchoredPosition = local + hud.rect.size * .5f;
            }
            else if (reticle.gameObject.activeSelf) reticle.gameObject.SetActive(false);
            // Waiting for the auto-switch after the controlled member died.
            bool down = !s.alive && !game.CommunityGone;
            if (deathNotice.gameObject.activeSelf != down) { deathNotice.gameObject.SetActive(down); if (down) deathText.text = "<b>" + s.name + " is dead.</b>\n<size=20><color=#ccc>" + (string.IsNullOrEmpty(s.causeOfDeath) ? "" : s.causeOfDeath + " ") + "Control passes to another member of your community.</color></size>"; }
            if (!slow) return;
            if (acting) actionText.text = ActionName(a);
            TickStrip(w);
            TickClock(w, s);
            RefreshHotbar();
            TickPrompt(s, a, acting);
            TickWeapon(w, s);
            tipText.text = game.Prefs.showTips ? Tip(s, w) : "";
        }

        void TickCard(World w, Survivor s)
        {
            if (cPlayer != s.id) { cPlayer = s.id; cardName.text = s.name; cardSub.text = Profession.Get(s.profession).name + (s.traits.Count > 0 ? " · " + UiData.TraitName(s.traits[0]) + (s.traits.Count > 1 ? " +" + (s.traits.Count - 1) : "") : ""); }
            float max = s.MaxHealth;
            healthFill.fillAmount = Mathf.Clamp01(s.health / 100);
            // The injury-capped segment: from the current maximum to 100.
            float capFrac = Mathf.Clamp01(1 - max / 100); var ir = injuryFill.rectTransform; ir.anchorMin = new Vector2(1 - capFrac, 0); if (injuryFill.enabled != capFrac > .005f) injuryFill.enabled = capFrac > .005f;
            int hp = Mathf.CeilToInt(Mathf.Max(0, s.health)), mx = Mathf.RoundToInt(max);
            if (hp != cHealth || mx != cMax) { cHealth = hp; cMax = mx; if (mx < 100) healthText.SetText("{0} / {1}", (float)hp, (float)mx); else healthText.SetText("{0}", hp); }
            staminaFill.fillAmount = Mathf.Clamp01(s.endurance / 100);
            float sCap = Mathf.Clamp01(1 - UiCommunityApi.StaminaCap(s) / 100); staminaCap.rectTransform.anchorMin = new Vector2(1 - sCap, 0); if (staminaCap.enabled != sCap > .005f) staminaCap.enabled = sCap > .005f;
            fatigueFill.fillAmount = Mathf.Clamp01(1 - s.fatigue / 100);
            bool infected = s.plagueInfected || s.plague > 0;
            if (plagueRow.gameObject.activeSelf != infected) { plagueRow.gameObject.SetActive(infected); card.sizeDelta = new Vector2(450, infected ? 198 : 172); mateCard.anchoredPosition = new Vector2(24, infected ? -226 : -200); }
            if (infected) { plagueFill.fillAmount = Mathf.Clamp01(s.plague / 100); int pl = Mathf.RoundToInt(s.plague); if (pl != cPlague) { cPlague = pl; plagueText.SetText("BLOOD PLAGUE {0}%", pl); } }
            Toggle(chipBleed, s.Bleeding); Toggle(chipHeavy, s.Encumbrance > 1); Toggle(chipTired, s.fatigue > 75); Toggle(chipSneak, s.sneaking);
            int armor = Mathf.RoundToInt(Armor(s) * 100); Toggle(chipArmor, armor > 0);
            if (armor != cArmor) { cArmor = armor; armorText.SetText("Armour {0}%", armor); }
            // Companion mini-card.
            int mid = w.community.companion; var mate = mid >= 0 && mid < w.survivors.Count ? w.survivors[mid] : null;
            if (mate != null && (!mate.alive || mate.id == s.id)) mate = null;
            if (cMate != (mate == null ? -1 : mate.id)) { cMate = mate == null ? -1 : mate.id; mateCard.gameObject.SetActive(mate != null); if (mate != null) mateName.text = mate.name; cMateOrder = -1; }
            if (mate != null)
            {
                mateHealth.fillAmount = Mathf.Clamp01(mate.health / 100);
                if ((int)mate.order != cMateOrder) { cMateOrder = (int)mate.order; mateOrder.text = OrderName(mate.order).ToUpperInvariant() + "  <color=#777>[C]</color>"; }
            }
        }
        static void Toggle(GameObject go, bool on) { if (go.activeSelf != on) go.SetActive(on); }
        static float Armor(Survivor s) { float a = 0; foreach (var kv in s.worn) { var it = s.inventory.ByUid(kv.Value); if (it != null) a = Mathf.Max(a, it.Def.armor * it.condition); } return a; }

        void TickStrip(World w)
        {
            var c = w.community;
            for (int i = 0; i < 5; i++)
            {
                var r = UiData.Resources[i];
                int v = Mathf.FloorToInt(c.Stock(r)), cap = Mathf.RoundToInt(UiData.Capacity(w, r));
                if (v == cStock[i] && cap == cCap[i]) continue;
                cStock[i] = v; cCap[i] = cap;
                stockText[i].SetText("{0}<size=14><color=#888>/{1}</color></size>", (float)v, (float)cap);
                stockText[i].color = v <= 0 ? UIKit.Bad : cap > 0 && v >= cap ? UIKit.Good : v < Mathf.Max(3, cap * .1f) ? UIKit.Warn : UIKit.Ink;
            }
            int morale = Mathf.RoundToInt(c.morale), infl = Mathf.FloorToInt(c.influence);
            if (morale != cMorale) { cMorale = morale; moraleText.SetText("{0}", morale); moraleText.color = morale < 30 ? UIKit.Bad : morale < 50 ? UIKit.Warn : UIKit.Good; }
            if (infl != cInfluence) { cInfluence = infl; influenceText.SetText("{0}", infl); }
        }

        void TickClock(World w, Survivor s)
        {
            int minute = (int)w.minutes;
            if (minute != cMinute)
            {
                cMinute = minute; clock.text = w.Clock;
                weather.text = (w.raining ? "Rain" : w.fog > .2f ? "Fog" : w.Daylight > .5f ? "Clear" : "Night") + (s.sleeping ? " · <color=#9fd3ff>Resting (E to wake)</color>" : "") + (game.Sim.AtBase(s) ? " · <color=#8fb8ff>At base</color>" : "");
            }
            int alive = w.SurvivorsAlive, hearts = w.HeartsRemaining;
            if (alive != cAlive || hearts != cHearts) { cAlive = alive; cHearts = hearts; counters.SetText("<color=#999>SURVIVORS ALIVE</color> <b>{0}</b>    <color=#999>PLAGUE HEARTS</color> <b><color=#c48cff>{1}</color></b>", (float)alive, (float)hearts); }
        }

        void TickPrompt(Survivor s, TimedAction a, bool acting)
        {
            var f = game.Focus;
            string label, action = null; int count = 0;
            if (s.sleeping) label = "Resting...";
            else if (acting) label = "";
            else if (f != null) { label = f.label; count = f.actions.Count; foreach (var x in f.actions) if (x.enabled) { action = x.label; break; } }
            else label = null;
            if (label == cPromptLabel && action == cPromptAction && count == cPromptCount) return;
            cPromptLabel = label; cPromptAction = action; cPromptCount = count;
            if (s.sleeping) prompt.text = "Resting...   <color=#f0b050>[E]</color> wake up";
            else if (acting) prompt.text = "<color=#f0b050>[E]</color> cancel";
            else if (f != null) prompt.text = "<b>" + f.label + "</b>" + (action != null ? "   <color=#f0b050>[E]</color> " + action : "") + (count > 1 ? "   <color=#f0b050>[Q]</color> more" : "");
            else prompt.text = "";
        }

        void TickWeapon(World w, Survivor s)
        {
            var p = s.Primary; var sec = s.Secondary; var car = w.VehicleOf(s);
            string t;
            if (car != null) t = "<b>" + car.model + "</b>  " + Mathf.RoundToInt(Mathf.Abs(car.speed) * 3.6f) + " km/h" + (car.engineOn ? "" : "  <color=#ff9a80>engine off</color>")
                + "\n<size=20>Fuel " + Mathf.RoundToInt(car.fuel / Vehicle.TankLiters * 100) + "% · Condition " + Mathf.RoundToInt(car.condition * 100) + "%</size>\n<size=18><color=#999>W/S drive · A/D steer · Space brake · R engine</color></size>";
            else
            {
                t = p == null ? "<b>Bare hands</b>" : "<b><color=" + UIKit.TierHex(p.Def.tier) + ">" + p.Label + "</color></b>";
                if (p != null && p.Def.category == ItemCategory.Firearm && !string.IsNullOrEmpty(p.Def.ammo)) t += "\n<size=20>" + s.inventory.Count(p.Def.ammo) + " spare " + ItemCatalog.Get(p.Def.ammo).name + "</size>";
                if (sec != null) t += "\n<size=20>Off hand: " + sec.Label + (s.lightOn ? " (on)" : "") + "</size>";
                var thr = game.Throwable(); if (thr != null) t += "\n<size=20><color=#f0b050>[G]</color> " + thr.Def.name + (s.inventory.Count(thr.def) > 1 ? " ×" + s.inventory.Count(thr.def) : "") + "</size>";
                t += "\n<size=18><color=#999>Load " + s.CarriedWeight.ToString("0.0") + " / " + s.Capacity.ToString("0") + "</color></size>";
            }
            if (t != cWeapon) { cWeapon = t; weaponText.text = t; }
        }

        void TickBanners(float now)
        {
            if (now >= bannerUntil && banners.Count > 0)
            {
                var b = banners.Dequeue();
                bannerTitle.text = b.title; bannerTitle.color = b.color; bannerSub.text = b.sub ?? ""; bannerRule.color = b.color;
                bannerShown = now; bannerUntil = now + b.seconds;
                game.Audio.Ui("open");
            }
            float a = now < bannerUntil ? Mathf.Min(Mathf.Clamp01((now - bannerShown) / .2f), Mathf.Clamp01((bannerUntil - now) / .5f)) : 0;
            if (Mathf.Abs(bannerGroup.alpha - a) > .001f) bannerGroup.alpha = a;
        }

        void TickFeedAndLog(float now)
        {
            for (int i = 0; i < FeedLines; i++)
            {
                float age = i < feedCount ? now - feedTimes[i] : 99;
                float alpha = age < 6 ? 1 : age < 9 ? 1 - (age - 6) / 3 : 0;
                if (feedDirty) feedLabels[i].text = i < feedCount ? feed[i] : "";
                if (Mathf.Abs(feedLabels[i].alpha - alpha) > .01f) feedLabels[i].alpha = alpha;
            }
            feedDirty = false;
            int offset = LogLines - log.Count;
            for (int i = 0; i < LogLines; i++)
            {
                int k = i - offset; float age = k >= 0 ? now - logTimes[k] : 99;
                if (logDirty) logLabels[i].text = k >= 0 ? log[k] : "";
                float alpha = age < 9 ? 1 : age < 12 ? 1 - (age - 9) / 3 : 0;
                if (Mathf.Abs(logLabels[i].alpha - alpha) > .01f) logLabels[i].alpha = alpha;
            }
            logDirty = false;
        }

        // ---------------------------------------------------------------- red zone banner and minimap
        int cZoneSecond = int.MinValue; bool cZoneInside;
        void TickMinimap(World w, Survivor s, float dt)
        {
            if (countyMap == null) return;
            countyMap.Poll();
            if (miniMap.texture != countyMap.texture) { miniMap.texture = countyMap.texture; miniFog.texture = countyMap.fog; miniFogClock = 0; }
            miniFogClock -= dt; if (miniFogClock <= 0) { miniFogClock = 3; countyMap.RefreshFog(w.map); }
            float W = countyMap.mapWidth, H = countyMap.mapHeight, px = s.pos.x, py = s.pos.y;
            var uv = new UnityEngine.Rect((px - MiniRadius) / W, (py - MiniRadius) / H, MiniRadius * 2 / W, MiniRadius * 2 / H);
            miniMap.uvRect = uv; miniFog.uvRect = uv;
            miniPlayer.rectTransform.localEulerAngles = new Vector3(0, 0, s.facing * Mathf.Rad2Deg - 90);
            miniUsed = 0;
            var c = w.community;
            if (c.baseBuilding >= 0) MiniMarker(c.basePos, px, py, UIKit.IconOr("ICON_Map_Home", UIKit.Rounded), UIKit.Blue, 22, true);
            foreach (var o in c.outposts) MiniMarker(o.pos, px, py, UIKit.IconOr("ICON_Map_Home", UIKit.Rounded), new Color(.6f, .8f, 1f), 15, false);
            foreach (var m in c.missions) if (m.status == MissionStatus.Active && Tracked(m.id) && (m.target.x != 0 || m.target.y != 0)) MiniMarker(m.target, px, py, UIKit.IconOr("ICON_Map_Quest", UIKit.Circle), UIKit.Accent, 18, true);
            foreach (var d in w.airdrops) if (!d.opened) MiniMarker(d.pos, px, py, UIKit.IconOr("ICON_Map_Ammo", UIKit.Circle), d.landed ? UIKit.Bad : new Color(1, .4f, .35f, .55f), d.landed ? 18 : 14, true);
            foreach (var h in w.hearts) if (h.discovered && !h.destroyed) MiniMarker(h.pos, px, py, UIKit.IconOr("ICON_Map_Biohazard", UIKit.Circle), UIKit.Plague, 20, true);
            foreach (var f in w.factions) if (f.id != 0 && f.role == AiRole.Enclave && f.homeBuilding >= 0) MiniMarker(UiData.BuildingCenter(w.map, f.homeBuilding), px, py, UIKit.Circle, f.Hostile ? UIKit.Bad : UIKit.Good, 12, false);
            int mid = c.companion; if (mid >= 0 && mid < w.survivors.Count && w.survivors[mid].alive && mid != s.id) MiniMarker(w.survivors[mid].pos, px, py, UIKit.Circle, UIKit.Blue, 10, false);
            for (int i = miniUsed; i < miniPool.Count; i++) if (miniPool[i].enabled) miniPool[i].enabled = false;
            // Red zone circle on the minimap plus the warning card with countdown, distance and direction.
            var z = w.redZone; double minutes = w.minutes;
            bool show = z != null && w.settings.redZones && (z.Warning(minutes) || z.Active(minutes));
            float scale = MiniSize / (MiniRadius * 2);
            miniZoneFill.enabled = miniZoneRing.enabled = show;
            if (show)
            {
                var pos = new Vector2((z.center.x - px) * scale, (z.center.y - py) * scale); float size = z.radius * 2 * scale;
                miniZoneFill.rectTransform.anchoredPosition = miniZoneRing.rectTransform.anchoredPosition = pos;
                miniZoneFill.rectTransform.sizeDelta = miniZoneRing.rectTransform.sizeDelta = new Vector2(size, size);
            }
            if (zoneCard.gameObject.activeSelf != show) { zoneCard.gameObject.SetActive(show); cZoneSecond = int.MinValue; }
            if (show)
            {
                bool active = z.Active(minutes);
                float dist = Vec2.Distance(s.pos, z.center); bool inside = dist < z.radius;
                double secondsLeft = (active ? z.endsAt - minutes : z.startsAt - minutes) * 60 / w.TimeScale;   // in-game minutes to real seconds
                int sec = Mathf.Max(0, (int)secondsLeft);
                if (sec != cZoneSecond || inside != cZoneInside)
                {
                    cZoneSecond = sec; cZoneInside = inside;
                    string time = (sec / 60) + ":" + (sec % 60).ToString("00");
                    var away = new Vector2(s.pos.x - z.center.x, s.pos.y - z.center.y);
                    string where = inside ? "<color=#ffb0a0>YOU ARE INSIDE</color> · edge " + Mathf.RoundToInt(z.radius - dist) + " m, head " + UiCompass.Bearing(away)
                        : Mathf.RoundToInt(dist - z.radius) + " m to the edge · " + UiCompass.Bearing(-away);
                    zoneText.text = "<b>" + (active ? "ARTILLERY STRIKE" : "RED ZONE WARNING") + "</b>  " + (active ? "ends in " : "shelling in ") + time + "   " + where;
                    zoneCard.GetComponent<Image>().color = inside ? new Color(.42f, .04f, .04f, .9f) : new Color(.22f, .03f, .03f, .86f);
                }
            }
        }
        void MiniMarker(Vec2 at, float px, float py, Sprite sprite, Color color, float size, bool clamp)
        {
            float scale = MiniSize / (MiniRadius * 2);
            var p = new Vector2((at.x - px) * scale, (at.y - py) * scale); float edge = MiniSize * .5f - size * .5f;
            bool outside = Mathf.Abs(p.x) > edge || Mathf.Abs(p.y) > edge;
            if (outside && !clamp) return;
            if (outside) { float k = edge / Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)); p *= k; color.a *= .7f; size *= .8f; }
            Image img;
            if (miniUsed < miniPool.Count) img = miniPool[miniUsed];
            else { img = UIKit.Img(miniMarkers, "Marker", sprite, color, new Vector2(.5f, .5f), Vector2.zero, new Vector2(size, size)); miniPool.Add(img); }
            miniUsed++;
            if (!img.enabled) img.enabled = true;
            if (img.sprite != sprite) img.sprite = sprite;
            img.color = color;
            var r = img.rectTransform; r.anchoredPosition = p; if (r.sizeDelta.x != size) r.sizeDelta = new Vector2(size, size);
        }

        // ---------------------------------------------------------------- hotbar and tips
        void RefreshHotbar()
        {
            var s = game.Player;
            // Cheap signature first: the hotbar list is rebuilt only when the inventory or equipment changed.
            int sig = s.primary * 7919 + s.secondary * 104729 + s.inventory.items.Count;
            foreach (var i in s.inventory.items) sig = sig * 31 + i.uid + i.count;
            if (sig == cHotbar) return; cHotbar = sig;
            var bar = game.Hotbar();
            for (int i = 0; i < 6; i++)
            {
                var slot = hotbar.GetChild(i); var img = slot.Find("Icon").GetComponent<Image>(); var texts = slot.GetComponentsInChildren<TextMeshProUGUI>();
                var bg = slot.GetComponent<Image>();
                if (i < bar.Count)
                {
                    var item = bar[i]; var spr = UIKit.Icon(UIKit.ItemIcon(item.Def));
                    img.enabled = spr != null; img.sprite = spr; texts[0].text = item.Def.name; texts[0].color = UIKit.TierColor(item.Def.tier);
                    bool eq = s.primary == item.uid || s.secondary == item.uid; bg.color = eq ? new Color(.55f, .33f, .12f, .85f) : new Color(0, 0, 0, .5f);
                }
                else { img.enabled = false; texts[0].text = ""; bg.color = new Color(0, 0, 0, .3f); }
            }
        }

        string Tip(Survivor s, World w)
        {
            if (w.HoursSurvived < .3) return "Search buildings with <color=#f0b050>E</color>. <color=#f0b050>Tab</color> inventory · <color=#f0b050>K</color> community · <color=#f0b050>J</color> missions · <color=#f0b050>M</color> map.";
            if (s.Primary == null) return "Find a weapon. A bat or a kitchen knife beats bare hands. <color=#f0b050>Space</color> shoves zombies back.";
            if (s.Bleeding) return "You are bleeding! Use a bandage from your inventory (<color=#f0b050>Tab</color>).";
            foreach (var i in s.inventory.items) if (UiData.IsRucksack(i.Def)) return "Carry the " + i.Def.name.ToLowerInvariant() + " home and deposit it at base for the whole community.";
            if (s.fatigue > 75) return "Exhausted. Rest at a base bed, or switch to a fresher member at base (<color=#f0b050>K</color>).";
            if (w.Daylight < .3f && !s.lightOn) return "It's dark. A flashlight helps you see, but it also makes you easier to spot.";
            return "";
        }

        static string ActionName(TimedAction a)
        {
            switch (a.kind)
            {
                case ActionKind.Craft: return "Crafting";
                case ActionKind.Barricade: return "Barricading"; case ActionKind.ClearGlass: return "Clearing glass";
                case ActionKind.Hotwire: return "Hotwiring"; case ActionKind.Refuel: return "Refuelling"; case ActionKind.Loot: return "Searching";
                case ActionKind.Bandage: return "Bandaging"; case ActionKind.Reload: return "Reloading"; case ActionKind.Rest: return "Resting";
                case ActionKind.Eat: return "Eating"; case ActionKind.Drink: return "Drinking"; case ActionKind.Pry: return "Prying";
                default: return a.kind.ToString();
            }
        }
    }
}
