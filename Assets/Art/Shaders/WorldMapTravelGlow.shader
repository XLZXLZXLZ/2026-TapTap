Shader "TapTap/UI/World Map Travel Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Glow ("HDR Glow", Float) = 7
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 position : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float2 localPosition : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                float _Glow;
                float4 _ClipRect;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position);
                output.localPosition = input.position.xy;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = input.color;
                if (input.uv.x < 1.5)
                {
                    float radius = length(input.uv * 2.0 - 1.0);
                    float core = 1.0 - smoothstep(0.56, 0.68, radius);
                    float halo = pow(saturate(1.0 - radius), 1.4) * 0.7;
                    color.a *= saturate(core + halo);
                }
                #ifdef UNITY_UI_CLIP_RECT
                float2 inside = step(_ClipRect.xy, input.localPosition) * step(input.localPosition, _ClipRect.zw);
                color.a *= inside.x * inside.y;
                #endif
                return half4(color.rgb * _Glow, color.a);
            }
            ENDHLSL
        }
    }
}
