#ifndef TAPTAP_FULLSCREEN_INCLUDED
#define TAPTAP_FULLSCREEN_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct FullscreenAttributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
struct v2f_img { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
v2f_img vert_img(FullscreenAttributes input)
{
    v2f_img output;
    output.pos = TransformObjectToHClip(input.vertex.xyz);
    output.uv = input.uv;
    return output;
}
#endif
