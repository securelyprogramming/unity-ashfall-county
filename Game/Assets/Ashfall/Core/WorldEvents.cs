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
        public int prop = -1;                 // the SupplyCrate prop once landed
        public bool planeSeen;                // the "plane" event has fired
        public int initialItems;              // crate contents at landing (taking anything also counts as opening it)
        public double ExpiresAt { get { return landsAt + World.AirdropLifetimeMinutes; } }
    }

    /// <summary>Guard artillery "cleansing" of a circle: warned on the radio, then shelled for a while.</summary>
    [Serializable]
    public sealed class RedZone
    {
        public Vec2 center; public float radius;
        public double warnAt, startsAt, endsAt;   // in-game minutes
        public string area = "";                  // landmark or town named in the warning
        public float shellClock;                  // real seconds to the next shell
        public int shells;                        // shells fired so far
        public bool Active(double minutes) { return minutes >= startsAt && minutes < endsAt; }
        public bool Warning(double minutes) { return minutes >= warnAt && minutes < startsAt; }
        public bool Contains(Vec2 p) { return Vec2.Distance(p, center) <= radius; }
    }

    public sealed partial class World
    {
        public readonly List<Airdrop> airdrops = new List<Airdrop>();
        public RedZone redZone;                    // current or next zone; null when none is scheduled
        public double nextAirdropAt, nextRedZoneAt;   // landing time of the next drop; shelling start of the next red zone (in-game minutes)
        public int airdropCount;                   // drops so far (ids)
        /// <summary>World-event randomness has its own seeded stream (saved), so events neither shift nor depend on other systems' rolls.</summary>
        public Rng eventRng;

        // Game-owned timings, in in-game minutes.
        public const double AirdropWarningMinutes = 30, PlaneLeadMinutes = 3, AirdropLifetimeMinutes = 2 * 1440;
        public const double FirstAirdropMinutes = 120, RedZoneWarningMinutes = 30, RedZoneShellingMinutes = 20;

        /// <summary>Schedules the first airdrop (about two in-game hours in) and red zone (8 to 14 hours in).</summary>
        void SetupWorldEvents()
        {
            airdrops.Clear(); redZone = null; airdropCount = 0;
            eventRng = new Rng((ulong)settings.seed, 29);
            nextAirdropAt = minutes + FirstAirdropMinutes + eventRng.Range(-15f, 15f);
            nextRedZoneAt = minutes + eventRng.Range(8f, 14f) * 60;
        }

        public Rng EventRng { get { if (eventRng == null) eventRng = new Rng((ulong)settings.seed, 29); return eventRng; } }

        /// <summary>The landmark or town named for a point ("Fort Ashby", "the open county near Millbrook").</summary>
        public string AreaName(Vec2 p)
        {
            var l = gen == null ? null : gen.NearestLandmark(p);
            if (l == null) return "the open county";
            float d = l.rect.DistanceTo(p);
            return d < 60 ? l.name : d < 600 ? "the countryside near " + l.name : "the open county";
        }

        internal void WriteEvents(BinaryWriter b)
        {
            b.Write(1);   // section version
            b.Write(nextAirdropAt); b.Write(nextRedZoneAt); b.Write(airdropCount); b.Write(EventRng.State);
            b.Write(airdrops.Count);
            foreach (var a in airdrops)
            {
                b.Write(a.id); b.Write(a.pos.x); b.Write(a.pos.y); b.Write(a.announcedAt); b.Write(a.landsAt); b.Write(a.landed); b.Write(a.opened);
                b.Write(a.container); b.Write(a.prop); b.Write(a.planeHeading); b.Write(a.planeSeen); b.Write(a.initialItems);
            }
            b.Write(redZone != null);
            if (redZone != null)
            {
                var z = redZone;
                b.Write(z.center.x); b.Write(z.center.y); b.Write(z.radius); b.Write(z.warnAt); b.Write(z.startsAt); b.Write(z.endsAt); b.Write(z.area ?? ""); b.Write(z.shellClock); b.Write(z.shells);
            }
        }
        internal void ReadEvents(BinaryReader b)
        {
            airdrops.Clear(); redZone = null;
            int version = b.ReadInt32(); if (version == 0) return;   // written before world events existed
            if (version != 1) throw new InvalidDataException("Unknown world events section version " + version + ".");
            nextAirdropAt = b.ReadDouble(); nextRedZoneAt = b.ReadDouble(); airdropCount = b.ReadInt32(); EventRng.State = b.ReadUInt64();
            int n = b.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var a = new Airdrop { id = b.ReadInt32(), pos = new Vec2(b.ReadSingle(), b.ReadSingle()), announcedAt = b.ReadDouble(), landsAt = b.ReadDouble(), landed = b.ReadBoolean(), opened = b.ReadBoolean() };
                a.container = b.ReadInt32(); a.prop = b.ReadInt32(); a.planeHeading = b.ReadSingle(); a.planeSeen = b.ReadBoolean(); a.initialItems = b.ReadInt32();
                if (a.landed && (a.container < 0 || a.container >= map.containers.Count || a.prop < 0 || a.prop >= map.props.Count || map.props[a.prop].kind != PropKind.SupplyCrate))
                    throw new InvalidDataException("Save airdrops do not match the county.");
                airdrops.Add(a);
            }
            if (b.ReadBoolean())
                redZone = new RedZone { center = new Vec2(b.ReadSingle(), b.ReadSingle()), radius = b.ReadSingle(), warnAt = b.ReadDouble(), startsAt = b.ReadDouble(), endsAt = b.ReadDouble(), area = b.ReadString(), shellClock = b.ReadSingle(), shells = b.ReadInt32() };
        }
    }

    public sealed partial class Simulation
    {
        /// <summary>Artillery shell: blast radius (m) and damage (zombie hit points, as Blast).</summary>
        public const float ShellRadius = 6, ShellDamage = 3.5f;

        /// <summary>Airdrops and red zones: scheduling, radio warnings, landing, opening, expiry and shelling.</summary>
        void UpdateWorldEvents(float dt, float hours)
        {
            UpdateAirdrops();
            UpdateRedZone(hours);
        }

        void Radio(Vec2 pos, string text) { world.Emit(EventKind.Radio, pos, world.playerId, text); }

        // ---------------------------------------------------------------- airdrops
        void UpdateAirdrops()
        {
            var w = world;
            if (w.settings.airdrops && w.minutes >= w.nextAirdropAt - World.AirdropWarningMinutes && !w.airdrops.Any(a => !a.landed))
            {
                var site = AirdropSite();
                var drop = new Airdrop { id = w.airdropCount++, pos = site, announcedAt = w.minutes, landsAt = Math.Max(w.nextAirdropAt, w.minutes + 5), planeHeading = w.EventRng.Range(-3.1416f, 3.1416f) };
                w.airdrops.Add(drop);
                w.nextAirdropAt = drop.landsAt + w.EventRng.Range(6f, 10f) * 60;
                Radio(site, "Guard broadcast: supply drop inbound over " + w.AreaName(site) + " in about " + (int)Math.Round(drop.landsAt - w.minutes) + " minutes. Red smoke marks the crate.");
                w.Emit(EventKind.Airdrop, site, -1, "announce");
            }
            for (int i = w.airdrops.Count - 1; i >= 0; i--)
            {
                var a = w.airdrops[i];
                if (!a.landed)
                {
                    if (!a.planeSeen && w.minutes >= a.landsAt - World.PlaneLeadMinutes) { a.planeSeen = true; w.Emit(EventKind.Airdrop, a.pos, -1, "plane"); }
                    if (w.minutes >= a.landsAt) Land(a);
                    continue;
                }
                if (!a.opened)
                {
                    var c = w.map.containers[a.container]; int by = -1;
                    foreach (var s in w.survivors) if (s.alive && s.lootContainer == a.container) { by = s.id; break; }
                    if (by >= 0 || c.inventory.items.Count != a.initialItems) { a.opened = true; w.Emit(EventKind.Airdrop, a.pos, by, "open"); }
                }
                if (w.minutes >= a.ExpiresAt)
                {
                    // Old drops are cleared away; whatever is left in the crate goes with it.
                    var p = w.map.props[a.prop]; w.map.RemoveProp(p); p.modified = true;
                    foreach (var s in w.survivors) if (s.lootContainer == a.container) s.lootContainer = -1;
                    w.airdrops.RemoveAt(i);
                }
            }
        }

        /// <summary>The crate lands on open dry ground (re-checked now, in case something moved there), loud enough to draw the dead.</summary>
        void Land(Airdrop a)
        {
            var w = world; Vec2 at = a.pos;
            if (!OpenDropGround((int)at.x, (int)at.y))
            {
                bool found = false;
                for (int r = 1; r <= 12 && !found; r++)
                    for (int k = 0; k < 16 && !found; k++)
                    {
                        var q = a.pos + Vec2.FromAngle(k * .3927f) * r;
                        if (OpenDropGround((int)q.x, (int)q.y)) { at = q; found = true; }
                    }
                if (!found) at = AirdropSite();
            }
            int x = (int)at.x, y = (int)at.y;
            var prop = w.map.AddProp(PropKind.SupplyCrate, x, y); prop.modified = true; prop.rotation = w.EventRng.Range(0, 4);
            var c = w.map.AddContainer(prop, "airdrop");
            Loot.Roll(c, (ulong)w.settings.seed ^ (ulong)(a.id * 7919 + 17), Math.Max(1, w.settings.lootAbundance));   // rolled now so saves keep it
            a.container = c.id; a.prop = prop.id; a.landed = true; a.pos = prop.Center; a.initialItems = c.inventory.items.Count;
            w.Emit(EventKind.Airdrop, a.pos, -1, "land");
            w.MakeNoise(a.pos, 140);
        }

        /// <summary>Open, dry, outdoor ground with a clear 5x5 patch (no walls, fences, props, water or rooms), away from the home base.</summary>
        bool OpenDropGround(int x, int y)
        {
            var m = world.map;
            if (x < 12 || y < 12 || x >= m.width - 12 || y >= m.height - 12) return false;
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    int tx = x + dx, ty = y + dy; var g = m.GroundAt(tx, ty);
                    if (m.TileBlocked(tx, ty) || m.RoomAt(tx, ty) != null || g == Ground.Water || g == Ground.Sand || g == Ground.Bridge) return false;
                    if (m.H(tx, ty) != Edge.None || m.V(tx, ty) != Edge.None) return false;
                }
            var c = world.community;
            if (c != null && c.baseBuilding >= 0 && Vec2.Distance(new Vec2(x, y), c.basePos) < 20) return false;
            return true;
        }

        /// <summary>A drop site: usually a few hundred metres from the controlled survivor (so it can be contested), otherwise anywhere.</summary>
        Vec2 AirdropSite()
        {
            var w = world; var focus = w.Controlled; var m = w.map;
            for (int i = 0; i < 300; i++)
            {
                Vec2 p = focus != null && focus.alive && i < 200 && w.EventRng.Chance(.65f)
                    ? focus.pos + Vec2.FromAngle(w.EventRng.Range(0, 6.2832f)) * w.EventRng.Range(120f, 450f)
                    : new Vec2(w.EventRng.Range(40, m.width - 40), w.EventRng.Range(40, m.height - 40));
                if (OpenDropGround((int)p.x, (int)p.y)) return new Vec2((int)p.x + .5f, (int)p.y + .5f);
            }
            if (w.gen != null) foreach (var z in w.gen.landingZones) if (OpenDropGround(z.x, z.y)) return z.Center;
            return new Vec2(m.width / 2f, m.height / 2f);
        }

        // ---------------------------------------------------------------- red zones
        void UpdateRedZone(float hours)
        {
            var w = world; var z = w.redZone;
            if (z == null)
            {
                if (!w.settings.redZones || w.minutes < w.nextRedZoneAt - World.RedZoneWarningMinutes) return;
                z = w.redZone = PlanRedZone();
                z.warnAt = w.minutes; z.startsAt = Math.Max(w.nextRedZoneAt, w.minutes + 5); z.endsAt = z.startsAt + World.RedZoneShellingMinutes;
                w.nextRedZoneAt = z.endsAt + w.EventRng.Range(8f, 14f) * 60;
                w.Emit(EventKind.Artillery, z.center, -1, "warn");
                Radio(z.center, "Guard broadcast: artillery will shell " + z.area + " in " + (int)Math.Round(z.startsAt - w.minutes) + " minutes. Everyone within " + (int)z.radius + " metres, get clear of the red zone.");
                return;
            }
            if (w.minutes >= z.endsAt)
            {
                w.Emit(EventKind.Artillery, z.center, -1, "clear"); w.redZone = null;
                return;
            }
            if (!z.Active(w.minutes)) return;
            // Shells land every couple of real seconds. The clock runs on game time (scaled back to real seconds) so a
            // fast-forward keeps the same density; at most a few per tick.
            z.shellClock -= hours * 60 / Math.Max(.01f, w.TimeScale);
            for (int fired = 0; z.shellClock <= 0 && fired < 3; fired++)
            {
                z.shellClock += w.EventRng.Range(1.4f, 2.6f);
                float r = z.radius * (float)Math.Sqrt(w.EventRng.Float()); var at = z.center + Vec2.FromAngle(w.EventRng.Range(0, 6.2832f)) * r;
                z.shells++;
                w.Emit(EventKind.Artillery, at, -1, "shell");
                Blast(at, ShellRadius, ShellDamage, null, "artillery");
            }
            if (z.shellClock < 0) z.shellClock = 0;
        }

        /// <summary>Picks a circle of 60-120 m on land: sometimes close to the controlled survivor, otherwise around a landmark
        /// or a random spot. Never over the community's home base.</summary>
        RedZone PlanRedZone()
        {
            var w = world; var m = w.map; var focus = w.Controlled;
            var c = w.community; bool hasBase = c != null && c.baseBuilding >= 0;
            float radius = w.EventRng.Range(60f, 120f);
            Vec2 center = new Vec2(m.width / 2f, m.height / 2f);
            for (int i = 0; i < 200; i++)
            {
                Vec2 p; float roll = w.EventRng.Float();
                if (focus != null && focus.alive && roll < .35f) p = focus.pos + Vec2.FromAngle(w.EventRng.Range(0, 6.2832f)) * w.EventRng.Range(radius * .3f, radius + 150);
                else if (w.gen != null && w.gen.landmarks.Count > 0 && roll < .75f) { var l = w.gen.landmarks[w.EventRng.Range(0, w.gen.landmarks.Count)]; p = l.Center + Vec2.FromAngle(w.EventRng.Range(0, 6.2832f)) * w.EventRng.Range(0, Math.Min(l.radius, 150)); }
                else p = new Vec2(w.EventRng.Range(radius, m.width - radius), w.EventRng.Range(radius, m.height - radius));
                if (p.x < radius || p.y < radius || p.x > m.width - radius || p.y > m.height - radius) continue;
                if (m.IsWater((int)p.x, (int)p.y)) continue;
                if (hasBase && Vec2.Distance(p, c.basePos) < radius + 40) continue;
                center = p; break;
            }
            return new RedZone { center = center, radius = radius, area = w.AreaName(center) };
        }
    }
}
