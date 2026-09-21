using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    public class CameraSlotsController : MonoBehaviour
    {
        public VisualConfig config;
        public Transform focus;
        public int slot;
        public float distance = 18f;
        public bool transitioning;
        float _blend;
        int _fromSlot;
        int _toSlot;
        const float Duration = 0.18f;

        public int Slot => slot;
        public bool BlocksWorldInput => transitioning;

        public void ConfigureFromLevel(CameraData data)
        {
            if (data == null) return;
            slot = data.initialSlot;
            if (data.pitchDegrees > 0 && config != null) config.cameraPitch = data.pitchDegrees;
        }

        public void Rotate(int delta)
        {
            if (transitioning) return;
            _fromSlot = slot;
            _toSlot = (slot + delta) & 3;
            transitioning = true;
            _blend = 0f;
        }

        public WorldDirection ScreenToWorld(Vector2 screen)
        {
            // WASD relative to camera yaw slot: spec 12.1
            int s = transitioning ? _toSlot : slot;
            if (Mathf.Abs(screen.y) >= Mathf.Abs(screen.x))
            {
                bool forward = screen.y > 0f;
                switch (s)
                {
                    case 0: return forward ? WorldDirection.North : WorldDirection.South;
                    case 1: return forward ? WorldDirection.East : WorldDirection.West;
                    case 2: return forward ? WorldDirection.South : WorldDirection.North;
                    default: return forward ? WorldDirection.West : WorldDirection.East;
                }
            }
            bool right = screen.x > 0f;
            switch (s)
            {
                case 0: return right ? WorldDirection.East : WorldDirection.West;
                case 1: return right ? WorldDirection.South : WorldDirection.North;
                case 2: return right ? WorldDirection.West : WorldDirection.East;
                default: return right ? WorldDirection.North : WorldDirection.South;
            }
        }

        void LateUpdate()
        {
            if (transitioning)
            {
                _blend += Time.deltaTime / Duration;
                if (_blend >= 1f)
                {
                    _blend = 1f;
                    transitioning = false;
                    slot = _toSlot;
                }
            }
            ApplyPose();
        }

        public void Snap()
        {
            transitioning = false;
            ApplyPose();
        }

        void ApplyPose()
        {
            float pitch = config != null ? config.cameraPitch : 35.264f;
            float dist = config != null ? config.cameraDistance : distance;
            float[] yaws = config != null && config.cameraYaws != null && config.cameraYaws.Length == 4
                ? config.cameraYaws
                : new[] { 45f, 135f, 225f, 315f };
            int a = transitioning ? _fromSlot : slot;
            int b = transitioning ? _toSlot : slot;
            float yaw = Mathf.LerpAngle(yaws[a], yaws[b], transitioning ? _blend : 1f);
            Vector3 target = focus != null ? focus.position : Vector3.zero;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target - rot * Vector3.forward * dist;
            transform.rotation = rot;
            var cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(6f, dist * 0.35f);
            }
        }
    }
}
