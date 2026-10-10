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

    [Test]
    public void IndependentWorldsMatchPositionsOnEveryFrameFor600Frames()
    {
        var first = CombatWorld.Create(new BattleConfig(), BattleConfig.DefaultSeed);
        var second = CombatWorld.Create(new BattleConfig(), BattleConfig.DefaultSeed);
        Assert.That(second.State, Is.Not.SameAs(first.State));
        foreach (var entity in first.State.Entities)
            Assert.That(second.FindEntity(entity.EntityId), Is.Not.SameAs(entity));

        // Change direction periodically; NoOp frames must continue the existing movement.
        var targets = new[]
        {
            new Int2(-39000, -9000), new Int2(39000, 9000),
            new Int2(0, 0), new Int2(12345, -6789),
            new Int2(-12345, 6789), new Int2(40000, -10000)
        };
        for (int frame = 1; frame <= 600; frame++)
        {
            var blue = CombatCommand.NoOp;
            var red = CombatCommand.NoOp;
            if ((frame - 1) % 60 == 0)
            {
                var target = targets[((frame - 1) / 60) % targets.Length];
                blue = CombatCommand.MoveTo(target);
                red = CombatCommand.MoveTo(new Int2(-target.X, -target.Z));
            }

            // Separate frame objects ensure the worlds share no mutable input arrays.
            first.Step(new FrameCommands(frame, blue, red,
                new ActorCommand(4, red), new ActorCommand(3, blue)));
            second.Step(new FrameCommands(frame, blue, red,
                new ActorCommand(4, red), new ActorCommand(3, blue)));

            Assert.That(first.State.Frame, Is.EqualTo(frame));
            Assert.That(second.State.Frame, Is.EqualTo(frame));
            Assert.That(second.State.Entities.Count, Is.EqualTo(first.State.Entities.Count));
            foreach (var entity in first.State.Entities)
            {
                var other = second.FindEntity(entity.EntityId);
                Assert.That(other, Is.Not.Null, $"Frame {frame}, entity {entity.EntityId}");
                Assert.That(other.Position, Is.EqualTo(entity.Position),
                    $"Position mismatch at frame {frame}, entity {entity.EntityId}");
            }

            if (frame == 1)
                Assert.That(first.FindEntity(1).Position, Is.Not.EqualTo(first.Config.BlueSpawn),
                    "The sequence must actually move units, not just compare idle worlds.");
        }
    }

    [TestCase(-40001, 0)]
    [TestCase(40001, 0)]
    [TestCase(0, -10001)]
    [TestCase(0, 10001)]
    public void OutOfMapMoveIsRejectedWithoutChangingState(int x, int z)
    {
        var world = CombatWorld.Create(new BattleConfig());
        world.Step(new FrameCommands(1,
            CombatCommand.MoveTo(new Int2(-30000, 0)),
            CombatCommand.MoveTo(new Int2(30000, 0))));

        var positions = new Int2[world.State.Entities.Count];
        var intents = new CombatCommand[positions.Length];
        var states = new FsmState[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = world.State.Entities[i].Position;
            intents[i] = world.State.Entities[i].Intent;
            states[i] = world.State.Entities[i].State;
        }

        // A valid blue command precedes the invalid red command: neither may be applied.
        Assert.Throws<ArgumentException>(() => world.Step(new FrameCommands(2,
            CombatCommand.MoveTo(new Int2(-20000, 1000)),
            CombatCommand.MoveTo(new Int2(x, z)))));

        Assert.That(world.State.Frame, Is.EqualTo(1));
        Assert.That(world.State.Entities.Count, Is.EqualTo(positions.Length));
        for (int i = 0; i < positions.Length; i++)
        {
            Assert.That(world.State.Entities[i].Position, Is.EqualTo(positions[i]));
            Assert.That(world.State.Entities[i].Intent, Is.EqualTo(intents[i]));
            Assert.That(world.State.Entities[i].State, Is.EqualTo(states[i]));
        }
    }

    [Test]
    public void ArrivalTransitionsOnTheExactFrameAndStaysIdleNextFrame()
    {
        var world = CombatWorld.Create(new BattleConfig());
        var hero = world.FindEntity(1);
        var start = hero.Position;
        int speed = world.Config.HeroSpeed;
        // Arrive on frame 3 with a short final step, exercising target clamping.
        var destination = new Int2(start.X + speed * 2 + speed / 2, start.Z);
        var move = CombatCommand.MoveTo(destination);

        world.Step(new FrameCommands(1, move, CombatCommand.NoOp));
        world.Step(new FrameCommands(2, CombatCommand.NoOp, CombatCommand.NoOp));
        Assert.That(world.State.Frame, Is.EqualTo(2));
        Assert.That(hero.Position, Is.EqualTo(new Int2(start.X + speed * 2, start.Z)));
        Assert.That(hero.Position, Is.Not.EqualTo(destination));
        Assert.That(hero.State, Is.EqualTo(FsmState.Moving));
        Assert.That(hero.Intent, Is.EqualTo(move));

        world.Step(new FrameCommands(3, CombatCommand.NoOp, CombatCommand.NoOp));
        Assert.That(world.State.Frame, Is.EqualTo(3));
        Assert.That(hero.Position, Is.EqualTo(destination));
        Assert.That(hero.State, Is.EqualTo(FsmState.Idle));
        Assert.That(hero.Intent, Is.EqualTo(CombatCommand.NoOp));

        world.Step(new FrameCommands(4, CombatCommand.NoOp, CombatCommand.NoOp));
        Assert.That(world.State.Frame, Is.EqualTo(4));
        Assert.That(hero.Position, Is.EqualTo(destination));
        Assert.That(hero.State, Is.EqualTo(FsmState.Idle));
        Assert.That(hero.Intent, Is.EqualTo(CombatCommand.NoOp));
    }

    [TestCase(3)]
    [TestCase(4)]
    public void DuplicateRobotIdIsRejected(int robotId)
    {
        Assert.Throws<ArgumentException>(() => new FrameCommands(1,
            CombatCommand.NoOp, CombatCommand.NoOp,
            new ActorCommand((uint)robotId, CombatCommand.MoveTo(new Int2(0, 0))),
            new ActorCommand((uint)robotId, CombatCommand.NoOp)));
    }
}
