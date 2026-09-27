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

        bool _framed;
        Vector3 _frameCenter, _desiredCenter;
        float _frameSize, _desiredSize;

        public void FrameLevel(GridCellBox bounds, bool immediate)
        {
            if (bounds == null) return;
            var min = GridMap.ToWorld(bounds.min, config);
            var max = GridMap.ToWorld(bounds.max, config);
            _desiredCenter = (min + max) * .5f;
            var inverse = Quaternion.Inverse(ViewRotation());
            float cell = config != null ? config.cellSize : 1f;
            var extents = (max - min) * .5f + Vector3.one * cell * .5f;
            float width = 0f, height = 0f;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                var corner = inverse * Vector3.Scale(extents, new Vector3(x, y, z));
                width = Mathf.Max(width, Mathf.Abs(corner.x));
                height = Mathf.Max(height, Mathf.Abs(corner.y));
            }
            var cam = GetComponent<Camera>();
            float aspect = cam != null ? Mathf.Max(.1f, cam.aspect) : 1.6f;
            _desiredSize = Mathf.Max(3f, Mathf.Max(height, width / aspect) * 1.25f + .5f);
            if (immediate || !_framed)
            {
                _frameCenter = _desiredCenter;
                _frameSize = _desiredSize;
            }
            _framed = true;
            ApplyPose();
        }

        Quaternion ViewRotation()
        {
            float pitch = config != null ? config.cameraPitch : 35.264f;
            float[] yaws = config != null && config.cameraYaws != null && config.cameraYaws.Length == 4
                ? config.cameraYaws : new[] { 45f, 135f, 225f, 315f };
            return Quaternion.Euler(pitch, yaws[0], 0f);
        }

        public int Slot => 0;
        public bool BlocksWorldInput => false;

        public void ConfigureFromLevel(CameraData data)
        {
            slot = 0;
            if (data == null) return;
            if (data.pitchDegrees > 0 && config != null) config.cameraPitch = data.pitchDegrees;
        }

        public WorldDirection ScreenToWorld(Vector2 screen)
        {
            if (Mathf.Abs(screen.y) >= Mathf.Abs(screen.x))
                return screen.y > 0f ? WorldDirection.North : WorldDirection.South;
            return screen.x > 0f ? WorldDirection.East : WorldDirection.West;
        }

        void LateUpdate()
        {
            slot = 0;
            if (_framed)
            {
                float t = 1f - Mathf.Exp(-Time.deltaTime * 7f);
                _frameCenter = Vector3.Lerp(_frameCenter, _desiredCenter, t);
                _frameSize = Mathf.Lerp(_frameSize, _desiredSize, t);
            }
            ApplyPose();
        }

        public void Snap()
        {
            slot = 0;
            ApplyPose();
        }

        void ApplyPose()
        {
            float dist = config != null ? config.cameraDistance : distance;
            if (_framed) dist = Mathf.Max(dist, _frameSize * 3f);
            Vector3 target = _framed ? _frameCenter : focus != null ? focus.position : Vector3.zero;
            Quaternion rot = ViewRotation();
            transform.position = target - rot * Vector3.forward * dist;
            transform.rotation = rot;
            var cam = GetComponent<Camera>();
            if (cam != null)
            {
                if (config != null)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = config.backgroundColor;
                }
                cam.orthographic = true;
                cam.orthographicSize = _framed ? _frameSize : Mathf.Max(6f, dist * 0.35f);
            }
        }
    }
}
