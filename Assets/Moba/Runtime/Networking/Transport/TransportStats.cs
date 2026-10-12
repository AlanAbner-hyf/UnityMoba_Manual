using System;

namespace Moba.Networking
{
    /// <summary>
    /// 传输层观察数据，由后续收发/可靠性实现维护，不属于确定性战斗状态。
    /// 字段名称与参考传输实现保持一致，方便继续完成步骤 07。
    /// </summary>
    [Serializable]
    public sealed class TransportStats
    {
        public string transport = "handwritten-udp-v1", fault;

        // 累计计数：实际数据报与应用层数据报字节；不包含 IP/UDP 系统头开销。
        public long sentDatagrams, receivedDatagrams, sentBytes, receivedBytes;
        public long firstSends, retransmissions, ackOnly;
        public long duplicates, outOfOrder, invalid, outsideWindow;
        public long tracesDropped, tracesCoalesced;

        // 开发故障注入：提交给数据报层不等于最终实际发出。
        public long injectedDrops, injectedCopies, submittedDatagrams;

        // 当前缓冲占用与连接生命周期峰值；sendBytes 不同于累计 sentBytes。
        public int sendBytes, receiveBytes, peakSendBytes, peakReceiveBytes, maxDatagramBytes;

        // 单位为毫秒；-1 表示尚无 RTT 样本。这里只存数值，不执行估计/拥塞算法。
        public double rttMs = -1, srttMs = -1, rtoMs = 500, congestionWindow = 8;

        // 当前只有值类型和不可变 string，浅复制即可形成独立快照。
        public TransportStats Copy() => (TransportStats)MemberwiseClone();

        /// <summary>
        /// 对同一统计实例生命周期内的历史快照计算累计计数增量。
        /// 当前量、峰值、RTT、故障原因保留当前值；不修改本对象或 baseline。
        /// </summary>
        public TransportStats Since(TransportStats baseline)
        {
            var delta = Copy();
            if (baseline == null) return delta;
            delta.sentDatagrams -= baseline.sentDatagrams;
            delta.receivedDatagrams -= baseline.receivedDatagrams;
            delta.sentBytes -= baseline.sentBytes;
            delta.receivedBytes -= baseline.receivedBytes;
            delta.firstSends -= baseline.firstSends;
            delta.retransmissions -= baseline.retransmissions;
            delta.ackOnly -= baseline.ackOnly;
            delta.duplicates -= baseline.duplicates;
            delta.outOfOrder -= baseline.outOfOrder;
            delta.invalid -= baseline.invalid;
            delta.outsideWindow -= baseline.outsideWindow;
            delta.tracesDropped -= baseline.tracesDropped;
            delta.tracesCoalesced -= baseline.tracesCoalesced;
            delta.injectedDrops -= baseline.injectedDrops;
            delta.injectedCopies -= baseline.injectedCopies;
            delta.submittedDatagrams -= baseline.submittedDatagrams;
            return delta;
        }
    }

    /// <summary>
    /// 业务层使用的完整消息连接；具体传输实现负责分片、重组和排队。
    /// 接口不提供推进或修改 CombatWorld 的方法。
    /// </summary>
    public interface IMessageConnection : IDisposable
    {
        /// <summary>
        /// 尝试接纳一条完整消息。true 仅代表已被传输层接纳，不能作为对端收到或执行的确认。
        /// false 表示未接纳，调用方应结合连接状态处理，不能当作已经发送成功。
        /// </summary>
        bool TrySend(WireMessage message);

        /// <summary>
        /// 尝试取出一条已接收的完整消息；无消息时立即返回 false，不阻塞主线程等待。
        /// 分片和 UDP ACK 不通过此方法交付给业务层。
        /// </summary>
        bool TryReceive(out WireMessage message);

        /// <summary>
        /// 请求排空已排队的可靠业务消息后关闭，停止接纳新的业务消息。
        /// 需要后续传输驱动继续运行；不保证调用返回时已关闭，也不保证永远不会超时。
        /// </summary>
        void CloseAfterSend();

        /// <summary>立即终止连接，不等待发送队列排空；可记录关闭原因。</summary>
        void Close(string reason = null);

        /// <summary>连接已经关闭；接收队列仍可能保存关闭前收到的完整消息。</summary>
        bool IsClosed { get; }

        /// <summary>是否还有已接收、尚未交付给业务层的完整消息。</summary>
        bool HasPendingIncoming { get; }

        /// <summary>实现记录的故障/关闭原因；没有原因时可为 null。</summary>
        string Fault { get; }

        /// <summary>累计实际发送的数据报字节数，口径由传输实现维护。</summary>
        long SentBytes { get; }

        /// <summary>累计实际接收的数据报字节数，口径由传输实现维护。</summary>
        long ReceivedBytes { get; }

        /// <summary>当前统计；业务观察者使用 Copy 保存快照，不应修改计数。</summary>
        TransportStats Stats { get; }
    }
}
