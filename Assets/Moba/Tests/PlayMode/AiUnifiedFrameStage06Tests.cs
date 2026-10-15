using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BehaviorDesigner.Runtime;
using Moba.AI;
using Moba.Core;
using Moba.Tests.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 将真实 ServerAiSystem 接到统一帧夹具，两个客户端不创建任何行为树。
public sealed class AiUnifiedFrameStage06Tests
{
    private ServerAiSystem ai;
    private UnifiedFrameHarness run;
    private readonly List<int> evaluatedFrames = new List<int>();

    [SetUp]
    public void SetUp()
    {
#if UNITY_EDITOR
        Assert.That(BehaviorManager.instance == null, Is.True,
            "请从 Test Runner 运行测试，不要同时运行游戏中的 ServerAiSystem。");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Moba/Prefabs/ServerAiAgent.prefab");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.activeSelf, Is.False);
        var tree = prefab.GetComponent<BehaviorTree>();
        Assert.That(tree, Is.Not.Null);
        Assert.That(tree.ExternalBehavior, Is.Not.Null);
        Assert.That(tree.StartWhenEnabled, Is.False);
        Assert.That(tree.AsynchronousLoad, Is.False);
        Assert.That(tree.RestartWhenComplete, Is.True);

        // MovementFixture 拒绝攻击/技能；CombatFixture 支持真实 AI 且不会因塔死亡提前结算。
        run = new UnifiedFrameHarness(new BattleConfig(mode: Moba.Core.SimulationMode.CombatFixture),
            frame => ai.Evaluate(frame));
        ai = new ServerAiSystem(run.Server, prefab);
        evaluatedFrames.Clear();
        ai.Evaluated += traces =>
        {
            Assert.That(traces.Count, Is.EqualTo(2));
            foreach (var trace in traces)
            {
                Assert.That(trace.BasedOnFrame, Is.EqualTo(run.Server.State.Frame));
                Assert.That(trace.CommandFrame, Is.EqualTo(run.Server.State.Frame + 1));
                Assert.That(run.FirstClient.State.Frame, Is.EqualTo(run.Server.State.Frame));
                Assert.That(run.SecondClient.State.Frame, Is.EqualTo(run.Server.State.Frame));
            }
            evaluatedFrames.Add(traces[0].CommandFrame);
        };
#else
        Assert.Ignore("真实 AI 资产测试需在 Unity Editor 的 PlayMode Test Runner 中运行。");
#endif
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        ai?.Dispose();
        ai = null;
        run = null;
        yield return null; // 等待 Destroy 完成，再允许下一例创建 BehaviorManager。
    }

    [UnityTest]
    public IEnumerator MissingInputDoesNotEvaluateRealAi_ResumeDoesNotCatchUp()
    {
        SubmitBoth(1);
        Assert.That(run.TryAdvance(0.049), Is.False);
        Assert.That(ai.Runner.EvaluationCount, Is.Zero);
        for (int frame = 1; frame <= 4; frame++)
        {
            if (frame != 1) SubmitBoth(frame);
            Assert.That(run.TryAdvance(run.NextDue), Is.True);
        }
        Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(1));

        // 第 5 帧本来应该 Tick 行为树，但红方输入缺失，等待期间一次也不能 Tick。
        run.Submit(0, 5, CombatCommand.NoOp);
        byte[] before = StateHasher.Compute(run.Server.State);
        Assert.That(run.TryAdvance(run.NextDue), Is.False);
        Assert.That(run.TryAdvance(2.0), Is.False);
        yield return null;
        Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(1));
        Assert.That(run.WaitingForInputs, Is.True);
        Assert.That(run.History.Count, Is.EqualTo(4));
        foreach (var world in new[] { run.Server, run.FirstClient, run.SecondClient })
        {
            Assert.That(world.State.Frame, Is.EqualTo(4));
            CollectionAssert.AreEqual(before, StateHasher.Compute(world.State));
        }

        run.Submit(1, 5, CombatCommand.NoOp);
        Assert.That(run.TryAdvance(2.0), Is.True);
        Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(2));
        CollectionAssert.AreEqual(new[] { 1, 5 }, evaluatedFrames);
        Assert.That(run.NextDue, Is.EqualTo(2.05).Within(1e-10));
        SubmitBoth(6);
        Assert.That(run.TryAdvance(2.0), Is.False);
        Assert.That(run.TryAdvance(2.049), Is.False);
        Assert.That(run.Server.State.Frame, Is.EqualTo(5));
        Assert.That(run.TryAdvance(run.NextDue), Is.True);
        Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator RealAiUnifiedFramesAgreeFor6000Frames_ClientsReplayWithoutTrees()
    {
        Assert.That(run.Server, Is.Not.SameAs(run.FirstClient));
        Assert.That(run.Server, Is.Not.SameAs(run.SecondClient));
        Assert.That(run.FirstClient, Is.Not.SameAs(run.SecondClient));
        var boards = Object.FindObjectsByType<BlackboardAdapter>(FindObjectsSortMode.None);
        Assert.That(boards.Length, Is.EqualTo(2), "整个夹具只能有服务端的两个 AI 代理。");
        Assert.That(boards.All(board => ReferenceEquals(board.World, run.Server)), Is.True);

        int robotCommandCount = 0;
        int combatCommandCount = 0;
        for (int frame = 1; frame <= 6000; frame++)
        {
            var player = frame % 200 == 1
                ? CombatCommand.MoveTo(new Int2(frame % 400 == 1 ? -10000 : 10000, 2000))
                : CombatCommand.NoOp;
            run.Submit(0, frame, player);
            // 故意在部分帧延迟另一方，覆盖三世界长跑中的等待恢复。
            if (frame % 401 == 1)
            {
                int count = ai.Runner.EvaluationCount;
                Assert.That(run.TryAdvance(run.NextDue), Is.False);
                Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(count));
                Assert.That(run.Server.State.Frame, Is.EqualTo(frame - 1));
            }
            run.Submit(1, frame, CombatCommand.NoOp);
            Assert.That(run.TryAdvance(run.NextDue), Is.True);
            var evidence = run.History[frame - 1];
            Assert.That(evidence.Commands.FrameId, Is.EqualTo(frame));
            Assert.That(evidence.Matches, Is.True, "frame " + frame);
            CollectionAssert.AreEqual(evidence.CanonicalBytes[0], evidence.CanonicalBytes[1], "first frame " + frame);
            CollectionAssert.AreEqual(evidence.CanonicalBytes[0], evidence.CanonicalBytes[2], "second frame " + frame);
            robotCommandCount += evidence.Commands.Robots.Count;
            combatCommandCount += evidence.Commands.Robots.Count(command =>
                command.Command.Kind == CommandKind.Attack || command.Command.Kind == CommandKind.CastArea);
            if ((frame - 1) % 4 != 0) Assert.That(evidence.Commands.Robots, Is.Empty);
            if (frame % 100 == 0) yield return null;
        }
        Assert.That(run.History.Count, Is.EqualTo(6000));
        Assert.That(robotCommandCount, Is.GreaterThan(0), "不能用全空机器人输入冒充真实 AI 测试。");
        Assert.That(combatCommandCount, Is.GreaterThan(0), "必须覆盖真实 AI 的攻击/技能输出。");
        Assert.That(ai.Runner.EvaluationCount, Is.EqualTo(1500));
        CollectionAssert.AreEqual(Enumerable.Range(0, 1500).Select(i => 1 + i * 4), evaluatedFrames);

        // 再销毁唯一服务端 AI，证明保存的统一帧足以从零重放，不依赖任何行为树。
        ai.Dispose();
        ai = null;
        yield return null;
        Assert.That(BehaviorManager.instance == null, Is.True);
        Assert.That(Object.FindObjectsByType<BlackboardAdapter>(FindObjectsSortMode.None), Is.Empty);
        var replay = CombatWorld.Create(run.Server.Config);
        foreach (var evidence in run.History)
        {
            replay.Step(evidence.Commands);
            CollectionAssert.AreEqual(evidence.Hashes[0], StateHasher.Compute(replay.State),
                "tree-free replay frame " + evidence.Commands.FrameId);
        }
    }

    private void SubmitBoth(int frame)
    {
        run.Submit(0, frame, CombatCommand.NoOp);
        run.Submit(1, frame, CombatCommand.NoOp);
    }
}
