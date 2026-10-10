using System;

namespace Moba.Core
{
    public enum SimulationMode : byte
    {
        MovementFixture = 0, CombatFixture = 1, Match = 2
    }

    public sealed class BattleConfig
    {
        public const string RulesVersion = "manual-movement-1";
        public const uint DefaultSeed = 0x6D2B79F5;
        public const int LogicHz = 20;
        public readonly int ConfigVersion = 1;
        public readonly int MinX = -40000, MaxX = 40000;
        public readonly int MinZ = -10000, MaxZ = 10000;
        public readonly Int2 BlueSpawn = new Int2(-35000, 0);
        public readonly Int2 RedSpawn = new Int2(35000, 0);
        public readonly Int2 BlueTower = new Int2(-25000, 0);
        public readonly Int2 RedTower = new Int2(25000, 0);
        public readonly int HeroHp = 1000, TowerHp = 6000;
        public readonly int HeroSpeed;
        public readonly SimulationMode Mode;

        public BattleConfig(int heroSpeed = 200,
            SimulationMode mode = SimulationMode.MovementFixture)
        {
            if (heroSpeed < 2 || heroSpeed > 1000 ||
                mode > SimulationMode.Match)
                throw new ArgumentException("Invalid config");
            HeroSpeed = heroSpeed;
            Mode = mode;
        }

        public static BattleConfig CreateDemo()
            => new BattleConfig(mode: SimulationMode.Match);

        public bool Contains(Int2 p)
            => p.X >= MinX && p.X <= MaxX &&
               p.Z >= MinZ && p.Z <= MaxZ;
    }
}