using System;

namespace Moba.Core
{
    public static class IntegerMath
    {
        public static long DistanceSquared(Int2 a, Int2 b)
        {
            long x = (long)a.X - b.X, z = (long)a.Z - b.Z;
            return checked(x * x + z * z);
        }

        public static long SqrtCeiling(long value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            long low = 0, high = Math.Min(value, 3037000499L), floor = 0;
            while (low <= high)
            {
                long mid = low + (high - low) / 2;
                if (mid * mid <= value) { floor = mid; low = mid + 1; }
                else high = mid - 1;
            }
            return floor * floor == value ? floor : floor + 1;
        }

        public static bool WithinRange(Int2 a, Int2 b, int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            return DistanceSquared(a, b) <= (long)radius * radius;
        }

        public static Int2 MoveTowards(Int2 current, Int2 target, int step)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            long square = DistanceSquared(current, target);
            if (square <= (long)step * step) return target;
            long distance = SqrtCeiling(square);
            // C# integer division truncates toward zero, including negative directions.
            long x = ((long)target.X - current.X) * step / distance;
            long z = ((long)target.Z - current.Z) * step / distance;
            return new Int2(checked((int)(current.X + x)), checked((int)(current.Z + z)));
        }
    }
}