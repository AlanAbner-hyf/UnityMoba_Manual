# 步骤 07 · 第二步：业务消息与长度校验

日期：2026-10-12。

## 范围与文件

按照既有 `Docs/07-消息协议与可靠UDP.md` 第二步及用户本次实现请求，增加 ProtocolMessages.cs、MessageCodec.cs 和 MessageCodecTests.cs。消息编号、编码版本与参考项目一致，增加 Decode(byte[]) 单消息检查入口。不包含连接、队列、Socket、UDP 报头、重传或会话业务处理器。

## 验收结果

| 验收内容 | 结果 | 证据 |
| --- | --- | --- |
| Hello=1 至 MatchEndAck=23，编号固定，消息往返 | 通过 | AllMessageKindsRoundTripWithFixedNumbers |
| 小端长度、版本 4、消息类型的明确字节布局 | 通过 | HeaderUsesFixedLittleEndianLayout |
| 构造时复制载荷，读取流只读且相互独立 | 通过 | MessageOwnsPayloadAndReadersAreIndependentAndReadOnly |
| 短读循环、连续多消息、正常 EOF | 通过 | StreamReadHandlesShortReadsAndConsecutiveMessages |
| 长度前缀和消息体每个截断位置均拒绝 | 通过 | EveryTruncatedPrefixOrBodyIsRejected |
| 声明长度为负、过短、超长时拒绝读取消息体 | 通过 | InvalidDeclaredBodyLengthIsRejectedBeforeReadingBody，6 个参数用例 |
| 未知类型和编码版本不匹配时拒绝 | 通过 | UnknownMessageKindIsRejectedOnConstructionAndDecode，3 个参数用例；WrongVersionIsRejected |
| 最小 7 字节、最大 65540 字节消息可往返；再多一字节拒绝 | 通过 | MinimumAndMaximumMessageSizesRoundTrip |
| 单消息 Decode 拒绝附加字节或第二条消息 | 通过 | SingleMessageDecodeRejectsAppendedBytesOrSecondMessage |
| 字符串按 UTF-8 字节数限制，恰好 256 字节允许 | 通过 | Utf8StringLimitCountsBytesAndAcceptsExactBoundary |
| 非法 UTF-8、非法 UTF-16 输入、截断和超长字符串拒绝 | 通过 | InvalidUtf8AndTruncatedOrOversizedStringsAreRejected 及字符串边界测试 |
| 字段读取前检查剩余长度，无效长度不消费载荷 | 通过 | InvalidFieldLengthDoesNotConsumePayload，3 个参数用例 |
| 业务处理器读完字段后拒绝 payload 内额外数据 | 通过 | BusinessHandlerMustRejectUnreadPayloadFields |

共 23 个新增 NUnit 用例，全部通过。原有 32 个 EditMode 用例同时回归通过；合计 55 个通过、0 失败、0 跳过。原始结果：[editmode-results.xml](editmode-results.xml)。

## 执行环境与限制

使用 Unity 6000.3.23f1 自带 Roslyn 编译器及 PowerShell 运行环境的 .NET 参考程序集，分别编译实际 Core、Networking、EditMode 测试源码，引用项目自带 NUnit，在 Unity 编辑器外运行 NUnitTestAssemblyRunner。

因此确认的是独立程序集编译与纯 C# 测试结果，尚未验证 Unity Editor 完整导入/编译、Test Runner 内执行、PlayMode 场景、多进程或真实网络传输。本步不修改场景或 Prefab；真实 UDP 传输属于后续步骤。

## 在 Unity 中重复执行

等待 Unity 导入和编译后，打开 Window → General → Test Runner → EditMode，选择 MessageCodecTests，点击 Run Selected；选择 Run All 可回归现有全部 EditMode 测试。

## 校验职责边界

- Read(Stream) 允许多条连续消息；Decode(byte[]) 要求恰好一条消息。
- Decode 只认识消息外壳，不识别 Hello、Input 等载荷的具体业务字段。业务处理器应按约定读取，再调用 RequireEnd。
- 枚举中预留了未来阶段的业务类型，不代表已经实现相应处理逻辑。
- 字符串必须使用 ProtocolFields 的严格 UTF-8 编码方法，不能混用 BinaryWriter 默认字符串格式。
- ReadBytes 的剩余长度检查针对有界内存流，不用于直接轮询非阻塞 Socket。
