#ifndef CODEX_DISTANT_GRASS_VERTEX_INCLUDED
#define CODEX_DISTANT_GRASS_VERTEX_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/ArtStyleTest/OutAssets/Codex_WorldSmallGrass/GrassBreeze.hlsl"
#include "Assets/ArtStyleTest/OutAssets/GrassSkyRepair/MeadowGrassDistanceBlend.hlsl"
struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 root:TEXCOORD0;};
struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;};
V Vert(A i){V o;float3 p=TransformObjectToWorld(i.positionOS.xyz);float3 root=TransformObjectToWorld(i.root.xyz);CodexGentleGrassBreeze(p,root,max(.5,i.root.w));MeadowBlendDistantGrass(p,root);o.positionWS=p;o.positionCS=TransformWorldToHClip(p);o.normalWS=float3(0,1,0);return o;}
#endif
