using System;
using System.IO;

namespace Moba.Networking
{
    // 线上编号必须稳定；这是传输包类型，不是业务层的 MessageKind。
    public enum UdpKind : byte
    {
        ConnectRequest = 1,
        Challenge = 2,
        ConnectResponse = 3,
        Accept = 4,
        Confirm = 5,
        Ready = 6,
        HandshakeReject = 7,
        Data = 8,
        Ack = 9,
        Trace = 10,
        Close = 11
    }

    public static class UdpProtocol
    {
        public const uint Magic = 0x3155444D; // 小端字节为 4D 44 55 31，即 M D U 1。
        public const byte Version = 1; // 传输版本，独立于 MessageCodec.ProtocolVersion。

        // Mtu 是本协议的数据报上限，包含自定义头，不含操作系统 IP/UDP 头。
        public const int Header = 60;
        public const int Mtu = 1200;
        public const int Fragment = Mtu - Header;
        public const int MaxMessage = 65540; // MessageCodec 的 65536 字节 body + 4 字节长度。
        public const int MaxFragments = (MaxMessage + Fragment - 1) / Fragment;
        public const int Window = 64; // 与 ulong AckBits 的位数一致。

        // 后续队列、重传、握手和关闭实现共用的上限；声明常量不会自动执行这些策略。
        public const int QueueBytes = 1024 * 1024;
        public const int QueueMessages = 256;
        public const double WorkTimeout = 8, HandshakeTimeout = 8, PendingTimeout = 5;
        public const double DrainTimeout = 5, Linger = 4; // 时间单位均为秒。

        public static void Require(bool value, string reason)
        {
            if (!value) throw new InvalidDataException(reason);
        }
    }

    // uint 序号按模 2^32 循环。比较的有效跨度必须小于半圈，不能用于任意 uint 的全局排序。
    public static class Sequence32
    {
        // 返回 1：a 比 b 新；0：相同；-1：a 比 b 旧。
        public static int Compare(uint a, uint b)
        {
            // 明确允许回绕，不受编译器是否开启溢出检查影响。
            uint delta = unchecked(a - b);
            if (delta == 0x80000000u)
                throw new InvalidDataException("AmbiguousSequence"); // 正好半圈，无法确定先后。
            return delta == 0 ? 0 : delta < 0x80000000u ? 1 : -1;
        }

        // 从 b 沿序号递增方向走到 a 的模距离，不是绝对值或最短距离。
        // 接收窗口应先 Compare 判定新旧，再用 Distance 检查是否落在 Window 内。
        public static uint Distance(uint a, uint b) => unchecked(a - b);
    }

    // 单个传输包的内存表示；不能按此对象的内存布局直接发送。
    // 它可变且不克隆 Payload，构造完成后不要在 Encode 期间修改它。
    public sealed class UdpPacket
    {
        public UdpKind Kind;
        public Guid Connection;
        public bool AckValid;
        public uint Sequence; // 当前方向的可靠包序号，Data/Close 使用；环绕后可为 0。
        public uint AckBase; // 对端可靠包中，已经连续收到的末尾序号。
        public ulong AckBits; // bit i 确认 AckBase + 1 + i，不表示游戏逻辑帧。
        public uint Message; // 完整可靠消息或独立 Trace 快照的编号。
        public ushort Index; // 从 0 开始的分片索引。
        public ushort Count;
        public int Total; // 完整消息总字节数，不含每片的 UDP 自定义头。
        public byte[] Payload = Array.Empty<byte>();
    }
}
