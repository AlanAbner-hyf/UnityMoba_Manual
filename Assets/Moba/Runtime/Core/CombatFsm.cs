namespace Moba.Core
{
    internal enum SimulationPhase { Movement, Actions }

    internal interface ICombatState
    {
        void Enter(CombatWorld world, EntityState entity);
        void Tick(CombatWorld world, EntityState entity, SimulationPhase phase);
        void Exit(CombatWorld world, EntityState entity);
    }

    // States are stateless singletons. Every timer and persistent intent lives in the hashed EntityState.
    internal static class CombatFsm
    {
        private static readonly ICombatState[] States = { new IdleState(), new MovingState(), new AttackWindupState(), new DeadState() };

        public static void Transition(CombatWorld world, EntityState entity, FsmState next)
        {
            if (entity.State == next) return;
            FsmState previous = entity.State;
            States[(int)previous].Exit(world, entity);
            entity.State = next;
            States[(int)next].Enter(world, entity);
            world.Emit(new SimEvent(world.CurrentFrame, SimEventKind.StateChanged, entity.EntityId, 0, 0, previous, next));
        }

        public static void Tick(CombatWorld world, EntityState entity, SimulationPhase phase)
            => States[(int)entity.State].Tick(world, entity, phase);
    }

    internal abstract class LivingState : ICombatState
    {
        public virtual void Enter(CombatWorld world, EntityState entity) { }
        public virtual void Exit(CombatWorld world, EntityState entity) { }
        public virtual void Tick(CombatWorld world, EntityState entity, SimulationPhase phase)
        {
            if (phase == SimulationPhase.Movement) world.AdvanceIntent(entity);
            else world.TryStartAttack(entity);
        }
    }

    internal sealed class IdleState : LivingState { }
    internal sealed class MovingState : LivingState { }

    internal sealed class AttackWindupState : LivingState
    {
        public override void Enter(CombatWorld world, EntityState entity)
        {
            entity.HitFrame = checked(world.CurrentFrame + world.AttackWindup(entity));
            entity.AttackReadyFrame = checked(world.CurrentFrame + world.AttackCooldown(entity));
            world.Emit(new SimEvent(world.CurrentFrame, SimEventKind.AttackStarted, entity.EntityId, entity.Intent.TargetId));
        }

        public override void Exit(CombatWorld world, EntityState entity)
        {
            entity.HitFrame = 0; // Leaving windup never refunds AttackReadyFrame.
        }

        public override void Tick(CombatWorld world, EntityState entity, SimulationPhase phase)
        {
            if (phase == SimulationPhase.Movement) world.AdvanceIntent(entity);
            else world.ResolveWindup(entity);
        }
    }

    internal sealed class DeadState : ICombatState
    {
        public void Enter(CombatWorld world, EntityState entity)
        {
            entity.Intent = CombatCommand.NoOp; entity.HitFrame = 0;
            entity.RespawnFrame = entity.IsHero ? checked(world.CurrentFrame + world.Config.RespawnDelay) : 0;
        }
        public void Tick(CombatWorld world, EntityState entity, SimulationPhase phase) { }
        public void Exit(CombatWorld world, EntityState entity) { }
    }
}