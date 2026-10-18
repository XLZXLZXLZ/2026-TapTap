Shader "Hidden/TapTap/Return Journey Signal"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #pragma target 3.0
            #include "Assets/Rendering/Shaders/Fullscreen.hlsl"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _JourneyParams;
            float Hash(float v) { return frac(sin(v * 127.1) * 43758.5453); }
            half4 Frag(v2f_img input) : SV_Target
            {
                float tick = floor(_JourneyParams.w * 8.0);
                float band = floor(input.uv.y * 42.0);
                float pulse = step(0.86, Hash(tick + band * 7.3));
                float2 uv = input.uv;
                uv.x += (Hash(band + tick) - 0.5) * _JourneyParams.y * abs(_MainTex_TexelSize.x) * pulse;
                float spread = _JourneyParams.z * abs(_MainTex_TexelSize.x) * (0.3 + pulse);
                half4 color = tex2D(_MainTex, saturate(uv));
                color.r = tex2D(_MainTex, saturate(uv + float2(spread, 0))).r;
                color.b = tex2D(_MainTex, saturate(uv - float2(spread, 0))).b;
                color.rgb *= 1.0 - saturate(_JourneyParams.x);
                return color;
            }
            ENDHLSL
        }
    }
}
