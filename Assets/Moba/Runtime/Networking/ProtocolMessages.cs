using System;
using System.IO;
using System.Text;

namespace Moba.Networking
{
    // 编号属于协议约定，后续增加处理逻辑时不能随意重排。
    public enum MessageKind : byte
    {
        Hello = 1, Welcome = 2, Reject = 3, Prepare = 4, InitialStateAck = 5, Countdown = 6, Start = 7,
        Input = 8, FrameBundle = 9, FrameAck = 10, DesyncReport = 11, SyncStatus = 12,
        WaitStatus = 13, AiTrace = 14, Ping = 15, Pong = 16, MatchEnd = 17, ProtocolError = 18,
        RoomSnapshot = 19, SetReady = 20, LeaveRoom = 21, LeaveAck = 22, MatchEndAck = 23
    }

    // 消息拥有独立的载荷副本，调用方不能在排队后修改消息内容。
    public sealed class WireMessage
    {
        private readonly byte[] payload;
        public MessageKind Kind { get; }
        public int WireSize => 4 + MessageCodec.HeaderSize + payload.Length;

        public WireMessage(MessageKind kind, byte[] payload)
        {
            if (!Enum.IsDefined(typeof(MessageKind), kind))
                throw new InvalidDataException("Unknown message type.");
            if (payload == null || payload.Length > MessageCodec.MaxBodySize - MessageCodec.HeaderSize)
                throw new InvalidDataException("Invalid payload length.");
            Kind = kind;
            this.payload = (byte[])payload.Clone();
        }

        public static WireMessage Create(MessageKind kind, Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                write?.Invoke(writer);
                writer.Flush();
                return new WireMessage(kind, stream.ToArray());
            }
        }

        // 每次取得独立的只读流，读取位置不会互相影响。
        public BinaryReader OpenReader()
            => new BinaryReader(new MemoryStream(payload, false), Encoding.UTF8);

        internal void WritePayload(BinaryWriter writer) => writer.Write(payload);
    }

    public static class ProtocolFields
    {
        // 无 BOM；非法 UTF-8 或不成对的 UTF-16 代理项直接报错，不静默替换字符。
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public const int MaxStringBytes = 256;

        public static void WriteString(BinaryWriter writer, string value)
        {
            if (value == null) throw new InvalidDataException("Null text.");
            if (Utf8.GetByteCount(value) > MaxStringBytes)
                throw new InvalidDataException("Text too long.");
            byte[] bytes = Utf8.GetBytes(value);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        public static string ReadString(BinaryReader reader)
        {
            int size = reader.ReadUInt16();
            if (size > MaxStringBytes) throw new InvalidDataException("Text too long.");
            return Utf8.GetString(ReadBytes(reader, size));
        }

        // 对 OpenReader 提供的内存流使用；先检查长度再分配字段缓冲区。
        public static byte[] ReadBytes(BinaryReader reader, int count)
        {
            if (count < 0 || count > MessageCodec.MaxBodySize ||
                count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid field length.");
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException("Truncated field.");
            return bytes;
        }

        // 业务处理器按消息类型读完字段后调用，拒绝未约定的额外字段。
        public static void RequireEnd(BinaryReader reader)
        {
            if (reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("Trailing payload bytes.");
        }
    }
}
