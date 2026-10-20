using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashfall.View
{
    /// <summary>New Game trait picker. The selection is only a request: the host validates it again in World.Create.</summary>
    public sealed partial class AshfallUI
    {
        RectTransform traitsPanel; TextMeshProUGUI traitPoints, traitWarning, traitSummary;
        readonly List<string> chosenTraits = new List<string>();
        readonly Dictionary<string, Button> traitButtons = new Dictionary<string, Button>();

        // Native QA probe hooks: open the picker and press trait buttons through their real click handlers.
        public bool TraitsOpen { get { return traitsPanel != null && traitsPanel.gameObject.activeSelf; } }
        public List<string> ChosenTraits { get { return new List<string>(chosenTraits); } }
        public string TraitWarning { get { return traitWarning == null ? "" : traitWarning.text; } }
        public void QaOpenTraits() { var b = newGame.GetComponentsInChildren<Button>(true).FirstOrDefault(x => x.name == "Button Choose traits"); if (b != null) b.onClick.Invoke(); }
        public void QaClickTrait(string id) { Button b; if (traitButtons.TryGetValue(id, out b)) b.onClick.Invoke(); }
        public void QaClearTraits() { chosenTraits.Clear(); RefreshTraits(); }

        void BuildTraits()
        {
            foreach (var id in (game.Prefs.lastTraits ?? "").Split(',')) if (Trait.Get(id) != null && !chosenTraits.Contains(id)) chosenTraits.Add(id);
            if (Trait.Validate(chosenTraits) != "") chosenTraits.Clear();

            // Opaque: the New Game screen stays active underneath and must not show through.
            traitsPanel = UIKit.Fill(root, "Traits"); UIKit.Background(traitsPanel, new Color(.035f, .037f, .04f, 1));
            UIKit.Label(traitsPanel, "TRAITS", 56, UIKit.Rust, new Vector2(0, 1), new Vector2(1, 1), new Vector2(90, -120), new Vector2(0, -40)).fontStyle = FontStyles.Bold;
            UIKit.Label(traitsPanel, "Positive traits cost points. Negative traits give points back. You start with " + Trait.Budget + ".", 24, UIKit.Dim, new Vector2(0, 1), new Vector2(1, 1), new Vector2(92, -160), new Vector2(-600, -118));
            traitPoints = UIKit.Label(traitsPanel, "", 34, UIKit.Accent, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-560, -150), new Vector2(-90, -80), TextAlignmentOptions.Right);
            var pos = UIKit.Rect(traitsPanel, "Positive", new Vector2(0, 0), new Vector2(.5f, 1), new Vector2(90, 150), new Vector2(-15, -180)); UIKit.Column(pos, 8);
            var neg = UIKit.Rect(traitsPanel, "Negative", new Vector2(.5f, 0), new Vector2(1, 1), new Vector2(15, 150), new Vector2(-90, -180)); UIKit.Column(neg, 8);
            UIKit.Text(pos, "Positive", 28, UIKit.Good).gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
            UIKit.Text(neg, "Negative", 28, UIKit.Warn).gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
            foreach (var t in Trait.All)
            {
                var trait = t;
                var b = UIKit.Button(t.Positive ? pos : neg, "", () => ToggleTrait(trait), 800, 66, 22);
                var label = b.GetComponentInChildren<TextMeshProUGUI>(); label.alignment = TextAlignmentOptions.MidlineLeft;
                traitButtons[t.id] = b;
            }
            traitWarning = UIKit.Label(traitsPanel, "", 22, UIKit.Warn, new Vector2(0, 0), new Vector2(1, 0), new Vector2(90, 100), new Vector2(-600, 140));
            var bottom = UIKit.Rect(traitsPanel, "Actions", new Vector2(0, 0), new Vector2(1, 0), new Vector2(90, 30), new Vector2(-90, 100));
            var h = bottom.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 20; h.childAlignment = TextAnchor.MiddleRight; h.childControlWidth = false; h.childControlHeight = false;
            UIKit.Button(bottom, "Clear all", () => { chosenTraits.Clear(); traitWarning.text = ""; RefreshTraits(); }, 240, 62, 26);
            UIKit.Button(bottom, "Done", () => CloseOverlay(), 240, 62, 28);
            traitsPanel.gameObject.SetActive(false);
            RefreshTraits();
        }

        void BuildTraitRow(RectTransform parent)
        {
            var row = UIKit.Row(parent, 54); UIKit.RowLabel(row.transform, "Traits", 260, 24, UIKit.Dim);
            UIKit.Button(row.transform, "Choose traits", () => { traitWarning.text = ""; RefreshTraits(); OpenOverlay(traitsPanel); }, 240, 48, 22);
            traitSummary = UIKit.Text(parent, "", 20, UIKit.Ink); traitSummary.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            RefreshTraitRow();
        }

        void ToggleTrait(Trait t)
        {
            traitWarning.text = "";
            if (chosenTraits.Remove(t.id)) { RefreshTraits(); return; }
            var next = new List<string>(chosenTraits) { t.id };
            string why = Trait.Validate(next);
            if (why.Length > 0) { traitWarning.text = why; if (game.Audio != null) game.Audio.Ui("deny"); return; }
            chosenTraits.Add(t.id); RefreshTraits();
        }

        void RefreshTraits()
        {
            int left = Trait.PointsLeft(chosenTraits);
            traitPoints.text = left + " point" + (left == 1 ? "" : "s") + " left";
            foreach (var t in Trait.All)
            {
                bool on = chosenTraits.Contains(t.id);
                bool blocked = !on && Trait.Validate(new List<string>(chosenTraits) { t.id }) != "";
                string cost = t.cost > 0 ? "costs " + t.cost : "gives " + (-t.cost);
                UIKit.SetLabel(traitButtons[t.id], (on ? "<color=#9fe08a>●</color> " : blocked ? "<color=#555>○</color> " : "<color=#777>○</color> ") + "<b>" + t.name + "</b>  <color=#aaa><size=18>" + cost + "</size></color>\n<size=17><color=" + (blocked ? "#777" : "#c8c4b8") + ">" + t.description + "</color></size>");
                traitButtons[t.id].GetComponent<Image>().color = on ? new Color(1.5f, 1.2f, .8f) : Color.white;
            }
            RefreshTraitRow();
        }

        void RefreshTraitRow()
        {
            if (traitSummary == null) return;
            traitSummary.text = chosenTraits.Count == 0 ? "<color=#999>No traits. " + Trait.Budget + " points to spend.</color>"
                : string.Join(", ", chosenTraits.Select(id => Trait.Get(id).name)) + "  <color=#999>· " + Trait.PointsLeft(chosenTraits) + " points left</color>";
        }
    }
}
