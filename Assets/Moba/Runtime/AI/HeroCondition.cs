using BehaviorDesigner.Runtime.Tasks;

namespace Moba.AI
{
    public enum HeroConditionKind { Dead, Retreat, EnemyInSkillRange, SkillReady, HasTarget }

    [TaskCategory("Moba/Hero")]
    public sealed class HeroCondition : Conditional
    {
        public HeroConditionKind Kind;
        private BlackboardAdapter board;
        public override void OnAwake() { board = Owner.GetComponent<BlackboardAdapter>(); }
        public override TaskStatus OnUpdate()
        {
            if (board == null || board.World == null) return TaskStatus.Failure;
            bool match;
            switch (Kind)
            {
                case HeroConditionKind.Dead: match = !board.Actor.Alive; break;
                case HeroConditionKind.Retreat: match = board.RetreatLatched; break;
                case HeroConditionKind.EnemyInSkillRange: match = board.EnemyInSkillRange; break;
                case HeroConditionKind.SkillReady: match = board.SkillReady; break;
                default: match = board.Target != null; break;
            }
            board.Record(ID, FriendlyName, match ? "Success" : "Failure");
            return match ? TaskStatus.Success : TaskStatus.Failure;
        }
    }
}