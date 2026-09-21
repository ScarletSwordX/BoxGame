using UnityEngine;

namespace RulePyramid.Runtime
{
    [CreateAssetMenu(menuName = "RulePyramid/Visual Config", fileName = "VisualConfig")]
    public class VisualConfig : ScriptableObject
    {
        public float cellSize = 1f;
        public Vector3 origin = Vector3.zero;
        public Material terrainMaterial;
        public Material redMaterial;
        public Material blueMaterial;
        public Material pinkMaterial;
        public Material pinkHollowMaterial;
        public Material textMaterial;
        public Material anchoredTextMaterial;
        public Color youTint = new Color(1f, 0.35f, 0.25f);
        public Color winGlow = new Color(1f, 0.5f, 0.8f, 0.45f);
        public float cameraDistance = 18f;
        public float cameraPitch = 35.264f;
        public float[] cameraYaws = { 45f, 135f, 225f, 315f };
    }
}
