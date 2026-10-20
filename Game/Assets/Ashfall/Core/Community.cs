using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ashfall.Core
{
    public enum FacilityKind : byte { Storage, Beds, Infirmary, Workshop, Watchtower, Garden, Kitchen, RadioRoom }

    [Serializable]
    public sealed class Facility
    {
        public FacilityKind kind; public int level = 1;
        public float buildHours;              // > 0 while under construction
        public int prop = -1;                 // map prop placed at the base for it, -1 = none
    }

    /// <summary>A claimed building that adds stock and a safe point on the map.</summary>
    [Serializable]
    public sealed class Outpost { public int building = -1; public Resource bonus; public Vec2 pos; }

    public enum MissionKind : byte { Rescue, Supply, ClearInfestation, KillFreak, Hunt, Trade, Defend, Scavenge }
    public enum MissionStatus : byte { Offered, Active, Completed, Failed, Expired }

    [Serializable]
    public sealed class Mission
    {
        public int id; public MissionKind kind; public MissionStatus status = MissionStatus.Offered;
        public string title = "", description = "", giver = "";   // giver: character key or faction name
        public Vec2 target; public int targetId = -1;               // survivor / heart / zombie / building id by kind
        public double expiresAt;                                    // in-game minutes
        public int rewardInfluence; public string rewardItem = "";
        public int faction = -1;                                    // faction that gains standing
    }

    /// <summary>The player's community: members, home base, shared stock, morale and influence.</summary>
    [Serializable]
    public sealed class Community
    {
        public readonly List<int> members = new List<int>();        // survivor ids with faction 0 (alive or dead)
        public int baseBuilding = -1; public Vec2 basePos;
        public float[] stock = new float[5];                        // indexed by (int)Resource
        public float morale = 60, influence = 100;
        public readonly List<Facility> facilities = new List<Facility>();
        public readonly List<Outpost> outposts = new List<Outpost>();
        public readonly List<Mission> missions = new List<Mission>();
        public int companion = -1;                                  // member following the controlled survivor
        public int nextMission;
        public float Stock(Resource r) { return stock[(int)r]; }
        public Facility Get(FacilityKind k) { return facilities.FirstOrDefault(f => f.kind == k); }
    }

    public sealed partial class Survivor
    {
        public float plague;        // blood plague 0..100: rises over time once infected, fatal at 100 unless cured
        public bool plagueInfected;
        public float injury;        // 0..100: lowers maximum health until rested/treated
        public float MaxHealth { get { return Math.Max(10, 100 - injury); } }
    }

    public sealed partial class World
    {
        public Community community = new Community();
        public Survivor Controlled { get { return playerId >= 0 && playerId < survivors.Count ? survivors[playerId] : null; } }

        /// <summary>Adds the other starting members, claims the home base and fills the opening stock.</summary>
        void SetupCommunity(Survivor leader, Building home) { community.members.Add(leader.id); if (home != null) { community.baseBuilding = home.id; community.basePos = new Vec2(home.rect.x + home.rect.w * .5f, home.rect.y + home.rect.h * .5f); } }

        internal void WriteCommunity(BinaryWriter b) { b.Write(0); }
        internal void ReadCommunity(BinaryReader b) { b.ReadInt32(); }
    }

    public sealed partial class Simulation
    {
        /// <summary>Daily upkeep (consumption, production, morale), construction, missions and base sieges.</summary>
        void UpdateCommunity(float dt, float hours) { }

        /// <summary>Community commands (switch, recruit, build, outposts, rucksacks, missions). Return true when handled.</summary>
        bool ExecuteCommunity(Survivor s, Command c) { return false; }

        /// <summary>Morale, member loss, switching to another member, and the "dead" outcome when nobody is left.</summary>
        void CommunityOnDeath(Survivor s)
        {
            if (world.survivors.Where(x => x.faction == 0).All(x => !x.alive) && world.outcome == "") world.outcome = "dead";
        }

        /// <summary>Facility slots at the home base (by building size).</summary>
        public int BaseSlots { get { return 4; } }
        /// <summary>Materials and in-game hours to build (level 1) or upgrade to a level.</summary>
        public static float FacilityMaterials(FacilityKind kind, int level) { return 10 * level; }
        public static float FacilityHours(FacilityKind kind, int level) { return 2 * level; }

        public bool AtBase(Survivor s) { var c = world.community; return c.baseBuilding >= 0 && Vec2.Distance(s.pos, c.basePos) < 24; }
        public bool CanSwitchTo(int survivor, out string reason) { reason = "Not available yet."; return false; }
        public bool CanRecruit(Survivor s, Survivor target, out string reason) { reason = "Not available yet."; return false; }
        public bool CanBuild(FacilityKind kind, out string reason) { reason = "Not available yet."; return false; }
        public bool CanClaimOutpost(Survivor s, Building b, out string reason) { reason = "Not available yet."; return false; }

        // ---------------------------------------------------------------- radio story (moved from Simulation.cs; rewrite for the community legacy)
        void Story()
        {
            var w = world; double since = w.HoursSurvived;
            Broadcast("radio-1", since > .05, "Ada Pike, Ashfall County emergency band. The county line is closed by the Guard. Stay indoors, lock your doors and keep quiet. They follow noise.");
            Broadcast("radio-2", since > 3, "Ada again. Water and power crews have walked off. Fill every bottle and bathtub you can while the taps still run.");
            if (w.settings.evacuationDay > 0)
            {
                Broadcast("radio-3", since > 9, "This is Jonah Reed in Cedar Fork. We are fixing up buses for a convoy. Day " + w.settings.evacuationDay + ", eight in the morning to two in the afternoon, the Ash River highway bridge. West bank. Be there.");
                Broadcast("radio-4", w.Day == w.settings.evacuationDay - 1 && w.HourOfDay > 18, "Jonah Reed: convoy leaves the Ash River bridge tomorrow, eight to two. Travel in daylight. Do not bring them with you.");
                Broadcast("radio-5", w.Day == w.settings.evacuationDay && w.HourOfDay > 7.5f, "Jonah Reed: we're at the bridge. Engines running. Come now.");
                if (w.Day == w.settings.evacuationDay && w.HourOfDay >= 8 && w.HourOfDay <= 14 && w.outcome == "")
                    foreach (var s in w.survivors)
                        if (s.alive && Vec2.Distance(s.pos, w.gen.evacuationPoint) < 14)
                        {
                            w.outcome = "evacuated"; w.Emit(EventKind.Evacuated, s.pos, s.id, s.name + " boarded Jonah Reed's convoy.");
                        }
                if ((w.Day > w.settings.evacuationDay || w.Day == w.settings.evacuationDay && w.HourOfDay > 14) && w.outcome == "")
                    Broadcast("radio-6", true, "Jonah Reed: we waited as long as we could. Convoy is moving out. Whoever is still out there... keep surviving.");
            }
            Broadcast("power-off", !w.PowerOn, "The lights flicker and die. The county grid is down.");
            Broadcast("water-off", !w.WaterOn, "The taps sputter and run dry. The water is off.");
            // Helicopter: a loud event that drags zombies across the county toward the survivor.
            double heliStart = (w.helicopterDay - 1) * 1440 + w.helicopterMinute;
            if (w.minutes > heliStart && w.minutes < heliStart + 30)
            {
                if (w.flags.Add("heli")) foreach (var s in w.survivors) if (s.alive) w.Message(s, "A helicopter thunders overhead, circling... it seems to be following you.");
                if (w.rng.Chance(.06f))
                    foreach (var s in w.survivors) if (s.alive) { var p = s.pos + Vec2.FromAngle(w.rng.Range(0, 6.28f)) * w.rng.Range(10, 30); w.MakeNoise(p, 85); w.Emit(EventKind.Helicopter, p, s.id); }
            }
        }
        void Broadcast(string flag, bool condition, string text)
        {
            if (!condition || world.flags.Contains(flag)) return;
            world.flags.Add(flag);
            foreach (var s in world.survivors)
                if (s.alive && (s.HasTag("radio") || flag == "power-off" || flag == "water-off"))
                    world.Emit(flag.StartsWith("radio") ? EventKind.Radio : EventKind.Message, s.pos, s.id, text);
        }

    }
}
