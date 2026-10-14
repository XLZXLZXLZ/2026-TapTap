Shader "Hidden/ArtStyleTest/CrispPixelation"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "Assets/Rendering/Shaders/Fullscreen.hlsl"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _PixelSize;

            float4 frag(v2f_img input) : SV_Target
            {
                float2 dimensions = _MainTex_TexelSize.zw;
                float size = max(1.0, floor(_PixelSize));
                // Snap to the center of an actual source texel, never between texels.
                float2 texel = floor(input.uv * dimensions / size) * size
                    + floor(size * 0.5) + 0.5;
                texel = min(texel, dimensions - 0.5);
                return tex2Dlod(_MainTex, float4(texel / dimensions, 0, 0));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
