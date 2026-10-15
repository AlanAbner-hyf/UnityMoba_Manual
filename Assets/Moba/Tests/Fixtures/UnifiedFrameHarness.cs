using System;
using System.Collections.Generic;
using Moba.Core;
using Moba.Networking;

namespace Moba.Tests.Fixtures
{
    // 阶段 06 的同进程教学夹具，不是网络会话，不参与游戏场景运行。
    // 唯一 AI 入口由服务端注入；两个客户端只接收不可变 FrameCommands。
    public sealed class UnifiedFrameHarness
    {
        private readonly Func<int, ActorCommand[]> evaluateServerAi;
        private readonly Queue<FrameCommands> firstInbox = new Queue<FrameCommands>();
        private readonly Queue<FrameCommands> secondInbox = new Queue<FrameCommands>();
        private readonly List<FrameEvidence> history = new List<FrameEvidence>();
        private readonly FrameScheduler scheduler = new FrameScheduler(BattleConfig.LogicHz);

        public CombatWorld Server { get; }
        public CombatWorld FirstClient { get; }
        public CombatWorld SecondClient { get; }
        public InputWindow Inputs { get; }
        public IReadOnlyList<FrameEvidence> History { get; }
        public byte[] ConfigHash { get; }
        public bool WaitingForInputs { get; private set; }
        public double NextDue => scheduler.NextDue;
        public Exception Failure { get; private set; }

        public UnifiedFrameHarness(BattleConfig config, Func<int, ActorCommand[]> evaluateServerAi,
            uint seed = BattleConfig.DefaultSeed)
        {
            this.evaluateServerAi = evaluateServerAi ?? throw new ArgumentNullException(nameof(evaluateServerAi));
            Server = CombatWorld.Create(config, seed);
            FirstClient = CombatWorld.Create(config, seed);
            SecondClient = CombatWorld.Create(config, seed);
            Inputs = new InputWindow(config);
            ConfigHash = StateHasher.ConfigHash(config);
            History = history.AsReadOnly();
            scheduler.Start(0); // 测试使用从 0 开始的可控时间，不读取 Unity 时间或真实时钟。
        }

        public InputAcceptance Submit(int slot, int frame, CombatCommand command)
            => Inputs.Add(slot, frame, Server.State.Frame, command);

        public bool TryAdvance(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (Failure != null) throw new InvalidOperationException("Harness already failed.", Failure);
            if (Server.State.Result != MatchResult.None || !scheduler.IsDue(now)) return false;

            int nextFrame = checked(Server.State.Frame + 1);
            if (!Inputs.TryTake(nextFrame, out var players))
            {
                WaitingForInputs = true;
                return false; // 不消费已有单方输入、不 Step，也完全不调用 AI。
            }

            try
            {
                // 此时三份世界仍为 S(n-1)，唯一服务端 AI 基于该状态生成 n 帧指令。
                ActorCommand[] robots = evaluateServerAi(nextFrame);
                var frame = new FrameCommands(nextFrame, players.BluePlayer, players.RedPlayer, robots);
                Server.Step(frame);

                // Queue 仅模拟传递完整帧；不复制服务端状态，也不模拟网络延迟或可靠性。
                firstInbox.Enqueue(frame);
                secondInbox.Enqueue(frame);
                FirstClient.Step(firstInbox.Dequeue());
                SecondClient.Step(secondInbox.Dequeue());

                var evidence = new FrameEvidence(frame, Server, FirstClient, SecondClient);
                history.Add(evidence); // 先保留异常帧证据，再判定一致性。
                if (!evidence.Matches)
                    throw new InvalidOperationException($"State mismatch at frame {nextFrame}: " +
                        $"server={StateHasher.Hex(evidence.Hashes[0])}, " +
                        $"first={StateHasher.Hex(evidence.Hashes[1])}, second={StateHasher.Hex(evidence.Hashes[2])}");

                scheduler.Advanced(now, WaitingForInputs);
                WaitingForInputs = false;
                return true;
            }
            catch (Exception error)
            {
                Failure = error; // 已经消费输入或评估 AI，失败后不能再尝试推进以掩盖问题。
                throw;
            }
        }
    }

    public sealed class FrameEvidence
    {
        public FrameCommands Commands { get; }
        // 顺序为服务端、客户端一、客户端二；保存字节快照，不保存 EntityState 引用。
        public byte[][] CanonicalBytes { get; }
        public byte[][] Hashes { get; }
        public bool Matches => StateHasher.Equal(Hashes[0], Hashes[1]) && StateHasher.Equal(Hashes[0], Hashes[2]);

        internal FrameEvidence(FrameCommands commands, CombatWorld server, CombatWorld first, CombatWorld second)
        {
            Commands = commands;
            CanonicalBytes = new[]
            {
                CanonicalStateWriter.Write(server.State),
                CanonicalStateWriter.Write(first.State),
                CanonicalStateWriter.Write(second.State)
            };
            Hashes = new[]
            {
                StateHasher.Hash(CanonicalBytes[0]),
                StateHasher.Hash(CanonicalBytes[1]),
                StateHasher.Hash(CanonicalBytes[2])
            };
        }
    }
}
