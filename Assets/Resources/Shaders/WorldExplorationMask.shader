Shader "Hidden/TapTap/World Exploration Mask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        // Union of room interiors and the actual outer model silhouettes.
        Pass
        {
            Name "Region silhouette"
            ColorMask R
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _WorldExplorationViewProjection;
            half _WorldExplorationRegionOpacity;
            float4 Vert(float3 positionOS : POSITION) : SV_POSITION
            {
                return mul(_WorldExplorationViewProjection, float4(TransformObjectToWorld(positionOS), 1));
            }
            half4 Frag() : SV_Target { return half4(_WorldExplorationRegionOpacity, 0, 0, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "Cover unexplored pixels"
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_WorldExplorationMask);
            SAMPLER(sampler_WorldExplorationMask);
            TEXTURE2D(_WorldExplorationBackground);
            SAMPLER(sampler_WorldExplorationBackground);
            half _WorldExplorationUseBackground;
            half4 _WorldExplorationMaskColor;
            half4 _WorldExplorationVignetteColor;
            float4 _WorldExplorationVignetteParams;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half visible = SAMPLE_TEXTURE2D(_WorldExplorationMask, sampler_WorldExplorationMask, input.uv).r;
                half3 background = SAMPLE_TEXTURE2D(_WorldExplorationBackground, sampler_WorldExplorationBackground, input.uv).rgb;
                half3 replacement = lerp(_WorldExplorationMaskColor.rgb, background, _WorldExplorationUseBackground);
                if (_WorldExplorationVignetteParams.z > 0)
                {
                    // Match URP ApplyVignette without darkening the already processed scene.
                    float2 dist = abs(input.uv - _WorldExplorationVignetteParams.xy) * _WorldExplorationVignetteParams.z;
                    dist.x *= _WorldExplorationVignetteColor.w;
                    float factor = pow(saturate(1.0 - dot(dist, dist)), _WorldExplorationVignetteParams.w);
                    replacement *= lerp(_WorldExplorationVignetteColor.rgb, (1.0).xxx, factor);
                }
                return half4(replacement, 1 - visible);
            }
            ENDHLSL
        }
    }
}
