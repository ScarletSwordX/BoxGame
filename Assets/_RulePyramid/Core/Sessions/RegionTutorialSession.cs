using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    public enum RegionTutorialState
    {
        Pending,
        Queued,
        Showing,
        Shown
    }

    /// <summary>One play session's region prompt queue. Undo and restart do not reset this state.</summary>
    public sealed class RegionTutorialSession
    {
        readonly RegionTutorialData[] _regions;
        readonly RegionTutorialState[] _states;
        readonly Queue<int> _queue = new Queue<int>();
        int _currentIndex = -1;
        double _remainingSeconds;

        public RegionTutorialSession(TutorialData tutorial)
        {
            _regions = tutorial?.regions ?? Array.Empty<RegionTutorialData>();
            _states = new RegionTutorialState[_regions.Length];
        }

        public RegionTutorialData Current => _currentIndex < 0 ? null : _regions[_currentIndex];
        public double RemainingSeconds => _remainingSeconds;
        public int QueuedCount => _queue.Count;
        public int Count => _regions.Length;

        public RegionTutorialState StateAt(int index)
        {
            if (index < 0 || index >= _states.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return _states[index];
        }

        /// <summary>Observe successful atomic visits, then the final world state.</summary>
        public bool Observe(GameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            bool changed = false;
            foreach (var cell in session.LastControlledCells)
                changed = ObserveCell(cell) || changed;
            return Observe(session.World) || changed;
        }

        /// <summary>Queue all newly satisfied prompts in authored list order.</summary>
        public bool Observe(WorldModel world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var actor = world.FindYou();
            if (actor == null) return false;

            return ObserveCell(actor.Cell);
        }

        public bool ObserveCell(GridCell cell)
        {

            bool changed = false;
            for (int i = 0; i < _regions.Length; i++)
            {
                var region = _regions[i];
                if (_states[i] != RegionTutorialState.Pending || region == null || !region.enabled
                    || region.bounds == null || string.IsNullOrEmpty(region.text)
                    || region.durationSeconds <= 0f || float.IsNaN(region.durationSeconds)
                    || float.IsInfinity(region.durationSeconds))
                    continue;
                var min = region.bounds.min;
                var max = region.bounds.max;
                if (cell.x < min.x || cell.x > max.x || cell.y < min.y || cell.y > max.y
                    || cell.z < min.z || cell.z > max.z)
                    continue;
                _states[i] = RegionTutorialState.Queued;
                _queue.Enqueue(i);
                changed = true;
            }
            return StartNext() || changed;
        }

        /// <summary>Advance visible time. A waiting prompt starts its own timer when shown.</summary>
        public bool Tick(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (_currentIndex < 0) return StartNext();
            _remainingSeconds -= seconds;
            if (_remainingSeconds > 0d) return false;
            _states[_currentIndex] = RegionTutorialState.Shown;
            _currentIndex = -1;
            _remainingSeconds = 0d;
            // 下一条在本帧才真正显示，不把上一帧卡顿的余量扣到它的展示时长。
            StartNext();
            return true;
        }

        bool StartNext()
        {
            if (_currentIndex >= 0 || _queue.Count == 0) return false;
            _currentIndex = _queue.Dequeue();
            _states[_currentIndex] = RegionTutorialState.Showing;
            _remainingSeconds = _regions[_currentIndex].durationSeconds;
            return true;
        }
    }
}
