Shader "TapTap/UI/Return Portrait Tear"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
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
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Attributes { float4 position : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float4 localPosition : TEXCOORD1; };
            sampler2D _MainTex;
            float4 _Tear, _ClipRect;
            fixed4 _TextureSampleAdd;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.localPosition = input.position;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            float Hash(float v) { return frac(sin(v * 127.1) * 43758.5453); }
            fixed4 Frag(Varyings input) : SV_Target
            {
                float band = floor(input.uv.y * 65.0);
                float tick = floor(_Tear.y * 32.0);
                float strength = _Tear.x;
                float2 uv = input.uv;
                uv.x += (Hash(band + tick * 8.3) - 0.5) * 0.20 * strength;
                fixed4 color = tex2D(_MainTex, saturate(uv)) + _TextureSampleAdd;
                color.r = tex2D(_MainTex, saturate(uv + float2(0.018 * strength, 0))).r;
                color.b = tex2D(_MainTex, saturate(uv - float2(0.018 * strength, 0))).b;
                color.rgb *= 1.0 - strength * 0.35 * step(0.65, Hash(band + tick));
                color *= input.color;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.localPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
