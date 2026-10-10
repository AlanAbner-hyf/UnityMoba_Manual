// 完整文件：Assets/Moba/Runtime/Core/CombatCommand.cs
using System;
using System.Collections.Generic;

namespace Moba.Core
{
    public enum CommandKind : byte { NoOp = 0, MoveTo = 1, Attack = 2, CastArea = 3 }

    public readonly struct CombatCommand : IEquatable<CombatCommand>
    {
        public readonly CommandKind Kind;
        public readonly Int2 Point;
        public readonly uint TargetId;
        public CombatCommand(CommandKind kind, Int2 point = default, uint targetId = 0)
        { Kind = kind; Point = point; TargetId = targetId; }
        public static CombatCommand NoOp => default;
        public static CombatCommand MoveTo(Int2 point) => new CombatCommand(CommandKind.MoveTo, point);
        public bool Equals(CombatCommand other) => Kind == other.Kind && Point == other.Point && TargetId == other.TargetId;
        public override bool Equals(object obj) => obj is CombatCommand other && Equals(other);
        public override int GetHashCode() { unchecked { return ((int)Kind * 397 ^ Point.GetHashCode()) * 397 ^ (int)TargetId; } }

        public void Validate(BattleConfig config)
        {
            if ((byte)Kind > (byte)CommandKind.CastArea) throw new ArgumentException("Unknown command.");
            if (Kind == CommandKind.MoveTo)
            {
                if (!config.Contains(Point) || TargetId != 0) throw new ArgumentException("Invalid move parameters.");
            }
            else if (Point != default || (Kind == CommandKind.Attack ? TargetId == 0 : TargetId != 0))
                throw new ArgumentException("Invalid command parameters.");
        }
    }

    public readonly struct ActorCommand
    {
        public readonly uint ActorId;
        public readonly CombatCommand Command;
        public ActorCommand(uint actorId, CombatCommand command) { ActorId = actorId; Command = command; }
    }

    public sealed class FrameCommands
    {
        public int FrameId { get; }
        public CombatCommand BluePlayer { get; }
        public CombatCommand RedPlayer { get; }
        public IReadOnlyList<ActorCommand> Robots { get; }
        public FrameCommands(int frameId, CombatCommand bluePlayer, CombatCommand redPlayer, params ActorCommand[] robots)
        {
            if (frameId < 1) throw new ArgumentOutOfRangeException(nameof(frameId));
            if (robots == null || robots.Length > 2) throw new ArgumentException("At most two robot inputs.");
            var copy = (ActorCommand[])robots.Clone();
            Array.Sort(copy, (a, b) => a.ActorId.CompareTo(b.ActorId));
            for (int i = 0; i < copy.Length; i++)
                if ((copy[i].ActorId != 3 && copy[i].ActorId != 4) || (i > 0 && copy[i - 1].ActorId == copy[i].ActorId))
                    throw new ArgumentException("Robot actor must be 3 or 4 and unique.");
            FrameId = frameId; BluePlayer = bluePlayer; RedPlayer = redPlayer;
            Robots = Array.AsReadOnly(copy);
        }
    }
}