using Ashfall.Core;

namespace Ashfall.View
{
    public sealed partial class AshfallGame
    {
        // Observe existing simulation events at their presentation boundary. No
        // gameplay writes, network access, generic UI text or per-frame samples.
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        void TraceWorkbenchEvent(SimEvent e)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            switch (e.kind)
            {
                case EventKind.Swing: case EventKind.Gunshot: case EventKind.Empty:
                case EventKind.Explosion: case EventKind.Hit: case EventKind.Kill:
                case EventKind.Hurt: case EventKind.Death: case EventKind.HeartDestroyed:
                case EventKind.Engine: case EventKind.Crash: break;
                default: return;
            }
            try
            {
                if (World == null) return;
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var target = e.zombie >= 0 && e.zombie < World.zombies.Count ? World.zombies[e.zombie] : null;
                var actor = e.survivor >= 0 && e.survivor < World.survivors.Count ? World.survivors[e.survivor] : null;
                var weapon = actor == null ? null : actor.Primary;
                string state = "simMinutes=" + World.minutes.ToString("F3", culture)
                    + "; actor=" + e.survivor + "; zombie=" + e.zombie
                    + "; x=" + e.pos.x.ToString("F2", culture) + "; y=" + e.pos.y.ToString("F2", culture)
                    + "; detail=" + e.text
                    + "; heldWeapon=" + (weapon == null ? "none" : weapon.Def.id)
                    + "; heldItem=" + (weapon == null ? "none" : weapon.uid.ToString())
                    + "; loaded=" + (weapon == null ? "unknown" : weapon.loaded.ToString())
                    + "; actorHealthAtDispatch=" + (actor == null ? "unknown" : actor.health.ToString("F2", culture))
                    + "; targetHealthAtDispatch=" + (target == null ? "unknown" : target.hp.ToString("F2", culture));
                WorkbenchDebug.Event("simulation." + e.kind.ToString().ToLowerInvariant() + ".emitted", "simulation-events",
                    target != null ? "zombie:" + e.zombie : "survivor:" + e.survivor, state);
            }
            catch (System.Exception) { /* Diagnostics must not interrupt event delivery. */ }
#endif
        }
    }
}
