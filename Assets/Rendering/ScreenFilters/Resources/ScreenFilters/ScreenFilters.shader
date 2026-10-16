Shader "Hidden/ScreenFilters/Stack"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct Attributes { float4 position : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.position = TransformObjectToHClip(input.position.xyz);
            output.uv = input.uv;
            return output;
        }
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _Strength, _FilterClock, _Seed, _PixelSize, _Duty;
        float4 _Brush, _BrushMotion, _Tear, _Chromatic, _Direction, _CRT, _CRTShape;

        float Hash(float2 p)
        {
            p = frac((p + _Seed * 0.173) * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }
        float2 SafeUV(float2 uv)
        {
            float2 border = abs(_MainTex_TexelSize.xy) * 0.5;
            return clamp(uv, border, 1.0 - border);
        }
        float4 Sample(float2 uv) { return tex2D(_MainTex, SafeUV(uv)); }
        float2 ReferencePixel() { return abs(_MainTex_TexelSize.xy) * abs(_MainTex_TexelSize.w) / 1080.0; }

        float4 Pixel(Varyings input) : SV_Target
        {
            float2 dimensions = abs(_MainTex_TexelSize.zw);
            float size = max(1.0, floor(_PixelSize));
            float2 texel = floor(input.uv * dimensions / size) * size + floor(size * 0.5) + 0.5;
            texel = clamp(texel, 0.5, dimensions - 0.5);
            float4 block = tex2Dlod(_MainTex, float4(texel / dimensions, 0, 0));
            return _Strength >= 1.0 ? block : lerp(Sample(input.uv), block, _Strength);
        }

        float4 Brush(Varyings input) : SV_Target
        {
            float2 uv = input.uv;
            float2 texel = abs(_MainTex_TexelSize.xy);
            float2 dimensions = abs(_MainTex_TexelSize.zw);
            float4 original = Sample(uv);
            float size = max(2.0, _Brush.x);
            float2 cell = floor(uv * dimensions / size);
            float2 direction = float2(Hash(cell), Hash(cell + 13.7)) * 2.0 - 1.0;
            // A cell has a stable identity. Only a small, continuous motion is animated.
            float2 motion = sin(_FilterClock * max(0.0, _BrushMotion.y) * 6.283185
                + float2(Hash(cell + 2.1), Hash(cell + 8.3)) * 6.283185);
            float2 center = ((cell + 0.5 + direction * 0.22) * size
                + motion * max(0.0, _BrushMotion.x)) / dimensions;
            float2 stroke = normalize(direction + float2(0.001, 0.0)) * texel * size * 0.22;
            float3 block = (Sample(center).rgb * 2.0 + Sample(center + stroke).rgb
                + Sample(center - stroke).rgb) * 0.25;
            float3 dx = Sample(uv + float2(texel.x, 0)).rgb - Sample(uv - float2(texel.x, 0)).rgb;
            float3 dy = Sample(uv + float2(0, texel.y)).rgb - Sample(uv - float2(0, texel.y)).rgb;
            float threshold = max(0.01, _Brush.w);
            float edge = saturate((length(dx) + length(dy)) / threshold);
            // Reject colors sampled across a silhouette, even when it lies inside a brush cell.
            float crossing = saturate(length(block - original.rgb) / (threshold * 2.0));
            float levels = max(2.0, _Brush.y);
            // Quantize in an approximate perceptual space, retaining dark scene colors.
            block = sqrt(max(0.0, block));
            block = floor(block * (levels - 1.0) + 0.5) / (levels - 1.0);
            block *= block;
            block = lerp(block, original.rgb, max(edge, crossing) * saturate(_Brush.z));
            return float4(lerp(original.rgb, block, _Strength), original.a);
        }

        float4 Tear(Varyings input) : SV_Target
        {
            float cycleTime = _FilterClock * max(0.0, _Tear.w);
            float cycle = floor(cycleTime);
            float phase = frac(cycleTime);
            float duty = saturate(_Duty);
            float pulse = duty >= 0.999 ? 1.0 :
                (1.0 - smoothstep(duty * 0.7, max(0.0001, duty), phase))
                * smoothstep(0.0, max(0.0001, duty * 0.12), phase);
            if (_Tear.w <= 0.0) pulse = duty > 0.0 ? 1.0 : 0.0;
            float offset = 0.0;
            [unroll] for (int i = 0; i < 8; i++)
            {
                if (i >= _Tear.z) break;
                float2 key = float2(cycle, i * 11.3);
                float center = lerp(0.03, 0.97, Hash(key));
                float height = max(0.001, _Tear.y) * lerp(0.5, 1.4, Hash(key + 4.7));
                float band = 1.0 - smoothstep(height * 0.25, height * 0.5, abs(input.uv.y - center));
                offset += band * (Hash(key + 9.2) * 2.0 - 1.0);
            }
            float4 original = Sample(input.uv);
            float2 warped = input.uv + float2(offset * _Tear.x * ReferencePixel().x * pulse, 0);
            return lerp(original, Sample(warped), _Strength);
        }

        float4 Chromatic(Varyings input) : SV_Target
        {
            float2 centered = (input.uv - 0.5) * 2.0;
            float edge = smoothstep(0.15, 1.0, length(centered));
            float clock = floor(_FilterClock * max(0.0, _Chromatic.w));
            float jitter = lerp(1.0, lerp(0.25, 1.75, Hash(float2(clock, 91.3))), saturate(_Chromatic.z));
            float2 offset = _Direction.xy * ReferencePixel() * _Chromatic.x
                * lerp(1.0, edge, saturate(_Chromatic.y)) * jitter;
            float4 original = Sample(input.uv);
            float3 split = float3(Sample(input.uv + offset).r, original.g, Sample(input.uv - offset).b);
            return float4(lerp(original.rgb, split, _Strength), original.a);
        }

        float4 CRT(Varyings input) : SV_Target
        {
            float4 original = Sample(input.uv);
            float2 centered = (input.uv - 0.5) * 2.0;
            float2 warped = centered * (1.0 + max(0.0, _CRTShape.x) * dot(centered, centered));
            float2 uv = warped * 0.5 + 0.5;
            float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            float3 color = Sample(uv).rgb;
            float scan = 0.5 + 0.5 * cos(uv.y * abs(_MainTex_TexelSize.w)
                / max(1.0, _CRT.y) * 6.283185);
            color *= 1.0 - scan * saturate(_CRT.x);
            float channel = fmod(floor(uv.x * abs(_MainTex_TexelSize.z) / max(1.0, _CRT.w)), 3.0);
            float3 mask = float3(channel < 0.5, channel >= 0.5 && channel < 1.5, channel >= 1.5);
            color *= lerp(1.0.xxx, lerp(0.6.xxx, 1.15.xxx, mask), saturate(_CRT.z));
            color *= 1.0 - smoothstep(0.15, 1.8, dot(centered, centered)) * saturate(_CRTShape.y);
            color *= max(0.0, _CRTShape.w) * (1.0 + sin(_FilterClock * 113.0) * _CRTShape.z);
            return float4(lerp(original.rgb, max(0.0, color) * inside, _Strength), original.a);
        }
        ENDHLSL
        Pass
        {
            Name "Pixelation"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Pixel
            ENDHLSL
        }
        Pass
        {
            Name "Brush"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Brush
            ENDHLSL
        }
        Pass
        {
            Name "Tear"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Tear
            ENDHLSL
        }
        Pass
        {
            Name "Chromatic"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Chromatic
            ENDHLSL
        }
        Pass
        {
            Name "CRT"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment CRT
            ENDHLSL
        }
    }
    Fallback Off
}
