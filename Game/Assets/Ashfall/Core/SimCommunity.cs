using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>
    /// Host rules for the player's community (State of Decay style): daily upkeep at 06:00, morale and influence,
    /// facilities and their construction, rucksack deposits, workshop crafting, switching between members, the
    /// companion, recruiting, outposts and the "dead" outcome. Missions live in CommunityMissions.cs.
    /// </summary>
    public sealed partial class Simulation
    {
        float communityClock, communityHours;

        // ---------------------------------------------------------------- balance
        public const float DepositInfluence = 10, OutpostCost = 100, RecruitCost = 100, RecruitStanding = 25;
        public const float DeathMorale = -12, HeartMorale = 8, HeartInfluence = 50, LowMorale = 15;
        static readonly float[] BaseMaterials = { 15, 10, 20, 15, 15, 10, 15, 20 };   // by FacilityKind, × level
        static readonly float[] BaseHours = { 3, 2, 4, 3, 3, 2, 3, 4 };               // in-game hours, × level

        /// <summary>Daily upkeep (consumption, production, morale), construction, missions and base sieges.</summary>
        void UpdateCommunity(float dt, float hours)
        {
            var w = world; var c = w.community;
            CheckLegacy();   // Community.cs: outcome "legacy" when every heart is destroyed
            if (c.members.Count == 0) return;
            foreach (var f in c.facilities)
                if (f.buildHours > 0) { f.buildHours -= hours; if (f.buildHours <= 0) FinishFacility(f); }
            if (w.HourOfDay >= 6 && w.Day > c.lastUpkeepDay) { c.lastUpkeepDay = w.Day; DailyUpkeep(); }
            communityHours += hours;
            for (int i = 0; i < 4 && communityHours >= 1; i++) { communityHours -= 1; HourlyCommunity(); }
            // Frequent checks: every half second, or every tick while time races.
            communityClock -= dt;
            if (communityClock <= 0 || hours > .05f)
            {
                communityClock = .5f;
                CheckHearts(); InfirmaryCures(); UpdateMissions();
            }
        }

        // ---------------------------------------------------------------- queries for the UI
        /// <summary>Facility slots at the home base (by building size: its room count, 3..7). Storage and Beds do not use one.</summary>
        public int BaseSlots { get { var b = world.BaseBuilding; return b == null ? 3 : MathUtil.Clamp(b.rooms.Count, 3, 7); } }
        public int UsedSlots { get { int n = 0; foreach (var f in world.community.facilities) if (!Community.BuiltIn(f.kind)) n++; return n; } }
        /// <summary>Materials and in-game hours to build (level 1) or upgrade to a level.</summary>
        public static float FacilityMaterials(FacilityKind kind, int level) { return BaseMaterials[(int)kind] * Math.Max(1, level); }
        public static float FacilityHours(FacilityKind kind, int level) { return BaseHours[(int)kind] * Math.Max(1, level); }
        /// <summary>Most outposts the community can hold: 2, +1 with a Radio Room, +1 for a large base (6+ rooms); at most 4.</summary>
        public int MaxOutposts { get { return Math.Min(4, 2 + (world.community.Level(FacilityKind.RadioRoom) > 0 ? 1 : 0) + (BaseSlots >= 6 ? 1 : 0)); } }
        public Facility UnderConstruction { get { foreach (var f in world.community.facilities) if (f.Busy) return f; return null; } }

        public bool AtBase(Survivor s)
        {
            var c = world.community; if (s == null || c.baseBuilding < 0) return false;
            var b = world.BaseBuilding; return b != null && RectDistance(b.rect, s.pos) < 8 || Vec2.Distance(s.pos, c.basePos) < 24;
        }
        static float RectDistance(Rect r, Vec2 p)
        {
            float dx = Math.Max(Math.Max(r.x - p.x, 0), p.x - r.MaxX), dy = Math.Max(Math.Max(r.y - p.y, 0), p.y - r.MaxY);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public bool CanBuild(FacilityKind kind, out string reason)
        {
            var c = world.community; reason = "";
            if ((int)kind < 0 || (int)kind >= BaseMaterials.Length) { reason = "Unknown facility."; return false; }
            if (c.baseBuilding < 0) { reason = "The community has no home base."; return false; }
            if (c.Get(kind) != null) { reason = Community.FacilityName(kind) + " is already built. Upgrade it instead."; return false; }
            if (UnderConstruction != null) { reason = "Finish building the " + Community.FacilityName(UnderConstruction.kind) + " first."; return false; }
            if (!Community.BuiltIn(kind) && UsedSlots >= BaseSlots) { reason = "No free facility slot (" + UsedSlots + "/" + BaseSlots + "). A bigger base has more."; return false; }
            float cost = FacilityMaterials(kind, 1);
            if (c.Stock(Resource.Materials) < cost) { reason = "Needs " + cost + " Materials (" + (int)c.Stock(Resource.Materials) + " in stock)."; return false; }
            return true;
        }
        public bool CanUpgrade(FacilityKind kind, out string reason)
        {
            var c = world.community; var f = c.Get(kind); reason = "";
            if (f == null) { reason = Community.FacilityName(kind) + " is not built yet."; return false; }
            if (f.level >= Community.MaxLevel) { reason = Community.FacilityName(kind) + " is at the top level."; return false; }
            if (UnderConstruction != null) { reason = "Finish building the " + Community.FacilityName(UnderConstruction.kind) + " first."; return false; }
            float cost = FacilityMaterials(kind, f.level + 1);
            if (c.Stock(Resource.Materials) < cost) { reason = "Needs " + cost + " Materials (" + (int)c.Stock(Resource.Materials) + " in stock)."; return false; }
            return true;
        }

        public bool CanSwitchTo(int survivor, out string reason)
        {
            var c = world.community; var t = Survivor(survivor); reason = "";
            if (t == null || !t.alive) { reason = "They are dead."; return false; }
            if (t.faction != 0 || !c.members.Contains(t.id)) { reason = t.name + " is not in your community."; return false; }
            if (t.id == world.playerId) { reason = "You are already " + t.name + "."; return false; }
            var cur = world.Controlled;
            if (cur != null && cur.alive && !AtBase(cur)) { reason = "Return to base to switch characters."; return false; }
            if (!AtBase(t)) { reason = t.name + " is away from the base."; return false; }
            if (t.vehicle >= 0) { reason = t.name + " is driving."; return false; }
            return true;
        }

        public bool CanRecruit(Survivor s, Survivor target, out string reason)
        {
            var c = world.community; reason = "";
            if (target == null || !target.alive) { reason = "They are dead."; return false; }
            if (target.faction == 0) { reason = target.name + " is already one of yours."; return false; }
            if (world.Hostile(s, target)) { reason = target.name + " is hostile."; return false; }
            var def = string.IsNullOrEmpty(target.character) ? null : Characters.Get(target.character);
            if (def != null && !def.recruitable) { reason = target.name + " won't leave their people."; return false; }
            bool willing = def != null || target.ai == AiRole.Enclave || target.ai == AiRole.Trader || IsWanderer(target);
            if (!willing) { reason = target.name + " won't join you."; return false; }
            if (Vec2.Distance(s.pos, target.pos) > 3) { reason = "Get closer to " + target.name + "."; return false; }
            var f = world.FactionOf(target); float standing = f == null ? 0 : f.standing;
            if (standing < RecruitStanding && !Rescued(target)) { reason = target.name + " doesn't trust you enough yet (standing " + (int)standing + "/" + (int)RecruitStanding + "). Help their people first."; return false; }
            float cost = RecruitInfluence(target);
            if (c.influence < cost) { reason = "Recruiting " + target.name + " takes " + cost + " influence (you have " + (int)c.influence + ")."; return false; }
            if (world.LivingMembers().Count >= c.BedCapacity) { reason = "No free bed (" + c.BedCapacity + "). Upgrade Beds."; return false; }
            return true;
        }
        public float RecruitInfluence(Survivor target)
        {
            var def = target == null || string.IsNullOrEmpty(target.character) ? null : Characters.Get(target.character);
            return def != null ? def.recruitInfluence : RecruitCost;
        }
        bool Rescued(Survivor t) { foreach (var m in world.community.missions) if (m.kind == MissionKind.Rescue && m.targetId == t.id && m.status == MissionStatus.Completed) return true; return false; }
        bool IsWanderer(Survivor t) { var f = world.FactionOf(t); return f != null ? f.name == WanderersName : t.faction < 0; }

        public bool CanClaimOutpost(Survivor s, Building b, out string reason)
        {
            var c = world.community; reason = "";
            if (b == null) { reason = "Stand at a building to claim it."; return false; }
            if (b.id == c.baseBuilding) { reason = "That is your home base."; return false; }
            if (c.OutpostAt(b.id) != null) { reason = (b.name.Length > 0 ? b.name : "This building") + " is already your outpost."; return false; }
            if (!b.visited) { reason = "Search the building first."; return false; }
            if (RectDistance(b.rect, s.pos) > 12) { reason = "Get closer to the building."; return false; }
            world.index.Query(b.rect.Center, 25 + Math.Max(b.rect.w, b.rect.h) * .5f, query);
            if (query.Count > 0) { reason = "Clear the zombies around it first (" + query.Count + " nearby)."; return false; }
            if (c.outposts.Count >= MaxOutposts) { reason = "You can hold " + MaxOutposts + " outposts. Abandon one or build a Radio Room."; return false; }
            if (c.influence < OutpostCost) { reason = "Claiming an outpost takes " + OutpostCost + " influence (you have " + (int)c.influence + ")."; return false; }
            return true;
        }

        /// <summary>Returns "" when the recipe can be crafted now, otherwise why not.</summary>
        public string WorkshopProblem(Survivor s, string recipe)
        {
            var c = world.community; var r = WorkshopRecipe.Get(recipe);
            if (r == null) return "Unknown recipe.";
            if (!AtBase(s)) return "Craft at the home base.";
            if (c.Level(r.facility) < r.level) return "Needs a " + r.FacilityLabel + ".";
            for (int i = 0; i < 5; i++) if (c.stock[i] < r.cost[i]) return "Not enough " + (Resource)i + " (needs " + r.cost[i] + ").";
            foreach (var i in r.items) if (s.inventory.Count(i.item) < i.count) return "Needs " + (ItemCatalog.Exists(i.item) ? i.Label : i.item) + ".";
            if (r.result.Length > 0 && !ItemCatalog.Exists(r.result)) return r.name + " is not available yet.";
            if (r.id == "repair_weapon" && !s.inventory.items.Any(i => i.Def.IsWeapon && i.Def.category != ItemCategory.Firearm && i.condition < .99f)) return "Nothing needs repair.";
            if (r.id == "draw_ammo" && !ItemCatalog.Exists(AmmoFor(s))) return "No ammunition fits.";
            return "";
        }
        string AmmoFor(Survivor s)
        {
            var gun = s.Primary != null && s.Primary.Def.category == ItemCategory.Firearm ? s.Primary : s.inventory.items.FirstOrDefault(i => i.Def.category == ItemCategory.Firearm);
            return gun != null ? gun.Def.ammo : "ammo_9mm";
        }

        // ---------------------------------------------------------------- commands
        /// <summary>Community commands (switch, recruit, build, outposts, rucksacks, missions). Return true when handled.</summary>
        bool ExecuteCommunity(Survivor s, Command c)
        {
            switch (c.kind)
            {
                case CommandKind.SwitchCharacter: case CommandKind.Recruit: case CommandKind.BuildFacility: case CommandKind.UpgradeFacility:
                case CommandKind.ClaimOutpost: case CommandKind.AbandonOutpost: case CommandKind.DepositRucksack: case CommandKind.AcceptMission:
                case CommandKind.AbandonMission: case CommandKind.Rest: case CommandKind.CraftAtWorkshop: case CommandKind.SetCompanion: break;
                default: return false;
            }
            if (s.faction != 0) return true;   // only the community issues community commands
            string why;
            switch (c.kind)
            {
                case CommandKind.SwitchCharacter:
                    if (s.id != world.playerId) return true;
                    if (!CanSwitchTo(c.target, out why)) { world.Message(s, why); return true; }
                    var to = Survivor(c.target); SwitchTo(to, "You take over as " + to.name + ".");
                    return true;
                case CommandKind.Recruit:
                    {
                        var t = Survivor(c.target);
                        if (!CanRecruit(s, t, out why)) { world.Message(s, why); return true; }
                        float cost = RecruitInfluence(t); AddInfluence(-cost);
                        JoinCommunity(t, s);
                        return true;
                    }
                case CommandKind.BuildFacility: case CommandKind.UpgradeFacility:
                    {
                        var kind = (FacilityKind)Math.Max(0, Math.Min(c.target, BaseMaterials.Length - 1));
                        if (c.target < 0 || c.target >= BaseMaterials.Length) { world.Message(s, "Unknown facility."); return true; }
                        if (!AtBase(s)) { world.Message(s, "Build facilities at the home base."); return true; }
                        bool upgrade = c.kind == CommandKind.UpgradeFacility;
                        if (!(upgrade ? CanUpgrade(kind, out why) : CanBuild(kind, out why))) { world.Message(s, why); return true; }
                        StartFacility(kind, upgrade);
                        return true;
                    }
                case CommandKind.ClaimOutpost:
                    {
                        var b = c.target >= 0 && c.target < world.map.buildings.Count ? world.map.buildings[c.target] : world.map.BuildingAt(s.pos);
                        if (!CanClaimOutpost(s, b, out why)) { world.Message(s, why); return true; }
                        ClaimOutpost(b);
                        return true;
                    }
                case CommandKind.AbandonOutpost:
                    {
                        var o = world.community.OutpostAt(c.target); if (o == null) { world.Message(s, "That is not one of your outposts."); return true; }
                        AbandonOutpost(o); return true;
                    }
                case CommandKind.DepositRucksack: DepositRucksacks(s, c.item); return true;
                case CommandKind.AcceptMission: AcceptMission(s, c.target); return true;
                case CommandKind.AbandonMission: AbandonMission(s, c.target); return true;
                case CommandKind.Rest: Rest(s); return true;
                case CommandKind.CraftAtWorkshop: CraftAtWorkshop(s, c.recipe); return true;
                case CommandKind.SetCompanion: SetCompanion(s, c.target); return true;
            }
            return true;
        }

        void Rest(Survivor s)
        {
            if (!AtBase(s)) { world.Message(s, "Rest at the home base (or sleep anywhere when you're tired)."); return; }
            if (s.Bleeding) { world.Message(s, "Stop the bleeding first."); return; }
            if (s.fatigue < 5 && s.health >= s.MaxHealth - 1 && s.injury <= 0) { world.Message(s, "You're already rested."); return; }
            world.index.Query(s.pos, 14, query);
            if (query.Any(z => z.state == ZombieState.Chase || z.state == ZombieState.Thump || z.state == ZombieState.Attack)) { world.Message(s, "You can't rest with zombies at the door."); return; }
            Prop bed = null; float best = 99;
            foreach (var f in world.community.facilities)
            {
                var p = f.prop >= 0 && f.prop < world.map.props.Count ? world.map.props[f.prop] : null;
                if (p != null && !p.removed && (p.kind == PropKind.Bunk || p.kind == PropKind.MedicalBed) && Vec2.Distance(p.Center, s.pos) < best) { best = Vec2.Distance(p.Center, s.pos); bed = p; }
            }
            CancelAction(s, "");
            s.sleeping = true; s.action = new TimedAction { kind = ActionKind.Sleep, duration = 1e9f, target = bed == null ? -1 : bed.id, extra = 1 };
            world.Message(s, "You rest at the base. Time passes quickly.");
        }

        void SetCompanion(Survivor s, int target)
        {
            var c = world.community;
            var old = Survivor(c.companion);
            if (target < 0)
            {
                if (old == null) { world.Message(s, "Nobody is following you."); return; }
                Dismiss(old); world.Message(s, old.name + " heads back to base duty."); return;
            }
            var t = Survivor(target);
            if (t == null || !t.alive || t.faction != 0 || !c.members.Contains(t.id)) { world.Message(s, "Only a living community member can come along."); return; }
            if (t.id == world.playerId || t == s) { world.Message(s, "You can't follow yourself."); return; }
            if (Vec2.Distance(s.pos, t.pos) > 8 && !(AtBase(s) && AtBase(t))) { world.Message(s, "Find " + t.name + " first (at base or close by)."); return; }
            if (old != null && old != t) Dismiss(old);
            if (c.garrison == t.id) c.garrison = -1;
            t.ai = AiRole.Companion; t.leader = world.playerId; t.order = CompanionOrder.Follow; t.sleeping = false; if (t.action != null && t.action.kind == ActionKind.Sleep) t.action = null;
            c.companion = t.id;
            world.Message(s, t.name + " will come with you.");
        }
        void Dismiss(Survivor t)
        {
            var c = world.community;
            if (c.companion == t.id) c.companion = -1;
            if (t.alive && t.faction == 0 && t.id != world.playerId) { t.ai = AiRole.Resident; t.leader = -1; }
        }

        /// <summary>Hands control to another member. The old one becomes a resident; the companion follows the new one.</summary>
        void SwitchTo(Survivor t, string why)
        {
            var c = world.community; var old = world.Controlled;
            if (old != null && old != t && old.alive) { old.ai = AiRole.Resident; old.leader = -1; inputs[old.id] = new InputState { face = old.facing }; }
            if (t.sleeping) Wake(t, "");
            inputs.Remove(t.id);
            t.ai = AiRole.None; t.leader = -1;
            world.playerId = t.id;
            if (c.companion == t.id) c.companion = -1;
            if (c.garrison == t.id) c.garrison = -1;
            var comp = Survivor(c.companion); if (comp != null && comp.alive) comp.leader = t.id; else c.companion = -1;
            world.Emit(EventKind.Switch, t.pos, t.id, t.name);
            if (why.Length > 0) world.Message(t, why);
        }

        void JoinCommunity(Survivor t, Survivor by)
        {
            var c = world.community;
            t.faction = 0; t.ai = AiRole.Resident; t.squad = -1; t.leader = -1; t.disposition = Disposition.Friendly; t.offscreen = false;
            if (!c.members.Contains(t.id)) c.members.Add(t.id);
            world.Emit(EventKind.Recruit, t.pos, t.id, t.name);
            world.Emit(EventKind.KillFeed, t.pos, t.id, t.name + " joined your community");
            ChangeMorale(3);
            var who = by ?? world.Controlled; if (who != null) world.Message(who, t.name + " joins the community and heads for the base.");
        }

        /// <summary>Morale, member loss, switching to another member, and the "dead" outcome when nobody is left.</summary>
        void CommunityOnDeath(Survivor s)
        {
            var c = world.community;
            bool member = s.faction == 0 && c.members.Contains(s.id);
            if (member)
            {
                ChangeMorale(DeathMorale);
                if (c.companion == s.id) c.companion = -1;
                if (c.garrison == s.id) c.garrison = -1;
                var lead = world.Controlled; if (lead != null && lead.alive && lead != s) world.Message(lead, s.name + " is dead. The community mourns.");
            }
            if (s.id == world.playerId)
            {
                // Control passes to the companion, else the healthiest living member.
                Survivor next = Survivor(c.companion);
                if (next == null || !next.alive || next.faction != 0)
                {
                    next = null; float best = float.MinValue;
                    foreach (var m in world.LivingMembers()) { float score = m.health - m.plague * .5f - m.injury * .2f; if (m != s && score > best) { best = score; next = m; } }
                }
                if (next != null) { SwitchTo(next, s.name + " is gone. You are " + next.name + " now."); return; }
            }
            if (member && world.LivingMembers().Count == 0 && world.outcome == "") world.outcome = "dead";
        }

        /// <summary>Influence for killing a freak (Bestiary.Get(kind).influence). Bestiary's KillZombie should call this.</summary>
        public void CommunityOnZombieKilled(Zombie z, Survivor by)
        {
            if (z == null || by == null || by.faction != 0) return;
            var def = Bestiary.Get(z.kind);
            if (def.influence > 0) AddInfluence(def.influence);
            if (def.threat >= 3) ChangeMorale(2);
        }

        // ---------------------------------------------------------------- morale and influence
        public void ChangeMorale(float delta)
        {
            var c = world.community; float before = c.morale;
            c.morale = MathUtil.Clamp(c.morale + delta, 0, 100);
            float moved = c.morale - before; if (Math.Abs(moved) < .5f) return;
            var p = world.Controlled;
            world.Emit(EventKind.Morale, p == null ? c.basePos : p.pos, p == null ? -1 : p.id, moved > 0 ? "up" : "down");
        }
        public void AddInfluence(float amount)
        {
            var c = world.community; if (amount == 0) return;
            c.influence = Math.Max(0, c.influence + amount);
            var p = world.Controlled;
            world.Emit(EventKind.Influence, p == null ? c.basePos : p.pos, p == null ? -1 : p.id, (amount > 0 ? "+" : "") + (int)Math.Round(amount));
        }
        void AddStock(Resource r, float amount)
        {
            var c = world.community; c.stock[(int)r] = MathUtil.Clamp(c.stock[(int)r] + amount, 0, c.StorageCap);
        }

        // ---------------------------------------------------------------- daily upkeep (06:00)
        void DailyUpkeep()
        {
            var w = world; var c = w.community; var living = w.LivingMembers();
            if (living.Count == 0) return;
            var report = new List<string>();
            // Production: the Garden, gardeners and anglers at base, and outposts.
            float food = c.Level(FacilityKind.Garden) * 2;
            foreach (var m in living) if (AtBase(m)) { if (m.HasTrait("green_thumb") && c.Level(FacilityKind.Garden) > 0) food += 1; if (m.HasTrait("angler")) food += 1; }
            if (food > 0) { AddStock(Resource.Food, food); report.Add("+" + food + " Food grown"); }
            foreach (var o in c.outposts) AddStock(o.bonus, o.amount);
            if (c.outposts.Count > 0) report.Add(c.outposts.Count + " outpost" + (c.outposts.Count > 1 ? "s" : "") + " delivered");
            // Consumption: Food for everyone, Meds for the injured and plagued, Fuel for the watchtower and generator.
            float eat = 0; foreach (var m in living) eat += Trait.Upkeep(m);
            float ate = Math.Min(eat, c.Stock(Resource.Food)); c.stock[(int)Resource.Food] -= ate;
            bool hungry = ate < eat - .01f;
            int patients = living.Count(m => m.injury > 10 || m.plagueInfected);
            float meds = Math.Min(patients, c.Stock(Resource.Meds)); c.stock[(int)Resource.Meds] -= meds;
            foreach (var m in living.Where(m => m.injury > 10).Take((int)meds)) m.injury = Math.Max(0, m.injury - 10);
            int fuelNeed = (c.Level(FacilityKind.Watchtower) > 0 ? 1 : 0) + (c.generatorProp >= 0 ? 1 : 0);
            float fuel = Math.Min(fuelNeed, c.Stock(Resource.Fuel)); c.stock[(int)Resource.Fuel] -= fuel;
            c.powered = fuel >= fuelNeed - .01f;
            // Morale drifts toward a target set by facilities and traits; shortfalls hurt on top.
            float target = 50 + Facilities(c) * 1.5f + (hungry ? 0 : c.Level(FacilityKind.Kitchen) * 6) + (c.powered ? 0 : -5);
            foreach (var m in living) target += Trait.Morale(m);
            float delta = (MathUtil.Clamp(target, 10, 95) - c.morale) * .3f;
            if (hungry) { delta -= 12 * (1 - ate / Math.Max(1, eat)); foreach (var m in living) m.fatigue = Math.Min(100, m.fatigue + 15); report.Add("not enough Food"); }
            if (meds < patients) { delta -= 4; report.Add("short of Meds"); }
            if (!c.powered) { delta -= 3; report.Add("out of Fuel"); }
            ChangeMorale(delta);
            for (int i = 0; i < 5; i++) c.stock[i] = MathUtil.Clamp(c.stock[i], 0, c.StorageCap);
            // Very low morale: someone walks out.
            if (c.morale < LowMorale && living.Count > 1 && w.rng.Chance(.4f))
            {
                var leaving = living.Where(m => m.id != w.playerId && m.id != c.companion).OrderBy(m => w.rng.Float()).FirstOrDefault();
                if (leaving != null) Leave(leaving);
            }
            // Radio Room: scouts locate the nearest unknown plague heart.
            if (c.Level(FacilityKind.RadioRoom) > 0)
            {
                PlagueHeart near = null; float best = float.MaxValue;
                foreach (var h in w.hearts) if (!h.destroyed && !h.discovered && Vec2.Distance(h.pos, c.basePos) < best) { best = Vec2.Distance(h.pos, c.basePos); near = h; }
                if (near != null) { near.discovered = true; report.Add("the radio room located a plague heart " + (int)best + " m away"); }
            }
            AssignGarrison();
            string summary = "Day " + w.Day + " report: " + w.LivingMembers().Count + " members, Food " + (int)c.Stock(Resource.Food) + ", Meds " + (int)c.Stock(Resource.Meds) + ", Ammo " + (int)c.Stock(Resource.Ammo) +
                ", Materials " + (int)c.Stock(Resource.Materials) + ", Fuel " + (int)c.Stock(Resource.Fuel) + ". Morale " + (int)c.morale + ", influence " + (int)c.influence + "." + (report.Count > 0 ? " " + string.Join("; ", report) + "." : "");
            c.lastReport = summary; Radio(summary);
        }
        static int Facilities(Community c) { int n = 0; foreach (var f in c.facilities) if (!Community.BuiltIn(f.kind) && f.Active > 0) n++; return n; }

        void Leave(Survivor m)
        {
            var c = world.community;
            c.members.Remove(m.id); if (c.garrison == m.id) c.garrison = -1; if (c.companion == m.id) c.companion = -1;
            m.faction = WanderersFaction(); m.ai = AiRole.Enclave; m.leader = -1;
            world.Emit(EventKind.KillFeed, m.pos, m.id, m.name + " left your community");
            var lead = world.Controlled; if (lead != null) world.Message(lead, m.name + " has had enough of this place and leaves the community.");
            ChangeMorale(-3);
        }

        const string WanderersName = "Wanderers";
        /// <summary>The loose faction of lone survivors (rescue targets, members who left). Created on first use if Humans.cs has none.</summary>
        int WanderersFaction()
        {
            var fs = world.factions;
            foreach (var f in fs) if (f.name == WanderersName) return f.id;
            if (fs.Count == 0) return -1;
            var w = new Faction { id = fs.Count, name = WanderersName, description = "Lone survivors drifting through the county.", role = AiRole.Enclave, disposition = Disposition.Friendly, standing = RecruitStanding };
            fs.Add(w); return w.id;
        }

        /// <summary>Hourly: members simulated abstractly (offscreen) still rest, heal and fight the plague.</summary>
        void HourlyCommunity()
        {
            foreach (var m in world.LivingMembers())
            {
                if (!m.offscreen) continue;
                bool atBase = AtBase(m);
                if (atBase) m.fatigue = Math.Max(0, m.fatigue - 4); else m.fatigue = Math.Min(100, m.fatigue + 2.2f);
                Recover(m, 1, atBase);
                Plague(m, 1);
            }
        }

        /// <summary>A plagued member at base with an Infirmary is treated with Meds.</summary>
        void InfirmaryCures()
        {
            var c = world.community; if (c.Level(FacilityKind.Infirmary) <= 0) return;
            foreach (int id in c.members)
            {
                var m = Survivor(id); if (m == null || !m.alive || !m.plagueInfected || !AtBase(m)) continue;
                if (c.Stock(Resource.Meds) < InfirmaryPlagueMeds) { if (world.flags.Add("infirmary-meds-" + world.Day + "-" + m.id)) world.Message(m, "The infirmary needs " + InfirmaryPlagueMeds + " Meds to treat the blood plague."); continue; }
                c.stock[(int)Resource.Meds] -= InfirmaryPlagueMeds;
                CurePlague(m, "The infirmary treats " + m.name + "'s blood plague (" + InfirmaryPlagueMeds + " Meds).");
            }
        }
        public const int InfirmaryPlagueMeds = 5;

        /// <summary>Destroyed hearts reward the community (and Story() declares the legacy when none are left).</summary>
        void CheckHearts()
        {
            var w = world; var c = w.community; int destroyed = 0;
            foreach (var h in w.hearts) if (h.destroyed) destroyed++;
            if (destroyed <= c.heartsDestroyed) return;
            int fresh = destroyed - c.heartsDestroyed; c.heartsDestroyed = destroyed;
            AddInfluence(HeartInfluence * fresh); ChangeMorale(HeartMorale * fresh);
            if (w.HeartsRemaining > 0) Radio("Ada: a plague heart is down! " + w.HeartsRemaining + " remain" + (w.HeartsRemaining == 1 ? "s" : "") + ".");
        }

        // ---------------------------------------------------------------- facilities
        void StartFacility(FacilityKind kind, bool upgrade)
        {
            var c = world.community; var f = c.Get(kind);
            int level = upgrade ? f.level + 1 : 1;
            c.stock[(int)Resource.Materials] -= FacilityMaterials(kind, level);
            if (f == null) { f = new Facility { kind = kind, level = 1 }; c.facilities.Add(f); }
            f.upgrading = upgrade; f.buildHours = FacilityHours(kind, level);
            var p = world.Controlled;
            world.Emit(EventKind.Facility, c.basePos, p == null ? -1 : p.id, "start");
            if (p != null) world.Message(p, (upgrade ? "Upgrading the " : "Building a ") + Community.FacilityName(kind) + (upgrade ? " to level " + level : "") + ": " + f.buildHours.ToString("0") + " hours.");
        }

        void FinishFacility(Facility f)
        {
            var c = world.community; f.buildHours = 0;
            string what;
            if (f.upgrading) { f.upgrading = false; f.level++; what = "upgraded"; }
            else what = "built";
            if (f.prop < 0 || f.prop >= world.map.props.Count || world.map.props[f.prop].removed)
            {
                bool inside; var kind = Community.PropFor(f.kind, out inside);
                var p = world.PlaceBaseProp(kind, inside); if (p != null) f.prop = p.id;
            }
            if (f.level >= 2 && c.generatorProp < 0) { var g = world.PlaceBaseProp(PropKind.Generator, false); if (g != null) c.generatorProp = g.id; }
            var at = f.prop >= 0 ? world.map.props[f.prop].Center : c.basePos;
            var lead = world.Controlled;
            world.Emit(EventKind.Facility, at, lead == null ? -1 : lead.id, what);
            if (lead != null) world.Message(lead, Community.FacilityName(f.kind) + (what == "built" ? " is built." : " upgraded to level " + f.level + "."));
            ChangeMorale(3);
            if (f.kind == FacilityKind.Watchtower) AssignGarrison();
        }

        /// <summary>Position residents guard from: beside the watchtower prop.</summary>
        public Vec2 GarrisonPost
        {
            get
            {
                var f = world.community.Get(FacilityKind.Watchtower);
                var p = f != null && f.prop >= 0 && f.prop < world.map.props.Count ? world.map.props[f.prop] : null;
                if (p == null) return world.community.basePos;
                foreach (var d in new[] { new Vec2(0, 1.2f), new Vec2(1.2f, 0), new Vec2(0, -1.2f), new Vec2(-1.2f, 0), new Vec2(1.2f, 1.2f) })
                    if (!world.map.Collides(p.Center + d, Ashfall.Core.Survivor.Radius)) return p.Center + d;
                return p.Center;
            }
        }

        /// <summary>The best-armed resident stands guard at the Watchtower (Humans' resident brain does the shooting).</summary>
        void AssignGarrison()
        {
            var c = world.community; if (c.Level(FacilityKind.Watchtower) <= 0) { c.garrison = -1; return; }
            var g = Survivor(c.garrison);
            if (g != null && g.alive && g.faction == 0 && g.ai == AiRole.Resident) return;
            Survivor best = null; float bestScore = -1;
            foreach (var m in world.LivingMembers())
            {
                if (m.id == world.playerId || m.id == c.companion || m.ai != AiRole.Resident) continue;
                float score = m.Level(Skill.Aiming) * .5f;
                foreach (var i in m.inventory.items) { var d = i.Def; if (d.category == ItemCategory.Firearm) score = Math.Max(score, 10 + d.damage * d.range * .2f + m.Level(Skill.Aiming)); else if (d.IsWeapon) score = Math.Max(score, d.damage * 2); }
                if (score > bestScore) { bestScore = score; best = m; }
            }
            c.garrison = best == null ? -1 : best.id;
            if (best == null) return;
            best.pos = GarrisonPost;
            var gun = best.inventory.items.FirstOrDefault(i => i.Def.category == ItemCategory.Firearm); if (gun != null) best.primary = gun.uid;
            var lead = world.Controlled; if (lead != null) world.Message(lead, best.name + " takes the watchtower.");
        }

        // ---------------------------------------------------------------- rucksacks
        /// <summary>Deposits one carried rucksack (uid), or (-1) every carried rucksack plus those in the trunk of a car parked at base.</summary>
        void DepositRucksacks(Survivor s, int uid)
        {
            if (!AtBase(s)) { world.Message(s, "Bring rucksacks to the home base to deposit them."); return; }
            var c = world.community; var found = new List<KeyValuePair<Inventory, ItemStack>>();
            if (uid >= 0)
            {
                var item = s.inventory.ByUid(uid);
                if (item == null || item.Def.resource < 0) { world.Message(s, "That isn't a rucksack."); return; }
                found.Add(new KeyValuePair<Inventory, ItemStack>(s.inventory, item));
            }
            else
            {
                foreach (var i in s.inventory.items) if (i.Def.resource >= 0) found.Add(new KeyValuePair<Inventory, ItemStack>(s.inventory, i));
                foreach (var trunk in BaseTrunks()) foreach (var i in trunk.items) if (i.Def.resource >= 0) found.Add(new KeyValuePair<Inventory, ItemStack>(trunk, i));
            }
            if (found.Count == 0) { world.Message(s, "No rucksacks to deposit (carry them, or park a car with them in the trunk at base)."); return; }
            int deposited = 0; var full = new HashSet<Resource>(); var gained = new float[5];
            foreach (var kv in found)
            {
                var d = kv.Value.Def; var r = (Resource)d.resource;
                float room = c.StorageCap - c.Stock(r);
                if (room < .5f) { full.Add(r); continue; }
                float amount = Math.Min(room, d.resourceAmount * Math.Max(1, kv.Value.count));
                if (kv.Key == s.inventory && s.IsEquipped(kv.Value.uid)) Unequip(s, kv.Value.uid);
                kv.Key.Take(kv.Value.uid, kv.Value.count);
                c.stock[(int)r] += amount; gained[(int)r] += amount; deposited++;
            }
            if (deposited > 0)
            {
                AddInfluence(DepositInfluence * deposited); ChangeMorale(deposited);
                var parts = new List<string>(); for (int i = 0; i < 5; i++) if (gained[i] > 0) parts.Add("+" + gained[i].ToString("0.#") + " " + (Resource)i);
                world.Message(s, "Deposited " + deposited + " rucksack" + (deposited > 1 ? "s" : "") + ": " + string.Join(", ", parts) + ", +" + (int)(DepositInfluence * deposited) + " influence.");
            }
            if (full.Count > 0) world.Message(s, "Storage is full of " + string.Join(" and ", full) + ". Upgrade Storage or use some first.");
        }

        /// <summary>Trunk inventories of cars parked within about 15 m of the base building.</summary>
        List<Inventory> BaseTrunks()
        {
            var list = new List<Inventory>(); var b = world.BaseBuilding; if (b == null) return list;
            foreach (var v in world.vehicles)
            {
                if (v.driver >= 0 || RectDistance(b.rect, v.pos) > 15) continue;
                var p = v.prop >= 0 && v.prop < world.map.props.Count ? world.map.props[v.prop] : null;
                if (p == null || p.container < 0 || p.container >= world.map.containers.Count) continue;
                var box = world.map.containers[p.container]; if (box.generated) list.Add(box.inventory);
            }
            return list;
        }

        // ---------------------------------------------------------------- workshop
        void CraftAtWorkshop(Survivor s, string id)
        {
            string why = WorkshopProblem(s, id); var r = WorkshopRecipe.Get(id);
            if (why.Length > 0) { world.Message(s, why); return; }
            var c = world.community;
            for (int i = 0; i < 5; i++) c.stock[i] -= r.cost[i];
            foreach (var i in r.items) s.inventory.Consume(i.item, i.count);
            if (r.id == "repair_weapon")
            {
                int n = 0; foreach (var i in s.inventory.items) if (i.Def.IsWeapon && i.Def.category != ItemCategory.Firearm && i.condition < .99f) { i.condition = 1; n++; }
                world.Message(s, "Repaired " + n + " weapon" + (n == 1 ? "" : "s") + " at the workshop.");
            }
            else if (r.id == "draw_ammo")
            {
                string ammo = AmmoFor(s); int rounds = WorkshopRecipe.RoundsPerAmmo(ammo);
                s.inventory.Add(ItemCatalog.Make(ammo, rounds)); world.Message(s, "You draw " + rounds + " " + ItemCatalog.Get(ammo).name + " from the stock.");
            }
            else
            {
                s.inventory.Add(ItemCatalog.Make(r.result, r.resultCount));
                world.Message(s, "Made " + (r.resultCount > 1 ? r.resultCount + " × " : "") + ItemCatalog.Get(r.result).name + ".");
            }
            GainXp(s, r.skill, r.xp);
            world.Emit(EventKind.Craft, s.pos, s.id, r.name);
        }

        // ---------------------------------------------------------------- outposts
        void ClaimOutpost(Building b)
        {
            var c = world.community; float amount;
            var o = new Outpost { building = b.id, bonus = Community.OutpostBonus(b.kind, out amount), amount = amount, pos = b.rect.Center };
            // The flag goes on open ground just outside the building.
            for (int ring = 1; ring <= 3 && o.prop < 0; ring++)
                for (int x = b.rect.x - ring; x < b.rect.MaxX + ring && o.prop < 0; x++)
                    foreach (int y in new[] { b.rect.y - ring, b.rect.MaxY + ring - 1 })
                    {
                        if (!world.map.InBounds(x, y) || world.map.TileBlocked(x, y) || world.map.RoomAt(x, y) != null || NonBlockingPropAt(x, y) != null) continue;
                        if (world.map.H(x, y) != Edge.None || world.map.H(x, y + 1) != Edge.None || world.map.V(x, y) != Edge.None || world.map.V(x + 1, y) != Edge.None) continue;
                        var flag = world.map.AddProp(PropKind.OutpostFlag, x, y); flag.modified = true; o.prop = flag.id; o.pos = flag.Center; break;
                    }
            c.outposts.Add(o);
            AddInfluence(-OutpostCost); ChangeMorale(2);
            var lead = world.Controlled;
            if (lead != null) world.Message(lead, "Outpost claimed: " + (b.name.Length > 0 ? b.name : b.kind.ToString()) + ". It sends " + amount + " " + o.bonus + " a day and keeps the dead from gathering here.");
        }

        void AbandonOutpost(Outpost o)
        {
            var c = world.community; c.outposts.Remove(o);
            if (o.prop >= 0 && o.prop < world.map.props.Count) { var p = world.map.props[o.prop]; world.map.RemoveProp(p); p.modified = true; }
            var lead = world.Controlled; if (lead != null) world.Message(lead, "Outpost abandoned.");
        }
    }
}
