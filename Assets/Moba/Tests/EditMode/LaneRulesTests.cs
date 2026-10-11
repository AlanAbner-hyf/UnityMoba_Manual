using System;
using System.Linq;
using Moba.Core;
using NUnit.Framework;

public sealed class LaneRulesTests
{
    private static CombatWorld CreateMatch(int waveInterval = 300)
        => CombatWorld.Create(new BattleConfig(mode: SimulationMode.Match,
            lane: new LaneConfig(waveInterval: waveInterval)));

    private static CombatCommand Attack(uint targetId)
        => new CombatCommand(CommandKind.Attack, default, targetId);

    private static void Step(CombatWorld world,
        CombatCommand blue = default, CombatCommand red = default)
        => world.Step(new FrameCommands(world.State.Frame + 1, blue, red));

    private static void RunTo(CombatWorld world, int frame)
    {
        while (world.State.Frame < frame) Step(world);
    }

    private static int LivingMinions(CombatWorld world, Team team)
        => world.State.Entities.Count(e =>
            e.Kind == EntityKind.Minion && e.Team == team && e.Alive);

    private static SimEvent[] SpawnEvents(CombatWorld world)
        => world.Events.Where(e => e.Kind == SimEventKind.Spawned).ToArray();

    // 默认配置：第一帧每队出 3 个兵，下一波是第 301 帧。
    [Test]
    public void FirstFrameSpawnsThreeMinionsPerTeamWithDefaultConfig()
    {
        var world = CreateMatch();
        Assert.That(world.State.NextWaveFrame, Is.EqualTo(1));
        Step(world);

        Assert.That(LivingMinions(world, Team.Blue), Is.EqualTo(3));
        Assert.That(LivingMinions(world, Team.Red), Is.EqualTo(3));
        Assert.That(world.State.Entities.Count, Is.EqualTo(12));
        Assert.That(world.State.WaveNumber, Is.EqualTo(1));
        Assert.That(world.State.NextWaveFrame, Is.EqualTo(301));
        Assert.That(SpawnEvents(world).Length, Is.EqualTo(6));
    }

    // 同时覆盖测试用短间隔和正式默认间隔，避免只测到其中一种配置。
    [TestCase(10)]
    [TestCase(300)]
    public void NextWaveSpawnsOnlyOnItsScheduledFrame(int interval)
    {
        var world = CreateMatch(interval);
        Step(world);
        uint nextId = world.State.NextEntityId;

        while (world.State.Frame < interval)
        {
            Step(world);
            Assert.That(SpawnEvents(world), Is.Empty,
                $"Unexpected spawn on frame {world.State.Frame}");
        }
        Assert.That(world.State.WaveNumber, Is.EqualTo(1));
        Assert.That(world.State.NextWaveFrame, Is.EqualTo(interval + 1));
        Assert.That(world.State.NextEntityId, Is.EqualTo(nextId));

        Step(world);
        var spawned = SpawnEvents(world);
        Assert.That(world.State.Frame, Is.EqualTo(interval + 1));
        Assert.That(world.State.WaveNumber, Is.EqualTo(2));
        Assert.That(world.State.NextWaveFrame, Is.EqualTo(interval * 2 + 1));
        Assert.That(world.State.NextEntityId, Is.EqualTo(nextId + 6));
        Assert.That(spawned.Length, Is.EqualTo(6));
        foreach (Team team in new[] { Team.Blue, Team.Red })
            Assert.That(spawned.Count(e => world.FindEntity(e.SourceId).Team == team),
                Is.EqualTo(3));
    }

    // 10 帧一波：双方尚未接敌便在第 71 帧达到上限，无需提高血量。
    [Test]
    public void MinionCapStopsSpawningAndOnlyReplacesVacantSlots()
    {
        var world = CreateMatch(10);
        RunTo(world, 71);
        Assert.That(LivingMinions(world, Team.Blue), Is.EqualTo(24));
        Assert.That(LivingMinions(world, Team.Red), Is.EqualTo(24));
        uint nextId = world.State.NextEntityId;

        RunTo(world, 81);
        Assert.That(LivingMinions(world, Team.Blue), Is.EqualTo(24));
        Assert.That(LivingMinions(world, Team.Red), Is.EqualTo(24));
        Assert.That(SpawnEvents(world), Is.Empty);
        Assert.That(world.State.NextEntityId, Is.EqualTo(nextId));
        Assert.That(world.State.NextWaveFrame, Is.EqualTo(91));

        // 测试夹具释放一个蓝方名额，下一波只能补一个，不能再补三个。
        var removed = world.State.Entities.First(e =>
            e.Kind == EntityKind.Minion && e.Team == Team.Blue && e.Alive);
        removed.Hp = 0;
        removed.State = FsmState.Dead;
        RunTo(world, 90);
        Assert.That(world.FindEntity(removed.EntityId), Is.Null);
        Assert.That(LivingMinions(world, Team.Blue), Is.EqualTo(23));
        Step(world);

        Assert.That(LivingMinions(world, Team.Blue), Is.EqualTo(24));
        Assert.That(LivingMinions(world, Team.Red), Is.EqualTo(24));
        Assert.That(SpawnEvents(world).Length, Is.EqualTo(1));
        Assert.That(world.FindEntity(SpawnEvents(world)[0].SourceId).Team,
            Is.EqualTo(Team.Blue));
        Assert.That(world.State.NextEntityId, Is.EqualTo(nextId + 1));
    }

    [Test]
    public void TowerPrefersEnemyMinionOverCloserEnemyHero()
    {
        var world = CreateMatch();
        Step(world);
        var tower = world.FindEntity(5);
        var enemyHero = world.FindEntity(2);
        var enemyMinion = world.State.Entities.First(e =>
            e.Kind == EntityKind.Minion && e.Team == Team.Red);
        enemyHero.Position = new Int2(tower.Position.X + 1000, 0);
        enemyMinion.Position = new Int2(tower.Position.X + 3000, 0);

        Step(world);

        Assert.That(tower.Intent.Kind, Is.EqualTo(CommandKind.Attack));
        Assert.That(tower.Intent.TargetId, Is.EqualTo(enemyMinion.EntityId));
        Assert.That(tower.State, Is.EqualTo(FsmState.AttackWindup));
    }

    [Test]
    public void SpawnRecoveryWaits59FramesAndStartsOnFrame60AfterDamage()
    {
        var world = CreateMatch();
        var blue = world.FindEntity(1);
        var red = world.FindEntity(2);
        red.Position = new Int2(blue.Position.X + 1000, blue.Position.Z);
        Step(world, CombatCommand.NoOp, Attack(1));
        RunTo(world, 6);
        Assert.That(blue.Hp, Is.EqualTo(940));
        int damagedFrame = blue.LastDamagedFrame;
        Assert.That(damagedFrame, Is.EqualTo(world.State.Frame));

        // 攻击真实触发受伤时间；随后把攻击者移走，隔离回复边界。
        red.Position = world.Config.RedSpawn;
        red.Intent = CombatCommand.NoOp;
        red.State = FsmState.Idle;
        red.HitFrame = 0;
        while (world.State.Frame < damagedFrame + 59)
        {
            Step(world);
            Assert.That(blue.Hp, Is.EqualTo(940));
        }

        Step(world);
        Assert.That(world.State.Frame, Is.EqualTo(damagedFrame + 60));
        Assert.That(blue.Hp, Is.EqualTo(942));
        Assert.That(world.Events.Any(e => e.Kind == SimEventKind.Healed &&
            e.TargetId == blue.EntityId && e.Amount == 2), Is.True);
    }

    // Match 在塔倒下后已结束，不能继续 Step；用战斗夹具单独推进复活等待期。
    // Match 的摧塔结算和结束后拒绝推进由下面的测试另行覆盖。
    [Test]
    public void DestroyedTowerNeverRespawnsInCombatFixture()
    {
        var world = CombatWorld.Create(new BattleConfig(mode: SimulationMode.CombatFixture));
        var hero = world.FindEntity(1);
        var tower = world.FindEntity(6);
        hero.Position = new Int2(tower.Position.X + 1000, tower.Position.Z);
        tower.Hp = world.Config.HeroDamage;
        Step(world, Attack(6));
        RunTo(world, 6);
        Assert.That(tower.State, Is.EqualTo(FsmState.Dead));
        Assert.That(tower.RespawnFrame, Is.Zero);
        int endFrame = world.State.Frame + world.Config.RespawnDelay + 1;

        while (world.State.Frame < endFrame)
        {
            Step(world);
            Assert.That(tower.Hp, Is.Zero);
            Assert.That(tower.State, Is.EqualTo(FsmState.Dead));
            Assert.That(world.FindEntity(6), Is.SameAs(tower));
            Assert.That(world.Events.Any(e => e.Kind == SimEventKind.Respawned &&
                e.SourceId == tower.EntityId), Is.False);
        }
    }

    [Test]
    public void BothTowersKilledByAttacksOnSameFrameProduceDraw()
    {
        var world = CreateMatch();
        world.FindEntity(5).Hp = world.Config.HeroDamage;
        world.FindEntity(6).Hp = world.Config.HeroDamage;
        world.FindEntity(1).Position = new Int2(world.Config.RedTower.X + 1000, 0);
        world.FindEntity(2).Position = new Int2(world.Config.BlueTower.X + 1000, 0);
        Step(world, Attack(6), Attack(5));
        RunTo(world, 5);
        Assert.That(world.State.Result, Is.EqualTo(MatchResult.None));
        Assert.That(world.FindEntity(5).Alive && world.FindEntity(6).Alive, Is.True);

        Step(world);
        Assert.That(world.FindEntity(5).Hp, Is.Zero);
        Assert.That(world.FindEntity(6).Hp, Is.Zero);
        Assert.That(world.State.Result, Is.EqualTo(MatchResult.Draw));
        CollectionAssert.AreEquivalent(new uint[] { 5, 6 },
            world.Events.Where(e => e.Kind == SimEventKind.Died).Select(e => e.TargetId));
    }

    [TestCase(Team.Blue)]
    [TestCase(Team.Red)]
    public void VictoryRejectsFurtherStepWithoutChangingState(Team winner)
    {
        var world = CreateMatch();
        uint heroId = winner == Team.Blue ? 1u : 2u;
        uint towerId = winner == Team.Blue ? 6u : 5u;
        var tower = world.FindEntity(towerId);
        tower.Hp = world.Config.HeroDamage;
        world.FindEntity(heroId).Position = new Int2(tower.Position.X + 1000, 0);
        Step(world, winner == Team.Blue ? Attack(towerId) : CombatCommand.NoOp,
            winner == Team.Red ? Attack(towerId) : CombatCommand.NoOp);
        RunTo(world, 6);
        var expected = winner == Team.Blue ? MatchResult.BlueWins : MatchResult.RedWins;
        Assert.That(world.State.Result, Is.EqualTo(expected));
        Assert.That(tower.State, Is.EqualTo(FsmState.Dead));
        Assert.That(tower.RespawnFrame, Is.Zero);
        int frame = world.State.Frame;
        var positions = world.State.Entities.Select(e => e.Position).ToArray();
        var hp = world.State.Entities.Select(e => e.Hp).ToArray();
        int nextWave = world.State.NextWaveFrame;

        Assert.Throws<InvalidOperationException>(() => Step(world));
        Assert.That(world.State.Frame, Is.EqualTo(frame));
        Assert.That(world.State.Result, Is.EqualTo(expected));
        Assert.That(world.State.NextWaveFrame, Is.EqualTo(nextWave));
        CollectionAssert.AreEqual(positions, world.State.Entities.Select(e => e.Position));
        CollectionAssert.AreEqual(hp, world.State.Entities.Select(e => e.Hp));
    }

    // 完整局不改血量、不瞬移、不缩短波次；蓝方持续提交攻击红塔的正常命令。
    [Test]
    public void DefaultConfigCompletesFullMatchThroughNormalCommands()
    {
        var world = CombatWorld.Create(BattleConfig.CreateDemo());
        Assert.That(world.Config.Lane.WaveInterval, Is.EqualTo(300));
        Assert.That(world.FindEntity(6).Hp, Is.EqualTo(6000));
        int towerDamage = 0;
        bool towerDied = false;
        const int frameLimit = 36000;
        while (world.State.Result == MatchResult.None && world.State.Frame < frameLimit)
        {
            Step(world, Attack(6));
            foreach (var e in world.Events)
            {
                if (e.Kind == SimEventKind.Damage && e.TargetId == 6) towerDamage += e.Amount;
                if (e.Kind == SimEventKind.Died && e.TargetId == 6) towerDied = true;
            }
        }

        Assert.That(world.State.Result, Is.EqualTo(MatchResult.BlueWins),
            $"No blue victory by frame {world.State.Frame}; reaching the limit is not success.");
        Assert.That(world.State.WaveNumber, Is.GreaterThan(1));
        Assert.That(world.FindEntity(5).Alive, Is.True);
        Assert.That(world.FindEntity(6).Hp, Is.Zero);
        Assert.That(towerDamage, Is.EqualTo(world.Config.TowerHp));
        Assert.That(towerDied, Is.True);
        TestContext.WriteLine($"Default match: frame={world.State.Frame}, result={world.State.Result}, " +
            $"waves={world.State.WaveNumber}, blueTowerHp={world.FindEntity(5).Hp}, redTowerHp={world.FindEntity(6).Hp}");
    }
}
