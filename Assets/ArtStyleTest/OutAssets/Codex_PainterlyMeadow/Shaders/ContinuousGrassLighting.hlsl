#ifndef CODEX_CONTINUOUS_GRASS_INCLUDED
#define CODEX_CONTINUOUS_GRASS_INCLUDED
float4 _MeadowGrassSurfaceColour;
float4 _MeadowGrassSurfaceParameters;
float4 _MeadowGrassSunlitColour,_MeadowGrassShadeColour,_MeadowGrassPaletteSettings;
float MeadowGrassHash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float MeadowGrassPatch(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(MeadowGrassHash(i),MeadowGrassHash(i+float2(1,0)),f.x),lerp(MeadowGrassHash(i+float2(0,1)),MeadowGrassHash(i+1),f.x),f.y);}
half3 MeadowContinuousGrassColour(InputData inputData){
 // Shared world-space colour makes neighbouring blades and the ground join into one surface.
 // Its lighting deliberately ignores each card's normal, specular highlight and local AO.
 Light sun=GetMainLight(inputData.shadowCoord,inputData.positionWS,inputData.shadowMask);
 Light fullSun=GetMainLight();
 float direct=max(.001,dot(fullSun.color,float3(.2126,.7152,.0722)));
 float cookie=saturate(dot(sun.color,float3(.2126,.7152,.0722))/direct);
 float shadow=1-saturate(sun.shadowAttenuation*cookie);
 float shade=saturate(_MeadowGrassSurfaceParameters.y)*shadow;
 float patch=MeadowGrassPatch(inputData.positionWS.xz*_MeadowGrassSurfaceParameters.w);
 half3 colour=_MeadowGrassSurfaceColour.rgb*(1+(patch-.5)*2*_MeadowGrassSurfaceParameters.z);
 colour*=1-shade;
 colour*=lerp(half3(1,1,1),half3(.94,1.01,1.02),shade);
 if(_MeadowGrassPaletteSettings.x>.5){
  float lightField=smoothstep(.06,.92,saturate(sun.shadowAttenuation*cookie));
  lightField=saturate(lightField*.92+(patch-.45)*.22+.035);
  half3 middle=_MeadowGrassSurfaceColour.rgb*lerp(half3(.94,1,1.06),half3(1.07,1.025,.93),patch);
  half3 cool=lerp(middle,_MeadowGrassShadeColour.rgb,saturate(_MeadowGrassSurfaceParameters.y));
  colour=lerp(cool,middle,smoothstep(.02,.52,lightField));
  colour=lerp(colour,_MeadowGrassSunlitColour.rgb,smoothstep(.38,1,lightField)*.86);
  colour*=1+(patch-.5)*2*_MeadowGrassSurfaceParameters.z;
 }
 return colour;
}
float MeadowGroundGrassMask(InputData inputData,SurfaceData surface){
 float3 c=max(surface.albedo,.0001);
 float green=smoothstep(1.01,1.20,c.g/c.r)*smoothstep(1.10,1.55,c.g/c.b);
 return green*smoothstep(.35,.70,inputData.normalWS.y);
}
#endif
