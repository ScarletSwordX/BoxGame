using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    /// <summary>内置渲染管线的顶点时停提示。场景后处理不影响 HUD 和输入。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class BounceApexFeedback : MonoBehaviour
    {
        public GameBootstrap bootstrap;
        public VisualConfig config;
        readonly BounceApexCueState _state = new BounceApexCueState();
        Camera _camera;
        Material _material;
        Vector3 _focus;
        bool _hasFocus;
        GUIStyle _hintStyle;
        static readonly Vector2[] DirectionInputs = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };
        static readonly string[] DirectionKeys = { "W", "A", "S", "D" };
        static readonly WorldDirection[] DefaultDirections = { WorldDirection.North, WorldDirection.West, WorldDirection.South, WorldDirection.East };

        public BounceApexCueState State => _state;

        void Awake() => _camera = GetComponent<Camera>();

        void LateUpdate()
        {
            if (bootstrap != null && bootstrap.IsMenuOpen) return;
            if (bootstrap == null || !bootstrap.isActiveAndEnabled)
            {
                ResetFeedback();
                return;
            }
            _state.Tick(bootstrap.Session, bootstrap.animator != null && bootstrap.animator.IsPlaying,
                Time.deltaTime, config != null ? config.bounceHintDelay : 1.5f);
            if (!_state.Waiting) return;
            var world = bootstrap.Session.World;
            var actor = world.Entity(_state.ActorId);
            if (actor == null) { ResetFeedback(); return; }
            if (bootstrap.worldView != null && bootstrap.worldView.TryGetView(actor.Id, out var view))
                _focus = view.position;
            else
                _focus = GridMap.ToWorld(actor.Cell, config);
            _hasFocus = true;
        }

        public void ResetFeedback()
        {
            _state.Reset();
            _hasFocus = false;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!_hasFocus || _state.Strength <= 0f || !EnsureMaterial())
            {
                Graphics.Blit(source, destination);
                return;
            }
            Vector3 center = _camera.WorldToViewportPoint(_focus);
            if (center.z <= 0f)
            {
                Graphics.Blit(source, destination);
                return;
            }
            float cell = config != null ? config.cellSize : 1f;
            // 清晰区至少涵盖附近两格，镜头缩放后依旧能判断落脚点。
            var nearby = _camera.WorldToViewportPoint(_focus + _camera.transform.up * (cell * 2.5f));
            float radius = Mathf.Clamp(Mathf.Abs(nearby.y - center.y), 0.14f, 0.4f);
            _material.SetVector("_Focus", new Vector4(center.x, center.y, radius, (float)source.width / source.height));
            _material.SetFloat("_Strength", _state.Strength);
            _material.SetFloat("_Darkness", config != null ? config.bouncePeripheralDarkness : 0.38f);
            _material.SetFloat("_DispersionPixels", config != null ? config.bounceDispersionPixels : 1.1f);
            Graphics.Blit(source, destination, _material);
        }

        void OnGUI()
        {
            if (bootstrap != null && bootstrap.IsMenuOpen) return;
            if (Event.current.type != EventType.Repaint || !_state.Waiting || _state.HintAlpha <= 0f
                || !_hasFocus || _camera == null || !_camera.isActiveAndEnabled
                || bootstrap == null || bootstrap.Session == null
                || bootstrap.Session.Phase != RulePyramid.Core.MotionPhase.BounceApex
                || (bootstrap.animator != null && bootstrap.animator.IsPlaying)) return;
            Vector3 center = _camera.WorldToViewportPoint(_focus);
            if (center.z <= 0f || center.x < 0f || center.x > 1f || center.y < 0f || center.y > 1f) return;
            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = true
                };
                _hintStyle.normal.textColor = Color.white;
            }
            Rect viewport = _camera.pixelRect;
            Rect area = new Rect(viewport.x, Screen.height - viewport.yMax, viewport.width, viewport.height);
            DrawDirectionArrows(area, center);
            float width = Mathf.Min(340f, area.width - 12f);
            if (width <= 0f || area.height < 68f) return;
            float x = Mathf.Clamp(area.x + center.x * area.width - width * 0.5f, area.x + 6f, area.xMax - width - 6f);
            float y = Mathf.Clamp(area.y + (1f - center.y) * area.height + 106f, area.y + 6f, area.yMax - 62f);
            var rect = new Rect(x, y, width, 56f);
            Color oldColor = GUI.color;
            GUI.color = new Color(0.06f, 0.09f, 0.13f, 0.82f * _state.HintAlpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, _state.HintAlpha);
            GUI.Label(rect, "At the top\nWASD to move and drop / Space to drop", _hintStyle);
            GUI.color = oldColor;
        }

        void DrawDirectionArrows(Rect area, Vector3 center)
        {
            if (area.width < 1f || area.height < 1f) return;
            Color oldColor = GUI.color;
            GUI.BeginGroup(area);
            try
            {
                float scale = Mathf.Min(1f, Mathf.Min(area.width, area.height) / 180f);
                float margin = 88f * scale;
                var anchor = new Vector2(
                    Mathf.Clamp(center.x * area.width, margin, area.width - margin),
                    Mathf.Clamp((1f - center.y) * area.height, margin, area.height - margin));
                for (int i = 0; i < DirectionInputs.Length; i++)
                {
                    var direction = bootstrap.cameraSlots != null
                        ? bootstrap.cameraSlots.ScreenToWorld(DirectionInputs[i]) : DefaultDirections[i];
                    var projected = BounceApexArrows.ProjectDirection(_camera, _focus, direction);
                    bool usable = BounceApexArrows.CanSteer(bootstrap.Session.World, direction);
                    var tint = usable ? new Color(0.55f, 0.94f, 1f) : new Color(0.52f, 0.52f, 0.52f);
                    tint.a = _state.HintAlpha;
                    var tail = anchor + projected * (32f * scale);
                    var tip = anchor + projected * (60f * scale);
                    var side = new Vector2(-projected.y, projected.x);
                    var left = tip - projected * (11f * scale) + side * (7f * scale);
                    var right = tip - projected * (11f * scale) - side * (7f * scale);
                    // 深色描边让灰色受阻箭头在浅色地形上仍可辨认。
                    GUI.color = new Color(0.04f, 0.06f, 0.08f, _state.HintAlpha);
                    DrawLine(tail, tip, 6f * scale);
                    DrawLine(left, tip, 6f * scale);
                    DrawLine(right, tip, 6f * scale);
                    GUI.color = tint;
                    DrawLine(tail, tip, 3f * scale);
                    DrawLine(left, tip, 3f * scale);
                    DrawLine(right, tip, 3f * scale);
                    var label = anchor + projected * (77f * scale);
                    var labelRect = new Rect(label.x - 10f, label.y - 10f, 20f, 20f);
                    GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.8f * _state.HintAlpha);
                    GUI.DrawTexture(labelRect, Texture2D.whiteTexture);
                    GUI.color = tint;
                    GUI.Label(labelRect, DirectionKeys[i], _hintStyle);
                }
            }
            finally
            {
                GUI.EndGroup();
                GUI.color = oldColor;
            }
        }

        static void DrawLine(Vector2 from, Vector2 to, float width)
        {
            Matrix4x4 matrix = GUI.matrix;
            try
            {
                Vector2 delta = to - from;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
                GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, delta.magnitude, width), Texture2D.whiteTexture);
            }
            finally { GUI.matrix = matrix; }
        }

        bool EnsureMaterial()
        {
            if (_material != null) return true;
            var shader = Resources.Load<Shader>("RuleWorkshop/BounceApexFeedback");
            if (shader == null || !shader.isSupported) return false;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        void OnDisable()
        {
            ResetFeedback();
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
