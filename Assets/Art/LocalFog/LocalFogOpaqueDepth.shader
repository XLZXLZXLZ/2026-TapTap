Shader "Hidden/TapTap/LocalFog/OpaqueDepth"
{
    SubShader
    {
        Pass
        {
            Cull Back ZWrite On ZTest LEqual Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _LocalFogOpaqueVP;
            float4x4 _LocalFogOpaqueView;
            float _LocalFogOpaqueFar;
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float eyeDepth : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                float4 world = float4(TransformObjectToWorld(input.positionOS), 1);
                output.positionCS = mul(_LocalFogOpaqueVP, world);
                output.eyeDepth = -mul(_LocalFogOpaqueView, world).z;
                return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                float normalizedDepth = saturate(input.eyeDepth / max(0.001, _LocalFogOpaqueFar));
                return float4(normalizedDepth, normalizedDepth, normalizedDepth, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}