using Moba.Core;
using UnityEngine;
namespace Moba.Bootstrap
{
    [CreateAssetMenu(menuName = "Moba/Battle Config")]
    public sealed class BattleConfigAsset : ScriptableObject
    {
        public int HeroSpeed = 200;
        public uint Seed = BattleConfig.DefaultSeed;
        public Moba.Core.SimulationMode Mode = Moba.Core.SimulationMode.MovementFixture;
        public BattleConfig ToConfig() => new BattleConfig(heroSpeed: HeroSpeed, mode: Mode);
    }
}
