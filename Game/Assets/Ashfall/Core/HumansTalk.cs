using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>Talking, trading and companion orders. The UI lists TradeOffers; the host re-validates every Trade command.</summary>
    public sealed partial class Simulation
    {
        /// <summary>One trade line: the NPC sells an item for influence, or buys a rucksack from the player for influence.</summary>
        sealed class Deal { public string label = "", detail = "", item = ""; public int count = 1, price; public bool buysRucksack; public int resource = -1; }

        public const float TalkRange = 2.5f;

        public bool CanTalk(Survivor s, Survivor npc) { return s != null && npc != null && npc.alive && npc != s && npc.ai != AiRole.None && !world.Hostile(s, npc) && Vec2.Distance(s.pos, npc.pos) < TalkRange; }

        /// <summary>What an NPC says when the player talks to them (depends on faction, standing and character).</summary>
        public string Greeting(Survivor s, Survivor npc)
        {
            if (npc == null) return "";
            var f = world.FactionOf(npc); var c = Characters.Get(npc.character);
            if (s != null && world.Hostile(s, npc)) return c != null && c.hostileLine.Length > 0 ? c.hostileLine : "Back off or bleed.";
            float standing = f == null ? 0 : f.standing;
            int pick = world.Day + npc.id;
            string line;
            if (c != null && c.lines.Length > 0) line = c.lines[pick % c.lines.Length];
            else if (f == null) line = npc.name + " eyes you warily.";
            else if (f.enclave) line = Pick(pick, "Welcome to " + f.name + ". Trade's open if you have the influence.", "Keep your voice down. The dead hear everything.", "Our people could use supplies. Rucksacks, especially.");
            else if (f.key == "wanderers") line = Pick(pick, "Please... is there somewhere safe out there?", "I've been alone since the cordon went up.", "Haven't seen a friendly face in days.");
            else if (f.disposition == Disposition.Opportunist) line = Pick(pick, "Nice gear. Long way from home, aren't you?", "We were here first. Keep walking.", "Heard a drop is coming. Maybe we'll see you there.");
            else if (f.disposition == Disposition.Friendly) line = Pick(pick, "Good to see a living face.", "Safe travels, friend.");
            else line = Pick(pick, "Keep your distance and we'll keep ours.", "We don't want trouble.", "This town's picked clean. Try the farms.");
            if (standing >= 50) line += " Good to see you again, friend.";
            else if (standing <= -25) line += " We haven't forgotten what you did.";
            return line;
        }
        static string Pick(int i, params string[] lines) { return lines[Math.Abs(i) % lines.Length]; }

        /// <summary>Trade/talk options; Command.Trade with slot = index executes one.</summary>
        public List<TradeOffer> TradeOffers(Survivor s, Survivor npc)
        {
            var list = new List<TradeOffer>();
            if (s == null || npc == null) return list;
            foreach (var d in Deals(npc))
            {
                string reason = Blocked(s, npc, d);
                list.Add(new TradeOffer { label = d.label, detail = d.detail, enabled = reason.Length == 0, reason = reason });
            }
            return list;
        }

        /// <summary>The fixed list of deals an NPC offers (index = Trade slot). Prices move with standing.</summary>
        List<Deal> Deals(Survivor npc)
        {
            var list = new List<Deal>();
            var f = world.FactionOf(npc); if (f == null || f.id == 0) return list;
            float markup = 1 - MathUtil.Clamp(f.standing, -100, 100) / 400f;   // up to 25% cheaper for friends
            Action<string, int, int> sell = (id, count, price) =>
            {
                if (!ItemCatalog.Exists(id)) return; var def = ItemCatalog.Get(id);
                int p = Math.Max(1, (int)Math.Round(price * markup));
                list.Add(new Deal { label = "Buy " + def.name + (count > 1 ? " ×" + count : ""), detail = p + " influence", item = id, count = count, price = p });
            };
            Action<int, int> buy = (resource, pay) =>
            {
                string what = resource < 0 ? "a rucksack" : "a " + ((Resource)resource).ToString().ToLowerInvariant() + " rucksack";
                list.Add(new Deal { label = "Sell " + what, detail = "+" + pay + " influence", buysRucksack = true, resource = resource, price = pay });
            };
            if (npc.ai == AiRole.Trader || npc.character == "pilgrim")
            {
                // The Pilgrim sells rare tier-3 goods; the stock rotates daily.
                var rare = ItemCatalog.All.Where(d => d.tier >= 3 && d.resource < 0 && d.category != ItemCategory.Ammo).Select(d => d.id).OrderBy(id => id, StringComparer.Ordinal).ToList();
                foreach (var id in new[] { "hunting_rifle", "rifle", "police_vest", "helmet", "first_aid_kit" }) if (rare.Count < 4 && ItemCatalog.Exists(id) && !rare.Contains(id)) rare.Add(id);
                int start = world.Day * 3 % rare.Count;
                for (int i = 0; i < Math.Min(4, rare.Count); i++) sell(rare[(start + i) % rare.Count], 1, 150 + ItemCatalog.Get(rare[(start + i) % rare.Count]).tier * 10);
                buy(-1, 25);
                return list;
            }
            switch (f.key)
            {
                case "radio_station": sell("bandage", 3, 15); sell("ammo_9mm", 30, 25); sell("battery", 4, 10); sell("flashlight", 1, 15); sell("pistol", 1, 80); buy(-1, 30); break;
                case "reeds_garage": sell("gas_can", 1, 20); sell("crowbar", 1, 25); sell("shells", 12, 25); sell("shotgun", 1, 100); buy((int)Resource.Fuel, 45); buy((int)Resource.Materials, 40); buy(-1, 25); break;
                case "hospital": sell("bandage", 4, 12); sell("first_aid_kit", 1, 35); sell("antibiotics", 1, 20); sell("plague_cure", 1, 160); buy((int)Resource.Meds, 45); buy(-1, 25); break;
                default:
                    if (f.enclave) { sell("bandage", 2, 12); buy(-1, 25); }
                    else if (f.disposition != Disposition.Hunter) { sell("ammo_9mm", 15, 20); sell("bandage", 2, 15); }
                    break;
            }
            return list;
        }

        ItemStack RucksackFor(Survivor s, Deal d)
        {
            foreach (var i in s.inventory.items) if (i.Def.resource >= 0 && (d.resource < 0 || i.Def.resource == d.resource)) return i;
            return null;
        }

        /// <summary>Why a deal is unavailable right now ("" = available). Shared by the menu and the host's Trade validation.</summary>
        string Blocked(Survivor s, Survivor npc, Deal d)
        {
            if (!CanTalk(s, npc)) return "Get closer, and don't look hostile.";
            if (s.faction != 0) return "Only community members can trade.";
            var f = world.FactionOf(npc);
            if (f == null || f.standing < -20) return "They won't trade with you.";
            if (d.buysRucksack) return RucksackFor(s, d) == null ? "You carry no " + (d.resource < 0 ? "rucksack" : ((Resource)d.resource).ToString().ToLowerInvariant() + " rucksack") + "." : "";
            if (world.community.influence < d.price) return "Not enough influence (need " + d.price + ").";
            return "";
        }

        /// <summary>Human-specific commands (talk, trade, companion orders, throws). Return true when handled.</summary>
        bool ExecuteHumans(Survivor s, Command c)
        {
            switch (c.kind)
            {
                case CommandKind.Talk:
                    {
                        var npc = Survivor(c.target); if (!CanTalk(s, npc)) return true;
                        world.Emit(EventKind.Talk, npc.pos, s.id, npc.name + ": " + Greeting(s, npc));
                        if (npc.character.Length > 0 && world.flags.Add("met:" + npc.character)) world.ChangeStanding(npc.faction, 2);
                        return true;
                    }
                case CommandKind.Trade:
                    {
                        var npc = Survivor(c.target); if (npc == null) return true;
                        var deals = Deals(npc); if (c.slot < 0 || c.slot >= deals.Count) return true;
                        var d = deals[c.slot]; string reason = Blocked(s, npc, d);
                        if (reason.Length > 0) { world.Message(s, reason); return true; }
                        if (d.buysRucksack)
                        {
                            var sack = RucksackFor(s, d); if (s.IsEquipped(sack.uid)) Unequip(s, sack.uid);
                            var moved = s.inventory.Take(sack.uid, 1); npc.inventory.Add(moved);
                            world.community.influence += d.price; world.ChangeStanding(npc.faction, 4);
                            world.Emit(EventKind.Trade, npc.pos, s.id, "Sold " + moved.Def.name + " to " + world.DisplayName(npc) + ".");
                            world.Emit(EventKind.Influence, s.pos, s.id, "+" + d.price);
                        }
                        else
                        {
                            world.community.influence -= d.price; world.ChangeStanding(npc.faction, 1);
                            var item = ItemCatalog.Make(d.item, d.count);
                            if (item.Def.category == ItemCategory.Firearm) item.loaded = item.Def.magazine;
                            if (s.CarriedWeight + item.Weight > s.Capacity * 1.9f) world.PileAt((int)s.pos.x, (int)s.pos.y, true).inventory.Add(item);
                            else s.inventory.Add(item);
                            world.Emit(EventKind.Trade, npc.pos, s.id, "Bought " + item.Def.name + (d.count > 1 ? " ×" + d.count : "") + " from " + world.DisplayName(npc) + ".");
                            world.Emit(EventKind.Influence, s.pos, s.id, "-" + d.price);
                        }
                        return true;
                    }
                case CommandKind.CompanionOrder:
                    {
                        if (c.slot < 0 || c.slot > (int)CompanionOrder.Attack) return true;
                        var order = (CompanionOrder)c.slot;
                        foreach (var m in world.survivors)
                        {
                            if (!m.alive || m.ai != AiRole.Companion || m.faction != s.faction || m == s) continue;
                            if (!(s.faction == 0 && m.id == world.community.companion || m.leader == s.id)) continue;
                            m.order = order;
                            Brain b; if (brains.TryGetValue(m.id, out b)) { b.hasHold = false; b.hasGoal = false; b.path.Clear(); }
                            world.Emit(EventKind.Talk, m.pos, m.id, m.name + ": " + (order == CompanionOrder.Follow ? "Right behind you." : order == CompanionOrder.Hold ? "Holding here." : "On it. Engaging."));
                        }
                        return true;
                    }
                case CommandKind.Throw: Throw(s, c); return true;   // SimCombat.cs
            }
            return false;
        }
    }
}
