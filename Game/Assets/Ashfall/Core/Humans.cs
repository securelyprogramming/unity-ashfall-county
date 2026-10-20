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
    }

    public sealed partial class Survivor
    {
        public int leader = -1;                         // companion: survivor id being followed
        public CompanionOrder order = CompanionOrder.Follow;
        public int squad = -1;                          // rival crew id (members share goals)
        public Disposition disposition = Disposition.Wary;
    }

    /// <summary>One line of a trade/talk menu offered by an NPC. The host re-validates when the Trade command arrives.</summary>
    public sealed class TradeOffer { public string label = "", detail = ""; public bool enabled = true; public string reason = ""; }

    public static class Characters
    {
        static readonly List<CharacterDef> all = new List<CharacterDef>();
        public static IEnumerable<CharacterDef> All { get { return all; } }
        public static CharacterDef Get(string key) { return all.FirstOrDefault(c => c.key == key); }
    }

    public sealed partial class World
    {
        public readonly List<Faction> factions = new List<Faction>();
        public Faction FactionOf(Survivor s) { return s != null && s.faction >= 0 && s.faction < factions.Count ? factions[s.faction] : null; }

        /// <summary>True when a and b will fight each other on sight.</summary>
        public bool Hostile(Survivor a, Survivor b)
        {
            if (a == null || b == null || a == b || a.faction == b.faction) return false;
            var other = a.faction == 0 ? FactionOf(b) : b.faction == 0 ? FactionOf(a) : null;
            return other != null ? other.Hostile : false;
        }

        /// <summary>Living survivors in the county (the battle-royale style counter shown on the HUD).</summary>
        public int SurvivorsAlive { get { int n = 0; foreach (var s in survivors) if (s.alive) n++; return n; } }

        /// <summary>Creates factions, enclaves, named characters and rival crews away from the player's base.</summary>
        void SetupHumans() { if (factions.Count == 0) factions.Add(new Faction { id = 0, name = "Your community", role = AiRole.Resident, disposition = Disposition.Friendly, standing = 100 }); }

        internal void WriteHumans(BinaryWriter b) { b.Write(0); }
        internal void ReadHumans(BinaryReader b) { b.ReadInt32(); }
    }

    public sealed partial class Simulation
    {
        /// <summary>AI brains for every non-player survivor: submit InputState/Commands through SetInput/Submit, and LOD far survivors.</summary>
        void UpdateHumans(float dt, float hours, bool perceive) { }

        /// <summary>Human-specific commands (talk, trade, companion orders). Return true when handled.</summary>
        bool ExecuteHumans(Survivor s, Command c) { return false; }

        /// <summary>What an NPC says when the player talks to them (depends on faction, standing and character).</summary>
        public string Greeting(Survivor s, Survivor npc) { return npc == null ? "" : npc.name + " eyes you warily."; }
        /// <summary>Trade/talk options; Command.Trade with slot = index executes one.</summary>
        public List<TradeOffer> TradeOffers(Survivor s, Survivor npc) { return new List<TradeOffer>(); }
        public bool CanTalk(Survivor s, Survivor npc) { return npc != null && npc.alive && npc != s && npc.ai != AiRole.None && !world.Hostile(s, npc) && Vec2.Distance(s.pos, npc.pos) < 2.5f; }

        /// <summary>Called once when any survivor dies (after the body drops its gear).</summary>
        void OnSurvivorKilled(Survivor s, string cause) { }

        /// <summary>Damage to a survivor in health points (0..100 scale) from a weapon, explosion or other survivor.</summary>
        public void DamageSurvivor(Survivor target, Survivor by, float damage, string cause)
        {
            if (target == null || !target.alive || damage <= 0) return;
            target.health -= damage; target.hurtFlash = .5f;
            if (by != null) target.lastHitBy = by.id;
            world.Emit(EventKind.Hurt, target.pos, target.id, cause);
            if (target.health <= 0) Kill(target, by != null ? "Killed by " + by.name + "." : cause);
        }
    }
}
