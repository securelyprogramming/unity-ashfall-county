using System;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>All event text: the HUD log, the radio log (game.Radio), the kill feed, toasts and banners.
    /// Filters keep it relevant: the controlled survivor's own events, community-wide news and what happens nearby.</summary>
    public sealed partial class AshfallUI
    {
        const float Nearby = 22, Earshot = 45;

        /// <summary>Every simulation event, forwarded by AshfallGame.Present before the frame's events are cleared.
        /// Text, the radio log, the kill feed, banners and notifications are handled here; audio and effects are not.</summary>
        public void OnEvent(SimEvent e)
        {
            var w = game.World; var me = game.Player; if (w == null || me == null) return;
            bool mine = e.survivor == me.id;
            var actor = e.survivor >= 0 && e.survivor < w.survivors.Count ? w.survivors[e.survivor] : null;
            bool member = actor != null && actor.faction == 0;
            float dist = Vec2.Distance(e.pos, me.pos);
            bool near = dist < Nearby, earshot = dist < Earshot;
            string text = e.text ?? "";
            switch (e.kind)
            {
                case EventKind.Message: if (mine || e.survivor < 0) Log(text); break;
                case EventKind.Radio:
                    // Broadcasts reach every radio carrier: keep the controlled member's copy (or a county-wide one).
                    if (mine || e.survivor < 0) Radio(text);
                    break;
                case EventKind.LevelUp: if (mine) Log("<color=#ffe08a>" + text + "</color>"); break;
                case EventKind.Hurt: if (mine && text != "blocked" && text != "glass" && text.Length > 0) Log("<color=#ff8a7a>" + text + "</color>"); break;
                case EventKind.Bite: if (mine) Log("<color=#ff5050>" + text + "</color>"); break;
                case EventKind.Break: case EventKind.Craft: case EventKind.Barricade:
                    if (text.Length > 0 && (mine || near)) Log(e.kind == EventKind.Craft ? "Made: " + text : e.kind == EventKind.Barricade ? "Barricaded: " + text : text);
                    break;
                case EventKind.Evacuated: Log("<color=#9fffb0>" + text + "</color>"); break;
                case EventKind.Death:
                    if (member && !mine) { Banner(actor.name.ToUpperInvariant() + " IS DEAD", text.Length > 0 ? text : "A member of your community is gone.", UIKit.Bad, 5); Log("<color=#ff8a7a>" + actor.name + " died. " + text + "</color>"); }
                    break;
                case EventKind.KillFeed: KillFeed(text); break;

                // Freaks and hearts.
                case EventKind.Scream: if (earshot) Log("<color=#ffb070>" + Or(text, "A screamer shrieks. Everything nearby heard that.") + "</color>"); break;
                case EventKind.Gas: if (near) Toast(Or(text, "Toxic gas! Get out of the cloud.")); break;
                case EventKind.Pounce: if (mine) Log("<color=#ff6050>" + Or(text, "A feral pounces on you!") + "</color>"); else if (member && near) Log(actor.name + " was pounced by a feral!"); break;
                case EventKind.Charge: if (mine || near) Log("<color=#ff7a50>" + Or(text, "A juggernaut charges!") + "</color>"); break;
                case EventKind.Grab: if (mine) Log("<color=#ff5050>" + Or(text, "Grabbed! Break free!") + "</color>"); break;
                case EventKind.Explosion: if (mine && text.Length > 0 && near) Log(text); break;
                case EventKind.Heart:
                    if (text == "scream" && earshot) Log("<color=#c48cff>The plague heart screams. More of them are coming.</color>");
                    else if (text != "hit" && text != "pulse" && text != "scream" && text.Length > 0 && near) Log(text);
                    break;
                case EventKind.HeartDestroyed:
                    {
                        int left = w.HeartsRemaining;
                        Banner("PLAGUE HEART DESTROYED", left > 0 ? left + " heart" + (left == 1 ? "" : "s") + " remain in the county." : "The county is clean.", UIKit.Plague, 5);
                        KillFeed((actor != null ? UiData.DisplayName(actor) : "Someone") + " destroyed a plague heart");
                        break;
                    }
                case EventKind.Plague:
                    if (!(mine || member)) break;
                    if (text == "infected") Banner(mine ? "BLOOD PLAGUE" : actor.name.ToUpperInvariant() + " HAS BLOOD PLAGUE", "Find or make a plague cure before it runs its course.", UIKit.Plague, 5);
                    else if (text == "cured") { Toast((mine ? "You are" : actor.name + " is") + " cured of the blood plague."); Log("<color=#9fe08a>" + (actor == null ? "Cured" : actor.name + " is cured of the blood plague.") + "</color>"); }
                    else if (text == "turned") Log("<color=#c48cff>" + (actor == null ? "Someone" : actor.name) + " turned.</color>");
                    else if (text.Length > 0) Log(text);
                    break;

                // World events.
                case EventKind.Airdrop:
                    {
                        string where = Mathf.RoundToInt(dist) + " m " + UiCompass.Bearing(new Vector2(e.pos.x - me.pos.x, e.pos.y - me.pos.y));
                        if (text == "announce") { Banner("SUPPLY DROP INCOMING", "Guard plane inbound · " + where, UIKit.Warn, 5); Radio("Guard air control: supply drop inbound, " + where + " of you. Expect company."); }
                        else if (text == "plane") Toast("A plane drones overhead.");
                        else if (text == "land") { Banner("SUPPLY DROP LANDED", "Red smoke · " + where, UIKit.Bad, 5); Log("<color=#ff9a80>A supply crate landed " + where + ".</color>"); }
                        else if (text == "open") { if (mine) Log("You open the supply crate."); else if (earshot) Log((actor != null ? UiData.DisplayName(actor) : "Someone") + " opened the supply crate."); }
                        break;
                    }
                case EventKind.Artillery:
                    if (text == "warn") { Banner("ARTILLERY WARNING", "The Guard will shell a red zone. Check the map and get clear.", UIKit.Bad, 6); Radio("Guard artillery: cleansing fire ordered. Clear the marked zone."); }
                    else if (text == "clear") Toast("The shelling has stopped.");
                    break;
                case EventKind.Siege:
                    if (text == "start") { Banner("BASE UNDER SIEGE", "A horde is at your walls. Get home or hold the line.", UIKit.Bad, 6); Log("<color=#ff6050>Your base is under siege!</color>"); }
                    else if (text == "end") { Toast("The siege is broken."); Log("<color=#9fe08a>The siege is over.</color>"); }
                    break;

                // Community.
                case EventKind.Recruit:
                    if (member || mine) { string who = actor != null && !mine ? actor.name : Or(text, "A survivor"); Banner("NEW MEMBER", who + " joined your community.", UIKit.Blue, 4); Log("<color=#8fb8ff>" + who + " joined the community.</color>"); }
                    break;
                case EventKind.Facility:
                    {
                        string name = e.zombie >= 0 && e.zombie < UiData.Facilities.Length ? UiData.FacilityName((FacilityKind)e.zombie) : "Facility";
                        if (text == "start") Toast("Construction started: " + name);
                        else if (text == "built") { Banner("FACILITY BUILT", name + " is ready.", UIKit.Good, 4); Log("<color=#9fe08a>" + name + " built.</color>"); }
                        else if (text == "upgraded") { Banner("FACILITY UPGRADED", name, UIKit.Good, 4); Log("<color=#9fe08a>" + name + " upgraded.</color>"); }
                        else if (text.Length > 0) Log(text);
                        RefreshOpenCommunity();
                        break;
                    }
                case EventKind.Mission:
                    {
                        var m = FindMission(w, e.zombie);
                        string title = m != null && m.title.Length > 0 ? m.title : "Mission";
                        if (text == "offered") { Toast("New mission: " + title + "  [J]"); Log("<color=#f0b050>Mission offered: " + title + "</color>"); }
                        else if (text == "accepted") Toast("Mission accepted: " + title);
                        else if (text == "completed") Banner("MISSION COMPLETE", title + (m != null && m.rewardInfluence > 0 ? " · +" + m.rewardInfluence + " influence" : ""), UIKit.Good, 5);
                        else if (text == "failed") Banner("MISSION FAILED", title, UIKit.Bad, 4);
                        else if (text.Length > 0) Log(text);
                        RefreshOpenMissions();
                        break;
                    }
                case EventKind.Morale:
                    if (text == "up") Toast("Morale is rising.");
                    else if (text == "down") { Toast("Morale is falling."); Log("<color=#ffb070>Community morale dropped.</color>"); }
                    else if (text.Length > 0) Log(text);
                    break;
                case EventKind.Influence: if (text.Length > 0) Toast((text.StartsWith("-") || text.StartsWith("+") ? text : "+" + text) + " influence"); break;
                case EventKind.Switch: break;   // AshfallGame notices control changes (ControlChanged) whatever their cause
                case EventKind.Talk: if (mine && text.Length > 0) { TalkLine(text); } break;
                case EventKind.Trade: if (mine && text.Length > 0) { Log(text); TalkLine(text); } break;
                case EventKind.Legacy: Banner("LEGACY", Or(text, "Ashfall County is cleansed."), UIKit.Good, 6); break;
            }
        }

        static string Or(string text, string fallback) { return string.IsNullOrEmpty(text) ? fallback : text; }
        static Mission FindMission(World w, int id) { if (id < 0) return null; foreach (var m in w.community.missions) if (m.id == id) return m; return null; }

        /// <summary>Adds a broadcast to the radio log (Missions panel) and the HUD log, without repeating the latest line.</summary>
        void Radio(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var radio = game.Radio; string line = game.World.Clock + " — " + text;
            if (radio.Count > 0 && radio[radio.Count - 1].EndsWith(text, StringComparison.Ordinal)) return;
            radio.Add(line); if (radio.Count > 200) radio.RemoveAt(0);
            Log("<color=#9fd3ff>[Radio] " + text + "</color>");
        }
    }
}
