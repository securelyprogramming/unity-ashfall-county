using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Frontier.Networking
{
    /// <summary>
    /// LAN-only TCP transport. Host owns simulation; clients may send validated JSON commands.
    /// Network workers never call consumer code. Call Pump from the Unity main thread.
    /// </summary>
    public sealed class LanSession : IDisposable
    {
        public const int ProtocolVersion = 1;
        public const int MaxCommandBytes = 4096;
        public const int MaxSnapshotBytes = 65536;
        public const int MaxFrameBytes = MaxSnapshotBytes + 9;
        const int MaxQueuedEvents = 1024;
        const int MaxPendingCommands = 64;
        const int MaxPendingWrites = 64;
        const int HandshakeTimeoutMs = 4000;
        const int IdleTimeoutMs = 15000;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        enum Kind : byte { Hello = 1, Welcome = 2, Command = 3, Snapshot = 4, Ping = 5, Pong = 6 }
        enum EventKind { Connected, Joined, Left, Command, Snapshot, Error }
        sealed class PendingEvent { public EventKind Kind; public Peer Peer; public int Id; public string Text; }
        sealed class Frame { public Kind Kind; public ulong Sequence; public string Payload; }

        readonly object gate = new object();
        readonly Queue<PendingEvent> events = new Queue<PendingEvent>();
        readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        readonly HashSet<TcpClient> handshaking = new HashSet<TcpClient>();
        readonly int pumpThread;
        readonly bool isHost;
        readonly string gameId;
        readonly int maxClients;
        TcpListener listener;
        Peer server;
        int nextPlayerId;
        int localPlayerId;
        int pendingDepartures;
        int pendingErrors;
        volatile bool disposed;
        volatile bool connected;
        ulong snapshotSequence;
        Frame latestSnapshot;

        public event Action<int> Connected;
        public event Action<int> PeerJoined;
        public event Action<int> PeerLeft;
        public event Action<int, string> CommandReceived;
        public event Action<string> SnapshotReceived;
        public event Action<string> Faulted;

        public bool IsHost { get { return isHost; } }
        public bool IsConnected { get { return connected && !disposed; } }
        public int LocalPlayerId { get { return Volatile.Read(ref localPlayerId); } }
        public int Port { get; private set; }
        public string GameId { get { return gameId; } }
        public int[] PeerIds
        {
            get { lock (gate) { var ids = new int[peers.Count]; peers.Keys.CopyTo(ids, 0); Array.Sort(ids); return ids; } }
        }

        LanSession(bool host, string id, int maxClients)
        {
            ValidateGameId(id);
            if (maxClients < 1 || maxClients > 15) throw new ArgumentOutOfRangeException("maxClients");
            isHost = host; gameId = id; this.maxClients = maxClients;
            localPlayerId = host ? 0 : -1;
            pumpThread = Thread.CurrentThread.ManagedThreadId;
        }

        public static LanSession Host(string gameId, int port, string bindAddress = "0.0.0.0", int maxClients = 7)
        {
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port");
            IPAddress ip;
            if (!IPAddress.TryParse(bindAddress, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Use an IPv4 bind address.", "bindAddress");
            var session = new LanSession(true, gameId, maxClients);
            try
            {
                session.listener = new TcpListener(ip, port);
                session.listener.Start(maxClients);
                session.Port = ((IPEndPoint)session.listener.LocalEndpoint).Port;
                session.connected = true;
                session.Enqueue(EventKind.Connected, null, 0, null);
                session.StartThread(session.AcceptLoop, "Frontier LAN accept");
                return session;
            }
            catch { session.Dispose(); throw; }
        }

        /// <summary>Returns immediately; connection success/failure is delivered by Pump.</summary>
        public static LanSession Join(string gameId, string address, int port)
        {
            if (string.IsNullOrWhiteSpace(address) || address.Length > 253) throw new ArgumentException("Enter a host address.", "address");
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");
            var session = new LanSession(false, gameId, 1) { Port = port };
            session.StartThread(() => session.Connect(address, port), "Frontier LAN connect");
            return session;
        }

        public int Pump(int maxEvents = 256)
        {
            if (Thread.CurrentThread.ManagedThreadId != pumpThread) throw new InvalidOperationException("Pump must run on the thread that created the session.");
            if (maxEvents < 1 || maxEvents > MaxQueuedEvents) throw new ArgumentOutOfRangeException("maxEvents");
            int count = 0;
            while (!disposed && count < maxEvents)
            {
                PendingEvent item;
                lock (gate) { if (events.Count == 0) break; item = events.Dequeue(); }
                count++;
                switch (item.Kind)
                {
                    case EventKind.Connected: if (Connected != null) Connected(item.Id); break;
                    case EventKind.Joined: if (PeerJoined != null) PeerJoined(item.Id); break;
                    case EventKind.Left:
                        if (isHost) { lock (gate) pendingDepartures--; }
                        if (PeerLeft != null) PeerLeft(item.Id);
                        break;
                    case EventKind.Command:
                        Interlocked.Decrement(ref item.Peer.PendingCommands);
                        // A departing client cannot leave commands behind for a future incarnation.
                        if (!item.Peer.IsClosed && CommandReceived != null) CommandReceived(item.Id, item.Text);
                        break;
                    case EventKind.Snapshot:
                        Interlocked.Decrement(ref item.Peer.PendingSnapshots);
                        if (!item.Peer.IsClosed && SnapshotReceived != null) SnapshotReceived(item.Text);
                        break;
                    case EventKind.Error:
                        lock (gate) pendingErrors--;
                        if (Faulted != null) Faulted(item.Text);
                        break;
                }
            }
            return count;
        }

        /// <summary>Only clients send input. False means not connected, rate-limited, or closing.</summary>
        public bool SendCommand(string json)
        {
            if (isHost) throw new InvalidOperationException("Apply host input directly to its authoritative simulation.");
            ValidatePayload(json, MaxCommandBytes);
            Peer peer;
            lock (gate) { peer = server; }
            return peer != null && IsConnected && peer.SendCommand(json);
        }

        /// <summary>Only hosts publish authoritative state. Slow receivers keep the latest snapshot.</summary>
        public void BroadcastSnapshot(string json)
        {
            if (!isHost) throw new InvalidOperationException("Only the host can publish a snapshot.");
            ValidatePayload(json, MaxSnapshotBytes);
            lock (gate)
            {
                if (disposed) return;
                if (snapshotSequence == ulong.MaxValue) throw new InvalidOperationException("Snapshot sequence exhausted; restart the lobby.");
                var frame = new Frame { Kind = Kind.Snapshot, Sequence = ++snapshotSequence, Payload = json };
                latestSnapshot = frame;
                foreach (var peer in peers.Values) peer.QueueSnapshot(frame);
            }
        }

        public void DisconnectPeer(int playerId)
        {
            if (!isHost) throw new InvalidOperationException("Only the host can remove a peer.");
            Peer peer;
            lock (gate) peers.TryGetValue(playerId, out peer);
            if (peer != null) peer.Close();
        }

        void AcceptLoop()
        {
            while (!disposed)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { if (!disposed) Enqueue(EventKind.Error, null, -1, "LAN listener stopped unexpectedly."); break; }
                catch (ObjectDisposedException) { break; }
                lock (gate)
                {
                    // Includes untrusted sockets waiting for a hello, so slow handshakes are bounded.
                    if (disposed || peers.Count + handshaking.Count + pendingDepartures >= maxClients) { client.Close(); continue; }
                    handshaking.Add(client);
                }
                StartThread(() => AcceptPeer(client), "Frontier LAN handshake");
            }
        }

        void AcceptPeer(TcpClient client)
        {
            Peer peer = null;
            try
            {
                Configure(client, HandshakeTimeoutMs);
                var hello = ReadFrame(client.GetStream());
                if (hello.Kind != Kind.Hello || hello.Sequence != 0 || hello.Payload != ProtocolVersion.ToString(CultureInfo.InvariantCulture) + "\n" + gameId) throw new InvalidDataException("Game or protocol mismatch.");
                int id;
                lock (gate)
                {
                    if (disposed) return;
                    if (nextPlayerId == int.MaxValue) throw new InvalidDataException("Lobby identity space exhausted.");
                    id = ++nextPlayerId;
                }
                // Do not hold the session lock across socket I/O: Pump must never wait for a slow handshake.
                WriteFrame(client.GetStream(), new Frame { Kind = Kind.Welcome, Payload = ProtocolVersion.ToString(CultureInfo.InvariantCulture) + "\n" + id.ToString(CultureInfo.InvariantCulture) });
                lock (gate)
                {
                    if (disposed) return;
                    client.ReceiveTimeout = IdleTimeoutMs;
                    peer = new Peer(this, client, id);
                    handshaking.Remove(client);
                    peers.Add(id, peer);
                    if (!Enqueue(EventKind.Joined, peer, id, null)) throw new InvalidDataException("Main-thread event queue is full.");
                    if (latestSnapshot != null) peer.QueueSnapshot(latestSnapshot);
                }
                peer.Start();
            }
            catch (Exception ex)
            {
                if (!IsExpectedNetworkFailure(ex)) throw;
                if (!disposed) Enqueue(EventKind.Error, null, -1, "A LAN connection was rejected or interrupted (" + SafeFailure(ex) + ").");
            }
            finally
            {
                lock (gate) handshaking.Remove(client);
                if (peer == null) client.Close();
                else if (!peer.Started) peer.Close();
            }
        }

        void Connect(string address, int port)
        {
            var client = new TcpClient(AddressFamily.InterNetwork);
            Peer peer = null;
            lock (gate) { if (disposed) { client.Close(); return; } handshaking.Add(client); }
            try
            {
                IAsyncResult attempt = client.BeginConnect(address, port, null, null);
                using (attempt.AsyncWaitHandle)
                {
                    if (!attempt.AsyncWaitHandle.WaitOne(HandshakeTimeoutMs)) throw new IOException("Connection timed out.");
                    client.EndConnect(attempt);
                }
                Configure(client, HandshakeTimeoutMs);
                WriteFrame(client.GetStream(), new Frame { Kind = Kind.Hello, Payload = ProtocolVersion.ToString(CultureInfo.InvariantCulture) + "\n" + gameId });
                var welcome = ReadFrame(client.GetStream());
                int id;
                string[] parts = welcome.Payload.Split('\n');
                if (welcome.Kind != Kind.Welcome || welcome.Sequence != 0 || parts.Length != 2 || parts[0] != ProtocolVersion.ToString(CultureInfo.InvariantCulture) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out id) || id < 1) throw new InvalidDataException("Invalid host welcome.");
                client.ReceiveTimeout = IdleTimeoutMs;
                lock (gate)
                {
                    if (disposed) return;
                    peer = new Peer(this, client, id);
                    server = peer; localPlayerId = id; connected = true;
                    handshaking.Remove(client);
                    if (!Enqueue(EventKind.Connected, peer, id, null)) throw new InvalidDataException("Main-thread event queue is full.");
                }
                peer.Start();
            }
            catch (Exception ex)
            {
                if (!IsExpectedNetworkFailure(ex)) throw;
                if (!disposed) Enqueue(EventKind.Error, null, -1, "Could not join the LAN host (" + SafeFailure(ex) + "). Check the host address, game version, and local network access.");
            }
            finally
            {
                lock (gate) handshaking.Remove(client);
                if (peer == null) client.Close();
                else if (!peer.Started) peer.Close();
            }
        }

        void Receive(Peer peer, Frame frame)
        {
            if (frame.Kind == Kind.Ping || frame.Kind == Kind.Pong)
            {
                if (frame.Sequence != 0 || frame.Payload.Length != 0 || !peer.ControlRate.Take()) throw new InvalidDataException("Invalid heartbeat.");
                if (frame.Kind == Kind.Ping && !peer.Queue(new Frame { Kind = Kind.Pong })) throw new InvalidDataException("Outbound queue is full.");
                return;
            }
            if (isHost)
            {
                if (frame.Kind != Kind.Command || peer.LastReceivedSequence == ulong.MaxValue || frame.Sequence != peer.LastReceivedSequence + 1) throw new InvalidDataException("Invalid command sequence or authority.");
                ValidatePayload(frame.Payload, MaxCommandBytes);
                if (!peer.ReceiveRate.Take()) throw new InvalidDataException("Command cadence exceeded.");
                peer.LastReceivedSequence = frame.Sequence;
                if (Interlocked.Increment(ref peer.PendingCommands) > MaxPendingCommands || !Enqueue(EventKind.Command, peer, peer.Id, frame.Payload)) throw new InvalidDataException("Main-thread command queue is full.");
            }
            else
            {
                if (frame.Kind != Kind.Snapshot || frame.Sequence == 0 || frame.Sequence <= peer.LastReceivedSequence) throw new InvalidDataException("Invalid snapshot sequence or authority.");
                ValidatePayload(frame.Payload, MaxSnapshotBytes);
                peer.LastReceivedSequence = frame.Sequence;
                if (Interlocked.Increment(ref peer.PendingSnapshots) > MaxPendingCommands || !Enqueue(EventKind.Snapshot, peer, peer.Id, frame.Payload)) throw new InvalidDataException("Main-thread snapshot queue is full.");
            }
        }

        void Depart(Peer peer)
        {
            lock (gate)
            {
                if (isHost)
                {
                    Peer current;
                    if (peers.TryGetValue(peer.Id, out current) && ReferenceEquals(current, peer))
                    {
                        peers.Remove(peer.Id);
                        // Retain a capacity reservation until the main thread consumes departure.
                        // This bounds churn and ensures it cannot crowd out another peer's leave event.
                        pendingDepartures++;
                        Enqueue(EventKind.Left, peer, peer.Id, null);
                    }
                }
                else if (ReferenceEquals(server, peer))
                {
                    server = null; connected = false; localPlayerId = -1;
                    Enqueue(EventKind.Left, peer, peer.Id, null);
                }
            }
        }

        bool Enqueue(EventKind kind, Peer peer, int id, string text)
        {
            lock (gate)
            {
                if (disposed || events.Count >= MaxQueuedEvents) return false;
                if (kind == EventKind.Error)
                {
                    if (pendingErrors >= 16) return false;
                    pendingErrors++;
                }
                events.Enqueue(new PendingEvent { Kind = kind, Peer = peer, Id = id, Text = text });
                return true;
            }
        }

        static void ValidatePayload(string json, int maxBytes)
        {
            if (json == null || json.Length > maxBytes || Utf8.GetByteCount(json) > maxBytes || !JsonPayload.IsValidObject(json)) throw new InvalidDataException("Expected a bounded JSON object with finite numbers.");
        }

        public static void ValidateGameId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) throw new ArgumentException("Game ID must be a lowercase slug.", "id");
            for (int i = 0; i < id.Length; i++) if (!(id[i] >= 'a' && id[i] <= 'z') && !(id[i] >= '0' && id[i] <= '9') && !(i > 0 && id[i] == '-')) throw new ArgumentException("Game ID must be a lowercase slug.", "id");
        }

        static bool IsExpectedNetworkFailure(Exception ex) { return ex is IOException || ex is InvalidDataException || ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException || ex is ArgumentException; }
        static string SafeFailure(Exception ex) { return ex is InvalidDataException ? "invalid message" : ex is SocketException ? "socket unavailable" : "connection closed"; }
        static void Configure(TcpClient client, int timeout) { client.NoDelay = true; client.ReceiveTimeout = timeout; client.SendTimeout = HandshakeTimeoutMs; }
        void StartThread(ThreadStart action, string name) { new Thread(action) { IsBackground = true, Name = name }.Start(); }

        static Frame ReadFrame(NetworkStream stream)
        {
            byte[] header = new byte[4]; ReadExactly(stream, header, 4);
            uint length = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
            if (length < 9 || length > MaxFrameBytes) throw new InvalidDataException("Invalid frame length.");
            var body = new byte[(int)length]; ReadExactly(stream, body, body.Length);
            ulong sequence = 0; for (int i = 1; i <= 8; i++) sequence = (sequence << 8) | body[i];
            string payload = Utf8.GetString(body, 9, body.Length - 9);
            return new Frame { Kind = (Kind)body[0], Sequence = sequence, Payload = payload };
        }

        static void ReadExactly(NetworkStream stream, byte[] target, int count)
        {
            int at = 0;
            while (at < count) { int got = stream.Read(target, at, count - at); if (got <= 0) throw new EndOfStreamException(); at += got; }
        }

        static void WriteFrame(NetworkStream stream, Frame frame)
        {
            byte[] text = Utf8.GetBytes(frame.Payload ?? string.Empty);
            int length = text.Length + 9;
            if (length > MaxFrameBytes) throw new InvalidDataException("Frame too large.");
            var body = new byte[length + 4];
            body[0] = (byte)(length >> 24); body[1] = (byte)(length >> 16); body[2] = (byte)(length >> 8); body[3] = (byte)length;
            body[4] = (byte)frame.Kind;
            ulong sequence = frame.Sequence;
            for (int i = 12; i >= 5; i--) { body[i] = (byte)sequence; sequence >>= 8; }
            Buffer.BlockCopy(text, 0, body, 13, text.Length);
            stream.Write(body, 0, body.Length);
        }

        public void Dispose()
        {
            Peer[] current;
            TcpClient[] pending;
            lock (gate)
            {
                if (disposed) return;
                disposed = true; connected = false; localPlayerId = -1;
                current = new Peer[peers.Count + (server == null ? 0 : 1)];
                peers.Values.CopyTo(current, 0);
                if (server != null) current[current.Length - 1] = server;
                pending = new TcpClient[handshaking.Count]; handshaking.CopyTo(pending);
                peers.Clear(); server = null; latestSnapshot = null; events.Clear(); handshaking.Clear(); pendingDepartures = 0; pendingErrors = 0;
            }
            if (listener != null) listener.Stop();
            foreach (var socket in pending) socket.Close();
            foreach (var peer in current) peer.Close();
        }

        sealed class RateLimit
        {
            readonly double rate;
            readonly double capacity;
            readonly Stopwatch clock = Stopwatch.StartNew();
            double tokens;
            long last;
            public RateLimit(double rate, double burst) { this.rate = rate; capacity = burst; tokens = burst; }
            public bool Take()
            {
                long now = clock.ElapsedTicks;
                tokens = Math.Min(capacity, tokens + (now - last) * rate / Stopwatch.Frequency); last = now;
                if (tokens < 1) return false;
                tokens--; return true;
            }
        }

        sealed class Peer
        {
            readonly LanSession owner;
            readonly TcpClient client;
            readonly NetworkStream stream;
            readonly object writesGate = new object();
            readonly Queue<Frame> writes = new Queue<Frame>();
            readonly AutoResetEvent wake = new AutoResetEvent(false);
            readonly RateLimit sendRate = new RateLimit(60, 80);
            public readonly RateLimit ReceiveRate = new RateLimit(60, 80);
            public readonly RateLimit ControlRate = new RateLimit(4, 12);
            Frame snapshot;
            ulong sendSequence;
            int closed;
            public readonly int Id;
            public int PendingCommands;
            public int PendingSnapshots;
            public ulong LastReceivedSequence;
            public bool Started { get; private set; }
            public bool IsClosed { get { return Volatile.Read(ref closed) != 0; } }
            public Peer(LanSession owner, TcpClient client, int id) { this.owner = owner; this.client = client; stream = client.GetStream(); Id = id; }

            public void Start()
            {
                Started = true;
                owner.StartThread(ReadLoop, "Frontier LAN read");
                owner.StartThread(WriteLoop, "Frontier LAN write");
            }

            public bool SendCommand(string payload)
            {
                lock (writesGate)
                {
                    if (IsClosed || writes.Count >= MaxPendingWrites || !sendRate.Take() || sendSequence == ulong.MaxValue) return false;
                    writes.Enqueue(new Frame { Kind = Kind.Command, Sequence = ++sendSequence, Payload = payload });
                    wake.Set(); return true;
                }
            }
            public bool Queue(Frame frame)
            {
                lock (writesGate)
                {
                    if (IsClosed || writes.Count >= MaxPendingWrites) return false;
                    writes.Enqueue(frame); wake.Set(); return true;
                }
            }
            public void QueueSnapshot(Frame frame) { lock (writesGate) { if (!IsClosed) { snapshot = frame; wake.Set(); } } }

            void ReadLoop()
            {
                try { while (!IsClosed && !owner.disposed) owner.Receive(this, ReadFrame(stream)); }
                catch (Exception ex)
                {
                    if (!IsExpectedNetworkFailure(ex)) throw;
                    if (!IsClosed && !owner.disposed && ex is InvalidDataException) owner.Enqueue(EventKind.Error, this, Id, "Peer " + Id + " sent an invalid LAN message.");
                }
                finally { Close(); }
            }

            void WriteLoop()
            {
                var clock = Stopwatch.StartNew();
                long heartbeatAt = 0;
                try
                {
                    while (!IsClosed && !owner.disposed)
                    {
                        Frame frame = null;
                        lock (writesGate)
                        {
                            if (writes.Count > 0) frame = writes.Dequeue();
                            else if (snapshot != null) { frame = snapshot; snapshot = null; }
                        }
                        if (frame != null) WriteFrame(stream, frame);
                        else if (clock.ElapsedMilliseconds - heartbeatAt >= 2000)
                        {
                            heartbeatAt = clock.ElapsedMilliseconds;
                            WriteFrame(stream, new Frame { Kind = Kind.Ping });
                        }
                        else wake.WaitOne(200);
                    }
                }
                catch (Exception ex) { if (!IsExpectedNetworkFailure(ex)) throw; }
                finally { Close(); wake.Dispose(); }
            }

            public void Close()
            {
                if (Interlocked.Exchange(ref closed, 1) != 0) return;
                // Wake before the writer disposes its event. No future producer can Set after closed.
                lock (writesGate) { writes.Clear(); snapshot = null; wake.Set(); }
                client.Close();
                owner.Depart(this);
                if (!Started) wake.Dispose();
            }
        }
    }
}
