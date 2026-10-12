# 步骤 07 · 第五步：uint 序号环绕

日期：2026-10-12。

## 实现范围

按用户对教程第五步的实现请求，在 UdpProtocol.cs 中加入 Sequence32.Compare 和 Distance，保持教程与参考项目的模 2^32 半区间约定。新增 UdpSequenceTests.cs，并更新教程第五步说明。不改变线上格式、场景、Prefab、程序集引用、游戏帧号或战斗规则。

本步提供后续窗口与 ACK 算法使用的工具，不实现可靠发送、接收、重传或跨回绕的数据交付。

## 验收结果

| 验收标准 | 结果 | 证据及范围 |
| --- | --- | --- |
| 相同序号返回 0，普通新旧返回 1 / -1 | 通过 | CompareRecognizesEqualityOrderAndWrap；同时检查交换参数后的反向结果 |
| 0 相对 uint.MaxValue 为新序号，反向为旧序号 | 通过 | 同上，含跨越多个序号的回绕例子 |
| 半圈前后分别判断为新 / 旧 | 通过 | 同上，delta=0x7FFFFFFF 与 0x80000001 |
| 恰好半圈的比较在两个方向均拒绝 | 通过 | ExactHalfRangeIsAmbiguousInBothDirections，3 组不同起点，错误为 AmbiguousSequence |
| Distance 返回沿递增方向的模距离 | 通过 | DistanceIsForwardModuloDistanceNotAbsoluteDistance，6 个用例；包括相等、回绕、反向及半圈距离 |
| 回绕后第 64 个序号仍在窗口内，第 65 个在窗口外 | 通过 | ReceiveWindowDistancesRemainCorrectAcrossWrap，2 个起点，每个逐项检查距离 1～65；不代表已经实现接收窗口 |
| 开启编译器溢出检查后工具仍正确 | 通过 | 单独用 -checked+ 编译 UdpProtocol.cs 和 UdpSequenceTests.cs，22/22 通过 |
| Core、Networking、EditMode 测试分别编译并回归 | 通过 | Unity 自带 Roslyn + 宿主 .NET 参考程序集，项目 NUnit 执行器，122 通过、0 失败、0 跳过，其中新增 22 个 |
| Unity Editor 全项目编译、Console 和 Test Runner | 未验证 | 本次在编辑器外编译执行，不等同完整 Unity 导入与编辑器内验证 |
| Scene/Prefab 引用、Host/Server/Client 启动与两客户端同步 | 未验证 | 本步未改变 Unity 对象；传输驱动尚待后续步骤 |
| 真实网络回绕、ACK、乱序缓存、重传与有序交付 | 未验证 | 本步只有序号工具，不将数值测试当作可靠传输验证 |

原始结果：[全部 EditMode 逻辑测试](editmode-results.xml)、[checked 编译下的序号测试](checked-sequence-results.xml)。额外 22 个 checked 测试是同一组测试的第二种编译配置，不计为 144 个不同用例。

## 重复验证

Unity 导入后打开 Window → General → Test Runner → EditMode，选择 UdpSequenceTests → Run Selected；Run All 可回归现有 EditMode 测试。

编辑器外验证使用 Unity 6000.3.23f1 自带 Roslyn；分别生成 Core、Networking 与 EditMode 测试程序集，引用项目缓存中的 NUnit，再使用 NUnitTestAssemblyRunner 执行。checked 配置仅对 UdpProtocol.cs 和 UdpSequenceTests.cs 建立独立测试程序集，不修改项目编译选项。

## 使用约束

比较只在有效跨度小于 2^31 的协议上下文内具有先后意义，不能用于任意序号集合的全局排序。Distance 不做先后判定；先 Compare，再检查窗口距离。恰好半圈由上层捕获并按协议错误处理，不能继续更新 ACK 或缓存。序号加法仍需调用方明确允许回绕；连接隔离、迟到包处置和超时也仍由后续传输层负责。
