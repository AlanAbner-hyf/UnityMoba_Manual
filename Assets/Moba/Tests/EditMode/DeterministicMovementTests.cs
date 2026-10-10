using System;
using Moba.Core;
using NUnit.Framework;

public sealed class DeterministicMovementTests
{
    [Test]
    public void NoOpKeepsIntentAndArrivalStops()
    {
        var world = CombatWorld.Create(new BattleConfig());
        var destination = new Int2(-33000, 0);
        for (int f = 1; f <= 20; f++)
            world.Step(new FrameCommands(f,
                f == 1 ? CombatCommand.MoveTo(destination) : CombatCommand.NoOp,
                CombatCommand.NoOp));
        Assert.That(world.FindEntity(1).Position, Is.EqualTo(destination));
        Assert.That(world.FindEntity(1).State, Is.EqualTo(FsmState.Idle));
        Assert.That(world.State.Frame, Is.EqualTo(20));
    }

    [Test]
    public void InvalidFrameDoesNotChangeState()
    {
        var world = CombatWorld.Create(new BattleConfig());
        var before = world.FindEntity(1).Position;
        Assert.Throws<ArgumentException>(() => world.Step(
            new FrameCommands(2, CombatCommand.NoOp, CombatCommand.NoOp)));
        Assert.That(world.State.Frame, Is.Zero);
        Assert.That(world.FindEntity(1).Position, Is.EqualTo(before));
    }

    [Test]
    public void DiagonalStepDoesNotExceedSpeed()
    {
        var from = new Int2(0, 0);
        var to = IntegerMath.MoveTowards(from, new Int2(-999, 777), 200);
        Assert.That(IntegerMath.DistanceSquared(from, to), Is.LessThanOrEqualTo(40000));
        Assert.That(to.X, Is.LessThan(0));
        Assert.That(to.Z, Is.GreaterThan(0));
    }
}