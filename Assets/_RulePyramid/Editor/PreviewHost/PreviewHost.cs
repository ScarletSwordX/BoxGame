using RulePyramid.Core;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    public static class PreviewHost
    {
        public static void Start(LevelEditSession session)
        {
            if (session == null) return;
            Selection.activeObject = null;
            Debug.Log("[RulePyramid] Isolated playtest started in editor session. Use Stop in the level editor. Draft is unchanged.");
        }
    }
}
