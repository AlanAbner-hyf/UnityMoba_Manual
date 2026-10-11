using System;
using System.Collections.Generic;
using System.IO;
using Moba.Core;

namespace Moba.Networking
{
    public enum InputAcceptance { Accepted, Duplicate, Stale }
    public sealed class InputWindow
    {
        public const int Width = 3;
        private readonly Dictionary<int, CombatCommand>[] pending = { new Dictionary<int, CombatCommand>(), new Dictionary<int, CombatCommand>() };
        private readonly BattleConfig config;
        public InputWindow(BattleConfig config) { this.config = config ?? throw new ArgumentNullException(nameof(config)); }
        public InputAcceptance Add(int slot, int frame, int serverFrame, CombatCommand command)
        {
            if (slot < 0 || slot > 1) throw new InvalidDataException("Invalid player slot.");
            if (frame < 1) throw new InvalidDataException("Invalid input frame.");
            command.Validate(config);
            if (frame <= serverFrame) return InputAcceptance.Stale;
            if ((long)frame > (long)serverFrame + Width) throw new InvalidDataException("Input beyond window.");
            if (pending[slot].TryGetValue(frame, out var existing))
            {
                if (!existing.Equals(command)) throw new InvalidDataException("Conflicting duplicate input.");
                return InputAcceptance.Duplicate;
            }
            pending[slot].Add(frame, command); return InputAcceptance.Accepted;
        }
        public bool Has(int slot, int frame) => pending[slot].ContainsKey(frame);
        public bool TryTake(int frame, out FrameCommands commands)
        {
            commands = null;
            if (!pending[0].TryGetValue(frame, out var blue) || !pending[1].TryGetValue(frame, out var red)) return false;
            commands = new FrameCommands(frame, blue, red);
            pending[0].Remove(frame); pending[1].Remove(frame); return true;
        }
    }
}