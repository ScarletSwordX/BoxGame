Shader "Hidden/RuleWorkshop/AuthoringBlocks"
{
    Properties
    {
        _Color ("填充颜色", Color) = (0.48, 0.54, 0.6, 1)
        _EdgeColor ("边缘颜色", Color) = (0.08, 0.11, 0.15, 1)
        _LineWidth ("边线像素宽度", Float) = 0.9
        _WorldGrid ("按世界整数格分线", Float) = 0
        _SrcBlend ("源混合", Float) = 1
        _DstBlend ("目标混合", Float) = 0
        _ZWrite ("写入深度", Float) = 1
        _ZTest ("深度比较", Float) = 4
        _Cull ("面剔除", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _Color, _EdgeColor;
            float _LineWidth, _WorldGrid;
            struct VertexInput { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct FragmentInput { float4 position : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float2 uv : TEXCOORD2; };

            FragmentInput vert(VertexInput input)
            {
                FragmentInput output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.uv = input.uv;
                return output;
            }

            float4 frag(FragmentInput input) : SV_Target
            {
                // 地形仍按大盒渲染，只在表面按世界整数格分线，避免逐格生成网格。
                float3 normal = abs(input.normal);
                float2 plane = normal.y > 0.5 ? input.world.xz : (normal.x > 0.5 ? input.world.zy : input.world.xy);
                float2 coordinate = lerp(input.uv, plane, _WorldGrid);
                float2 unit = frac(coordinate);
                float2 distanceToEdge = min(unit, 1.0 - unit);
                float2 pixelDistance = distanceToEdge / max(fwidth(coordinate), float2(0.00001, 0.00001));
                float edge = 1.0 - smoothstep(_LineWidth - 0.5, _LineWidth + 0.5, min(pixelDistance.x, pixelDistance.y));
                return lerp(_Color, _EdgeColor, edge);
            }
            ENDCG
        }
    }
    Fallback Off
}
