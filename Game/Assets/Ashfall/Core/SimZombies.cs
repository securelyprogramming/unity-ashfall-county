using System;
using System.Linq;

namespace Ashfall.Core
{
    public sealed partial class Simulation
    {
        public const int MaxSearchesPerTick = 10;
        int searches;
        void UpdateZombie(Zombie z, float dt, bool perceive)
        {
            if (!z.Alive) return;
            z.groan -= dt;
            if (z.groan <= 0) { z.groan = world.rng.Range(6, 22); if (z.state == ZombieState.Chase || world.rng.Chance(.25f)) world.Emit(EventKind.Groan, z.pos, -1, z.state == ZombieState.Chase ? "chase" : "idle", z.id); }
            if (z.stagger > 0) { z.stagger -= dt; return; }
            if (z.state == ZombieState.Knocked)
            {
                z.timer -= dt;
                if (z.timer <= 0) { z.state = z.targetSurvivor >= 0 ? ZombieState.Chase : ZombieState.Idle; z.timer = 2; }
                return;
            }
            if (z.state == ZombieState.Climb)
            {
                z.timer -= dt;
                if (z.timer <= 0) { z.pos = z.target; world.index.Moved(z); z.state = z.targetSurvivor >= 0 ? ZombieState.Chase : ZombieState.Investigate; z.target = z.pos; z.path.Clear(); }
                return;
            }
            if (perceive) Perceive(z);
            Survivor prey = z.targetSurvivor >= 0 ? Survivor(z.targetSurvivor) : null;
            if (prey != null && !prey.alive) { prey = null; z.targetSurvivor = -1; if (z.state == ZombieState.Chase || z.state == ZombieState.Attack) { z.state = ZombieState.Eating; z.timer = world.rng.Range(20, 60); } }
            switch (z.state)
            {
                case ZombieState.Idle:
                    z.timer -= dt;
                    if (z.timer <= 0) { z.state = ZombieState.Wander; z.target = WanderPoint(z); z.timer = world.rng.Range(6, 18); }
                    break;
                case ZombieState.Eating:
                    z.timer -= dt; if (z.timer <= 0) { z.state = ZombieState.Idle; z.timer = 5; }
                    break;
                case ZombieState.Wander:
                    z.timer -= dt;
                    if (MoveTo(z, z.target, z.speed * .45f, dt) || z.timer <= 0) { z.state = ZombieState.Idle; z.timer = world.rng.Range(4, 16); }
                    break;
                case ZombieState.Investigate:
                    z.timer -= dt;
                    if (MoveTo(z, z.target, z.speed * .8f, dt)) { if (z.timer <= 0) { z.state = ZombieState.Idle; z.timer = world.rng.Range(5, 15); } }
                    else if (z.timer < -30) { z.state = ZombieState.Idle; z.timer = 5; }
                    break;
                case ZombieState.Chase:
                    if (prey == null) { z.state = ZombieState.Investigate; z.timer = 20; break; }
                    if (world.map.LineOfSight(z.pos, prey.pos)) { z.target = prey.pos; z.lostSight = 0; }
                    else { z.lostSight += dt; if (z.lostSight > 14) { z.state = ZombieState.Investigate; z.timer = 25; z.targetSurvivor = -1; break; } }
                    float d = Vec2.Distance(z.pos, prey.pos);
                    if (d < .95f && world.map.LineOfSight(z.pos, prey.pos)) { z.state = ZombieState.Attack; z.attackClock = .55f; z.facing = (prey.pos - z.pos).Angle; break; }
                    MoveTo(z, z.target, z.speed, dt);
                    break;
                case ZombieState.Attack:
                    if (prey == null) { z.state = ZombieState.Idle; break; }
                    z.facing = (prey.pos - z.pos).Angle;
                    z.attackClock -= dt;
                    if (z.attackClock <= 0)
                    {
                        if (Vec2.Distance(z.pos, prey.pos) < 1.15f && world.map.LineOfSight(z.pos, prey.pos)) ZombieStrike(z, prey);
                        z.attackClock = world.rng.Range(1.0f, 1.5f);
                        if (Vec2.Distance(z.pos, prey.pos) > 1.2f) z.state = ZombieState.Chase;
                    }
                    else if (Vec2.Distance(z.pos, prey.pos) > 1.6f) z.state = ZombieState.Chase;
                    break;
                case ZombieState.Thump:
                    {
                        var o = z.thumping >= 0 ? world.map.openings[z.thumping] : null;
                        if (o == null || !o.BlocksMovement || o.Climbable) { z.state = prey != null ? ZombieState.Chase : ZombieState.Investigate; z.timer = 15; z.path.Clear(); z.repath = 0; break; }
                        z.facing = (o.Center - z.pos).Angle;
                        z.attackClock -= dt;
                        if (z.attackClock <= 0)
                        {
                            z.attackClock = world.rng.Range(1.1f, 1.6f);
                            world.MakeNoise(o.Center, 9);
                            if (o.barricade > 0) DamageBarricade(o, .05f);
                            else if (o.kind == Edge.Window) { if (world.rng.Chance(.35f)) BreakGlass(o, z.pos); else world.Emit(EventKind.Thump, o.Center); }
                            else { o.hp -= .035f; world.Emit(EventKind.Thump, o.Center); if (o.hp <= 0) { o.broken = true; o.open = true; world.Emit(EventKind.Break, o.Center, -1, "A door gives way."); } }
                        }
                        // Lose interest if the prey is long gone.
                        if (prey == null) { z.timer -= dt; if (z.timer < -40) { z.state = ZombieState.Idle; z.timer = 4; } }
                        break;
                    }
            }
        }

        void Perceive(Zombie z)
        {
            if (z.state == ZombieState.Attack || z.state == ZombieState.Climb) return;
            var forward = Vec2.FromAngle(z.facing);
            foreach (var s in world.survivors)
            {
                if (!s.alive) continue;
                var to = s.pos - z.pos; float d = to.Length;
                float range = NoticeRange(s) * z.sight;
                if (d > range) continue;
                bool cone = d < 2.2f || Vec2.Dot(to * (1 / Math.Max(.01f, d)), forward) > .15f;
                if (!cone || !world.map.LineOfSight(z.pos, s.pos)) continue;
                if (z.state != ZombieState.Chase && z.state != ZombieState.Thump)
                {
                    world.Emit(EventKind.Groan, z.pos, -1, "spot", z.id);
                    // A spotting zombie's moan draws the ones around it.
                    world.MakeNoise(z.pos, 7);
                }
                if (z.state != ZombieState.Thump || d < 3) { z.state = ZombieState.Chase; z.path.Clear(); z.repath = 0; }
                z.targetSurvivor = s.id; z.target = s.pos; z.lostSight = 0;
                return;
            }
        }

        Vec2 WanderPoint(Zombie z)
        {
            for (int i = 0; i < 6; i++)
            {
                var p = z.pos + Vec2.FromAngle(world.rng.Range(0, 6.28f)) * world.rng.Range(2, 9);
                if (!world.map.TileBlocked((int)p.x, (int)p.y) && world.map.LineOfSight(z.pos, p)) return p;
            }
            return z.pos;
        }

        /// <summary>Steers toward a point: straight when clear, otherwise along an A* path that may lead to a door to thump.</summary>
        bool MoveTo(Zombie z, Vec2 target, float speed, float dt)
        {
            float d = Vec2.Distance(z.pos, target);
            if (d < .35f) return true;
            Vec2 waypoint = target;
            bool direct = d < 24 && world.map.LineOfSight(z.pos, target) && !PathCrossesBlock(z.pos, target);
            if (!direct)
            {
                z.repath -= dt;
                if (z.repath <= 0 && searches < MaxSearchesPerTick)
                {
                    searches++;
                    // A failed search is not retried until the next repath window; the zombie shuffles straight at the target meanwhile.
                    z.repath = world.rng.Range(1.2f, 2.4f);
                    var start = new Int2((int)z.pos.x, (int)z.pos.y); var goal = new Int2((int)target.x, (int)target.y);
                    if (!pathfinder.Find(start, goal, z.path, 1500)) { z.path.Clear(); }
                    z.pathIndex = 1;
                }
                if (z.path.Count > 1 && z.pathIndex < z.path.Count)
                {
                    var cur = new Int2((int)z.pos.x, (int)z.pos.y); var next = z.path[z.pathIndex];
                    if (cur.Equals(next)) { z.pathIndex++; if (z.pathIndex >= z.path.Count) return d < 1.2f; next = z.path[z.pathIndex]; }
                    // Crossing a blocking opening: thump it or climb through.
                    if (Math.Abs(next.x - cur.x) + Math.Abs(next.y - cur.y) == 1)
                    {
                        Opening o; world.map.Between(cur.x, cur.y, next.x, next.y, out o);
                        if (o != null && o.BlocksMovement)
                        {
                            if (Vec2.Distance(z.pos, o.Center) < .9f)
                            {
                                if (o.Climbable) { z.state = ZombieState.Climb; z.timer = world.rng.Range(1.3f, 2.2f); z.target = new Vec2(next.x + .5f, next.y + .5f); return false; }
                                z.state = ZombieState.Thump; z.thumping = o.id; z.attackClock = .5f; z.timer = 0; return false;
                            }
                            waypoint = o.Center + (new Vec2(cur.x + .5f, cur.y + .5f) - o.Center) * .5f;
                            goto steer;
                        }
                    }
                    waypoint = new Vec2(next.x + .5f, next.y + .5f);
                }
                else if (z.path.Count == 0) waypoint = target;
            }
        steer:
            var dir = (waypoint - z.pos).Normalized;
            z.facing = MathUtil.WrapAngle(z.facing + MathUtil.Clamp(MathUtil.WrapAngle(dir.Angle - z.facing), -dt * 6, dt * 6));
            var before = z.pos;
            z.pos = world.map.Slide(z.pos, dir * (speed * dt), Zombie.Radius);
            world.index.Moved(z);
            if (Vec2.Distance(before, z.pos) < speed * dt * .2f) { z.timer -= dt; z.repath = Math.Min(z.repath, .6f); }
            return false;
        }

        /// <summary>True if a straight line between two points crosses a closed door/window (zombies must path to it).</summary>
        bool PathCrossesBlock(Vec2 a, Vec2 b)
        {
            int ax = (int)a.x, ay = (int)a.y, bx = (int)b.x, by = (int)b.y;
            if (ax == bx && ay == by) return false;
            var ra = world.map.RoomAt(ax, ay); var rb = world.map.RoomAt(bx, by);
            return (ra == null) != (rb == null) || ra != null && rb != null && ra.building != rb.building;
        }

        void Separate()
        {
            // Light crowd separation among nearby active zombies.
            for (int i = 0; i < active.Count; i++)
            {
                var a = active[i]; if (!a.Alive || a.state == ZombieState.Knocked) continue;
                for (int j = i + 1; j < active.Count; j++)
                {
                    var b = active[j]; if (!b.Alive || b.state == ZombieState.Knocked) continue;
                    var d = a.pos - b.pos; float l = d.SqrLength;
                    if (l > .36f || l < 1e-6f) continue;
                    var push = d.Normalized * ((.6f - (float)Math.Sqrt(l)) * .5f);
                    a.pos = world.map.Slide(a.pos, push, Zombie.Radius); b.pos = world.map.Slide(b.pos, push * -1, Zombie.Radius);
                    world.index.Moved(a); world.index.Moved(b);
                }
                foreach (var s in world.survivors)
                {
                    if (!s.alive) continue;
                    var d = a.pos - s.pos; float l = d.Length;
                    if (l < .58f && l > 1e-4f) { a.pos = world.map.Slide(a.pos, d.Normalized * (.58f - l), Zombie.Radius); world.index.Moved(a); }
                }
            }
        }
    }
}
