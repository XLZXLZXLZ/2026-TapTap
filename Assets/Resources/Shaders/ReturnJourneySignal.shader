Shader "Hidden/TapTap/Return Journey Signal"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "white" {}
        [HideInInspector] _JourneyColor ("Saturation, Vignette Strength, Start", Vector) = (1, 0, 0.55, 0)
    }
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
            float4 _MainTex_TexelSize, _JourneyParams, _JourneyTearTiming, _JourneyColor;
            float Hash(float v) { return frac(sin(v * 127.1) * 43758.5453); }
            half4 Frag(v2f_img input) : SV_Target
            {
                float frequency = max(0.01, _JourneyTearTiming.x);
                float cycle = _JourneyParams.w * frequency;
                float tick = floor(cycle);
                float burst = 1.0 - step(saturate(_JourneyTearTiming.y * frequency), frac(cycle));
                float band = floor(input.uv.y * 42.0);
                float pulse = burst * step(0.86, Hash(tick + band * 7.3));
                float2 uv = input.uv;
                uv.x += (Hash(band + tick) - 0.5) * _JourneyParams.y * abs(_MainTex_TexelSize.x) * pulse;
                float spread = _JourneyParams.z * abs(_MainTex_TexelSize.x) * (0.1 + 1.4 * pulse);
                half4 color = tex2D(_MainTex, saturate(uv));
                color.r = tex2D(_MainTex, saturate(uv + float2(spread, 0))).r;
                color.b = tex2D(_MainTex, saturate(uv - float2(spread, 0))).b;
                half luminance = dot(color.rgb, half3(0.2126, 0.7152, 0.0722));
                color.rgb = lerp(luminance.xxx, color.rgb, saturate(_JourneyColor.x));
                float vignette = smoothstep(_JourneyColor.z, 1.35, length(input.uv * 2.0 - 1.0));
                color.rgb *= 1.0 - saturate(_JourneyColor.y) * vignette;
                color.rgb *= 1.0 - saturate(_JourneyParams.x);
                return color;
            }
            ENDHLSL
        }
    }
}
