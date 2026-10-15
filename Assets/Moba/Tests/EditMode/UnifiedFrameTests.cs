using System;
using Moba.Core;
using Moba.Tests.Fixtures;
using NUnit.Framework;

public sealed class UnifiedFrameTests
{
    [Test]
    public void BeforeDeadlineDoesNotConsumeInputsOrEvaluateAi()
    {
        int calls = 0;
        var run = new UnifiedFrameHarness(new BattleConfig(), frame => { calls++; return Array.Empty<ActorCommand>(); });
        SubmitBoth(run, 1);
        Assert.That(run.TryAdvance(0.049), Is.False);
        Assert.That(run.Inputs.Has(0, 1) && run.Inputs.Has(1, 1), Is.True);
        Assert.That(calls, Is.Zero);
        Assert.That(run.Server.State.Frame, Is.Zero);
        Assert.That(run.TryAdvance(0.05), Is.True);
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void MissingEitherPlayerFreezesAllWorldsAndAiWithoutReplacingInput(int presentSlot)
    {
        int calls = 0;
        var move = CombatCommand.MoveTo(new Int2(1000, 2000));
        var run = new UnifiedFrameHarness(new BattleConfig(), frame => { calls++; return Array.Empty<ActorCommand>(); });
        byte[] before = StateHasher.Compute(run.Server.State);
        run.Submit(presentSlot, 1, move);
        foreach (double now in new[] { 0.05, 0.5, 1.0, 2.0 }) Assert.That(run.TryAdvance(now), Is.False);
        Assert.That(run.WaitingForInputs, Is.True);
        Assert.That(calls, Is.Zero);
        Assert.That(run.History, Is.Empty);
        Assert.That(run.Inputs.Has(presentSlot, 1), Is.True);
        foreach (var world in new[] { run.Server, run.FirstClient, run.SecondClient })
        {
            Assert.That(world.State.Frame, Is.Zero);
            CollectionAssert.AreEqual(before, StateHasher.Compute(world.State));
        }
        run.Submit(1 - presentSlot, 1, CombatCommand.NoOp);
        Assert.That(run.TryAdvance(2), Is.True);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(presentSlot == 0 ? run.History[0].Commands.BluePlayer : run.History[0].Commands.RedPlayer,
            Is.EqualTo(move));
        Assert.That(run.WaitingForInputs, Is.False);
    }

    [Test]
    public void ResumeResetsDeadlineInsteadOfExecutingWallClockDebt()
    {
        int calls = 0;
        var run = new UnifiedFrameHarness(new BattleConfig(), frame => { calls++; return Array.Empty<ActorCommand>(); });
        Assert.That(run.TryAdvance(0.05), Is.False);
        for (int frame = 1; frame <= 3; frame++) SubmitBoth(run, frame);
        Assert.That(run.TryAdvance(2), Is.True);
        Assert.That(run.NextDue, Is.EqualTo(2.05).Within(1e-10));
        Assert.That(run.TryAdvance(2), Is.False);
        Assert.That(run.TryAdvance(2.049), Is.False);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(run.TryAdvance(run.NextDue), Is.True);
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(run.Server.State.Frame, Is.EqualTo(2));
    }

    [Test]
    public void ScriptedRobotOutputReplaysInThreeIndependentWorldsFor6000Frames()
    {
        int calls = 0;
        UnifiedFrameHarness run = null;
        run = new UnifiedFrameHarness(new BattleConfig(), frame =>
        {
            calls++;
            Assert.That(run.Server.State.Frame, Is.EqualTo(frame - 1));
            Assert.That(run.FirstClient.State.Frame, Is.EqualTo(frame - 1));
            Assert.That(run.SecondClient.State.Frame, Is.EqualTo(frame - 1));
            // EditMode 使用确定的替身输出验证组帧，真实行为树由 PlayMode 验证。
            if (frame % 100 != 1) return Array.Empty<ActorCommand>();
            int x = frame % 200 == 1 ? 10000 : -10000;
            return new[] { new ActorCommand(4, CombatCommand.MoveTo(new Int2(-x, -1000))),
                new ActorCommand(3, CombatCommand.MoveTo(new Int2(x, 1000))) };
        });
        Assert.That(run.Server, Is.Not.SameAs(run.FirstClient));
        Assert.That(run.FirstClient, Is.Not.SameAs(run.SecondClient));
        Assert.That(run.Server.FindEntity(3), Is.Not.SameAs(run.FirstClient.FindEntity(3)));
        for (int frame = 1; frame <= 6000; frame++)
        {
            SubmitBoth(run, frame);
            Assert.That(run.TryAdvance(run.NextDue), Is.True);
            var evidence = run.History[frame - 1];
            Assert.That(evidence.Commands.FrameId, Is.EqualTo(frame));
            Assert.That(evidence.Matches, Is.True, "frame " + frame);
            CollectionAssert.AreEqual(evidence.CanonicalBytes[0], evidence.CanonicalBytes[1]);
            CollectionAssert.AreEqual(evidence.CanonicalBytes[0], evidence.CanonicalBytes[2]);
        }
        Assert.That(calls, Is.EqualTo(6000));
        Assert.That(run.History.Count, Is.EqualTo(6000));
        Assert.That(run.History[0].Commands.Robots[0].ActorId, Is.EqualTo(3));
        Assert.That(run.History[0].Commands.Robots[1].ActorId, Is.EqualTo(4));
        Assert.That(run.History[1].Commands.Robots, Is.Empty);
        Assert.That(run.Server.FindEntity(3).Position, Is.Not.EqualTo(run.Server.Config.BlueSpawn));
        CollectionAssert.AreEqual(run.History[0].Hashes[0], StateHasher.Hash(run.History[0].CanonicalBytes[0]));
    }

    [Test]
    public void MismatchPreservesEvidenceAndStopsFurtherAdvance()
    {
        var run = new UnifiedFrameHarness(new BattleConfig(), frame => Array.Empty<ActorCommand>());
        // 使用现有 Core 测试友元权限，注入肉眼看不出的冷却差异。
        run.FirstClient.FindEntity(1).SkillReadyFrame = 100;
        SubmitBoth(run, 1);
        StringAssert.Contains("State mismatch at frame 1", Assert.Throws<InvalidOperationException>(
            () => run.TryAdvance(run.NextDue)).Message);
        Assert.That(run.History.Count, Is.EqualTo(1));
        Assert.That(run.History[0].Matches, Is.False);
        Assert.That(run.History[0].Commands.FrameId, Is.EqualTo(1));
        Assert.That(run.Failure, Is.Not.Null);
        Assert.Throws<InvalidOperationException>(() => run.TryAdvance(1));
    }

    private static void SubmitBoth(UnifiedFrameHarness run, int frame)
    {
        run.Submit(0, frame, CombatCommand.NoOp);
        run.Submit(1, frame, CombatCommand.NoOp);
    }
}
