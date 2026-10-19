#ifndef CODEX_WORLD_GRASS_SIZE_INCLUDED
#define CODEX_WORLD_GRASS_SIZE_INCLUDED
#include "Assets/ArtStyleTest/OutAssets/Codex_WorldSmallGrass/GrassBreeze.hlsl"
#include "Assets/ArtStyleTest/OutAssets/GrassSkyRepair/MeadowGrassDistanceBlend.hlsl"
// UV3 stores the original blade root and height; UV2 stores width and natural variation.
// Wind is evaluated by the original foliage graph before resizing the individual blade.
void WorldGrassSize_float(float3 Position, float4 Root, float4 Shape, out float3 Out)
{
    Out = Position;
    if (Root.w < 0.015 || Shape.x < 0.015) return;
    float sx = length(TransformObjectToWorldDir(float3(1,0,0), false));
    float sy = length(TransformObjectToWorldDir(float3(0,1,0), false));
    float sz = length(TransformObjectToWorldDir(float3(0,0,1), false));
    float heightScale = min(1.65, (0.80 * Shape.y) / max(0.01, Root.w * sy));
    float widthScale = min(1.65, (0.90 * Shape.z) / max(0.01, Shape.x * max(sx,sz)));
    float3 offset = Position - Root.xyz;
    Out = Root.xyz + offset * float3(widthScale, heightScale, widthScale);
    float3 ws = TransformObjectToWorld(Out);
    float3 rootWS = TransformObjectToWorld(Root.xyz);
    CodexGentleGrassBreeze(ws, rootWS, max(0.1, Root.w * sy * heightScale));
    MeadowBlendDistantGrass(ws,rootWS);
    Out = TransformWorldToObject(ws);
}
void WorldGrassSize_half(half3 Position, half4 Root, half4 Shape, out half3 Out)
{
    float3 p;
    WorldGrassSize_float(Position, Root, Shape, p);
    Out = p;
}
#endif
