# 13　KayKit模型与动画

[上一阶段](12-确定性单位碰撞.md) · [返回实现大纲](手动实现大纲.md) · [下一阶段](14-英雄死亡动画与尸体停留.md)

适用：Unity 6000.3.23f1 / UnityMOBA_Manual。以下为后续手动实施教程，不表示已执行Unity运行验证。

## 本阶段要得到什么

玩家为Knight、机器人为Barbarian、小兵为Skeleton_Minion，模型有待机、移动、普攻和施法动作。动作按逻辑显示时间播放，不改变战斗计算。

前置条件：阶段12已存在MotionTrace和合法路径插值，阶段09已有回放暂停/倍速。先制作一个角色验证骨骼，再扩展到三种。

## 1. 导入资源并保留原文件

把已有合法KayKit资源放在Assets/ThirdParty/KayKit下，保留Adventurers、Skeletons、CharacterAnimations和许可证。原素材不改动；派生资产放在Moba目录：

```text
Assets/Moba/Animations/KayKit/
Assets/Moba/Materials/KayKit/
Assets/Moba/Config/KayKit/
```

先确认模型实际有SkinnedMeshRenderer与Rig_Medium骨架。不要把缺贴图或缺动画绑定的问题交给运行时代码自动猜。

## 2. 制作可控的动画资产

对应源动画组：

| 动作 | 源组 | 剪辑 | 循环 |
| --- | --- | --- | --- |
| 待机 | General | Idle_A | 是 |
| 英雄移动 | MovementBasic | Running_A | 是 |
| 小兵移动 | MovementBasic | Walking_A | 是 |
| 普攻 | CombatMelee | Melee_1H_Attack_Chop | 否 |
| 英雄技能 | CombatRanged | Ranged_Magic_Spellcasting | 否 |
| 英雄死亡，下一章使用 | General | Death_A | 否 |

源路径在CharacterAnimations/Animations/fbx/Rig_Medium/下。展开相应FBX检查剪辑名称；创建派生.anim副本，保留原FBX不变。

手动创建派生剪辑时可编写小Editor工具处理一个选中剪辑，依次做：

1. CopySerialized到新的AnimationClip资产，legacy=false。
2. 清空AnimationEvents，避免演示动画触发伤害或声音逻辑。
3. 检查曲线都是预期Transform骨骼路径。
4. 对Rig_Medium与Rig_Medium/root根部曲线固定位置为0、旋转为单位四元数、缩放为1。
5. 设置loopTime，EnsureQuaternionContinuity并保存。
6. 对每条曲线绑定，检查model.transform.Find(path)确实存在。

完整方法可逐个对照 [KayKitVisualBuilder.MakeClip / CheckBindings](../../UnityMOBA/Assets/Moba/Editor/KayKitVisualBuilder.cs)。本章手动练习不要求调用一键Prepare覆盖所有Prefab。

## 3. 创建 URP 派生材质

为各模型原材质在Moba/Materials/KayKit创建对应URP/Lit材质。原贴图接Base Map，颜色保持白色或原基础色；Metallic=0、Smoothness约0.2可作为参考起点。

给模型Renderer逐槽替换为派生材质。不要把两队整个人刷成纯红/蓝丢失原贴图；队伍用脚下环和血条区分。

## 4. 建立角色层级

打开HeroView.prefab，形成如下结构：

```text
HeroView                 EntityView + 拾取CapsuleCollider
  VisualPivot            UnitVisual（负责朝向）
    ModelOffset          缩放与脚底校正
      KayKitModel        Animator + 骨骼 + SkinnedMeshRenderer
  TeamMarker             环形Mesh + Renderer
  LabelAnchor
```

根对象Scale=1，位置由逻辑控制。ModelOffset调整模型高度：英雄2.0、小兵约1.2世界单位；根据待机采样后的实际蒙皮包围盒把脚底移到y=0。

骑士右手挂sword_1handed，左手挂shield_round；野蛮人右手axe_1handed；小兵右手Skeleton_Blade。寻找模型内handslot.r/handslot.l，武器局部位置和旋转为0、缩放为1，再检查可见结果。

脚下环放y=0.03；阵营环半径参考英雄0.55、小兵0.46。LabelAnchor放模型高度+0.2。拾取Collider只是输入命中；不要用它覆盖Core碰撞半径。

## 5. 创建 UnitVisualProfile

创建以下完整文件，并通过Create → Moba → Unit Visual Profile为三种角色建立资产：


```csharp
// 完整文件：Assets/Moba/Runtime/Presentation/UnitVisualProfile.cs
using UnityEngine;
namespace Moba.Presentation
{
    [CreateAssetMenu(menuName = "Moba/Unit Visual Profile")]
    public sealed class UnitVisualProfile : ScriptableObject
    {
        public AnimationClip Idle, Move, Attack, Cast, Death;
        public float Height = 2, Stride = 2.5f, CastSeconds = .45f, TurnSpeed = 720, ForwardYaw;
        public AnimationClip Clip(int index) => index == 0 ? Idle : index == 1 ? Move : index == 2 ? Attack : index == 3 ? Cast : Death;
    }
}
```


英雄Move用Running_A，Stride=2.5；小兵用Walking_A，Stride=1.25；CastSeconds=0.45，TurnSpeed=720，ForwardYaw=0先试。小兵Cast为空。本章Death可留空，阶段14再绑定。

## 6. 统一逻辑显示时间

当前逻辑世界已经执行到n，显示在(n-1)到n之间插值：

```text
displayTime = (n - 1 + alpha) / 20
alpha∈[0,1]
```

alpha超过1就停在已知帧末，不用墙钟无限推进；回放暂停alpha停止，倍速只加快到达下一逻辑显示时间。

Animator不要自动累加时间。使用手动PlayableGraph，为每个ClipPlayable设置Speed=0，自己SetTime后Evaluate(0)。API说明见 [Manual更新模式](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Playables.DirectorUpdateMode.Manual.html) 与 [PlayableGraph.Evaluate](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Playables.PlayableGraph.Evaluate.html)。

## 7. 先补齐 EntityView / BattleView 的接口

UnitVisual代码需要以下接入点，先补完再编译：

- EntityView.TeamColor(Team)返回红蓝颜色；保留阶段02颜色规则即可。
- EntityView新增UnitVisual Visual、BattleView Owner、Transform LabelAnchor。
- BattleView保留Logical(Int2)，增加PresentationPaused与PlaybackSpeed。
- 每次创建EntityView时赋Owner，绑定Visual/Profile/Animator。
- EntityView的Snapshot新增frame、radius参数，并计算本帧路径长度moved。
- Snapshot调用Visual.Snapshot(entity,frame,moved,reset,selected,radius)，reset用于首次创建/复活。
- Render沿路径定位后调用Visual.Sample(alpha,本段方向)。
- 回房清理实体会触发UnitVisual.OnDisable/OnDestroy，释放图。

建议此处把早期Snapshot/Render重命名为最终SetSnapshot/RenderAt，并一次更新全部调用点；或者保留原名适配相同参数。不能同时保留两套独立更新时间。

## 8. 编写 UnitVisual，并按方法逐个检查

下面是参考项目的**完整UnitVisual文件**。Death分支已包含作为下一阶段接入点，但只有绑定Death剪辑并保留死亡显示对象后才会显示尸体。


```csharp
// 完整文件：Assets/Moba/Runtime/Presentation/UnitVisual.cs
using System;
using Moba.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
namespace Moba.Presentation
{
    public enum UnitPose { Idle, Move, Attack, Cast, Death }
    // Presentation only: samples existing authoritative events, never generates combat commands.
    public sealed class UnitVisual : MonoBehaviour
    {
        public UnitVisualProfile Profile;
        public Animator Animator;
        public Renderer Marker;
        public static int ActiveGraphs { get; private set; }
        public UnitPose Pose { get; private set; }
        public float SampleTime { get; private set; }
        public int AttackPlays { get; private set; }
        public int CastPlays { get; private set; }
        public bool HasGraph => graph.IsValid();
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[5];
        private MaterialPropertyBlock tint;
        private int frame = -1, lastAttack = -1, lastCast = -1, hitFrame, previousPose;
        private float segmentDistance, distanceBefore, lastTime = -1, blendStart;
        private double attackStart = -100, attackEnd = -100, castStart = -100, castEnd = -100, deathStart = -100;
        private float attackDuration = 1;
        private Vector3 attackDirection, facing;
        private bool alive, initialized;
        private void OnEnable() { if (Application.isPlaying) EnsureGraph(); }
        private void OnDisable() { ReleaseGraph(); }
        private void OnDestroy() { ReleaseGraph(); }
        private void EnsureGraph()
        {
            if (graph.IsValid() || Profile == null || Animator == null || !gameObject.activeInHierarchy) return;
            if (!Profile.Idle || !Profile.Move || !Profile.Attack) throw new InvalidOperationException("Unit animation clips missing: " + name);
            Animator.applyRootMotion = false; Animator.fireEvents = false; Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("MobaUnit:" + name); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            int count = clips.Length; mixer = AnimationMixerPlayable.Create(graph, count);
            for (int i = 0; i < count; i++)
            {
                var clip = Profile.Clip(i); if (!clip) continue;
                clips[i] = AnimationClipPlayable.Create(graph, clip); clips[i].SetApplyFootIK(false); clips[i].SetApplyPlayableIK(false);
                clips[i].SetSpeed(0); graph.Connect(clips[i], 0, mixer, i); mixer.SetInputWeight(i, i == 0 ? 1 : 0);
            }
            AnimationPlayableOutput.Create(graph, "Pose", Animator).SetSourcePlayable(mixer);
            graph.Play(); graph.Evaluate(0); ActiveGraphs++;
        }
        private void ReleaseGraph() { if (!graph.IsValid()) return; graph.Destroy(); ActiveGraphs--; }
        public void ResetState(Team team)
        {
            frame = lastAttack = lastCast = -1; hitFrame = 0; segmentDistance = distanceBefore = 0; lastTime = -1;
            attackStart = attackEnd = castStart = castEnd = deathStart = -100; Pose = UnitPose.Idle; previousPose = 0;
            AttackPlays = CastPlays = 0; facing = team == Team.Blue ? Vector3.right : Vector3.left; initialized = true;
            transform.localRotation = Quaternion.LookRotation(facing) * Quaternion.Euler(0, Profile ? Profile.ForwardYaw : 0, 0);
            EnsureGraph();
        }
        public void Snapshot(EntityState entity, int nextFrame, float moved, bool reset, bool selected, float radius)
        {
            bool wasDead = initialized && !alive;
            if (reset || !initialized) ResetState(entity.Team);
            if (!entity.Alive && (!wasDead || reset))
            {
                // Record once; subsequent dead snapshots must not restart the fall.
                deathStart = Math.Max(0, nextFrame - 1) / (double)BattleConfig.LogicHz;
                attackEnd = castEnd = -100;
            }
            alive = entity.Alive;
            if (nextFrame != frame)
            {
                distanceBefore += segmentDistance; segmentDistance = moved; frame = nextFrame;
                if (entity.State != FsmState.AttackWindup && nextFrame < hitFrame) attackEnd = -100;
            }
            if (Marker != null)
            {
                Marker.enabled = alive;
                if (tint == null) tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", EntityView.TeamColor(entity.Team) * (selected ? 1.35f : 1)); Marker.SetPropertyBlock(tint);
                Marker.transform.localScale = Vector3.one * radius * (selected ? 1.15f : 1);
            }
        }
        public void OnSimEvent(SimEvent e, EntityState actor, EntityState target, BattleConfig config)
        {
            if (!alive || Profile == null) return;
            double start = Math.Max(0, e.Frame - 1) / (double)BattleConfig.LogicHz;
            if (e.Kind == SimEventKind.SkillCast && Profile.Cast && e.Frame > lastCast)
            { lastCast = e.Frame; castStart = start; castEnd = start + Profile.CastSeconds; CastPlays++; }
            else if (e.Kind == SimEventKind.AttackStarted && e.Frame > lastAttack)
            {
                lastAttack = e.Frame;
                if (start < castEnd) return;
                attackStart = start; int windup = actor.IsHero ? config.HeroWindup : config.Lane.MinionWindup;
                int cooldown = actor.IsHero ? config.HeroCooldown : config.Lane.MinionCooldown;
                hitFrame = e.Frame + windup;
                attackDuration = Mathf.Max(windup / (float)BattleConfig.LogicHz, Mathf.Min(cooldown / (float)BattleConfig.LogicHz, Profile.Attack.length));
                attackEnd = start + attackDuration; AttackPlays++;
                if (target != null) attackDirection = BattleView.Logical(target.Position) - BattleView.Logical(actor.Position);
            }
        }
        public void Sample(float alpha, Vector3 direction)
        {
            if (Profile == null || (!alive && !Profile.Death)) return; EnsureGraph(); if (!graph.IsValid()) return;
            alpha = Mathf.Clamp01(alpha); float time = Mathf.Max(0, frame - 1 + alpha) / BattleConfig.LogicHz;
            float dt = lastTime < 0 ? 0 : Mathf.Max(0, time - lastTime); lastTime = time; SampleTime = time;
            var next = !alive ? UnitPose.Death : time < castEnd ? UnitPose.Cast : time < attackEnd ? UnitPose.Attack : segmentDistance > .00001f ? UnitPose.Move : UnitPose.Idle;
            if (next != Pose) { previousPose = (int)Pose; Pose = next; blendStart = time; }
            int current = (int)Pose; float blend = Mathf.Clamp01((time - blendStart) / (current >= 2 ? .05f : .08f));
            for (int i = 0; i < mixer.GetInputCount(); i++)
            {
                var clip = Profile.Clip(i); if (!clip) continue; double clipTime;
                if (i == 0) clipTime = time % clip.length;
                else if (i == 1) clipTime = ((distanceBefore + segmentDistance * alpha) / Profile.Stride * clip.length) % clip.length;
                else if (i == 2) clipTime = Mathf.Clamp01((float)(time - attackStart) / attackDuration) * clip.length;
                else if (i == 3) clipTime = Mathf.Clamp01((float)(time - castStart) / Profile.CastSeconds) * clip.length;
                else clipTime = Math.Max(0, Math.Min(clip.length, time - deathStart));
                clips[i].SetTime(clipTime);
                mixer.SetInputWeight(i, i == current ? (previousPose == current ? 1 : blend) : i == previousPose ? 1 - blend : 0);
            }
            graph.Evaluate(0);
            Vector3 desired = Pose == UnitPose.Attack ? attackDirection : (Pose == UnitPose.Cast || Pose == UnitPose.Death) ? facing : direction;
            desired.y = 0;
            if (desired.sqrMagnitude > .000001f)
            {
                facing = Vector3.RotateTowards(facing, desired.normalized, Profile.TurnSpeed * Mathf.Deg2Rad * dt, 0);
                transform.localRotation = Quaternion.LookRotation(facing) * Quaternion.Euler(0, Profile.ForwardYaw, 0);
            }
        }
    }
}
```


按这个顺序实现和验证：

1. EnsureGraph/ReleaseGraph：只建一次、幂等销毁，ActiveGraphs用于生命周期测试。
2. ResetState：设初始朝向，清上局动作和事件帧号。
3. Snapshot：接收存活状态、帧号、实际移动距离，更新环形队伍标记。
4. OnSimEvent：AttackStarted、SkillCast去重，不由事件重新结算伤害。
5. Sample：决定Pose、计算剪辑时间、混合、朝向，最后Evaluate。

动画优先级是Death→Cast→Attack→实际位移Move→Idle。Move按累计真实移动路程采样；被碰撞卡住、虽有移动意图但路程为0时应待机。

## 9. 把 SimEvent 接到动画

BattleView在新逻辑帧的Snapshot之后遍历World.Events：

1. 先分发SkillCast，确保技能在同帧有表现优先级。
2. 再分发其他事件，将source实体、target实体、config传给对应Visual。
3. 用lastAttack/lastCast去重；同一帧多次刷新HUD不得重播。
4. 攻击取消且尚未到命中帧时，取消旧攻击表现窗口。
5. 不在渲染Update读取FSM=AttackWindup就每帧重新开始攻击动画。

每帧Snapshot本身也要去重，否则elapsed反复清零会让动画一直停在开始。参考 [BattleView](../../UnityMOBA/Assets/Moba/Runtime/Presentation/BattleView.cs)。

## 10. 测试顺序

| 步骤 | 验证 |
| --- | --- |
| 单模型待机 | 骨骼和实际网格均改变姿态，脚底稳定 |
| 移动 | 走路方向与路径一致，停住不原地跑 |
| 普攻 | 一次AttackStarted只播放一次，取消不继续前摇 |
| 技能 | 只在逻辑成功施放时播放，冷却失败不播放 |
| 等待 | 无新帧时停到已知帧末，不继续挥砍 |
| 回放 | 暂停、1×/2×在相同显示时间采样结果一致 |
| 两客户端 | 模型正常，同时逐帧hash仍一致 |
| 回房 | ActiveGraphs及EntityView数量归零 |

ReferenceValidator补查模型、材质、武器槽、LabelAnchor、Profile和必需剪辑。资源测试对照 [UnitVisualAssetTests](../../UnityMOBA/Assets/Moba/Tests/EditMode/UnitVisualAssetTests.cs)、[UnitVisualTests](../../UnityMOBA/Assets/Moba/Tests/PlayMode/UnitVisualTests.cs)。

## 11. 常见问题与验收

| 现象 | 定位 |
| --- | --- |
| 骨骼动但模型不动 | SkinnedMeshRenderer bones/rootBone、剪辑绑定路径 |
| 动作拖着根位移 | 根曲线未清，或applyRootMotion仍开 |
| 英雄忽大忽小 | 动画包含根缩放，或根与ModelOffset重复缩放 |
| 每帧重播攻击 | 没有按事件帧号去重 |
| 暂停后还会转身 | 朝向dt来自墙钟，而非逻辑显示时间 |
| 图数量每局增加 | OnDisable/OnDestroy没有幂等ReleaseGraph |

- [ ] 三种模型、装备、贴图与动作对应正确。
- [ ] 动画不改变Core/协议/状态hash。
- [ ] 网络等待与回放控制同时约束动作。
- [ ] 多局资源释放通过；下一阶段补Death资产与尸体生命周期。
