using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Ashfall.View
{
    /// <summary>Small uGUI/TextMeshPro builder with the game's dark theme. Menus are fully navigable by keyboard, mouse and gamepad.</summary>
    public static class UIKit
    {
        // Theme: near-black slate panels, warm ink, one amber accent. Status colours are reserved for meaning.
        public static readonly Color Ink = new Color(.93f, .92f, .88f), Dim = new Color(.58f, .6f, .62f), Rust = new Color(.9f, .45f, .2f), Panel = new Color(.045f, .052f, .06f, .95f),
            PanelLight = new Color(.1f, .112f, .126f, .96f), Accent = new Color(.97f, .64f, .24f), Good = new Color(.46f, .86f, .5f), Bad = new Color(.95f, .33f, .3f), Warn = new Color(.98f, .78f, .3f);
        public static readonly Color Blue = new Color(.42f, .68f, 1f), Plague = new Color(.74f, .44f, .98f), Rule = new Color(1, 1, 1, .07f), Shade = new Color(0, 0, 0, .55f),
            Stamina = new Color(.36f, .74f, .92f), Fatigue = new Color(.78f, .7f, .46f), Health = new Color(.86f, .26f, .24f), Injury = new Color(.32f, .08f, .08f, .95f);
        static Sprite frame, box, white, rounded, circle, ring, skull, arrow;
        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        const string Hud = "LicensedFrontier/Portfolio/Zomboid/UI/ApocalypseHUD/";

        public static Sprite White { get { if (white == null) { var t = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; t.SetPixels(px); t.Apply(); white = Sprite.Create(t, new UnityEngine.Rect(0, 0, 4, 4), new Vector2(.5f, .5f)); } return white; } }
        public static Sprite Frame { get { if (frame == null) frame = Icon("SPR_HUD_Frame_Border_Grunge_Med"); return frame; } }
        public static Sprite Box { get { if (box == null) box = Icon("SPR_Apocalypse_Box_Solid01"); return box; } }

        /// <summary>Procedural 9-sliced rounded rectangle used for every panel, card and button.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                const int n = 32; const float r = 9;
                rounded = Sprite.Create(Shape(n, (x, y) => { float dx = Mathf.Max(0, Mathf.Max(r - x, x - (n - 1 - r))), dy = Mathf.Max(0, Mathf.Max(r - y, y - (n - 1 - r))); return Mathf.Clamp01(r + .5f - Mathf.Sqrt(dx * dx + dy * dy)); }),
                    new UnityEngine.Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(11, 11, 11, 11));
                return rounded;
            }
        }
        public static Sprite Circle { get { if (circle == null) circle = Sprite.Create(Shape(64, (x, y) => Mathf.Clamp01(31.5f - Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) + .5f)), new UnityEngine.Rect(0, 0, 64, 64), new Vector2(.5f, .5f)); return circle; } }
        public static Sprite Ring { get { if (ring == null) ring = Sprite.Create(Shape(128, (x, y) => { float d = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f)); return Mathf.Clamp01(1.5f - Mathf.Abs(d - 60)); }), new UnityEngine.Rect(0, 0, 128, 128), new Vector2(.5f, .5f)); return ring; } }
        /// <summary>A pointed marker (player direction on maps), pointing up.</summary>
        public static Sprite Arrow { get { if (arrow == null) arrow = Sprite.Create(Shape(32, (x, y) => { float t = y / 31f; float half = (1 - t) * 14 * (y < 6 ? y / 6f + .2f : 1); return Mathf.Clamp01(half - Mathf.Abs(x - 15.5f) + .5f); }), new UnityEngine.Rect(0, 0, 32, 32), new Vector2(.5f, .5f)); return arrow; } }
        /// <summary>A tiny pixel skull for threat ratings (fonts may not carry a skull glyph).</summary>
        public static Sprite Skull
        {
            get
            {
                if (skull != null) return skull;
                string[] rows = { "..XXXXXX..", ".XXXXXXXX.", "XXXXXXXXXX", "XX..XX..XX", "XX..XX..XX", "XXXXXXXXXX", ".XXXX.XXX.", "..XXXXXX..", "..X.XX.X..", "..XXXXXX.." };
                var t = new Texture2D(10, 10, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                for (int y = 0; y < 10; y++) for (int x = 0; x < 10; x++) t.SetPixel(x, 9 - y, rows[y][x] == 'X' ? Color.white : Color.clear);
                t.Apply(); skull = Sprite.Create(t, new UnityEngine.Rect(0, 0, 10, 10), new Vector2(.5f, .5f)); return skull;
            }
        }
        static Texture2D Shape(int n, Func<float, float, float> alpha)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[x + y * n] = new Color32(255, 255, 255, (byte)(alpha(x, y) * 255));
            t.SetPixels32(px); t.Apply(); return t;
        }

        static bool indexed;
        /// <summary>Owned HUD sprites indexed by file name (the pack uses sub-folders); null when not restored.</summary>
        public static Sprite Icon(string name)
        {
            if (!indexed) { indexed = true; foreach (var sprite in Resources.LoadAll<Sprite>(Hud.TrimEnd('/'))) icons[sprite.name] = sprite; }
            Sprite s; return name != null && icons.TryGetValue(name, out s) ? s : null;
        }
        /// <summary>The owned icon when restored, otherwise the given procedural fallback.</summary>
        public static Sprite IconOr(string name, Sprite fallback) { return Icon(name) ?? fallback; }

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

        /// <summary>A flat background. Framed panels get rounded corners and a thin accent rule along the top.</summary>
        public static Image Background(RectTransform r, Color color, bool framed = false)
        {
            var img = r.gameObject.AddComponent<Image>(); img.color = color; img.sprite = framed ? Rounded : White; img.type = framed ? Image.Type.Sliced : Image.Type.Simple; img.raycastTarget = true;
            if (framed)
            {
                var edge = Rect(r, "Edge", new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -3), new Vector2(-10, 0)); var ei = edge.gameObject.AddComponent<Image>(); ei.sprite = White; ei.color = new Color(Accent.r, Accent.g, Accent.b, .75f); ei.raycastTarget = false;
            }
            return img;
        }
        /// <summary>A rounded card without the accent rule (HUD widgets, list rows).</summary>
        public static Image Card(RectTransform r, Color color, bool raycast = false)
        {
            var img = r.gameObject.AddComponent<Image>(); img.sprite = Rounded; img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = raycast; return img;
        }
        public static Image Img(Transform parent, string name, Sprite sprite, Color color, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var r = Box2(parent, name, anchor, new Vector2(.5f, .5f), position, size); var img = r.gameObject.AddComponent<Image>(); img.sprite = sprite; img.color = color; img.raycastTarget = false; img.preserveAspect = true; return img;
        }
        public static Image Divider(Transform parent, float height = 2)
        {
            var r = Rect(parent, "Divider", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); var img = r.gameObject.AddComponent<Image>(); img.sprite = White; img.color = Rule; img.raycastTarget = false;
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = height; le.minHeight = height; return img;
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
        /// <summary>A line of text inside a vertical layout with a fixed preferred height.</summary>
        public static TextMeshProUGUI Line(Transform parent, string text, float size, Color color, float height)
        {
            var t = Text(parent, text, size, color); var le = t.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = height; le.minHeight = height; return t;
        }
        /// <summary>Screen heading with the accent tick used by every menu.</summary>
        public static TextMeshProUGUI Heading(Transform parent, string text, float size, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var t = Label(parent, text, size, Ink, anchorMin, anchorMax, offsetMin, offsetMax, align); t.fontStyle = FontStyles.Bold; t.characterSpacing = 6;
            if (align == TextAlignmentOptions.TopLeft || align == TextAlignmentOptions.Left || align == TextAlignmentOptions.MidlineLeft)
            {
                var tick = Rect(t.rectTransform, "Tick", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, -10), new Vector2(64, -6)); var ti = tick.gameObject.AddComponent<Image>(); ti.sprite = White; ti.color = Accent; ti.raycastTarget = false;
            }
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, float width = 320, float height = 54, float fontSize = 26)
        {
            var go = new GameObject("Button " + label, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.sizeDelta = new Vector2(width, height);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height; le.minHeight = height;
            var img = go.AddComponent<Image>(); img.sprite = Rounded; img.type = Image.Type.Sliced; img.color = Color.white;
            var b = go.AddComponent<Button>(); b.targetGraphic = img;
            var colors = b.colors; colors.normalColor = new Color(.13f, .145f, .16f, .96f); colors.highlightedColor = new Color(.22f, .24f, .27f, 1); colors.selectedColor = new Color(.6f, .37f, .13f, 1);
            colors.pressedColor = new Color(.85f, .55f, .2f, 1); colors.disabledColor = new Color(.09f, .095f, .1f, .75f); colors.fadeDuration = .08f; colors.colorMultiplier = 1; b.colors = colors;
            var t = Text(go.transform, label, fontSize, Ink, TextAlignmentOptions.Center); var tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(14, 2); tr.offsetMax = new Vector2(-14, -2);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            if (onClick != null) b.onClick.AddListener(onClick);
            b.onClick.AddListener(() => { if (AshfallGame.Instance != null && AshfallGame.Instance.Audio != null) AshfallGame.Instance.Audio.Ui("click"); });
            return b;
        }
        /// <summary>A primary (accent-filled) call to action.</summary>
        public static Button Primary(Transform parent, string label, UnityAction onClick, float width = 320, float height = 58, float fontSize = 26)
        {
            var b = Button(parent, label, onClick, width, height, fontSize);
            var c = b.colors; c.normalColor = new Color(.78f, .46f, .14f, 1); c.highlightedColor = new Color(.92f, .58f, .2f, 1); c.selectedColor = new Color(1f, .66f, .26f, 1); c.pressedColor = new Color(1f, .76f, .4f, 1); b.colors = c;
            var t = b.GetComponentInChildren<TextMeshProUGUI>(); t.color = new Color(.06f, .05f, .04f); t.fontStyle = FontStyles.Bold;
            return b;
        }
        public static void SetLabel(Button b, string label) { var t = b.GetComponentInChildren<TextMeshProUGUI>(); if (t != null) t.text = label; }
        /// <summary>Marks a list/tab button as the active one (tint survives the button's own colour transitions).</summary>
        public static void SetActive(Button b, bool active) { var c = b.colors; c.normalColor = active ? ActiveFill : new Color(.13f, .145f, .16f, .96f); b.colors = c; }
        public static readonly Color ActiveFill = new Color(.46f, .29f, .11f, 1), WarmFill = new Color(.27f, .2f, .11f, .96f);
        /// <summary>A warm resting tint for highlighted rows (rucksacks).</summary>
        public static void SetWarm(Button b) { var c = b.colors; c.normalColor = WarmFill; b.colors = c; }

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
        /// <summary>Horizontal button strip for tabs and actions.</summary>
        public static RectTransform Strip(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, TextAnchor align = TextAnchor.MiddleLeft, float spacing = 8)
        {
            var r = Rect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = spacing; h.childAlignment = align; h.childControlWidth = false; h.childControlHeight = false; return r;
        }

        /// <summary>Left/right value picker that works with d-pad and mouse.</summary>
        public sealed class Selector
        {
            public string[] options; public int index; public Action<int> changed; public TextMeshProUGUI value; public Button left, right;
            public void Set(int i) { index = (i % options.Length + options.Length) % options.Length; value.text = options[index]; if (changed != null) changed(index); }
        }
        public static Selector Picker(Transform parent, string label, string[] options, int index, Action<int> changed, float labelWidth = 260, float valueWidth = 300)
        {
            var row = Row(parent, 50, 8); RowLabel(row.transform, label, labelWidth, 23, Dim);
            var s = new Selector { options = options, index = index, changed = changed };
            s.left = Button(row.transform, "<", () => s.Set(s.index - 1), 50, 44, 24);
            s.value = RowLabel(row.transform, options[index], valueWidth, 23); s.value.alignment = TextAlignmentOptions.Center;
            s.right = Button(row.transform, ">", () => s.Set(s.index + 1), 50, 44, 24);
            return s;
        }

        public static TMP_InputField Input(Transform parent, string value, float width, float height = 50)
        {
            var go = new GameObject("Input", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var img = go.AddComponent<Image>(); img.sprite = Rounded; img.type = Image.Type.Sliced; img.color = new Color(.25f, .27f, .3f, 1);
            var area = Rect(go.transform, "Text Area", Vector2.zero, Vector2.one, new Vector2(14, 4), new Vector2(-14, -4)); area.gameObject.AddComponent<RectMask2D>();
            var text = Text(area, value, 24, Ink, TextAlignmentOptions.MidlineLeft); var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var field = go.AddComponent<TMP_InputField>(); field.textViewport = area; field.textComponent = text; field.text = value; field.characterLimit = 28; field.targetGraphic = img;
            var c = field.colors; c.normalColor = new Color(.5f, .5f, .5f); c.highlightedColor = new Color(.72f, .72f, .72f); c.selectedColor = new Color(1f, .8f, .55f); c.pressedColor = new Color(1f, .85f, .6f); field.colors = c;
            return field;
        }

        public static Slider Slider(Transform parent, string label, float value, Action<float> changed, float labelWidth = 260)
        {
            var row = Row(parent, 50, 8); RowLabel(row.transform, label, labelWidth, 23, Dim);
            var go = new GameObject("Slider", typeof(RectTransform)); go.transform.SetParent(row.transform, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 360; le.preferredHeight = 30;
            var bg = Rect(go.transform, "Background", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, -6), new Vector2(0, 6)); Card(bg, new Color(.16f, .17f, .19f));
            var fillArea = Rect(go.transform, "Fill Area", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, -6), new Vector2(0, 6));
            var fill = Fill(fillArea, "Fill"); Card(fill, Accent);
            var handleArea = Rect(go.transform, "Handle Area", Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
            var handle = Rect(handleArea, "Handle", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-11, 2), new Vector2(11, -2)); var hi = handle.gameObject.AddComponent<Image>(); hi.sprite = Circle; hi.color = Ink;
            var s = go.AddComponent<Slider>(); s.fillRect = fill; s.handleRect = handle; s.targetGraphic = hi; s.minValue = 0; s.maxValue = 1; s.value = value;
            var c = s.colors; c.selectedColor = Accent; c.highlightedColor = new Color(1, .85f, .6f); s.colors = c;
            s.onValueChanged.AddListener(v => changed(v));
            var readout = RowLabel(row.transform, Mathf.RoundToInt(value * 100) + "%", 90, 22, Dim); s.onValueChanged.AddListener(v => readout.text = Mathf.RoundToInt(v * 100) + "%");
            return s;
        }

        public static RectTransform Scroll(Transform parent, string name, out RectTransform content)
        {
            var root = Fill(parent, name); var img = root.gameObject.AddComponent<Image>(); img.sprite = Rounded; img.type = Image.Type.Sliced; img.color = new Color(0, 0, 0, .28f);
            var viewport = Rect(root, "Viewport", Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4)); viewport.gameObject.AddComponent<RectMask2D>(); var vi = viewport.gameObject.AddComponent<Image>(); vi.color = new Color(0, 0, 0, 0.01f);
            content = Rect(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero); content.pivot = new Vector2(.5f, 1);
            Column(content, 4, TextAnchor.UpperLeft, 6);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = root.gameObject.AddComponent<ScrollRect>(); sr.viewport = viewport; sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 40;
            return root;
        }
        public static void Clear(Transform t) { for (int i = t.childCount - 1; i >= 0; i--) { var c = t.GetChild(i).gameObject; c.SetActive(false); UnityEngine.Object.Destroy(c); } }

        public static Image Bar(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color, out TextMeshProUGUI label)
        {
            var r = Box2(parent, name, anchor, new Vector2(0, .5f), position, size); Card(r, new Color(0, 0, 0, .6f));
            var img = FillBar(r, color);
            label = Label(r, name, 18, Ink, Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-8, 0), TextAlignmentOptions.MidlineLeft);
            return img;
        }
        /// <summary>A horizontal fill inside a bar rect (inset by 3).</summary>
        public static Image FillBar(RectTransform bar, Color color, float inset = 3)
        {
            var fill = Rect(bar, "Fill", Vector2.zero, Vector2.one, new Vector2(inset, inset), new Vector2(-inset, -inset)); var img = fill.gameObject.AddComponent<Image>(); img.sprite = White; img.color = color; img.raycastTarget = false;
            img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Horizontal; img.fillAmount = 1;
            return img;
        }

        /// <summary>Fades a panel in from transparent (adds a CanvasGroup on first use).</summary>
        public static void FadeIn(RectTransform r, float seconds = .14f)
        {
            var f = r.GetComponent<UiFade>(); if (f == null) f = r.gameObject.AddComponent<UiFade>(); f.Play(seconds);
        }

        // ---------------------------------------------------------------- items
        /// <summary>Loot tier colours: Common white, Uncommon green, Rare blue, Military orange.</summary>
        public static Color TierColor(int tier) { switch (tier) { case 1: return new Color(.45f, .88f, .45f); case 2: return new Color(.42f, .66f, 1f); case 3: return new Color(1f, .6f, .2f); default: return new Color(.9f, .9f, .88f); } }
        public static string TierHex(int tier) { switch (tier) { case 1: return "#73e073"; case 2: return "#6ba8ff"; case 3: return "#ff9933"; default: return "#e6e6e0"; } }
        public static string TierName(int tier) { switch (tier) { case 1: return "Uncommon"; case 2: return "Rare"; case 3: return "Military"; default: return "Common"; } }
        public static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }

        public static string ItemIcon(ItemDefLite d)
        {
            switch (d.id)
            {
                case "baseball_bat": case "nailed_bat": return "ICON_SM_Wep_Bat_Wood_01_Clean";
                case "pipe": case "crowbar": case "golf_club": return "ICON_SM_Wep_Bat_Metal_01_Clean";
                case "axe": return "ICON_SM_Wep_FireAxe_01_Clean";
                case "hammer": return "ICON_SM_Wep_Hammer_01_Clean";
                case "shovel": return "ICON_SM_Wep_Pickaxe_01_Clean";
                case "pistol": case "smg": return "ICON_SM_Wep_Pistol_01_Clean";
                case "revolver": return "ICON_SM_Wep_Pistol_Revolver_01_Clean";
                case "shotgun": return "ICON_SM_Wep_Shotgun_01_Farm_Clean";
                case "rifle": case "hunting_rifle": case "assault_rifle": return "ICON_SM_Wep_HuntingRifle_01_Clean";
                case "ammo_9mm": case "ammo_38": case "ammo_308": case "ammo_556": return "ICON_SM_Wep_Pistol_Ammo_Box_01";
                case "shells": return "ICON_SM_Wep_Shotgun_Ammo_Box_01";
                case "grenade": case "molotov": case "pipe_bomb": return "ICON_Inventory_Explosives";
                case "rucksack_food": case "rucksack_meds": case "rucksack_ammo": case "rucksack_materials": case "rucksack_fuel": return "ICON_SM_Chr_Attach_Backpack_01";
                case "plague_sample": case "plague_cure": return "ICON_Status_Radiation";
                case "vest_1": case "vest_2": case "vest_3": return "ICON_Status_Armor";
                case "helmet_1": case "helmet_2": case "helmet_3": return "ICON_Inventory_Helmets";
                case "water_bottle": return "ICON_SM_Item_Bottle_01";
                case "energy_drink": case "soda": case "juice": case "milk": case "coffee": return "ICON_SM_Item_Drink_Bottle_02";
                case "first_aid_kit": return "ICON_SM_Item_HealthKit_01";
                case "painkillers": case "antibiotics": return "ICON_SM_Item_Pills_01";
                case "battery": return "ICON_SM_Item_Battery_01";
                case "gas_can": return "ICON_SM_Prop_GasCan_01";
                case "plank": case "log": case "branch": return "ICON_SM_Prop_Wood_Stack_01";
                case "nails": case "duct_tape": case "screwdriver": case "saw": return "ICON_SM_Prop_Scav_Scrap_13";
                case "boots": return "ICON_SM_Prop_Shoes_01";
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
