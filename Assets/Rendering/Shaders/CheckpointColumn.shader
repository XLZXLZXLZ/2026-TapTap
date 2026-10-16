Shader "TapTap/Checkpoint Column"
{
    Properties
    {
        [HDR] _Color ("Green glow and opacity", Color) = (0.45, 1, 0.16, 0.2)
        _HeightRange ("Local Z bottom and inverse height", Vector) = (-0.02, 25, 0, 0)
        _FadeStart ("Height at which fading starts", Range(0, 0.8)) = 0.05
        _FadePower ("Fade softness", Range(0.5, 4)) = 1.4
        _Drift ("Upward drift", Range(0, 0.15)) = 0.045
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _HeightRange;
                float _FadeStart, _FadePower, _Drift;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float height = saturate((input.positionOS.z - _HeightRange.x) * _HeightRange.y);
                float2 around = input.positionOS.xy * _HeightRange.y;
                float drift = sin(around.x * 13.0 + height * 9.0 - _Time.y * 1.2)
                    * sin(around.y * 11.0 - height * 7.0 + _Time.y * 0.7);
                // Keep the very top fully transparent, including the model's cap.
                float fade = pow(1.0 - smoothstep(_FadeStart, 1.0, height), _FadePower);
                fade *= 1.0 + drift * _Drift;
                return half4(_Color.rgb, _Color.a * fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
