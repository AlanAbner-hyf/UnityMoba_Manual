# 07　消息协议与可靠UDP

[上一阶段](06-编帧与状态校验.md) · [返回实现大纲](手动实现大纲.md) · [下一阶段](08-独立服务端与双客户端.md)

适用：Unity 6000.3.23f1 / UnityMOBA_Manual。以下是后续实施教程；示例的完整文件、方法片段与伪代码已分别标注。

## 本阶段要得到什么

实现能承载业务消息的可靠 UDP：建立连接、可靠有序交付、去重、重传、分片、容量限制和关闭。先用可控的内存数据报验证，再接真实 Socket。本章尚不启动完整 MOBA 会话。

前置条件：阶段 06 Networking 程序集存在。这个模块需要分成多个小提交；每个步骤有明确方法和测试，不建议一口气写完再运行。

本章大部分算法块标为**伪代码**，需按说明写成类方法；完整实现可逐方法对照原项目 [Transport 目录](../../UnityMOBA/Assets/Moba/Runtime/Networking/Transport)。它不是可直接编译的单文件网络库。

## 1. 先画三层数据，不混淆 ACK

```text
业务：Input、FrameBundle、FrameAck、MatchEnd …
消息：长度 + MessageCodec版本 + MessageKind + payload
数据报：60字节UDP头 + 最多1140字节消息分片
```

UDP ACK 表示“传输包已收到”；FrameAck 表示“客户端模拟到某帧并计算了状态哈希”。两者不能互相替代。

创建顺序：

| 顺序 | 文件 | 本步职责 |
| --- | --- | --- |
| 1 | ProtocolMessages、MessageCodec、BoundedMessageQueue | 完整业务消息 |
| 2 | TransportStats、UdpProtocol、UdpPacketCodec | 接口、计数与报文格式 |
| 3 | DatagramSocket | 真实数据报端口与可注入接口 |
| 4 | ReliableUdpPeer、RttAndPacer | 可靠性、时限和发送节奏 |
| 5 | UdpHandshake、UdpHost、UdpConnector | 连接建立与Socket管理 |
| 6 | UnreliableTraceChannel | 独立AI观察快照 |
| 7 | DatagramFaultInjector | 开发环境故障注入 |

先定义被引用的数据类型再写使用方，避免把大堆“类型不存在”误判为算法错误。

## 2. 定义业务消息和长度校验

阅读 [ProtocolMessages.cs](../../UnityMOBA/Assets/Moba/Runtime/Networking/ProtocolMessages.cs)，手动建立 MessageKind、WireMessage、ProtocolFields。

当前编号从 Hello=1 到 MatchEndAck=23；可以先定义全部枚举，阶段 08/11 再实现相应处理。不要随意重排编号。

实现要点：

1. WireMessage 拷贝输入 byte[]，不能让调用方修改排队中的内容。
2. WriteString 使用严格 UTF-8，按字节长度限制 256，不能用字符数量代替。
3. ReadBytes 先检查剩余长度再分配；RequireEnd 检查一条消息是否完整消费。
4. MessageCodec 最大 body 为 65536 字节；前面另有 4 字节长度。body 内含 ushort 编码版本和 byte MessageKind，所以完整消息上限 65540。
5. Decode 遇到未知枚举、尾部多余内容、截断和非法长度必须拒绝。

对照 [MessageCodec.cs](../../UnityMOBA/Assets/Moba/Runtime/Networking/MessageCodec.cs)，先完成 Encode→Read 往返测试。版本号 4 是当前消息编码版本，独立于之后 SessionWire 的业务版本。

## 3. 定义连接抽象

先在 TransportStats.cs 建立计数字段，至少包括实际收发字节、数据报数、重传、重复、乱序、非法包、队列当前值和峰值。

以下为放在同文件命名空间内的**接口片段**：

```csharp
public interface IMessageConnection : System.IDisposable
{
    bool TrySend(WireMessage message);
    bool TryReceive(out WireMessage message);
    void CloseAfterSend();
    void Close(string reason = null);
    bool IsClosed { get; }
    bool HasPendingIncoming { get; }
    string Fault { get; }
    long SentBytes { get; }
    long ReceivedBytes { get; }
    TransportStats Stats { get; }
}
```

TrySend 成功表示进入传输排队，不代表对方已经执行消息。IMessageConnection 不提供修改 CombatWorld 的能力。

## 4. 写 UdpProtocol 和固定头

照 [UdpProtocol.cs](../../UnityMOBA/Assets/Moba/Runtime/Networking/Transport/UdpProtocol.cs) 定义常量：Header=60、Mtu=1200、Fragment=1140、MaxMessage=65540、Window=64。这里的 MTU 是本项目协议上限，没有自动路径 MTU 探测。

数字按 little-endian 写入：

| 偏移 | 长度 | 字段 |
| --- | --- | --- |
| 0 | 4 | Magic 0x3155444D |
| 4 | 1 | TransportVersion |
| 5 | 1 | UdpKind |
| 6 | 2 | Flags，只有 AckValid 位 |
| 8 | 16 | ConnectionId |
| 24 | 4 | PacketSequence |
| 28 | 4 | AckBase |
| 32 | 8 | AckBits |
| 40 | 4 | MessageId |
| 44 | 2 | FragmentIndex |
| 46 | 2 | FragmentCount |
| 48 | 4 | TotalMessageBytes |
| 52 | 2 | PayloadBytes |
| 54 | 2 | Reserved=0 |
| 56 | 4 | CRC32 |
| 60 | 可变 | payload |

Encode 先把 CRC 字段写为零，再对完整报文计算 CRC32/IEEE；Decode 计算时同样把 56—59 字节视作零。反射多项式为 0xEDB88320，标准测试文本“123456789”的 CRC 应为 0xCBF43926。

校验顺序：物理长度 → Magic/版本 → 标志与类型 → CRC/载荷长度 → 各类型字段约束 → 连接身份 → ACK/缓存修改。CRC 检测损坏，不提供加密或账号认证。

## 5. 处理 uint 序号环绕

下面是**完整类型**，放入 UdpProtocol.cs 的 Moba.Networking 命名空间：

```csharp
public static class Sequence32
{
    public static int Compare(uint a, uint b)
    {
        uint delta = unchecked(a - b);
        if (delta == 0x80000000)
            throw new System.IO.InvalidDataException("AmbiguousSequence");
        return delta == 0 ? 0 : delta < 0x80000000 ? 1 : -1;
    }
    public static uint Distance(uint a, uint b) => unchecked(a - b);
}
```

测试 0 相对 uint.MaxValue 是下一序号；差值恰好 2^31 的比较拒绝。后面所有包序号比较统一调用它，不能局部改回 `a > b`。

## 6. 从一个包开始实现可靠接收窗口

ReliableUdpPeer 先建立 receiveBase、receiveBits、received 字典和接收消息队列。接收 Data 的**伪代码**：

```text
确认 ConnectionId、来源端点和完整字段合法
验证并处理报文携带的 ACK
若 seq <= receiveBase：记重复，安排 ACK，返回
distance = seq - receiveBase（按uint环绕）
若 distance > 64：不缓存，安排 ACK，返回
若字典已有 seq：内容相同视为重复；不同则协议错误
检查缓存字节上限
缓存该片；设置 bit(distance-1)
while bit0 == 1:
    取 receiveBase+1，按顺序 Commit
    移除缓存，receiveBase++，receiveBits >>= 1
安排 ACK
```

接收 1、3 时 AckBase=1，AckBits 的 bit1=1；补到 2 后可顺序提交 2、3，AckBase=3。此例写成测试，不只打印日志。

重复 ACK 不需要再 ACK；AckOnly 没有可靠序号。否则会形成 ACK 相互确认的循环。

## 7. 实现可靠发送和 ACK 清理

建立 outgoing 消息队列、sent 未确认包、nextPacket、sendBase、highestSent。先按窗口发新包，再处理到期重传：

1. 给每个实际分配的数据片保留原 PacketSequence、MessageId 和内容；重传不能换序号。
2. ApplyAck 先验证 AckBase 和每一个 AckBits 不指向尚未实际发送的包，然后再释放缓存。
3. 对已经确认的 pending 包删除一次，更新 sendBase。
4. 只有首次发送且从未重传的包可用于 RTT 样本，避免不知道 ACK 对应哪一次发送。
5. ACK 可附在出站数据上；否则约 20ms 后发送 AckOnly。发送失败时不能假装已经发出。

写出“伪造未来 ACK 不能清空真实未确认包”的测试。不能先释放再检查 ACK 合法性。

## 8. 添加重传时限与发送节奏

按 [RttAndPacer.cs](../../UnityMOBA/Assets/Moba/Runtime/Networking/Transport/RttAndPacer.cs) 实现：

- 初始 RTO=0.5 秒；首次 SRTT=样本，Variation=样本/2。
- 后续 Variation=0.75×旧值+0.25×|旧SRTT-样本|；SRTT=0.875×旧值+0.125×样本。
- RTO 限制在 0.15—2 秒；重传时对当前等待间隔退避。
- 数据令牌桶限制突发，ACK 使用独立预算。
- 从排队开始计算工作时限，不能每次重传就重新获得完整时限。

当前参考值：连接可靠工作超时 8 秒、单包最多 8 次发送、队列约 1MiB/256 项；拥塞窗口从 8 起，最大 64，丢包恢复时收缩。详细实现对照 ReliableUdpPeer.TickSend 和 SendPending。

统计分清“协议提交次数”和“Socket实际发送次数”。故障注入吞包后，不能仍把它计为实际 Socket 发出。

## 9. 加入分片重组

总消息按 1140 切片；非末片必须恰为 1140，末片长度由 TotalMessageBytes 计算。最大 58 片。

为了降低实现复杂度，可靠消息的分片不交错分配序号。Commit 只在包序号连续后执行：

1. 首片必须 Index=0 且 MessageId 等于下一个消息号。
2. 在总长、队列内存额度校验后分配 assembly。
3. 每片检查 MessageId、Total、Count 和期望 Index。
4. 按顺序拷贝，最后长度必须刚好等于总长。
5. 解码恰好一条完整 WireMessage，不允许尾部第二条消息。
6. 完成消息进入 incoming，之后 TryReceive 才交给业务。

测试大小 7、1140、1141、65540 字节附近的合法与非法边界；不能只测试小消息。

## 10. 实现数据报端口与可控测试

在 DatagramSocket.cs 中把 UDP 收发封成接口，调用方只处理 byte[]、远端端点、TryReceive/TrySend 和 Dispose。Socket 使用非阻塞轮询，不在 Unity 主线程阻塞等待 Read。

接收缓冲至少能区分大于 1200 的数据报，截断报文整包丢弃。收到一个过长包不等于立刻让所有客户端掉线。

测试中实现相同接口的内存数据报端口：用 Queue 保存包，显式安排投递时间、丢弃第几个包、复制、交换顺序。时间由测试传入，例如 0、0.02、0.04，不用 Thread.Sleep 做协议单元测试。

参考：[DatagramSocket.cs](../../UnityMOBA/Assets/Moba/Runtime/Networking/Transport/DatagramSocket.cs)、[UDP 测试](../../UnityMOBA/Assets/Moba/Tests/EditMode/UdpTransportTests.cs)。

## 11. 加入握手与身份绑定

按 UdpHandshake、UdpConnector、UdpHost 的方法顺序实现：

```text
ConnectRequest(clientNonce)
→ Challenge(nonce, timeBucket, cookie)
→ ConnectResponse(原样返回challenge)
→ Accept(nonce, ConnectionId)
→ Confirm(ConnectionId)
→ Ready(ConnectionId)
→ 可靠业务 Hello（下一阶段）
```

- nonce 和 ConnectionId 使用密码学随机源，不能消耗战斗 RandomState。
- cookie 使用服务器进程私有密钥 HMAC，绑定 IPv4、端口、nonce、时间桶。
- 发 Challenge 时不分配正式 peer；有效 Response 才创建 pending。
- 同一个 endpoint+nonce 重试复用握手状态，不重复上报连接。
- ConnectionId 与准确 endpoint 绑定，旧连接包不能进入新连接。
- 客户端收到 Ready 才向业务层交出连接；仅收到 Accept 不算完成。
- 握手总体期限 8 秒，重试间隔 250→500→1000ms，再按 1000ms；重复包不延长总期限。

固定握手 payload 和填零规则逐字段对照 [UDP 设计](../../UnityMOBA/Docs/Specs/005-udp-transport/design.md)。这是传输身份隔离，不是登录鉴权或防在途攻击方案。

## 12. 关闭与独立观察通道

可靠 Close 占用包序号，排在前面的可靠业务消息后。CloseAfterSend 先进入 draining，拒绝新业务输入，发完既有数据再关闭。对端收到 Close 后仍保留尾部 ACK 重发窗口，不能立即删掉所有状态。

AI 快照使用 Trace 类型：不占可靠窗口，不重传，只保留最新可用完整快照，过期片和旧序号丢弃。不要让可有可无的调试画面阻塞战斗。

连接创建、释放、关闭再重连都各做测试；Dispose 需要幂等。

## 13. 按此顺序验收后才接游戏

| 顺序 | 条件 | 通过标准 |
| --- | --- | --- |
| 1 | 无损内存数据报 | 多条消息有序且仅交付一次 |
| 2 | 丢一个数据包 | 到期重传后交付，无重复 |
| 3 | 丢 ACK | 发送方重传，接收方不重复交付 |
| 4 | 乱序/复制 | 缓存和窗口按预期推进 |
| 5 | 分片丢失 | 不提前交付半条消息，超时可观察 |
| 6 | 未来 ACK/恶意长度 | 无提前释放或无界分配 |
| 7 | 序号回绕 | 边界比较一致 |
| 8 | 握手逐包丢失 | 重试后仅一次连接事件，或明确超时 |
| 9 | 全丢与队列超限 | 在有界时间内失败，内存不持续增长 |
| 10 | 本机实际 UDP | 两进程交换多条消息，退出后端口释放 |

常见误区：窗口位图移位差一位；数据重传换新序号；连接超时只看战斗帧；没有消息边界便直接拼接 byte[]；把重试中的合法重复握手当新玩家。

## 本阶段验收与交接

- [ ] 报文和业务消息分层，所有长度有上限。
- [ ] 可靠层、握手、关闭和弱网边界测试通过。
- [ ] 实际 Socket 测试与模拟数据报测试分别记录。
- [ ] 无任何网络类直接修改 CombatWorld。

阶段 08 才把这个连接接口接入独立服务端和客户端的会话状态机。
