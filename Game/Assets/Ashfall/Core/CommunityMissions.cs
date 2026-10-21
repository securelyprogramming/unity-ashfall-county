using System;
using System.Collections.Generic;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>
    /// Radio missions from named characters, enclaves and the community itself: rescue a survivor, deliver supplies,
    /// clear an infestation, kill a freak, hunt a hostile crew, or defend the base from a siege. Offered every few
    /// in-game hours, they expire, and pay influence, faction standing or a recruit. The host detects completion.
    /// </summary>
    public sealed partial class Simulation
    {
        public const int MaxOffered = 3, MaxActive = 3;
        /// <summary>Hours between mission offers (a Radio Room shortens it and allows one more offer).</summary>
        public float MissionInterval { get { return world.community.Level(FacilityKind.RadioRoom) > 0 ? 3 : 4; } }
        public int MissionOfferLimit { get { return MaxOffered + (world.community.Level(FacilityKind.RadioRoom) > 0 ? 1 : 0); } }

        /// <summary>Missions shown on the board: offered and active first, then the most recent closed ones.</summary>
        public List<Mission> MissionBoard()
        {
            return world.community.missions.OrderBy(m => m.Open ? 0 : 1).ThenBy(m => m.status == MissionStatus.Active ? 0 : 1).ThenByDescending(m => m.offeredAt).ToList();
        }
        /// <summary>Hours until a mission expires (negative once past).</summary>
        public float MissionHoursLeft(Mission m) { return (float)((m.expiresAt - world.minutes) / 60); }

        void UpdateMissions()
        {
            var w = world; var c = w.community;
            if (w.minutes >= c.nextOfferAt)
            {
                c.nextOfferAt = w.minutes + MissionInterval * 60;
                if (c.missions.Count(m => m.status == MissionStatus.Offered) < MissionOfferLimit && c.baseBuilding >= 0) OfferMission();
            }
            for (int i = 0; i < c.missions.Count; i++)
            {
                var m = c.missions[i];
                if (m.status == MissionStatus.Offered && w.minutes > m.expiresAt) { m.status = MissionStatus.Expired; continue; }
                if (m.status != MissionStatus.Active) continue;
                if (w.minutes > m.expiresAt) { FailMission(m, "ran out of time"); continue; }
                CheckMission(m);
            }
            // Keep the list short: the open missions plus the last few closed ones.
            int closed = 0;
            for (int i = c.missions.Count - 1; i >= 0; i--) if (!c.missions[i].Open && ++closed > 10) c.missions.RemoveAt(i);
        }

        void AcceptMission(Survivor s, int id)
        {
            var c = world.community; var m = c.MissionById(id);
            if (m == null || m.status != MissionStatus.Offered) { world.Message(s, "That mission is no longer available."); return; }
            if (c.missions.Count(x => x.status == MissionStatus.Active) >= MaxActive) { world.Message(s, "You already have " + MaxActive + " missions underway."); return; }
            m.status = MissionStatus.Active;
            if (m.kind == MissionKind.Defend) { m.expiresAt = world.minutes + 180; StartSiege(m); }
            else m.expiresAt = Math.Max(m.expiresAt, world.minutes + 360);
            world.Emit(EventKind.Mission, m.target, s.id, "accepted", m.id);
            world.Message(s, "Mission accepted: " + m.title + ".");
        }

        void AbandonMission(Survivor s, int id)
        {
            var m = world.community.MissionById(id);
            if (m == null || !m.Open) { world.Message(s, "That mission is already over."); return; }
            if (m.status == MissionStatus.Offered) { m.status = MissionStatus.Expired; world.Message(s, "You turn down: " + m.title + "."); return; }
            FailMission(m, "abandoned");
        }

        void CheckMission(Mission m)
        {
            var w = world;
            switch (m.kind)
            {
                case MissionKind.Rescue:
                    {
                        var t = Survivor(m.targetId);
                        if (t == null || !t.alive) { FailMission(m, (t == null ? "the survivor" : t.name) + " died"); return; }
                        if (t.faction == 0) { CompleteMission(m); return; }
                        foreach (var s in w.LivingMembers())
                            if (!s.offscreen && Vec2.Distance(s.pos, t.pos) < 3) { JoinCommunity(t, s); CompleteMission(m); return; }
                        m.target = t.pos;
                        return;
                    }
                case MissionKind.Supply:
                    {
                        var s = w.Controlled;
                        if (s != null && s.alive && Vec2.Distance(s.pos, m.target) < 18 && w.community.Stock(m.resource) >= m.amount)
                        {
                            w.community.stock[(int)m.resource] -= m.amount;
                            world.Message(s, "You hand over " + m.amount + " " + m.resource + ".");
                            CompleteMission(m);
                        }
                        return;
                    }
                case MissionKind.ClearInfestation:
                    {
                        var h = w.hearts.FirstOrDefault(x => x.id == m.targetId);
                        if (h == null || h.destroyed) CompleteMission(m);
                        return;
                    }
                case MissionKind.KillFreak:
                    {
                        var z = m.targetId >= 0 && m.targetId < w.zombies.Count ? w.zombies[m.targetId] : null;
                        if (z == null || !z.Alive) CompleteMission(m); else m.target = z.pos;
                        return;
                    }
                case MissionKind.Hunt:
                    {
                        bool any = false;
                        foreach (int id in m.targets) { var t = Survivor(id); if (t != null && t.alive && t.faction != 0) { any = true; m.target = t.pos; } }
                        if (!any) CompleteMission(m);
                        return;
                    }
                case MissionKind.Defend:
                    {
                        int dead = 0; foreach (int id in m.targets) if (id < 0 || id >= w.zombies.Count || !w.zombies[id].Alive) dead++;
                        if (m.targets.Count == 0 || dead >= Math.Ceiling(m.targets.Count * .8f)) { w.Emit(EventKind.Siege, w.community.basePos, w.playerId, "end"); CompleteMission(m); }
                        return;
                    }
            }
        }

        void CompleteMission(Mission m)
        {
            var c = world.community; m.status = MissionStatus.Completed;
            AddInfluence(m.rewardInfluence); ChangeMorale(5);
            var f = m.faction >= 0 && m.faction < world.factions.Count ? world.factions[m.faction] : null;
            if (f != null && f.id != 0) f.standing = Math.Min(100, f.standing + 15);
            if (m.rewardItem.Length > 0 && ItemCatalog.Exists(m.rewardItem) && world.Controlled != null) world.Controlled.inventory.Add(ItemCatalog.Make(m.rewardItem));
            world.Emit(EventKind.Mission, m.target, world.playerId, "completed", m.id);
            Radio((m.giverName.Length > 0 ? m.giverName : "Ada") + ": " + m.title + " - done. " + (m.rewardInfluence > 0 ? "+" + m.rewardInfluence + " influence." : ""));
        }

        void FailMission(Mission m, string why)
        {
            m.status = MissionStatus.Failed; ChangeMorale(-3);
            var f = m.faction >= 0 && m.faction < world.factions.Count ? world.factions[m.faction] : null;
            if (f != null && f.id != 0) f.standing = Math.Max(-100, f.standing - 5);
            if (m.kind == MissionKind.Defend) world.Emit(EventKind.Siege, world.community.basePos, world.playerId, "end");
            world.Emit(EventKind.Mission, m.target, world.playerId, "failed", m.id);
            Radio("Mission failed: " + m.title + " (" + why + ").");
        }

        // ---------------------------------------------------------------- offering
        void OfferMission()
        {
            var kinds = new List<MissionKind> { MissionKind.Rescue, MissionKind.Supply, MissionKind.ClearInfestation, MissionKind.KillFreak, MissionKind.Hunt, MissionKind.Defend };
            for (int i = kinds.Count - 1; i > 0; i--) { int j = world.rng.Range(0, i + 1); var t = kinds[i]; kinds[i] = kinds[j]; kinds[j] = t; }
            foreach (var k in kinds) { var m = MakeMission(k); if (m != null) { PostMission(m); return; } }
        }

        /// <summary>Builds (and posts) a mission of a kind if the county allows one now; null when it can't. Used by tests and events.</summary>
        public Mission OfferMission(MissionKind kind) { var m = MakeMission(kind); if (m != null) PostMission(m); return m; }

        void PostMission(Mission m)
        {
            var c = world.community;
            m.id = c.nextMission++; m.offeredAt = world.minutes;
            c.missions.Add(m);
            world.Emit(EventKind.Mission, m.target, world.playerId, "offered", m.id);
            Radio((m.giverName.Length > 0 ? m.giverName : "Ada Pike") + " on the radio: " + m.description);
        }

        Mission MakeMission(MissionKind kind)
        {
            var w = world; var c = w.community; var r = w.rng;
            string giver, giverName; int faction; Giver(out giver, out giverName, out faction);
            var m = new Mission { kind = kind, giver = giver, giverName = giverName, faction = faction, expiresAt = w.minutes + r.Range(6, 25) * 60 };
            switch (kind)
            {
                case MissionKind.Rescue:
                    {
                        var b = PickBuilding(60, 350); if (b == null) return null;
                        bool visited = b.visited, female = r.Chance(.5f);
                        string name = r.Pick(female ? new[] { "Annie", "Bea", "Clara", "Dot", "Faye", "Gwen", "Ivy", "Kit" } : new[] { "Abe", "Ben", "Curtis", "Duke", "Floyd", "Gus", "Ike", "Lyle" }) + " " + r.Pick(new[] { "Farrow", "Pike", "Dunn", "Holt", "Marsh", "Kerr", "Ruiz", "Stone" });
                        var t = w.AddSurvivor(name, Profession.All[r.Range(0, Profession.All.Length)].id, female, b);
                        b.visited = visited;
                        t.faction = WanderersFaction(); t.ai = AiRole.Enclave;
                        SurroundWithDead(b.rect.Center, r.Range(5, 9));
                        m.targetId = t.id; m.target = t.pos; m.rewardInfluence = 60;
                        m.title = "Rescue " + name;
                        m.description = name + " is trapped in " + Place(b) + " with the dead outside. Reach them and they'll join you.";
                        return m;
                    }
                case MissionKind.Supply:
                    {
                        var f = faction >= 0 && faction < w.factions.Count ? w.factions[faction] : null;
                        if (f == null || f.homeBuilding < 0 || f.homeBuilding >= w.map.buildings.Count) return null;
                        var home = w.map.buildings[f.homeBuilding];
                        m.resource = (Resource)r.Range(0, 5); m.amount = r.Range(3, 6);
                        m.target = home.rect.Center; m.targetId = home.id; m.rewardInfluence = 40 + m.amount * 10;
                        m.title = "Bring " + m.amount + " " + m.resource + " to " + f.name;
                        m.description = f.name + " is running short. Bring " + m.amount + " " + m.resource + " from your stock to " + Place(home) + ".";
                        return m;
                    }
                case MissionKind.ClearInfestation:
                    {
                        PlagueHeart best = null; float d = float.MaxValue;
                        foreach (var h in w.hearts) if (!h.destroyed && Vec2.Distance(h.pos, c.basePos) < d && !c.missions.Any(x => x.Open && x.kind == kind && x.targetId == h.id)) { d = Vec2.Distance(h.pos, c.basePos); best = h; }
                        if (best == null) return null;
                        best.discovered = true;
                        m.targetId = best.id; m.target = best.pos; m.rewardInfluence = 150; m.expiresAt = w.minutes + 24 * 60;
                        m.title = "Clear the infestation"; m.description = "A plague heart is pulsing " + (int)d + " m from your base. Bring explosives and friends, and destroy it.";
                        return m;
                    }
                case MissionKind.KillFreak:
                    {
                        Zombie best = null; float d = 500;
                        foreach (var z in w.zombies)
                            if (z.Alive && (z.kind == ZombieKind.Feral || z.kind == ZombieKind.Juggernaut) && Vec2.Distance(z.pos, c.basePos) < d && !c.missions.Any(x => x.Open && x.kind == kind && x.targetId == z.id)) { d = Vec2.Distance(z.pos, c.basePos); best = z; }
                        if (best == null) return null;
                        var def = Bestiary.Get(best.kind);
                        m.targetId = best.id; m.target = best.pos; m.rewardInfluence = best.kind == ZombieKind.Juggernaut ? 150 : 80; m.expiresAt = w.minutes + 12 * 60;
                        m.title = "Kill the " + def.name.ToLowerInvariant(); m.description = "A " + def.name.ToLowerInvariant() + " is stalking the area " + (int)d + " m from your base. Put it down.";
                        return m;
                    }
                case MissionKind.Hunt:
                    {
                        var me = w.Controlled; if (me == null) return null;
                        Survivor near = null; float d = 800;
                        foreach (var s in w.survivors) if (s.alive && s.squad >= 0 && s.faction != 0 && w.Hostile(me, s) && Vec2.Distance(s.pos, c.basePos) < d) { d = Vec2.Distance(s.pos, c.basePos); near = s; }
                        if (near == null || c.missions.Any(x => x.Open && x.kind == kind)) return null;
                        foreach (var s in w.survivors) if (s.alive && s.squad == near.squad && s.faction == near.faction) m.targets.Add(s.id);
                        var f = w.FactionOf(near);
                        m.targetId = near.squad; m.target = near.pos; m.rewardInfluence = 120;
                        m.title = "Hunt " + (f != null && f.name.Length > 0 ? f.name : "the raiders"); m.description = (f != null && f.name.Length > 0 ? f.name : "A hostile crew") + " has been hitting survivors " + (int)d + " m from your base. Take out all " + m.targets.Count + " of them.";
                        return m;
                    }
                case MissionKind.Defend:
                    {
                        if (c.missions.Any(x => x.Open && x.kind == kind) || w.HoursSurvived < 12) return null;
                        m.amount = r.Range(10, 15); m.target = c.basePos; m.rewardInfluence = 100; m.expiresAt = w.minutes + 6 * 60;
                        m.giver = ""; m.giverName = "Your lookout"; m.faction = -1;
                        m.title = "Defend the base"; m.description = "A horde of about " + m.amount + " is drifting toward the base. Accept to make a stand when they arrive.";
                        return m;
                    }
            }
            return null;
        }

        /// <summary>Spawns the siege horde near the base and sends it at the base.</summary>
        void StartSiege(Mission m)
        {
            var w = world; var c = w.community; m.targets.Clear();
            for (int tries = 0; tries < m.amount * 6 && m.targets.Count < m.amount; tries++)
            {
                var p = c.basePos + Vec2.FromAngle(w.rng.Range(0, 6.283f)) * w.rng.Range(35, 55);
                int x = (int)p.x, y = (int)p.y;
                if (!w.map.InBounds(x, y) || w.map.TileBlocked(x, y) || w.map.IsWater(x, y) || w.map.RoomAt(x, y) != null) continue;
                var z = w.AddZombie(new Vec2(x + .5f, y + .5f), w.rng);
                z.dormant = false; z.state = ZombieState.Investigate; z.target = c.basePos + Vec2.FromAngle(w.rng.Range(0, 6.283f)) * w.rng.Range(0, 4); z.timer = 180; z.path.Clear(); z.repath = 0;
                m.targets.Add(z.id);
            }
            w.Emit(EventKind.Siege, c.basePos, w.playerId, "start");
            Radio("Lookout: here they come! " + m.targets.Count + " of them, closing on the base.");
        }

        void SurroundWithDead(Vec2 at, int count)
        {
            var w = world;
            for (int tries = 0, n = 0; tries < count * 6 && n < count; tries++)
            {
                var p = at + Vec2.FromAngle(w.rng.Range(0, 6.283f)) * w.rng.Range(7, 16);
                int x = (int)p.x, y = (int)p.y;
                if (!w.map.InBounds(x, y) || w.map.TileBlocked(x, y) || w.map.IsWater(x, y) || w.map.RoomAt(x, y) != null) continue;
                var z = w.AddZombie(new Vec2(x + .5f, y + .5f), w.rng); z.state = ZombieState.Wander; n++;
            }
        }

        /// <summary>A random non-base building in a distance band from the base.</summary>
        Building PickBuilding(float min, float max)
        {
            var w = world; var c = w.community;
            for (int tries = 0; tries < 40; tries++)
            {
                var b = w.map.buildings[w.rng.Range(0, w.map.buildings.Count)];
                float d = Vec2.Distance(b.rect.Center, c.basePos);
                if (b.id == c.baseBuilding || b.rooms.Count == 0 || d < min || d > max || c.OutpostAt(b.id) != null) continue;
                return b;
            }
            return null;
        }
        static string Place(Building b) { return b.name.Length > 0 ? b.name : "a " + b.kind.ToString().ToLowerInvariant(); }

        /// <summary>Picks who is asking: a named character, an enclave, or the community itself.</summary>
        void Giver(out string key, out string name, out int faction)
        {
            var w = world; var me = w.Controlled;
            var named = new List<Survivor>();
            foreach (var s in w.survivors) if (s.alive && s.faction != 0 && !string.IsNullOrEmpty(s.character) && (me == null || !w.Hostile(me, s))) named.Add(s);
            var enclaves = new List<Faction>();
            foreach (var f in w.factions) if (f.id != 0 && f.role == AiRole.Enclave && !f.Hostile && f.name != WanderersName) enclaves.Add(f);
            if (named.Count > 0 && (enclaves.Count == 0 || w.rng.Chance(.5f)))
            {
                var s = named[w.rng.Range(0, named.Count)]; var def = Characters.Get(s.character);
                key = s.character; name = def != null ? def.name : s.name; faction = s.faction; return;
            }
            if (enclaves.Count > 0) { var f = enclaves[w.rng.Range(0, enclaves.Count)]; key = f.name; name = f.name; faction = f.id; return; }
            key = ""; name = "Ada Pike"; faction = -1;
        }
    }
}
