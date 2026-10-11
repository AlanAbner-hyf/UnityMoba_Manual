# 阶段 04 · 第 7 步验证记录

验证日期：2026-10-11。

## 本次修改

- 新增 `Assets/Moba/Tests/EditMode/LaneRulesTests.cs`：9 个测试方法，展开参数后为 11 个用例。
- 将 `Assets/Moba/Config/BattleConfig.asset` 的 Mode 从 CombatFixture 改为 Match，使场景启动时启用兵线、恢复与胜负规则。
- 保留正式配置的默认出兵间隔 300 帧。10 帧间隔只用于部分测试。
- 本次未修改运行时 C# 代码。

## 验收对应关系

以下“通过”均指编辑器外运行 NUnit 对实际 Core 代码进行验证，范围不包含场景表现和鼠标输入。

| 验收项 | 对应测试方法 | 结果与验证内容 |
| --- | --- | --- |
| 开局波次 | `FirstFrameSpawnsThreeMinionsPerTeamWithDefaultConfig` | 通过：第 1 帧每队 3 个小兵，下一波为 301 帧 |
| 下一波边界 | `NextWaveSpawnsOnlyOnItsScheduledFrame` | 通过：分别使用 10/300 帧间隔，中间帧不出兵，第 11/301 帧各生成下一波 |
| 数量上限 | `MinionCapStopsSpawningAndOnlyReplacesVacantSlots` | 通过：每队 24 个存活小兵时不再生成；释放一个蓝方名额后，下波只补一个 |
| 塔选敌 | `TowerPrefersEnemyMinionOverCloserEnemyHero` | 通过：英雄比小兵更近时，塔仍优先攻击范围内的小兵 |
| 恢复暂停 | `SpawnRecoveryWaits59FramesAndStartsOnFrame60AfterDamage` | 通过：真实攻击造成伤害后，第 59 帧前 HP 保持 940，第 60 帧恢复到 942，并产生恢复事件 |
| 塔不复活 | `DestroyedTowerNeverRespawnsInCombatFixture` | 通过：塔被真实攻击击杀后，超过英雄复活等待期仍保持死亡，无复活事件 |
| 双塔同帧毁灭 | `BothTowersKilledByAttacksOnSameFrameProduceDraw` | 通过：两名英雄第 1 帧开始攻击，两塔第 6 帧同时被击杀，结果为 Draw |
| 结算后禁止 Step | `VictoryRejectsFurtherStepWithoutChangingState` | 通过：分别验证蓝胜、红胜；继续 Step 抛出异常，帧号、结果、出兵计划、实体血量和位置不变 |
| 默认配置完整逻辑对局 | `DefaultConfigCompletesFullMatchThroughNormalCommands` | 通过：不改血量、不瞬移、不缩短出兵间隔，通过正常攻击命令产生摧塔胜利 |

“塔不复活”单独使用 CombatFixture，是因为 Match 在塔死亡后立即结算，再调用 Step 会被拒绝，无法继续推进复活等待期。该测试隔离验证共同的死亡与复活规则；Match 模式的摧塔结算和停止推进由另外两个测试覆盖。

## 完整逻辑对局结果

配置为 `BattleConfig.CreateDemo()`，出兵间隔 300 帧，塔初始 HP 为 6000。蓝方每帧提交攻击红塔的命令，红方和机器人不提交主动命令，小兵与防御塔执行自动战斗逻辑。

- 结束帧：8833。
- 结果：BlueWins。
- 波次：30。
- 蓝塔剩余 HP：4800；红塔剩余 HP：0。
- 测试检查了红塔累计受到 6000 点伤害及死亡事件。36000 帧只作为失败保护上限，到达上限不算胜利。

这是程序驱动的完整逻辑对局，尚未在 Unity 场景内通过人工鼠标操作玩完一局。

## 实际执行方式及回归结果

使用 Unity 6000.3.23f1 附带的 Roslyn 编译器，将项目现有 Core 脚本与全部 EditMode 测试分别编译为 `Moba.Core.dll` 和 `Moba.Tests.EditMode.dll`，并引用项目 PackageCache 中的 NUnit。两个程序集分开编译，也检查了测试友元访问是否成立。

随后在 Unity 编辑器外使用 NUnit 的 `NUnitTestAssemblyRunner` 执行测试。此方式验证纯 C# 逻辑，不替代 Unity 的完整导入、程序集编译和场景运行检查。

| 测试类 | 通过 | 失败 |
| --- | ---: | ---: |
| DeterministicMovementTests | 11 | 0 |
| CombatRuleTests | 7 | 0 |
| CombatTests | 1 | 0 |
| LaneRulesTests | 11 | 0 |
| 合计 | 30 | 0 |

原始结果：[editmode-results.xml](editmode-results.xml)。无跳过用例。

## 在 Unity 中重复验证

1. 打开 UnityMOBA_Manual，等待脚本导入与编译完成，检查 Console 是否出现编译错误。
2. 打开 **Window → General → Test Runner**，选择 **EditMode**。
3. 选中 `LaneRulesTests`，点击 **Run Selected**。应看到 11 个用例通过；需要回归现有全部测试时点击 **Run All**。
4. 如需查看完整逻辑对局结果，选中 `DefaultConfigCompletesFullMatchThroughNormalCommands`，查看测试输出。
5. 检查场景使用的 BattleConfig 资源 Mode 为 **Match**，保持默认 300 帧出兵间隔，然后进入 Play。
6. 通过鼠标操作英雄，观察出兵、交战、攻击防御塔和胜负产生后的停止推进。场景 UI 是否显示波次、塔 HP 和胜负，取决于第 8 步的视图实现进度。

## 尚未验证

- Unity 编辑器内 Test Runner 执行与完整项目编译：本次使用编辑器外的编译和 NUnit 执行方式。
- 场景内人工鼠标操作完成整局、视图表现及运行时 Console：本次未操作 Unity 场景。
- 多客户端同步：不属于阶段 04 本地逻辑测试范围，本次未执行。
