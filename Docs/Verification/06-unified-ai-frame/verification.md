# 阶段 06 · 操作八：AI 指令加入统一帧

日期：2026-10-12。

## 范围

采用教程明确允许的“测试夹具”实现：新增 Tests/Fixtures/UnifiedFrameHarness.cs，复用真实 InputWindow、FrameScheduler、FrameCommands、CombatWorld 与规范化状态编码；通过唯一服务端 AI 回调组帧，两个独立客户端只重放帧指令。

新增 Fixtures 测试支持程序集、6 个 EditMode 用例、2 个真实行为树 PlayMode 用例；补齐 EditMode/PlayMode 程序集引用，更新教程。未改变生产运行时代码、场景、Prefab、外部行为树资产或 Packages/ProjectSettings。

真实 AI 长跑使用 CombatFixture，因为 MovementFixture 明确拒绝 Attack/CastArea，而 Match 可能提前结算。此选择保留三世界、真实 AI、6000 帧逐帧一致的验收目的；不将测试结果扩展为完整 Match 对局或真实网络验证。

## 验收结果

| 验收标准 | 结果 | 证据 |
| --- | --- | --- |
| 未到调度期限不消费输入、不调用 AI | 通过 | BeforeDeadlineDoesNotConsumeInputsOrEvaluateAi；真实 AI PlayMode 也覆盖 |
| 缺任一方输入时不推进任何世界、不调用 AI、不伪造 NoOp | 通过 | MissingEitherPlayerFreezesAllWorldsAndAiWithoutReplacingInput，分别缺蓝/红输入；PlayMode 在本应评估 AI 的第 5 帧等待 |
| 保留先到输入，另一方补齐后在同一下一帧恢复 | 通过 | 同上；验证原移动指令仍被保留 |
| 等待 2 秒后只推进一帧，下一期限重新从恢复时刻安排 | 通过 | ResumeResetsDeadlineInsteadOfExecutingWallClockDebt；真实 AI PlayMode 验证 2.00 恢复、2.05 下一帧 |
| AI 在服务端 Step 前读取 S(n-1)，客户端没有自己的 AI | 通过 | PlayMode 对实际 AiTrace 的 BasedOnFrame/CommandFrame 做断言，并确认场景中仅 2 个 BlackboardAdapter，全部绑定 Server |
| 同一 FrameCommands 包含玩家与机器人输入，三世界独立执行 | 通过 | 夹具两条 Queue 交付同一不可变指令对象；独立世界/实体断言及逐帧比较 |
| 确定的机器人替身指令执行 6000 帧，三世界字节及哈希逐帧相同 | 通过 | ScriptedRobotOutputReplaysInThreeIndependentWorldsFor6000Frames，EditMode |
| 真实行为树执行 6000 帧，三世界字节及哈希逐帧相同 | 通过 | RealAiUnifiedFramesAgreeFor6000Frames_ClientsReplayWithoutTrees，Unity PlayMode |
| 真实 AI 评估 1500 次，帧号为 1、5、9……5997，并确实产生机器人与攻击/技能指令 | 通过 | 同上；禁止全空机器人输入蒙混通过 |
| 每帧保存规范化字节、哈希和指令，不保存可变实体引用 | 通过 | FrameEvidence；销毁 AI 后从零重放保存的 6000 帧并逐帧匹配原哈希 |
| 注入客户端冷却差异，保留异常帧证据并拒绝继续推进 | 通过 | MismatchPreservesEvidenceAndStopsFurtherAdvance |
| 新增纯 C# 夹具、Core、Networking、EditMode 程序集编译与全部逻辑回归 | 通过 | Unity 自带 Roslyn + 项目 NUnit，128 通过、0 失败、0 跳过，其中新增 6 个 |
| Unity 导入相关脚本、AI Prefab/外部树并执行新增 PlayMode 测试 | 通过 | 独立临时项目，Unity 6000.3.23f1，2 通过、0 失败、0 跳过；使用未修改的原资产 |
| 原项目编辑器当前 Console、完整场景/渲染/Bootstrap 路径 | 未验证 | 未操纵用户正在编辑的项目；临时验证项目仅包含本操作相关模块 |
| 实际 Host/Server/Client 启动、两个客户端进程和 UDP 传输 | 未验证 | 本步用同进程 Queue 传递帧，不启动 Socket；属于后续阶段 |

原始结果：[EditMode 逻辑回归](editmode-results.xml)、[Unity PlayMode 测试](playmode-results.xml)。

## 验证环境与过程

编辑器外分别编译 Moba.Core、Moba.Networking、Moba.Tests.Fixtures、Moba.Tests.EditMode，使用项目自带 NUnitTestAssemblyRunner 执行全部测试，工作线程数为 0。

真实 AI 需要 Unity 引擎。为避免与用户打开的项目冲突，复制 Core、Networking、AI、Tests、Behavior Designer Runtime、ServerAiAgent.prefab 与 HeroDecision.asset 到独立临时项目，使用项目缓存中的 Test Framework 1.6.0 与 NUnit 包，执行：

```text
Unity.exe -batchmode -nographics -projectPath <临时项目完整路径>
  -runTests -testPlatform PlayMode -testFilter AiUnifiedFrameStage06Tests
  -testResults <结果文件> -logFile <日志文件>
```

完成后逐一核对原项目与最终验证副本中的 49 个 Moba 脚本、程序集定义和相关 AI 资产，SHA-256 均一致；最终 Unity 日志未发现 C# 编译错误、脚本缺失或未处理异常。

首次沙箱启动无法连接许可证服务；允许正常访问本机许可服务后，使用 Windows 8.3 短路径的临时项目出现 MonoScript.GetClass 为空及 Prefab 组件缺失，两个测试在 SetUp 阶段失败。修改临时资产的程序集标识没有改善；未将该尝试写回原项目。使用完整长路径新建验证项目，并恢复原始 Prefab/外部树后，两项测试通过。最终结果使用完整路径项目，不能将先前的临时环境加载失败描述为用户资产已损坏。

## 在原项目复验

1. 保存 Prefab/外部树，退出场景 Play，等待导入并检查 Console。
2. Window → General → Test Runner → EditMode → UnifiedFrameTests → Run Selected：6 项。
3. PlayMode → AiUnifiedFrameStage06Tests → Run Selected：2 项。
4. 不要同时运行场景中的 ServerAiSystem；两个测试之间通过 UnityTearDown 等待 Destroy 完成。

## 实现边界

夹具保留固定测试长度内的全部 History，供定位帧、状态字节和哈希差异，不作为生产缓存策略或磁盘回放。AI 或 Step 发生异常后，夹具记录 Failure 并禁止重试推进；不尝试回滚已消费输入或已改变的世界。网络乱序、丢包、会话超时、断线与多进程一致性不属于本次测试。
