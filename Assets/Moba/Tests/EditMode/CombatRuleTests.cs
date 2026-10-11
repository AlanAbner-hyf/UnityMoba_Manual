using System;
using Moba.Core;
using NUnit.Framework;

public sealed class CombatRuleTests
{
    // 创建一个独立的测试世界，让两名玩家英雄相距 1000。
    private static CombatWorld CreateDuel(
        out EntityState blue, out EntityState red)
    {
        var world = CombatWorld.Create(
            new BattleConfig(mode: SimulationMode.CombatFixture));

        blue = world.FindEntity(1);
        red = world.FindEntity(2);

        blue.Position = new Int2(0, 0);
        red.Position = new Int2(1000, 0);

        return world;
    }

    // 创建普攻命令。
    private static CombatCommand Attack(uint targetId)
        => new CombatCommand(CommandKind.Attack, default, targetId);

    // 推进一帧；不传命令时，两边都是 NoOp。
    private static void Step(
        CombatWorld world,
        CombatCommand blue = default,
        CombatCommand red = default)
        => world.Step(new FrameCommands(
            world.State.Frame + 1, blue, red));

    // 连续提交 NoOp，推进到指定帧。
    private static void RunTo(CombatWorld world, int frame)
    {
        while (world.State.Frame < frame)
            Step(world);
    }

    // 检查当前帧是否产生指定来源的事件。
    private static bool HasEvent(
        CombatWorld world,
        SimEventKind kind,
        uint sourceId)
    {
        foreach (var e in world.Events)
        {
            if (e.Kind == kind && e.SourceId == sourceId)
                return true;
        }

        return false;
    }

    // 通过真实攻击制造死亡，供后面的测试复用。
    private static CombatWorld CreateDeadRedWorld(
        out EntityState blue, out EntityState red)
    {
        var world = CreateDuel(out blue, out red);
        red.Hp = 60;

        // 双方在第 1 帧同时开始普攻。
        Step(world, Attack(2), Attack(1));

        Assert.That(red.State, Is.EqualTo(FsmState.AttackWindup));

        RunTo(world, 6);
        Assert.That(red.Alive, Is.False);

        return world;
    }

    // 1. 正常命中
    [Test]
    public void BasicAttackHitsOnFrame6()
    {
        var world = CreateDuel(out var blue, out var red);

        Step(world, Attack(2));
        Assert.That(blue.HitFrame, Is.EqualTo(6));

        // 命中前一帧不能提前扣血。
        RunTo(world, 5);
        Assert.That(red.Hp, Is.EqualTo(1000));

        // 第 6 帧命中，扣除 60 HP。
        Step(world);
        Assert.That(world.State.Frame, Is.EqualTo(6));
        Assert.That(red.Hp, Is.EqualTo(940));
        Assert.That(
            HasEvent(world, SimEventKind.Damage, 1),
            Is.True);
    }

    // 2. 命中前出范围
    [Test]
    public void LeavingRangeBeforeHitPreventsDamage()
    {
        var world = CreateDuel(out var blue, out var red);

        Step(world, Attack(2));
        RunTo(world, 4);

        // 第 5 帧开始前，把目标放到攻击范围外。
        red.Position = new Int2(10000, 0);

        RunTo(world, 6);

        Assert.That(red.Hp, Is.EqualTo(1000));
        Assert.That(blue.HitFrame, Is.Zero);
        Assert.That(blue.AttackReadyFrame, Is.EqualTo(21));
        Assert.That(blue.State, Is.EqualTo(FsmState.Moving));
    }

    // 3. Q 冷却
    [Test]
    public void SkillCooldownRejectsSecondCast()
    {
        var world = CreateDuel(out var blue, out var red);
        var cast = new CombatCommand(CommandKind.CastArea);

        // 第 1 帧释放成功。
        Step(world, cast);

        Assert.That(red.Hp, Is.EqualTo(880));
        Assert.That(blue.SkillReadyFrame, Is.EqualTo(161));
        Assert.That(
            HasEvent(world, SimEventKind.SkillCast, 1),
            Is.True);

        // 第 2 帧再次释放，应被冷却阻止。
        Step(world, cast);

        Assert.That(red.Hp, Is.EqualTo(880));
        Assert.That(blue.SkillReadyFrame, Is.EqualTo(161));
        Assert.That(
            HasEvent(world, SimEventKind.SkillCast, 1),
            Is.False);
        Assert.That(
            HasEvent(world, SimEventKind.Damage, 1),
            Is.False);
    }

    // 4. 死亡抢占
    [Test]
    public void DeathClearsIntentAndPreventsRetaliation()
    {
        var world = CreateDeadRedWorld(out var blue, out var red);

        Assert.That(red.Hp, Is.Zero);
        Assert.That(red.State, Is.EqualTo(FsmState.Dead));
        Assert.That(red.Intent, Is.EqualTo(CombatCommand.NoOp));
        Assert.That(red.HitFrame, Is.Zero);

        // 红方已死亡，其同帧待结算攻击不能再伤害蓝方。
        Assert.That(blue.Hp, Is.EqualTo(1000));
        Assert.That(
            HasEvent(world, SimEventKind.Died, 1),
            Is.True);
        Assert.That(
            HasEvent(world, SimEventKind.Damage, 2),
            Is.False);
    }

    // 5. 死亡拒绝技能
    [Test]
    public void DeadHeroCannotCastSkill()
    {
        var world = CreateDeadRedWorld(out var blue, out var red);
        int oldCooldown = red.SkillReadyFrame;

        // 死亡的是红方，所以技能命令放在红方输入槽。
        Step(
            world,
            CombatCommand.NoOp,
            new CombatCommand(CommandKind.CastArea));

        Assert.That(red.State, Is.EqualTo(FsmState.Dead));
        Assert.That(red.Intent, Is.EqualTo(CombatCommand.NoOp));
        Assert.That(red.SkillReadyFrame, Is.EqualTo(oldCooldown));
        Assert.That(blue.Hp, Is.EqualTo(1000));
        Assert.That(
            HasEvent(world, SimEventKind.SkillCast, 2),
            Is.False);
    }

    // 6. 复活
    [Test]
    public void HeroRespawnsExactly100FramesAfterDeath()
    {
        var world = CreateDeadRedWorld(out var blue, out var red);
        int deathFrame = world.State.Frame;

        Assert.That(
            red.RespawnFrame,
            Is.EqualTo(deathFrame + 100));

        // 复活前一帧，仍然死亡。
        RunTo(world, deathFrame + 99);

        Assert.That(red.Alive, Is.False);
        Assert.That(red.State, Is.EqualTo(FsmState.Dead));

        // 再推进一帧，应该满血回到出生点。
        Step(world);

        Assert.That(
            world.State.Frame,
            Is.EqualTo(deathFrame + 100));
        Assert.That(red.Alive, Is.True);
        Assert.That(red.Hp, Is.EqualTo(world.Config.HeroHp));
        Assert.That(red.Position, Is.EqualTo(world.Config.RedSpawn));
        Assert.That(red.State, Is.EqualTo(FsmState.Idle));
        Assert.That(red.Intent, Is.EqualTo(CombatCommand.NoOp));
        Assert.That(
            HasEvent(world, SimEventKind.Respawned, 2),
            Is.True);
    }

    // 7. 原子拒绝
    [Test]
    public void InvalidRedCommandRejectsWholeFrame()
    {
        var world = CreateDuel(out var blue, out var red);

        // 先让两名英雄都进入移动状态。
        Step(
            world,
            CombatCommand.MoveTo(new Int2(-5000, 0)),
            CombatCommand.MoveTo(new Int2(5000, 0)));

        var bluePosition = blue.Position;
        var redPosition = red.Position;
        var blueIntent = blue.Intent;
        var redIntent = red.Intent;
        var blueState = blue.State;
        var redState = red.State;
        int previousFrame = world.State.Frame;

        // 蓝方命令合法，红方目标超过地图最大 X。
        Assert.Throws<ArgumentException>(() => Step(
            world,
            CombatCommand.MoveTo(new Int2(0, 5000)),
            CombatCommand.MoveTo(
                new Int2(world.Config.MaxX + 1, 0))));

        // 整帧被拒绝：帧号、位置、意图和状态都不能改变。
        Assert.That(world.State.Frame, Is.EqualTo(previousFrame));
        Assert.That(blue.Position, Is.EqualTo(bluePosition));
        Assert.That(red.Position, Is.EqualTo(redPosition));
        Assert.That(blue.Intent, Is.EqualTo(blueIntent));
        Assert.That(red.Intent, Is.EqualTo(redIntent));
        Assert.That(blue.State, Is.EqualTo(blueState));
        Assert.That(red.State, Is.EqualTo(redState));
    }
}