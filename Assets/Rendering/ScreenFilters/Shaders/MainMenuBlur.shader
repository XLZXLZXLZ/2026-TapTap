Shader "Hidden/TapTap/Main Menu Blur"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_MenuBlurred); SAMPLER(sampler_MenuBlurred);
        float4 _MainTex_TexelSize;
        float4 _MenuBlurDirection;
        float4 _MenuBlurMask;
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }
        half4 Blur(Varyings input) : SV_Target
        {
            float2 offset = _MenuBlurDirection.xy * abs(_MainTex_TexelSize.xy);
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * 0.227027027;
            color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + offset * 1.384615385) * 0.316216216;
            color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - offset * 1.384615385) * 0.316216216;
            color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + offset * 3.230769231) * 0.070270270;
            color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - offset * 3.230769231) * 0.070270270;
            return color;
        }
        half4 Composite(Varyings input) : SV_Target
        {
            // Mask uses bottom-to-top screen Y even when a render target is flipped.
            float y = input.uv.y;
            #if UNITY_UV_STARTS_AT_TOP
                y = 1.0 - y;
            #endif
            float boundary = lerp(_MenuBlurMask.x, _MenuBlurMask.y, y);
            float halfFeather = _MenuBlurMask.z * 0.5;
            float amount = (1.0 - smoothstep(boundary - halfFeather, boundary + halfFeather, input.uv.x)) * _MenuBlurMask.w;
            half4 sharp = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            half4 blurred = SAMPLE_TEXTURE2D(_MenuBlurred, sampler_MenuBlurred, input.uv);
            return lerp(sharp, blurred, amount);
        }
        ENDHLSL
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Blur
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
    }
}
