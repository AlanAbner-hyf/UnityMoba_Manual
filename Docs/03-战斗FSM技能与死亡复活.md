# 03　战斗FSM技能与死亡复活

[上一阶段](02-场景单位显示与鼠标输入.md) · [返回实现大纲](手动实现大纲.md) · [下一阶段](04-兵线防御塔与胜负.md)

适用：Unity 6000.3.23f1 / UnityMOBA_Manual。本文是后续手动实施教程；本次未修改运行时代码，也未在模板中执行这些步骤。

## 本阶段要得到什么

右键敌人能够追击并普攻，右键地面取消前摇，Q 造成范围伤害，死亡英雄停止行动并在 100 帧后复活。首先用测试观察规则，再用场景观察表现。

前置条件：阶段 02 可移动。本章提供移除兵线/碰撞依赖后的完整教学战斗版本；后面阶段 04 将继续扩展它。

## 1. 固定这一阶段的战斗参数

| 参数 | 默认值 | 含义 |
| --- | --- | --- |
| 英雄生命 | 1000 | 逻辑 HP |
| 普攻伤害 / 范围 | 60 / 1800 | 中心距离，暂未加入碰撞半径 |
| 普攻前摇 / 冷却 | 5 / 20 帧 | 0.25 秒 / 1 秒 |
| Q 半径 / 伤害 / 冷却 | 3000 / 120 / 160 帧 | 3 单位 / 120 / 8 秒 |
| 复活等待 | 100 帧 | 5 秒 |

伤害发生在逻辑帧，不由动画事件触发。攻击开始帧为 1 时，HitFrame=6；在第 6 帧到来前先接受新的移动指令，可以取消这次攻击。

将 `BattleConfig.cs` **完整替换**为：

```csharp
using System;

namespace Moba.Core
{
    public enum SimulationMode : byte { MovementFixture = 0, CombatFixture = 1, Match = 2 }

    public sealed class BattleConfig
    {
        public const string RulesVersion = "manual-combat-1";
        public const uint DefaultSeed = 0x6D2B79F5;
        public const int LogicHz = 20;
        public readonly int ConfigVersion;
        public readonly int MinX, MaxX, MinZ, MaxZ;
        public readonly Int2 BlueSpawn, RedSpawn, BlueTower, RedTower;
        public readonly int HeroSpeed, HeroHp, TowerHp;
        public readonly SimulationMode Mode;
        public readonly int HeroDamage, HeroRange, HeroWindup, HeroCooldown;
        public readonly int SkillRadius, SkillDamage, SkillCooldown;
        public readonly int RespawnDelay;

        public BattleConfig(int configVersion = 2, int minX = -40000, int maxX = 40000,
            int minZ = -10000, int maxZ = 10000, int heroSpeed = 200, int heroHp = 1000, int towerHp = 6000,
            SimulationMode mode = SimulationMode.MovementFixture,
            int heroDamage = 60, int heroRange = 1800, int heroWindup = 5, int heroCooldown = 20,
            int skillRadius = 3000, int skillDamage = 120, int skillCooldown = 160, int respawnDelay = 100)
        {
            if (configVersion < 1 || minX < -40000 || maxX > 40000 || minZ < -10000 || maxZ > 10000 ||
                minX > -35000 || maxX < 35000 || minZ > 0 || maxZ < 0 ||
                heroSpeed < 2 || heroSpeed > 1000 || heroHp < 1 || heroHp > 100000 || towerHp < 1 || towerHp > 100000)
                throw new ArgumentException("Invalid map or unit configuration.");
            if (mode > SimulationMode.Match || heroDamage < 1 || heroDamage > 100000 ||
                heroRange < 1 || heroRange > 80000 || heroWindup < 1 || heroWindup > 1200 ||
                heroCooldown <= heroWindup || heroCooldown > 12000 || skillRadius < 1 || skillRadius > 80000 ||
                skillDamage < 1 || skillDamage > 100000 || skillCooldown < 1 || skillCooldown > 12000 ||
                respawnDelay < 1 || respawnDelay > 12000)
                throw new ArgumentException("Invalid combat configuration.");
            ConfigVersion = configVersion;
            MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ;
            BlueSpawn = new Int2(-35000, 0); RedSpawn = new Int2(35000, 0);
            BlueTower = new Int2(-25000, 0); RedTower = new Int2(25000, 0);
            HeroSpeed = heroSpeed; HeroHp = heroHp; TowerHp = towerHp;
            Mode = mode;
            HeroDamage = heroDamage; HeroRange = heroRange; HeroWindup = heroWindup; HeroCooldown = heroCooldown;
            SkillRadius = skillRadius; SkillDamage = skillDamage; SkillCooldown = skillCooldown; RespawnDelay = respawnDelay;
        }

        public static BattleConfig CreateDemo() => new BattleConfig(mode: SimulationMode.Match);

        public bool Contains(Int2 point) => point.X >= MinX && point.X <= MaxX && point.Z >= MinZ && point.Z <= MaxZ;
    }
}
```


在场景的配置资产中把 Mode 改为 CombatFixture，并勾选 PlayerCommandInput.EnableCombat。不要把 MovementFixture 的不支持战斗异常当作新代码错误。

## 2. 添加 SimEvent 和待结算伤害

创建以下**完整文件**。事件只向外报告结果，任何观察者都不能通过事件回调再改战斗状态。


```csharp
// 完整文件：Assets/Moba/Runtime/Core/SimEvent.cs
namespace Moba.Core
{
    public enum SimEventKind : byte { StateChanged, AttackStarted, SkillCast, Damage, Died, Spawned, Respawned, Healed }
    internal enum DamageKind : byte { BasicAttack = 0, AreaSkill = 1 }

    // Read-only output for presentation and diagnostics; never fed back into the simulation.
    public readonly struct SimEvent
    {
        public readonly int Frame, Amount;
        public readonly SimEventKind Kind;
        public readonly uint SourceId, TargetId;
        public readonly FsmState PreviousState, NextState;
        public SimEvent(int frame, SimEventKind kind, uint sourceId, uint targetId = 0, int amount = 0,
            FsmState previousState = FsmState.Idle, FsmState nextState = FsmState.Idle)
        {
            Frame = frame; Kind = kind; SourceId = sourceId; TargetId = targetId; Amount = amount;
            PreviousState = previousState; NextState = nextState;
        }
    }

    internal readonly struct PendingDamage
    {
        public readonly uint SourceId, TargetId;
        public readonly DamageKind Kind;
        public PendingDamage(uint source, uint target, DamageKind kind) { SourceId = source; TargetId = target; Kind = kind; }
    }
}
```


PendingDamage 是本帧临时数据；EntityState 的意图、HitFrame 和冷却是跨帧数据。阶段 06 哈希覆盖跨帧数据，不把显示事件当作新的权威输入。

## 3. 实现四个状态

先读下面完整文件，再按顺序敲入：

1. ICombatState 定义 Enter、Tick、Exit。
2. CombatFsm.Transition 统一处理退出、状态修改和进入。
3. LivingState 在 Movement 阶段执行意图，在 Actions 阶段尝试攻击。
4. AttackWindupState 在进入时记录命中帧和下一可攻击帧；退出时只清 HitFrame。
5. DeadState 清除意图，为英雄设置复活帧；死亡期间 Tick 不执行行为。


```csharp
// 完整文件：Assets/Moba/Runtime/Core/CombatFsm.cs
namespace Moba.Core
{
    internal enum SimulationPhase { Movement, Actions }

    internal interface ICombatState
    {
        void Enter(CombatWorld world, EntityState entity);
        void Tick(CombatWorld world, EntityState entity, SimulationPhase phase);
        void Exit(CombatWorld world, EntityState entity);
    }

    // States are stateless singletons. Every timer and persistent intent lives in the hashed EntityState.
    internal static class CombatFsm
    {
        private static readonly ICombatState[] States = { new IdleState(), new MovingState(), new AttackWindupState(), new DeadState() };

        public static void Transition(CombatWorld world, EntityState entity, FsmState next)
        {
            if (entity.State == next) return;
            FsmState previous = entity.State;
            States[(int)previous].Exit(world, entity);
            entity.State = next;
            States[(int)next].Enter(world, entity);
            world.Emit(new SimEvent(world.CurrentFrame, SimEventKind.StateChanged, entity.EntityId, 0, 0, previous, next));
        }

        public static void Tick(CombatWorld world, EntityState entity, SimulationPhase phase)
            => States[(int)entity.State].Tick(world, entity, phase);
    }

    internal abstract class LivingState : ICombatState
    {
        public virtual void Enter(CombatWorld world, EntityState entity) { }
        public virtual void Exit(CombatWorld world, EntityState entity) { }
        public virtual void Tick(CombatWorld world, EntityState entity, SimulationPhase phase)
        {
            if (phase == SimulationPhase.Movement) world.AdvanceIntent(entity);
            else world.TryStartAttack(entity);
        }
    }

    internal sealed class IdleState : LivingState { }
    internal sealed class MovingState : LivingState { }

    internal sealed class AttackWindupState : LivingState
    {
        public override void Enter(CombatWorld world, EntityState entity)
        {
            entity.HitFrame = checked(world.CurrentFrame + world.AttackWindup(entity));
            entity.AttackReadyFrame = checked(world.CurrentFrame + world.AttackCooldown(entity));
            world.Emit(new SimEvent(world.CurrentFrame, SimEventKind.AttackStarted, entity.EntityId, entity.Intent.TargetId));
        }

        public override void Exit(CombatWorld world, EntityState entity)
        {
            entity.HitFrame = 0; // Leaving windup never refunds AttackReadyFrame.
        }

        public override void Tick(CombatWorld world, EntityState entity, SimulationPhase phase)
        {
            if (phase == SimulationPhase.Movement) world.AdvanceIntent(entity);
            else world.ResolveWindup(entity);
        }
    }

    internal sealed class DeadState : ICombatState
    {
        public void Enter(CombatWorld world, EntityState entity)
        {
            entity.Intent = CombatCommand.NoOp; entity.HitFrame = 0;
            entity.RespawnFrame = entity.IsHero ? checked(world.CurrentFrame + world.Config.RespawnDelay) : 0;
        }
        public void Tick(CombatWorld world, EntityState entity, SimulationPhase phase) { }
        public void Exit(CombatWorld world, EntityState entity) { }
    }
}
```


状态对象是无状态单例，不能把某个英雄剩余冷却放在状态类字段里，否则所有英雄会共享冷却。

## 4. 替换 CombatWorld，并逐段理解调用顺序

以下是本阶段的**完整替换文件**。保持阶段 01 的 EntityState/WorldState，不加入 Motion、Lane 或 Collision 类。

```csharp
using System;
using System.Collections.Generic;

namespace Moba.Core
{
    public sealed partial class CombatWorld
    {
        public BattleConfig Config { get; }
        public WorldState State { get; }
        public IReadOnlyList<SimEvent> Events { get; }
        private readonly List<EntityState> entities;
        private readonly List<SimEvent> events = new List<SimEvent>();
        private readonly List<PendingDamage> damage = new List<PendingDamage>();
        private readonly List<uint> areaCasts = new List<uint>();
        internal int CurrentFrame { get; private set; }

        private CombatWorld(BattleConfig config, uint seed)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            entities = new List<EntityState>(54);
            Events = events.AsReadOnly();
            for (uint id = 1; id <= 6; id++)
            {
                bool blue = (id & 1) == 1, tower = id >= 5;
                entities.Add(new EntityState
                {
                    EntityId = id, Kind = tower ? EntityKind.Tower : id >= 3 ? EntityKind.RobotHero : EntityKind.PlayerHero,
                    Team = blue ? Team.Blue : Team.Red,
                    Position = tower ? (blue ? config.BlueTower : config.RedTower) : (blue ? config.BlueSpawn : config.RedSpawn),
                    Hp = tower ? config.TowerHp : config.HeroHp
                });
            }
            State = new WorldState(entities) { NextEntityId = 7, RandomState = seed };
        }

        public static CombatWorld Create(BattleConfig config, uint seed = BattleConfig.DefaultSeed) => new CombatWorld(config, seed);

        public void Step(FrameCommands commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (State.Frame == int.MaxValue || commands.FrameId != State.Frame + 1) throw new ArgumentException("Non-contiguous frame.");
            // Validate every command before changing any state, so a rejected bundle is atomic.
            Validate(commands.BluePlayer); Validate(commands.RedPlayer);
            foreach (var robot in commands.Robots) Validate(robot.Command);
            if (State.Result != MatchResult.None) throw new InvalidOperationException("Match already ended.");
            CurrentFrame = commands.FrameId;
            events.Clear(); damage.Clear(); areaCasts.Clear();
            BeginCombatFrame();
            Apply(State.Entities[0], commands.BluePlayer); Apply(State.Entities[1], commands.RedPlayer);
            foreach (var robot in commands.Robots) Apply(State.Entities[(int)robot.ActorId - 1], robot.Command);
            foreach (var entity in entities) CombatFsm.Tick(this, entity, SimulationPhase.Movement);
            foreach (var entity in entities) CombatFsm.Tick(this, entity, SimulationPhase.Actions);
            foreach (uint source in areaCasts)
                foreach (var target in entities)
                    damage.Add(new PendingDamage(source, target.EntityId, DamageKind.AreaSkill));
            ResolveDamage();
            State.Frame = commands.FrameId;
        }

        private void Validate(CombatCommand command)
        {
            command.Validate(Config);
            if (Config.Mode == SimulationMode.MovementFixture && (command.Kind == CommandKind.Attack || command.Kind == CommandKind.CastArea))
                throw new NotSupportedException("The movement fixture does not accept combat commands.");
        }

        private void Apply(EntityState entity, CombatCommand command)
        {
            if (command.Kind == CommandKind.NoOp || !entity.Alive) return;
            if (command.Kind == CommandKind.CastArea)
            {
                if (!entity.IsHero || CurrentFrame < entity.SkillReadyFrame) return;
                entity.SkillReadyFrame = checked(CurrentFrame + Config.SkillCooldown);
                entity.Intent = CombatCommand.NoOp;
                CombatFsm.Transition(this, entity, FsmState.Idle);
                areaCasts.Add(entity.EntityId);
                Emit(new SimEvent(CurrentFrame, SimEventKind.SkillCast, entity.EntityId));
                return;
            }
            if (command.Kind == CommandKind.Attack)
            {
                if (!IsEnemy(entity, FindEntity(command.TargetId))) return;
                if (entity.Intent.Kind == CommandKind.Attack && entity.Intent.TargetId == command.TargetId) return;
            }
            entity.Intent = command;
            CombatFsm.Transition(this, entity, command.Kind == CommandKind.MoveTo ? FsmState.Moving : FsmState.Idle);
        }

        public EntityState FindEntity(uint id)
        {
            // Bounded to 54 entities; explicit traversal avoids unordered collection behaviour.
            foreach (var entity in entities) if (entity.EntityId == id) return entity;
            return null;
        }

        internal void Emit(SimEvent value) => events.Add(value);
        internal int AttackWindup(EntityState entity) => Config.HeroWindup;
        internal int AttackCooldown(EntityState entity) => Config.HeroCooldown;
        private int AttackRange(EntityState entity) => Config.HeroRange;
        private int AttackDamage(EntityState entity) => Config.HeroDamage;
        private int MoveSpeed(EntityState entity) => entity.Kind == EntityKind.Tower ? 0 : Config.HeroSpeed;
        private static bool IsEnemy(EntityState source, EntityState target)
            => source != null && source.Alive && target != null && target.Alive && source.Team != target.Team;

        internal void AdvanceIntent(EntityState entity)
        {
            if (!entity.Alive) return;
            if (entity.Intent.Kind == CommandKind.MoveTo)
            {
                Int2 goal = entity.Intent.Point;
                if (MoveSpeed(entity) > 0) entity.Position = IntegerMath.MoveTowards(entity.Position, goal, MoveSpeed(entity));
                if (entity.Position == goal || MoveSpeed(entity) == 0) ClearIntent(entity);
                else CombatFsm.Transition(this, entity, FsmState.Moving);
            }
            else if (entity.Intent.Kind == CommandKind.Attack)
            {
                var target = FindEntity(entity.Intent.TargetId);
                if (!IsEnemy(entity, target)) { ClearIntent(entity); return; }
                if (!InAttackRange(entity, target))
                {
                    CombatFsm.Transition(this, entity, MoveSpeed(entity) > 0 ? FsmState.Moving : FsmState.Idle);
                    if (MoveSpeed(entity) > 0) entity.Position = IntegerMath.MoveTowards(entity.Position, target.Position, MoveSpeed(entity));
                }
                else if (entity.State == FsmState.Moving) CombatFsm.Transition(this, entity, FsmState.Idle);
            }
        }

        internal void TryStartAttack(EntityState entity)
        {
            if (!entity.Alive || entity.Intent.Kind != CommandKind.Attack || CurrentFrame < entity.AttackReadyFrame) return;
            var target = FindEntity(entity.Intent.TargetId);
            if (IsEnemy(entity, target) && InAttackRange(entity, target))
                CombatFsm.Transition(this, entity, FsmState.AttackWindup);
        }

        internal void ResolveWindup(EntityState entity)
        {
            var target = FindEntity(entity.Intent.TargetId);
            if (!IsEnemy(entity, target)) { ClearIntent(entity); return; }
            if (!InAttackRange(entity, target))
            {
                CombatFsm.Transition(this, entity, MoveSpeed(entity) > 0 ? FsmState.Moving : FsmState.Idle);
                return;
            }
            if (CurrentFrame < entity.HitFrame) return;
            damage.Add(new PendingDamage(entity.EntityId, target.EntityId, DamageKind.BasicAttack));
            CombatFsm.Transition(this, entity, FsmState.Idle);
        }

        private void ClearIntent(EntityState entity)
        {
            entity.Intent = CombatCommand.NoOp;
            CombatFsm.Transition(this, entity, FsmState.Idle);
        }

        private void ResolveDamage()
        {
            damage.Sort((a, b) => {
                int compare = a.SourceId.CompareTo(b.SourceId);
                if (compare == 0) compare = a.Kind.CompareTo(b.Kind);
                return compare == 0 ? a.TargetId.CompareTo(b.TargetId) : compare;
            });
            foreach (var pending in damage)
            {
                var source = FindEntity(pending.SourceId); var target = FindEntity(pending.TargetId);
                if (!IsEnemy(source, target)) continue;
                bool area = pending.Kind == DamageKind.AreaSkill;
                if (!IntegerMath.WithinRange(source.Position, target.Position, area ? Config.SkillRadius : AttackRange(source))) continue;
                int amount = Math.Min(target.Hp, area ? Config.SkillDamage : AttackDamage(source));
                target.Hp -= amount; target.LastDamagedFrame = CurrentFrame;
                Emit(new SimEvent(CurrentFrame, SimEventKind.Damage, source.EntityId, target.EntityId, amount));
                if (target.Alive) continue;
                CombatFsm.Transition(this, target, FsmState.Dead);
                Emit(new SimEvent(CurrentFrame, SimEventKind.Died, source.EntityId, target.EntityId));
            }
        }
        public bool InAttackRange(EntityState source, EntityState target)
            => source != null && target != null &&
               IntegerMath.WithinRange(source.Position, target.Position, AttackRange(source));

        private void BeginCombatFrame()
        {
            foreach (var entity in entities)
            {
                if (!entity.IsHero || entity.Alive || entity.RespawnFrame == 0 ||
                    CurrentFrame < entity.RespawnFrame) continue;
                entity.Hp = Config.HeroHp;
                entity.Position = entity.Team == Team.Blue ? Config.BlueSpawn : Config.RedSpawn;
                entity.Intent = CombatCommand.NoOp;
                entity.HitFrame = entity.RespawnFrame = entity.AttackReadyFrame = entity.SkillReadyFrame = 0;
                entity.LastDamagedFrame = -1;
                CombatFsm.Transition(this, entity, FsmState.Idle);
                Emit(new SimEvent(CurrentFrame, SimEventKind.Respawned, entity.EntityId));
            }
        }

    }
}
```


建议不要一次读完再猜。按五个小步骤实现，每步先编译：

1. Create、FindEntity 与 Validate：保持六个实体和命令原子校验。
2. Apply：处理命令替换与 Q 合法性；重复攻击同一目标不能反复重启前摇。
3. AdvanceIntent：地面移动与追击；失效目标清除意图。
4. TryStartAttack / ResolveWindup：分别负责进入前摇和在命中帧生成伤害请求。
5. ResolveDamage / BeginCombatFrame：统一伤害排序、死亡转换和复活重置。

**固定执行顺序**：复活 → 玩家/机器人命令 → 所有实体移动 → 所有实体动作 → 技能候选伤害 → 伤害排序和结算 → 提交帧号。后续加兵线和碰撞时保留这一顺序的明确性。

当前伤害按 SourceId、伤害类型、TargetId 排序，结算时重新检查来源与目标是否存活。先被击杀的来源，后续尚未结算的伤害可能被跳过。这是当前参考项目的规则，不要擅自换成“本帧所有攻击必定同时生效”。

## 5. 给测试开放内部访问，而不是公开修改权限

在 Core 新建 `AssemblyInfo.cs`，以下为**完整文件**：

```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Moba.Tests.EditMode")]
[assembly: InternalsVisibleTo("Moba.Tests.PlayMode")]
```

这允许测试摆放场景，但普通 Presentation/Networking 程序集仍不能改 EntityState。测试中设置位置属于准备夹具，不是游戏流程。

创建 `Tests/EditMode/CombatTests.cs`，先写如下**完整测试文件**：

```csharp
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
```

然后按下表逐个增加测试。每个测试都从新世界开始，不依赖前一测试的状态：

| 测试 | 夹具与执行 | 断言 |
| --- | --- | --- |
| 正常命中 | 两英雄间距 1000，帧 1 发 Attack，推进至帧 6 | 敌人 HP=940 |
| 命中前出范围 | 第 5 帧前让目标移到范围外 | 本次不伤害 |
| Q 冷却 | 两英雄间距 1000，连续两帧 CastArea | 只第一次扣 120 |
| 死亡抢占 | 被攻击者 HP=60，在命中帧被击杀 | Dead、Intent=NoOp |
| 死亡拒绝技能 | 死亡后给该英雄 CastArea | 不产生 SkillCast |
| 复活 | 记录实际死亡帧 d，推进到 d+99 与 d+100 | 前者仍死，后者满血出生 |
| 原子拒绝 | 同一帧蓝方合法、红方参数越界 | 两者都不发生移动 |

## 6. 把事件显示出来

先在 ManualLocalBootstrap 的 `World.Step(...)` 后加临时 Debug.Log，打印 World.Events 中 Damage、Died、Respawned。只用于观察，确认后减少日志。

然后在 EntityView 上增加最小血条显示或在屏幕角落列出选中实体的 HP、State、HitFrame、AttackReadyFrame。先用数值验证，再画攻击闪光与技能圈。不要把“闪光已经播放”当作“伤害已经结算”。

EntityView 目前在死亡时隐藏 Body、关闭 Collider；本阶段允许这样，阶段 14 会改成尸体保留。复活时把插值起点和终点同时设为出生点，避免从死亡位置慢慢滑回：在 Snapshot 前记录上一帧 Alive，发现 false→true 时令 from=to=新位置。

## 7. 场景内操作顺序

1. 运行，控制蓝方走到红方玩家旁边。
2. 右键红方模型，观察 Moving→AttackWindup→Idle/下一次前摇。
3. 前摇中右键远处，验证取消伤害而冷却保留。
4. 靠近敌方按 Q，观察 HP 与冷却；快速连按不能重复生效。
5. 用测试夹具或临时开发配置降低 HP，观察死亡与复活；不要把便于观察的低 HP 保存成正式默认值。

## 8. 常见问题

| 现象 | 先检查 |
| --- | --- |
| Q 抛 NotSupportedException | 配置是否仍为 MovementFixture |
| 一直追击却不攻击 | 范围单位是否把 1800 写成 1.8，是否每帧重置意图 |
| 攻击永远不命中 | 每帧重复 Transition 到前摇、不断改 HitFrame |
| 取消后立即再攻击 | Exit 错把 AttackReadyFrame 清零 |
| 死亡后还能命中 | 伤害结算未重新校验 source.Alive |
| Console 找不到 Lane/Collision | 混入最终源码；本章版本没有这些依赖 |

参考：[原 CombatWorld](../../UnityMOBA/Assets/Moba/Runtime/Core/CombatWorld.cs)、[战斗测试](../../UnityMOBA/Assets/Moba/Tests/EditMode/CombatTests.cs)。

## 本阶段验收与交接

- [ ] 普攻、取消、冷却、Q、死亡抢占、复活均有测试。
- [ ] 移动阶段旧测试仍通过。
- [ ] 输入、FSM、事件与显示职责分离。
- [ ] 复活不插值穿过整张地图。

阶段 04 给这个世界加入自动兵线与塔规则；阶段 05 才由行为树控制机器人英雄。
