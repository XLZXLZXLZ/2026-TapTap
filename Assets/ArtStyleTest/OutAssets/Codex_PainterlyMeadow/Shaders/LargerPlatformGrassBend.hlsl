#ifndef CODEX_PLATFORM_GRASS_BEND_INCLUDED
#define CODEX_PLATFORM_GRASS_BEND_INCLUDED
#include "Assets/ArtStyleTest/OutAssets/Codex_WorldSmallGrass/GrassBreeze.hlsl"
float4 _CodexPlatformGrassContacts[16];
float4 _CodexPlatformGrassStrengths[16];
void PlatformGrassBend_float(float3 Position, out float3 Out)
{
    float3 ws = TransformObjectToWorld(Position);
    float2 push = 0;
    float flatten = 0;
    [unroll] for (int i = 0; i < 16; i++)
    {
        float4 contact = _CodexPlatformGrassContacts[i];
        float2 delta = ws.xz - contact.xz;
        float distanceToContact = length(delta);
        float influence = 1.0 - smoothstep(contact.w * 0.18, max(0.01, contact.w), distanceToContact);
        influence *= _CodexPlatformGrassStrengths[i].x;
        push += (delta + float2(0.0001, 0.0002)) / max(distanceToContact, 0.02) * influence;
        flatten = max(flatten, influence);
    }
    float height = saturate(Position.y / 0.82);
    float tipWeight = height * height;
    float pushLength = length(push);
    push /= max(1.0, pushLength);
    ws.xz += push * (0.82 * tipWeight);
    ws.y -= flatten * (0.50 * tipWeight);
    float3 rootWS = TransformObjectToWorld(float3(Position.x, 0, Position.z));
    CodexGentleGrassBreeze(ws, rootWS, 0.82);
    Out = TransformWorldToObject(ws);
}
void PlatformGrassBend_half(half3 Position, out half3 Out)
{
    float3 p;
    PlatformGrassBend_float(Position, p);
    Out = p;
}
#endif
