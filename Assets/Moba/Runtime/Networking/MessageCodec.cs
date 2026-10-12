using System;
using System.IO;
using System.Text;

namespace Moba.Networking
{
    public static class MessageCodec
    {
        public const ushort ProtocolVersion = 4;
        public const int MaxBodySize = 64 * 1024;
        public const int HeaderSize = 3; // ushort 版本 + byte 类型；数值采用小端编码。

        public static byte[] Encode(WireMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            using (var stream = new MemoryStream(message.WireSize))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(message.WireSize - 4);
                writer.Write(ProtocolVersion);
                writer.Write((byte)message.Kind);
                message.WritePayload(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        // 读取流中的下一条消息，允许后面还有消息；只有消息之间的正常 EOF 返回 null。
        public static WireMessage Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var prefix = new byte[4];
            int first = stream.ReadByte();
            if (first < 0) return null;
            prefix[0] = (byte)first;
            ReadExact(stream, prefix, 1, 3);
            int length = prefix[0] | (prefix[1] << 8) | (prefix[2] << 16) | (prefix[3] << 24);
            if (length < HeaderSize || length > MaxBodySize)
                throw new InvalidDataException("Invalid message length.");

            // 只有声明长度合法，才为消息体分配内存。
            var body = new byte[length];
            ReadExact(stream, body, 0, length);
            using (var reader = new BinaryReader(new MemoryStream(body, false), Encoding.UTF8))
            {
                if (reader.ReadUInt16() != ProtocolVersion)
                    throw new InvalidDataException("Protocol version mismatch.");
                var kind = (MessageKind)reader.ReadByte();
                return new WireMessage(kind, reader.ReadBytes(length - HeaderSize));
            }
        }

        // 解码恰好一条完整消息，适用于后续 UDP 分片重组后的字节数组。
        public static WireMessage Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length > MaxBodySize + 4)
                throw new InvalidDataException("Message too large.");
            using (var stream = new MemoryStream(bytes, false))
            {
                WireMessage message = Read(stream);
                if (message == null) throw new InvalidDataException("Empty message.");
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("Trailing message bytes.");
                return message;
            }
        }

        private static void ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read == 0) throw new EndOfStreamException("Truncated message.");
                offset += read;
                count -= read;
            }
        }
    }
}
