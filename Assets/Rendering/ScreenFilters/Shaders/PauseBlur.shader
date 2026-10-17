Shader "Hidden/TapTap/Pause Blur"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_PauseBlurTexture); SAMPLER(sampler_PauseBlurTexture);
        float4 _MainTex_TexelSize;
        float4 _PauseBlurDirection;
        float _PauseBlurAmount;
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
            float2 offset = _PauseBlurDirection.xy * _MainTex_TexelSize.xy * lerp(1.0, 4.0, _PauseBlurAmount);
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * 0.227027;
            color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + offset * 1.384615) * 0.316216;
            color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - offset * 1.384615) * 0.316216;
            color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + offset * 3.230769) * 0.070270;
            color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - offset * 3.230769) * 0.070270;
            return color;
        }
        half4 Composite(Varyings input) : SV_Target
        {
            return lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv),
                SAMPLE_TEXTURE2D(_PauseBlurTexture, sampler_PauseBlurTexture, input.uv), _PauseBlurAmount);
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
