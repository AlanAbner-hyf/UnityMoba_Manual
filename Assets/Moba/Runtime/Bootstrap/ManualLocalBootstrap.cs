using Moba.Core;
using Moba.Presentation;
using Moba.AI;
using UnityEngine;
namespace Moba.Bootstrap
{
    public sealed class ManualLocalBootstrap : MonoBehaviour
    {
        public BattleConfigAsset Configuration;
        public BattleView View;
        public PlayerCommandInput Input;
        public CombatWorld World { get; private set; }
        public GameObject ServerAiPrefab;
        private ServerAiSystem ai;
        private double accumulator;
        private void Start()
        {
            World = CombatWorld.Create(Configuration.ToConfig(), Configuration.Seed);
            ai = new ServerAiSystem(World, ServerAiPrefab);
            View.Bind(World);
            Input.World = World;
            Input.ControlledId = 1;
        }
        private void Update()
        {
            if (World == null || World.State.Result != MatchResult.None) return;
            accumulator += Time.unscaledDeltaTime;
            const double period = 1.0 / BattleConfig.LogicHz;
            int budget = 8;
            while (accumulator >= period && budget-- > 0)
            {
                int frame = World.State.Frame + 1;
                var robots = ai.Evaluate(frame);
                World.Step(new FrameCommands(frame, Input.Sample(), CombatCommand.NoOp, robots));
                View.Refresh();
                accumulator -= period;
                if (World.State.Result != MatchResult.None) break;
            }
        }

        private void OnDestroy()
        {
            ai?.Dispose();
        }
    }
}