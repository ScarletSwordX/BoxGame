using System.Collections;
using System.Collections.Generic;
using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    public class EventAnimator : MonoBehaviour
    {
        public WorldView worldView;
        public float stepDuration = 0.12f;
        Coroutine _playing;
        int _gen;

        public bool IsPlaying => _playing != null;

        public void Play(List<SimEvent> events, WorldModel finalState)
        {
            Stop();
            _gen = worldView != null ? worldView.Generation : 0;
            _playing = StartCoroutine(PlayRoutine(events, finalState, _gen));
        }

        public void Stop()
        {
            if (_playing != null)
            {
                StopCoroutine(_playing);
                _playing = null;
            }
        }

        public void CancelAndSnap(WorldModel state)
        {
            Stop();
            if (worldView != null) worldView.AlignToState(state);
        }

        IEnumerator PlayRoutine(List<SimEvent> events, WorldModel finalState, int gen)
        {
            if (events != null)
            {
                foreach (var ev in events)
                {
                    if (worldView != null && worldView.Generation != gen) yield break;
                    if (IsMove(ev.Kind) && worldView != null && worldView.TryGetView(ev.EntityId, out var t))
                    {
                        Vector3 from = GridMap.ToWorld(ev.From, worldView.config);
                        Vector3 to = GridMap.ToWorld(ev.To, worldView.config);
                        float t0 = 0f;
                        while (t0 < 1f)
                        {
                            if (worldView.Generation != gen) yield break;
                            t0 += Time.deltaTime / stepDuration;
                            t.position = Vector3.Lerp(from, to, Mathf.Clamp01(t0));
                            yield return null;
                        }
                        t.position = to;
                    }
                }
            }
            if (worldView != null && worldView.Generation == gen)
                worldView.AlignToState(finalState);
            _playing = null;
        }

        static bool IsMove(string kind)
        {
            return kind == "Walk" || kind == "TextPushed" || kind == "HeadBump" || kind == "JumpApex"
                || kind == "GravityFall" || kind == "PlayerFell" || kind == "LandingPress"
                || kind == "BounceStep" || kind == "ApexSteer" || kind == "FlyOrRide";
        }
    }
}
