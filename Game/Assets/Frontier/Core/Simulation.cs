using System;
using System.Collections.Generic;

namespace Mayo.Frontier
{
    /// <summary>Host-owned fixed-step rules. Only transport-authenticated player IDs may call Apply.
    /// State is a read-only-by-convention snapshot DTO; clients must never send state back.</summary>
    public sealed partial class Simulation
    {
        public static readonly string[] Modes = { "ashfall-county", "dead-orbit", "skyhold-siege", "rift-runners", "thornvale", "havenfall-coalition", "emberwake-rebuilders", "fjordbound", "gatebreakers-guild", "market-day", "hearth-rush", "stormline-express" };
        public World State { get; private set; }
        public Config Configuration { get; private set; }
        public const float Step = 0.05f, MapRadius = 21, InteractionRange = 2.4f;
        private int nextId;
        private uint random;
        private readonly Dictionary<int, int> disconnectedSequences = new Dictionary<int, int>();
        private double accumulator;
        private float waveClock, spawnClock, communityClock;
        private int spawned, wavesFinished, communityPulses;

        public Simulation(Config config, int chapter = 1)
        {
            if (config == null) throw new ArgumentNullException("config");
            string mode = string.IsNullOrEmpty(config.mode) ? config.id : config.mode;
            if (Array.IndexOf(Modes, mode) < 0) throw new ArgumentException("Unknown game mode: " + mode);
            Configuration = new Config { id = config.id ?? mode, title = config.title ?? mode, mode = mode, seed = config.seed, difficulty = Clamp(config.difficulty, 1, 3) };
            State = new World { configId = Configuration.id, mode = mode, chapter = Clamp(chapter, 1, 3) };
            AddPlayer(0);
            BuildChapter();
        }
        public bool AddPlayer(int id)
        {
            if (id < 0 || id > 9999 || ConnectedCount() >= 4 || State.phase == "won" || State.phase == "lost") return false;
            Player old = PlayerById(id);
            if (old != null) { if (old.connected) return false; old.connected = true; old.dx = old.dz = 0; if (State.phase == "paused") State.phase = "active"; return true; }
            if (State.players.Count >= 4) State.players.RemoveAll(p => !p.connected);
            int priorSequence; disconnectedSequences.TryGetValue(id, out priorSequence);
            State.players.Add(new Player { id = id, x = -15 + ConnectedCount() * 1.3f, z = -14, lastSequence = disconnectedSequences.ContainsKey(id) ? priorSequence : -1 });
            // Joining makes a solo pause end so one client cannot freeze the others.
            if (State.phase == "paused") State.phase = "active";
            return true;
        }
        public bool Disconnect(int id)
        {
            Player p = PlayerById(id);
            if (id == 0 || p == null || !p.connected) return false;
            p.connected = false; p.dx = p.dz = 0; disconnectedSequences[id] = p.lastSequence; return true;
        }
        public ActionResult Apply(int playerId, Command command)
        {
            Player p = PlayerById(playerId);
            if (p == null || !p.connected) return Reject("Unknown or disconnected player.");
            if (command == null) return Reject("Missing command.");
            if (command.sequence <= p.lastSequence || command.sequence < 0) return Reject("Stale or duplicated command sequence.");
            // Rejected commands also consume their sequence, preventing a modified replay.
            p.lastSequence = command.sequence;
            if (!Finite(command.x) || !Finite(command.z) || !Finite(command.aimX) || !Finite(command.aimZ)) return Reject("Non-finite input.");
            if (Math.Abs(command.x) > 1024 || Math.Abs(command.z) > 1024 || Math.Abs(command.aimX) > 1024 || Math.Abs(command.aimZ) > 1024) return Reject("Input vector exceeds the bounded protocol range.");
            string kind = command.kind ?? "";
            if (kind == "join") return playerId == 0 && AddPlayer(command.targetId) ? Accept("Player joined.") : Reject("Only the host can admit an available player slot.");
            if (kind == "disconnect") return playerId == 0 && Disconnect(command.targetId) ? Accept("Player disconnected.") : Reject("Only the host can disconnect a client.");
            if (kind == "pause")
            {
                if (playerId != 0 || ConnectedCount() > 1) return Reject("Pause is available to the solo host only.");
                if (State.phase != "active" && State.phase != "paused") return Reject("This chapter has ended.");
                State.phase = State.phase == "paused" ? "active" : "paused";
                accumulator = 0; StopPlayers(); return Accept(State.phase == "paused" ? "Paused." : "Resumed.");
            }
            if (kind == "advance")
            {
                if (playerId != 0 || State.phase != "won" || State.chapter >= 3) return Reject("Win the current chapter before advancing; chapter three is the finale.");
                State.chapter++; BuildChapter(); return Accept("New chapter started.");
            }
            if (kind == "restart")
            {
                if (playerId != 0 || (State.phase != "won" && State.phase != "lost")) return Reject("Only the host may retry an ended chapter.");
                BuildChapter(); return Accept("Chapter retried.");
            }
            if (State.phase != "active") return Reject("The simulation is paused or this chapter has ended.");
            if (p.dead) return Reject("This player is down. A teammate can revive them nearby.");
            if (kind == "move")
            {
                float length = Length(command.x, command.z); float divisor = Math.Max(1, length);
                p.dx = command.x / divisor; p.dz = command.z / divisor;
                if (length > 0.02f) { p.faceX = p.dx; p.faceZ = p.dz; }
                SetAim(p, command.aimX, command.aimZ); return Accept("");
            }
            if (kind == "aim") { if (Length(command.aimX, command.aimZ) < .01f) return Reject("Aim requires a direction."); SetAim(p, command.aimX, command.aimZ); return Accept(""); }
            if (kind == "attack") return Attack(p, command);
            if (kind == "interact") return Interact(p, command);
            if (kind == "build") return Build(p, command);
            if (kind == "craft") return Craft(p, command.option ?? "");
            if (kind == "assign") return Assign(p, command);
            return Reject("Unknown command.");
        }
        public void Tick(float seconds)
        {
            if (!Finite(seconds) || seconds <= 0 || seconds > 2 || State.phase != "active") return;
            accumulator += seconds;
            // Host tick is bounded; catch-up is explicit and uses the same 20 Hz steps.
            while (accumulator + 0.0000001 >= Step && State.phase == "active") { accumulator -= Step; FixedTick(); }
        }
        public Player PlayerById(int id) { return State.players.Find(p => p.id == id); }
        public Enemy EnemyById(int id) { return State.enemies.Find(e => e.id == id); }
        public Structure StructureById(int id) { return State.structures.Find(s => s.id == id); }
        public TaskState TaskById(int id) { return State.tasks.Find(t => t.id == id); }
        public Pickup PickupById(int id) { return State.pickups.Find(p => p.id == id); }
        public int ConnectedCount() { int count = 0; foreach (Player p in State.players) if (p.connected) count++; return count; }
        private int LivingEnemies() { int count = 0; foreach (Enemy e in State.enemies) if (e.alive && e.kind != "customer") count++; return count; }
        private int CompleteTasks(string kind) { int count = 0; foreach (TaskState t in State.tasks) if (t.kind == kind && t.status == "complete") count++; return count; }
        private bool HostileNear(float x, float z, float range) { foreach (Enemy e in State.enemies) if (e.alive && e.kind != "customer" && Distance(e.x, e.z, x, z) < range) return true; return false; }
        private static bool Finite(float n) { return !float.IsNaN(n) && !float.IsInfinity(n); }
        private static int Clamp(int v, int lo, int hi) { return Math.Min(hi, Math.Max(lo, v)); }
        private static float Clamp(float v, float lo, float hi) { return Math.Min(hi, Math.Max(lo, v)); }
        private static float Length(float x, float z) { return (float)Math.Sqrt(x * x + z * z); }
        public static float Distance(float ax, float az, float bx, float bz) { return Length(ax - bx, az - bz); }
        private static bool Near(Player p, float x, float z) { return Distance(p.x, p.z, x, z) <= InteractionRange; }
        private void SetAim(Player p, float x, float z) { float len = Length(x, z); if (len > .01f) { p.faceX = x / len; p.faceZ = z / len; } }
        private float NextFloat() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (random & 0x00ffffff) / 16777216f; }
        private ActionResult Reject(string reason) { return new ActionResult(false, reason); }
        private ActionResult Accept(string reason) { if (!string.IsNullOrEmpty(reason)) State.message = reason; return new ActionResult(true, reason); }
        private void StopPlayers() { foreach (Player p in State.players) p.dx = p.dz = 0; }
        private void Win(string text) { State.phase = "won"; State.score += 100 * State.chapter; State.message = text; StopPlayers(); }
        private void Lose(string text) { State.phase = "lost"; State.message = text; StopPlayers(); }
        private void Damage(Player p, float amount) { p.hp = Math.Max(0, p.hp - amount); if (p.hp <= 0) { p.dead = true; p.dx = p.dz = 0; p.carried = ""; p.carryCount = 0; State.message = "A teammate is down; bring food to revive them."; } }
        private void Complete(TaskState t) { t.status = "complete"; t.progress = t.required; State.score += 10 + t.reward; }
        private Enemy SpawnEnemy(string kind, float x, float z, float hp = 40)
        {
            Enemy e = new Enemy { id = nextId++, kind = kind, x = x, z = z, hp = hp, maxHp = hp, speed = kind == "wolf" ? 2.4f : kind == "bot" ? 2.1f : 1.3f, attackCooldown = 1.2f };
            State.enemies.Add(e); return e;
        }
        private Structure Structure(string kind, float x, float z, int stock = 0) { Structure s = new Structure { id = nextId++, kind = kind, x = x, z = z, stock = stock }; State.structures.Add(s); return s; }
        private Pickup Pickup(string kind, float x, float z, int amount = 1) { Pickup p = new Pickup { id = nextId++, kind = kind, x = x, z = z, amount = amount }; State.pickups.Add(p); return p; }
        private TaskState Task(string kind, string label, float x, float z, int required = 1) { TaskState t = new TaskState { id = nextId++, kind = kind, label = label, x = x, z = z, required = required }; State.tasks.Add(t); return t; }
    }
}
