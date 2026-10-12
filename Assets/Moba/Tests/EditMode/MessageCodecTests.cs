using System;
using System.IO;
using System.Linq;
using System.Text;
using Moba.Networking;
using NUnit.Framework;

public sealed class MessageCodecTests
{
    private sealed class ShortReadStream : MemoryStream
    {
        public ShortReadStream(byte[] bytes) : base(bytes, false) { }
        public override int Read(byte[] buffer, int offset, int count)
            => base.Read(buffer, offset, Math.Min(count, 1));
    }

    [Test]
    public void AllMessageKindsRoundTripWithFixedNumbers()
    {
        string[] names = { "Hello", "Welcome", "Reject", "Prepare", "InitialStateAck", "Countdown", "Start",
            "Input", "FrameBundle", "FrameAck", "DesyncReport", "SyncStatus", "WaitStatus", "AiTrace",
            "Ping", "Pong", "MatchEnd", "ProtocolError", "RoomSnapshot", "SetReady", "LeaveRoom", "LeaveAck", "MatchEndAck" };
        Assert.That(Enum.GetValues(typeof(MessageKind)).Length, Is.EqualTo(names.Length));
        for (int i = 0; i < names.Length; i++)
        {
            var kind = (MessageKind)(i + 1);
            Assert.That(kind.ToString(), Is.EqualTo(names[i]));
            var source = WireMessage.Create(kind, w => { w.Write(123); ProtocolFields.WriteString(w, "你好 MOBA"); });
            var decoded = MessageCodec.Decode(MessageCodec.Encode(source));
            Assert.That(decoded.Kind, Is.EqualTo(kind));
            using (var reader = decoded.OpenReader())
            {
                Assert.That(reader.ReadInt32(), Is.EqualTo(123));
                Assert.That(ProtocolFields.ReadString(reader), Is.EqualTo("你好 MOBA"));
                ProtocolFields.RequireEnd(reader);
            }
        }
    }

    [Test]
    public void HeaderUsesFixedLittleEndianLayout()
    {
        var message = new WireMessage(MessageKind.Input, new byte[] { 0xAA, 0xBB });
        CollectionAssert.AreEqual(new byte[] { 5, 0, 0, 0, 4, 0, 8, 0xAA, 0xBB }, MessageCodec.Encode(message));
        Assert.That(message.WireSize, Is.EqualTo(9));
    }

    [Test]
    public void MessageOwnsPayloadAndReadersAreIndependentAndReadOnly()
    {
        var original = new byte[] { 7, 9 };
        var message = new WireMessage(MessageKind.Ping, original);
        original[0] = 99;
        using (var first = message.OpenReader())
        using (var second = message.OpenReader())
        {
            Assert.That(first.ReadByte(), Is.EqualTo(7));
            Assert.That(first.ReadByte(), Is.EqualTo(9));
            Assert.That(second.ReadByte(), Is.EqualTo(7));
            Assert.That(second.BaseStream.CanWrite, Is.False);
            Assert.Throws<NotSupportedException>(() => second.BaseStream.WriteByte(1));
        }
    }

    [Test]
    public void StreamReadHandlesShortReadsAndConsecutiveMessages()
    {
        var first = WireMessage.Create(MessageKind.Hello, w => ProtocolFields.WriteString(w, "你好"));
        var second = WireMessage.Create(MessageKind.Ping, w => w.Write(123L));
        using (var stream = new ShortReadStream(MessageCodec.Encode(first).Concat(MessageCodec.Encode(second)).ToArray()))
        {
            var a = MessageCodec.Read(stream);
            Assert.That(a.Kind, Is.EqualTo(MessageKind.Hello));
            using (var reader = a.OpenReader())
            {
                Assert.That(ProtocolFields.ReadString(reader), Is.EqualTo("你好"));
                ProtocolFields.RequireEnd(reader);
            }
            var b = MessageCodec.Read(stream);
            Assert.That(b.Kind, Is.EqualTo(MessageKind.Ping));
            using (var reader = b.OpenReader())
            {
                Assert.That(reader.ReadInt64(), Is.EqualTo(123L));
                ProtocolFields.RequireEnd(reader);
            }
            Assert.That(MessageCodec.Read(stream), Is.Null);
        }
    }

    [Test]
    public void EveryTruncatedPrefixOrBodyIsRejected()
    {
        var valid = MessageCodec.Encode(WireMessage.Create(MessageKind.Ping, w => w.Write(123L)));
        for (int length = 1; length < valid.Length; length++)
        {
            byte[] truncated = valid.Take(length).ToArray();
            Assert.Throws<EndOfStreamException>(() => MessageCodec.Decode(truncated), $"length={length}");
        }
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(Array.Empty<byte>()));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(65537)]
    [TestCase(int.MaxValue)]
    public void InvalidDeclaredBodyLengthIsRejectedBeforeReadingBody(int length)
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) writer.Write(length);
            stream.Position = 0;
            Assert.Throws<InvalidDataException>(() => MessageCodec.Read(stream));
            Assert.That(stream.Position, Is.EqualTo(4));
        }
    }

    [TestCase(0)]
    [TestCase(24)]
    [TestCase(255)]
    public void UnknownMessageKindIsRejectedOnConstructionAndDecode(int kind)
    {
        Assert.Throws<InvalidDataException>(() => new WireMessage((MessageKind)kind, Array.Empty<byte>()));
        var bytes = MessageCodec.Encode(WireMessage.Create(MessageKind.Start, null));
        bytes[6] = (byte)kind;
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(bytes));
    }

    [Test]
    public void WrongVersionIsRejected()
    {
        var bytes = MessageCodec.Encode(WireMessage.Create(MessageKind.Start, null));
        bytes[4] = 5;
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(bytes));
        bytes[4] = 4;
        bytes[5] = 1;
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(bytes));
    }

    [Test]
    public void MinimumAndMaximumMessageSizesRoundTrip()
    {
        var empty = WireMessage.Create(MessageKind.Start, null);
        Assert.That(MessageCodec.Encode(empty).Length, Is.EqualTo(7));
        using (var reader = MessageCodec.Decode(MessageCodec.Encode(empty)).OpenReader())
            ProtocolFields.RequireEnd(reader);

        var payload = Enumerable.Repeat((byte)0xA5, 65533).ToArray();
        var maximum = new WireMessage(MessageKind.FrameBundle, payload);
        byte[] bytes = MessageCodec.Encode(maximum);
        Assert.That(bytes.Length, Is.EqualTo(65540));
        using (var reader = MessageCodec.Decode(bytes).OpenReader())
        {
            CollectionAssert.AreEqual(payload, ProtocolFields.ReadBytes(reader, payload.Length));
            ProtocolFields.RequireEnd(reader);
        }
        Assert.Throws<InvalidDataException>(() => new WireMessage(MessageKind.FrameBundle, new byte[65534]));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(new byte[65541]));
    }

    [Test]
    public void SingleMessageDecodeRejectsAppendedBytesOrSecondMessage()
    {
        var bytes = MessageCodec.Encode(WireMessage.Create(MessageKind.Start, null));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(bytes.Concat(bytes).ToArray()));
    }

    [Test]
    public void Utf8StringLimitCountsBytesAndAcceptsExactBoundary()
    {
        foreach (string text in new[] { "", new string('a', 256), new string('你', 85) + "a", "你好 MOBA 😀" })
        {
            var message = WireMessage.Create(MessageKind.Hello, w => ProtocolFields.WriteString(w, text));
            using (var reader = message.OpenReader())
            {
                Assert.That(ProtocolFields.ReadString(reader), Is.EqualTo(text));
                ProtocolFields.RequireEnd(reader);
            }
        }
        Assert.Throws<InvalidDataException>(() => WireMessage.Create(MessageKind.Hello,
            w => ProtocolFields.WriteString(w, new string('你', 86))));
        Assert.Throws<InvalidDataException>(() => WireMessage.Create(MessageKind.Hello,
            w => ProtocolFields.WriteString(w, new string('a', 257))));
        Assert.Throws<InvalidDataException>(() => WireMessage.Create(MessageKind.Hello,
            w => ProtocolFields.WriteString(w, null)));
        Assert.Throws<EncoderFallbackException>(() => WireMessage.Create(MessageKind.Hello,
            w => ProtocolFields.WriteString(w, "\uD800")));
    }

    [Test]
    public void InvalidUtf8AndTruncatedOrOversizedStringsAreRejected()
    {
        using (var reader = new WireMessage(MessageKind.Hello, new byte[] { 1, 0, 0xFF }).OpenReader())
            Assert.Throws<DecoderFallbackException>(() => ProtocolFields.ReadString(reader));
        using (var reader = new WireMessage(MessageKind.Hello, new byte[] { 1, 1 }).OpenReader())
            Assert.Throws<InvalidDataException>(() => ProtocolFields.ReadString(reader));
        using (var reader = new WireMessage(MessageKind.Hello, new byte[] { 5, 0, 65 }).OpenReader())
            Assert.Throws<InvalidDataException>(() => ProtocolFields.ReadString(reader));
        using (var reader = new WireMessage(MessageKind.Hello, new byte[] { 1 }).OpenReader())
            Assert.Throws<EndOfStreamException>(() => ProtocolFields.ReadString(reader));
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(65537)]
    public void InvalidFieldLengthDoesNotConsumePayload(int count)
    {
        using (var reader = new WireMessage(MessageKind.Ping, new byte[] { 1, 2 }).OpenReader())
        {
            Assert.Throws<InvalidDataException>(() => ProtocolFields.ReadBytes(reader, count));
            Assert.That(reader.BaseStream.Position, Is.Zero);
        }
    }

    [Test]
    public void BusinessHandlerMustRejectUnreadPayloadFields()
    {
        // 外壳声明长度合法；只有具体业务处理器知道 Ping 示例只读取一个 long。
        var message = WireMessage.Create(MessageKind.Ping, w => { w.Write(123L); w.Write((byte)99); });
        using (var reader = MessageCodec.Decode(MessageCodec.Encode(message)).OpenReader())
        {
            Assert.That(reader.ReadInt64(), Is.EqualTo(123L));
            Assert.Throws<InvalidDataException>(() => ProtocolFields.RequireEnd(reader));
        }
    }
}
