Shader "TapTap/UI/Neon Level Outline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Emission ("HDR Emission", Float) = 3.2
        _TextMode ("Font Mode", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One
        ColorMask [_ColorMask]
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _ClipRect;
                float _Emission;
                float _TextMode;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float2 animation:TEXCOORD1; };
            struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float2 local:TEXCOORD1; float2 animation:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.local=v.positionOS.xy; o.color=v.color*_Color; o.uv=v.uv; o.animation=v.animation;
                return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                float alpha;
                float3 energy;
                if (_TextMode>0.5)
                {
                    alpha=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a*i.color.a;
                    energy=i.color.rgb*_Emission;
                }
                else
                {
                    clip(i.animation.x-i.uv.y+0.00001);
                    // Inset the fine core so neighbouring rooms retain separate colours.
                    // Screen derivatives keep the stroke smooth at different UI scales.
                    float d=abs(i.uv.x-0.30);
                    float aa=max(fwidth(i.uv.x)*0.65,0.015);
                    float core=1.0-smoothstep(0.065,0.065+aa,d);
                    float glow=exp2(-d*d*65.0)*0.10;
                    float edgeFade=smoothstep(0.0,0.13,i.uv.x)*(1.0-smoothstep(0.70,1.0,i.uv.x));
                    float head=exp2(-pow((i.uv.y-i.animation.x)*45.0,2.0))*(1.0-step(0.999,i.animation.x));
                    energy=lerp(i.color.rgb,1.0,core*0.04)*_Emission*(1.0+i.animation.y*0.25+head*0.65);
                    alpha=i.color.a*saturate(core+glow)*edgeFade;
                }
                #ifdef UNITY_UI_CLIP_RECT
                float2 inside=step(_ClipRect.xy,i.local)*step(i.local,_ClipRect.zw);
                alpha*=inside.x*inside.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha-0.001);
                #endif
                return float4(energy,alpha);
            }
            ENDHLSL
        }
    }
}
