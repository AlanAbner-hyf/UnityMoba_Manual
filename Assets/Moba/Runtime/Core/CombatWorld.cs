using System;
using System.Collections.Generic;

namespace Moba.Core
{
    public sealed partial class CombatWorld
    {
        public BattleConfig Config { get; }
        public WorldState State { get; }
        public IReadOnlyList<SimEvent> Events { get; }
        private readonly List<EntityState> entities;
        private readonly List<SimEvent> events = new List<SimEvent>();
        private readonly List<PendingDamage> damage = new List<PendingDamage>();
        private readonly List<uint> areaCasts = new List<uint>();
        internal int CurrentFrame { get; private set; }

        private CombatWorld(BattleConfig config, uint seed)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            entities = new List<EntityState>(54);
            Events = events.AsReadOnly();
            for (uint id = 1; id <= 6; id++)
            {
                bool blue = (id & 1) == 1, tower = id >= 5;
                entities.Add(new EntityState
                {
                    EntityId = id,
                    Kind = tower ? EntityKind.Tower : id >= 3 ? EntityKind.RobotHero : EntityKind.PlayerHero,
                    Team = blue ? Team.Blue : Team.Red,
                    Position = tower ? (blue ? config.BlueTower : config.RedTower) : (blue ? config.BlueSpawn : config.RedSpawn),
                    Hp = tower ? config.TowerHp : config.HeroHp
                });
            }
            State = new WorldState(entities) { NextEntityId = 7, RandomState = seed };
        }

        public static CombatWorld Create(BattleConfig config, uint seed = BattleConfig.DefaultSeed) => new CombatWorld(config, seed);

        public void Step(FrameCommands commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (State.Frame == int.MaxValue || commands.FrameId != State.Frame + 1) throw new ArgumentException("Non-contiguous frame.");
            // Validate every command before changing any state, so a rejected bundle is atomic.
            Validate(commands.BluePlayer); Validate(commands.RedPlayer);
            foreach (var robot in commands.Robots) Validate(robot.Command);
            if (State.Result != MatchResult.None) throw new InvalidOperationException("Match already ended.");
            CurrentFrame = commands.FrameId;
            events.Clear(); damage.Clear(); areaCasts.Clear();
            if (Config.Mode == SimulationMode.Match) BeginMatchFrame();
            else BeginCombatFrame();
            Apply(State.Entities[0], commands.BluePlayer); Apply(State.Entities[1], commands.RedPlayer);
            foreach (var robot in commands.Robots) Apply(State.Entities[(int)robot.ActorId - 1], robot.Command);
            if (Config.Mode == SimulationMode.Match) UpdateNpcIntents();
            foreach (var entity in entities) CombatFsm.Tick(this, entity, SimulationPhase.Movement);
            foreach (var entity in entities) CombatFsm.Tick(this, entity, SimulationPhase.Actions);
            foreach (uint source in areaCasts)
                foreach (var target in entities)
                    damage.Add(new PendingDamage(source, target.EntityId, DamageKind.AreaSkill));
            ResolveDamage();
            if (Config.Mode == SimulationMode.Match) EndMatchFrame();
            State.Frame = commands.FrameId;
        }

        private void Validate(CombatCommand command)
        {
            command.Validate(Config);
            if (Config.Mode == SimulationMode.MovementFixture && (command.Kind == CommandKind.Attack || command.Kind == CommandKind.CastArea))
                throw new NotSupportedException("The movement fixture does not accept combat commands.");
        }

        private void Apply(EntityState entity, CombatCommand command)
        {
            if (command.Kind == CommandKind.NoOp || !entity.Alive) return;
            if (command.Kind == CommandKind.CastArea)
            {
                if (!entity.IsHero || CurrentFrame < entity.SkillReadyFrame) return;
                entity.SkillReadyFrame = checked(CurrentFrame + Config.SkillCooldown);
                entity.Intent = CombatCommand.NoOp;
                CombatFsm.Transition(this, entity, FsmState.Idle);
                areaCasts.Add(entity.EntityId);
                Emit(new SimEvent(CurrentFrame, SimEventKind.SkillCast, entity.EntityId));
                return;
            }
            if (command.Kind == CommandKind.Attack)
            {
                if (!IsEnemy(entity, FindEntity(command.TargetId))) return;
                if (entity.Intent.Kind == CommandKind.Attack && entity.Intent.TargetId == command.TargetId) return;
            }
            entity.Intent = command;
            CombatFsm.Transition(this, entity, command.Kind == CommandKind.MoveTo ? FsmState.Moving : FsmState.Idle);
        }

        public EntityState FindEntity(uint id)
        {
            // Bounded to 54 entities; explicit traversal avoids unordered collection behaviour.
            foreach (var entity in entities) if (entity.EntityId == id) return entity;
            return null;
        }

        internal void Emit(SimEvent value) => events.Add(value);
        internal int AttackWindup(EntityState e) => e.Kind == EntityKind.Tower
            ? Config.Lane.TowerWindup : e.Kind == EntityKind.Minion
            ? Config.Lane.MinionWindup : Config.HeroWindup;
        internal int AttackCooldown(EntityState e) => e.Kind == EntityKind.Tower
            ? Config.Lane.TowerCooldown : e.Kind == EntityKind.Minion
            ? Config.Lane.MinionCooldown : Config.HeroCooldown;
        private int AttackRange(EntityState e) => e.Kind == EntityKind.Tower
            ? Config.Lane.TowerRange : e.Kind == EntityKind.Minion
            ? Config.Lane.MinionRange : Config.HeroRange;
        private int AttackDamage(EntityState e) => e.Kind == EntityKind.Tower
            ? Config.Lane.TowerDamage : e.Kind == EntityKind.Minion
            ? Config.Lane.MinionDamage : Config.HeroDamage;
        private int MoveSpeed(EntityState e) => e.Kind == EntityKind.Tower ? 0 :
            e.Kind == EntityKind.Minion ? Config.Lane.MinionSpeed : Config.HeroSpeed;
        private static bool IsEnemy(EntityState source, EntityState target)
            => source != null && source.Alive && target != null && target.Alive && source.Team != target.Team;

        internal void AdvanceIntent(EntityState entity)
        {
            if (!entity.Alive) return;
            if (entity.Intent.Kind == CommandKind.MoveTo)
            {
                Int2 goal = entity.Intent.Point;
                if (MoveSpeed(entity) > 0) entity.Position = IntegerMath.MoveTowards(entity.Position, goal, MoveSpeed(entity));
                if (entity.Position == goal || MoveSpeed(entity) == 0) ClearIntent(entity);
                else CombatFsm.Transition(this, entity, FsmState.Moving);
            }
            else if (entity.Intent.Kind == CommandKind.Attack)
            {
                var target = FindEntity(entity.Intent.TargetId);
                if (!IsEnemy(entity, target)) { ClearIntent(entity); return; }
                if (!InAttackRange(entity, target))
                {
                    CombatFsm.Transition(this, entity, MoveSpeed(entity) > 0 ? FsmState.Moving : FsmState.Idle);
                    if (MoveSpeed(entity) > 0) entity.Position = IntegerMath.MoveTowards(entity.Position, target.Position, MoveSpeed(entity));
                }
                else if (entity.State == FsmState.Moving) CombatFsm.Transition(this, entity, FsmState.Idle);
            }
        }

        internal void TryStartAttack(EntityState entity)
        {
            if (!entity.Alive || entity.Intent.Kind != CommandKind.Attack || CurrentFrame < entity.AttackReadyFrame) return;
            var target = FindEntity(entity.Intent.TargetId);
            if (IsEnemy(entity, target) && InAttackRange(entity, target))
                CombatFsm.Transition(this, entity, FsmState.AttackWindup);
        }

        internal void ResolveWindup(EntityState entity)
        {
            var target = FindEntity(entity.Intent.TargetId);
            if (!IsEnemy(entity, target)) { ClearIntent(entity); return; }
            if (!InAttackRange(entity, target))
            {
                CombatFsm.Transition(this, entity, MoveSpeed(entity) > 0 ? FsmState.Moving : FsmState.Idle);
                return;
            }
            if (CurrentFrame < entity.HitFrame) return;
            damage.Add(new PendingDamage(entity.EntityId, target.EntityId, DamageKind.BasicAttack));
            CombatFsm.Transition(this, entity, FsmState.Idle);
        }

        private void ClearIntent(EntityState entity)
        {
            entity.Intent = CombatCommand.NoOp;
            CombatFsm.Transition(this, entity, FsmState.Idle);
        }

        private void ResolveDamage()
        {
            damage.Sort((a, b) => {
                int compare = a.SourceId.CompareTo(b.SourceId);
                if (compare == 0) compare = a.Kind.CompareTo(b.Kind);
                return compare == 0 ? a.TargetId.CompareTo(b.TargetId) : compare;
            });
            foreach (var pending in damage)
            {
                var source = FindEntity(pending.SourceId); var target = FindEntity(pending.TargetId);
                if (!IsEnemy(source, target)) continue;
                bool area = pending.Kind == DamageKind.AreaSkill;
                if (!IntegerMath.WithinRange(source.Position, target.Position, area ? Config.SkillRadius : AttackRange(source))) continue;
                int amount = Math.Min(target.Hp, area ? Config.SkillDamage : AttackDamage(source));
                target.Hp -= amount; target.LastDamagedFrame = CurrentFrame;
                Emit(new SimEvent(CurrentFrame, SimEventKind.Damage, source.EntityId, target.EntityId, amount));
                if (target.Alive) continue;
                CombatFsm.Transition(this, target, FsmState.Dead);
                Emit(new SimEvent(CurrentFrame, SimEventKind.Died, source.EntityId, target.EntityId));
            }
        }
        public bool InAttackRange(EntityState source, EntityState target)
            => source != null && target != null &&
               IntegerMath.WithinRange(source.Position, target.Position, AttackRange(source));

        private void BeginCombatFrame()
        {
            foreach (var entity in entities)
            {
                if (!entity.IsHero || entity.Alive || entity.RespawnFrame == 0 ||
                    CurrentFrame < entity.RespawnFrame) continue;
                entity.Hp = Config.HeroHp;
                entity.Position = entity.Team == Team.Blue ? Config.BlueSpawn : Config.RedSpawn;
                entity.Intent = CombatCommand.NoOp;
                entity.HitFrame = entity.RespawnFrame = entity.AttackReadyFrame = entity.SkillReadyFrame = 0;
                entity.LastDamagedFrame = -1;
                CombatFsm.Transition(this, entity, FsmState.Idle);
                Emit(new SimEvent(CurrentFrame, SimEventKind.Respawned, entity.EntityId));
            }
        }

    }
}