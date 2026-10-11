using System;
using System.Collections.Generic;

namespace Moba.Core
{
    public readonly struct AiNodeTrace
    {
        public readonly int NodeId;
        public readonly string Name, Phase;
        public AiNodeTrace(int nodeId, string name, string phase) { NodeId = nodeId; Name = name; Phase = phase; }
    }

    // Server observations only. These fields do not influence client simulation or state hashes.
    public sealed class AiTrace
    {
        public int BasedOnFrame { get; }
        public int CommandFrame { get; }
        public uint RobotId { get; }
        public string Decision { get; }
        public uint TargetId { get; }
        public bool Dead { get; }
        public bool Retreat { get; }
        public bool InRecoveryZone { get; }
        public bool SkillReady { get; }
        public bool EnemyInSkillRange { get; }
        public int Hp { get; }
        public IReadOnlyList<AiNodeTrace> Nodes { get; }
        public AiTrace(int basedOnFrame, int commandFrame, uint robotId, string decision, uint targetId,
            bool dead, bool retreat, bool inRecoveryZone, bool skillReady, bool enemyInSkillRange, int hp, AiNodeTrace[] nodes)
        {
            if (nodes == null || nodes.Length > 32) throw new ArgumentException("AI trace node limit.");
            BasedOnFrame = basedOnFrame; CommandFrame = commandFrame; RobotId = robotId; Decision = decision; TargetId = targetId;
            Dead = dead; Retreat = retreat; InRecoveryZone = inRecoveryZone; SkillReady = skillReady;
            EnemyInSkillRange = enemyInSkillRange; Hp = hp; Nodes = Array.AsReadOnly((AiNodeTrace[])nodes.Clone());
        }
    }
}