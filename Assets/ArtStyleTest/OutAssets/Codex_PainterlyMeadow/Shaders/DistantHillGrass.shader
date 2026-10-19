Shader "Codex/PainterlyMeadow/DistantHillGrass" {
SubShader {
 Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
 Cull Off
 Pass {
 Tags {"LightMode"="UniversalForward"} ZWrite On
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fragment _ _LIGHT_COOKIES
 #pragma multi_compile_fog
 #include "Assets/ArtStyleTest/OutAssets/Codex_PainterlyMeadow/Shaders/DistantGrassVertex.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 #include "Assets/ArtStyleTest/OutAssets/Codex_PainterlyMeadow/Shaders/ContinuousGrassLighting.hlsl"
 half4 Frag(V i):SV_Target {InputData g=(InputData)0;g.positionWS=i.positionWS;g.shadowCoord=TransformWorldToShadowCoord(i.positionWS);g.shadowMask=half4(1,1,1,1);g.normalWS=half3(0,1,0);half3 colour=MeadowContinuousGrassColour(g);float fog=ComputeFogFactor(TransformWorldToHClip(i.positionWS).z);return half4(MixFog(colour,fog),1);}
 ENDHLSL
 }
 Pass {
 Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ZWrite On ColorMask 0
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Depth
 #include "Assets/ArtStyleTest/OutAssets/Codex_PainterlyMeadow/Shaders/DistantGrassVertex.hlsl"
 half4 Depth(V i):SV_Target {return 0;}
 ENDHLSL
 }
 Pass {
 Name "DepthNormals" Tags {"LightMode"="DepthNormals"} ZWrite On
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Normal
 #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
 #include "Assets/ArtStyleTest/OutAssets/Codex_PainterlyMeadow/Shaders/DistantGrassVertex.hlsl"
 half4 Normal(V i):SV_Target {
 #if defined(_GBUFFER_NORMALS_OCT)
 return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(float3(0,1,0))*.5+.5)),0);
 #else
 return half4(0,1,0,0);
 #endif
 }
 ENDHLSL
 }
}Fallback Off
}
