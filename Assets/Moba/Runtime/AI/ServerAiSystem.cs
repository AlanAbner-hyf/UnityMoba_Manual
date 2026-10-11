using System;
using System.Collections.Generic;
using BehaviorDesigner.Runtime;
using Moba.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moba.AI
{
    // Constructed only by the server role. It emits commands; it never writes CombatWorld state.
    public sealed class ServerAiSystem : IDisposable
    {
        private readonly GameObject managerObject;
        private readonly GameObject[] agents = new GameObject[2];
        private readonly BlackboardAdapter[] boards = new BlackboardAdapter[2];
        private readonly BehaviorTree[] trees = new BehaviorTree[2];
        private int lastFrame;
        public ServerAiRunner Runner { get; }
        public IReadOnlyList<AiTrace> LatestTraces { get; private set; } = Array.Empty<AiTrace>();
        public event Action<IReadOnlyList<AiTrace>> Evaluated;

        public ServerAiSystem(CombatWorld world, GameObject agentPrefab)
        {
            if (world == null || agentPrefab == null) throw new ArgumentNullException();
            if (BehaviorManager.instance != null) throw new InvalidOperationException("A server must own exactly one BehaviorManager.");
            managerObject = new GameObject("ServerBehaviorManager");
            Runner = new ServerAiRunner(managerObject.AddComponent<BehaviorManager>());
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    agents[i] = Object.Instantiate(agentPrefab); agents[i].name = "ServerRobot_" + (i + 3);
                    agents[i].SetActive(false);
                    boards[i] = agents[i].GetComponent<BlackboardAdapter>(); trees[i] = agents[i].GetComponent<BehaviorTree>();
                    if (boards[i] == null || trees[i] == null || trees[i].ExternalBehavior == null) throw new InvalidOperationException("Incomplete server AI prefab.");
                    boards[i].Initialize(world, (uint)(i + 3));
                    trees[i].StartWhenEnabled = false; trees[i].RestartWhenComplete = true; trees[i].AsynchronousLoad = false;
                    agents[i].SetActive(true); trees[i].EnableBehavior();
                }
            }
            catch { Dispose(); throw; }
        }

        public ActorCommand[] Evaluate(int frame, bool inputsReady = true)
        {
            if (frame < 1) throw new ArgumentOutOfRangeException(nameof(frame));
            if (frame < lastFrame) throw new InvalidOperationException("AI frame moved backwards.");
            if (!inputsReady || frame == lastFrame) return Array.Empty<ActorCommand>();
            lastFrame = frame;
            if ((frame - 1) % 4 == 0) foreach (var board in boards) board.Prepare(frame);
            if (!Runner.TryEvaluate(frame, true, trees)) return Array.Empty<ActorCommand>();
            var commands = new List<ActorCommand>(2); var traces = new AiTrace[2];
            for (int i = 0; i < boards.Length; i++)
            {
                if (boards[i].HasCommand) commands.Add(new ActorCommand(boards[i].RobotId, boards[i].Command));
                traces[i] = boards[i].Snapshot();
            }
            LatestTraces = Array.AsReadOnly(traces); Evaluated?.Invoke(LatestTraces);
            return commands.ToArray();
        }

        public void Dispose()
        {
            for (int i = 0; i < trees.Length; i++)
            {
                if (trees[i] != null) trees[i].DisableBehavior(false);
                if (agents[i] != null) Object.Destroy(agents[i]);
            }
            if (managerObject != null) Object.Destroy(managerObject);
        }
    }
}
