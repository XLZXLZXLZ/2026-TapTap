Shader "Codex/Meadow/LocalVolumetricFog" {
 Properties {[HideInInspector]_MainTex("Scene colour",2D)="white"{} _NoiseVolume("Density noise",3D)="white"{}}
 SubShader{Tags{"RenderPipeline"="UniversalPipeline"} Cull Off ZWrite Off ZTest Always
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
 TEXTURE2D(_MeadowFogBuffer);SAMPLER(sampler_MeadowFogBuffer);
 TEXTURE3D(_NoiseVolume);SAMPLER(sampler_NoiseVolume);
 TEXTURE2D(_ShaftCloudCookie);SAMPLER(sampler_ShaftCloudCookie);
 float4x4 _ShaftWorldToLight;float4 _ShaftLightDirection,_ShaftCookieMotion;
 float4 _MainTex_TexelSize,_MeadowFogBuffer_TexelSize;
 float4x4 _FogWorldToLocal,_FogInverseViewProjection;float3 _FogCameraPosition;
 float3 _FogHalfSize;
 float4 _FogParams,_FogDetail,_FogAnimation,_FogColour,_SunScatterColour,_FogLighting,_ReferenceShafts;
 float4 _MeadowAerialParams,_MeadowAerialColour;
 struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
 struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
 V Vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;}
 float DeviceDepth(float2 uv){return SampleSceneDepth(uv);}
 float3 WorldAt(float2 uv,float depth){
 #if !UNITY_REVERSED_Z
 depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
 #endif
 return ComputeWorldSpacePosition(uv,depth,_FogInverseViewProjection);}
 float Density(float3 world){float3 p=mul(_FogWorldToLocal,float4(world,1)).xyz;float3 edge=_FogHalfSize-abs(p);float weight=smoothstep(0,_FogParams.y,min(edge.x,min(edge.y,edge.z)));float h=exp(-max(0,world.y-_FogParams.w)/_FogParams.z);float3 uv=world*_FogDetail.z+float3(_FogAnimation.x,0,_FogAnimation.y)*.015;float n=SAMPLE_TEXTURE3D_LOD(_NoiseVolume,sampler_NoiseVolume,uv,0).r;return _FogParams.x*weight*h*lerp(1,n*1.65,_FogDetail.y);}
 ENDHLSL
 Pass{Name "Integrate local mist"
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Fog
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fragment _ _LIGHT_COOKIES
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 float CloudTransmission(float3 p){
 #if defined(_LIGHT_COOKIES)
 if(IsMainLightCookieEnabled()){float2 uv=ComputeLightCookieUVDirectional(_MainLightWorldToLight,p,float4(1,1,0,0),URP_TEXTURE_WRAP_MODE_NONE);return SAMPLE_TEXTURE2D_LOD(_MainLightCookieTexture,sampler_MainLightCookieTexture,uv,0).r;}
 #endif
 return 1;
 }
 // Separate in-scattering preserves the reference lighting and its original mist extinction.
 float3 ReferenceShafts(float3 origin,float3 ray,float limit,float jitter){
 if(_ReferenceShafts.x<.5||_ReferenceShafts.y<=0)return 0;
 float3 halfSize=float3(_FogHalfSize.x,_ReferenceShafts.z*.5,_FogHalfSize.z);
 float3 localOrigin=mul(_FogWorldToLocal,float4(origin,1)).xyz-float3(0,_ReferenceShafts.w,0);
 float3 direction=mul((float3x3)_FogWorldToLocal,ray);float3 inv=rcp(direction+1e-7);
 float3 a=(-halfSize-localOrigin)*inv,b=(halfSize-localOrigin)*inv;
 float3 nearPlane=min(a,b),farPlane=max(a,b);
 float start=max(_FogDetail.x,max(0,max(nearPlane.x,max(nearPlane.y,nearPlane.z))));
 float end=min(limit,min(farPlane.x,min(farPlane.y,farPlane.z)));if(end<=start)return 0;
 float stride=(end-start)/48,t=start+stride*lerp(.2,.8,jitter),trans=1;float3 total=0;
 float g=.55;float cosTheta=dot(ray,_ShaftLightDirection.xyz);
 float phase=min(1.1,.25*(1-g*g)/pow(max(.02,1+g*g-2*g*cosTheta),1.5));
 [loop]for(int j=0;j<48;j++){
 float3 p=origin+ray*t;float3 local=mul(_FogWorldToLocal,float4(p,1)).xyz-float3(0,_ReferenceShafts.w,0);
 float3 edge=halfSize-abs(local);float weight=smoothstep(0,_FogParams.y,min(edge.x,min(edge.y,edge.z)));
 float height=exp(-max(0,p.y-_FogParams.w)/26);
 float3 uv=p*_FogDetail.z+float3(_FogAnimation.x,0,_FogAnimation.y)*.015;
 float noise=SAMPLE_TEXTURE3D_LOD(_NoiseVolume,sampler_NoiseVolume,uv,0).r;
 float density=_FogParams.x*.6*weight*height*lerp(1,noise*1.65,_FogDetail.y);
 float attenuation=exp(-density*stride);
 float2 cookieUV=mul(_ShaftWorldToLight,float4(p,1)).xy/100+.5+_ShaftCookieMotion.xy;
 float cookie=SAMPLE_TEXTURE2D_LOD(_ShaftCloudCookie,sampler_ShaftCloudCookie,cookieUV,0).r;
 float gap=smoothstep(.28,.9,cookie)*cookie;
 total+=trans*(1-attenuation)*_SunScatterColour.rgb*float3(3.2,2.91,2.14)*gap*_ReferenceShafts.y*phase;
 trans*=attenuation;t+=stride;
 }return total;
 }
 half4 Fog(V i):SV_Target{
 float depth=DeviceDepth(i.uv);float3 endpoint=WorldAt(i.uv,depth);float3 origin=_FogCameraPosition;float distanceToSurface=length(endpoint-origin);float3 ray=(endpoint-origin)/max(distanceToSurface,.001);float3 localOrigin=mul(_FogWorldToLocal,float4(origin,1)).xyz;float3 localDir=mul((float3x3)_FogWorldToLocal,ray);float3 inv=rcp(localDir+1e-7);float3 a=(-_FogHalfSize-localOrigin)*inv,b=(_FogHalfSize-localOrigin)*inv;float3 nearPlane=min(a,b),farPlane=max(a,b);float start=max(_FogDetail.x,max(0,max(nearPlane.x,max(nearPlane.y,nearPlane.z))));float end=min(distanceToSurface,min(farPlane.x,min(farPlane.y,farPlane.z)));if(end<=start){float jitter=frac(52.9829189*frac(dot(i.positionCS.xy,float2(.06711056,.00583715))));return half4(ReferenceShafts(origin,ray,distanceToSurface,jitter),1);}
 int steps=(int)_FogAnimation.z;float stepLength=(end-start)/max(1,steps);float jitter=frac(52.9829189*frac(dot(i.positionCS.xy,float2(.06711056,.00583715))));float t=start+stepLength*lerp(.2,.8,jitter);float transmittance=1;float3 total=0;Light sun=GetMainLight();float cosTheta=dot(ray,sun.direction);float g=_FogLighting.x;float phase=min(1.1,.25*(1-g*g)/pow(max(.02,1+g*g-2*g*cosTheta),1.5));float referencePhase=.25+.75*pow(saturate(cosTheta*.5+.5),5);
 [loop]for(int j=0;j<64;j++){if(j>=steps||transmittance<.025)break;float3 p=origin+ray*t;float density=Density(p);float attenuation=exp(-density*stepLength);float4 shadowCoord=TransformWorldToShadowCoord(p);Light l=GetMainLight(shadowCoord);
#if defined(_LIGHT_COOKIES)
if(IsMainLightCookieEnabled()){float2 cuv=ComputeLightCookieUVDirectional(_MainLightWorldToLight,p,float4(1,1,0,0),URP_TEXTURE_WRAP_MODE_NONE);l.color*=SAMPLE_TEXTURE2D_LOD(_MainLightCookieTexture,sampler_MainLightCookieTexture,cuv,0).rgb;}
#endif
float3 illumination=_FogColour.rgb*_FogLighting.y*lerp(1,.8,saturate(p.y/50))+_SunScatterColour.rgb*l.color*l.shadowAttenuation*_FogDetail.w*phase;
if(_ReferenceShafts.x>.5)illumination=_FogColour.rgb*(.55+.15*saturate(p.y/20))+_SunScatterColour.rgb*l.color*l.shadowAttenuation*_FogDetail.w*(.45+referencePhase);
total+=transmittance*(1-attenuation)*illumination;transmittance*=attenuation;t+=stepLength;}total+=ReferenceShafts(origin,ray,distanceToSurface,jitter);return half4(total,transmittance);}
 ENDHLSL}
 Pass{Name "Depth aware composite"
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Composite
 half4 Composite(V i):SV_Target{half4 colour=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);float d=LinearEyeDepth(DeviceDepth(i.uv),_ZBufferParams);float2 pixel=i.uv*_MeadowFogBuffer_TexelSize.zw-.5;float2 basePixel=floor(pixel);float2 f=frac(pixel);float4 integrated=0;float sum=0;
 [unroll]for(int y=0;y<2;y++){[unroll]for(int x=0;x<2;x++){float2 uv=(basePixel+float2(x,y)+.5)*_MeadowFogBuffer_TexelSize.xy;float sampled=LinearEyeDepth(DeviceDepth(uv),_ZBufferParams);float bilinear=(x?f.x:1-f.x)*(y?f.y:1-f.y);float weight=(bilinear+.0001)*exp(-abs(sampled-d)*1.5/max(1,d*.025));integrated+=SAMPLE_TEXTURE2D(_MeadowFogBuffer,sampler_MeadowFogBuffer,uv)*weight;sum+=weight;}}
 if(sum<.00001)integrated=SAMPLE_TEXTURE2D(_MeadowFogBuffer,sampler_MeadowFogBuffer,i.uv);else integrated/=sum;
 half3 result=colour.rgb*integrated.a+integrated.rgb;
 // A separate depth tint keeps nearby warm and cool grass intact. Distant sky geometry is excluded.
 float aerial=smoothstep(_MeadowAerialParams.x,max(_MeadowAerialParams.x+1,_MeadowAerialParams.y),d)*_MeadowAerialParams.z*_MeadowAerialParams.w;
 aerial*=1-smoothstep(650,900,d);result=lerp(result,_MeadowAerialColour.rgb,aerial);
 return half4(result,colour.a);}
 ENDHLSL}
 }FallBack Off
}
