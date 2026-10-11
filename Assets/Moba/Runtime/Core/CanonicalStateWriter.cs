using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Moba.Core
{
    public static class CanonicalStateWriter
    {
        public const int SchemaVersion = 4;

        public static byte[] Write(WorldState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(0x53544154u); writer.Write(SchemaVersion);
                writer.Write(state.Frame); writer.Write((byte)state.Result);
                writer.Write(state.NextEntityId); writer.Write(state.RandomState);
                writer.Write(state.NextWaveFrame); writer.Write(state.WaveNumber);
                var entities = state.Entities.OrderBy(e => e.EntityId).ToArray();
                writer.Write(entities.Length);
                uint previous = 0;
                foreach (var entity in entities)
                {
                    if (entity.EntityId <= previous) throw new InvalidOperationException("Duplicate or zero entity ID.");
                    previous = entity.EntityId;
                    writer.Write(entity.EntityId); writer.Write((byte)entity.Kind); writer.Write((byte)entity.Team);
                    writer.Write((byte)(entity.Alive ? 1 : 0)); WritePoint(writer, entity.Position);
                    writer.Write(entity.Hp); writer.Write((byte)entity.State); WriteCommand(writer, entity.Intent);
                    writer.Write(entity.AttackReadyFrame); writer.Write(entity.HitFrame); writer.Write(entity.SkillReadyFrame);
                    writer.Write(entity.RespawnFrame); writer.Write(entity.LastDamagedFrame);
                    writer.Write(entity.PathIndex);
                }
                writer.Flush(); return stream.ToArray();
            }
        }

        public static byte[] WriteConfig(BattleConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(0x434F4E46u); writer.Write(SchemaVersion); writer.Write(BattleConfig.RulesVersion);
                writer.Write(BattleConfig.LogicHz); BattleConfigCodec.Write(writer, config);
                WritePoint(writer, config.BlueSpawn); WritePoint(writer, config.RedSpawn);
                WritePoint(writer, config.BlueTower); WritePoint(writer, config.RedTower);
                WritePoint(writer, config.Lane.Midpoint);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static void WritePoint(BinaryWriter writer, Int2 point) { writer.Write(point.X); writer.Write(point.Z); }
        public static void WriteCommand(BinaryWriter writer, CombatCommand command)
        { writer.Write((byte)command.Kind); WritePoint(writer, command.Point); writer.Write(command.TargetId); }
    }
}