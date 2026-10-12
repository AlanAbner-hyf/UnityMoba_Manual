using System;
using System.IO;
using System.Linq;
using System.Text;
using Moba.Networking;
using NUnit.Framework;

public sealed class UdpPacketCodecTests
{
    private static readonly Guid Connection = new Guid("00112233-4455-6677-8899-aabbccddeeff");

    [Test]
    public void CrcMatchesKnownVectorAndChecksSliceArguments()
    {
        var bytes = Encoding.ASCII.GetBytes("x123456789y");
        Assert.That(UdpPacketCodec.Crc(bytes, 1, 9), Is.EqualTo(0xCBF43926u));
        Assert.That(UdpPacketCodec.Crc(Encoding.ASCII.GetBytes("123456789")), Is.EqualTo(0xCBF43926u));
        Assert.That(UdpPacketCodec.Crc(Array.Empty<byte>()), Is.Zero);
        Assert.That(UdpPacketCodec.Crc(bytes, bytes.Length), Is.Zero);
        Assert.Throws<ArgumentNullException>(() => UdpPacketCodec.Crc(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => UdpPacketCodec.Crc(bytes, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => UdpPacketCodec.Crc(bytes, bytes.Length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => UdpPacketCodec.Crc(bytes, 0, -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => UdpPacketCodec.Crc(bytes, 1, int.MaxValue));
    }

    [Test]
    public void EntireDatagramMatchesIndependentLittleEndianGoldenBytes()
    {
        var packet = Data(7);
        packet.Sequence = 0x11223344;
        packet.AckValid = true;
        packet.AckBase = 0x55667788;
        packet.AckBits = 0x8000000000000001;
        packet.Message = 0x99AABBCC;
        packet.Payload = new byte[] { 3, 0, 0, 0, 4, 0, 1 }; // 空 payload 的业务 Hello。

        // 独立使用 Python struct.pack('<IBBH16sIIQIHHIHHI')、UUID.bytes_le、zlib.crc32 生成。
        const string hex = "4d4455310108010033221100554477668899aabbccddeeff44332211" +
            "887766550100000000000080ccbbaa99000001000700000007000000d13fc59603000000040001";
        var expected = Enumerable.Range(0, hex.Length / 2)
            .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();

        CollectionAssert.AreEqual(expected, UdpPacketCodec.Encode(packet));
        Assert.That(UdpPacketCodec.Crc(expected, packet: true), Is.EqualTo(0x96C53FD1u));
        var decoded = UdpPacketCodec.Decode(expected);
        Assert.That(decoded.Kind, Is.EqualTo(UdpKind.Data));
        Assert.That(decoded.Connection, Is.EqualTo(Connection));
        Assert.That(decoded.Sequence, Is.EqualTo(0x11223344u));
        Assert.That(decoded.AckValid, Is.True);
        Assert.That(decoded.AckBase, Is.EqualTo(0x55667788u));
        Assert.That(decoded.AckBits, Is.EqualTo(0x8000000000000001ul));
        Assert.That(decoded.Message, Is.EqualTo(0x99AABBCCu));
        Assert.That(decoded.Index, Is.Zero);
        Assert.That(decoded.Count, Is.EqualTo(1));
        Assert.That(decoded.Total, Is.EqualTo(7));
        CollectionAssert.AreEqual(packet.Payload, decoded.Payload);
    }

    [Test]
    public void ConstantsAndPacketKindIdsMatchProtocolContract()
    {
        Assert.That(UdpProtocol.Header, Is.EqualTo(60));
        Assert.That(UdpProtocol.Mtu, Is.EqualTo(1200));
        Assert.That(UdpProtocol.Fragment, Is.EqualTo(1140));
        Assert.That(UdpProtocol.MaxMessage, Is.EqualTo(MessageCodec.MaxBodySize + 4));
        Assert.That(UdpProtocol.MaxFragments, Is.EqualTo(58));
        Assert.That(UdpProtocol.Window, Is.EqualTo(64));
        var kinds = new[] { UdpKind.ConnectRequest, UdpKind.Challenge, UdpKind.ConnectResponse,
            UdpKind.Accept, UdpKind.Confirm, UdpKind.Ready, UdpKind.HandshakeReject,
            UdpKind.Data, UdpKind.Ack, UdpKind.Trace, UdpKind.Close };
        CollectionAssert.AreEqual(Enumerable.Range(1, 11), kinds.Select(k => (int)k));
    }

    [TestCase(UdpKind.ConnectRequest)]
    [TestCase(UdpKind.Challenge)]
    [TestCase(UdpKind.ConnectResponse)]
    [TestCase(UdpKind.Accept)]
    [TestCase(UdpKind.Confirm)]
    [TestCase(UdpKind.Ready)]
    [TestCase(UdpKind.HandshakeReject)]
    [TestCase(UdpKind.Data)]
    [TestCase(UdpKind.Ack)]
    [TestCase(UdpKind.Trace)]
    [TestCase(UdpKind.Close)]
    public void EveryKindRoundTripsItsLegalEnvelope(UdpKind kind)
    {
        UdpPacket packet;
        if (kind == UdpKind.Data || kind == UdpKind.Trace)
        {
            packet = Data(7);
            packet.Kind = kind;
            if (kind == UdpKind.Trace) packet.Sequence = 0;
        }
        else
        {
            bool emptyId = kind == UdpKind.ConnectRequest || kind == UdpKind.Challenge ||
                kind == UdpKind.ConnectResponse || kind == UdpKind.HandshakeReject;
            packet = new UdpPacket
            {
                Kind = kind, Connection = emptyId ? Guid.Empty : Connection,
                Payload = new byte[kind <= UdpKind.Accept || kind == UdpKind.HandshakeReject ? 96 : 0]
            };
        }
        byte[] bytes = UdpPacketCodec.Encode(packet);
        Assert.That(bytes.Length, Is.EqualTo(60 + packet.Payload.Length));
        var decoded = UdpPacketCodec.Decode(bytes);
        Assert.That(decoded.Kind, Is.EqualTo(kind));
        Assert.That(decoded.Connection, Is.EqualTo(packet.Connection));
        CollectionAssert.AreEqual(bytes, UdpPacketCodec.Encode(decoded));
    }

    [TestCase(7)]
    [TestCase(1140)]
    [TestCase(1141)]
    [TestCase(65540)]
    public void AllFragmentsAtMessageBoundariesHaveExactSizes(int total)
    {
        int count = (total + 1139) / 1140;
        int sum = 0;
        for (int index = 0; index < count; index++)
        {
            var packet = Data(total, index);
            byte[] bytes = UdpPacketCodec.Encode(packet);
            var decoded = UdpPacketCodec.Decode(bytes);
            int expectedSize = Math.Min(1140, total - index * 1140);
            Assert.That(bytes.Length, Is.EqualTo(60 + expectedSize));
            Assert.That(bytes.Length, Is.LessThanOrEqualTo(1200));
            Assert.That(decoded.Total, Is.EqualTo(total));
            Assert.That(decoded.Count, Is.EqualTo(count));
            Assert.That(decoded.Index, Is.EqualTo(index));
            CollectionAssert.AreEqual(packet.Payload, decoded.Payload);
            sum += decoded.Payload.Length;
        }
        Assert.That(sum, Is.EqualTo(total));
    }

    [Test]
    public void OneFragmentCarriesAnExistingBusinessMessageWithoutChangingItsEncoding()
    {
        byte[] messageBytes = MessageCodec.Encode(WireMessage.Create(MessageKind.Input, writer => writer.Write(123)));
        var packet = Data(messageBytes.Length);
        packet.Payload = messageBytes;
        var decodedPacket = UdpPacketCodec.Decode(UdpPacketCodec.Encode(packet));
        var message = MessageCodec.Decode(decodedPacket.Payload);
        Assert.That(message.Kind, Is.EqualTo(MessageKind.Input));
        using (var reader = message.OpenReader())
        {
            Assert.That(reader.ReadInt32(), Is.EqualTo(123));
            ProtocolFields.RequireEnd(reader);
        }
    }

    [Test]
    public void CorruptionAtEveryByteIsRejectedAndDecodingDoesNotModifyInput()
    {
        byte[] bytes = UdpPacketCodec.Encode(Data(1140));
        byte[] original = (byte[])bytes.Clone();
        var decoded = UdpPacketCodec.Decode(bytes);
        decoded.Payload[0] ^= 1;
        CollectionAssert.AreEqual(original, bytes);
        for (int i = 0; i < bytes.Length; i++)
        {
            var bad = (byte[])bytes.Clone();
            bad[i] ^= 1;
            Assert.Throws<InvalidDataException>(() => UdpPacketCodec.Decode(bad), "byte " + i);
        }
        CollectionAssert.AreEqual(original, bytes);
    }

    [Test]
    public void AllTruncationsAndTrailingBytesAreRejected()
    {
        byte[] bytes = UdpPacketCodec.Encode(Data(7));
        Assert.Throws<InvalidDataException>(() => UdpPacketCodec.Decode(null));
        Assert.Throws<InvalidDataException>(() => UdpPacketCodec.Decode(new byte[1201]));
        for (int length = 0; length < bytes.Length; length++)
        {
            byte[] truncated = bytes.Take(length).ToArray();
            Assert.Throws<InvalidDataException>(() => UdpPacketCodec.Decode(truncated), "length " + length);
        }
        // 重算 CRC 后仍须拒绝实际长度与 PayloadBytes 不符、以及两个数据报拼接。
        foreach (byte[] trailing in new[] { bytes.Concat(new byte[1]).ToArray(), bytes.Concat(bytes).ToArray() })
        {
            Seal(trailing);
            Assert.That(Assert.Throws<InvalidDataException>(() => UdpPacketCodec.Decode(trailing)).Message,
                Is.EqualTo("UdpPayloadLength"));
        }
    }

    [TestCase(0, 0u, 4, "UdpVersionMismatch")]
    [TestCase(4, 2u, 1, "UdpVersionMismatch")]
    [TestCase(5, 0u, 1, "UdpKindOrPayload")]
    [TestCase(5, 12u, 1, "UdpKindOrPayload")]
    [TestCase(6, 2u, 2, "UdpFlags")]
    [TestCase(6, 32768u, 2, "UdpFlags")]
    [TestCase(28, 1u, 4, "UnusedAck")]
    [TestCase(32, 1u, 4, "UnusedAck")]
    [TestCase(44, 1u, 2, "FragmentMetadata")]
    [TestCase(46, 0u, 2, "FragmentMetadata")]
    [TestCase(46, 2u, 2, "FragmentMetadata")]
    [TestCase(48, 0u, 4, "FragmentMetadata")]
    [TestCase(48, 6u, 4, "FragmentMetadata")]
    [TestCase(48, 65541u, 4, "UdpMessageLength")]
    [TestCase(48, uint.MaxValue, 4, "UdpMessageLength")]
    [TestCase(52, 6u, 2, "UdpPayloadLength")]
    [TestCase(54, 1u, 2, "UdpChecksumOrReserved")]
    public void InvalidFieldsAreRejectedEvenWithCorrectCrc(int offset, uint value, int width, string reason)
    {
        AssertBadWire(Data(7), bytes => WriteLittleEndian(bytes, offset, value, width), reason);
    }

    [Test]
    public void IdentityAckAndUnusedFieldsFollowThePacketKind()
    {
        AssertBadWire(Data(7), bytes => Array.Clear(bytes, 8, 16), "UdpIdentity");
        var request = new UdpPacket { Kind = UdpKind.ConnectRequest, Payload = new byte[96] };
        AssertBadWire(request, bytes => bytes[8] = 1, "UdpIdentity");
        AssertBadWire(request, bytes => bytes[6] = 1, "HandshakeAck");

        var ack = new UdpPacket { Kind = UdpKind.Ack, Connection = Connection };
        AssertBadWire(ack, bytes => bytes[24] = 1, "UnusedSequence");
        foreach (int offset in new[] { 40, 44, 46, 48 })
            AssertBadWire(ack, bytes => bytes[offset] = 1, "UnusedFragmentFields");

        var trace = Data(7);
        trace.Kind = UdpKind.Trace;
        trace.Sequence = 0;
        AssertBadWire(trace, bytes => bytes[24] = 1, "UnusedSequence");

        // Data/Close 的序号 0 可以是环绕后的正常值；单包编解码不能禁止它。
        var data = Data(7);
        data.Sequence = 0;
        Assert.That(UdpPacketCodec.Decode(UdpPacketCodec.Encode(data)).Sequence, Is.Zero);
        var close = new UdpPacket { Kind = UdpKind.Close, Connection = Connection, AckValid = true };
        Assert.That(UdpPacketCodec.Decode(UdpPacketCodec.Encode(close)).Sequence, Is.Zero);
    }

    [Test]
    public void NonFinalAndFinalFragmentLengthsCannotBeInvented()
    {
        AssertBadWire(Data(1141), bytes => WriteLittleEndian(bytes, 48, 2281, 4), "FragmentMetadata");
        // 改 total 后 count 仍正确，但原末片 1 字节已经不能匹配新的末片 2 字节。
        AssertBadWire(Data(1141, 1), bytes => WriteLittleEndian(bytes, 48, 1142, 4), "FragmentLength");
        // 首片伪装成只有 7 字节；CRC 和载荷长度都正确，仍不满足非末片 1140 字节规则。
        AssertBadWire(Data(7), bytes =>
        {
            WriteLittleEndian(bytes, 46, 2, 2);
            WriteLittleEndian(bytes, 48, 1141, 4);
        }, "FragmentLength");
    }

    [Test]
    public void ControlPayloadSizesAreExactEvenWithValidCrc()
    {
        foreach (var kind in new[] { UdpKind.ConnectRequest, UdpKind.Challenge, UdpKind.ConnectResponse,
            UdpKind.Accept, UdpKind.HandshakeReject, UdpKind.Confirm, UdpKind.Ready, UdpKind.Ack, UdpKind.Close })
        {
            bool emptyId = kind == UdpKind.ConnectRequest || kind == UdpKind.Challenge ||
                kind == UdpKind.ConnectResponse || kind == UdpKind.HandshakeReject;
            int size = kind <= UdpKind.Accept || kind == UdpKind.HandshakeReject ? 96 : 0;
            var packet = new UdpPacket { Kind = kind, Connection = emptyId ? Guid.Empty : Connection, Payload = new byte[size] };
            foreach (int wrongSize in size == 0 ? new[] { 1 } : new[] { 95, 97 })
            {
                var bytes = new byte[60 + wrongSize];
                Array.Copy(UdpPacketCodec.Encode(packet), bytes, 60);
                WriteLittleEndian(bytes, 52, (uint)wrongSize, 2);
                Seal(bytes);
                Assert.That(Assert.Throws<InvalidDataException>(() => UdpPacketCodec.Decode(bytes)).Message,
                    Is.EqualTo("ControlPayload"), kind.ToString());
            }
        }
    }

    [Test]
    public void EncoderAlsoRejectsInvalidLocalPackets()
    {
        Assert.Throws<InvalidDataException>(() => UdpPacketCodec.Encode(null));
        Action<UdpPacket>[] invalidEdits =
        {
            p => p.Kind = (UdpKind)255, p => p.Payload = null,
            p => p.Payload = new byte[1141], p => p.Payload = new byte[6],
            p => p.Connection = Guid.Empty, p => p.AckBase = 1, p => p.AckBits = 1,
            p => p.Total = -1, p => p.Total = 65541, p => p.Count = 0, p => p.Index = 1,
            p => p.Kind = UdpKind.Ack
        };
        foreach (var edit in invalidEdits)
        {
            var packet = Data(7);
            edit(packet);
            Assert.Throws<InvalidDataException>(() => UdpPacketCodec.Encode(packet));
        }
    }

    private static UdpPacket Data(int total, int index = 0)
    {
        int size = Math.Min(1140, total - index * 1140);
        return new UdpPacket
        {
            Kind = UdpKind.Data, Connection = Connection, Sequence = (uint)(index + 1), Message = 1,
            Index = (ushort)index, Count = (ushort)((total + 1139) / 1140), Total = total,
            Payload = Enumerable.Range(0, size).Select(i => (byte)(i * 17)).ToArray()
        };
    }

    private static void AssertBadWire(UdpPacket original, Action<byte[]> change, string reason)
    {
        byte[] bytes = UdpPacketCodec.Encode(original);
        change(bytes);
        Seal(bytes); // 绕过损坏检测，专门验证字段语义。
        Assert.That(Assert.Throws<InvalidDataException>(() => UdpPacketCodec.Decode(bytes)).Message, Is.EqualTo(reason));
    }

    private static void Seal(byte[] bytes) => WriteLittleEndian(bytes, 56, UdpPacketCodec.Crc(bytes, packet: true), 4);

    private static void WriteLittleEndian(byte[] bytes, int offset, uint value, int width)
    {
        for (int i = 0; i < width; i++) bytes[offset + i] = (byte)(value >> (8 * i));
    }
}
