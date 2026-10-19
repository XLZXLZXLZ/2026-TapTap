Shader "TapTap/Golden Door Unlit Dissolve"
{
    Properties
    {
        _BaseColor ("Flat colour", Color) = (1, 0.65, 0.16, 1)
        [HDR] _EdgeColor ("Golden dissolve glow", Color) = (4, 2.1, 0.3, 1)
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _EdgeWidth ("Glowing edge width (grid units)", Range(0.005, 0.2)) = 0.065
        _NoiseScale ("Dissolve breakup scale", Float) = 13
        _NoiseAmount ("Dissolve breakup amount", Range(0, 0.5)) = 0.14
        _DissolveOrigin ("Lock centre in door space", Vector) = (0, 1, 0, 0)
        _DissolveRadius ("Maximum dissolve radius", Float) = 1.9
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        ZWrite On
        Cull Back
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _EdgeColor;
                float _Dissolve, _EdgeWidth, _NoiseScale, _NoiseAmount, _DissolveRadius;
                float4 _DissolveOrigin;
                float4x4 _WorldToDoor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 doorPosition : TEXCOORD0;
                half4 color : COLOR;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.doorPosition = mul(_WorldToDoor, float4(position.positionWS, 1)).xyz;
                output.color = input.color;
                return output;
            }
            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float Noise(float3 p)
            {
                float3 cell = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(cell), Hash(cell + float3(1,0,0)), f.x),
                                 lerp(Hash(cell + float3(0,1,0)), Hash(cell + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(cell + float3(0,0,1)), Hash(cell + float3(1,0,1)), f.x),
                                 lerp(Hash(cell + float3(0,1,1)), Hash(cell + 1), f.x), f.y), f.z);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 color = _BaseColor.rgb * input.color.rgb;
                if (_Dissolve > 0.00001)
                {
                    clip(0.99999 - _Dissolve);
                    float distance = length(input.doorPosition.xy - _DissolveOrigin.xy);
                    float noise = Noise(input.doorPosition * _NoiseScale) * 0.72
                                + Noise(input.doorPosition * _NoiseScale * 2.13) * 0.28;
                    float frontier = distance + (noise - 0.5) * _NoiseAmount
                                   - _Dissolve * _DissolveRadius;
                    clip(frontier);
                    float glow = 1 - smoothstep(0, _EdgeWidth, frontier);
                    color = lerp(color, _EdgeColor.rgb, glow);
                }
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
