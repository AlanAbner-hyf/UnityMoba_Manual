using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BehaviorDesigner.Runtime;
using Moba.AI;
using Moba.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 使用手动制作的真实 Prefab 和外部树；不依赖阶段 06/09 的哈希、网络或回放代码。
public sealed class HeroAiStage05Tests
{
    private ServerAiSystem ai;
    private CombatWorld world;
    private GameObject prefab;
    private BehaviorTree blueTree;
    private AiTrace Blue => ai.LatestTraces.Single(t => t.RobotId == 3);

    [SetUp]
    public void SetUp()
    {
#if UNITY_EDITOR
        Assert.That(BehaviorManager.instance == null, Is.True,
            "请从 Test Runner 运行测试，不要同时运行已有的游戏/AI 场景。");
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Moba/Prefabs/ServerAiAgent.prefab");
        Assert.That(prefab, Is.Not.Null, "请先保存 ServerAiAgent.prefab。");
        Assert.That(prefab.activeSelf, Is.False, "Prefab 根对象应保持 inactive。");
        var tree = prefab.GetComponent<BehaviorTree>();
        Assert.That(tree, Is.Not.Null);
        Assert.That(tree.ExternalBehavior, Is.Not.Null, "请绑定 HeroDecision 外部树资产。");
        Assert.That(tree.StartWhenEnabled, Is.False);
        Assert.That(tree.RestartWhenComplete, Is.True);
        Assert.That(tree.AsynchronousLoad, Is.False);
        world = CombatWorld.Create(new BattleConfig(mode: Moba.Core.SimulationMode.CombatFixture));
        ai = new ServerAiSystem(world, prefab);
        blueTree = GameObject.Find("ServerRobot_3").GetComponent<BehaviorTree>();
        Assert.That(blueTree.FindTasks<HeroAction>().Count(t => t.Kind == HeroActionKind.MoveAlongLane),
            Is.EqualTo(1), "最右侧 Advance 节点的 Kind 必须是 MoveAlongLane，仅改名称不改变行为。");
#else
        Assert.Ignore("本测试通过 AssetDatabase 加载资产，请在 Unity Editor 的 PlayMode 中运行。");
#endif
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        ai?.Dispose();
        ai = null;
        // Destroy 在帧末完成；必须等待后再创建下一例的 BehaviorManager。
        yield return null;
    }

    private ActorCommand[] EvaluateAndStep()
    {
        int frame = world.State.Frame + 1;
        var commands = ai.Evaluate(frame);
        Assert.That(commands.GroupBy(c => c.ActorId).All(g => g.Count() <= 1), Is.True,
            "每次评估中，每个机器人最多输出一条命令。");
        world.Step(new FrameCommands(frame, default, default, commands));
        return commands;
    }

    // 推进到目标帧之前：例如 Before(5) 返回时 State.Frame == 4。
    private void Before(int frame)
    {
        while (world.State.Frame < frame - 1) EvaluateAndStep();
    }

    private int ActionId(HeroActionKind kind)
        => blueTree.FindTasks<HeroAction>().Single(t => t.Kind == kind).ID;

    private void AssertTransition(int nodeId, string phase)
        => Assert.That(Blue.Nodes.Any(n => n.NodeId == nodeId && n.Phase == phase), Is.True,
            $"第 {Blue.CommandFrame} 帧缺少节点 {nodeId} 的 {phase} 记录；检查 Sequence 的 Both 中断配置。");

    [UnityTest]
    public IEnumerator Scheduling_200FramesEvaluate50Times_AndWaitingDoesNotTick()
    {
        var board = blueTree.GetComponent<BlackboardAdapter>();
        int before = board.Snapshot().Nodes.Count;
        yield return new WaitForSecondsRealtime(0.12f);
        Assert.That(board.Snapshot().Nodes.Count, Is.EqualTo(before));
        Assert.That(ai.Runner.EvaluationCount, Is.Zero);
        Assert.That(ai.Evaluate(1, false), Is.Empty);
        Assert.That(ai.Runner.EvaluationCount, Is.Zero);

        var evaluatedFrames = new List<int>();
        ai.Evaluated += traces => evaluatedFrames.Add(traces[0].CommandFrame);
        EvaluateAndStep();
        Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(1));
        before = board.Snapshot().Nodes.Count;
        yield return new WaitForSecondsRealtime(0.12f);
        Assert.That(board.Snapshot().Nodes.Count, Is.EqualTo(before),
            "只经过渲染帧不应让动作继续执行；检查 BehaviorManager 是否为 Manual。");

        Assert.That(ai.Evaluate(1), Is.Empty);
        Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(1));
        for (int frame = 2; frame <= 200; frame++)
        {
            EvaluateAndStep();
            int count = ai.Runner.EvaluationCount;
            Assert.That(ai.Evaluate(frame), Is.Empty);
            Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(count));
            if (frame % 17 == 0) yield return null;
        }
        Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(50));
        CollectionAssert.AreEqual(Enumerable.Range(0, 50).Select(i => 1 + i * 4), evaluatedFrames);
    }

    [UnityTest]
    public IEnumerator Retreat_290Then500StaysRetreating_700Returns()
    {
        var bot = world.FindEntity(3);
        bot.Position = default;
        EvaluateAndStep();
        Assert.That(Blue.Decision, Is.EqualTo("Advance"));

        Before(5);
        bot.Hp = 290;
        EvaluateAndStep();
        Assert.That(Blue.Decision, Is.EqualTo("Retreat"));
        Assert.That(Blue.Retreat, Is.True);
        Assert.That(Blue.BasedOnFrame, Is.EqualTo(4));
        Assert.That(Blue.CommandFrame, Is.EqualTo(5));

        Before(9);
        bot.Hp = 500;
        EvaluateAndStep();
        Assert.That(Blue.Decision, Is.EqualTo("Retreat"));
        Assert.That(Blue.Retreat, Is.True);

        Before(13);
        bot.Hp = 699;
        bot.Position = new Int2(world.Config.BlueSpawn.X, 1000);
        EvaluateAndStep();
        Assert.That(Blue.Decision, Is.EqualTo("Recover"));
        Assert.That(Blue.Retreat, Is.True);

        Before(17);
        bot.Hp = 700;
        EvaluateAndStep();
        Assert.That(Blue.Retreat, Is.False);
        Assert.That(Blue.Decision, Is.EqualTo("Advance"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator LowHp_AbortsChase_WithActualExitAndEnter()
    {
        var bot = world.FindEntity(3);
        bot.Position = default;
        bot.SkillReadyFrame = 999; // 隔离技能分支，先确认追击确实运行。
        world.FindEntity(2).Position = new Int2(4000, 0);
        int combatId = ActionId(HeroActionKind.AttackOrChase);
        int retreatId = ActionId(HeroActionKind.RetreatOrRecover);
        EvaluateAndStep();
        Assert.That(Blue.Decision, Is.EqualTo("Chase"));
        Assert.That(blueTree.GetActiveTasks().Any(t => t.ID == combatId), Is.True);

        Before(5);
        bot.Hp = 290;
        var commands = EvaluateAndStep();
        Assert.That(Blue.Decision, Is.EqualTo("Retreat"));
        AssertTransition(combatId, "Exit");
        AssertTransition(retreatId, "Enter");
        Assert.That(blueTree.GetActiveTasks().Any(t => t.ID == retreatId), Is.True);
        var order = commands.Single(c => c.ActorId == 3).Command;
        Assert.That(order.Kind, Is.EqualTo(CommandKind.MoveTo));
        Assert.That(order.Point, Is.EqualTo(world.Config.BlueSpawn));
        yield return null;
    }

    [UnityTest]
    public IEnumerator Skill_PrecedesAttack_AndSubmitsOneCommandPerRobot()
    {
        var bot = world.FindEntity(3);
        bot.Position = default;
        world.FindEntity(2).Position = new Int2(1200, 0);
        var commands = EvaluateAndStep();
        Assert.That(Blue.Decision, Is.EqualTo("CastArea"));
        Assert.That(commands.Count(c => c.ActorId == 3), Is.EqualTo(1));
        Assert.That(commands.Single(c => c.ActorId == 3).Command.Kind, Is.EqualTo(CommandKind.CastArea));
        Assert.That(world.FindEntity(2).Hp, Is.EqualTo(880));

        Before(5);
        commands = EvaluateAndStep();
        Assert.That(Blue.Decision, Is.EqualTo("Attack"));
        Assert.That(commands.Single(c => c.ActorId == 3).Command.Kind, Is.EqualTo(CommandKind.Attack));
        int hitFrame = bot.HitFrame;
        Before(9);
        commands = EvaluateAndStep();
        Assert.That(commands.Any(c => c.ActorId == 3), Is.False,
            "同一攻击意图不必重复提交；继续由 Core 执行原意图。");
        Assert.That(bot.HitFrame, Is.EqualTo(hitFrame), "重复评估不应重启前摇。");
        yield return null;
    }

    [UnityTest]
    public IEnumerator RecordedCommands_ReplayFrameByFrame_WithoutAnyAiTree()
    {
        ai.Dispose();
        ai = null;
        yield return null;
        var config = BattleConfig.CreateDemo();
        world = CombatWorld.Create(config, BattleConfig.DefaultSeed);
        ai = new ServerAiSystem(world, prefab);
        var recording = new List<FrameCommands>();
        var expected = new List<object[]>();
        int commandCount = 0;
        for (int frame = 1; frame <= 600 && world.State.Result == MatchResult.None; frame++)
        {
            var robots = ai.Evaluate(frame);
            commandCount += robots.Length;
            var commands = new FrameCommands(frame, default, default, robots);
            recording.Add(commands);
            world.Step(commands);
            expected.Add(Capture(world));
            if (frame % 100 == 0) yield return null;
        }
        Assert.That(recording.Count, Is.EqualTo(600));
        Assert.That(commandCount, Is.GreaterThan(0), "必须录到真实 AI 命令，不能只重演空输入。");
        ai.Dispose();
        ai = null;
        yield return null;
        Assert.That(BehaviorManager.instance == null, Is.True);
        Assert.That(Object.FindObjectsByType<BlackboardAdapter>(FindObjectsSortMode.None), Is.Empty);

        var replay = CombatWorld.Create(config, BattleConfig.DefaultSeed);
        for (int i = 0; i < recording.Count; i++)
        {
            replay.Step(recording[i]); // 这里没有 Evaluate，也没有新建行为树。
            CollectionAssert.AreEqual(expected[i], Capture(replay), $"重演第 {i + 1} 帧状态不同。");
        }
    }

    // 快照复制值，不能保存 EntityState 引用，否则之后的帧会覆盖“历史状态”。
    // 当前阶段尚无 StateHasher，逐项比较所有已实现的世界/实体状态。
    private static object[] Capture(CombatWorld value)
    {
        var s = value.State;
        var result = new List<object>
        {
            s.Frame, s.NextEntityId, s.RandomState, s.Result,
            s.NextWaveFrame, s.WaveNumber, s.Entities.Count
        };
        foreach (var e in s.Entities)
        {
            result.AddRange(new object[]
            {
                e.EntityId, e.Kind, e.Team, e.Position, e.Hp, e.State, e.Intent,
                e.AttackReadyFrame, e.HitFrame, e.SkillReadyFrame, e.RespawnFrame,
                e.LastDamagedFrame, e.PathIndex
            });
        }
        return result.ToArray();
    }
}
