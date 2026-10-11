using BehaviorDesigner.Runtime.Tasks;
using Moba.Core;

namespace Moba.AI
{
    public enum HeroActionKind { HoldUntilAlive, RetreatOrRecover, IssueAreaSkill, AttackOrChase, MoveAlongLane }

    [TaskCategory("Moba/Hero")]
    public sealed class HeroAction : Action
    {
        public HeroActionKind Kind;
        private BlackboardAdapter board;
        public override void OnAwake() { board = Owner.GetComponent<BlackboardAdapter>(); }
        public override void OnStart() { board.Record(ID, FriendlyName, "Enter"); }
        public override void OnEnd() { board.Record(ID, FriendlyName, "Exit"); }
        public override TaskStatus OnUpdate()
        {
            var actor = board.Actor;
            TaskStatus status = TaskStatus.Running;
            switch (Kind)
            {
                case HeroActionKind.HoldUntilAlive:
                    board.Decision = "Dead"; if (actor.Alive) status = TaskStatus.Success;
                    break;
                case HeroActionKind.RetreatOrRecover:
                    board.Decision = board.InRecoveryZone ? "Recover" : "Retreat";
                    if (!board.RetreatLatched) { status = TaskStatus.Success; break; }
                    if (!board.InRecoveryZone) board.Submit(CombatCommand.MoveTo(board.World.SpawnFor(actor.Team)));
                    else if (actor.Intent.Kind != CommandKind.NoOp) board.Submit(CombatCommand.MoveTo(actor.Position));
                    break;
                case HeroActionKind.IssueAreaSkill:
                    board.Decision = "CastArea";
                    if (!board.SkillReady || !board.EnemyInSkillRange) { status = TaskStatus.Failure; break; }
                    board.Submit(new CombatCommand(CommandKind.CastArea));
                    // Running holds this branch for the rest of this plugin tick. Cooldown causes a conditional abort next evaluation.
                    break;
                case HeroActionKind.AttackOrChase:
                    if (board.Target == null) { status = TaskStatus.Failure; break; }
                    board.Decision = board.World.InAttackRange(actor, board.Target) ? "Attack" : "Chase";
                    board.Submit(new CombatCommand(CommandKind.Attack, default, board.Target.EntityId));
                    break;
                case HeroActionKind.MoveAlongLane:
                    board.Decision = "Advance";
                    board.Submit(CombatCommand.MoveTo(board.World.EnemyTowerFor(actor.Team)));
                    break;
            }
            board.Record(ID, FriendlyName, status.ToString());
            return status;
        }
    }
}