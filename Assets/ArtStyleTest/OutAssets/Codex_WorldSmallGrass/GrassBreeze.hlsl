#ifndef CODEX_GRASS_BREEZE_INCLUDED
#define CODEX_GRASS_BREEZE_INCLUDED
float _CodexGrassUsePreviewTime;
float _CodexGrassPreviewTime;
void CodexGentleGrassBreeze(inout float3 positionWS, float3 rootWS, float bladeHeight)
{
    float t = lerp(_Time.y, _CodexGrassPreviewTime, saturate(_CodexGrassUsePreviewTime));
    float phase = dot(rootWS.xz, float2(0.31, 0.19));
    float sway = 0.70 * sin(t * 0.95 + phase) + 0.30 * sin(t * 1.55 + phase * 0.73);
    float crossSway = sin(t * 0.72 + phase * 1.31);
    float tip = saturate((positionWS.y - rootWS.y) / max(0.1, bladeHeight));
    tip *= tip;
    positionWS.xz += (float2(0.85, 0.53) * (0.055 * sway) + float2(-0.53, 0.85) * (0.012 * crossSway)) * tip;
    positionWS.y -= 0.004 * abs(sway) * tip;
}
#endif
