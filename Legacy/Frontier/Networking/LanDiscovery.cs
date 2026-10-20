using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Frontier.Networking
{
    public sealed class LanRoom
    {
        public string GameId { get; internal set; }
        public string Address { get; internal set; }
        public int Port { get; internal set; }
        public string Name { get; internal set; }
    }

    /// <summary>Optional IPv4 LAN discovery; direct-address joining works without UDP broadcast.</summary>
    public static class LanDiscovery
    {
        public const int DefaultPort = 47771;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static string Prefix(string gameId) { return "FRONTIER-LAN/" + LanSession.ProtocolVersion.ToString(CultureInfo.InvariantCulture) + "\n" + gameId; }

        public static IDisposable Advertise(string gameId, int sessionPort, string roomName, int discoveryPort = DefaultPort, string bindAddress = "0.0.0.0")
        {
            LanSession.ValidateGameId(gameId);
            if (sessionPort < 1 || sessionPort > 65535 || discoveryPort < 1 || discoveryPort > 65535) throw new ArgumentOutOfRangeException("sessionPort");
            if (string.IsNullOrWhiteSpace(roomName) || roomName.Length > 48 || roomName.IndexOf('\n') >= 0 || roomName.IndexOf('\r') >= 0) throw new ArgumentException("Room name must fit on one line (48 characters).", "roomName");
            IPAddress ip;
            if (!IPAddress.TryParse(bindAddress, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Use an IPv4 bind address.", "bindAddress");
            return new Advertisement(gameId, sessionPort, roomName, discoveryPort, ip);
        }

        /// <summary>Blocking bounded scan: call on a worker and marshal its result to the main thread.</summary>
        public static LanRoom[] Scan(string gameId, int timeoutMs = 1500, int discoveryPort = DefaultPort, string targetAddress = "255.255.255.255")
        {
            LanSession.ValidateGameId(gameId);
            if (timeoutMs < 100 || timeoutMs > 5000 || discoveryPort < 1 || discoveryPort > 65535) throw new ArgumentOutOfRangeException("timeoutMs");
            IPAddress ip;
            if (!IPAddress.TryParse(targetAddress, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Use an IPv4 discovery destination.", "targetAddress");
            var found = new Dictionary<string, LanRoom>();
            using (var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            {
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 100;
                byte[] query = Utf8.GetBytes(Prefix(gameId));
                udp.Send(query, query.Length, new IPEndPoint(ip, discoveryPort));
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < timeoutMs && found.Count < 32)
                {
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] bytes = udp.Receive(ref from);
                        if (bytes.Length > 512 || from.Port != discoveryPort) continue;
                        string text;
                        try { text = Utf8.GetString(bytes); } catch (DecoderFallbackException) { continue; }
                        string[] parts = text.Split('\n');
                        int port;
                        if (parts.Length != 4 || parts[0] != "FRONTIER-LAN/" + LanSession.ProtocolVersion || parts[1] != gameId || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535 || string.IsNullOrWhiteSpace(parts[3]) || parts[3].Length > 48 || parts[3].IndexOf('\r') >= 0) continue;
                        string address = from.Address.ToString();
                        found[address + ":" + port] = new LanRoom { GameId = gameId, Address = address, Port = port, Name = parts[3] };
                    }
                    catch (SocketException ex) { if (ex.SocketErrorCode != SocketError.TimedOut && ex.SocketErrorCode != SocketError.WouldBlock) throw; }
                }
            }
            var rooms = new LanRoom[found.Count]; found.Values.CopyTo(rooms, 0);
            Array.Sort(rooms, (a, b) => string.CompareOrdinal(a.Address + ":" + a.Port, b.Address + ":" + b.Port));
            return rooms;
        }

        sealed class Advertisement : IDisposable
        {
            readonly UdpClient udp;
            readonly byte[] query;
            readonly byte[] reply;
            int disposed;
            public Advertisement(string gameId, int sessionPort, string roomName, int port, IPAddress bind)
            {
                query = Utf8.GetBytes(Prefix(gameId));
                reply = Utf8.GetBytes(Prefix(gameId) + "\n" + sessionPort.ToString(CultureInfo.InvariantCulture) + "\n" + roomName);
                udp = new UdpClient(new IPEndPoint(bind, port));
                udp.Client.ReceiveTimeout = 250;
                new Thread(Run) { IsBackground = true, Name = "Frontier LAN discovery" }.Start();
            }
            void Run()
            {
                var clock = Stopwatch.StartNew();
                long second = -1;
                int replies = 0;
                while (Volatile.Read(ref disposed) == 0)
                {
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] bytes = udp.Receive(ref from);
                        if (bytes.Length != query.Length) continue;
                        bool match = true; for (int i = 0; i < query.Length; i++) if (bytes[i] != query[i]) { match = false; break; }
                        if (!match) continue;
                        long now = clock.ElapsedMilliseconds / 1000;
                        if (second != now) { second = now; replies = 0; }
                        if (++replies <= 20) udp.Send(reply, reply.Length, from);
                    }
                    catch (SocketException ex)
                    {
                        if (Volatile.Read(ref disposed) != 0) break;
                        if (ex.SocketErrorCode != SocketError.TimedOut && ex.SocketErrorCode != SocketError.WouldBlock) break;
                    }
                    catch (ObjectDisposedException) { break; }
                }
            }
            public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) udp.Close(); }
        }
    }
}
