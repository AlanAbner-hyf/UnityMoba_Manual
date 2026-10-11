using Moba.Core;
using Moba.Networking;
using NUnit.Framework;

public sealed class StateHashTests
{
    [Test]
    public void ThreeIndependentWorldsAgreeFor6000Frames()
    {
        var config = new BattleConfig();
        var server = CombatWorld.Create(config);
        var first = CombatWorld.Create(config);
        var second = CombatWorld.Create(config);
        var inputs = new InputWindow(config);
        for (int f = 1; f <= 6000; f++)
        {
            var command = f % 200 == 1
                ? CombatCommand.MoveTo(new Int2(f % 400 == 1 ? -30000 : 30000, 2000))
                : CombatCommand.NoOp;
            inputs.Add(0, f, server.State.Frame, command);
            Assert.That(inputs.TryTake(f, out _), Is.False);
            inputs.Add(1, f, server.State.Frame, CombatCommand.NoOp);
            Assert.That(inputs.TryTake(f, out var frame), Is.True);
            server.Step(frame);
            first.Step(frame);
            second.Step(frame);
            var expected = StateHasher.Compute(server.State);
            CollectionAssert.AreEqual(expected, StateHasher.Compute(first.State), "first frame " + f);
            CollectionAssert.AreEqual(expected, StateHasher.Compute(second.State), "second frame " + f);
        }
    }

    [Test]
    public void FutureCooldownChangesHash()
    {
        var a = CombatWorld.Create(new BattleConfig());
        var b = CombatWorld.Create(new BattleConfig());
        b.FindEntity(1).SkillReadyFrame = 100;
        Assert.That(StateHasher.Equal(
            StateHasher.Compute(a.State), StateHasher.Compute(b.State)), Is.False);
    }
}