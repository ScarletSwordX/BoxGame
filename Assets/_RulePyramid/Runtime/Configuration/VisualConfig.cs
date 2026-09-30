using UnityEngine;

namespace RulePyramid.Runtime
{
    [CreateAssetMenu(menuName = "规则工坊/视觉配置", fileName = "VisualConfig")]
    public class VisualConfig : ScriptableObject
    {
        public const float DefaultPlayerCameraYaw = 15f;
        public const float MaxPlayerCameraYaw = 20f;

        public float cellSize = 1f;
        public Vector3 origin = Vector3.zero;
        public Material terrainMaterial;
        public Material redMaterial;
        public Material blueMaterial;
        public Material pinkMaterial;
        public Material pinkHollowMaterial;
        public Material textMaterial;
        public Material anchoredTextMaterial;
        [Header("暖砂配色")]
        public Material structureMaterial;
        public Material cloudMaterial;
        public Material springMaterial;
        public Material wallMaterial;
        public Material lavaMaterial;
        public Material nounTextMaterial;
        public Material operatorTextMaterial;
        public Material propertyTextMaterial;
        public Color backgroundColor = new Color32(243, 239, 230, 255);
        public Color wordInk = new Color32(41, 50, 70, 255);
        [HideInInspector] public int paletteVersion;
        public Color youTint = new Color(1f, 0.35f, 0.25f);
        public Color winGlow = new Color(1f, 0.5f, 0.8f, 0.45f);
        [Header("玩家遮挡剖切")]
        [Min(0f), Tooltip("玩家周围圆形开口的半径（格）；0 关闭。仅剔除前方遮挡，圆外保持原样。")]
        public float occlusionRadiusCells = 0.9f;
        [Header("弹跳顶点提示")]
        [Min(0f), Tooltip("抵达顶点且上升动画结束后，弱引导延迟出现的秒数。")]
        public float bounceHintDelay = 1.5f;
        [Range(0f, 0.65f)] public float bouncePeripheralDarkness = 0.38f;
        [Range(0f, 3f)] public float bounceDispersionPixels = 1.1f;

        public float cameraDistance = 18f;
        [Range(60f, 80f), Tooltip("玩家镜头相对水平地面的俯角；所有关卡共用。")]
        public float cameraPitch = 75f;
        [Tooltip("玩家只使用第 1 项：水平偏航默认 15°，实际限制在 −20°至 +20°。其余项保留兼容。")]
        public float[] cameraYaws = { DefaultPlayerCameraYaw, 135f, 225f, 315f };

        public static float GetPlayerCameraYaw(VisualConfig config)
        {
            float yaw = config != null && config.cameraYaws != null && config.cameraYaws.Length == 4
                ? config.cameraYaws[0] : DefaultPlayerCameraYaw;
            if (float.IsNaN(yaw) || float.IsInfinity(yaw)) yaw = DefaultPlayerCameraYaw;
            return Mathf.Clamp(Mathf.DeltaAngle(0f, yaw), -MaxPlayerCameraYaw, MaxPlayerCameraYaw);
        }

        public static Quaternion GetPlayerViewRotation(VisualConfig config)
        {
            float pitch = config != null ? config.cameraPitch : 75f;
            return Quaternion.Euler(pitch, GetPlayerCameraYaw(config), 0f);
        }
    }
}
