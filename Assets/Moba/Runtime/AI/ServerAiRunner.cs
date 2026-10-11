using System;
using System.Collections.Generic;
using BehaviorDesigner.Runtime;

namespace Moba.AI
{
    /// <summary>Called by the server only, after the frame's required inputs have arrived.</summary>
    public sealed class ServerAiRunner
    {
        private readonly BehaviorManager manager;
        private readonly int interval;
        private int lastProcessedFrame;

        public int EvaluationCount { get; private set; }

        public ServerAiRunner(BehaviorManager manager, int interval = 4)
        {
            this.manager = manager != null ? manager : throw new ArgumentNullException(nameof(manager));
            if (interval < 1) throw new ArgumentOutOfRangeException(nameof(interval));
            this.interval = interval;
            manager.UpdateInterval = UpdateIntervalType.Manual;
            manager.ExecutionsPerTick = BehaviorManager.ExecutionsPerTickType.NoDuplicates;
        }

        public bool TryEvaluate(int frame, bool inputsReady, IReadOnlyList<BehaviorTree> orderedTrees)
        {
            if (frame < 1) throw new ArgumentOutOfRangeException(nameof(frame));
            if (frame < lastProcessedFrame) throw new InvalidOperationException("AI frame moved backwards.");
            if (orderedTrees == null) throw new ArgumentNullException(nameof(orderedTrees));
            if (!inputsReady || frame == lastProcessedFrame) return false;
            if (manager.UpdateInterval != UpdateIntervalType.Manual)
                throw new InvalidOperationException("Server behavior trees must use manual scheduling.");

            lastProcessedFrame = frame;
            if ((frame - 1) % interval != 0) return false;
            for (var i = 0; i < orderedTrees.Count; ++i)
            {
                var tree = orderedTrees[i];
                if (tree == null || !manager.IsBehaviorEnabled(tree))
                    throw new InvalidOperationException("A scheduled behavior tree is missing or disabled.");
                manager.Tick(tree);
            }
            EvaluationCount++;
            return true;
        }
    }
}
