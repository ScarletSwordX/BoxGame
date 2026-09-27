Shader "Hidden/RuleWorkshop/BounceApexFeedback"
{
    Properties
    {
        _MainTex ("画面", 2D) = "white" {}
        _Strength ("强度", Range(0,1)) = 0
        _Darkness ("外围压暗", Range(0,0.65)) = 0.38
        _DispersionPixels ("色散像素", Range(0,3)) = 1.1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _Focus;
            float _Strength, _Darkness, _DispersionPixels;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 center = _Focus.xy;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) center.y = 1 - center.y;
                #endif
                float2 radial = (i.uv - center) * float2(_Focus.w, 1);
                float distance = length(radial);
                float peripheral = smoothstep(_Focus.z, _Focus.z + 0.42, distance);
                float2 direction = radial / max(distance, 0.0001);
                float2 offset = direction * abs(_MainTex_TexelSize.xy)
                    * clamp(_DispersionPixels, 0, 3) * saturate(_Strength) * peripheral;
                fixed4 color = tex2D(_MainTex, i.uv);
                color.r = tex2D(_MainTex, saturate(i.uv + offset)).r;
                color.b = tex2D(_MainTex, saturate(i.uv - offset)).b;
                color.rgb *= 1 - peripheral * clamp(_Darkness, 0, 0.65) * saturate(_Strength);
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
