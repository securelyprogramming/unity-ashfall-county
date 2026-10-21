using System.Linq;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.View
{
    /// <summary>Community and people context actions: base deposits, switching, outposts, talking, trading and recruiting.
    /// Each one is a host command (or opens a panel whose buttons are); the simulation re-validates everything.
    /// The Zomboid farming, digging and fishing actions that lived here are retired (the base Garden replaces them).</summary>
    public sealed partial class AshfallGame
    {
        void CommunitySelfActions(Interactable self, Survivor s)
        {
            var c = World.community; bool atBase = Sim.AtBase(s);
            if (atBase)
            {
                int sacks = 0; foreach (var i in s.inventory.items) if (i.Def.resource >= 0) sacks += i.count;
                if (sacks > 0) { Add(self, "Deposit " + sacks + " rucksack" + (sacks == 1 ? "" : "s") + " at base", () => Submit(new Command { kind = CommandKind.DepositRucksack, item = -1, recipe = "" })); self.score = 98; }
                Add(self, "Switch survivor (community)", () => UI.OpenCommunityTab("members"));
                Add(self, "Base facilities", () => UI.OpenCommunityTab("base"));
            }
            // Claim the building you stand in (or face) as an outpost; abandon one you hold.
            var b = World.map.BuildingAt(s.pos) ?? World.map.BuildingAt(s.pos + Vec2.FromAngle(s.facing) * 1.5f);
            if (b != null && b.id != c.baseBuilding)
            {
                var held = c.outposts.FirstOrDefault(o => o.building == b.id);
                string name = string.IsNullOrEmpty(b.name) ? b.kind.ToString() : b.name;
                if (held != null) Add(self, "Abandon outpost: " + name, () => Submit(new Command { kind = CommandKind.AbandonOutpost, target = b.id, recipe = "" }));
                else
                {
                    string why; bool can = Sim.CanClaimOutpost(s, b, out why); int id = b.id;
                    Add(self, "Claim " + name + " as an outpost", () => Submit(new Command { kind = CommandKind.ClaimOutpost, target = id, recipe = "" }), can, why);
                }
            }
        }

        /// <summary>People within arm's reach: NPCs (talk, trade, recruit) and community members (companion, orders, switch).</summary>
        void PeopleInteractions(Survivor s, Vector2 forward)
        {
            foreach (var o in World.survivors)
            {
                if (o == s || !o.alive || o.offscreen || o.inVehicle) continue;
                if (Vec2.Distance(o.pos, s.pos) > 2.6f) continue;
                var it = new Interactable { label = UiData.DisplayName(o) + "  <size=80%><color=#aaa>" + UiData.Subtitle(World, o) + "</color></size>", pos = Pos(o.pos, 1.8f) };
                int id = o.id;
                if (o.faction == 0)
                {
                    bool mate = World.community.companion == o.id;
                    if (mate)
                        foreach (CompanionOrder order in new[] { CompanionOrder.Follow, CompanionOrder.Hold, CompanionOrder.Attack })
                        {
                            var ord = order; Add(it, "Order: " + AshfallUI.OrderName(ord), () => Submit(new Command { kind = CommandKind.CompanionOrder, target = id, slot = (int)ord, recipe = "" }), o.order != ord, "Current order");
                        }
                    Add(it, mate ? "Dismiss companion" : "Bring along as companion", () => Submit(new Command { kind = CommandKind.SetCompanion, target = mate ? -1 : id, recipe = "" }));
                    string why; bool can = Sim.CanSwitchTo(o.id, out why);
                    Add(it, "Switch to " + o.name, () => Submit(new Command { kind = CommandKind.SwitchCharacter, target = id, recipe = "" }), can, why);
                }
                else if (World.Hostile(s, o)) continue;   // no menu for people trying to kill you
                else
                {
                    bool talk = Sim.CanTalk(s, o);
                    Add(it, "Talk", () => UI.OpenTalk(id), talk, "Not willing to talk");
                    if (talk && Sim.TradeOffers(s, o).Count > 0) Add(it, "Trade", () => UI.OpenTalk(id));
                    string why; bool recruit = Sim.CanRecruit(s, o, out why);
                    Add(it, "Recruit", () => Submit(new Command { kind = CommandKind.Recruit, target = id, recipe = "" }), recruit, why);
                }
                Score(it, s, forward); it.score -= .3f; Nearby.Add(it);
            }
        }
    }
}
