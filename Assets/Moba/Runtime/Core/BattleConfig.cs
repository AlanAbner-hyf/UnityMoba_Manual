using System;

namespace Moba.Core
{
    public enum SimulationMode : byte { MovementFixture = 0, CombatFixture = 1, Match = 2 }

    public sealed class BattleConfig
    {
        public const string RulesVersion = "manual-lane-1";
        public const uint DefaultSeed = 0x6D2B79F5;
        public const int LogicHz = 20;
        public readonly int ConfigVersion;
        public readonly int MinX, MaxX, MinZ, MaxZ;
        public readonly Int2 BlueSpawn, RedSpawn, BlueTower, RedTower;
        public readonly int HeroSpeed, HeroHp, TowerHp;
        public readonly SimulationMode Mode;
        public readonly int HeroDamage, HeroRange, HeroWindup, HeroCooldown;
        public readonly int SkillRadius, SkillDamage, SkillCooldown;
        public readonly int RespawnDelay;
        public readonly LaneConfig Lane;

        public BattleConfig(int configVersion = 2, int minX = -40000, int maxX = 40000,
            int minZ = -10000, int maxZ = 10000, int heroSpeed = 200, int heroHp = 1000, int towerHp = 6000,
            SimulationMode mode = SimulationMode.MovementFixture,
            int heroDamage = 60, int heroRange = 1800, int heroWindup = 5, int heroCooldown = 20,
            int skillRadius = 3000, int skillDamage = 120, int skillCooldown = 160, int respawnDelay = 100, LaneConfig lane = null)
        {
            if (configVersion < 1 || minX < -40000 || maxX > 40000 || minZ < -10000 || maxZ > 10000 ||
                minX > -35000 || maxX < 35000 || minZ > 0 || maxZ < 0 ||
                heroSpeed < 2 || heroSpeed > 1000 || heroHp < 1 || heroHp > 100000 || towerHp < 1 || towerHp > 100000)
                throw new ArgumentException("Invalid map or unit configuration.");
            if (mode > SimulationMode.Match || heroDamage < 1 || heroDamage > 100000 ||
                heroRange < 1 || heroRange > 80000 || heroWindup < 1 || heroWindup > 1200 ||
                heroCooldown <= heroWindup || heroCooldown > 12000 || skillRadius < 1 || skillRadius > 80000 ||
                skillDamage < 1 || skillDamage > 100000 || skillCooldown < 1 || skillCooldown > 12000 ||
                respawnDelay < 1 || respawnDelay > 12000)
                throw new ArgumentException("Invalid combat configuration.");
            ConfigVersion = configVersion;
            MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ;
            BlueSpawn = new Int2(-35000, 0); RedSpawn = new Int2(35000, 0);
            BlueTower = new Int2(-25000, 0); RedTower = new Int2(25000, 0);
            HeroSpeed = heroSpeed; HeroHp = heroHp; TowerHp = towerHp;
            Mode = mode;
            HeroDamage = heroDamage; HeroRange = heroRange; HeroWindup = heroWindup; HeroCooldown = heroCooldown;
            SkillRadius = skillRadius; SkillDamage = skillDamage; SkillCooldown = skillCooldown; RespawnDelay = respawnDelay;
            Lane = lane ?? new LaneConfig();
        }

        public static BattleConfig CreateDemo() => new BattleConfig(mode: SimulationMode.Match);

        public bool Contains(Int2 point) => point.X >= MinX && point.X <= MaxX && point.Z >= MinZ && point.Z <= MaxZ;
    }
}