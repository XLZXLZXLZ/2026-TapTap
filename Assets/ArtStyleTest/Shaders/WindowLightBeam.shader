Shader "ArtStyleTest/Window Light Beam"
{
    Properties
    {
        _Color ("Light Color and Opacity", Color) = (1,0.88,0.62,0.18)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            struct appdata { float4 vertex : POSITION; half4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; half4 color : COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.color = v.color;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                return half4(_Color.rgb, _Color.a * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
