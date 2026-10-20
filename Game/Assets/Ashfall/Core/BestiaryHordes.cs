using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    /// <summary>Hordes: packs of walkers that drift along the roads between towns. Far from every survivor they move
    /// abstractly (members keep formation around the pack centre); near one they walk for real.</summary>
    public sealed partial class Simulation
    {
        /// <summary>Gathers the nearest roaming horde (topped up to size) or spawns one out of sight, and marches it to target.
        /// It stays there until ReleaseHorde. Returns the horde id, or -1 when no spawn point was found.</summary>
        public int SendHorde(Vec2 target, int size)
        {
            var w = world; size = MathUtil.Clamp(size, 5, 60);
            Horde best = null; float bestD = 900;
            foreach (var h in w.hordes)
            {
                int alive = Alive(h); float d = Vec2.Distance(h.center, target);
                if (alive >= Math.Min(8, size / 2) && d < bestD) { best = h; bestD = d; }
            }
            if (best == null)
            {
                Vec2 at = target; bool found = false;
                for (int t = 0; t < 60 && !found; t++)
                {
                    at = target + Vec2.FromAngle(w.rng.Range(0, 6.28f)) * w.rng.Range(140f, 220f);
                    found = w.LandFree(at) && !NearSurvivor(at, t < 40 ? 100 : 60);
                }
                if (!found) return -1;
                best = w.NewHorde(at, size, w.rng);
            }
            else
            {
                int missing = size - Alive(best);
                if (missing > 0 && !NearSurvivor(best.center, 90)) w.AddToHorde(best, missing, w.rng);
            }
            best.sent = true; w.PlanHorde(best, target);
            return best.id;
        }

        /// <summary>Lets a sent horde roam again (for example when a siege ends).</summary>
        public void ReleaseHorde(int id) { var h = world.HordeById(id); if (h != null) h.sent = false; }

        int Alive(Horde h)
        {
            int n = 0;
            foreach (int id in h.members) { var z = id < world.zombies.Count ? world.zombies[id] : null; if (z != null && z.Alive && z.horde == h.id) n++; }
            return n;
        }

        void Hordes(float minutes)
        {
            var w = world;
            for (int i = w.hordes.Count - 1; i >= 0; i--)
            {
                var h = w.hordes[i];
                h.members.RemoveAll(id => id >= w.zombies.Count || !w.zombies[id].Alive || w.zombies[id].horde != h.id);
                if (h.members.Count == 0 || h.members.Count < 4 && !h.sent)
                {
                    foreach (int id in h.members) w.zombies[id].horde = -1;
                    w.hordes.RemoveAt(i); continue;
                }
                var goal = h.viaWaypoint ? h.waypoint : h.target;
                if (NearSurvivor(h.center, ActiveRadius + 16)) LiveHorde(h, goal);
                else DriftHorde(h, goal, minutes * Bestiary.HordeMetersPerMinute);
                if (Vec2.Distance(h.center, goal) < 10)
                {
                    if (h.viaWaypoint) h.viaWaypoint = false;
                    else if (!h.sent) w.PlanHorde(h, w.NextHordeTarget(h.center, Moment(100 + h.id)));
                }
            }
        }

        static Vec2 Formation(int id) { return Vec2.FromAngle(id * 2.39996f) * (1.5f + id % 7 * .8f); }
        static bool Busy(Zombie z) { return z.act != FreakAct.None || z.state == ZombieState.Chase || z.state == ZombieState.Attack || z.state == ZombieState.Thump || z.state == ZombieState.Climb || z.state == ZombieState.Knocked || z.state == ZombieState.Lunge; }

        /// <summary>Near survivors the members really walk: each heads for its slot a little ahead of the pack.</summary>
        void LiveHorde(Horde h, Vec2 goal)
        {
            var sum = Vec2.Zero; int n = 0;
            foreach (int id in h.members) { sum = sum + world.zombies[id].pos; n++; }
            h.center = sum * (1f / n);
            float d = Vec2.Distance(h.center, goal);
            var ahead = d < 1 ? h.center : h.center + (goal - h.center) * (Math.Min(12, d) / d);
            foreach (int id in h.members)
            {
                var z = world.zombies[id];
                if (Busy(z)) continue;
                var slot = ahead + Formation(id);
                if (z.state != ZombieState.Investigate || Vec2.Distance(z.target, slot) > 3) { z.state = ZombieState.Investigate; z.target = slot; z.timer = 8; }
            }
        }

        /// <summary>Far from everyone the pack slides along its route and members re-form around the new centre.</summary>
        void DriftHorde(Horde h, Vec2 goal, float step)
        {
            var delta = goal - h.center; float d = delta.Length;
            if (d < .01f) return;
            var center = h.center + delta * (Math.Min(step, d) / d);
            foreach (int id in h.members)
            {
                var z = world.zombies[id];
                if (z.act != FreakAct.None) continue;
                var p = center + Formation(id);
                if (!world.LandFree(p)) continue;
                z.pos = p; z.target = p; z.targetSurvivor = -1; z.path.Clear();
                if (z.state != ZombieState.Idle && z.state != ZombieState.Wander) { z.state = ZombieState.Idle; z.timer = 4; }
                world.index.Moved(z);
            }
            h.center = center;
        }
    }
}
