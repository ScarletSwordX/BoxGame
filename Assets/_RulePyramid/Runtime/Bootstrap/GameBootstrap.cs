using System.Collections;
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

        public bool IsMainMenu { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsMenuOpen => IsMainMenu || IsPaused;
        float _timeScaleBeforePause = 1f;
        int _resumeFrame = -1;

        BounceApexFeedback _bounceFeedback;
        GameSession _session;
        RegionTutorialSession _regionTutorial;
        int _index;
        string _reject;
        int _stageIndex;
        bool _campaignStartedFromMenu;
        Coroutine _stageTransition;
        [Tooltip("保留旧场景兼容；阶段完成后由玩家确认继续。")]
        public float stageWinPause = 0.35f;
        bool _completionWasReady;
        public float stageExpansionDuration = 1.1f;
        public int CurrentStageIndex => _stageIndex;
        public int CurrentStageCount => catalog != null && catalog.Count > 0 ? catalog.StageCount(_index) : 1;
        public bool IsStageTransitioning { get; private set; }
        public bool IsCompletionReady => _session != null && _session.Won && !IsStageTransitioning
            && (animator == null || !animator.IsPlaying);
        public bool IsStageComplete => IsCompletionReady && HasNextStage;
        public bool IsLevelComplete => IsCompletionReady && !HasNextStage;
        public bool HasNextStage => _session != null && _stageIndex + 1 < CurrentStageCount;
        public bool HasNextLevel => catalog != null && _index + 1 < catalog.Count;
        public bool HasContinue => CampaignProgress.TryGetNextIndex(catalog, out _);

        public GameSession Session => _session;
        public RegionTutorialSession RegionTutorial => _regionTutorial;

        void Start()
        {
            if (worldView != null) worldView.config = visualConfig;
            if (cameraSlots != null) cameraSlots.config = visualConfig;
            if (hud != null) hud.Bind(this);
            var camera = cameraSlots != null ? cameraSlots.GetComponent<Camera>() : Camera.main;
            if (camera != null)
            {
                _bounceFeedback = camera.GetComponent<BounceApexFeedback>() ?? camera.gameObject.AddComponent<BounceApexFeedback>();
                _bounceFeedback.bootstrap = this;
                _bounceFeedback.config = visualConfig;
            }
            // 无 HUD 的作者预览与测试仍可直接加载地图。
            if (hud != null && hud.HasMenus)
            {
                IsMainMenu = true;
                hud.RefreshMenus();
            }
            else LoadIndex(startIndex);
        }

        void Update()
        {
            if (_completionWasReady != IsCompletionReady)
            {
                _completionWasReady = IsCompletionReady;
                if (_completionWasReady) _bounceFeedback?.ResetFeedback();
                hud?.Refresh(_session, _reject, _regionTutorial);
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && !IsMainMenu && _session != null)
            {
                TogglePause();
                return;
            }
            if (IsMenuOpen || Time.frameCount == _resumeFrame) return;
            if (_session == null || IsStageTransitioning) return;
            if (_regionTutorial != null && _regionTutorial.Tick(Time.unscaledDeltaTime))
                hud?.Refresh(_session, _reject, _regionTutorial);
            if (inputAdapter == null) return;
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
            if (IsMenuOpen) return;
            if (catalog == null || catalog.Count == 0)
            {
                Debug.LogError("LevelCatalog is empty");
                return;
            }
            CancelStageTransition();
            _index = Mathf.Clamp(index, 0, catalog.Count - 1);
            ActivateStage(new GameSession(catalog.LoadStage(_index, 0)), 0, true);
        }

        void ActivateStage(GameSession session, int stage, bool rebuild)
        {
            _bounceFeedback?.ResetFeedback();
            _completionWasReady = false;
            _stageIndex = stage;
            _session = session;
            _regionTutorial = new RegionTutorialSession(_session.Level.tutorial);
            _regionTutorial.Observe(_session);
            _reject = null;
            animator?.Stop();
            if (rebuild) worldView?.Rebuild(_session.World);
            if (cameraSlots != null)
            {
                cameraSlots.ConfigureFromLevel(_session.Level.camera);
                cameraSlots.FrameLevel(_session.Level.bounds, rebuild);
            }
            inputAdapter?.ResetRepeat();
            hud?.ResetHints();
            hud?.Refresh(_session, _reject, _regionTutorial);
        }

        public void ContinueStage()
        {
            if (!isActiveAndEnabled || IsMenuOpen || !IsStageComplete) return;
            GameSession next;
            try { next = new GameSession(catalog.LoadStage(_index, _stageIndex + 1)); }
            catch (System.Exception ex)
            {
                _reject = "Could not load the next stage.";
                hud?.Refresh(_session, _reject, _regionTutorial);
                Debug.LogError(_reject + " " + ex.Message);
                return;
            }
            IsStageTransitioning = true;
            _reject = null;
            hud?.Refresh(_session, _reject, _regionTutorial);
            inputAdapter?.ResetRepeat();
            _stageTransition = StartCoroutine(AdvanceStage(next, _stageIndex + 1));
        }

        IEnumerator AdvanceStage(GameSession next, int nextStage)
        {
            // 至少跨一帧，确保协程句柄在零时长或无视图时也能正确清理。
            yield return null;
            while (IsPaused) yield return null;
            hud?.Refresh(_session, _reject, _regionTutorial);
            while (animator != null && animator.IsPlaying) yield return null;
            while (IsPaused) yield return null;
            _bounceFeedback?.ResetFeedback();
            animator?.Stop();
            var layout = StageTransitionLayout.Between(_session.World, next.World);
            cameraSlots?.FrameLevel(layout.FramingBounds, false);
            if (worldView != null)
                yield return worldView.ExpandTo(next.World, Mathf.Max(0f, stageExpansionDuration));
            else if (stageExpansionDuration > 0f)
                yield return new WaitForSeconds(stageExpansionDuration);
            while (IsPaused) yield return null;
            IsStageTransitioning = false;
            _stageTransition = null;
            float cellSize = visualConfig != null ? visualConfig.cellSize : 1f;
            cameraSlots?.TranslateFrame(new Vector3(-layout.NextOffset.x, 0f, -layout.NextOffset.z) * cellSize);
            ActivateStage(next, nextStage, false);
        }

        void CancelStageTransition()
        {
            bool wasTransitioning = IsStageTransitioning;
            if (_stageTransition != null) StopCoroutine(_stageTransition);
            _stageTransition = null;
            IsStageTransitioning = false;
            worldView?.CancelExpansion();
            if (wasTransitioning && _session != null) cameraSlots?.FrameLevel(_session.Level.bounds, true);
            animator?.Stop();
        }

        void OnEnable()
        {
            hud?.Refresh(_session, _reject, _regionTutorial);
        }

        void OnDisable()
        {
            ResumeGame();
            bool interrupted = IsStageTransitioning;
            CancelStageTransition();
            if (_session != null && (interrupted || _session.Won)) worldView?.Rebuild(_session.World);
        }

        public void Submit(string command)
        {
            if (IsMenuOpen || _session == null || IsStageTransitioning) return;
            if (_session.Won) return;
            int logFrom = _session.World.Log.Count;
            bool ok = _session.TryExecute(command);
            _reject = ok ? null : _session.LastRejectReason;
            if (ok)
            {
                _regionTutorial?.Observe(_session);
                var events = _session.World.Log.GetRange(logFrom, _session.World.Log.Count - logFrom);
                if (animator != null) animator.Play(events, _session.World);
                else worldView?.AlignToState(_session.World);
            }
            if (ok && _session.Won)
            {
                if (!HasNextStage && _campaignStartedFromMenu) CampaignProgress.RecordCompleted(catalog, _index);
            }
            hud?.Refresh(_session, _reject, _regionTutorial);
        }

        public void Undo()
        {
            if (IsMenuOpen || _session == null || IsStageTransitioning) return;
            if (_session.Undo())
            {
                _bounceFeedback?.ResetFeedback();
                animator?.CancelAndSnap(_session.World);
                worldView?.Rebuild(_session.World);
                _reject = null;
                _regionTutorial?.Observe(_session);
            }
            hud?.Refresh(_session, _reject, _regionTutorial);
        }

        public void Restart()
        {
            if (IsMenuOpen || _session == null || IsStageTransitioning) return;
            _bounceFeedback?.ResetFeedback();
            _session.Restart();
            animator?.CancelAndSnap(_session.World);
            worldView?.Rebuild(_session.World);
            _reject = null;
            _regionTutorial?.Observe(_session);
            hud?.Refresh(_session, _reject, _regionTutorial);
        }

        public void NextLevel()
        {
            if (!isActiveAndEnabled || IsMenuOpen || !IsLevelComplete) return;
            if (HasNextLevel) LoadIndex(_index + 1);
        }

        public void StartGame()
        {
            if (!IsMainMenu || catalog == null || catalog.Count == 0) return;
            IsMainMenu = false;
            _campaignStartedFromMenu = true;
            LoadIndex(startIndex);
            _resumeFrame = Time.frameCount;
            hud?.RefreshMenus();
        }

        public void ContinueGame()
        {
            if (!IsMainMenu || !CampaignProgress.TryGetNextIndex(catalog, out int nextIndex)) return;
            IsMainMenu = false;
            _campaignStartedFromMenu = true;
            LoadIndex(nextIndex);
            _resumeFrame = Time.frameCount;
            hud?.RefreshMenus();
        }

        public void TogglePause()
        {
            if (IsMainMenu || _session == null) return;
            if (IsPaused) { ResumeGame(); return; }
            _timeScaleBeforePause = Time.timeScale;
            IsPaused = true;
            Time.timeScale = 0f;
            inputAdapter?.ResetRepeat();
            hud?.RefreshMenus();
        }

        public void ResumeGame()
        {
            if (!IsPaused) return;
            Time.timeScale = _timeScaleBeforePause;
            IsPaused = false;
            _resumeFrame = Time.frameCount;
            inputAdapter?.ResetRepeat();
            hud?.RefreshMenus();
        }

        public void QuitGame()
        {
            ResumeGame();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
