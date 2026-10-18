Shader "ArtStyleTest/ForestCloudSky"
{
 Properties { _Zenith("Zenith",Color)=(0.39,0.68,0.81,1) _Horizon("Horizon",Color)=(0.75,0.85,0.88,1) _Cloud("Cloud ivory",Color)=(0.99,0.97,0.89,1) }
 SubShader { Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" } Cull Off ZWrite Off
 Pass { HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A { float4 positionOS:POSITION; }; struct V {float4 positionCS:SV_POSITION; float3 dir:TEXCOORD0;};
 half4 _Zenith,_Horizon,_Cloud;
 V vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.dir=i.positionOS.xyz;return o; }
 float blob(float2 uv,float2 center,float2 radius){return 1-smoothstep(.72,1.0,length((uv-center)/radius));}
 half4 frag(V i):SV_Target {float3 d=normalize(i.dir);float h=saturate(d.y);float3 c=lerp(_Horizon.rgb,_Zenith.rgb,pow(h,.4));float2 uv=float2(atan2(d.x,d.z),asin(d.y));float cloud=0;cloud=max(cloud,blob(uv,float2(-.62,.30),float2(.55,.05)));cloud=max(cloud,blob(uv,float2(-.40,.255),float2(.48,.045)));cloud=max(cloud,blob(uv,float2(.36,.31),float2(.39,.025)));cloud=max(cloud,blob(uv,float2(.66,.33),float2(.24,.04)));cloud=max(cloud,blob(uv,float2(.05,.12),float2(.58,.025)));cloud=max(cloud,blob(uv,float2(-.72,.42),float2(.32,.09)));cloud=max(cloud,blob(uv,float2(.82,.19),float2(.35,.055)));c=lerp(c,_Cloud.rgb,cloud*.9);return half4(c,1);}
 ENDHLSL } }
}
