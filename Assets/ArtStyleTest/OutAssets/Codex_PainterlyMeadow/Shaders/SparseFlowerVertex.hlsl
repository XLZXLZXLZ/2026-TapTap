#ifndef CODEX_SPARSE_FLOWER_VERTEX_INCLUDED
#define CODEX_SPARSE_FLOWER_VERTEX_INCLUDED
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Assets/ArtStyleTest/OutAssets/Codex_WorldSmallGrass/GrassBreeze.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _PetalTint;
 CBUFFER_END
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 colour:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 colour:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
 V Vert(A i){V o=(V)0;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);float3 p=TransformObjectToWorld(i.positionOS.xyz);float3 root=TransformObjectToWorld(float3(0,0,0));float3 swaying=p;CodexGentleGrassBreeze(swaying,root,max(.1,length(TransformObjectToWorldDir(float3(0,.95,0),false))));p=lerp(p,swaying,.35);o.positionWS=p;o.positionCS=TransformWorldToHClip(p);o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.colour=i.colour;return o;}

#endif
