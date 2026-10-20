using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Ashfall.Core
{
    /// <summary>Versioned, compressed save. The county regenerates from its seed; only changed state is stored.</summary>
    public static class SaveCodec
    {
        const uint Magic = 0x41534846; // "ASHF"
        public const int Version = 1;

        public sealed class Summary { public string name = "", profession = "", clock = "", outcome = ""; public int day, kills, seed, mapSize; public double hours; public bool alive; public long savedAtTicks; }

        public static void Save(World w, string path)
        {
            string temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var file = File.Create(temp))
            using (var zip = new GZipStream(file, CompressionLevel.Fastest))
            using (var b = new BinaryWriter(zip)) Write(w, b);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }

        public static World Load(string path)
        {
            using (var file = File.OpenRead(path))
            using (var zip = new GZipStream(file, CompressionMode.Decompress))
            using (var b = new BinaryReader(zip)) return Read(b);
        }

        public static Summary Peek(string path)
        {
            using (var file = File.OpenRead(path))
            using (var zip = new GZipStream(file, CompressionMode.Decompress))
            using (var b = new BinaryReader(zip))
            {
                Header(b); var settings = ReadSettings(b); double minutes = b.ReadDouble(); double start = b.ReadDouble(); string outcome = b.ReadString(); b.ReadInt64();
                var sum = new Summary { seed = settings.seed, mapSize = settings.mapSize, outcome = outcome, day = (int)(minutes / 1440) + 1, hours = (minutes - start) / 60 };
                sum.name = b.ReadString(); sum.profession = b.ReadString(); sum.alive = b.ReadBoolean(); sum.kills = b.ReadInt32(); sum.savedAtTicks = b.ReadInt64();
                return sum;
            }
        }

        static void Header(BinaryReader b)
        {
            if (b.ReadUInt32() != Magic) throw new InvalidDataException("Not an Ashfall County save.");
            int version = b.ReadInt32(); if (version != Version) throw new InvalidDataException("Save version " + version + " is not supported by this build.");
            int gen = b.ReadInt32(); if (gen != WorldGen.Version) throw new InvalidDataException("This save was made with a different county generator (" + gen + ").");
        }

        static void Write(World w, BinaryWriter b)
        {
            b.Write(Magic); b.Write(Version); b.Write(WorldGen.Version);
            WriteSettings(w.settings, b);
            b.Write(w.minutes); b.Write(w.startMinutes); b.Write(w.outcome ?? ""); b.Write(w.rng.State);
            // Quick summary block for the load menu.
            var lead = w.survivors.FirstOrDefault();
            b.Write(lead == null ? "" : lead.name); b.Write(lead == null ? "" : lead.profession); b.Write(lead != null && lead.alive); b.Write(lead == null ? 0 : lead.kills); b.Write(DateTime.UtcNow.Ticks);

            b.Write(w.powerOffDay); b.Write(w.waterOffDay); b.Write(w.helicopterDay); b.Write(w.helicopterMinute);
            b.Write(w.raining); b.Write(w.rainIntensity); b.Write(w.nextCorpse); b.Write(w.initialPopulation); b.Write(ItemCatalog.Uid);
            b.Write(w.flags.Count); foreach (var f in w.flags) b.Write(f);

            var m = w.map;
            b.Write(m.openings.Count);
            foreach (var o in m.openings) { b.Write(o.open); b.Write(o.broken); b.Write(o.locked); b.Write(o.glassCleared); b.Write((byte)o.barricade); b.Write(o.hp); b.Write(o.barricadeHp); }
            b.Write(w.generatedProps);
            var changed = m.props.Take(w.generatedProps).Where(p => p.modified).ToList();
            b.Write(changed.Count);
            foreach (var p in changed) { b.Write(p.id); WritePropState(p, b); }
            b.Write(m.props.Count - w.generatedProps);
            foreach (var p in m.props.Skip(w.generatedProps)) { b.Write((byte)p.kind); b.Write(p.x); b.Write(p.y); b.Write(p.container >= 0); WritePropState(p, b); }
            var rolled = m.containers.Where(c => c.generated).ToList();
            b.Write(m.containers.Count); b.Write(rolled.Count);
            foreach (var c in rolled) { b.Write(c.id); b.Write(c.prop); WriteInventory(c.inventory, b); }
            WriteBits(m.explored, b);
            foreach (var bld in m.buildings) b.Write(bld.visited);

            b.Write(w.survivors.Count); foreach (var s in w.survivors) WriteSurvivor(s, b);
            var alive = w.zombies.Where(z => z.Alive).ToList();
            b.Write(alive.Count);
            foreach (var z in alive) { b.Write(z.pos.x); b.Write(z.pos.y); b.Write(z.facing); b.Write((byte)(z.state == ZombieState.Climb || z.state == ZombieState.Attack ? ZombieState.Chase : z.state)); b.Write(z.hp); b.Write(z.speed); b.Write(z.variant); b.Write(z.female); b.Write(z.crawler); b.Write(z.hearing); b.Write(z.sight); b.Write(z.target.x); b.Write(z.target.y); b.Write(z.targetSurvivor); }
            b.Write(w.corpses.Count);
            foreach (var c in w.corpses) { b.Write(c.id); b.Write(c.pos.x); b.Write(c.pos.y); b.Write(c.facing); b.Write(c.variant); b.Write(c.female); b.Write(c.time); b.Write(c.rolled); WriteInventory(c.inventory, b); }
            var piles = w.piles.Values.Where(p => p.inventory.items.Count > 0).ToList();
            b.Write(piles.Count);
            foreach (var p in piles) { b.Write(p.x); b.Write(p.y); WriteInventory(p.inventory, b); }
        }

        static World Read(BinaryReader b)
        {
            Header(b);
            var settings = ReadSettings(b);
            WorldGen gen; ItemCatalog.Uid = 0;
            var w = new World { settings = settings };
            w.map = WorldGen.Generate(settings.seed, settings.mapSize, out gen); w.gen = gen;
            w.minutes = b.ReadDouble(); w.startMinutes = b.ReadDouble(); w.outcome = b.ReadString(); w.rng = new Rng((ulong)settings.seed, 3); w.rng.State = b.ReadUInt64();
            b.ReadString(); b.ReadString(); b.ReadBoolean(); b.ReadInt32(); b.ReadInt64();
            w.powerOffDay = b.ReadInt32(); w.waterOffDay = b.ReadInt32(); w.helicopterDay = b.ReadInt32(); w.helicopterMinute = b.ReadDouble();
            w.raining = b.ReadBoolean(); w.rainIntensity = b.ReadSingle(); w.nextCorpse = b.ReadInt32(); w.initialPopulation = b.ReadInt32(); int uid = b.ReadInt32();
            int flags = b.ReadInt32(); for (int i = 0; i < flags; i++) w.flags.Add(b.ReadString());

            var m = w.map;
            int openings = b.ReadInt32(); if (openings != m.openings.Count) throw new InvalidDataException("Save does not match its county.");
            foreach (var o in m.openings) { o.open = b.ReadBoolean(); o.broken = b.ReadBoolean(); o.locked = b.ReadBoolean(); o.glassCleared = b.ReadBoolean(); o.barricade = b.ReadByte(); o.hp = b.ReadSingle(); o.barricadeHp = b.ReadSingle(); }
            int generated = b.ReadInt32(); if (generated != m.props.Count) throw new InvalidDataException("Save does not match its county.");
            w.generatedProps = generated;
            int changed = b.ReadInt32();
            for (int i = 0; i < changed; i++) { var p = m.props[b.ReadInt32()]; ReadPropState(p, b); p.modified = true; if (p.removed) { p.removed = false; m.RemoveProp(p); } }
            int added = b.ReadInt32();
            for (int i = 0; i < added; i++)
            {
                var kind = (PropKind)b.ReadByte(); int x = b.ReadInt32(), y = b.ReadInt32(); bool container = b.ReadBoolean();
                var p = m.AddProp(kind, x, y); p.modified = true; ReadPropState(p, b);
                if (p.removed) { p.removed = false; m.RemoveProp(p); }
                if (container) m.AddContainer(p, "").generated = true;
            }
            int total = b.ReadInt32(); if (total != m.containers.Count) throw new InvalidDataException("Save containers do not match.");
            int rolled = b.ReadInt32();
            for (int i = 0; i < rolled; i++) { var c = m.containers[b.ReadInt32()]; b.ReadInt32(); c.generated = true; c.inventory = ReadInventory(b, c.inventory.capacity); }
            ReadBits(m.explored, b);
            foreach (var bld in m.buildings) bld.visited = b.ReadBoolean();

            int survivors = b.ReadInt32(); for (int i = 0; i < survivors; i++) w.survivors.Add(ReadSurvivor(b));
            int zombies = b.ReadInt32();
            for (int i = 0; i < zombies; i++)
            {
                var z = new Zombie { id = w.zombies.Count };
                z.pos = new Vec2(b.ReadSingle(), b.ReadSingle()); z.facing = b.ReadSingle(); z.state = (ZombieState)b.ReadByte(); z.hp = b.ReadSingle(); z.speed = b.ReadSingle(); z.variant = b.ReadInt32(); z.female = b.ReadBoolean(); z.crawler = b.ReadBoolean(); z.hearing = b.ReadSingle(); z.sight = b.ReadSingle(); z.target = new Vec2(b.ReadSingle(), b.ReadSingle()); z.targetSurvivor = b.ReadInt32();
                w.zombies.Add(z);
            }
            int corpses = b.ReadInt32();
            for (int i = 0; i < corpses; i++) { var c = new Corpse { id = b.ReadInt32(), pos = new Vec2(b.ReadSingle(), b.ReadSingle()), facing = b.ReadSingle(), variant = b.ReadInt32(), female = b.ReadBoolean(), time = b.ReadDouble(), rolled = b.ReadBoolean() }; c.inventory = ReadInventory(b, 999); w.corpses.Add(c); }
            int piles = b.ReadInt32();
            for (int i = 0; i < piles; i++) { var p = new Pile { x = b.ReadInt32(), y = b.ReadInt32() }; p.inventory = ReadInventory(b, 999); w.piles[m.Index(p.x, p.y)] = p; }
            ItemCatalog.Uid = uid;
            w.index = new ZombieIndex(w);
            return w;
        }

        static void WriteSettings(SandboxSettings s, BinaryWriter b)
        {
            b.Write(s.seed); b.Write(s.mapSize); b.Write((int)s.population); b.Write((int)s.speed); b.Write(s.dayLengthMinutes); b.Write(s.utilitiesMinDay); b.Write(s.utilitiesMaxDay); b.Write(s.evacuationDay); b.Write(s.lootAbundance); b.Write(s.infection); b.Write(s.respawn);
        }
        static SandboxSettings ReadSettings(BinaryReader b)
        {
            return new SandboxSettings { seed = b.ReadInt32(), mapSize = b.ReadInt32(), population = (Population)b.ReadInt32(), speed = (ZombieSpeed)b.ReadInt32(), dayLengthMinutes = b.ReadInt32(), utilitiesMinDay = b.ReadInt32(), utilitiesMaxDay = b.ReadInt32(), evacuationDay = b.ReadInt32(), lootAbundance = b.ReadSingle(), infection = b.ReadBoolean(), respawn = b.ReadBoolean() };
        }
        static void WritePropState(Prop p, BinaryWriter b) { b.Write(p.removed); b.Write(p.lit); b.Write(p.fuel); b.Write(p.water); b.Write(p.hp); }
        static void ReadPropState(Prop p, BinaryReader b) { p.removed = b.ReadBoolean(); p.lit = b.ReadBoolean(); p.fuel = b.ReadSingle(); p.water = b.ReadSingle(); p.hp = b.ReadSingle(); }

        static void WriteInventory(Inventory inv, BinaryWriter b)
        {
            b.Write(inv.capacity); b.Write(inv.items.Count);
            foreach (var s in inv.items) { b.Write(s.uid); b.Write(s.def); b.Write(s.count); b.Write(s.condition); b.Write(s.water); b.Write(s.tainted); b.Write(s.loaded); b.Write(s.ageHours); b.Write(s.cooked); }
        }
        static Inventory ReadInventory(BinaryReader b, float fallbackCapacity)
        {
            var inv = new Inventory { capacity = b.ReadSingle() }; int n = b.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var s = new ItemStack { uid = b.ReadInt32(), def = b.ReadString(), count = b.ReadInt32(), condition = b.ReadSingle(), water = b.ReadInt32(), tainted = b.ReadBoolean(), loaded = b.ReadInt32(), ageHours = b.ReadSingle(), cooked = b.ReadBoolean() };
                if (ItemCatalog.Exists(s.def)) inv.items.Add(s);
            }
            return inv;
        }

        static void WriteSurvivor(Survivor s, BinaryWriter b)
        {
            b.Write(s.id); b.Write(s.name); b.Write(s.profession); b.Write(s.female); b.Write(s.pos.x); b.Write(s.pos.y); b.Write(s.facing); b.Write(s.alive);
            foreach (var v in new[] { s.health, s.hunger, s.thirst, s.fatigue, s.endurance, s.panic, s.pain, s.boredom, s.sickness, s.wetness, s.stress, s.infectionHours, s.wellFedHours }) b.Write(v);
            b.Write(s.zombieInfected); b.Write(s.kills); b.Write(s.primary); b.Write(s.secondary); b.Write(s.lightOn); b.Write(s.spawnedAt); b.Write(s.diedAt); b.Write(s.causeOfDeath ?? "");
            b.Write(s.worn.Count); foreach (var kv in s.worn) { b.Write((int)kv.Key); b.Write(kv.Value); }
            WriteInventory(s.inventory, b);
            b.Write(s.wounds.Count);
            foreach (var w in s.wounds) { b.Write((int)w.part); b.Write((int)w.kind); b.Write(w.severity); b.Write(w.bleeding); b.Write(w.bandaged); b.Write(w.dirtyBandage); b.Write(w.disinfected); b.Write(w.infected); b.Write(w.sutured); b.Write(w.bandageAge); }
            b.Write(s.xp.Length); for (int i = 0; i < s.xp.Length; i++) { b.Write(s.xp[i]); b.Write(s.level[i]); }
        }
        static Survivor ReadSurvivor(BinaryReader b)
        {
            var s = new Survivor { id = b.ReadInt32(), name = b.ReadString(), profession = b.ReadString(), female = b.ReadBoolean(), pos = new Vec2(b.ReadSingle(), b.ReadSingle()), facing = b.ReadSingle(), alive = b.ReadBoolean() };
            s.health = b.ReadSingle(); s.hunger = b.ReadSingle(); s.thirst = b.ReadSingle(); s.fatigue = b.ReadSingle(); s.endurance = b.ReadSingle(); s.panic = b.ReadSingle(); s.pain = b.ReadSingle(); s.boredom = b.ReadSingle(); s.sickness = b.ReadSingle(); s.wetness = b.ReadSingle(); s.stress = b.ReadSingle(); s.infectionHours = b.ReadSingle(); s.wellFedHours = b.ReadSingle();
            s.zombieInfected = b.ReadBoolean(); s.kills = b.ReadInt32(); s.primary = b.ReadInt32(); s.secondary = b.ReadInt32(); s.lightOn = b.ReadBoolean(); s.spawnedAt = b.ReadDouble(); s.diedAt = b.ReadDouble(); s.causeOfDeath = b.ReadString();
            int worn = b.ReadInt32(); for (int i = 0; i < worn; i++) s.worn[(WearSlot)b.ReadInt32()] = b.ReadInt32();
            s.inventory = ReadInventory(b, 15);
            int wounds = b.ReadInt32();
            for (int i = 0; i < wounds; i++) s.wounds.Add(new Wound { part = (BodyPart)b.ReadInt32(), kind = (WoundKind)b.ReadInt32(), severity = b.ReadSingle(), bleeding = b.ReadBoolean(), bandaged = b.ReadBoolean(), dirtyBandage = b.ReadBoolean(), disinfected = b.ReadBoolean(), infected = b.ReadBoolean(), sutured = b.ReadBoolean(), bandageAge = b.ReadSingle() });
            int skills = b.ReadInt32(); for (int i = 0; i < skills; i++) { float xp = b.ReadSingle(); int lvl = b.ReadInt32(); if (i < s.xp.Length) { s.xp[i] = xp; s.level[i] = lvl; } }
            return s;
        }

        static void WriteBits(bool[] bits, BinaryWriter b)
        {
            b.Write(bits.Length); byte cur = 0;
            for (int i = 0; i < bits.Length; i++) { if (bits[i]) cur |= (byte)(1 << (i & 7)); if ((i & 7) == 7) { b.Write(cur); cur = 0; } }
            if ((bits.Length & 7) != 0) b.Write(cur);
        }
        static void ReadBits(bool[] bits, BinaryReader b)
        {
            int n = b.ReadInt32(); if (n != bits.Length) throw new InvalidDataException("Explored map size differs.");
            byte cur = 0;
            for (int i = 0; i < n; i++) { if ((i & 7) == 0) cur = b.ReadByte(); bits[i] = (cur & (1 << (i & 7))) != 0; }
        }
    }
}
