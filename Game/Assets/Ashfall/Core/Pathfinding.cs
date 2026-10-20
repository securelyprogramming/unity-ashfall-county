using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    /// <summary>Bounded A* over the tile grid. Closed doors, windows and barricades are passable at high cost so
    /// zombies route to them and thump; survivors use the same graph for auto-walk.</summary>
    public sealed class Pathfinder
    {
        const int Window = 96, Half = Window / 2;
        readonly Map map;
        readonly float[] g = new float[Window * Window];
        readonly int[] parent = new int[Window * Window];
        readonly int[] stamp = new int[Window * Window];
        readonly bool[] closed = new bool[Window * Window];
        int generation;
        readonly List<KeyValuePair<float, int>> heap = new List<KeyValuePair<float, int>>();
        public Pathfinder(Map map) { this.map = map; }

        public bool Find(Int2 start, Int2 goal, List<Int2> path, int budget = 2500, bool zombie = true)
        {
            path.Clear();
            if (Math.Abs(goal.x - start.x) >= Half - 2 || Math.Abs(goal.y - start.y) >= Half - 2) return false;
            int ox = (start.x + goal.x) / 2 - Half, oy = (start.y + goal.y) / 2 - Half;
            generation++; heap.Clear();
            int s = Local(start, ox, oy), t = Local(goal, ox, oy);
            if (s < 0 || t < 0) return false;
            Open(s, 0, -1, Heuristic(start, goal));
            int expanded = 0;
            while (heap.Count > 0 && expanded++ < budget)
            {
                int cur = Pop();
                if (closed[cur] && stamp[cur] == generation) continue;
                closed[cur] = true;
                if (cur == t) { Build(t, ox, oy, path); return true; }
                int cx = cur % Window + ox, cy = cur / Window + oy;
                for (int d = 0; d < 8; d++)
                {
                    int dx = DX[d], dy = DY[d], nx = cx + dx, ny = cy + dy;
                    if (!map.InBounds(nx, ny) || map.TileBlocked(nx, ny) && !(nx == goal.x && ny == goal.y)) continue;
                    float cost = d < 4 ? 1 : 1.414f;
                    if (d < 4) { float c = Crossing(cx, cy, nx, ny, zombie); if (c < 0) continue; cost += c; }
                    else
                    {
                        // Diagonals only through fully open corners.
                        if (map.TileBlocked(cx + dx, cy) || map.TileBlocked(cx, cy + dy)) continue;
                        if (Crossing(cx, cy, cx + dx, cy, zombie) != 0 || Crossing(cx + dx, cy, nx, ny, zombie) != 0) continue;
                        if (Crossing(cx, cy, cx, cy + dy, zombie) != 0 || Crossing(cx, cy + dy, nx, ny, zombie) != 0) continue;
                    }
                    int n = Local(new Int2(nx, ny), ox, oy); if (n < 0) continue;
                    float ng = g[cur] + cost;
                    if (stamp[n] == generation && (closed[n] || g[n] <= ng)) continue;
                    Open(n, ng, cur, ng + Heuristic(new Int2(nx, ny), goal));
                }
            }
            return false;
        }

        /// <summary>Extra cost of crossing an edge; negative when impassable.</summary>
        float Crossing(int ax, int ay, int bx, int by, bool zombie)
        {
            Opening o; var e = map.Between(ax, ay, bx, by, out o);
            if (e == Edge.None) return 0;
            if (o != null)
            {
                if (!o.BlocksMovement) return 0;
                if (o.Climbable) return 3;
                return zombie ? 14 + o.barricade * 6 : -1;
            }
            if (e == Edge.Fence) return zombie ? -1 : 4;
            return -1;
        }

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 }, DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static float Heuristic(Int2 a, Int2 b) { int dx = Math.Abs(a.x - b.x), dy = Math.Abs(a.y - b.y); return Math.Max(dx, dy) + .414f * Math.Min(dx, dy); }
        int Local(Int2 p, int ox, int oy) { int x = p.x - ox, y = p.y - oy; return x < 0 || y < 0 || x >= Window || y >= Window ? -1 : x + y * Window; }
        void Open(int n, float cost, int from, float f)
        {
            if (stamp[n] != generation) { stamp[n] = generation; closed[n] = false; }
            g[n] = cost; parent[n] = from; Push(f, n);
        }
        void Build(int t, int ox, int oy, List<Int2> path)
        {
            for (int n = t, guard = 0; n >= 0 && guard < Window * Window; n = parent[n], guard++) path.Add(new Int2(n % Window + ox, n / Window + oy));
            path.Reverse();
        }
        void Push(float f, int n)
        {
            heap.Add(new KeyValuePair<float, int>(f, n)); int i = heap.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (heap[p].Key <= heap[i].Key) break; var tmp = heap[p]; heap[p] = heap[i]; heap[i] = tmp; i = p; }
        }
        int Pop()
        {
            int result = heap[0].Value; var last = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
            if (heap.Count > 0)
            {
                heap[0] = last; int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < heap.Count && heap[l].Key < heap[m].Key) m = l;
                    if (r < heap.Count && heap[r].Key < heap[m].Key) m = r;
                    if (m == i) break; var tmp = heap[m]; heap[m] = heap[i]; heap[i] = tmp; i = m;
                }
            }
            return result;
        }
    }
}
