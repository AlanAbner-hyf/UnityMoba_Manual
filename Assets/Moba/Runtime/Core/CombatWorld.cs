using System;
using System.Collections.Generic;

namespace Moba.Core
{
    public sealed partial class CombatWorld
    {
        private readonly List<EntityState> entities = new List<EntityState>();
        public BattleConfig Config { get; }
        public WorldState State { get; }

        private CombatWorld(BattleConfig config, uint seed)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            for (uint id = 1; id <= 6; id++)
            {
                bool blue = (id & 1) == 1;
                bool tower = id >= 5;
                entities.Add(new EntityState
                {
                    EntityId = id,
                    Kind = tower ? EntityKind.Tower :
                        id >= 3 ? EntityKind.RobotHero : EntityKind.PlayerHero,
                    Team = blue ? Team.Blue : Team.Red,
                    Position = tower
                        ? (blue ? config.BlueTower : config.RedTower)
                        : (blue ? config.BlueSpawn : config.RedSpawn),
                    Hp = tower ? config.TowerHp : config.HeroHp,
                    Intent = CombatCommand.NoOp,
                    State = FsmState.Idle
                });
            }
            State = new WorldState(entities)
            {
                NextEntityId = 7,
                RandomState = seed
            };
        }

        public static CombatWorld Create(BattleConfig config,
            uint seed = BattleConfig.DefaultSeed) => new CombatWorld(config, seed);

        public EntityState FindEntity(uint id)
        {
            foreach (var e in entities)
                if (e.EntityId == id) return e;
            return null;
        }

        private void ValidateMovement(CombatCommand command)
        {
            command.Validate(Config);
            if (command.Kind != CommandKind.NoOp &&
                command.Kind != CommandKind.MoveTo)
                throw new NotSupportedException("Movement stage only");
        }

        private void ApplyMovement(EntityState entity, CombatCommand command)
        {
            if (!entity.Alive || command.Kind == CommandKind.NoOp) return;
            entity.Intent = command;
            entity.State = FsmState.Moving;
        }

        public void Step(FrameCommands commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (State.Frame == int.MaxValue ||
                commands.FrameId != State.Frame + 1)
                throw new ArgumentException("Non-contiguous frame");
            ValidateMovement(commands.BluePlayer);
            ValidateMovement(commands.RedPlayer);
            foreach (var robot in commands.Robots) ValidateMovement(robot.Command);

            ApplyMovement(FindEntity(1), commands.BluePlayer);
            ApplyMovement(FindEntity(2), commands.RedPlayer);
            foreach (var robot in commands.Robots)
                ApplyMovement(FindEntity(robot.ActorId), robot.Command);

            foreach (var entity in entities)
            {
                if (!entity.Alive || !entity.IsHero ||
                    entity.Intent.Kind != CommandKind.MoveTo) continue;
                entity.Position = IntegerMath.MoveTowards(
                    entity.Position, entity.Intent.Point, Config.HeroSpeed);
                if (entity.Position == entity.Intent.Point)
                {
                    entity.Intent = CombatCommand.NoOp;
                    entity.State = FsmState.Idle;
                }
            }
            State.Frame = commands.FrameId;
        }
    }
}