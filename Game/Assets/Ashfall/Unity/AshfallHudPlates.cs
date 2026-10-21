using System.Collections.Generic;
using Ashfall.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashfall.View
{
    /// <summary>Screen-space threat nameplates over visible freaks (name, 1–5 skulls, health once damaged), nearby humans
    /// (name or title, coloured by stance) and plague hearts. Targets are re-picked ten times a second; positions every frame.</summary>
    public sealed partial class AshfallUI
    {
        sealed class Plate
        {
            public RectTransform r; public CanvasGroup group; public TextMeshProUGUI name, sub; public readonly Image[] skulls = new Image[5];
            public RectTransform hp; public Image hpFill; public long key = long.MinValue;
        }
        struct PlateTarget { public byte kind; public int id; public float dist; }   // kind 0 zombie, 1 survivor, 2 heart
        const int PlateCount = 24;
        const float ZombieRange = 30, HumanRange = 34, HeartRange = 44;
        readonly Plate[] plates = new Plate[PlateCount];
        readonly PlateTarget[] targets = new PlateTarget[PlateCount]; int targetCount; float plateClock;

        void BuildPlates()
        {
            for (int i = 0; i < PlateCount; i++)
            {
                var p = new Plate();
                p.r = UIKit.Box2(plateLayer, "Plate " + i, new Vector2(.5f, .5f), new Vector2(.5f, 0), Vector2.zero, new Vector2(260, 60));
                p.group = p.r.gameObject.AddComponent<CanvasGroup>(); p.group.blocksRaycasts = false; p.group.interactable = false;
                p.name = UIKit.Label(p.r, "", 19, UIKit.Ink, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 22), new Vector2(0, 46), TextAlignmentOptions.Bottom);
                p.name.fontStyle = FontStyles.Bold; p.name.textWrappingMode = TextWrappingModes.NoWrap; p.name.overflowMode = TextOverflowModes.Overflow; p.name.outlineWidth = .22f; p.name.outlineColor = new Color32(0, 0, 0, 230);
                p.sub = UIKit.Label(p.r, "", 14, UIKit.Dim, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 44), new Vector2(0, 62), TextAlignmentOptions.Bottom);
                p.sub.textWrappingMode = TextWrappingModes.NoWrap; p.sub.overflowMode = TextOverflowModes.Overflow; p.sub.outlineWidth = .2f; p.sub.outlineColor = new Color32(0, 0, 0, 220);
                for (int k = 0; k < 5; k++) { p.skulls[k] = UIKit.Img(p.r, "Skull", UIKit.Skull, UIKit.Ink, new Vector2(.5f, 0), new Vector2((k - 2) * 15, 14), new Vector2(12, 12)); p.skulls[k].preserveAspect = false; }
                p.hp = UIKit.Box2(p.r, "Health", new Vector2(.5f, 0), new Vector2(.5f, .5f), new Vector2(0, 3), new Vector2(110, 6)); UIKit.Card(p.hp, new Color(0, 0, 0, .7f));
                p.hpFill = UIKit.FillBar(p.hp, UIKit.Bad, 1);
                p.r.gameObject.SetActive(false);
                plates[i] = p;
            }
        }

        void TickPlates(World w, Survivor s, float dt)
        {
            plateClock -= dt;
            if (plateClock <= 0) { plateClock = .1f; PickPlateTargets(w, s); }
            var cam = game.Cam;
            for (int i = 0; i < PlateCount; i++)
            {
                var p = plates[i];
                if (i >= targetCount) { if (p.r.gameObject.activeSelf) { p.r.gameObject.SetActive(false); p.key = long.MinValue; } continue; }
                var t = targets[i];
                Vector3 world; float hpFrac; bool show = PlateState(w, t, p, out world, out hpFrac);
                Vector3 sp = show ? cam.WorldToScreenPoint(world) : Vector3.zero;
                Vector2 local;
                if (!show || sp.z <= 0 || !RectTransformUtility.ScreenPointToLocalPointInRectangle(plateLayer, sp, null, out local)) { if (p.r.gameObject.activeSelf) p.r.gameObject.SetActive(false); continue; }
                if (!p.r.gameObject.activeSelf) p.r.gameObject.SetActive(true);
                p.r.anchoredPosition = local;
                float range = t.kind == 0 ? ZombieRange : t.kind == 1 ? HumanRange : HeartRange;
                float a = Mathf.Clamp01((range - t.dist) / 6f); if (Mathf.Abs(p.group.alpha - a) > .02f) p.group.alpha = a;
                bool damaged = hpFrac < .995f;
                if (p.hp.gameObject.activeSelf != damaged) p.hp.gameObject.SetActive(damaged);
                if (damaged) p.hpFill.fillAmount = Mathf.Clamp01(hpFrac);
            }
        }

        /// <summary>World anchor and health fraction for a plate target; writes the labels when the target changes.</summary>
        bool PlateState(World w, PlateTarget t, Plate p, out Vector3 world, out float hpFrac)
        {
            world = Vector3.zero; hpFrac = 1;
            long key = ((long)t.kind << 32) | (uint)t.id;
            if (t.kind == 0)
            {
                if (t.id < 0 || t.id >= w.zombies.Count) return false;
                var z = w.zombies[t.id]; if (!z.Alive) return false;
                var def = Bestiary.Get(z.kind);
                world = AshfallGame.Pos(z.pos, 2.1f * Mathf.Max(1, def.scale));
                hpFrac = z.maxHp > 0 ? z.hp / z.maxHp : 1;
                if (p.key != key) { p.key = key; SetPlate(p, (z.plague ? "Plague " : "") + def.name, UiData.ThreatColor(def.threat), "", def.threat, UiData.ThreatColor(def.threat)); }
                return true;
            }
            if (t.kind == 1)
            {
                if (t.id < 0 || t.id >= w.survivors.Count) return false;
                var o = w.survivors[t.id]; if (!o.alive || o.inVehicle) return false;
                world = AshfallGame.Pos(o.pos, 2.25f);
                hpFrac = o.health / Mathf.Max(1, o.MaxHealth);
                // Stance can change (standing drops, a recruit joins): refresh the colour every pick, the text when the target changes.
                var c = UiData.Stance(w, game.Player, o);
                if (p.key != key) { p.key = key; SetPlate(p, UiData.DisplayName(o), c, UiData.Subtitle(w, o) + " · " + UiData.StanceName(w, game.Player, o), 0, c); }
                else if (p.name.color != c) p.name.color = c;
                return true;
            }
            if (t.id < 0 || t.id >= w.hearts.Count) return false;
            var h = w.hearts[t.id]; if (h.destroyed) return false;
            world = AshfallGame.Pos(h.pos, 2.6f);
            hpFrac = h.maxHp > 0 ? h.hp / h.maxHp : 1;
            if (p.key != key) { p.key = key; SetPlate(p, "Plague heart", UIKit.Plague, "Destroy it to cleanse the county", Bestiary.HeartThreat, UIKit.Plague); }
            return true;
        }

        static void SetPlate(Plate p, string name, Color color, string sub, int skulls, Color skullColor)
        {
            p.name.text = name; p.name.color = color; p.sub.text = sub;
            for (int k = 0; k < 5; k++) { bool on = k < skulls; if (p.skulls[k].enabled != on) p.skulls[k].enabled = on; if (on) { p.skulls[k].color = skullColor; p.skulls[k].rectTransform.anchoredPosition = new Vector2((k - (skulls - 1) * .5f) * 15, 14); } }
            p.sub.rectTransform.offsetMin = new Vector2(0, skulls > 0 ? 44 : 22); p.sub.rectTransform.offsetMax = new Vector2(0, skulls > 0 ? 62 : 40);
            p.name.rectTransform.offsetMin = new Vector2(0, skulls > 0 ? 22 : 6); p.name.rectTransform.offsetMax = new Vector2(0, skulls > 0 ? 46 : 28);
        }

        /// <summary>The nearest visible freaks, humans in line of sight and hearts, up to the plate pool size.</summary>
        void PickPlateTargets(World w, Survivor s)
        {
            targetCount = 0;
            HashSet<int> seen;
            if (game.Sim.visible.TryGetValue(s.id, out seen))
                foreach (int id in seen)
                {
                    if (id < 0 || id >= w.zombies.Count) continue;
                    var z = w.zombies[id]; if (!z.Alive) continue;
                    if (Bestiary.Get(z.kind).threat < 2) continue;   // walkers and crawlers stay unlabelled
                    float d = Vec2.Distance(z.pos, s.pos); if (d < ZombieRange) AddTarget(0, id, d);
                }
            foreach (var o in w.survivors)
            {
                if (o == s || !o.alive || o.offscreen) continue;
                float d = Vec2.Distance(o.pos, s.pos); if (d > HumanRange) continue;
                if (!w.map.LineOfSight(s.pos, o.pos)) continue;
                AddTarget(1, o.id, d);
            }
            for (int i = 0; i < w.hearts.Count; i++)
            {
                var h = w.hearts[i]; if (h.destroyed) continue;
                float d = Vec2.Distance(h.pos, s.pos); if (d > HeartRange) continue;
                if (!h.discovered && !w.map.LineOfSight(s.pos, h.pos)) continue;
                AddTarget(2, i, d);
            }
        }
        void AddTarget(byte kind, int id, float dist)
        {
            // Keep the nearest PlateCount targets, sorted by distance (insertion into a fixed array: no allocation).
            int at = targetCount;
            while (at > 0 && targets[at - 1].dist > dist) at--;
            if (at >= PlateCount) return;
            int last = Mathf.Min(targetCount, PlateCount - 1);
            for (int k = last; k > at; k--) targets[k] = targets[k - 1];
            targets[at] = new PlateTarget { kind = kind, id = id, dist = dist };
            if (targetCount < PlateCount) targetCount++;   // a slot whose target changed rewrites its labels in PlateState (key check)
        }
    }
}
