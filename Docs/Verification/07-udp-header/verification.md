# 步骤 07 · 第四步：UdpProtocol 与固定头

日期：2026-10-12。

## 范围与依据

按用户对教程第四步的实现请求，沿用参考项目已批准的 [UDP 设计第 4 节](../../../../UnityMOBA/Docs/Specs/005-udp-transport/design.md) 的固定头格式与类型约束，新增 UdpProtocol.cs、UdpPacketCodec.cs、UdpPacketCodecTests.cs，并更新教程。编号、字节布局、Guid 字节顺序和 CRC 参数与参考实现兼容，无消息协议版本变更。

相对参考源码：展开用途注释；类型检查提前到 CRC 前；Crc 增加空数组引用与切片参数检查；MaxFragments 用常量表达式计算，结果仍为 58。Sequence32 留在教程第五步。本步不实现窗口、重传、握手状态机、拆片重组或实际 Socket。

## 验收结果

| 验收标准 | 结果 | 证据及边界 |
| --- | --- | --- |
| Core、Networking、EditMode 测试按独立程序集编译 | 通过 | Unity 6000.3.23f1 自带 Roslyn，使用宿主 .NET 参考程序集；不等同 Unity Editor 全项目编译 |
| 固定编号、60 字节头、小端顺序、Guid 字节序、CRC 偏移符合协议 | 通过 | ConstantsAndPacketKindIdsMatchProtocolContract、EntireDatagramMatchesIndependentLittleEndianGoldenBytes |
| CRC32/IEEE 标准向量、切片和参数边界正确 | 通过 | CrcMatchesKnownVectorAndChecksSliceArguments，123456789 → 0xCBF43926 |
| 所有 11 种包的合法外壳能往返 | 通过 | EveryKindRoundTripsItsLegalEnvelope，11 个用例；不验证握手载荷内部语义 |
| 总消息 7、1140、1141、65540 字节的每片长度及往返正确 | 通过 | AllFragmentsAtMessageBoundariesHaveExactSizes，4 个用例；没有执行可靠层重组 |
| 已有消息编码可包装到单个数据报再还原 | 通过 | OneFragmentCarriesAnExistingBusinessMessageWithoutChangingItsEncoding |
| 最大数据报任一字节翻转、空输入、过长、截断、尾随数据均拒绝 | 通过 | CorruptionAtEveryByteIsRejectedAndDecodingDoesNotModifyInput、AllTruncationsAndTrailingBytesAreRejected |
| CRC 正确但未知类型、非法标志/长度/分片/控制字段的包拒绝 | 通过 | InvalidFieldsAreRejectedEvenWithCorrectCrc 等字段、身份外壳、分片和控制载荷测试 |
| 编码端同样拒绝非法本地包 | 通过 | EncoderAlsoRejectsInvalidLocalPackets |
| 校验不改写输入，解码载荷不与输入共享数组 | 通过 | CorruptionAtEveryByteIsRejectedAndDecodingDoesNotModifyInput |
| 编辑器外全部现有 EditMode 逻辑回归 | 通过 | 100 通过，0 失败，0 跳过；其中 UdpPacketCodecTests 42 个 |
| Unity Editor 导入、全项目 Console 编译与 Test Runner 执行 | 未验证 | 本次使用编辑器外编译和项目 NUnit 执行器，未控制已打开的 Unity 编辑器 |
| Scene、Prefab 和 Host/Server/Client 启动路径、双客户端同步 | 未验证 | 本步仅添加纯 C# 编解码文件，未改场景/Prefab；网络会话与 Socket 尚待后续步骤 |

原始 NUnit 结果：[editmode-results.xml](editmode-results.xml)。

## 执行方法

分别编译 Assets/Moba/Runtime/Core、Assets/Moba/Runtime/Networking 和 Assets/Moba/Tests/EditMode，使用项目缓存中的 com.unity.ext.nunit 的 nunit.framework.dll 与 NUnitTestAssemblyRunner 执行测试；工作线程数为 0。测试不依赖真实时间和网络。

Unity 内复验：等待导入完成，检查 Console，打开 Window → General → Test Runner → EditMode，选择 UdpPacketCodecTests → Run Selected。Run All 可回归现有 EditMode 测试。

## 独立字节样例

测试中的完整报文样例使用 Python 标准库构造，未调用本次 C# 编码器：

```python
import struct, uuid, zlib
payload = bytes.fromhex('03000000040001')
b = bytearray(struct.pack(
    '<IBBH16sIIQIHHIHHI',
    0x3155444D, 1, 8, 1,
    uuid.UUID('00112233-4455-6677-8899-aabbccddeeff').bytes_le,
    0x11223344, 0x55667788, 0x8000000000000001,
    0x99aabbcc, 0, 1, 7, len(payload), 0, 0
) + payload)
crc = zlib.crc32(b)  # 0x96c53fd1
struct.pack_into('<I', b, 56, crc)
assert len(b) == 67
```

## 必须保留的责任边界

Decode 只检查单包格式。连接 ID 非零不代表连接已建立；具体 ID、来源端点、ACK 是否确认了实际发出的包，必须由后续 Host/Peer 校验后才能修改可靠状态。CRC 仅检测损坏。分片跨包一致性、握手 nonce/cookie 和填零字段、可靠超时等均未由本步骤实现或验证。
