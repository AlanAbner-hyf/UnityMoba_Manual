using System.IO;

namespace Moba.Core
{
    // One fixed-order encoding is shared by the handshake, replay header and configuration hash.
    public static class BattleConfigCodec
    {
        public static void Write(BinaryWriter w, BattleConfig c)
        {
            w.Write(c.ConfigVersion); w.Write(c.MinX); w.Write(c.MaxX); w.Write(c.MinZ); w.Write(c.MaxZ);
            w.Write(c.HeroSpeed); w.Write(c.HeroHp); w.Write(c.TowerHp); w.Write((byte)c.Mode);
            w.Write(c.HeroDamage); w.Write(c.HeroRange); w.Write(c.HeroWindup); w.Write(c.HeroCooldown);
            w.Write(c.SkillRadius); w.Write(c.SkillDamage); w.Write(c.SkillCooldown); w.Write(c.RespawnDelay);
            c.Lane.Write(w);
        }

        public static BattleConfig Read(BinaryReader r) => new BattleConfig(
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), (SimulationMode)r.ReadByte(),
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), LaneConfig.Read(r));
    }
}