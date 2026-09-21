using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    public class GameBootstrap : MonoBehaviour
    {
        public LevelCatalog catalog;
        public VisualConfig visualConfig;
        public WorldView worldView;
        public EventAnimator animator;
        public CameraSlotsController cameraSlots;
        public InputAdapter inputAdapter;
        public HudController hud;
        public int startIndex;

        GameSession _session;
        int _index;
        string _reject;

        public GameSession Session => _session;

        void Start()
        {
            if (worldView != null) worldView.config = visualConfig;
            if (cameraSlots != null) cameraSlots.config = visualConfig;
            if (hud != null) hud.Bind(this);
            LoadIndex(startIndex);
        }

        void Update()
        {
            if (_session == null || inputAdapter == null) return;
            if (!inputAdapter.TryPoll(_session.Phase, out var command)) return;
            if (command == "UNDO")
            {
                Undo();
                return;
            }
            if (command == "RESTART")
            {
                Restart();
                return;
            }
            Submit(command);
        }

        public void LoadIndex(int index)
        {
            if (catalog == null || catalog.Count == 0)
            {
                Debug.LogError("LevelCatalog is empty");
                return;
            }
            _index = Mathf.Clamp(index, 0, catalog.Count - 1);
            var level = catalog.Load(_index);
            _session = new GameSession(level);
            _reject = null;
            if (worldView != null) worldView.Rebuild(_session.World);
            if (animator != null) animator.CancelAndSnap(_session.World);
            if (cameraSlots != null)
            {
                cameraSlots.ConfigureFromLevel(level.camera);
                cameraSlots.Snap();
            }
            hud?.ResetHints();
            hud?.Refresh(_session, _reject);
        }

        public void Submit(string command)
        {
            if (_session == null) return;
            int logFrom = _session.World.Log.Count;
            bool ok = _session.TryExecute(command);
            _reject = ok ? null : _session.LastRejectReason;
            if (ok)
            {
                var events = _session.World.Log.GetRange(logFrom, _session.World.Log.Count - logFrom);
                if (animator != null) animator.Play(events, _session.World);
                else worldView?.AlignToState(_session.World);
            }
            hud?.Refresh(_session, _reject);
        }

        public void Undo()
        {
            if (_session == null) return;
            if (_session.Undo())
            {
                animator?.CancelAndSnap(_session.World);
                worldView?.Rebuild(_session.World);
                _reject = null;
            }
            hud?.Refresh(_session, _reject);
        }

        public void Restart()
        {
            if (_session == null) return;
            _session.Restart();
            animator?.CancelAndSnap(_session.World);
            worldView?.Rebuild(_session.World);
            _reject = null;
            hud?.ResetHints();
            hud?.Refresh(_session, _reject);
        }

        public void NextLevel()
        {
            LoadIndex(_index + 1);
        }
    }
}
