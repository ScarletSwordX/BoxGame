using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    /// <summary>仅记录表现时间，不推进世界，也不修改顶点输入机会。</summary>
    public sealed class BounceApexCueState
    {
        WorldModel _world;
        string _actorId;
        int _turn;
        public bool Waiting { get; private set; }
        public string ActorId => _actorId;
        public float Elapsed { get; private set; }
        public float Strength { get; private set; }
        public float HintAlpha { get; private set; }

        public void Tick(GameSession session, bool animationBusy, float deltaTime, float hintDelay)
        {
            float dt = Mathf.Max(0f, deltaTime);
            bool waiting = session != null && session.Phase == MotionPhase.BounceApex && !animationBusy;
            string actor = waiting ? session.World.ActorId : null;
            if (session == null || !ReferenceEquals(_world, session.World)
                || (waiting && (_actorId != actor || _turn != session.TurnCount)))
            {
                Reset();
                _world = session?.World;
            }
            Waiting = waiting && actor != null;
            if (Waiting)
            {
                _actorId = actor;
                _turn = session.TurnCount;
                Elapsed += dt;
                Strength = Mathf.MoveTowards(Strength, 1f, dt / 0.18f);
                HintAlpha = Mathf.Clamp01((Elapsed - Mathf.Max(0f, hintDelay)) / 0.25f);
            }
            else
            {
                Elapsed = 0f;
                HintAlpha = 0f;
                Strength = Mathf.MoveTowards(Strength, 0f, dt / 0.15f);
            }
        }

        public void Reset()
        {
            _world = null;
            _actorId = null;
            _turn = 0;
            Waiting = false;
            Elapsed = Strength = HintAlpha = 0f;
        }
    }
}
