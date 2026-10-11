using System;
using System.IO;

namespace Moba.Core
{
    public sealed class LaneConfig
    {
        public readonly int MinionHp, MinionSpeed, MinionDamage, MinionRange, MinionWindup, MinionCooldown, AcquireRadius;
        public readonly int TowerDamage, TowerRange, TowerWindup, TowerCooldown;
        public readonly int WaveInterval, WaveSize, MinionCapPerTeam, RecoveryRadius, RecoveryPerFrame, RecoveryDelay;
        public Int2 Midpoint => default;

        public LaneConfig(int minionHp = 180, int minionSpeed = 150, int minionDamage = 15, int minionRange = 1200,
            int minionWindup = 6, int minionCooldown = 24, int acquireRadius = 6000,
            int towerDamage = 100, int towerRange = 8000, int towerWindup = 4, int towerCooldown = 20,
            int waveInterval = 300, int waveSize = 3, int minionCapPerTeam = 24,
            int recoveryRadius = 3000, int recoveryPerFrame = 2, int recoveryDelay = 60)
        {
            if (minionHp < 1 || minionHp > 100000 || minionSpeed < 2 || minionSpeed > 1000 ||
                minionDamage < 1 || minionDamage > 100000 || minionRange < 1 || minionRange > 80000 ||
                minionWindup < 1 || minionWindup > 1200 || minionCooldown <= minionWindup || minionCooldown > 12000 ||
                acquireRadius < minionRange || acquireRadius > 80000 || towerDamage < 1 || towerDamage > 100000 ||
                towerRange < 1 || towerRange > 80000 || towerWindup < 1 || towerWindup > 1200 ||
                towerCooldown <= towerWindup || towerCooldown > 12000 || waveInterval < 1 || waveInterval > 12000 ||
                waveSize < 1 || waveSize > 24 || minionCapPerTeam < waveSize || minionCapPerTeam > 24 ||
                recoveryRadius < 1 || recoveryRadius > 80000 || recoveryPerFrame < 1 || recoveryPerFrame > 100000 ||
                recoveryDelay < 1 || recoveryDelay > 12000) throw new ArgumentException("Invalid lane configuration.");
            MinionHp = minionHp; MinionSpeed = minionSpeed; MinionDamage = minionDamage; MinionRange = minionRange;
            MinionWindup = minionWindup; MinionCooldown = minionCooldown; AcquireRadius = acquireRadius;
            TowerDamage = towerDamage; TowerRange = towerRange; TowerWindup = towerWindup; TowerCooldown = towerCooldown;
            WaveInterval = waveInterval; WaveSize = waveSize; MinionCapPerTeam = minionCapPerTeam;
            RecoveryRadius = recoveryRadius; RecoveryPerFrame = recoveryPerFrame; RecoveryDelay = recoveryDelay;
        }

        internal void Write(BinaryWriter w)
        {
            w.Write(MinionHp); w.Write(MinionSpeed); w.Write(MinionDamage); w.Write(MinionRange);
            w.Write(MinionWindup); w.Write(MinionCooldown); w.Write(AcquireRadius);
            w.Write(TowerDamage); w.Write(TowerRange); w.Write(TowerWindup); w.Write(TowerCooldown);
            w.Write(WaveInterval); w.Write(WaveSize); w.Write(MinionCapPerTeam);
            w.Write(RecoveryRadius); w.Write(RecoveryPerFrame); w.Write(RecoveryDelay);
        }

        internal static LaneConfig Read(BinaryReader r) => new LaneConfig(
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
    }
}