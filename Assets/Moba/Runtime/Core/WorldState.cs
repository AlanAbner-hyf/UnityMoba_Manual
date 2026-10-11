using System.Collections.Generic;

namespace Moba.Core
{
    public enum Team : byte { Blue, Red }
    public enum EntityKind : byte { PlayerHero, RobotHero, Tower, Minion }
    public enum FsmState : byte { Idle, Moving, AttackWindup, Dead }
    public enum MatchResult : byte { None, BlueWins, RedWins, Draw }

    public sealed class EntityState
    {
        public uint EntityId { get; internal set; }
        public EntityKind Kind { get; internal set; }
        public Team Team { get; internal set; }
        public Int2 Position { get; internal set; }
        public int Hp { get; internal set; }
        public FsmState State { get; internal set; }
        public CombatCommand Intent { get; internal set; }
        public int AttackReadyFrame { get; internal set; }
        public int HitFrame { get; internal set; }
        public int SkillReadyFrame { get; internal set; }
        public int RespawnFrame { get; internal set; }
        public int LastDamagedFrame { get; internal set; } = -1;
        public int PathIndex { get; internal set; }
        public bool Alive => Hp > 0;
        public bool IsHero => Kind == EntityKind.PlayerHero ||
                              Kind == EntityKind.RobotHero;
    }

    public sealed class WorldState
    {
        public int Frame { get; internal set; }
        public uint NextEntityId { get; internal set; }
        public uint RandomState { get; internal set; }
        public MatchResult Result { get; internal set; }
        public int NextWaveFrame { get; internal set; } = 1;
        public int WaveNumber { get; internal set; }
        public IReadOnlyList<EntityState> Entities { get; }
        internal WorldState(List<EntityState> entities)
        {
            Entities = entities.AsReadOnly();
        }
    }
}
