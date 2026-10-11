namespace Moba.Core
{
    public enum TargetPriority { Robot, Minion, Tower }

    public static class TargetSelector
    {
        public static int Rank(EntityState target, TargetPriority priority)
        {
            if (priority == TargetPriority.Tower) return target.Kind == EntityKind.Minion ? 0 : target.IsHero ? 1 : 2;
            if (priority == TargetPriority.Robot) return target.IsHero ? 0 : target.Kind == EntityKind.Minion ? 1 : 2;
            return target.Kind == EntityKind.Tower ? 1 : 0;
        }

        public static EntityState Find(CombatWorld world, EntityState source, int radius, TargetPriority priority)
        {
            EntityState best = null; int rank = int.MaxValue; long distance = long.MaxValue;
            if (source == null || !source.Alive) return null;
            foreach (var candidate in world.State.Entities)
            {
                if (!candidate.Alive || candidate.Team == source.Team ||
                    (priority == TargetPriority.Tower && candidate.Kind == EntityKind.Tower)) continue;
                long d = IntegerMath.DistanceSquared(source.Position, candidate.Position);
                if (d > (long)radius * radius) continue;
                int r = Rank(candidate, priority);
                if (r > rank || (r == rank && (d > distance || (d == distance && candidate.EntityId > best.EntityId)))) continue;
                best = candidate; rank = r; distance = d;
            }
            return best;
        }
    }
}