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

### 本项目已实现的第二步

代码位于 [ProtocolMessages.cs](../Assets/Moba/Runtime/Networking/ProtocolMessages.cs) 和 [MessageCodec.cs](../Assets/Moba/Runtime/Networking/MessageCodec.cs)，测试位于 [MessageCodecTests.cs](../Assets/Moba/Tests/EditMode/MessageCodecTests.cs)。

消息格式为 `int32 body长度 + ushort版本 + byte类型 + payload`，数值为小端编码。body 长度包含版本、类型和载荷，不含前面的 4 字节长度字段。因此最小消息为 7 字节，最大载荷为 65533 字节，最大完整消息为 65540 字节。

`Read(Stream)` 读取流中的下一条消息，允许随后还有其他消息；正常流尾返回 null，半条消息抛出异常。新增的 `Decode(byte[])` 要求数组恰好容纳一条完整消息，适用于后续 UDP 重组结果；空数组或消息外多余字节均被拒绝。两者采用相同的消息编码，新增入口没有更改协议格式。

消息外壳与业务字段分两层检查：Decode 能拒绝声明长度之外的尾部数据，但不知道每种业务消息应有几个字段。具体消息处理器读取已约定字段后，仍必须调用 `ProtocolFields.RequireEnd(reader)`，才能拒绝 payload 内多余字段。

以下只是字符串往返示例，不是阶段 08 正式 Hello 的完整字段定义：

```csharp
var outgoing = WireMessage.Create(MessageKind.Hello,
    writer => ProtocolFields.WriteString(writer, "你好 MOBA"));
byte[] bytes = MessageCodec.Encode(outgoing);
WireMessage incoming = MessageCodec.Decode(bytes);
using (var reader = incoming.OpenReader())
{
    string text = ProtocolFields.ReadString(reader);
    ProtocolFields.RequireEnd(reader);
}
```

文本字段使用 `ProtocolFields.WriteString/ReadString` 成对读写，不与 BinaryWriter.Write(string)/BinaryReader.ReadString 混用。文本前缀是 2 字节 UTF-8 字节长度，上限 256 字节；不包括前缀本身。`ReadBytes` 用于已经装入内存的有边界载荷，不直接对 Socket 使用剩余长度检查。

在 Unity 中打开 Window → General → Test Runner → EditMode，选择 MessageCodecTests → Run Selected。已有验证包含 23 个新增用例；编辑器外 NUnit 回归合计 55 个通过。具体范围及未验证项见 [验证记录](Verification/07-message-codec/verification.md)。

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

### 本项目已实现的第三步

完整代码在 [Transport/TransportStats.cs](../Assets/Moba/Runtime/Networking/Transport/TransportStats.cs)，包含 TransportStats 和 IMessageConnection。目录用于组织代码，命名空间仍为 Moba.Networking；沿用现有 Moba.Networking.asmdef，不需要新增程序集，也不需要挂到 GameObject。

接口约定如下：TrySend 尝试接纳完整 WireMessage；TryReceive 非阻塞地取出一条完整消息，无消息时返回 false；CloseAfterSend 请求排空可靠消息后关闭，期间仍需驱动传输；Close 立即终止；Dispose 由具体实现释放其拥有的资源。IsClosed 不表示接收缓存已空，因此还提供 HasPendingIncoming，便于处理关闭前已经到达的最终消息。具体关闭、收发和幂等释放行为将在后续实现类中完成，接口本身没有这些运行逻辑。

统计字段分三类：

- 累计计数：sentDatagrams、receivedDatagrams、sentBytes、receivedBytes、firstSends、retransmissions、ackOnly、duplicates、outOfOrder、invalid、outsideWindow，以及后续 AI 快照与故障注入计数。
- 当前量及生命周期峰值：sendBytes、receiveBytes、peakSendBytes、peakReceiveBytes、maxDatagramBytes。其中 sentBytes 是累计实际发送字节，sendBytes 是当前发送缓冲占用。
- 当前估计与状态：rttMs、srttMs、rtoMs、congestionWindow、transport、fault。RTT 初始 -1 表示暂无样本；RTO=500ms 和窗口=8 是参考默认值，不表示已实现相应算法。

Copy() 保存独立快照；Since(baseline) 只对累计计数做差，当前量和生命周期峰值保留当前数值。baseline 必须来自同一统计生命周期，不能用另一个连接的计数或计数重置前的数据；null 表示返回累计快照。当前字段全部是值类型或不可变字符串，因此 MemberwiseClone 足够；未来若添加可变集合，应重新审查复制方式。

```csharp
TransportStats before = connection.Stats.Copy();
// 之后由后续传输实现正常处理一段时间的收发。
TransportStats interval = connection.Stats.Since(before);
// interval.sentBytes：这段时间实际发送的数据报字节。
// interval.sendBytes：现在发送缓冲占用，不能解释为本段时间增量。
```

这些统计是诊断数据，不加入战斗状态哈希。字段由后续实现主动维护，不会因为声明了字段就自动开始监测网络。实际字节数指本项目数据报字节，不含操作系统添加的 IP/UDP 头。

在 Test Runner 的 EditMode 下运行 TransportStatsTests，验证快照独立性、增量与当前量的区别、空基线快照。本次编辑器外 NUnit 回归为 58/58 通过；接口收发和真实网络行为尚未实现，不属于本次通过项。见 [验证记录](Verification/07-connection-abstraction/verification.md)。

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

### 本项目已实现的第四步

代码位于 [UdpProtocol.cs](../Assets/Moba/Runtime/Networking/Transport/UdpProtocol.cs) 与 [UdpPacketCodec.cs](../Assets/Moba/Runtime/Networking/Transport/UdpPacketCodec.cs)。继续使用 Moba.Networking 程序集，不需要挂载组件或修改场景。

**UdpProtocol.cs 中的三个类型**：

- UdpKind：传输包类型，固定编号 1～11。ConnectRequest、Challenge、ConnectResponse、Accept、Confirm、Ready、HandshakeReject 用于后续握手；Data 承载可靠消息分片；Ack 单独确认收到的可靠包；Trace 承载独立 AI 观察快照；Close 用于可靠关闭。定义枚举不代表这些行为已实现。
- UdpProtocol：统一保存头长、数据报大小、消息大小、窗口及后续容量/超时常量。Require(condition, reason) 在条件不成立时抛 InvalidDataException，使非法包不能作为正常结果返回。后续 Host 需要捕获异常并按来源处理，编解码器不会自行关闭连接。
- UdpPacket：单个包的内存表示。它不继承 MonoBehaviour，不是可直接发到网络上的内存块；要经 Encode 转为 byte[]。此对象可变，不克隆赋给 Payload 的数组，调用方不要在编码过程中修改它。

**为什么还需要一层头**：MessageCodec 描述“这是一条什么业务消息”，UdpPacketCodec 描述“这属于哪个连接、哪个传输包、哪条消息的第几片，以及确认了哪些包”。因此线上层次为：

```text
WireMessage（例如 Input）
  → MessageCodec.Encode：4 字节长度 + 2 字节消息版本 + 1 字节业务类型 + 业务字段
  → 后续发送器切成每片最多 1140 字节
  → 每片用 UdpPacketCodec.Encode 添加 60 字节头
  → 后续数据报端口交给 Socket
```

这里的 60 字节是应用自定义头，操作系统还会添加 IP/UDP 头；1200 只是本项目数据报字节数组的上限，不是路径 MTU 探测结果。传输版本 1 和消息编码版本 4 分别属于不同层，不要求相等。

**容易混淆的字段**：

| 字段 | 含义与用途 |
| --- | --- |
| Connection | 连接 ID。当前只验证类型所需的零/非零约束；实际 ID 和来源地址、端口是否匹配，由后续 Host/Peer 验证 |
| Sequence | 当前方向的可靠包编号，Data/Close 使用；一个业务消息分成多个包时，每包有自己的序号 |
| Message | 完整可靠消息或 Trace 快照编号；同一条消息的各片共用它 |
| Index / Count / Total | 第几片、总片数、完整消息总字节数；Total 包含 MessageCodec 的长度与版本字段，不包含各片的自定义 UDP 头 |
| AckValid / AckBase / AckBits | 是否携带有效确认、连续收到的对端包序号、其后 64 个序号的接收位图 |
| Payload | 当前片或控制包载荷。线上 PayloadBytes 从数组长度计算，避免手动填写两份长度 |

Sequence、Message 都不是游戏逻辑帧号。AckBits 的 bit i 代表 AckBase+1+i：例如依次收到包 1、3，AckBase=1，bit1=1，bit0=0；这表示包 2 仍缺失，不表示客户端已经模拟了第 3 帧。当前只是读写这些字段，接收窗口的推进在第 6 步实现。

**Encode 的过程**：先 Validate 检查本地构造的包，再用 BinaryWriter 按表中固定顺序写入。数值采用小端编码，例如 0x11223344 写成 44 33 22 11，不依赖结构体字段对齐或内存布局。Connection 沿用参考项目的 Guid.ToByteArray() / new Guid(byte[]) 配对；其字节顺序不完全等同 Guid 文本从左到右的字节顺序，其他语言实现必须遵守相同约定。Reserved 和 CRC 占位先写零，写完 Payload 后计算整个报文 CRC，最后把结果填回 56～59 字节。

**Decode 的过程**：先确认数组为 60～1200 字节，再检查 Magic、版本、标志位和已知类型。读取固定头后检查 Reserved、CRC、声明的载荷长度与实际长度、消息总长；然后只复制最多 1140 字节载荷，最后检查包类型与分片字段的组合。它不会按照不可信的 Total 分配完整消息缓冲，也不会处理 ACK、释放发送队列或修改战斗世界。CRC 计算把数组偏移 56～59 当作零，不改写传入数组；Decode 返回的 Payload 也不共享原报文数组。

除普通损坏外，还需拒绝“CRC 正确但字段非法”的包，例如：未知类型、保留位非零、伪造超大总长、片数不匹配、末片长度不对、无 AckValid 却填了 ACK、握手包携带 ACK、Ack/Trace 占用可靠 Sequence 等。CRC 可以被重新计算，所以它既不能代替字段校验，也不能证明发送者可信。

**分片长度例子**：完整 MessageCodec 消息若为 2500 字节，应有 3 片，载荷分别为 1140、1140、220 字节；每片声明 Total=2500、Count=3，Index 分别为 0、1、2。带头的数据报分别为 1200、1200、280 字节。本步验证单片元数据，不负责实际拆分和重组；跨片一致性还要由后续可靠层检查。最大消息 65540 字节共 58 片，最后一片载荷 560 字节。

以下例子演示一条能装入单片的小消息如何包装、解包，不执行网络收发，也不是阶段 08 正式 Input 字段定义：

```csharp
byte[] messageBytes = MessageCodec.Encode(
    WireMessage.Create(MessageKind.Input, writer => writer.Write(123)));
var packet = new UdpPacket
{
    Kind = UdpKind.Data,
    Connection = new System.Guid("00112233-4455-6677-8899-aabbccddeeff"), // 仅演示；实际由握手提供。
    Sequence = 1,
    Message = 1,
    Index = 0,
    Count = 1,
    Total = messageBytes.Length,
    Payload = messageBytes
};
byte[] datagram = UdpPacketCodec.Encode(packet);
UdpPacket receivedPacket = UdpPacketCodec.Decode(datagram);
WireMessage receivedMessage = MessageCodec.Decode(receivedPacket.Payload);
```

一般情况下 Decode 得到的 Payload 只是一片，必须等待后续重组完成才能交给 MessageCodec.Decode。握手包的 96 字节载荷现在只检查外层长度，内部 nonce、cookie、填零规则留到第 11 步。序号比较工具 Sequence32 见第 5 步。

测试位于 [UdpPacketCodecTests.cs](../Assets/Moba/Tests/EditMode/UdpPacketCodecTests.cs)。在 Window → General → Test Runner → EditMode 下运行该测试类；本次编辑器外执行新增 42 个用例、全部回归 100/100 通过。独立 Python struct/zlib 样例核对了整个报文的字节布局及 CRC。Unity 编辑器内测试、真实 Socket、多客户端同步尚未验证。详见 [验证记录](Verification/07-udp-header/verification.md)。

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

### 本项目已实现的第五步

已在 [UdpProtocol.cs](../Assets/Moba/Runtime/Networking/Transport/UdpProtocol.cs) 的 Moba.Networking 命名空间中加入 Sequence32。沿用参考项目的方法签名和行为，不改变固定头、消息版本或程序集引用。

**为什么要循环比较**：uint 的范围是 0～4294967295，只有 2^32 种值。持续编号在 unchecked 递增时会经历 `4294967294 → 4294967295 → 0 → 1`。普通整数比较会把 0 判为比 4294967295 小；若据此判断新旧，就会把回绕后的新包误当旧包，影响接收窗口和 ACK 处理。可靠 Data/Close 的 Sequence=0 因此可以是正常序号，不能永久作为“未分配”的标志。

**Compare(a, b)** 返回 a 相对 b 的顺序：1 表示更新，0 表示相同，-1 表示更旧。它先计算 `uint delta = unchecked(a - b)`，即 `(a - b) mod 2^32`。unchecked 明确允许无符号减法回绕，即使编译器启用了溢出检查也不会在此处抛 OverflowException。

| delta | 结果 |
| --- | --- |
| 0 | 同一序号，返回 0 |
| 1～2147483647，小于半圈 | a 在 b 的前方，返回 1 |
| 2147483648，恰好半圈 | 两个方向同样远，抛 InvalidDataException("AmbiguousSequence") |
| 2147483649～4294967295，大于半圈 | 将 a 判断为 b 后方的旧序号，返回 -1 |

例如 `Compare(0, uint.MaxValue)` 的 delta=1，结果为 1；反向比较 delta=4294967295，结果为 -1。对于 0 和 2147483648，两个方向都正好半圈，不能猜测先后，所以两个方向的 Compare 都拒绝。

这个规则要求参与有意义比较的实际序号跨度小于半圈。当前可靠窗口只有 64，后续连接隔离、窗口和超时共同限制有效包的范围。Sequence32 本身无法分辨已经绕过整圈的同值旧包，也不是任意 uint 集合的全局排序比较器；不能不加限制地交给 List.Sort 或有序树使用。

**Distance(a, b)** 返回从 b 沿递增方向走到 a 的模距离，仍为 `unchecked(a - b)`。它不是绝对值或最短距离，也不直接判定新旧：

```text
Distance(0, 4294967295) = 1
Distance(1, 4294967294) = 3   // 4294967294 → 4294967295 → 0 → 1
Distance(4294967295, 0) = 4294967295
Distance(2147483648, 0) = 2147483648
```

最后一例可以得到数学距离，但 Compare 会拒绝判断该对序号的先后。因此接收端应先比较，再根据距离判断窗口。下面只是第 6 步的调用顺序示意，当前没有实现接收窗口、缓存或 ACK 状态更新：

```csharp
int order = Sequence32.Compare(packet.Sequence, receiveBase);
if (order <= 0)
{
    // 相同或旧序号：进入后续重复包处理，不再作为新包交付。
    return;
}
uint distance = Sequence32.Distance(packet.Sequence, receiveBase);
if (distance > UdpProtocol.Window)
{
    // 新序号，但超出当前 64 包接收窗口：进入后续窗口外处理。
    return;
}
// 此时 distance 在 1～64 之间，才可使用 bit(distance - 1)。
```

序号递增时也应明确使用 `unchecked(sequence + 1u)`。Compare 里的 unchecked 只作用于它包住的表达式，不会自动让调用方的其他加法允许溢出。

新增测试 [UdpSequenceTests.cs](../Assets/Moba/Tests/EditMode/UdpSequenceTests.cs) 共 22 个用例：普通顺序、相等、回绕、半圈两侧、双向半圈拒绝、方向性距离，以及普通/跨零点的 64 与 65 包窗口距离。编辑器外全部 EditMode 逻辑回归 122/122 通过；另将 UdpProtocol.cs 与序号测试以 `-checked+` 编译，22/22 通过。Unity 编辑器内测试和可靠 UDP 收发未验证，见 [验证记录](Verification/07-uint-sequence/verification.md)。

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
