#ifndef CODEX_GRASS_DISTANCE_BLEND_INCLUDED
#define CODEX_GRASS_DISTANCE_BLEND_INCLUDED
float4 _MeadowGrassDistanceSettings;
float4 _MeadowGrassViewPosition;
void MeadowBlendDistantGrass(inout float3 positionWS,float3 rootWS){
 if(_MeadowGrassDistanceSettings.w<.5)return;
 float distanceToView=distance(rootWS.xz,_MeadowGrassViewPosition.xz);
 float blend=smoothstep(_MeadowGrassDistanceSettings.x,_MeadowGrassDistanceSettings.y,distanceToView);
 positionWS.y=rootWS.y+(positionWS.y-rootWS.y)*lerp(1,_MeadowGrassDistanceSettings.z,blend);
}
#endif
