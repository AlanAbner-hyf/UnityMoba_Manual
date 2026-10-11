using Moba.Core;
using NUnit.Framework;

public sealed class CombatTests
{
    [Test]
    public void MovingCancelsWindupButKeepsCooldown()
    {
        var world = CombatWorld.Create(new BattleConfig(mode: SimulationMode.CombatFixture));
        var attacker = world.FindEntity(1);
        var target = world.FindEntity(2);
        attacker.Position = new Int2(0, 0);
        target.Position = new Int2(1000, 0);
        world.Step(new FrameCommands(1,
            new CombatCommand(CommandKind.Attack, default, 2), CombatCommand.NoOp));
        Assert.That(attacker.State, Is.EqualTo(FsmState.AttackWindup));
        Assert.That(attacker.AttackReadyFrame, Is.EqualTo(21));
        world.Step(new FrameCommands(2,
            CombatCommand.MoveTo(new Int2(-5000, 0)), CombatCommand.NoOp));
        for (int f = 3; f <= 6; f++)
            world.Step(new FrameCommands(f, CombatCommand.NoOp, CombatCommand.NoOp));
        Assert.That(target.Hp, Is.EqualTo(1000));
        Assert.That(attacker.AttackReadyFrame, Is.EqualTo(21));
        Assert.That(attacker.HitFrame, Is.Zero);
    }
}