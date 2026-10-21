using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Read-only adapters between the Core contracts and the UI: names, colours, roles and the few queries the
    /// contracts do not expose yet. Keep contract guesses here so they are easy to replace when the systems fill in.</summary>
    public static class UiData
    {
        // ---------------------------------------------------------------- community stock
        /// <summary>Stock capacity for a resource (UiCommunityApi.StockCap; display only, never used for rules).</summary>
        public static float Capacity(World w, Resource r) { return UiCommunityApi.StockCap(w, r); }
        public static readonly Resource[] Resources = { Resource.Food, Resource.Meds, Resource.Ammo, Resource.Materials, Resource.Fuel };
        public static string ResourceName(Resource r) { return r.ToString(); }
        public static string ResourceIcon(Resource r)
        {
            switch (r) { case Resource.Food: return "ICON_Inventory_Herbs"; case Resource.Meds: return "ICON_Inventory_Healing"; case Resource.Ammo: return "ICON_Map_Ammo"; case Resource.Materials: return "ICON_Inventory_Minerals"; default: return "ICON_SM_Prop_GasCan_01"; }
        }
        public static Color ResourceColor(Resource r)
        {
            switch (r) { case Resource.Food: return new Color(.62f, .86f, .44f); case Resource.Meds: return new Color(.94f, .45f, .45f); case Resource.Ammo: return new Color(.96f, .8f, .4f); case Resource.Materials: return new Color(.72f, .66f, .58f); default: return new Color(.92f, .6f, .3f); }
        }

        // ---------------------------------------------------------------- members
        /// <summary>Community members (alive or dead). Falls back to every faction-0 survivor while the roster is empty.</summary>
        public static void Members(World w, List<Survivor> into)
        {
            into.Clear();
            foreach (int id in w.community.members) if (id >= 0 && id < w.survivors.Count) into.Add(w.survivors[id]);
            if (into.Count == 0) foreach (var s in w.survivors) if (s.faction == 0) into.Add(s);
        }
        /// <summary>Controlled, companion, garrison (resident at an outpost) or resident.</summary>
        public static string Role(World w, Survivor s)
        {
            if (!s.alive) return "Dead";
            if (s.id == w.playerId) return "Controlled";
            if (w.community.companion == s.id || s.ai == AiRole.Companion) return "Companion";
            if (UiCommunityApi.Garrison(w) == s.id) return "Garrison";
            foreach (var o in w.community.outposts) if (Vec2.Distance(o.pos, s.pos) < 20) return "Garrison";
            return "Resident";
        }
        public static Color RoleColor(string role)
        {
            switch (role) { case "Controlled": return UIKit.Accent; case "Companion": return UIKit.Blue; case "Garrison": return new Color(.7f, .8f, .6f); case "Dead": return UIKit.Bad; default: return UIKit.Dim; }
        }

        /// <summary>State of Decay skill names for the Core skills.</summary>
        public static string SkillName(Skill k)
        {
            switch (k)
            {
                case Skill.Fitness: return "Cardio"; case Skill.Sneaking: return "Wits"; case Skill.Melee: return "Fighting"; case Skill.Aiming: return "Shooting";
                case Skill.FirstAid: return "Medicine"; case Skill.Carpentry: return "Construction"; case Skill.Mechanics: return "Mechanics"; case Skill.Cooking: return "Cooking";
                case Skill.Foraging: return "Survival"; case Skill.Farming: return "Gardening"; case Skill.Fishing: return "Fishing";
                default: return k.ToString();
            }
        }
        /// <summary>The four core skills first, then specialties (only the ones the member has trained).</summary>
        public static readonly Skill[] CoreSkills = { Skill.Fitness, Skill.Sneaking, Skill.Melee, Skill.Aiming };
        public static readonly Skill[] Specialties = { Skill.FirstAid, Skill.Carpentry, Skill.Mechanics, Skill.Cooking, Skill.Foraging, Skill.Farming };

        public static string TraitName(string id) { var t = Trait.Get(id); return t == null ? id : t.name; }

        /// <summary>Short injury summary for the community card (replaces the old body-part panel).</summary>
        public static void Injuries(Survivor s, List<string> into)
        {
            into.Clear();
            if (s.injury >= 1) into.Add("Injured: maximum health " + Mathf.RoundToInt(s.MaxHealth) + " until rested or treated");
            foreach (var wd in s.wounds) into.Add(Wound.Part(wd.part) + ": " + (wd.kind == WoundKind.GlassCut ? "glass cut" : wd.kind.ToString().ToLowerInvariant()) + (wd.bleeding ? " <color=#ff7060>(bleeding)</color>" : wd.bandaged ? " (bandaged)" : ""));
            if (s.plagueInfected) into.Add("<color=#c48cff>Blood plague " + Mathf.RoundToInt(s.plague) + "%: needs a plague cure</color>");
        }

        // ---------------------------------------------------------------- humans
        public static string DisplayName(Survivor s) { var c = string.IsNullOrEmpty(s.character) ? null : Characters.Get(s.character); return c != null && !string.IsNullOrEmpty(c.name) ? c.name : s.name; }
        /// <summary>Character title, or the faction name for generic survivors.</summary>
        public static string Subtitle(World w, Survivor s)
        {
            var c = string.IsNullOrEmpty(s.character) ? null : Characters.Get(s.character);
            if (c != null && !string.IsNullOrEmpty(c.title)) return c.title;
            if (s.faction == 0) return "Your community";
            var f = w.FactionOf(s); return f == null || string.IsNullOrEmpty(f.name) ? RoleName(s.ai) : f.name;
        }
        public static string RoleName(AiRole r)
        {
            switch (r) { case AiRole.Rival: return "Rival crew"; case AiRole.Enclave: return "Enclave"; case AiRole.Raider: return "Raider"; case AiRole.Trader: return "Trader"; case AiRole.Hunter: return "Hunter"; case AiRole.Companion: return "Companion"; default: return "Survivor"; }
        }
        /// <summary>Nameplate colour: community blue, friendly green, wary white, opportunist yellow, hostile red.</summary>
        public static Color Stance(World w, Survivor viewer, Survivor other)
        {
            if (other.faction == 0) return UIKit.Blue;
            if (viewer != null && w.Hostile(viewer, other)) return UIKit.Bad;
            var f = w.FactionOf(other); var d = f != null ? f.disposition : other.disposition;
            switch (d) { case Disposition.Friendly: return UIKit.Good; case Disposition.Opportunist: return UIKit.Warn; case Disposition.Hunter: return UIKit.Bad; default: return UIKit.Ink; }
        }
        public static string StanceName(World w, Survivor viewer, Survivor other)
        {
            if (other.faction == 0) return "Community";
            if (viewer != null && w.Hostile(viewer, other)) return "Hostile";
            var f = w.FactionOf(other); var d = f != null ? f.disposition : other.disposition; return d.ToString();
        }

        // ---------------------------------------------------------------- base
        public static string FacilityName(FacilityKind k) { return UiCommunityApi.FacilityName(k); }
        public static string FacilityBlurb(FacilityKind k)
        {
            switch (k)
            {
                case FacilityKind.Storage: return "Raises the stock cap for every resource.";
                case FacilityKind.Beds: return "Members rest here to recover fatigue.";
                case FacilityKind.Infirmary: return "Faster injury recovery; makes plague cures from samples.";
                case FacilityKind.Workshop: return "Repairs gear and makes explosives.";
                case FacilityKind.Watchtower: return "A resident shoots zombies that come near the base.";
                case FacilityKind.Garden: return "Grows food every day.";
                case FacilityKind.Kitchen: return "Better meals: morale rises.";
                case FacilityKind.RadioRoom: return "Scouting reports and more missions over the radio.";
                default: return "";
            }
        }
        public static readonly FacilityKind[] Facilities = (FacilityKind[])Enum.GetValues(typeof(FacilityKind));
        /// <summary>0..1 construction progress of a facility under construction (1 when built).</summary>
        public static float Progress(Facility f) { return UiCommunityApi.Progress(f); }

        public static string MissionKindName(MissionKind k)
        {
            switch (k) { case MissionKind.ClearInfestation: return "Clear infestation"; case MissionKind.KillFreak: return "Kill a freak"; default: return k.ToString(); }
        }
        public static string GiverName(string giver)
        {
            if (string.IsNullOrEmpty(giver)) return "Your community";
            var c = Characters.Get(giver); return c != null && !string.IsNullOrEmpty(c.name) ? c.name : giver;
        }
        /// <summary>True when a mission belongs to this NPC (by character key, faction name or faction id).</summary>
        public static bool GivenBy(World w, Mission m, Survivor npc)
        {
            if (npc == null) return false;
            if (!string.IsNullOrEmpty(npc.character) && m.giver == npc.character) return true;
            var f = w.FactionOf(npc); return f != null && f.id != 0 && (m.faction == f.id || m.giver == f.name);
        }
        public static string Hours(double minutes) { double h = minutes / 60; return h >= 48 ? (h / 24).ToString("0") + " days" : h >= 1 ? h.ToString("0.#") + " h" : Math.Max(0, minutes).ToString("0") + " min"; }

        public static bool IsRucksack(ItemDef d) { return d.resource >= 0; }
        public static Vec2 BuildingCenter(Map m, int id) { if (id < 0 || id >= m.buildings.Count) return Vec2.Zero; var r = m.buildings[id].rect; return new Vec2(r.x + r.w * .5f, r.y + r.h * .5f); }
        public static string BuildingName(Map m, int id) { if (id < 0 || id >= m.buildings.Count) return "Unknown"; var b = m.buildings[id]; return string.IsNullOrEmpty(b.name) ? b.kind.ToString() : b.name; }

        public static Color ThreatColor(int threat) { return threat >= 4 ? UIKit.Bad : threat == 3 ? new Color(1, .6f, .25f) : threat == 2 ? UIKit.Warn : UIKit.Ink; }
    }
}
