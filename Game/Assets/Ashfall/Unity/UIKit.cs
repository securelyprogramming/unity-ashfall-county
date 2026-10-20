using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Ashfall.View
{
    /// <summary>Small uGUI/TextMeshPro builder. Menus are fully navigable by keyboard, mouse and gamepad.</summary>
    public static class UIKit
    {
        public static readonly Color Ink = new Color(.92f, .9f, .84f), Dim = new Color(.62f, .6f, .56f), Rust = new Color(.78f, .36f, .22f), Panel = new Color(.07f, .075f, .08f, .92f),
            PanelLight = new Color(.13f, .135f, .14f, .95f), Accent = new Color(.86f, .62f, .26f), Good = new Color(.5f, .82f, .48f), Bad = new Color(.92f, .36f, .3f), Warn = new Color(.95f, .76f, .3f);
        static Sprite frame, box, white;
        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        const string Hud = "LicensedFrontier/Portfolio/Zomboid/UI/ApocalypseHUD/";

        public static Sprite White { get { if (white == null) { var t = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; t.SetPixels(px); t.Apply(); white = Sprite.Create(t, new UnityEngine.Rect(0, 0, 4, 4), new Vector2(.5f, .5f)); } return white; } }
        public static Sprite Frame { get { if (frame == null) frame = Icon("SPR_HUD_Frame_Border_Grunge_Med"); return frame; } }
        public static Sprite Box { get { if (box == null) box = Icon("SPR_Apocalypse_Box_Solid01"); return box; } }
        static bool indexed;
        /// <summary>Owned HUD sprites indexed by file name (the pack uses sub-folders); null when not restored.</summary>
        public static Sprite Icon(string name)
        {
            if (!indexed) { indexed = true; foreach (var sprite in Resources.LoadAll<Sprite>(Hud.TrimEnd('/'))) icons[sprite.name] = sprite; }
            Sprite s; return icons.TryGetValue(name, out s) ? s : null;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.offsetMin = offsetMin; r.offsetMax = offsetMax; return r;
        }
        public static RectTransform Fill(Transform parent, string name) { return Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); }
        public static RectTransform Box2(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var r = Rect(parent, name, anchor, anchor, Vector2.zero, Vector2.zero); r.pivot = pivot; r.anchoredPosition = position; r.sizeDelta = size; return r;
        }

        public static Image Background(RectTransform r, Color color, bool framed = false)
        {
            var img = r.gameObject.AddComponent<Image>(); img.color = color; img.sprite = White; img.raycastTarget = true;
            // The owned grunge frame has no slice borders; a thin accent edge reads better than a stretched sprite.
            if (framed)
            {
                var edge = Rect(r, "Edge", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 3)); var ei = edge.gameObject.AddComponent<Image>(); ei.sprite = White; ei.color = new Color(Rust.r, Rust.g, Rust.b, .7f); ei.raycastTarget = false;
            }
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>(); t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style; t.raycastTarget = false; t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }
        public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var t = Text(parent, text, size, color, align); var r = t.rectTransform; r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.offsetMin = offsetMin; r.offsetMax = offsetMax; return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, float width = 320, float height = 54, float fontSize = 26)
        {
            var go = new GameObject("Button " + label, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.sizeDelta = new Vector2(width, height);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height; le.minHeight = height;
            var img = go.AddComponent<Image>(); img.sprite = Box ?? White; img.type = Box != null && Box.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple; img.color = Color.white;
            var b = go.AddComponent<Button>(); b.targetGraphic = img;
            var colors = b.colors; colors.normalColor = new Color(.2f, .21f, .22f, .96f); colors.highlightedColor = new Color(.42f, .26f, .16f, 1); colors.selectedColor = new Color(.52f, .3f, .17f, 1);
            colors.pressedColor = new Color(.7f, .42f, .2f, 1); colors.disabledColor = new Color(.14f, .14f, .15f, .7f); colors.fadeDuration = .06f; b.colors = colors;
            var t = Text(go.transform, label, fontSize, Ink, TextAlignmentOptions.Center); var tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(12, 2); tr.offsetMax = new Vector2(-12, -2);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            if (onClick != null) b.onClick.AddListener(onClick);
            b.onClick.AddListener(() => { if (AshfallGame.Instance != null && AshfallGame.Instance.Audio != null) AshfallGame.Instance.Audio.Ui("click"); });
            return b;
        }
        public static void SetLabel(Button b, string label) { var t = b.GetComponentInChildren<TextMeshProUGUI>(); if (t != null) t.text = label; }

        public static VerticalLayoutGroup Column(RectTransform r, float spacing = 10, TextAnchor align = TextAnchor.UpperLeft, int padding = 0)
        {
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = spacing; v.childAlignment = align; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            v.padding = new RectOffset(padding, padding, padding, padding); return v;
        }
        public static HorizontalLayoutGroup Row(Transform parent, float height, float spacing = 10)
        {
            var go = new GameObject("Row", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var h = go.AddComponent<HorizontalLayoutGroup>(); h.spacing = spacing; h.childControlHeight = true; h.childControlWidth = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true; h.childAlignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>(); le.preferredHeight = height; le.minHeight = height;
            return h;
        }
        public static TextMeshProUGUI RowLabel(Transform row, string text, float width, float size = 24, Color? color = null)
        {
            var t = Text(row, text, size, color ?? Ink, TextAlignmentOptions.MidlineLeft); var le = t.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.flexibleWidth = width <= 0 ? 1 : 0; return t;
        }

        /// <summary>Left/right value picker that works with d-pad and mouse.</summary>
        public sealed class Selector
        {
            public string[] options; public int index; public Action<int> changed; public TextMeshProUGUI value; public Button left, right;
            public void Set(int i) { index = (i % options.Length + options.Length) % options.Length; value.text = options[index]; if (changed != null) changed(index); }
        }
        public static Selector Picker(Transform parent, string label, string[] options, int index, Action<int> changed, float labelWidth = 260, float valueWidth = 300)
        {
            var row = Row(parent, 50, 8); RowLabel(row.transform, label, labelWidth, 24, Dim);
            var s = new Selector { options = options, index = index, changed = changed };
            s.left = Button(row.transform, "<", () => s.Set(s.index - 1), 54, 46, 26);
            s.value = RowLabel(row.transform, options[index], valueWidth, 24); s.value.alignment = TextAlignmentOptions.Center;
            s.right = Button(row.transform, ">", () => s.Set(s.index + 1), 54, 46, 26);
            return s;
        }

        public static TMP_InputField Input(Transform parent, string value, float width, float height = 50)
        {
            var go = new GameObject("Input", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var img = go.AddComponent<Image>(); img.sprite = White; img.color = new Color(.16f, .16f, .17f, 1);
            var area = Rect(go.transform, "Text Area", Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -4)); area.gameObject.AddComponent<RectMask2D>();
            var text = Text(area, value, 24, Ink, TextAlignmentOptions.MidlineLeft); var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var field = go.AddComponent<TMP_InputField>(); field.textViewport = area; field.textComponent = text; field.text = value; field.characterLimit = 28; field.targetGraphic = img;
            return field;
        }

        public static Slider Slider(Transform parent, string label, float value, Action<float> changed, float labelWidth = 260)
        {
            var row = Row(parent, 50, 8); RowLabel(row.transform, label, labelWidth, 24, Dim);
            var go = new GameObject("Slider", typeof(RectTransform)); go.transform.SetParent(row.transform, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 360; le.preferredHeight = 30;
            var bg = Fill(go.transform, "Background"); Background(bg, new Color(.18f, .18f, .19f));
            var fillArea = Rect(go.transform, "Fill Area", Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            var fill = Fill(fillArea, "Fill"); Background(fill, Accent);
            var handleArea = Rect(go.transform, "Handle Area", Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-8, 0));
            var handle = Rect(handleArea, "Handle", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-8, -4), new Vector2(8, 4)); var hi = Background(handle, Ink);
            var s = go.AddComponent<Slider>(); s.fillRect = fill; s.handleRect = handle; s.targetGraphic = hi; s.minValue = 0; s.maxValue = 1; s.value = value;
            s.onValueChanged.AddListener(v => changed(v));
            var readout = RowLabel(row.transform, Mathf.RoundToInt(value * 100) + "%", 90, 22, Dim); s.onValueChanged.AddListener(v => readout.text = Mathf.RoundToInt(v * 100) + "%");
            return s;
        }

        public static RectTransform Scroll(Transform parent, string name, out RectTransform content)
        {
            var root = Fill(parent, name); var img = root.gameObject.AddComponent<Image>(); img.sprite = White; img.color = new Color(0, 0, 0, .25f);
            var viewport = Fill(root, "Viewport"); viewport.gameObject.AddComponent<RectMask2D>(); var vi = viewport.gameObject.AddComponent<Image>(); vi.color = new Color(0, 0, 0, 0.01f);
            content = Rect(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero); content.pivot = new Vector2(.5f, 1);
            var v = Column(content, 4, TextAnchor.UpperLeft, 6);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = root.gameObject.AddComponent<ScrollRect>(); sr.viewport = viewport; sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 40;
            return root;
        }
        public static void Clear(Transform t) { for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject); }

        public static Image Bar(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color, out TextMeshProUGUI label)
        {
            var r = Box2(parent, name, anchor, new Vector2(0, .5f), position, size); Background(r, new Color(0, 0, 0, .6f));
            var fill = Rect(r, "Fill", Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3)); var img = fill.gameObject.AddComponent<Image>(); img.sprite = White; img.color = color;
            img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Horizontal; img.fillAmount = 1;
            label = Label(r, name, 18, Ink, Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-8, 0), TextAlignmentOptions.MidlineLeft);
            return img;
        }

        public static string ItemIcon(ItemDefLite d)
        {
            switch (d.id)
            {
                case "baseball_bat": case "nailed_bat": return "ICON_SM_Wep_Bat_Wood_01_Clean";
                case "pipe": case "crowbar": case "golf_club": return "ICON_SM_Wep_Bat_Metal_01_Clean";
                case "axe": return "ICON_SM_Wep_FireAxe_01_Clean";
                case "hammer": return "ICON_SM_Wep_Hammer_01_Clean";
                case "shovel": return "ICON_SM_Wep_Pickaxe_01_Clean";
                case "pistol": return "ICON_SM_Wep_Pistol_01_Clean";
                case "revolver": return "ICON_SM_Wep_Pistol_Revolver_01_Clean";
                case "shotgun": return "ICON_SM_Wep_Shotgun_01_Farm_Clean";
                case "rifle": return "ICON_SM_Wep_HuntingRifle_01_Clean";
                case "ammo_9mm": case "ammo_38": case "ammo_308": return "ICON_SM_Wep_Pistol_Ammo_Box_01";
                case "shells": return "ICON_SM_Wep_Shotgun_Ammo_Box_01";
                case "water_bottle": return "ICON_SM_Item_Bottle_01";
                case "water_jug": return "ICON_SM_Item_Canteen_01";
                case "soda": case "juice": case "milk": case "coffee": return "ICON_SM_Item_Drink_Bottle_02";
                case "first_aid_kit": return "ICON_SM_Item_HealthKit_01";
                case "painkillers": case "antibiotics": return "ICON_SM_Item_Pills_01";
                case "battery": return "ICON_SM_Item_Battery_01";
                case "gas_can": return "ICON_SM_Prop_GasCan_01";
                case "plank": case "log": case "branch": return "ICON_SM_Prop_Wood_Stack_01";
                case "nails": case "duct_tape": case "screwdriver": case "saw": return "ICON_SM_Prop_Scav_Scrap_13";
                case "boots": return "ICON_SM_Prop_Shoes_01";
                case "potato": case "carrots": case "berries": case "mushrooms": return "ICON_SM_Prop_SeedPacket_01";
                case "steak_cooked": case "chicken_cooked": case "steak": case "chicken": case "stew": return "ICON_SM_Item_Meat_Cooked_01";
            }
            switch (d.category)
            {
                case Ashfall.Core.ItemCategory.Food: return d.id.StartsWith("canned") || d.id == "dog_food" ? "ICON_SM_Item_Can_04" : "ICON_Inventory_Herbs";
                case Ashfall.Core.ItemCategory.Medical: return "ICON_Inventory_Healing";
                case Ashfall.Core.ItemCategory.Weapon: return d.blade ? "ICON_Inventory_Weapon" : "ICON_SM_Wep_Baton_01_Clean";
                case Ashfall.Core.ItemCategory.Clothing: return d.slot == Ashfall.Core.WearSlot.Head ? "ICON_Inventory_Helmets" : "ICON_Inventory_Armor";
                case Ashfall.Core.ItemCategory.Bag: return "ICON_Inventory_Backpack";
                case Ashfall.Core.ItemCategory.Tool: return "ICON_Inventory_Crafting";
                case Ashfall.Core.ItemCategory.Material: return "ICON_Inventory_Minerals";
                default: return "ICON_Inventory_Notes";
            }
        }
        public struct ItemDefLite { public string id; public Ashfall.Core.ItemCategory category; public bool blade; public Ashfall.Core.WearSlot slot; public static implicit operator ItemDefLite(Ashfall.Core.ItemDef d) { return new ItemDefLite { id = d.id, category = d.category, blade = d.blade, slot = d.slot }; } }
    }
}
