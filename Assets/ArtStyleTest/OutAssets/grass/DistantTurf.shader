Shader "Codex/Meadow/DistantTurf" {
 Properties {
 _TurfColour("草皮颜色",Color)=(.12,.30,.06,1)
 _Coverage("覆盖率",Range(0,1))=.54
 _PatchSize("草皮块大小（米）",Range(.5,20))=8
 _TopBias("顶部覆盖优先",Range(0,.6))=.20
 _EdgeBreakup("边缘细碎程度",Range(0,.2))=.015
 }
 SubShader {Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest+1"} Cull Off
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 p:POSITION;float3 n:NORMAL;float4 uv:TEXCOORD0;};
 struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;float height:TEXCOORD2;float fog:TEXCOORD3;};
 CBUFFER_START(UnityPerMaterial)
 half4 _TurfColour;float _Coverage,_PatchSize,_TopBias,_EdgeBreakup;
 CBUFFER_END
 float Hash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
 float Noise(float3 p){float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+1),f.x),f.y),f.z);}
 float Field(V i){float3 q=i.w/max(.1,_PatchSize)+float3(17,29,43);float field=Noise(q)*.86+Noise(q*2.1+7)*.14;float top=max(saturate(i.n.y),smoothstep(.4,1,i.height)*.7);return saturate(field-_TopBias*top+(Noise(i.w*5)-.5)*_EdgeBreakup);}
 void TurfEdge(V i){if(_Coverage<.001)clip(-1);if(_Coverage>.999)return;clip(_Coverage-Field(i));}
 V Vert(A a){V v;v.w=TransformObjectToWorld(a.p.xyz);v.p=TransformWorldToHClip(v.w);v.n=TransformObjectToWorldNormal(a.n);v.height=a.uv.x;v.fog=ComputeFogFactor(v.p.z);return v;}
 ENDHLSL
 Pass {Name "TurfForward" Tags {"LightMode"="UniversalForward"} ZWrite On
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 half4 Frag(V i):SV_Target{TurfEdge(i);Light sun=GetMainLight();half shape=.82+.18*saturate(dot(normalize(i.n),sun.direction));half variation=lerp(.96,1.04,Noise(i.w*.5));half3 colour=_TurfColour.rgb*shape*variation;return half4(MixFog(colour,i.fog),1);}
 ENDHLSL }
 Pass {Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ZWrite On ColorMask 0
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment FragDepth
 half4 FragDepth(V i):SV_Target{TurfEdge(i);return 0;}
 ENDHLSL }
 Pass {Name "DepthNormals" Tags {"LightMode"="DepthNormals"} ZWrite On
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment FragNormals
 #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
 half4 FragNormals(V i):SV_Target{TurfEdge(i);float3 n=normalize(i.n);
 #if defined(_GBUFFER_NORMALS_OCT)
 return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*.5+.5)),0);
 #else
 return half4(n,0);
 #endif
 }
 ENDHLSL }
 }Fallback Off
}
