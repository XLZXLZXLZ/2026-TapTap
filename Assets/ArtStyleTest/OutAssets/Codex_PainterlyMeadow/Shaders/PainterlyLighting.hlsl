#ifndef CODEX_MEADOW_PAINTERLY_LIGHTING_INCLUDED
#define CODEX_MEADOW_PAINTERLY_LIGHTING_INCLUDED
float4 _MeadowToonParameters;
float4 _MeadowToonShadow,_MeadowToonSunlit;
float4 _MeadowTreeShadeParameters,_MeadowTreeCoolShade,_MeadowTreeOliveShade;
half3 MeadowSoftFoliageColour(InputData inputData,SurfaceData surface,half3 original,float leafMask){
 float amount=saturate(_MeadowTreeShadeParameters.x)*leafMask;if(amount<=0)return original;
 Light sun=GetMainLight(inputData.shadowCoord,inputData.positionWS,inputData.shadowMask);
 Light fullSun=GetMainLight();
 float cookie=saturate(dot(sun.color,float3(.2126,.7152,.0722))/max(.001,dot(fullSun.color,float3(.2126,.7152,.0722))));
 float broadNormal=saturate(dot(inputData.normalWS,sun.direction)*.25+.65);
 float shadeWeight=1-smoothstep(.03,.82,broadNormal*sun.shadowAttenuation*cookie);
 float warm=smoothstep(.78,1.35,surface.albedo.r/max(.001,surface.albedo.g));
 float patch=.5+.25*sin(inputData.positionWS.x*.37+inputData.positionWS.z*.24)+.25*sin(inputData.positionWS.y*.43-inputData.positionWS.z*.19);
 half3 shade=lerp(_MeadowTreeCoolShade.rgb,_MeadowTreeOliveShade.rgb,warm)*lerp(.90,1.08,patch);
 float shadeLuma=max(.015,dot(shade,float3(.2126,.7152,.0722)));
 float sourceLuma=max(.005,dot(original,float3(.2126,.7152,.0722)));
 half3 leafHue=surface.albedo/max(.02,dot(surface.albedo,float3(.2126,.7152,.0722)));
 shade=lerp(shade,leafHue*shadeLuma,.14);
 shade*=lerp(1,clamp(sourceLuma/shadeLuma,.35,1.5),.35);
 shade*=lerp(.86,1.12,saturate(dot(inputData.normalWS,sun.direction)*.5+.5));
 half3 daylight=max(original,shade*.88);
 half3 painted=lerp(daylight,lerp(shade,daylight,.18),shadeWeight);
 return lerp(original,painted,amount);
}
half3 MeadowPainterlyColour(InputData inputData,SurfaceData surface,half3 original){
 float strength=saturate(_MeadowToonParameters.x);if(strength<=0)return original;
 Light sun=GetMainLight(inputData.shadowCoord,inputData.positionWS,inputData.shadowMask);
 Light fullSun=GetMainLight();
 float sourceBrightness=max(.001,dot(fullSun.color,float3(.2126,.7152,.0722)));
 float cookie=saturate(dot(sun.color,float3(.2126,.7152,.0722))/sourceBrightness);
 float nl=saturate(dot(inputData.normalWS,sun.direction)*.78+.22);
 float lit=nl*sun.shadowAttenuation*cookie;
 float softness=max(.05,_MeadowToonParameters.z);
 float mid=smoothstep(.24-softness,.24+softness,lit);
 float bright=smoothstep(.68-softness,.68+softness,lit);
 // Shift the hue of shade without replacing its leaf-level value variation.
 float luminance=max(0,dot(original,float3(.2126,.7152,.0722)));
 half3 shadeHue=_MeadowToonShadow.rgb/max(.01,dot(_MeadowToonShadow.rgb,float3(.2126,.7152,.0722)));
 half3 shade=lerp(original,shadeHue*luminance,.66);
 shade+=_MeadowToonShadow.rgb*_MeadowToonParameters.y*.18;
 // Broad, soft lighting bands retain volume and preserve the existing sunny colours.
 float bandGain=lerp(.92,1.05,mid)*lerp(1,1.025,bright);
 half3 daylight=original*bandGain*lerp(half3(1,1,1),_MeadowToonSunlit.rgb,.10);
 float shadowMask=1-smoothstep(.08,.48,lit);
 half3 painted=lerp(daylight,shade,shadowMask);
 return lerp(original,painted,strength);
}
#endif
