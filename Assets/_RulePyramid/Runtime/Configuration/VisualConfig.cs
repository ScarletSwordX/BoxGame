using UnityEngine;

namespace RulePyramid.Runtime
{
    [CreateAssetMenu(menuName = "规则工坊/视觉配置", fileName = "VisualConfig")]
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
        [Header("弹跳顶点提示")]
        [Min(0f), Tooltip("抵达顶点且上升动画结束后，弱引导延迟出现的秒数。")]
        public float bounceHintDelay = 1.5f;
        [Range(0f, 0.65f)] public float bouncePeripheralDarkness = 0.38f;
        [Range(0f, 3f)] public float bounceDispersionPixels = 1.1f;

        public float cameraDistance = 18f;
        public float cameraPitch = 35.264f;
        public float[] cameraYaws = { 45f, 135f, 225f, 315f };
    }
}
