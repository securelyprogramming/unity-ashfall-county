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
    /// <summary>New Game: the community leader, world options and the PUBG-style drop map.</summary>
    public sealed partial class AshfallUI
    {
        TMP_InputField nameField, seedField; UIKit.Selector sexPick, profPick; TextMeshProUGUI profText;
        readonly SandboxSettings draft = new SandboxSettings { mapSize = 4096 };

        static readonly int[] MapSizes = { 4096, 6144, 3072, 2048 };
        static readonly float[] FreakDensities = { 1, 1.6f, 2.4f, .5f };
        static readonly float[] HumanDensities = { 1, 1.6f, .5f, .25f };
        static readonly int[] DayLengths = { 45, 60, 90, 30 };
        static readonly int[] Founders = { 3, 4, 2, 1 };

        void BuildNewGame()
        {
            newGame = Page("New Game", "NEW COMMUNITY");
            UIKit.Label(newGame, "Choose who leads, shape the county, then pick where your group sets down.", 24, UIKit.Dim, new Vector2(0, 1), new Vector2(1, 1), new Vector2(92, -176), new Vector2(-90, -138));

            var leftCard = UIKit.Rect(newGame, "Leader", new Vector2(0, 0), new Vector2(.47f, 1), new Vector2(90, 140), new Vector2(-14, -196)); UIKit.Background(leftCard, UIKit.Panel, true);
            var left = UIKit.Rect(leftCard, "Column", Vector2.zero, Vector2.one, new Vector2(30, 20), new Vector2(-30, -24)); UIKit.Column(left, 10);
            UIKit.Line(left, "<b>Community leader</b>", 30, UIKit.Accent, 44);
            var nameRow = UIKit.Row(left, 54); UIKit.RowLabel(nameRow.transform, "Name", 240, 23, UIKit.Dim); nameField = UIKit.Input(nameRow.transform, game.Prefs.lastName, 420);
            sexPick = UIKit.Picker(left, "Sex", new[] { "Female", "Male" }, game.Prefs.lastFemale ? 0 : 1, i => { }, 240);
            int prof = Array.FindIndex(Profession.All, p => p.id == game.Prefs.lastProfession); if (prof < 0) prof = 0;
            profPick = UIKit.Picker(left, "Profession", Profession.All.Select(p => p.name).ToArray(), prof, i => profText.text = Profession.All[i].description + "\n<color=#999>Starts with: " + Kit(Profession.All[i]) + "</color>", 240);
            profText = UIKit.Line(left, "", 21, UIKit.Ink, 84);
            profPick.Set(prof);
            BuildTraitRow(left);
            UIKit.Divider(left);
            UIKit.Line(left, "Other members join with their own skills and traits. Everyone shares the base stock, morale and influence. Death is permanent per character; the game ends only when the whole community is gone.", 19, UIKit.Dim, 78);

            var rightCard = UIKit.Rect(newGame, "County", new Vector2(.47f, 0), new Vector2(1, 1), new Vector2(14, 140), new Vector2(-90, -196)); UIKit.Background(rightCard, UIKit.Panel, true);
            var right = UIKit.Rect(rightCard, "Column", Vector2.zero, Vector2.one, new Vector2(30, 20), new Vector2(-30, -24)); UIKit.Column(right, 4);
            UIKit.Line(right, "<b>The county</b>", 30, UIKit.Accent, 44);
            UIKit.Picker(right, "Map size", new[] { "4 km × 4 km", "Massive · 6 km", "3 km × 3 km", "2 km × 2 km" }, 0, i => draft.mapSize = MapSizes[i], 250, 290);
            UIKit.Picker(right, "Zombie population", new[] { "Normal", "High", "Insane", "Low" }, 0, i => draft.population = new[] { Population.Normal, Population.High, Population.Insane, Population.Low }[i], 250, 290);
            UIKit.Picker(right, "Zombie speed", new[] { "Shamblers", "Fast shamblers", "Sprinters" }, 0, i => draft.speed = (ZombieSpeed)i, 250, 290);
            UIKit.Picker(right, "Freaks", new[] { "Normal", "Many", "Nightmare", "Rare" }, 0, i => draft.freakDensity = FreakDensities[i], 250, 290);
            UIKit.Picker(right, "Other survivors", new[] { "Normal", "Crowded", "Few", "Sparse" }, 0, i => draft.humanDensity = HumanDensities[i], 250, 290);
            UIKit.Picker(right, "Supply airdrops", new[] { "On", "Off" }, 0, i => draft.airdrops = i == 0, 250, 290);
            UIKit.Picker(right, "Artillery red zones", new[] { "On", "Off" }, 0, i => draft.redZones = i == 0, 250, 290);
            UIKit.Picker(right, "Day length", new[] { "45 minutes", "60 minutes", "90 minutes", "30 minutes" }, 0, i => draft.dayLengthMinutes = DayLengths[i], 250, 290);
            UIKit.Picker(right, "Founding members", new[] { "3", "4", "2", "Leader only" }, 0, i => draft.startingMembers = Founders[i], 250, 290);
            var seedRow = UIKit.Row(right, 54); UIKit.RowLabel(seedRow.transform, "Seed", 250, 23, UIKit.Dim);
            seedField = UIKit.Input(seedRow.transform, UnityEngine.Random.Range(100000, 999999).ToString(), 220); seedField.contentType = TMP_InputField.ContentType.IntegerNumber; seedField.characterLimit = 9;
            UIKit.Button(seedRow.transform, "Random", () => seedField.text = UnityEngine.Random.Range(100000, 999999).ToString(), 150, 46, 21);

            var bottom = ActionsBar(newGame);
            UIKit.Button(bottom, "Back", () => game.Show(Screen.Title), 240, 60, 26);
            UIKit.Primary(bottom, "Choose landing zone  >", () => game.OpenDropMap(DraftSettings()), 400, 60, 26);
        }
        static string Kit(Profession p)
        {
            var names = p.kit.Select(id => ItemCatalog.Get(id).name).ToList();
            return names.Count == 0 ? "nothing extra" : string.Join(", ", names);
        }

        /// <summary>The settings the player picked. Retired Zomboid systems (utility shut-off, evacuation) are switched off.</summary>
        SandboxSettings DraftSettings()
        {
            int seed; if (!int.TryParse(seedField.text, out seed)) { seed = UnityEngine.Random.Range(100000, 999999); seedField.text = seed.ToString(); }
            return new SandboxSettings
            {
                seed = seed, mapSize = draft.mapSize, population = draft.population, speed = draft.speed, dayLengthMinutes = draft.dayLengthMinutes,
                utilitiesMinDay = 0, utilitiesMaxDay = 0, evacuationDay = 0, lootAbundance = draft.lootAbundance, infection = draft.infection, respawn = draft.respawn,
                airdrops = draft.airdrops, redZones = draft.redZones, freakDensity = draft.freakDensity, humanDensity = draft.humanDensity, startingMembers = draft.startingMembers, landingZone = -1,
            };
        }
        string LeaderName { get { return string.IsNullOrWhiteSpace(nameField.text) ? "Mara Vale" : nameField.text.Trim(); } }

        // ---------------------------------------------------------------- drop map
        RectTransform dropMap, dropViewport, dropImageRect, dropPins, dropList, dropSurvey; RawImage dropImage;
        TextMeshProUGUI dropTitle, dropDetail, dropSurveyText; Button dropButton;
        Texture2D dropTexture; int dropSelected = -1, dropBuiltFor = -1;
        readonly List<RectTransform> dropPinRects = new List<RectTransform>();
        readonly List<Button> dropZoneButtons = new List<Button>();

        void BuildDropMap()
        {
            dropMap = Page("Drop map", "CHOOSE YOUR DROP", .94f);
            UIKit.Label(dropMap, "Your community sets down at a landing zone and claims a home nearby. Towns have loot and company; the wilds are quieter.", 22, UIKit.Dim, new Vector2(0, 1), new Vector2(.62f, 1), new Vector2(92, -176), new Vector2(0, -138));
            // Map on the left, zone list on the right.
            dropViewport = UIKit.Rect(dropMap, "Viewport", new Vector2(0, 0), new Vector2(.62f, 1), new Vector2(90, 130), new Vector2(-20, -190)); UIKit.Card(dropViewport, new Color(.02f, .022f, .026f, 1), true);
            dropImageRect = UIKit.Box2(dropViewport, "Map", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(800, 800));
            dropImage = dropImageRect.gameObject.AddComponent<RawImage>(); dropImage.raycastTarget = false; dropImage.color = new Color(.85f, .85f, .85f);
            dropPins = UIKit.Fill(dropImageRect, "Pins");
            dropSurvey = UIKit.Fill(dropViewport, "Survey"); UIKit.Card(dropSurvey, new Color(.02f, .022f, .026f, .92f));
            dropSurveyText = UIKit.Label(dropSurvey, "", 30, UIKit.Ink, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);

            var side = UIKit.Rect(dropMap, "Zones", new Vector2(.62f, 0), new Vector2(1, 1), new Vector2(10, 130), new Vector2(-90, -150)); UIKit.Background(side, UIKit.Panel, true);
            dropTitle = UIKit.Label(side, "", 32, UIKit.Accent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(26, -70), new Vector2(-26, -20)); dropTitle.fontStyle = FontStyles.Bold;
            dropDetail = UIKit.Label(side, "", 21, UIKit.Ink, new Vector2(0, 1), new Vector2(1, 1), new Vector2(26, -170), new Vector2(-26, -74));
            var listArea = UIKit.Rect(side, "List", new Vector2(0, 0), new Vector2(1, 1), new Vector2(18, 18), new Vector2(-18, -180)); UIKit.Scroll(listArea, "Landing zones", out dropList);

            var bottom = ActionsBar(dropMap);
            UIKit.Button(bottom, "Back", () => game.Show(Screen.NewGame), 240, 60, 26);
            dropButton = UIKit.Primary(bottom, "Drop here", Drop, 340, 60, 27);
            dropMap.gameObject.SetActive(false);
        }

        /// <summary>The background survey finished: build the map texture, pins and the zone list.</summary>
        public void PreviewReady() { if (dropMap != null && dropMap.gameObject.activeSelf) RefreshDropMap(); }

        void RefreshDropMap()
        {
            var p = game.Preview;
            dropSurvey.gameObject.SetActive(p == null);
            dropButton.interactable = p != null;
            if (p == null)
            {
                dropTitle.text = "Surveying..."; dropDetail.text = "Generating the county layout. This takes a few seconds on the bigger maps.";
                UIKit.Clear(dropList); UIKit.Clear(dropPins); dropPinRects.Clear(); dropZoneButtons.Clear(); dropBuiltFor = -1;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(dropButton.transform.parent.GetChild(0).gameObject);
                return;
            }
            int key = p.seed * 31 + p.size;
            if (dropBuiltFor != key)
            {
                dropBuiltFor = key; dropSelected = -1;
                if (dropTexture != null) Destroy(dropTexture);
                dropTexture = new Texture2D(p.texWidth, p.texHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                dropTexture.SetPixels32(p.pixels); dropTexture.Apply(); dropImage.texture = dropTexture;
                UIKit.Clear(dropPins); UIKit.Clear(dropList); dropPinRects.Clear(); dropZoneButtons.Clear();
                // Landmarks for context (non-interactive), then the selectable landing zones.
                foreach (var lm in p.gen.landmarks)
                {
                    bool town = lm.kind == LandmarkKind.Town;
                    if (town && p.gen.landingZones.Any(z => Mathf.Abs(z.x - lm.x) < 8 && Mathf.Abs(z.y - lm.y) < 8)) continue;
                    var r = MapPin(dropPins, lm.name, new Vector2(lm.x / (float)p.mapWidth, lm.y / (float)p.mapHeight), LandmarkColor(lm.kind), LandmarkIcon(lm.kind), 17);
                    r.GetComponentInChildren<TextMeshProUGUI>().alpha = .8f;
                }
                var randomButton = UIKit.Button(dropList, "<b>Random</b>  <color=#999><size=18>let the county decide</size></color>", () => SelectZone(-1), 640, 54, 22);
                randomButton.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft; dropZoneButtons.Add(randomButton);
                for (int i = 0; i < p.gen.landingZones.Count; i++)
                {
                    var z = p.gen.landingZones[i]; int idx = i;
                    var pin = MapPin(dropPins, z.name, new Vector2(z.x / (float)p.mapWidth, z.y / (float)p.mapHeight), UIKit.Ink, null, 21);
                    var dot = UIKit.Box2(pin, "Dot", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 26), new Vector2(26, 26)); var di = dot.gameObject.AddComponent<Image>(); di.sprite = UIKit.Circle; di.color = UIKit.Accent;
                    var hit = dot.gameObject.AddComponent<Button>(); hit.targetGraphic = di; hit.onClick.AddListener(() => SelectZone(idx)); var nav = hit.navigation; nav.mode = Navigation.Mode.None; hit.navigation = nav;
                    dropPinRects.Add(pin);
                    var b = UIKit.Button(dropList, "<b>" + z.name + "</b>  <color=#999><size=18>" + z.description + "</size></color>", () => SelectZone(idx), 640, 54, 22);
                    b.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft; dropZoneButtons.Add(b);
                }
                if (p.gen.landingZones.Count == 0) UIKit.Line(dropList, "This county has no named landing zones; your group lands near a random town.", 20, UIKit.Dim, 60);
            }
            SelectZone(dropSelected);
            if (EventSystem.current != null && dropZoneButtons.Count > 0) EventSystem.current.SetSelectedGameObject(dropZoneButtons[Mathf.Clamp(dropSelected + 1, 0, dropZoneButtons.Count - 1)].gameObject);
        }

        RectTransform MapPin(RectTransform parent, string text, Vector2 uv, Color color, string icon, float size)
        {
            var r = UIKit.Rect(parent, text, uv, uv, new Vector2(-130, -16), new Vector2(130, 16));
            var spr = UIKit.Icon(icon);
            if (spr != null) { var ic = UIKit.Img(r, "Icon", spr, color, new Vector2(.5f, .5f), new Vector2(0, 22), new Vector2(26, 26)); }
            else if (icon != null) UIKit.Img(r, "Icon", UIKit.Circle, color, new Vector2(.5f, .5f), new Vector2(0, 22), new Vector2(10, 10));
            var t = UIKit.Label(r, text, size, color, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center); t.outlineWidth = .25f; t.outlineColor = new Color32(0, 0, 0, 255); t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
            return r;
        }

        void SelectZone(int i)
        {
            var p = game.Preview; if (p == null) return;
            dropSelected = i >= 0 && i < p.gen.landingZones.Count ? i : -1;
            for (int k = 0; k < dropZoneButtons.Count; k++) UIKit.SetActive(dropZoneButtons[k], k == dropSelected + 1);
            for (int k = 0; k < dropPinRects.Count; k++)
            {
                bool on = k == dropSelected; var pin = dropPinRects[k];
                pin.localScale = Vector3.one * (on ? 1.25f : 1); pin.GetComponentInChildren<TextMeshProUGUI>().color = on ? UIKit.Accent : UIKit.Ink;
                var dot = pin.Find("Dot"); if (dot != null) dot.GetComponent<Image>().color = on ? UIKit.Good : UIKit.Accent;
                if (on) pin.SetAsLastSibling();
            }
            if (dropSelected < 0) { dropTitle.text = "Random landing zone"; dropDetail.text = p.gen.landingZones.Count + " landing zones · " + p.gen.landmarks.Count + " landmarks · " + p.mapWidth + " × " + p.mapHeight + " m · seed " + p.seed; }
            else
            {
                var z = p.gen.landingZones[dropSelected];
                var near = p.gen.landmarks.Where(l => l.kind != LandmarkKind.Town).OrderBy(l => (l.x - z.x) * (l.x - z.x) + (l.y - z.y) * (l.y - z.y)).FirstOrDefault();
                dropTitle.text = z.name;
                dropDetail.text = z.description + (near == null ? "" : "\n<color=#aaa>Nearest landmark: " + near.name + " · " + Mathf.RoundToInt(Vector2.Distance(new Vector2(near.x, near.y), new Vector2(z.x, z.y))) + " m " + UiCompass.Bearing(new Vector2(near.x - z.x, near.y - z.y)) + "</color>");
            }
        }

        void Drop()
        {
            var p = game.Preview; if (p == null) return;
            var settings = DraftSettings(); settings.seed = p.seed; settings.mapSize = p.size; settings.landingZone = dropSelected;
            game.StartNewGame(settings, LeaderName, Profession.All[profPick.index].id, sexPick.index == 0, chosenTraits.ToArray());
        }

        void TickDropMap(bool back)
        {
            if (back) { game.Show(Screen.NewGame); return; }
            if (game.Preview == null) { dropSurveyText.SetText("Surveying Ashfall County...\n<size=22><color=#999>{0:1} s</color></size>", game.PreviewSeconds); return; }
            // Fit the square county into the viewport.
            var view = dropViewport.rect; float side = Mathf.Min(view.width, view.height) - 20;
            if (Mathf.Abs(dropImageRect.sizeDelta.x - side) > .5f) dropImageRect.sizeDelta = new Vector2(side, side);
        }

        static Color LandmarkColor(LandmarkKind k)
        {
            switch (k)
            {
                case LandmarkKind.MilitaryBase: case LandmarkKind.Airfield: return new Color(1f, .62f, .26f);
                case LandmarkKind.RaiderCompound: return new Color(1f, .4f, .36f);
                case LandmarkKind.Hospital: return new Color(.5f, .9f, .6f);
                case LandmarkKind.RadioStation: return new Color(.55f, .78f, 1f);
                default: return new Color(.86f, .84f, .78f);
            }
        }
        static string LandmarkIcon(LandmarkKind k)
        {
            switch (k)
            {
                case LandmarkKind.MilitaryBase: return "ICON_Map_GunShop"; case LandmarkKind.Airfield: return "ICON_Map_Vehicle"; case LandmarkKind.Hospital: return "ICON_Map_Healing";
                case LandmarkKind.Garage: return "ICON_Map_Petrol"; case LandmarkKind.RaiderCompound: return "ICON_Map_Skull"; case LandmarkKind.RadioStation: return "ICON_Map_Quest";
                case LandmarkKind.Warehouse: return "ICON_Map_Ammo"; default: return "ICON_Map_Unknown";
            }
        }
    }

    /// <summary>Compass helpers for banners and the drop map (map +y is south-to-north on the overview).</summary>
    public static class UiCompass
    {
        static readonly string[] Points = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
        /// <summary>Eight-point bearing of a map-space offset (x east, y north as drawn on the overview).</summary>
        public static string Bearing(Vector2 d)
        {
            if (d.sqrMagnitude < .01f) return "here";
            float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; int i = Mathf.RoundToInt(a / 45f); i = ((i % 8) + 8) % 8; return Points[i];
        }
    }
}
