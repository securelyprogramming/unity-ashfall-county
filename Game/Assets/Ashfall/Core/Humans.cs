using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>How eagerly another group tries to kill the player's community.</summary>
    public enum Disposition : byte
    {
        Friendly,     // never attacks; trades and gives missions
        Wary,         // fights back if attacked or if you trespass in their base
        Opportunist,  // attacks when you look weak: alone, hurt, carrying a rucksack, or at night
        Hunter        // attacks on sight and tracks you across the county
    }

    public enum CompanionOrder : byte { Follow, Hold, Attack }

    /// <summary>A group of humans: the player's community (id 0), enclaves, rival crews and raiders.</summary>
    [Serializable]
    public sealed class Faction
    {
        public int id; public string name = "", description = "";
        public AiRole role = AiRole.Rival;
        public Disposition disposition = Disposition.Wary;
        public float standing;                 // -100 hostile .. 100 allied, toward the player's community
        public int homeBuilding = -1;          // enclave/raider base, -1 for roaming crews
        public bool Hostile { get { return id != 0 && (standing <= -50 || disposition == Disposition.Hunter); } }
        // Open-county state (Humans.cs). Times are in-game minutes.
        public string key = "";                // stable id: "community", "radio_station", "red_talons", "crew_3" ...
        public string leader = "";             // named character key leading the group, "" for none
        public string member = "survivor";     // kill-feed noun for an unnamed member ("Red Talon raider")
        public string art = "rival";           // art group: community, enclave, rival, raider, soldier, trader
        public bool enclave;                   // a settlement that trades and gives missions
        public Vec2 home;                      // base or camp centre (roaming crews: where they landed)
        public double provokedUntil;           // fighting the community: Wary retaliation or an Opportunist robbery
        public int feud = -1; public double feudUntil;   // fighting another faction for a while
        public double nextRoll;                // Opportunist: next encounter roll
        public double nextBounty; public bool hasBounty; public Vec2 bounty;   // Hunter: last known position of the controlled survivor
        public int kills, losses;
        public bool IsCrew { get { return key.StartsWith("crew"); } }
    }

    /// <summary>A named special character the player can meet. Some recruit, some trade, some hunt you.</summary>
    public sealed class CharacterDef
    {
        public string key = "", name = "", title = "", description = "";
        public bool female; public string profession = "unemployed";
        public AiRole role = AiRole.Enclave; public Disposition disposition = Disposition.Wary;
        public bool recruitable; public int recruitInfluence = 150;
        public string[] traits = new string[0], kit = new string[0];
        public float health = 100, damageScale = 1;
        public LandmarkKind home = LandmarkKind.Town;   // preferred landmark for their enclave/camp
        // Open-county additions.
        public float recruitStanding;                    // faction standing needed before Recruit is offered
        public string[] lines = new string[0];           // talk lines (Greeting picks one by day)
        public string hostileLine = "", deathLine = "";  // shouted when attacking; radio message when killed
    }

    public sealed partial class Survivor
    {
        public int leader = -1;                         // companion: survivor id being followed
        public CompanionOrder order = CompanionOrder.Follow;
        public int squad = -1;                          // rival crew id (members share goals)
        public Disposition disposition = Disposition.Wary;
        public double lastHitAt = -1;                   // in-game minute of the last hit by another survivor (credits kills)
    }

    /// <summary>One line of a trade/talk menu offered by an NPC. The host re-validates when the Trade command arrives.</summary>
    public sealed class TradeOffer { public string label = "", detail = ""; public bool enabled = true; public string reason = ""; }

    /// <summary>A thrown grenade, molotov or pipe bomb: in flight for <see cref="flight"/> seconds, then lands at pos and burns its fuse.</summary>
    [Serializable]
    public sealed class Throwable
    {
        public int id; public string item = ""; public Vec2 from, pos; public int by = -1;
        public float fuse, flight, lure; public bool landed;
        public const float FlightTime = .6f;
        /// <summary>0..1 progress of the arc from <see cref="from"/> to <see cref="pos"/> (views draw the arc from this).</summary>
        public float Arc { get { return landed ? 1 : MathUtil.Clamp01(1 - flight / FlightTime); } }
    }

    public static class Characters
    {
        static readonly List<CharacterDef> all = new List<CharacterDef>
        {
            new CharacterDef { key = "ada", name = "Ada Pike", title = "Radio operator", female = true, profession = "deputy", role = AiRole.Enclave, disposition = Disposition.Friendly, home = LandmarkKind.RadioStation,
                description = "Runs the county emergency band from the old KASH radio station. Knows where everyone is, and tells most of it.",
                kit = new[] { "pistol" }, health = 110,
                lines = new[] { "You're one of the voices on my band. Good to put a face to it.", "Keep your radio on. If the Guard drops anything, you'll hear it from me first.", "Tess Monroe's crew went through Millbrook yesterday. Stripped it bare." },
                hostileLine = "Get off my station!", deathLine = "The emergency band is silent. Ada Pike is gone." },
            new CharacterDef { key = "jonah", name = "Jonah Reed", title = "Mechanic", profession = "firefighter", role = AiRole.Enclave, disposition = Disposition.Wary, home = LandmarkKind.Garage,
                description = "Holds Reed's Garage with his cousins. Trades fuel and car parts, but does not like strangers walking in armed.",
                kit = new[] { "shotgun", "crowbar" }, health = 120,
                lines = new[] { "Holster that before you come in my garage.", "Fuel's dear. Bring me materials and we'll talk.", "Convoy's dead, but an engine still runs if you treat it right." },
                hostileLine = "You were warned. Light 'em up!", deathLine = "Reed's Garage on the band: Jonah is dead. The cousins are pulling out." },
            new CharacterDef { key = "doc", name = "Doc Hollis", title = "Field medic", profession = "paramedic", role = AiRole.Enclave, disposition = Disposition.Friendly, home = LandmarkKind.Hospital,
                description = "A retired army medic keeping the county hospital's last ward open. Will join a community that can protect a clinic.",
                kit = new[] { "first_aid_kit", "bandage", "pistol" }, recruitable = true, recruitInfluence = 200, recruitStanding = 20,
                lines = new[] { "Sit down before you fall down. Who's bleeding?", "Bring me plague samples and I'll keep your people alive.", "If you ever build a real infirmary, I might come and run it." },
                hostileLine = "I patched you up and this is how you repay me?", deathLine = "County Hospital to anyone listening: Doc Hollis didn't make it." },
            new CharacterDef { key = "wren", name = "Wren Calder", title = "Loner sniper", female = true, profession = "deputy", role = AiRole.Rival, disposition = Disposition.Wary, home = LandmarkKind.Farmstead,
                description = "Hunts alone from the ridgelines. Watches you long before you see her. Joins only people the county speaks well of.",
                kit = new[] { "hunting_rifle", "rifle" }, traits = new[] { "eagle_eyed" }, recruitable = true, recruitInfluence = 250, recruitStanding = 25, health = 110, damageScale = 1.2f,
                lines = new[] { "I had you in my scope for a mile. You walk loud.", "Come back when people out here say your name kindly.", "Ferals hunt at night. So do I." },
                hostileLine = "Wrong ridge, friend.", deathLine = "Someone found Wren Calder's rifle on the ridge. No sign of her." },
            new CharacterDef { key = "tess", name = "Tess Monroe", title = "Rival scavenger", female = true, profession = "unemployed", role = AiRole.Rival, disposition = Disposition.Opportunist, home = LandmarkKind.Town,
                description = "Leads a fast crew that strips towns before anyone else gets there and races everyone to the airdrops. Friendly, until you look like an easy mark.",
                kit = new[] { "smg", "pistol", "machete" }, traits = new[] { "athletic" }, health = 110,
                lines = new[] { "Whatever you're looking for, we already took it.", "Nice bag. Be a shame if you dropped it.", "Race you to the next drop." },
                hostileLine = "Drop the bag and walk away!", deathLine = "Tess Monroe's crew is off the air. Somebody got the drop on the fastest looter in the county." },
            new CharacterDef { key = "kade", name = "Marshal Kade", title = "Guard deserter", profession = "deputy", role = AiRole.Hunter, disposition = Disposition.Hunter, home = LandmarkKind.MilitaryBase,
                description = "Walked off the cordon with a squad and a grudge. Hunts survivors for their gear, and the Guard still pays him bounties.",
                kit = new[] { "assault_rifle", "rifle", "pistol" }, health = 150, damageScale = 1.15f,
                lines = new[] { "Nothing personal. The Guard pays by the head." },
                hostileLine = "Contact! Take them down!", deathLine = "Guard channel chatter: deserter Kade confirmed dead. Bounty closed." },
            new CharacterDef { key = "sal", name = "\"Big Sal\" Varga", title = "Red Talon boss", profession = "firefighter", role = AiRole.Raider, disposition = Disposition.Hunter, home = LandmarkKind.RaiderCompound,
                description = "Runs the Red Talon raiders out of their scrapyard compound. Heavy weapons, heavier temper.",
                kit = new[] { "shotgun", "nailed_bat" }, traits = new[] { "strong" }, health = 170, damageScale = 1.25f,
                lines = new[] { "Everything in this county belongs to the Talons. Including you." },
                hostileLine = "Talons! Feed 'em to the dead!", deathLine = "The Red Talons are screaming on every channel. Big Sal is dead." },
            new CharacterDef { key = "pilgrim", name = "The Pilgrim", title = "Wandering trader", profession = "unemployed", role = AiRole.Trader, disposition = Disposition.Friendly, home = LandmarkKind.Church,
                description = "A soft-spoken traveller with a cart of things nobody else has. Nobody knows where the Pilgrim gets them.",
                kit = new[] { "revolver" }, health = 120,
                lines = new[] { "Everything has a price, friend. Influence is the only coin left.", "I walked here from the river. The river walked with me.", "The Guard drops what it cannot carry. I carry what the Guard drops." },
                hostileLine = "So be it.", deathLine = "The Pilgrim's cart stands empty at the roadside." },
        };
        public static IEnumerable<CharacterDef> All { get { return all; } }
        public static CharacterDef Get(string key) { return all.FirstOrDefault(c => c.key == key); }
    }

    public sealed partial class World
    {
        public readonly List<Faction> factions = new List<Faction>();
        public Faction FactionOf(Survivor s) { return s != null && s.faction >= 0 && s.faction < factions.Count ? factions[s.faction] : null; }
        public Faction FactionByKey(string key) { foreach (var f in factions) if (f.key == key) return f; return null; }
        /// <summary>Pending grenades, molotovs and pipe bombs (in flight or burning a fuse). Views draw them.</summary>
        public readonly List<Throwable> throwables = new List<Throwable>();
        public int nextThrowable;

        /// <summary>True when a and b will fight each other on sight. Combat (who a hit can damage) and the AI brains both use this.</summary>
        public bool Hostile(Survivor a, Survivor b)
        {
            if (a == null || b == null || a == b || a.faction == b.faction) return false;
            if (a.faction == 0) return HostileToCommunity(FactionOf(b), a);
            if (b.faction == 0) return HostileToCommunity(FactionOf(a), b);
            var fa = FactionOf(a); var fb = FactionOf(b);
            if (fa == null || fb == null) return false;
            if (fa.disposition == Disposition.Hunter || fb.disposition == Disposition.Hunter) return true;
            return fa.feud == fb.id && fa.feudUntil > minutes || fb.feud == fa.id && fb.feudUntil > minutes;
        }

        /// <summary>Faction f fights community member m: hunters and very low standing always; Wary/Opportunist while provoked
        /// (attacked, or an opportunist robbery); Wary also while an armed member trespasses in their home building.</summary>
        public bool HostileToCommunity(Faction f, Survivor m)
        {
            if (f == null || f.id == 0) return false;
            if (f.Hostile) return true;
            if (f.disposition == Disposition.Friendly) return false;
            if (f.provokedUntil > minutes) return true;
            if (f.disposition == Disposition.Wary && f.homeBuilding >= 0 && m != null && Armed(m))
            {
                var b = map.BuildingAt(m.pos); if (b != null && b.id == f.homeBuilding) return true;
            }
            return false;
        }
        public static bool Armed(Survivor s) { var w = s.Primary; return w != null && w.Def.IsWeapon && w.Def.damage > 0; }

        /// <summary>Living members of a faction (0 = the player's community).</summary>
        public IEnumerable<Survivor> Members(int faction) { foreach (var s in survivors) if (s.alive && s.faction == faction) yield return s; }

        /// <summary>Moves a faction's standing toward the community (clamped to -100..100).</summary>
        public void ChangeStanding(int faction, float delta)
        {
            if (faction <= 0 || faction >= factions.Count) return;
            var f = factions[faction]; f.standing = MathUtil.Clamp(f.standing + delta, -100, 100);
        }

        /// <summary>Kill-feed name: named characters and community members by name, others by group ("a Red Talon raider").</summary>
        public string DisplayName(Survivor s)
        {
            if (s == null) return "someone";
            var c = Characters.Get(s.character); if (c != null) return c.name;
            if (s.faction == 0) return s.name;
            var f = FactionOf(s); return f == null ? s.name : (StartsWithVowel(f.member) ? "an " : "a ") + f.member;
        }
        static bool StartsWithVowel(string t) { return t.Length > 0 && "AEIOUaeiou".IndexOf(t[0]) >= 0; }

        /// <summary>Living survivors in the county (the battle-royale style counter shown on the HUD).</summary>
        public int SurvivorsAlive { get { int n = 0; foreach (var s in survivors) if (s.alive) n++; return n; } }

        /// <summary>Creates factions, enclaves, named characters and rival crews away from the player's base.</summary>
        void SetupHumans()
        {
            if (factions.Count == 0) factions.Add(new Faction { id = 0, key = "community", name = "Your community", role = AiRole.Resident, disposition = Disposition.Friendly, standing = 100, art = "community", member = "survivor" });
            SpawnHumans();   // HumansSetup.cs
        }

        const int HumansVersion = 1;
        internal void WriteHumans(BinaryWriter b)
        {
            b.Write(HumansVersion);
            b.Write(factions.Count);
            foreach (var f in factions)
            {
                b.Write(f.id); b.Write(f.key); b.Write(f.name); b.Write(f.description); b.Write((byte)f.role); b.Write((byte)f.disposition); b.Write(f.standing); b.Write(f.homeBuilding);
                b.Write(f.leader); b.Write(f.member); b.Write(f.art); b.Write(f.enclave); b.Write(f.home.x); b.Write(f.home.y);
                b.Write(f.provokedUntil); b.Write(f.feud); b.Write(f.feudUntil); b.Write(f.nextRoll); b.Write(f.nextBounty); b.Write(f.hasBounty); b.Write(f.bounty.x); b.Write(f.bounty.y); b.Write(f.kills); b.Write(f.losses);
            }
            b.Write(survivors.Count);
            foreach (var s in survivors) { b.Write(s.leader); b.Write((byte)s.order); b.Write(s.squad); b.Write((byte)s.disposition); b.Write(s.offscreen); b.Write(s.lastHitBy); b.Write(s.lastHitAt); }
            b.Write(nextThrowable); b.Write(throwables.Count);
            foreach (var t in throwables) { b.Write(t.id); b.Write(t.item); b.Write(t.from.x); b.Write(t.from.y); b.Write(t.pos.x); b.Write(t.pos.y); b.Write(t.by); b.Write(t.fuse); b.Write(t.flight); b.Write(t.lure); b.Write(t.landed); }
        }
        internal void ReadHumans(BinaryReader b)
        {
            int version = b.ReadInt32(); if (version == 0) return;   // contract stub section: nothing stored
            if (version != HumansVersion) throw new InvalidDataException("Unknown humans section version " + version + ".");
            factions.Clear();
            int n = b.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var f = new Faction { id = b.ReadInt32(), key = b.ReadString(), name = b.ReadString(), description = b.ReadString(), role = (AiRole)b.ReadByte(), disposition = (Disposition)b.ReadByte(), standing = b.ReadSingle(), homeBuilding = b.ReadInt32() };
                f.leader = b.ReadString(); f.member = b.ReadString(); f.art = b.ReadString(); f.enclave = b.ReadBoolean(); f.home = new Vec2(b.ReadSingle(), b.ReadSingle());
                f.provokedUntil = b.ReadDouble(); f.feud = b.ReadInt32(); f.feudUntil = b.ReadDouble(); f.nextRoll = b.ReadDouble(); f.nextBounty = b.ReadDouble(); f.hasBounty = b.ReadBoolean(); f.bounty = new Vec2(b.ReadSingle(), b.ReadSingle()); f.kills = b.ReadInt32(); f.losses = b.ReadInt32();
                if (f.id != i) throw new InvalidDataException("Save factions are out of order.");
                factions.Add(f);
            }
            int count = b.ReadInt32(); if (count != survivors.Count) throw new InvalidDataException("Save humans do not match the survivors.");
            foreach (var s in survivors) { s.leader = b.ReadInt32(); s.order = (CompanionOrder)b.ReadByte(); s.squad = b.ReadInt32(); s.disposition = (Disposition)b.ReadByte(); s.offscreen = b.ReadBoolean(); s.lastHitBy = b.ReadInt32(); s.lastHitAt = b.ReadDouble(); }
            nextThrowable = b.ReadInt32(); throwables.Clear();
            int t = b.ReadInt32();
            for (int i = 0; i < t; i++)
            {
                var th = new Throwable { id = b.ReadInt32(), item = b.ReadString(), from = new Vec2(b.ReadSingle(), b.ReadSingle()), pos = new Vec2(b.ReadSingle(), b.ReadSingle()), by = b.ReadInt32(), fuse = b.ReadSingle(), flight = b.ReadSingle(), lure = b.ReadSingle(), landed = b.ReadBoolean() };
                if (ItemCatalog.Exists(th.item)) throwables.Add(th);
            }
        }
    }

    public sealed partial class Simulation
    {
        /// <summary>Damage to a survivor in health points (0..100 scale) from a weapon, explosion or other survivor.
        /// Worn torso armour absorbs part of it; named characters are tougher.</summary>
        public void DamageSurvivor(Survivor target, Survivor by, float damage, string cause) { HurtSurvivor(target, by, damage, cause, false); }

        /// <summary>Called once when any survivor dies (after the body drops its gear).</summary>
        void OnSurvivorKilled(Survivor s, string cause)
        {
            brains.Remove(s.id);
            var killer = s.lastHitBy >= 0 && world.minutes - s.lastHitAt < 2 ? Survivor(s.lastHitBy) : null;
            if (killer == s) killer = null;
            var vf = world.FactionOf(s); if (vf != null) vf.losses++;
            string text;
            if (killer != null)
            {
                text = Cap(world.DisplayName(killer)) + " killed " + world.DisplayName(s);
                var kf = world.FactionOf(killer); if (kf != null) kf.kills++;
                if (killer.faction == 0 && s.faction != 0 && vf != null) CommunityKilled(vf);
            }
            else text = Cap(world.DisplayName(s)) + " " + DeathPhrase(cause);
            world.Emit(EventKind.KillFeed, s.pos, killer != null ? killer.id : s.id, text);
            var c = Characters.Get(s.character);
            if (c != null && c.deathLine.Length > 0) world.Emit(EventKind.Radio, s.pos, -1, c.deathLine);
            // Companions of the dead stop following them.
            foreach (var o in world.survivors) if (o.leader == s.id) o.leader = -1;
        }

        /// <summary>Standing consequences when the community kills a member of another faction.</summary>
        void CommunityKilled(Faction vf)
        {
            if (vf.enclave)
            {
                vf.standing = Math.Min(vf.standing - 60, -60);   // murder turns an enclave hostile
                foreach (var f in world.factions) if (f.enclave && f != vf) world.ChangeStanding(f.id, -10);
            }
            else if (vf.disposition == Disposition.Hunter)
            {
                foreach (var f in world.factions) if (f.enclave) world.ChangeStanding(f.id, 6);   // the enclaves like raiders and deserters dead
            }
            else { world.ChangeStanding(vf.id, -40); vf.provokedUntil = Math.Max(vf.provokedUntil, world.minutes + 120); }
        }

        static string Cap(string t) { return string.IsNullOrEmpty(t) ? t : char.ToUpperInvariant(t[0]) + t.Substring(1); }
        static string DeathPhrase(string cause)
        {
            string c = (cause ?? "").Trim().TrimEnd('.'); if (c.Length == 0) return "died";
            string lower = char.ToLowerInvariant(c[0]) + c.Substring(1);
            if (lower.StartsWith("killed") || lower.StartsWith("dragged") || lower.StartsWith("shelled") || lower.StartsWith("blown") || lower.StartsWith("burned") || lower.StartsWith("crushed")) return "was " + lower;
            if (lower.StartsWith("bled") || lower.StartsWith("starved") || lower.StartsWith("died") || lower.StartsWith("succumbed")) return lower;
            return "died (" + lower + ")";
        }
    }
}
