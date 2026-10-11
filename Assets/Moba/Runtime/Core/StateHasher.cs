using System;
using System.Security.Cryptography;

namespace Moba.Core
{
    public static class StateHasher
    {
        public const int HashSize = 32;
        public static byte[] Compute(WorldState state) => Hash(CanonicalStateWriter.Write(state));
        public static byte[] ConfigHash(BattleConfig config) => Hash(CanonicalStateWriter.WriteConfig(config));
        public static byte[] Hash(byte[] canonicalBytes)
        { using (var sha = SHA256.Create()) return sha.ComputeHash(canonicalBytes); }
        public static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        public static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != HashSize || b.Length != HashSize) return false;
            int difference = 0;
            for (int i = 0; i < HashSize; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
    }
}