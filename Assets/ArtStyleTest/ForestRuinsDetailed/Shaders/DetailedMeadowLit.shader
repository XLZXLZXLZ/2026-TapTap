Shader "ArtStyleTest/DetailedMeadowLit"
{
 Properties { _BaseColor("Pigment",Color)=(1,1,1,1) _BaseMap("Base",2D)="white"{} _Cutoff("Cutoff",Float)=0.5 _Cull("Cull",Float)=0 _Surface("Surface",Float)=0 }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 Pass { Name "MeadowForward" Tags {"LightMode"="UniversalForward"} Cull [_Cull]
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 half4 _BaseColor;float4 _BaseMap_ST;half _Cutoff;half _Surface;
 CBUFFER_END
 struct A{float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR;};struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half4 c:COLOR;float fog:TEXCOORD2;};
 V vert(A i){V o;o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);o.c=i.c;o.fog=ComputeFogFactor(o.p.z);return o;}
 half4 frag(V i):SV_Target {half3 n=normalize(i.n);Light l=GetMainLight(TransformWorldToShadowCoord(i.w));half ndl=saturate((dot(n,l.direction)+.13)/1.13);half3 ambient=lerp(half3(.065,.12,.16),half3(.14,.21,.24),saturate(n.y*.5+.5));half3 rgb=_BaseColor.rgb*i.c.rgb*(ambient+l.color*ndl*lerp(.07,1,l.shadowAttenuation));rgb*=lerp(half3(.75,.88,1.05),half3(1,1,1),l.shadowAttenuation);float dist=distance(_WorldSpaceCameraPos,i.w);float hf=(1-exp(-max(0,dist-48)*.009))*saturate((20-i.w.y)/25)*.24;rgb=lerp(rgb,unity_FogColor.rgb,hf);return half4(MixFog(rgb,i.fog),1);}
 ENDHLSL }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }
}

