Shader "RuleWorkshop/PlayerCutaway"
{
    Properties
    {
        _Color ("颜色", Color) = (1,1,1,1)
        _MainTex ("贴图", 2D) = "white" {}
        _Metallic ("金属度", Range(0,1)) = 0
        _Glossiness ("光滑度", Range(0,1)) = 0.5
        _EmissionColor ("发光", Color) = (0,0,0,0)
        _EmissionMap ("发光贴图", 2D) = "white" {}
        [HideInInspector] _SrcBlend ("源混合", Float) = 1
        [HideInInspector] _DstBlend ("目标混合", Float) = 0
        [HideInInspector] _ZWrite ("深度写入", Float) = 1
        [HideInInspector] _CutawayRadius ("剖切半径", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows keepalpha
        #pragma target 3.0
        #pragma shader_feature _EMISSION
        #include "UnityCG.cginc"
        sampler2D _MainTex, _EmissionMap;
        fixed4 _Color, _EmissionColor;
        half _Metallic, _Glossiness;
        float4 _CutawayTarget;
        float _CutawayRadius, _CutawayFeather, _CutawayDepthBias;
        struct Input
        {
            float2 uv_MainTex;
            float2 uv_EmissionMap;
            float3 worldPos;
            float4 screenPos;
        };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 focusView = mul(UNITY_MATRIX_V, float4(_CutawayTarget.xyz, 1)).xyz;
            float fragmentDepth = -mul(UNITY_MATRIX_V, float4(IN.worldPos, 1)).z;
            if (_CutawayRadius > 0 && -focusView.z > 0
                && fragmentDepth < -focusView.z - _CutawayDepthBias)
            {
                float4 focusClip = mul(UNITY_MATRIX_P, float4(focusView, 1));
                float4 focusScreen = ComputeScreenPos(focusClip);
                float2 offset = IN.screenPos.xy / IN.screenPos.w - focusScreen.xy / focusScreen.w;
                // 用投影矩阵校正宽高比，正交、透视和 RenderTexture 都保持圆形。
                offset.x *= abs(UNITY_MATRIX_P._m11 / UNITY_MATRIX_P._m00);
                float projectionScale = 0.5 * abs(UNITY_MATRIX_P._m11) / max(focusClip.w, 0.0001);
                float distanceToEdge = length(offset) / projectionScale - _CutawayRadius;
                // 边缘仅做窄带像素剔除；没有整物体半透明或透明排序问题。
                float2 pixel = floor(IN.screenPos.xy / IN.screenPos.w * _ScreenParams.xy);
                float noise = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
                clip(distanceToEdge - max(_CutawayFeather, 0.0001) * noise);
            }
            fixed4 color = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = color.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = color.a;
            #ifdef _EMISSION
            o.Emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb * _EmissionColor.rgb;
            #endif
        }
        ENDCG
    }
    // 阴影继续使用完整物体，开口只影响玩家观察，不挖空真实地形。
    FallBack "Standard"
}
