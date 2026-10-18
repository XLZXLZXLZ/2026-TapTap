Shader "ArtStyleTest/DetailedForestSky"
{
 Properties { _Zenith("Sky blue",Color)=(.45,.72,.85,1) _Horizon("Horizon haze",Color)=(.76,.86,.88,1) _Cloud("Cloud cream",Color)=(1,.98,.92,1) }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background"} Cull Off ZWrite Off
 Pass {HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 d:TEXCOORD0;};half4 _Zenith,_Horizon,_Cloud;
 V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.d=i.p.xyz;return o;}
 float hash(float p){return frac(sin(p*127.1)*43758.5453);}float noise(float p){float i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(hash(i),hash(i+1),f);}
 float ribbon(float2 p,float x,float y,float w,float h,float seed){float nx=(p.x-x)/w;float cy=y+.035*sin(nx*3+seed)+.015*noise(p.x*15+seed);float width=h*(.65+.35*noise(p.x*12+seed));float d=sqrt(nx*nx+pow((p.y-cy)/width,2));return 1-smoothstep(.72,1.05,d);}
 half4 frag(V i):SV_Target {float3 d=normalize(i.d);float2 p=float2(atan2(d.x,d.z),asin(d.y));float3 sky=lerp(_Horizon.rgb,_Zenith.rgb,pow(saturate(d.y),.42));float c=max(ribbon(p,-.57,.27,.69,.06,1),ribbon(p,.40,.30,.47,.028,8));c=max(c,ribbon(p,-.18,.13,.65,.03,21));c=max(c,ribbon(p,1.1,.22,.44,.05,4));sky=lerp(sky,_Cloud.rgb,c*.94);return half4(sky,1);}
 ENDHLSL}
 }
}
