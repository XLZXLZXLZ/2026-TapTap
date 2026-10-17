Shader "TapTap/MagneticLinkRemake/Filament"
{
    Properties
    {
        [HDR] _Tint ("Light color", Color) = (4.7, 3.8, 0.15, 1)
        _Softness ("Soft edge", Float) = 1
        _FlowPhase ("Flow phase", Float) = 0
        _BandCount ("Flow bands", Float) = 5
        _FlowStrength ("Flow contrast", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Softness;
                float _FlowPhase;
                float _BandCount;
                float _FlowStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half falloff = pow(saturate(1.0 - abs(input.uv.y * 2.0 - 1.0)), max(0.1, _Softness));
                half band = pow(0.5 + 0.5 * cos((input.uv.x * _BandCount - _FlowPhase) * 6.2831853), 6.0);
                half brightness = lerp(1.0 - _FlowStrength, 1.0 + _FlowStrength, band);
                return half4(_Tint.rgb * brightness, _Tint.a * falloff);
            }
            ENDHLSL
        }
    }
}
