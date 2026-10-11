using System;

namespace Moba.Core
{
    public sealed partial class CombatWorld
    {
        public Int2 SpawnFor(Team team) => team == Team.Blue ? Config.BlueSpawn : Config.RedSpawn;
        public Int2 EnemyTowerFor(Team team) => team == Team.Blue ? Config.RedTower : Config.BlueTower;
        public int MaxHp(EntityState entity) => entity.Kind == EntityKind.Tower ? Config.TowerHp : entity.Kind == EntityKind.Minion ? Config.Lane.MinionHp : Config.HeroHp;

        private void BeginMatchFrame()
        {
            foreach (var entity in entities)
            {
                if (!entity.IsHero || entity.Alive || entity.RespawnFrame == 0 || CurrentFrame < entity.RespawnFrame) continue;
                var respawn = SpawnFor(entity.Team);
                entity.Hp = Config.HeroHp; entity.Position = respawn;
                entity.Intent = CombatCommand.NoOp; entity.HitFrame = 0; entity.RespawnFrame = 0;
                entity.AttackReadyFrame = 0; entity.SkillReadyFrame = 0; entity.LastDamagedFrame = -1; entity.PathIndex = 0;
                CombatFsm.Transition(this, entity, FsmState.Idle);
                Emit(new SimEvent(CurrentFrame, SimEventKind.Respawned, entity.EntityId));
            }
            if (CurrentFrame != State.NextWaveFrame) return;
            for (int teamIndex = 0; teamIndex < 2; teamIndex++)
            {
                var team = (Team)teamIndex; int living = 0;
                foreach (var entity in entities) if (entity.Kind == EntityKind.Minion && entity.Team == team && entity.Alive) living++;
                int count = Math.Min(Config.Lane.WaveSize, Config.Lane.MinionCapPerTeam - living);
                for (int i = 0; i < count; i++)
                {
                    var spawn = new Int2(SpawnFor(team).X, (i - 1) * 1000);
                    var entity = new EntityState
                    {
                        EntityId = State.NextEntityId++,
                        Kind = EntityKind.Minion,
                        Team = team,
                        Position = spawn,
                        Hp = Config.Lane.MinionHp
                    };
                    entities.Add(entity); Emit(new SimEvent(CurrentFrame, SimEventKind.Spawned, entity.EntityId));
                }
            }
            State.WaveNumber++; State.NextWaveFrame = checked(CurrentFrame + Config.Lane.WaveInterval);
        }

        private void UpdateNpcIntents()
        {
            foreach (var entity in entities)
            {
                if (!entity.Alive || entity.IsHero) continue;
                bool tower = entity.Kind == EntityKind.Tower;
                int radius = tower ? Config.Lane.TowerRange : Config.Lane.AcquireRadius;
                var priority = tower ? TargetPriority.Tower : TargetPriority.Minion;
                var target = TargetSelector.Find(this, entity, radius, priority);
                var current = entity.Intent.Kind == CommandKind.Attack ? FindEntity(entity.Intent.TargetId) : null;
                // Keep a valid target within its priority class, avoiding perpetual windup restarts as distances change.
                if (IsEnemy(entity, current) && IntegerMath.WithinRange(entity.Position, current.Position, radius) &&
                    (target == null || TargetSelector.Rank(current, priority) <= TargetSelector.Rank(target, priority))) target = current;
                if (target != null) Apply(entity, new CombatCommand(CommandKind.Attack, default, target.EntityId));
                else if (tower) ClearIntent(entity);
                else
                {
                    if (entity.PathIndex == 0 && (IntegerMath.WithinRange(entity.Position, Config.Lane.Midpoint, 1500) || (entity.Team == Team.Blue ? entity.Position.X >= 0 : entity.Position.X <= 0))) entity.PathIndex = 1;
                    Int2 destination = entity.PathIndex == 0 ? Config.Lane.Midpoint : EnemyTowerFor(entity.Team);
                    if (entity.Intent.Kind != CommandKind.MoveTo || entity.Intent.Point != destination)
                        Apply(entity, CombatCommand.MoveTo(destination));
                }
            }
        }

        private void EndMatchFrame()
        {
            foreach (var entity in entities)
            {
                if (!entity.IsHero || !entity.Alive || entity.Hp >= Config.HeroHp ||
                    !IntegerMath.WithinRange(entity.Position, SpawnFor(entity.Team), Config.Lane.RecoveryRadius) ||
                    (entity.LastDamagedFrame >= 0 && CurrentFrame - entity.LastDamagedFrame < Config.Lane.RecoveryDelay)) continue;
                int amount = Math.Min(Config.Lane.RecoveryPerFrame, Config.HeroHp - entity.Hp);
                entity.Hp += amount; Emit(new SimEvent(CurrentFrame, SimEventKind.Healed, entity.EntityId, entity.EntityId, amount));
            }
            entities.RemoveAll(e => e.Kind == EntityKind.Minion && !e.Alive);
            bool blueDead = !FindEntity(5).Alive, redDead = !FindEntity(6).Alive;
            if (blueDead && redDead) State.Result = MatchResult.Draw;
            else if (blueDead) State.Result = MatchResult.RedWins;
            else if (redDead) State.Result = MatchResult.BlueWins;
        }
    }
}