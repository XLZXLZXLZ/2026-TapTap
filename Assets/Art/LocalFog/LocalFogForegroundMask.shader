Shader "Hidden/TapTap/LocalFog/ForegroundMask"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _LocalFogMaskVP;
            struct Attributes { float4 vertex : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = mul(_LocalFogMaskVP, float4(TransformObjectToWorld(input.vertex.xyz), 1));
                return output;
            }
            half4 frag(Varyings input) : SV_Target { return half4(1,1,1,1); }
            ENDHLSL
        }
    }
    Fallback Off
}