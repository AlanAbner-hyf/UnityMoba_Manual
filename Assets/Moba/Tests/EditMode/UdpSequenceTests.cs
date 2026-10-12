using System.IO;
using Moba.Networking;
using NUnit.Framework;

public sealed class UdpSequenceTests
{
    [TestCase(0u, 0u, 0)]
    [TestCase(42u, 42u, 0)]
    [TestCase(uint.MaxValue, uint.MaxValue, 0)]
    [TestCase(11u, 10u, 1)]
    [TestCase(10u, 11u, -1)]
    [TestCase(0u, uint.MaxValue, 1)]
    [TestCase(uint.MaxValue, 0u, -1)]
    [TestCase(1u, 4294967294u, 1)]
    [TestCase(4294967294u, 1u, -1)]
    [TestCase(0x7FFFFFFFu, 0u, 1)]
    [TestCase(0x80000001u, 0u, -1)]
    public void CompareRecognizesEqualityOrderAndWrap(uint a, uint b, int expected)
    {
        Assert.That(Sequence32.Compare(a, b), Is.EqualTo(expected));
        Assert.That(Sequence32.Compare(b, a), Is.EqualTo(-expected));
    }

    [TestCase(0u, 0x80000000u)]
    [TestCase(123u, 0x8000007Bu)]
    [TestCase(uint.MaxValue, 0x7FFFFFFFu)]
    public void ExactHalfRangeIsAmbiguousInBothDirections(uint a, uint b)
    {
        Assert.That(Assert.Throws<InvalidDataException>(() => Sequence32.Compare(a, b)).Message,
            Is.EqualTo("AmbiguousSequence"));
        Assert.That(Assert.Throws<InvalidDataException>(() => Sequence32.Compare(b, a)).Message,
            Is.EqualTo("AmbiguousSequence"));
    }

    [TestCase(42u, 42u, 0u)]
    [TestCase(13u, 10u, 3u)]
    [TestCase(0u, uint.MaxValue, 1u)]
    [TestCase(1u, 4294967294u, 3u)]
    [TestCase(uint.MaxValue, 0u, uint.MaxValue)]
    [TestCase(0x80000000u, 0u, 0x80000000u)]
    public void DistanceIsForwardModuloDistanceNotAbsoluteDistance(uint a, uint b, uint expected)
    {
        Assert.That(Sequence32.Distance(a, b), Is.EqualTo(expected));
    }

    [TestCase(100u)]
    [TestCase(4294967293u)]
    public void ReceiveWindowDistancesRemainCorrectAcrossWrap(uint baseline)
    {
        // 使用更宽的整数构造期望序号，不靠被测函数计算预期结果。
        for (uint distance = 1; distance <= UdpProtocol.Window + 1; distance++)
        {
            uint sequence = (uint)(((ulong)baseline + distance) % 4294967296UL);
            Assert.That(Sequence32.Compare(sequence, baseline), Is.EqualTo(1));
            Assert.That(Sequence32.Distance(sequence, baseline), Is.EqualTo(distance));
            Assert.That(Sequence32.Distance(sequence, baseline) <= UdpProtocol.Window,
                Is.EqualTo(distance <= 64), "第 64 个包在窗口内，第 65 个在窗口外。");
        }
        uint previous = (uint)(((ulong)baseline + 4294967295UL) % 4294967296UL);
        Assert.That(Sequence32.Compare(previous, baseline), Is.EqualTo(-1));
    }
}
