using System;

namespace Moba.Core
{
    public readonly struct Int2 : IEquatable<Int2>
    {
        public readonly int X;
        public readonly int Z;
        public Int2(int x, int z) { X = x; Z = z; }
        public bool Equals(Int2 other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is Int2 other && Equals(other);
        public override int GetHashCode() { unchecked { return (X * 397) ^ Z; } }
        public static bool operator ==(Int2 a, Int2 b) => a.Equals(b);
        public static bool operator !=(Int2 a, Int2 b) => !a.Equals(b);
        public override string ToString() => "(" + X + "," + Z + ")";
    }
}
