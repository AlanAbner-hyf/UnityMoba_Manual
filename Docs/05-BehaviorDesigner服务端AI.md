# 05　BehaviorDesigner服务端AI

[上一阶段](04-兵线防御塔与胜负.md) · [返回实现大纲](手动实现大纲.md) · [下一阶段](06-编帧与状态校验.md)

适用：Unity 6000.3.23f1 / UnityMOBA_Manual。以下步骤用于后续手动实施；本次仅生成文档。

## 本阶段要得到什么

两个机器人英雄自动进攻、施法、低血量撤退和回满后返回；能在 Behavior Designer 中看到真实活动节点。行为树只发命令，仍由相同的 CombatWorld/FSM 执行。

前置条件：阶段 04 的 Match 世界可完整结束。阅读顺序：先接插件和树，再接本地权威驱动，最后验证 Tick 次数。

## 1. 导入并隔离插件依赖

通过 Assets → Import Package → Custom Package 选择已有合法 Behavior Designer 安装包。保留原 Assets/Behavior Designer 和 Gizmos。不要导入 Behavior Designer Pro 的 API 示例来替换当前 1.7.x 项目。

核对插件任务程序集 `BehaviorDesigner.Tasks` 与预编译 `BehaviorDesigner.Runtime.dll`。若安装包没有程序集定义，对照原项目 [Runtime asmdef](../../UnityMOBA/Assets/Behavior%20Designer/Runtime/BehaviorDesigner.Tasks.asmdef) 和 [Editor asmdef](../../UnityMOBA/Assets/Behavior%20Designer/Editor/BehaviorDesigner.Tasks.Editor.asmdef)，补齐相同隔离结构后再编译。

创建 `Runtime/AI/Moba.AI.asmdef`：

```json
{
  "name": "Moba.AI",
  "references": ["BehaviorDesigner.Tasks", "Moba.Core"],
  "overrideReferences": true,
  "precompiledReferences": ["BehaviorDesigner.Runtime.dll"]
}
```

在 Moba.Bootstrap 的 references 增加 Moba.AI。PlayMode 测试以后引用插件 DLL 时，也要显式保留 NUnit DLL，参考原项目测试 asmdef，避免勾选 Override References 后丢失测试依赖。

## 2. 建立观察数据，不污染战斗状态

在 Core 新建 `AiTrace.cs`。它只是带帧号的观察数据，不加入 WorldState 哈希。


```csharp
// 完整文件：Assets/Moba/Runtime/Core/AiTrace.cs
using System;
using System.Collections.Generic;

namespace Moba.Core
{
    public readonly struct AiNodeTrace
    {
        public readonly int NodeId;
        public readonly string Name, Phase;
        public AiNodeTrace(int nodeId, string name, string phase) { NodeId = nodeId; Name = name; Phase = phase; }
    }

    // Server observations only. These fields do not influence client simulation or state hashes.
    public sealed class AiTrace
    {
        public int BasedOnFrame { get; }
        public int CommandFrame { get; }
        public uint RobotId { get; }
        public string Decision { get; }
        public uint TargetId { get; }
        public bool Dead { get; }
        public bool Retreat { get; }
        public bool InRecoveryZone { get; }
        public bool SkillReady { get; }
        public bool EnemyInSkillRange { get; }
        public int Hp { get; }
        public IReadOnlyList<AiNodeTrace> Nodes { get; }
        public AiTrace(int basedOnFrame, int commandFrame, uint robotId, string decision, uint targetId,
            bool dead, bool retreat, bool inRecoveryZone, bool skillReady, bool enemyInSkillRange, int hp, AiNodeTrace[] nodes)
        {
            if (nodes == null || nodes.Length > 32) throw new ArgumentException("AI trace node limit.");
            BasedOnFrame = basedOnFrame; CommandFrame = commandFrame; RobotId = robotId; Decision = decision; TargetId = targetId;
            Dead = dead; Retreat = retreat; InRecoveryZone = inRecoveryZone; SkillReady = skillReady;
            EnemyInSkillRange = enemyInSkillRange; Hp = hp; Nodes = Array.AsReadOnly((AiNodeTrace[])nodes.Clone());
        }
    }
}
```


BasedOnFrame 表示决策读取哪一帧状态，CommandFrame 表示命令属于哪一帧。AI 在要生成第 n 帧命令时读 S(n-1)，不要先 Step 再让 AI 读取 S(n)。

## 3. 实现黑板适配

在 AI 新建 `BlackboardAdapter.cs`，按下面完整代码实现。先关注 Initialize、Prepare、Submit，最后补 Record/Snapshot。


```csharp
// 完整文件：Assets/Moba/Runtime/AI/BlackboardAdapter.cs
using System;
using System.Collections.Generic;
using Moba.Core;
using UnityEngine;

namespace Moba.AI
{
    public sealed class BlackboardAdapter : MonoBehaviour
    {
        public const int RetreatPercent = 30, ReturnPercent = 70, AcquireRadius = 6000, ChaseRadius = 8000;
        private readonly List<AiNodeTrace> nodes = new List<AiNodeTrace>(32);
        public CombatWorld World { get; private set; }
        public uint RobotId { get; private set; }
        public EntityState Actor => World.FindEntity(RobotId);
        public EntityState Target { get; private set; }
        public int CommandFrame { get; private set; }
        public bool RetreatLatched { get; private set; }
        public bool InRecoveryZone { get; private set; }
        public bool SkillReady { get; private set; }
        public bool EnemyInSkillRange { get; private set; }
        public bool HasCommand { get; private set; }
        public CombatCommand Command { get; private set; }
        public string Decision { get; set; } = "NotEvaluated";

        public void Initialize(CombatWorld world, uint robotId)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            if (robotId != 3 && robotId != 4) throw new ArgumentOutOfRangeException(nameof(robotId));
            RobotId = robotId;
        }

        public void Prepare(int frame)
        {
            if (World.State.Frame != frame - 1) throw new InvalidOperationException("AI must read S(n-1).");
            CommandFrame = frame; HasCommand = false; Command = default; nodes.Clear();
            var actor = Actor;
            if (!actor.Alive) RetreatLatched = false;
            else if ((long)actor.Hp * 100 <= (long)World.Config.HeroHp * RetreatPercent) RetreatLatched = true;
            else if ((long)actor.Hp * 100 >= (long)World.Config.HeroHp * ReturnPercent) RetreatLatched = false;
            InRecoveryZone = IntegerMath.WithinRange(actor.Position, World.SpawnFor(actor.Team), World.Config.Lane.RecoveryRadius);
            SkillReady = actor.Alive && frame >= actor.SkillReadyFrame;
            EnemyInSkillRange = TargetSelector.Find(World, actor, World.Config.SkillRadius, TargetPriority.Robot) != null;
            Target = TargetSelector.Find(World, actor, AcquireRadius, TargetPriority.Robot);
            var current = actor.Intent.Kind == CommandKind.Attack ? World.FindEntity(actor.Intent.TargetId) : null;
            if (actor.Alive && current != null && current.Alive && current.Team != actor.Team &&
                IntegerMath.WithinRange(actor.Position, current.Position, ChaseRadius) &&
                (Target == null || TargetSelector.Rank(current, TargetPriority.Robot) <= TargetSelector.Rank(Target, TargetPriority.Robot))) Target = current;
        }

        public void Submit(CombatCommand command)
        {
            if (HasCommand || !Actor.Alive || command.Kind == CommandKind.NoOp || command.Equals(Actor.Intent)) return;
            command.Validate(World.Config);
            Command = command; HasCommand = true;
        }

        public void Record(int nodeId, string name, string phase)
        {
            if (nodes.Count < 32) nodes.Add(new AiNodeTrace(nodeId, name, phase));
        }

        public AiTrace Snapshot() => new AiTrace(World.State.Frame, CommandFrame, RobotId, Decision, Target?.EntityId ?? 0,
            !Actor.Alive, RetreatLatched, InRecoveryZone, SkillReady, EnemyInSkillRange, Actor.Hp, nodes.ToArray());
    }
}
```


逐项理解阈值：

- HP ≤30% 时锁定撤退；达到 ≥70% 才退出撤退，中间区间保留原状态。
- 初次索敌半径 6000，持续追击边界 8000。
- 每次 Prepare 清空本次命令槽与观察节点，不清除 CombatWorld 里的持续意图。
- Submit 只接受本次第一条有效新命令，避免一个插件 Tick 中落入多个动作并覆盖命令。

撤退到出生区后用 MoveTo(当前位置) 停止旧移动意图。NoOp 不能用来取消旧意图，这是阶段 01 的约定。

## 4. 写条件节点与动作节点

创建以下两个**完整文件**。条件只返回 Success/Failure；持续动作返回 Running，让分支保持并等待条件中断。


```csharp
// 完整文件：Assets/Moba/Runtime/AI/HeroCondition.cs
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
```


```csharp
// 完整文件：Assets/Moba/Runtime/AI/HeroAction.cs
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
```


这里 `Action` 来自 BehaviorDesigner.Runtime.Tasks；若同文件还引入 System，注意与 System.Action 歧义，可使用完整类型名或别名。

## 5. 手动搭建行为树资产

1. 新建临时 GameObject，命名 ServerAiAgent，先设为 inactive。
2. 添加 BlackboardAdapter 和 BehaviorTree。
3. 打开已安装版本的 Behavior Designer 编辑窗口，在选中对象上建立树。菜单名称以当前插件为准，可在 Tools 中查找 Behavior Designer。
4. Root 添加 Selector，按以下从左到右优先级添加节点：
```text
Selector
├─ Sequence [Abort Type = Both]
│  ├─ HeroCondition: Dead
│  └─ HeroAction: HoldUntilAlive
├─ Sequence [Abort Type = Both]
│  ├─ HeroCondition: Retreat
│  └─ HeroAction: RetreatOrRecover
├─ Sequence [Abort Type = Both]
│  ├─ HeroCondition: EnemyInSkillRange
│  ├─ HeroCondition: SkillReady
│  └─ HeroAction: IssueAreaSkill
├─ Sequence [Abort Type = Both]
│  ├─ HeroCondition: HasTarget
│  └─ HeroAction: AttackOrChase
└─ HeroAction: MoveAlongLane
```
5. 给每个节点起清楚的 FriendlyName，如 Dead、Retreat、Area Skill、Combat、Advance。
6. 使用插件的 Export/保存为外部行为树功能，将树保存到 `Assets/Moba/AI/HeroDecision.asset`；回到组件，将 External Behavior 绑定到该资产。若菜单显示不同，目标类型应为 ExternalBehaviorTree。
7. BehaviorTree 关闭 Start When Enabled，打开 Restart When Complete，关闭 Asynchronous Load。
8. 保存 ServerAiAgent.prefab，删除临时场景对象，避免场景里多出一棵自动运行的树。

Both 同时允许本分支条件失效时中断，以及高优先级分支条件满足时抢占低优先级。不要只设置 Self 后期待低血撤退能从低优先级进攻分支抢占。

## 6. 手动 Tick 调度

创建 `ServerAiRunner.cs`：


```csharp
// 完整文件：Assets/Moba/Runtime/AI/ServerAiRunner.cs
using System;
using System.Collections.Generic;
using BehaviorDesigner.Runtime;

namespace Moba.AI
{
    /// <summary>Called by the server only, after the frame's required inputs have arrived.</summary>
    public sealed class ServerAiRunner
    {
        private readonly BehaviorManager manager;
        private readonly int interval;
        private int lastProcessedFrame;

        public int EvaluationCount { get; private set; }

        public ServerAiRunner(BehaviorManager manager, int interval = 4)
        {
            this.manager = manager != null ? manager : throw new ArgumentNullException(nameof(manager));
            if (interval < 1) throw new ArgumentOutOfRangeException(nameof(interval));
            this.interval = interval;
            manager.UpdateInterval = UpdateIntervalType.Manual;
            manager.ExecutionsPerTick = BehaviorManager.ExecutionsPerTickType.NoDuplicates;
        }

        public bool TryEvaluate(int frame, bool inputsReady, IReadOnlyList<BehaviorTree> orderedTrees)
        {
            if (frame < 1) throw new ArgumentOutOfRangeException(nameof(frame));
            if (frame < lastProcessedFrame) throw new InvalidOperationException("AI frame moved backwards.");
            if (orderedTrees == null) throw new ArgumentNullException(nameof(orderedTrees));
            if (!inputsReady || frame == lastProcessedFrame) return false;
            if (manager.UpdateInterval != UpdateIntervalType.Manual)
                throw new InvalidOperationException("Server behavior trees must use manual scheduling.");

            lastProcessedFrame = frame;
            if ((frame - 1) % interval != 0) return false;
            for (var i = 0; i < orderedTrees.Count; ++i)
            {
                var tree = orderedTrees[i];
                if (tree == null || !manager.IsBehaviorEnabled(tree))
                    throw new InvalidOperationException("A scheduled behavior tree is missing or disabled.");
                manager.Tick(tree);
            }
            EvaluationCount++;
            return true;
        }
    }
}
```


观察帧号序列应为 1、5、9、13……每四个逻辑帧一次。输入未收齐或重复请求同一帧，不应再次评估。手动更新需要调用 BehaviorManager.Tick；参见 [插件 Behavior Manager 说明](https://opsive.com/support/documentation/behavior-designer/behavior-manager/)；本教程的具体调用参数以本地 1.7.x 代码为准。

## 7. 管理两个机器人实例

创建 `ServerAiSystem.cs`：


```csharp
// 完整文件：Assets/Moba/Runtime/AI/ServerAiSystem.cs
using System;
using System.Collections.Generic;
using BehaviorDesigner.Runtime;
using Moba.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moba.AI
{
    // Constructed only by the server role. It emits commands; it never writes CombatWorld state.
    public sealed class ServerAiSystem : IDisposable
    {
        private readonly GameObject managerObject;
        private readonly GameObject[] agents = new GameObject[2];
        private readonly BlackboardAdapter[] boards = new BlackboardAdapter[2];
        private readonly BehaviorTree[] trees = new BehaviorTree[2];
        private int lastFrame;
        public ServerAiRunner Runner { get; }
        public IReadOnlyList<AiTrace> LatestTraces { get; private set; } = Array.Empty<AiTrace>();
        public event Action<IReadOnlyList<AiTrace>> Evaluated;

        public ServerAiSystem(CombatWorld world, GameObject agentPrefab)
        {
            if (world == null || agentPrefab == null) throw new ArgumentNullException();
            if (BehaviorManager.instance != null) throw new InvalidOperationException("A server must own exactly one BehaviorManager.");
            managerObject = new GameObject("ServerBehaviorManager");
            Runner = new ServerAiRunner(managerObject.AddComponent<BehaviorManager>());
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    agents[i] = Object.Instantiate(agentPrefab); agents[i].name = "ServerRobot_" + (i + 3);
                    agents[i].SetActive(false);
                    boards[i] = agents[i].GetComponent<BlackboardAdapter>(); trees[i] = agents[i].GetComponent<BehaviorTree>();
                    if (boards[i] == null || trees[i] == null || trees[i].ExternalBehavior == null) throw new InvalidOperationException("Incomplete server AI prefab.");
                    boards[i].Initialize(world, (uint)(i + 3));
                    trees[i].StartWhenEnabled = false; trees[i].RestartWhenComplete = true; trees[i].AsynchronousLoad = false;
                    agents[i].SetActive(true); trees[i].EnableBehavior();
                }
            }
            catch { Dispose(); throw; }
        }

        public ActorCommand[] Evaluate(int frame, bool inputsReady = true)
        {
            if (frame < 1) throw new ArgumentOutOfRangeException(nameof(frame));
            if (frame < lastFrame) throw new InvalidOperationException("AI frame moved backwards.");
            if (!inputsReady || frame == lastFrame) return Array.Empty<ActorCommand>();
            lastFrame = frame;
            if ((frame - 1) % 4 == 0) foreach (var board in boards) board.Prepare(frame);
            if (!Runner.TryEvaluate(frame, true, trees)) return Array.Empty<ActorCommand>();
            var commands = new List<ActorCommand>(2); var traces = new AiTrace[2];
            for (int i = 0; i < boards.Length; i++)
            {
                if (boards[i].HasCommand) commands.Add(new ActorCommand(boards[i].RobotId, boards[i].Command));
                traces[i] = boards[i].Snapshot();
            }
            LatestTraces = Array.AsReadOnly(traces); Evaluated?.Invoke(LatestTraces);
            return commands.ToArray();
        }

        public void Dispose()
        {
            for (int i = 0; i < trees.Length; i++)
            {
                if (trees[i] != null) trees[i].DisableBehavior(false);
                if (agents[i] != null) Object.Destroy(agents[i]);
            }
            if (managerObject != null) Object.Destroy(managerObject);
        }
    }
}
```


构造时明确禁止已有 BehaviorManager，防止误生成两套调度器。销毁使用 Unity Destroy，因此对象真正移除要到帧末；后续房间回收必须等这一点，不是在 Dispose 返回时立即断言场景没有对象。

## 8. 接入本地权威入口

对 ManualLocalBootstrap 做以下**成员/方法片段修改**：

- 增加 using Moba.AI、public GameObject ServerAiPrefab、private ServerAiSystem ai。
- 在 Start 创建 World 后构造 `ai = new ServerAiSystem(World, ServerAiPrefab);`。
- 替换原 Step 行：
```csharp
int frame = World.State.Frame + 1;
var robots = ai.Evaluate(frame);
World.Step(new FrameCommands(frame, Input.Sample(), CombatCommand.NoOp, robots));
```
- 在 OnDestroy 调用 `ai?.Dispose();`。
- Inspector 绑定 ServerAiAgent.prefab。本地版本是学习用权威驱动；阶段 08 正式客户端不会执行这一段。

顺序不能变成 Step 后 Evaluate。Prepare 内的帧校验会主动发现这种错误。

## 9. 分五组验证，不只看机器人会动

1. **调度**：在 Evaluate 返回成功处计数；200 逻辑帧应评估 50 次，不随渲染帧率变化。重复 Evaluate 同一帧不增加。
2. **撤退**：在 PlayMode 测试中通过友元把机器人 HP 设为 290，下一次评估应撤退；恢复至 500 仍撤退；达到 700 才允许重新进攻。
3. **抢占**：已经追击时降低 HP，观察旧动作 OnEnd、撤退动作 OnStart，不只看 Decision 字符串。
4. **技能**：敌人在范围内且冷却就绪时，优先于普通攻击；同一次评估只能提交一条指令。
5. **记录重演**：保存一段真实 AI 输出给另一个无树世界执行，两世界状态应一致。

可对照 [HeroAiTests](../../UnityMOBA/Assets/Moba/Tests/PlayMode/HeroAiTests.cs) 和 [插件烟雾测试](../../UnityMOBA/Assets/Moba/Tests/PlayMode/BehaviorDesignerSmokeTests.cs)。测试 TearDown 中 Dispose 后 `yield return null`，再开始下一例。

## 10. 常见问题与验收

| 问题 | 定位 |
| --- | --- |
| Actor 为 null | RobotId 必须为 3/4，Initialize 必须早于 EnableBehavior |
| 一局出现多个 BehaviorManager | Prefab/场景里有自动创建的管理器，或启动了两套 Bootstrap |
| AI 没有低血抢占 | Sequence 的 Abort Type 是否为 Both，黑板是否 Prepare |
| AI 每帧抖动改目标 | 缺少保留当前有效目标规则 |
| 等待时行为树仍前进 | UpdateInterval 不是 Manual，或在 MonoBehaviour.Update 直接 Tick |
| 客户端也创建 AI | 阶段 08 角色分支未限制，必须修正 |

- [ ] 树资产绑定和手动 Tick 已验证。
- [ ] 撤退迟滞、技能优先级、目标失效和死亡抢占通过。
- [ ] 节点只输出命令，未直接写血量或 Transform。
- [ ] AI 观察数据不进入战斗哈希。
