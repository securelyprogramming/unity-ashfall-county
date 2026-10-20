using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>A Guard supply drop: a plane passes, a crate parachutes down with rare loot and red smoke.</summary>
    [Serializable]
    public sealed class Airdrop
    {
        public int id; public Vec2 pos;
        public double announcedAt, landsAt;   // in-game minutes
        public bool landed, opened;
        public int container = -1;            // map container id of the crate once landed
        public float planeHeading;            // radians, for the fly-over presentation
    }

    /// <summary>Guard artillery "cleansing" of a circle: warned on the radio, then shelled for a while.</summary>
    [Serializable]
    public sealed class RedZone
    {
        public Vec2 center; public float radius;
        public double warnAt, startsAt, endsAt;   // in-game minutes
        public bool Active(double minutes) { return minutes >= startsAt && minutes < endsAt; }
        public bool Warning(double minutes) { return minutes >= warnAt && minutes < startsAt; }
    }

    public sealed partial class World
    {
        public readonly List<Airdrop> airdrops = new List<Airdrop>();
        public RedZone redZone;                    // current or next zone; null when none is scheduled
        public double nextAirdropAt, nextRedZoneAt;

        /// <summary>Schedules the first airdrop and red zone.</summary>
        void SetupWorldEvents() { }

        internal void WriteEvents(BinaryWriter b) { b.Write(0); }
        internal void ReadEvents(BinaryReader b) { b.ReadInt32(); }
    }

    public sealed partial class Simulation
    {
        /// <summary>Airdrops, red zones and the helicopter: scheduling, landing, shelling.</summary>
        void UpdateWorldEvents(float dt, float hours) { }
    }
}
