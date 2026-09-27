using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    /// <summary>只从成功的试玩操作生成参考解；撤销和重开同步录制路径。</summary>
    public sealed class PlaytestRecorder
    {
        readonly GameSession _session;
        readonly List<string> _commands = new List<string>();

        public PlaytestRecorder(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public GameSession Session => _session;
        public int CommandCount => _commands.Count;
        public string[] Commands => _commands.ToArray();
        public bool Won => _session.Won;

        public bool TryExecute(string command)
        {
            command = Tokens.NormalizeCommand(command);
            if (!_session.TryExecute(command)) return false;
            if (Tokens.Commands.Contains(command)) _commands.Add(command);
            return true;
        }

        public bool Undo()
        {
            if (_commands.Count == 0 || !_session.Undo()) return false;
            _commands.RemoveAt(_commands.Count - 1);
            return true;
        }

        public void Restart()
        {
            _session.Restart();
            _commands.Clear();
        }

        public ReferenceSolutionData ToSolution(string id, string name, string family = null)
        {
            if (!_session.Won) throw new InvalidOperationException("Win the playtest before saving a reference solution");
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Solution id is required", nameof(id));
            return new ReferenceSolutionData
            {
                id = id,
                name = name ?? id,
                family = family ?? "recorded",
                commands = _commands.ToArray(),
                expectedFinalStatus = "Won",
                status = "recorded"
            };
        }
    }
}
