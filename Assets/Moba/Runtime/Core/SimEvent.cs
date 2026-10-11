namespace Moba.Core
{
    public enum SimEventKind : byte { StateChanged, AttackStarted, SkillCast, Damage, Died, Spawned, Respawned, Healed }
    internal enum DamageKind : byte { BasicAttack = 0, AreaSkill = 1 }

    // Read-only output for presentation and diagnostics; never fed back into the simulation.
    public readonly struct SimEvent
    {
        public readonly int Frame, Amount;
        public readonly SimEventKind Kind;
        public readonly uint SourceId, TargetId;
        public readonly FsmState PreviousState, NextState;
        public SimEvent(int frame, SimEventKind kind, uint sourceId, uint targetId = 0, int amount = 0,
            FsmState previousState = FsmState.Idle, FsmState nextState = FsmState.Idle)
        {
            Frame = frame; Kind = kind; SourceId = sourceId; TargetId = targetId; Amount = amount;
            PreviousState = previousState; NextState = nextState;
        }
    }

    internal readonly struct PendingDamage
    {
        public readonly uint SourceId, TargetId;
        public readonly DamageKind Kind;
        public PendingDamage(uint source, uint target, DamageKind kind) { SourceId = source; TargetId = target; Kind = kind; }
    }
}