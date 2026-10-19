Shader "Codex/Meadow/EditableSky" {
 Properties {
 _Zenith("Sky blue",Color)=(.49803922,.74509805,.92941177,1)
 _Horizon("Horizon haze",Color)=(.76862746,.87058824,.9372549,1)
 _Cloud("Cloud cream",Color)=(1,.9882353,.94509804,1)
 _SkyRotation("Cloud rotation",Range(-180,180))=0
 _CloudOpacity("Cloud opacity",Range(0,1))=.94
 _CloudSoftness("Edge softness",Range(.2,3))=1
 _Cloud1("Cloud 1",Vector)=(-32.6585943,15.4698605,39.5340881,3.4377468)
 _Cloud2("Cloud 2",Vector)=(22.9183121,17.1887341,26.9290161,1.6042818)
 _Cloud3("Cloud 3",Vector)=(-10.3132401,7.4484515,37.2422562,1.7188734)
 _Cloud4("Cloud 4",Vector)=(63.0253563,12.6050711,25.2101421,2.8647890)
 }
 SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background"} Cull Off ZWrite Off
 Pass {HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 d:TEXCOORD0;};
 half4 _Zenith,_Horizon,_Cloud;float _SkyRotation,_CloudOpacity,_CloudSoftness;float4 _Cloud1,_Cloud2,_Cloud3,_Cloud4;
 V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.d=i.p.xyz;return o;}
 float hash(float p){return frac(sin(p*127.1)*43758.5453);}float noise(float p){float i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(hash(i),hash(i+1),f);}
 float ribbon(float2 p,float4 layer,float seed){layer=radians(layer);float nx=(p.x-layer.x)/max(.0001,layer.z);float cy=layer.y+.035*sin(nx*3+seed)+.015*noise(p.x*15+seed);float width=max(.0001,layer.w)*(.65+.35*noise(p.x*12+seed));float d=sqrt(nx*nx+pow((p.y-cy)/width,2));float softness=max(.2,_CloudSoftness);return 1-smoothstep(.885-.165*softness,.885+.165*softness,d);}
 half4 frag(V i):SV_Target {float3 d=normalize(i.d);float2 p=float2(atan2(d.x,d.z)-radians(_SkyRotation),asin(d.y));float3 sky=lerp(_Horizon.rgb,_Zenith.rgb,pow(saturate(d.y),.42));float c=max(ribbon(p,_Cloud1,1),ribbon(p,_Cloud2,8));c=max(c,ribbon(p,_Cloud3,21));c=max(c,ribbon(p,_Cloud4,4));return half4(lerp(sky,_Cloud.rgb,c*_CloudOpacity),1);}
 ENDHLSL}
 }
 CustomEditor "CodexOutTransfer.MeadowSkyShaderGUI"
}
