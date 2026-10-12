using Moba.Networking;
using NUnit.Framework;

public sealed class TransportStatsTests
{
    [Test]
    public void CopyRemainsIndependentWhenLiveStatsChange()
    {
        var stats = new TransportStats { sentBytes = 1000, sendBytes = 200, fault = "First" };
        var snapshot = stats.Copy();
        stats.sentBytes = 1500;
        stats.sendBytes = 0;
        stats.fault = "Second";

        Assert.That(snapshot, Is.Not.SameAs(stats));
        Assert.That(snapshot.sentBytes, Is.EqualTo(1000));
        Assert.That(snapshot.sendBytes, Is.EqualTo(200));
        Assert.That(snapshot.fault, Is.EqualTo("First"));
        snapshot.receivedBytes = 50;
        Assert.That(stats.receivedBytes, Is.Zero);
    }

    [Test]
    public void SinceSubtractsCountersButPreservesCurrentGaugesAndLifetimePeaks()
    {
        var stats = new TransportStats
        {
            sentBytes = 1000, sentDatagrams = 10, retransmissions = 2,
            injectedDrops = 1, submittedDatagrams = 11,
            sendBytes = 200, receiveBytes = 100, peakSendBytes = 400,
            peakReceiveBytes = 300, maxDatagramBytes = 800,
            rttMs = 80, srttMs = 90, rtoMs = 500, congestionWindow = 8
        };
        var baseline = stats.Copy();
        stats.sentBytes += 600;
        stats.sentDatagrams += 3;
        stats.retransmissions += 1;
        stats.injectedDrops += 2;
        stats.submittedDatagrams += 5;
        stats.sendBytes = 50;
        stats.receiveBytes = 20;
        stats.peakSendBytes = 500;
        stats.maxDatagramBytes = 1200;
        stats.rttMs = 40;
        stats.srttMs = 70;
        stats.rtoMs = 300;
        stats.congestionWindow = 10;
        stats.fault = "ExampleFailure";

        var delta = stats.Since(baseline);
        Assert.That(delta.sentBytes, Is.EqualTo(600));
        Assert.That(delta.sentDatagrams, Is.EqualTo(3));
        Assert.That(delta.retransmissions, Is.EqualTo(1));
        Assert.That(delta.injectedDrops, Is.EqualTo(2));
        Assert.That(delta.submittedDatagrams, Is.EqualTo(5));
        Assert.That(delta.receivedBytes, Is.Zero);
        Assert.That(delta.sendBytes, Is.EqualTo(50));
        Assert.That(delta.receiveBytes, Is.EqualTo(20));
        Assert.That(delta.peakSendBytes, Is.EqualTo(500));
        Assert.That(delta.peakReceiveBytes, Is.EqualTo(300));
        Assert.That(delta.maxDatagramBytes, Is.EqualTo(1200));
        Assert.That(delta.rttMs, Is.EqualTo(40));
        Assert.That(delta.srttMs, Is.EqualTo(70));
        Assert.That(delta.rtoMs, Is.EqualTo(300));
        Assert.That(delta.congestionWindow, Is.EqualTo(10));
        Assert.That(delta.fault, Is.EqualTo("ExampleFailure"));
        Assert.That(stats.sentBytes, Is.EqualTo(1600));
        Assert.That(baseline.sentBytes, Is.EqualTo(1000));
        Assert.That(baseline.sendBytes, Is.EqualTo(200));
    }

    [Test]
    public void SinceNullReturnsIndependentLifetimeSnapshot()
    {
        var stats = new TransportStats { receivedBytes = 300, duplicates = 4 };
        var snapshot = stats.Since(null);
        Assert.That(snapshot, Is.Not.SameAs(stats));
        Assert.That(snapshot.receivedBytes, Is.EqualTo(300));
        Assert.That(snapshot.duplicates, Is.EqualTo(4));
        snapshot.duplicates = 0;
        Assert.That(stats.duplicates, Is.EqualTo(4));
    }
}
