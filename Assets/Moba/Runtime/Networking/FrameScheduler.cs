using System;

namespace Moba.Networking
{
    // Wall-clock scheduling only. CombatWorld never reads this clock.
    public sealed class FrameScheduler
    {
        private readonly double period;
        public double NextDue { get; private set; }
        public FrameScheduler(int frequency)
        { if (frequency < 1) throw new ArgumentOutOfRangeException(nameof(frequency)); period = 1.0 / frequency; }
        public void Start(double now) => NextDue = now + period;
        public bool IsDue(double now) => now >= NextDue;
        public void Advanced(double now, bool resumedFromInputWait)
        {
            // Preserve the healthy cadence across render-frame quantization. After waiting or
            // a whole missed period, reset the deadline instead of replaying wall-clock debt.
            NextDue = resumedFromInputWait || now - NextDue >= period ? now + period : NextDue + period;
        }
    }
}