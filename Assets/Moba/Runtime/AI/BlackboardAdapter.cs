using System;
using System.Collections.Generic;
using Moba.Core;
using UnityEngine;

namespace Moba.AI
{
    public sealed class BlackboardAdapter : MonoBehaviour
    {
        public const int RetreatPercent = 30, ReturnPercent = 70, AcquireRadius = 6000, ChaseRadius = 8000;
        private readonly List<AiNodeTrace> nodes = new List<AiNodeTrace>(32);
        public CombatWorld World { get; private set; }
        public uint RobotId { get; private set; }
        public EntityState Actor => World.FindEntity(RobotId);
        public EntityState Target { get; private set; }
        public int CommandFrame { get; private set; }
        public bool RetreatLatched { get; private set; }
        public bool InRecoveryZone { get; private set; }
        public bool SkillReady { get; private set; }
        public bool EnemyInSkillRange { get; private set; }
        public bool HasCommand { get; private set; }
        public CombatCommand Command { get; private set; }
        public string Decision { get; set; } = "NotEvaluated";

        public void Initialize(CombatWorld world, uint robotId)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            if (robotId != 3 && robotId != 4) throw new ArgumentOutOfRangeException(nameof(robotId));
            RobotId = robotId;
        }

        public void Prepare(int frame)
        {
            if (World.State.Frame != frame - 1) throw new InvalidOperationException("AI must read S(n-1).");
            CommandFrame = frame; HasCommand = false; Command = default; nodes.Clear();
            var actor = Actor;
            if (!actor.Alive) RetreatLatched = false;
            else if ((long)actor.Hp * 100 <= (long)World.Config.HeroHp * RetreatPercent) RetreatLatched = true;
            else if ((long)actor.Hp * 100 >= (long)World.Config.HeroHp * ReturnPercent) RetreatLatched = false;
            InRecoveryZone = IntegerMath.WithinRange(actor.Position, World.SpawnFor(actor.Team), World.Config.Lane.RecoveryRadius);
            SkillReady = actor.Alive && frame >= actor.SkillReadyFrame;
            EnemyInSkillRange = TargetSelector.Find(World, actor, World.Config.SkillRadius, TargetPriority.Robot) != null;
            Target = TargetSelector.Find(World, actor, AcquireRadius, TargetPriority.Robot);
            var current = actor.Intent.Kind == CommandKind.Attack ? World.FindEntity(actor.Intent.TargetId) : null;
            if (actor.Alive && current != null && current.Alive && current.Team != actor.Team &&
                IntegerMath.WithinRange(actor.Position, current.Position, ChaseRadius) &&
                (Target == null || TargetSelector.Rank(current, TargetPriority.Robot) <= TargetSelector.Rank(Target, TargetPriority.Robot))) Target = current;
        }

        public void Submit(CombatCommand command)
        {
            if (HasCommand || !Actor.Alive || command.Kind == CommandKind.NoOp || command.Equals(Actor.Intent)) return;
            command.Validate(World.Config);
            Command = command; HasCommand = true;
        }

        public void Record(int nodeId, string name, string phase)
        {
            if (nodes.Count < 32) nodes.Add(new AiNodeTrace(nodeId, name, phase));
        }

        public AiTrace Snapshot() => new AiTrace(World.State.Frame, CommandFrame, RobotId, Decision, Target?.EntityId ?? 0,
            !Actor.Alive, RetreatLatched, InRecoveryZone, SkillReady, EnemyInSkillRange, Actor.Hp, nodes.ToArray());
    }
}