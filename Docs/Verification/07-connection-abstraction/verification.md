# 步骤 07 · 第三步：连接抽象

日期：2026-10-12。

## 本次范围

按教程第三步及用户实现请求，新建 Assets/Moba/Runtime/Networking/Transport/TransportStats.cs，包含 TransportStats 及 IMessageConnection。保持参考项目的字段名、默认值和接口签名，增加用途注释。未改变消息编码、模拟规则、场景、Prefab 或程序集引用；未提前实现 Socket、传输队列或 ReliableUdpPeer。

## 验证结果

| 项目 | 结果 | 证据与边界 |
| --- | --- | --- |
| Core、Networking、EditMode 测试独立程序集编译 | 通过 | Unity 自带 Roslyn 编译器，.NET 参考程序集，无 UnityEngine 依赖 |
| Copy 快照不随原对象变化，也不反向修改原对象 | 通过 | CopyRemainsIndependentWhenLiveStatsChange |
| Since 对累计计数做差，保留当前缓冲、峰值及 RTT 等当前值 | 通过 | SinceSubtractsCountersButPreservesCurrentGaugesAndLifetimePeaks |
| Since(null) 返回独立累计快照 | 通过 | SinceNullReturnsIndependentLifetimeSnapshot |
| 全部现有 EditMode 逻辑回归 | 通过 | 58 个通过，0 失败，0 跳过，其中新增 3 个用例 |
| Unity Editor 导入及 Test Runner 内运行 | 未验证 | 本次在编辑器外使用项目 NUnitTestAssemblyRunner 执行 |
| 接口的实际发送、接收、关闭、资源释放 | 未验证 | 本步只有接口约定，具体实现属于后续步骤 |
| Socket、真实 UDP、多进程或多人同步 | 未验证 | 本步不建立网络连接 |

原始 NUnit 结果：[editmode-results.xml](editmode-results.xml)。

## 重复执行

Unity 导入完成后，打开 Window → General → Test Runner → EditMode，选中 TransportStatsTests，点击 Run Selected；Run All 可回归现有 EditMode 测试。

## 统计语义

累计计数使用 long。队列当前量和连接生命周期峰值使用 int，与后续有界队列约定一致。Copy/Since 不修改源对象。Since 只适用于同一统计生命周期内较早的快照，不校验跨连接或重置后的基线。快照为浅复制，目前仅含值类型与不可变 string，不共享可变集合。

这些类型不会自行采集数据、实现重传或保证连接关闭；后续传输实现负责维护字段和履行接口约定。连接关闭后仍可能有待处理的完整接收消息，IsClosed 和 HasPendingIncoming 必须分别判断。
