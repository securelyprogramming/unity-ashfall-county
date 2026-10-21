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
        public float buildHours;              // > 0 while under construction (or upgrading)
        public int prop = -1;                 // map prop placed at the base for it, -1 = none
        public bool upgrading;                // buildHours count down to level + 1; the current level keeps working
        /// <summary>Level that currently has an effect: 0 while the first build is underway.</summary>
        public int Active { get { return buildHours > 0 && !upgrading ? 0 : level; } }
        public bool Busy { get { return buildHours > 0; } }
    }

    /// <summary>A claimed building that adds stock and a safe point on the map.</summary>
    [Serializable]
    public sealed class Outpost { public int building = -1; public Resource bonus; public Vec2 pos; public int prop = -1; public float amount = 1; }

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
        // Kind-specific state (CommunityMissions.cs).
        public Resource resource; public int amount;                // Supply: stock delivered; Defend/Hunt: kills needed
        public double offeredAt;
        public string giverName = "";                               // display name of the giver
        public readonly List<int> targets = new List<int>();        // Hunt: squad member ids; Defend: horde zombie ids
        public bool Open { get { return status == MissionStatus.Offered || status == MissionStatus.Active; } }
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
        // Schedules and bookkeeping (SimCommunity.cs).
        public int lastUpkeepDay;                                   // day whose 06:00 upkeep has run
        public double nextOfferAt;                                  // in-game minutes of the next mission offer
        public int heartsDestroyed;                                 // hearts already rewarded
        public int garrison = -1;                                   // resident posted at the Watchtower
        public int generatorProp = -1;                              // Generator prop (placed with the first level-2 facility)
        public bool powered = true;                                 // false after a day without Fuel for the generator/watchtower
        public string lastReport = "";                              // last daily radio summary

        public const int MaxLevel = 3;
        public float Stock(Resource r) { return stock[(int)r]; }
        public Facility Get(FacilityKind k) { foreach (var f in facilities) if (f.kind == k) return f; return null; }
        /// <summary>Working level of a facility (0 = not built or still under construction).</summary>
        public int Level(FacilityKind k) { var f = Get(k); return f == null ? 0 : f.Active; }
        /// <summary>Most of each resource the base can hold.</summary>
        public float StorageCap { get { return 20 + 15 * Math.Max(1, Level(FacilityKind.Storage)); } }
        /// <summary>Members the base can sleep (Beds level × 4).</summary>
        public int BedCapacity { get { return Math.Max(1, Level(FacilityKind.Beds)) * 4; } }
        public Outpost OutpostAt(int building) { foreach (var o in outposts) if (o.building == building) return o; return null; }
        public Mission MissionById(int id) { foreach (var m in missions) if (m.id == id) return m; return null; }

        /// <summary>Storage and Beds come with the base and do not use a facility slot.</summary>
        public static bool BuiltIn(FacilityKind k) { return k == FacilityKind.Storage || k == FacilityKind.Beds; }
        public static string FacilityName(FacilityKind k) { return k == FacilityKind.RadioRoom ? "Radio Room" : k.ToString(); }
        public static string FacilityDescription(FacilityKind k)
        {
            switch (k)
            {
                case FacilityKind.Storage: return "Holds the base stock. Each level raises the cap on every resource by 15.";
                case FacilityKind.Beds: return "Four beds per level. Recruits need a free bed. Resting at base heals faster.";
                case FacilityKind.Infirmary: return "Injuries heal three times faster at base. Treats the blood plague (5 Meds) and makes plague cures.";
                case FacilityKind.Workshop: return "Repairs weapons and makes molotovs, pipe bombs, grenades (level 2) and bandages.";
                case FacilityKind.Watchtower: return "A resident with the best gun stands guard and shoots zombies near the base. Uses 1 Fuel a day.";
                case FacilityKind.Garden: return "+2 Food a day per level.";
                case FacilityKind.Kitchen: return "Hot meals: community morale +6 per level while there is Food.";
                case FacilityKind.RadioRoom: return "More missions, one more outpost, and each morning it locates the nearest unknown plague heart.";
            }
            return "";
        }
        /// <summary>Prop placed at the base when the facility is built, and whether it goes inside the base building.</summary>
        public static PropKind PropFor(FacilityKind k, out bool inside)
        {
            inside = true;
            switch (k)
            {
                case FacilityKind.Storage: return PropKind.SupplyCrate;
                case FacilityKind.Beds: return PropKind.Bunk;
                case FacilityKind.Infirmary: return PropKind.MedicalBed;
                case FacilityKind.Workshop: return PropKind.Workbench;
                case FacilityKind.Watchtower: inside = false; return PropKind.Watchtower;
                case FacilityKind.Garden: inside = false; return PropKind.GardenBed;
                case FacilityKind.Kitchen: return PropKind.Stove;
                default: return PropKind.RadioSet;
            }
        }
        /// <summary>Daily outpost bonus resource for a building kind.</summary>
        public static Resource OutpostBonus(BuildingKind k, out float amount)
        {
            amount = 2;
            switch (k)
            {
                case BuildingKind.Grocery: case BuildingKind.Diner: case BuildingKind.Farmhouse: case BuildingKind.Barn: return Resource.Food;
                case BuildingKind.Pharmacy: case BuildingKind.Hospital: return Resource.Meds;
                case BuildingKind.Police: case BuildingKind.Barracks: case BuildingKind.Armory: return Resource.Ammo;
                case BuildingKind.GasStation: case BuildingKind.Garage: case BuildingKind.Hangar: return Resource.Fuel;
                case BuildingKind.Hardware: case BuildingKind.Warehouse: case BuildingKind.Shed: return Resource.Materials;
                case BuildingKind.House: case BuildingKind.Motel: case BuildingKind.Church: amount = 1; return Resource.Food;
                default: amount = 1; return Resource.Materials;
            }
        }
    }

    public sealed partial class Survivor
    {
        public float plague;        // blood plague 0..100: rises over time once infected, fatal at 100 unless cured
        public bool plagueInfected;
        public float injury;        // 0..100: lowers maximum health until rested/treated
        public float MaxHealth { get { return Math.Max(10, 100 - injury); } }
        public float healthSeen = -1;     // health after the last survival update; a sharp drop since then is a serious hit
    }

    public sealed partial class World
    {
        public Community community = new Community();
        public Survivor Controlled { get { return playerId >= 0 && playerId < survivors.Count ? survivors[playerId] : null; } }
        public Building BaseBuilding { get { var c = community; return c.baseBuilding >= 0 && c.baseBuilding < map.buildings.Count ? map.buildings[c.baseBuilding] : null; } }
        /// <summary>Living community members (faction 0), the controlled survivor included.</summary>
        public List<Survivor> LivingMembers()
        {
            var list = new List<Survivor>();
            foreach (int id in community.members) { var s = id >= 0 && id < survivors.Count ? survivors[id] : null; if (s != null && s.alive && s.faction == 0) list.Add(s); }
            return list;
        }

        static readonly string[] FirstFemale = { "Ada", "June", "Rosa", "Hazel", "Nora", "Iris", "Mae", "Lena", "Ruth", "Tamsin", "Willa", "Dee", "Carmen", "Priya", "Odette", "Lucy" };
        static readonly string[] FirstMale = { "Eli", "Marcus", "Hank", "Tobias", "Ray", "Dale", "Owen", "Silas", "Victor", "Joel", "Amos", "Reggie", "Theo", "Luis", "Grady", "Cal" };
        static readonly string[] Surnames = { "Hart", "Quinn", "Bishop", "Lowe", "Navarro", "Okafor", "Brandt", "Sutter", "Pruitt", "Delgado", "Ames", "Cole", "Hayes", "Merritt", "Voss", "Tran", "Whitlock", "Yates" };
        static readonly string[][] TraitRolls =
        {
            new[] { "athletic", "smoker" }, new[] { "marksman" }, new[] { "brawler" }, new[] { "cheerful", "light_eater" }, new[] { "quick_healer", "hardy" },
            new[] { "green_thumb", "light_eater" }, new[] { "strong", "gloomy", "clumsy" }, new[] { "angler", "eagle_eyed" }, new[] { "hardy", "graceful" },
            new[] { "marksman", "hearty_appetite", "light_sleeper" }, new[] { "brawler", "out_of_shape", "cheerful" }, new[] { "fast_learner", "slow_healer", "smoker" },
        };

        /// <summary>Adds the other starting members, claims the home base and fills the opening stock.</summary>
        void SetupCommunity(Survivor leader, Building home)
        {
            var c = community;
            if (!c.members.Contains(leader.id)) c.members.Add(leader.id);
            leader.faction = 0; leader.ai = AiRole.None;
            c.stock = new float[] { 12, 6, 6, 10, 4 }; c.morale = 60; c.influence = 100;
            c.facilities.Clear();
            c.facilities.Add(new Facility { kind = FacilityKind.Storage, level = 1 });
            c.facilities.Add(new Facility { kind = FacilityKind.Beds, level = 1 });
            c.lastUpkeepDay = HourOfDay >= 6 ? Day : Day - 1;
            c.nextOfferAt = minutes + 90;
            var r = new Rng((ulong)settings.seed, 41);
            var used = new HashSet<string> { leader.name };
            for (int i = 1; i < Math.Max(1, settings.startingMembers); i++)
            {
                bool female = r.Chance(.5f); string name = "";
                for (int tries = 0; tries < 20 && (name.Length == 0 || used.Contains(name)); tries++) name = r.Pick(female ? FirstFemale : FirstMale) + " " + r.Pick(Surnames);
                used.Add(name);
                var traits = TraitRolls[r.Range(0, TraitRolls.Length)];
                if (Trait.Validate(traits).Length > 0) traits = new string[0];
                var m = AddSurvivor(name, Profession.All[r.Range(0, Profession.All.Length)].id, female, home, traits);
                m.faction = 0; m.ai = AiRole.Resident; m.fatigue = r.Range(5f, 25f);
                c.members.Add(m.id);
            }
            if (home == null) return;
            c.baseBuilding = home.id; c.basePos = home.rect.Center;
            // Spread the members over the base so they do not stand on one tile.
            var spots = BaseTiles(true, false);
            int next = 0;
            foreach (int id in c.members)
            {
                if (id == leader.id || spots.Count == 0) continue;
                next = (next + Math.Max(1, spots.Count / Math.Max(1, c.members.Count))) % spots.Count;
                survivors[id].pos = new Vec2(spots[next].x + .5f, spots[next].y + .5f);
            }
            var storage = PlaceBaseProp(PropKind.SupplyCrate, true);
            if (storage != null) { c.facilities[0].prop = storage.id; map.AddContainer(storage, "").generated = true; }
            var bunk = PlaceBaseProp(PropKind.Bunk, true); if (bunk != null) c.facilities[1].prop = bunk.id;
            PlaceBaseProp(PropKind.Bunk, true);
            PlaceBaseProp(PropKind.RadioSet, true);
        }

        /// <summary>Free tiles of the base: inside its rooms (furniture spots against a wall, away from doors) or on open ground just outside.</summary>
        internal List<Int2> BaseTiles(bool inside, bool furniture)
        {
            var list = new List<Int2>(); var home = BaseBuilding; if (home == null) return list;
            var occupied = new HashSet<int>();
            foreach (var p in map.props) if (!p.removed && !p.Blocks && map.InBounds(p.x, p.y) && home.rect.Overlaps(new Rect(p.x, p.y, 1, 1), 6)) occupied.Add(map.Index(p.x, p.y));
            if (inside)
            {
                foreach (int rid in home.rooms)
                {
                    var room = map.rooms[rid];
                    for (int y = room.rect.y; y < room.rect.MaxY; y++)
                        for (int x = room.rect.x; x < room.rect.MaxX; x++)
                        {
                            var rm = map.RoomAt(x, y);
                            if (rm == null || rm.building != home.id || map.TileBlocked(x, y) || occupied.Contains(map.Index(x, y))) continue;
                            if (map.OpeningAt(x, y, true) != null || map.OpeningAt(x + 1, y, true) != null || map.OpeningAt(x, y, false) != null || map.OpeningAt(x, y + 1, false) != null) continue;
                            if (furniture)
                            {
                                bool wall = map.V(x, y) == Edge.Wall || map.V(x + 1, y) == Edge.Wall || map.H(x, y) == Edge.Wall || map.H(x, y + 1) == Edge.Wall;
                                if (!wall || NearDoorway(x, y)) continue;
                            }
                            else if (map.Collides(new Vec2(x + .5f, y + .5f), Survivor.Radius)) continue;
                            list.Add(new Int2(x, y));
                        }
                }
                return list;
            }
            var doors = map.openings.Where(o => o.building == home.id && o.exterior && o.kind == Edge.Door).Select(o => o.Center).ToList();
            for (int ring = 1; ring <= 4 && list.Count < 12; ring++)
                for (int x = home.rect.x - ring; x < home.rect.MaxX + ring; x++)
                    for (int y = home.rect.y - ring; y < home.rect.MaxY + ring; y++)
                    {
                        if (x > home.rect.x - ring && x < home.rect.MaxX + ring - 1 && y > home.rect.y - ring && y < home.rect.MaxY + ring - 1) continue;   // ring edge only
                        if (!map.InBounds(x, y) || map.TileBlocked(x, y) || map.RoomAt(x, y) != null || occupied.Contains(map.Index(x, y))) continue;
                        var centre = new Vec2(x + .5f, y + .5f); bool nearDoor = false;
                        foreach (var d in doors) if (Vec2.Distance(d, centre) < 2.6f) nearDoor = true;
                        if (nearDoor || map.H(x, y) != Edge.None || map.V(x, y) != Edge.None || map.H(x, y + 1) != Edge.None || map.V(x + 1, y) != Edge.None) continue;
                        list.Add(new Int2(x, y));
                    }
            return list;
        }
        bool NearDoorway(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                {
                    int tx = x + dx, ty = y + dy;
                    if (map.OpeningAt(tx, ty, true) != null || map.OpeningAt(tx + 1, ty, true) != null || map.OpeningAt(tx, ty, false) != null || map.OpeningAt(tx, ty + 1, false) != null) return true;
                }
            return false;
        }

        /// <summary>Places a base prop (facility furniture, flags) on a free tile, marked modified so saves keep it.</summary>
        internal Prop PlaceBaseProp(PropKind kind, bool inside)
        {
            var tiles = BaseTiles(inside, true);
            if (tiles.Count == 0 && inside) tiles = BaseTiles(false, true);
            foreach (var t in tiles)
            {
                var centre = new Vec2(t.x + .5f, t.y + .5f);
                if (survivors.Any(s => s.alive && Vec2.Distance(s.pos, centre) < .9f)) continue;
                var p = map.AddProp(kind, t.x, t.y); p.modified = true; return p;
            }
            return null;
        }

        // ---------------------------------------------------------------- save section ("community")
        const int CommunitySaveVersion = 1;
        internal void WriteCommunity(BinaryWriter b)
        {
            var c = community;
            b.Write(CommunitySaveVersion);
            b.Write(c.members.Count); foreach (int id in c.members) b.Write(id);
            b.Write(c.baseBuilding); b.Write(c.basePos.x); b.Write(c.basePos.y);
            for (int i = 0; i < 5; i++) b.Write(c.stock[i]);
            b.Write(c.morale); b.Write(c.influence); b.Write(c.companion); b.Write(c.nextMission);
            b.Write(c.lastUpkeepDay); b.Write(c.nextOfferAt); b.Write(c.heartsDestroyed); b.Write(c.garrison); b.Write(c.generatorProp); b.Write(c.powered); b.Write(c.lastReport ?? "");
            var comp = c.companion >= 0 && c.companion < survivors.Count ? survivors[c.companion] : null; b.Write(comp == null ? -1 : comp.leader);
            b.Write(c.facilities.Count);
            foreach (var f in c.facilities) { b.Write((byte)f.kind); b.Write(f.level); b.Write(f.buildHours); b.Write(f.prop); b.Write(f.upgrading); }
            b.Write(c.outposts.Count);
            foreach (var o in c.outposts) { b.Write(o.building); b.Write((byte)o.bonus); b.Write(o.pos.x); b.Write(o.pos.y); b.Write(o.prop); b.Write(o.amount); }
            b.Write(c.missions.Count);
            foreach (var m in c.missions)
            {
                b.Write(m.id); b.Write((byte)m.kind); b.Write((byte)m.status); b.Write(m.title ?? ""); b.Write(m.description ?? ""); b.Write(m.giver ?? ""); b.Write(m.giverName ?? "");
                b.Write(m.target.x); b.Write(m.target.y); b.Write(m.targetId); b.Write(m.expiresAt); b.Write(m.offeredAt); b.Write(m.rewardInfluence); b.Write(m.rewardItem ?? ""); b.Write(m.faction);
                b.Write((byte)m.resource); b.Write(m.amount); b.Write(m.targets.Count); foreach (int t in m.targets) b.Write(t);
            }
            b.Write(survivors.Count);
            foreach (var s in survivors) { b.Write(s.plague); b.Write(s.plagueInfected); b.Write(s.injury); }
        }
        internal void ReadCommunity(BinaryReader b)
        {
            var c = community = new Community();
            int version = b.ReadInt32(); if (version != CommunitySaveVersion) throw new InvalidDataException("Unknown community save version " + version + ".");
            int n = b.ReadInt32(); for (int i = 0; i < n; i++) c.members.Add(b.ReadInt32());
            c.baseBuilding = b.ReadInt32(); c.basePos = new Vec2(b.ReadSingle(), b.ReadSingle());
            for (int i = 0; i < 5; i++) c.stock[i] = b.ReadSingle();
            c.morale = b.ReadSingle(); c.influence = b.ReadSingle(); c.companion = b.ReadInt32(); c.nextMission = b.ReadInt32();
            c.lastUpkeepDay = b.ReadInt32(); c.nextOfferAt = b.ReadDouble(); c.heartsDestroyed = b.ReadInt32(); c.garrison = b.ReadInt32(); c.generatorProp = b.ReadInt32(); c.powered = b.ReadBoolean(); c.lastReport = b.ReadString();
            int leader = b.ReadInt32();
            n = b.ReadInt32();
            for (int i = 0; i < n; i++) c.facilities.Add(new Facility { kind = (FacilityKind)b.ReadByte(), level = b.ReadInt32(), buildHours = b.ReadSingle(), prop = b.ReadInt32(), upgrading = b.ReadBoolean() });
            n = b.ReadInt32();
            for (int i = 0; i < n; i++) c.outposts.Add(new Outpost { building = b.ReadInt32(), bonus = (Resource)b.ReadByte(), pos = new Vec2(b.ReadSingle(), b.ReadSingle()), prop = b.ReadInt32(), amount = b.ReadSingle() });
            n = b.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var m = new Mission { id = b.ReadInt32(), kind = (MissionKind)b.ReadByte(), status = (MissionStatus)b.ReadByte(), title = b.ReadString(), description = b.ReadString(), giver = b.ReadString(), giverName = b.ReadString() };
                m.target = new Vec2(b.ReadSingle(), b.ReadSingle()); m.targetId = b.ReadInt32(); m.expiresAt = b.ReadDouble(); m.offeredAt = b.ReadDouble(); m.rewardInfluence = b.ReadInt32(); m.rewardItem = b.ReadString(); m.faction = b.ReadInt32();
                m.resource = (Resource)b.ReadByte(); m.amount = b.ReadInt32(); int t = b.ReadInt32(); for (int k = 0; k < t; k++) m.targets.Add(b.ReadInt32());
                c.missions.Add(m);
            }
            n = b.ReadInt32(); if (n != survivors.Count) throw new InvalidDataException("Save community does not match its survivors.");
            foreach (var s in survivors) { s.plague = b.ReadSingle(); s.plagueInfected = b.ReadBoolean(); s.injury = b.ReadSingle(); }
            if (c.members.Any(id => id < 0 || id >= survivors.Count) || c.facilities.Any(f => f.prop >= map.props.Count) || c.baseBuilding >= map.buildings.Count) throw new InvalidDataException("Save community does not match the county.");
            if (c.companion >= 0 && c.companion < survivors.Count) survivors[c.companion].leader = leader;
        }
    }

    public sealed partial class Simulation
    {
        // ---------------------------------------------------------------- radio story: Ada Pike's emergency band and the legacy
        void Story()
        {
            var w = world; double since = w.HoursSurvived;
            Broadcast("radio-1", since > .05, "Ada Pike, Ashfall County emergency band, from the radio station. The Guard sealed the county line. If you have people, keep them together: pick a strong house, call it home, and bring everything you find back to it.");
            Broadcast("radio-2", since > 2, "Ada again. Rucksacks of food, meds, ammo, materials and fuel keep a group alive. Carry them home or load a car trunk and park at your base. Build what you need: an infirmary, a workshop, a watchtower.");
            Broadcast("radio-3", since > 6, "Ada: the Guard is dropping supply crates by parachute. Red smoke marks them. Everyone in the county sees that smoke too. Be quick, or be ready.");
            Broadcast("radio-4", since > 10, "Ada: Guard artillery is shelling 'red zones'. When I read out a warning, get out of the circle. The shells don't care who you are.");
            Broadcast("radio-5", since > 16, "Ada: there are growths in some buildings. Plague hearts. The red-eyed dead come from them, and their bite gives you the blood plague. Burn out every heart and this county can live again.");
            // Legacy progress: a daily count of the hearts still standing, and the cleansing itself.
            if (w.hearts.Count > 0 && w.Day >= 2 && w.HourOfDay >= 12)
                Broadcast("legacy-day-" + w.Day, true, "Ada: by my count " + w.HeartsRemaining + " plague heart" + (w.HeartsRemaining == 1 ? "" : "s") + " remain" + (w.HeartsRemaining == 1 ? "s" : "") + " in Ashfall County.");
            // Helicopter: a loud event that drags zombies across the county toward the survivor.
            double heliStart = (w.helicopterDay - 1) * 1440 + w.helicopterMinute;
            if (w.minutes > heliStart && w.minutes < heliStart + 30)
            {
                var p0 = w.Controlled;
                if (w.flags.Add("heli") && p0 != null && p0.alive) w.Message(p0, "A helicopter thunders overhead, circling... it seems to be following you.");
                if (w.rng.Chance(.06f) && p0 != null && p0.alive) { var p = p0.pos + Vec2.FromAngle(w.rng.Range(0, 6.28f)) * w.rng.Range(10, 30); w.MakeNoise(p, 85); w.Emit(EventKind.Helicopter, p, p0.id); }
            }
        }
        /// <summary>Legacy: once every plague heart is destroyed the county is cleansed (outcome "legacy"). Run from UpdateCommunity.</summary>
        void CheckLegacy()
        {
            var w = world;
            if (w.outcome != "" || w.hearts.Count == 0 || w.HeartsRemaining > 0) return;
            w.outcome = "legacy";
            var c = w.Controlled;
            w.Emit(EventKind.Legacy, c == null ? w.community.basePos : c.pos, c == null ? -1 : c.id, "Every plague heart in Ashfall County is destroyed. The county is cleansed.");
            Broadcast("legacy", true, "Ada: that was the last heart. Listen... it's quiet out there. You did it. Ashfall County is ours again.");
        }
        /// <summary>A one-time radio line (keyed by flag) to the controlled survivor.</summary>
        void Broadcast(string flag, bool condition, string text)
        {
            if (!condition || world.flags.Contains(flag)) return;
            world.flags.Add(flag);
            Radio(text);
        }
        /// <summary>Radio message to the controlled survivor (base radio, so no hand radio is needed).</summary>
        void Radio(string text)
        {
            var s = world.Controlled;
            world.Emit(EventKind.Radio, s == null ? world.community.basePos : s.pos, s == null ? -1 : s.id, text);
        }
    }
}
