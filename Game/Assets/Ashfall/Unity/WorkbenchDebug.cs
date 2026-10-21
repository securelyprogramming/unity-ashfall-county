// Local development evidence only. There are deliberately no network clients or credentials here.
using System;
using System.IO;
using System.Text;

public static class WorkbenchDebug
{
    static readonly object Gate = new object();
    static bool initialized, enabled;
    static string destination, session, game;
    static long bytes, second;
    static int count;
    const long MaximumBytes = 32 * 1024 * 1024;
    static string Quote(string value, int maximum = 1000)
    {
        var b = new StringBuilder("\"");
        foreach (char c in (value ?? "").Substring(0, Math.Min(maximum, (value ?? "").Length)))
        {
            if (c == '\\' || c == '"') b.Append('\\').Append(c);
            else if (c < 32 || char.IsSurrogate(c)) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
    // Conditional calls (including their arguments) are omitted from release players.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void Event(string eventName, string system, string entityId = "", string message = "")
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        lock (Gate)
        {
            try
            {
                if (!initialized)
                {
                    initialized = true;
                    if (Environment.GetEnvironmentVariable("WORKBENCH_DEBUG_ENABLED") != "1") return;
                    destination = Environment.GetEnvironmentVariable("WORKBENCH_DEBUG_LOG");
                    session = Environment.GetEnvironmentVariable("WORKBENCH_DEBUG_SESSION");
                    game = Environment.GetEnvironmentVariable("WORKBENCH_DEBUG_GAME");
                    Guid parsed;
                    if (string.IsNullOrEmpty(destination) || !Path.IsPathRooted(destination) || !Guid.TryParse(session, out parsed) || string.IsNullOrEmpty(game)) return;
                    // Workbench owns directory creation; a typo must not create unrelated folders.
                    if (!Directory.Exists(Path.GetDirectoryName(destination))) return;
                    bytes = File.Exists(destination) ? new FileInfo(destination).Length : 0;
                    enabled = true;
                }
                if (!enabled || bytes >= MaximumBytes) return;
                long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
                if (second != now) { second = now; count = 0; }
                if (++count > 50) return;
                string row = "{\"timestamp\":" + Quote(DateTime.UtcNow.ToString("o")) + ",\"sessionId\":" + Quote(session, 80) + ",\"gameKey\":" + Quote(game, 200) + ",\"event\":" + Quote(eventName, 100) + ",\"system\":" + Quote(system, 100) + ",\"entityId\":" + Quote(entityId, 200) + ",\"message\":" + Quote(message) + "}\n";
                byte[] data = Encoding.UTF8.GetBytes(row);
                if (bytes + data.Length > MaximumBytes) return;
                using (var stream = new FileStream(destination, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) stream.Write(data, 0, data.Length);
                bytes += data.Length;
            }
            catch (Exception) { enabled = false; } // Diagnostics must never interrupt gameplay.
        }
#endif
    }
}
