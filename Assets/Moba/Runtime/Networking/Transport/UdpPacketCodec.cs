using System;
using System.IO;

namespace Moba.Networking
{
    // 只负责单个数据报的格式，不查连接表、不处理 ACK，也不分配消息重组缓冲。
    public static class UdpPacketCodec
    {
        private const int CrcOffset = 56;

        // CRC32/IEEE，反射多项式 0xEDB88320，初值与最终异或均为 0xFFFFFFFF。
        // packet=true 时把数组绝对偏移 56..59 视为零，不修改传入数组。
        public static uint Crc(byte[] bytes, int offset = 0, int count = -1, bool packet = false)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || offset > bytes.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count == -1) count = bytes.Length - offset;
            if (count < 0 || count > bytes.Length - offset) throw new ArgumentOutOfRangeException(nameof(count));

            uint crc = uint.MaxValue;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                crc ^= packet && i >= CrcOffset && i < CrcOffset + 4 ? (byte)0 : bytes[i];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            return crc ^ uint.MaxValue;
        }

        public static byte[] Encode(UdpPacket packet)
        {
            Validate(packet);
            using (var stream = new MemoryStream(UdpProtocol.Header + packet.Payload.Length))
            using (var writer = new BinaryWriter(stream))
            {
                // BinaryWriter 的数值重载使用小端编码；固定顺序决定线上偏移。
                writer.Write(UdpProtocol.Magic);               // 0:  uint32
                writer.Write(UdpProtocol.Version);             // 4:  byte
                writer.Write((byte)packet.Kind);               // 5:  byte
                writer.Write((ushort)(packet.AckValid ? 1 : 0));// 6:  uint16 flags
                // 沿用参考项目的 Guid.ToByteArray() 顺序，与 Guid 文本的字节顺序不完全相同。
                writer.Write(packet.Connection.ToByteArray());// 8:  16 bytes
                writer.Write(packet.Sequence);                // 24: uint32
                writer.Write(packet.AckBase);                 // 28: uint32
                writer.Write(packet.AckBits);                 // 32: uint64
                writer.Write(packet.Message);                 // 40: uint32
                writer.Write(packet.Index);                   // 44: uint16
                writer.Write(packet.Count);                   // 46: uint16
                writer.Write((uint)packet.Total);             // 48: uint32
                writer.Write((ushort)packet.Payload.Length);  // 52: uint16
                writer.Write((ushort)0);                      // 54: reserved
                writer.Write(0u);                             // 56: CRC 占位
                writer.Write(packet.Payload);                 // 60: 本片载荷，无额外长度前缀
                writer.Flush();

                byte[] bytes = stream.ToArray();
                uint crc = Crc(bytes, packet: true);
                for (int i = 0; i < 4; i++)
                    bytes[CrcOffset + i] = (byte)(crc >> (8 * i));
                return bytes;
            }
        }

        public static UdpPacket Decode(byte[] bytes)
        {
            UdpProtocol.Require(bytes != null && bytes.Length >= UdpProtocol.Header &&
                bytes.Length <= UdpProtocol.Mtu, "DatagramLength");

            using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                UdpProtocol.Require(reader.ReadUInt32() == UdpProtocol.Magic &&
                    reader.ReadByte() == UdpProtocol.Version, "UdpVersionMismatch");

                var packet = new UdpPacket { Kind = (UdpKind)reader.ReadByte() };
                ushort flags = reader.ReadUInt16();
                UdpProtocol.Require(flags <= 1, "UdpFlags");
                UdpProtocol.Require(Enum.IsDefined(typeof(UdpKind), packet.Kind), "UdpKindOrPayload");
                packet.AckValid = flags == 1;
                packet.Connection = new Guid(reader.ReadBytes(16));
                packet.Sequence = reader.ReadUInt32();
                packet.AckBase = reader.ReadUInt32();
                packet.AckBits = reader.ReadUInt64();
                packet.Message = reader.ReadUInt32();
                packet.Index = reader.ReadUInt16();
                packet.Count = reader.ReadUInt16();
                uint total = reader.ReadUInt32(); // 尚未校验，不按它分配内存。
                int size = reader.ReadUInt16();
                ushort reserved = reader.ReadUInt16();
                uint expectedCrc = reader.ReadUInt32();

                UdpProtocol.Require(reserved == 0 && expectedCrc == Crc(bytes, packet: true),
                    "UdpChecksumOrReserved");
                UdpProtocol.Require(size == bytes.Length - UdpProtocol.Header, "UdpPayloadLength");
                UdpProtocol.Require(total <= UdpProtocol.MaxMessage, "UdpMessageLength");
                packet.Total = (int)total;
                packet.Payload = reader.ReadBytes(size); // 最多 1140 字节，且已检查实际剩余长度。
                Validate(packet);
                return packet;
            }
        }

        private static void Validate(UdpPacket packet)
        {
            UdpProtocol.Require(packet != null && Enum.IsDefined(typeof(UdpKind), packet.Kind) &&
                packet.Payload != null && packet.Payload.Length <= UdpProtocol.Fragment, "UdpKindOrPayload");

            bool data = packet.Kind == UdpKind.Data || packet.Kind == UdpKind.Trace;
            bool establishedKind = packet.Kind >= UdpKind.Data;
            UdpProtocol.Require(!packet.AckValid || establishedKind, "HandshakeAck");
            UdpProtocol.Require(packet.AckValid || (packet.AckBase == 0 && packet.AckBits == 0), "UnusedAck");

            // 这里只检查该类型应使用零/非零 ID；具体 ID 和来源端点由后续 Host/Peer 绑定验证。
            bool zeroConnection = packet.Kind == UdpKind.ConnectRequest || packet.Kind == UdpKind.Challenge ||
                packet.Kind == UdpKind.ConnectResponse || packet.Kind == UdpKind.HandshakeReject;
            UdpProtocol.Require(zeroConnection == (packet.Connection == Guid.Empty), "UdpIdentity");

            if (data)
            {
                UdpProtocol.Require(packet.Total >= 7 && packet.Total <= UdpProtocol.MaxMessage &&
                    packet.Count == (packet.Total + UdpProtocol.Fragment - 1) / UdpProtocol.Fragment &&
                    packet.Index < packet.Count, "FragmentMetadata");

                int expected = packet.Index == packet.Count - 1
                    ? packet.Total - packet.Index * UdpProtocol.Fragment
                    : UdpProtocol.Fragment;
                UdpProtocol.Require(packet.Payload.Length == expected, "FragmentLength");
            }
            else
            {
                UdpProtocol.Require(packet.Message == 0 && packet.Index == 0 && packet.Count == 0 &&
                    packet.Total == 0, "UnusedFragmentFields");
            }

            if (packet.Kind != UdpKind.Data && packet.Kind != UdpKind.Close)
                UdpProtocol.Require(packet.Sequence == 0, "UnusedSequence");

            if (!data)
            {
                // 握手载荷的内部字段与填零规则留给第 11 步 UdpHandshake。
                int expected = packet.Kind <= UdpKind.Accept || packet.Kind == UdpKind.HandshakeReject ? 96 : 0;
                UdpProtocol.Require(packet.Payload.Length == expected, "ControlPayload");
            }
        }
    }
}
