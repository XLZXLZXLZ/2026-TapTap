Shader "Codex/PainterlyMeadow/SparseFlowers" {
 Properties {_PetalTint("Petal tint (linear)",Vector)=(1,1,1,1)}
 SubShader {
 Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
 Pass {
 Tags {"LightMode"="UniversalForward"}
 Cull Off ZWrite On
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fragment _ _LIGHT_COOKIES
 #pragma multi_compile_instancing
 #include "Assets/ArtStyleTest/OutAssets/Codex_PainterlyMeadow/Shaders/SparseFlowerVertex.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 #include "Assets/ArtStyleTest/OutAssets/Codex_PainterlyMeadow/Shaders/ContinuousGrassLighting.hlsl"
 half4 Frag(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS),i.positionWS,half4(1,1,1,1));Light full=GetMainLight();float direct=max(.001,dot(full.color,float3(.2126,.7152,.0722)));float cookie=saturate(dot(sun.color,float3(.2126,.7152,.0722))/direct);float lit=saturate(sun.shadowAttenuation*cookie);half3 c=i.colour.rgb*lerp(half3(1,1,1),_PetalTint.rgb,i.colour.a);c*=lerp(half3(.72,.84,.88),half3(1.03,1.02,.96),lit);c*=.90+.10*abs(dot(normalize(i.normalWS),sun.direction));float stemMask=(1-i.colour.a)*(1-step(.35,i.colour.r));
 float3 head=TransformObjectToWorld(float3(sin(.94*2.2)*.022,.94,.94*.94*.018));
 float visibleStem=max(.025,min(.13,distance(head,_WorldSpaceCameraPos)*.018));
 float submerged=smoothstep(visibleStem*.15,visibleStem,max(0,head.y-i.positionWS.y));
 InputData grassData=(InputData)0;grassData.positionWS=i.positionWS;grassData.shadowCoord=TransformWorldToShadowCoord(i.positionWS);grassData.shadowMask=half4(1,1,1,1);grassData.normalWS=half3(0,1,0);
 c=lerp(c,MeadowContinuousGrassColour(grassData),stemMask*submerged);return half4(c,1);}
 ENDHLSL
 }

 Pass {
 Name "DepthOnly"
 Tags {"LightMode"="DepthOnly"}
 Cull Off ZWrite On ColorMask 0
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment FlowerDepth
 #pragma multi_compile_instancing
 #include "Assets/ArtStyleTest/OutAssets/Codex_PainterlyMeadow/Shaders/SparseFlowerVertex.hlsl"
 half4 FlowerDepth(V i):SV_Target {return 0;}
 ENDHLSL
 }
 Pass {
 Name "DepthNormals"
 Tags {"LightMode"="DepthNormals"}
 Cull Off ZWrite On
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment FlowerNormal
 #pragma multi_compile_instancing
 #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
 #include "Assets/ArtStyleTest/OutAssets/Codex_PainterlyMeadow/Shaders/SparseFlowerVertex.hlsl"
 half4 FlowerNormal(V i):SV_Target {
 float3 n=normalize(i.normalWS);
 #if defined(_GBUFFER_NORMALS_OCT)
 float2 oct=PackNormalOctQuadEncode(n);return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
 #else
 return half4(n,0);
 #endif
 }
 ENDHLSL
 }
 }FallBack Off
}
