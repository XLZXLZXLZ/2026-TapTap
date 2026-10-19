Shader "Codex/Meadow/SurfaceMoss" {
 Properties {_Cutoff("Patch edge",Range(0,1))=.5}
 SubShader {Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest+1"} Cull Off
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 p:POSITION;float3 n:NORMAL;half4 colour:COLOR;};
 struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half4 colour:COLOR;float fog:TEXCOORD2;};
 CBUFFER_START(UnityPerMaterial)
 half _Cutoff;
 CBUFFER_END
 void MossEdge(V i){float grain=frac(sin(dot(floor(i.w*85),float3(12.9898,78.233,37.719)))*43758.5453);clip(i.colour.a-_Cutoff+(grain-.5)*.16);}
 V Vert(A a){V v;v.w=TransformObjectToWorld(a.p.xyz);v.p=TransformWorldToHClip(v.w);v.n=TransformObjectToWorldNormal(a.n);v.colour=a.colour;v.fog=ComputeFogFactor(v.p.z);return v;}
 ENDHLSL
 Pass {Name "MossForward" Tags {"LightMode"="UniversalForward"} ZWrite On
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fragment _ _LIGHT_COOKIES
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 #include "Assets/ArtStyleTest/OutAssets/Codex_PainterlyMeadow/Shaders/ContinuousGrassLighting.hlsl"
 half4 Frag(V i):SV_Target{MossEdge(i);InputData g=(InputData)0;g.positionWS=i.w;g.shadowCoord=TransformWorldToShadowCoord(i.w);g.shadowMask=half4(1,1,1,1);g.normalWS=normalize(i.n);Light sun=GetMainLight();half shape=.72+.28*saturate(dot(g.normalWS,sun.direction));half3 colour=MeadowContinuousGrassColour(g)*shape*lerp(.90,1.04,i.colour.r)*half3(1.01,1.01,.98);return half4(MixFog(colour,i.fog),1);}
 ENDHLSL }
 Pass {Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ZWrite On ColorMask 0
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment FragDepth
 half4 FragDepth(V i):SV_Target{MossEdge(i);return 0;}
 ENDHLSL }
 Pass {Name "DepthNormals" Tags {"LightMode"="DepthNormals"} ZWrite On
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment FragNormals
 #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
 half4 FragNormals(V i):SV_Target{MossEdge(i);float3 n=normalize(i.n);
 #if defined(_GBUFFER_NORMALS_OCT)
 return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*.5+.5)),0);
 #else
 return half4(n,0);
 #endif
 }
 ENDHLSL }
 }Fallback Off
}
