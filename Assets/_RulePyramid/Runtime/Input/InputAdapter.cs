using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    public class InputAdapter : MonoBehaviour
    {
        public CameraSlotsController cameraSlots;
        public float repeatDelay = 0.22f;
        float _repeatTimer;
        string _held;

        public bool TryPoll(MotionPhase phase, out string command)
        {
            command = null;
            if (cameraSlots != null && cameraSlots.BlocksWorldInput) return false;

            if (GetKeyDown(KeyCode.Q))
            {
                cameraSlots?.Rotate(-1);
                return false;
            }
            if (GetKeyDown(KeyCode.E))
            {
                cameraSlots?.Rotate(1);
                return false;
            }
            if (GetKeyDown(KeyCode.Z))
            {
                command = "UNDO";
                return true;
            }
            if (GetKeyDown(KeyCode.R))
            {
                command = "RESTART";
                return true;
            }

            bool space = GetKeyDown(KeyCode.Space);
            Vector2 dir = ReadDirectionDown();
            if (phase == MotionPhase.Grounded && space)
            {
                command = "J";
                return true;
            }
            if (phase == MotionPhase.BounceApex)
            {
                if (space)
                {
                    command = "WAIT";
                    return true;
                }
                if (dir != Vector2.zero)
                {
                    command = WorldDirections.ToToken(Map(dir));
                    return true;
                }
            }
            if (dir != Vector2.zero)
            {
                var token = WorldDirections.ToToken(Map(dir));
                if (phase == MotionPhase.Grounded && (GetKey(KeyCode.LeftShift) || GetKey(KeyCode.RightShift)))
                    command = "P" + token;
                else
                    command = token;
                return true;
            }

            dir = ReadDirectionHeld();
            if (dir != Vector2.zero && phase == MotionPhase.Grounded)
            {
                string token = WorldDirections.ToToken(Map(dir));
                if (GetKey(KeyCode.LeftShift) || GetKey(KeyCode.RightShift))
                    token = "P" + token;
                if (_held == token)
                {
                    _repeatTimer += Time.deltaTime;
                    if (_repeatTimer >= repeatDelay)
                    {
                        _repeatTimer = 0f;
                        command = token;
                        return true;
                    }
                }
                else
                {
                    _held = token;
                    _repeatTimer = 0f;
                }
            }
            else
            {
                _held = null;
                _repeatTimer = 0f;
            }
            return false;
        }

        WorldDirection Map(Vector2 screen)
        {
            if (cameraSlots != null) return cameraSlots.ScreenToWorld(screen);
            if (Mathf.Abs(screen.y) >= Mathf.Abs(screen.x))
                return screen.y > 0 ? WorldDirection.North : WorldDirection.South;
            return screen.x > 0 ? WorldDirection.East : WorldDirection.West;
        }

        static Vector2 ReadDirectionDown()
        {
            if (GetKeyDown(KeyCode.W) || GetKeyDown(KeyCode.UpArrow)) return Vector2.up;
            if (GetKeyDown(KeyCode.S) || GetKeyDown(KeyCode.DownArrow)) return Vector2.down;
            if (GetKeyDown(KeyCode.A) || GetKeyDown(KeyCode.LeftArrow)) return Vector2.left;
            if (GetKeyDown(KeyCode.D) || GetKeyDown(KeyCode.RightArrow)) return Vector2.right;
            return Vector2.zero;
        }

        static Vector2 ReadDirectionHeld()
        {
            if (GetKey(KeyCode.W) || GetKey(KeyCode.UpArrow)) return Vector2.up;
            if (GetKey(KeyCode.S) || GetKey(KeyCode.DownArrow)) return Vector2.down;
            if (GetKey(KeyCode.A) || GetKey(KeyCode.LeftArrow)) return Vector2.left;
            if (GetKey(KeyCode.D) || GetKey(KeyCode.RightArrow)) return Vector2.right;
            return Vector2.zero;
        }

        static bool GetKeyDown(KeyCode key) => Input.GetKeyDown(key);
        static bool GetKey(KeyCode key) => Input.GetKey(key);
    }
}
