Shader "Hidden/TapTap/RewindSignal"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "white" {}
        _HistoryTex ("Recent scene", 2D) = "black" {}
        _OlderHistoryTex ("Older scene", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _HistoryTex;
            sampler2D _OlderHistoryTex;
            float4 _MainTex_TexelSize;
            float4 _RewindParams; // blend, saturation, cold tint, vignette
            float4 _SignalParams; // tear pixels at 1080p, band height, RGB pixels, motion
            float4 _DetailParams; // scanline strength, grain strength
            float4 _ClockParams;  // unscaled seconds, continuous signal cycle, ripple speed
            float _TearDuty;
            float4 _TimeParams; // ghost strength, sweep strength, sweep width, edge ripple
            float4 _FlowParams; // continuous flow clock, sweep speed, chevrons, edge RGB pixels
            float4 _HistoryState; // recent/older validity

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Band(float y, float center, float halfHeight)
            {
                return 1.0 - smoothstep(halfHeight * 0.55, halfHeight, abs(y - center));
            }

            float2 SafeUV(float2 uv)
            {
                float2 border = abs(_MainTex_TexelSize.xy) * 0.5;
                return clamp(uv, border, 1.0 - border);
            }

            float3 TimeGhost(float3 history, float3 present, float3 tint)
            {
                // Add only the vanished bright parts. Static scenery and the current actor remain sharp.
                float3 excess = max(history - present - 0.025, 0.0);
                float peak = max(excess.r, max(excess.g, excess.b));
                return lerp(excess, peak * tint, 0.7);
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float blend = _RewindParams.x;
                float motion = _SignalParams.w;
                float2 centered = (uv - 0.5) * 2.0;
                float radius = length(centered);
                float edge = smoothstep(0.35, 1.15, radius);
                // Increasing phase moves these rings inward, towards the preserved clear center.
                float ripple = sin(radius * 24.0 + _FlowParams.x * _ClockParams.z);
                float2 warpedUV = uv + centered * ripple * edge * _TimeParams.w * blend * motion;
                float sweepPosition = frac(_FlowParams.x * _FlowParams.y);
                float sweepDistance = abs(frac(uv.y - sweepPosition + 0.5) - 0.5);
                float sweep = 1.0 - smoothstep(0.0, max(0.001, _TimeParams.z), sweepDistance);
                float sweepLine = 1.0 - smoothstep(0.001, 0.005, sweepDistance);
                float cycle = floor(_ClockParams.y);
                float phase = frac(_ClockParams.y);
                float duty = max(0.001, _TearDuty);
                float pulse = (1.0 - smoothstep(duty * 0.7, duty, phase))
                    * smoothstep(0.0, duty * 0.15, phase);
                float centerA = lerp(0.06, 0.94, Hash(float2(cycle, 1.7)));
                float centerB = lerp(0.06, 0.94, Hash(float2(cycle, 8.3)));
                float height = _SignalParams.y * lerp(0.6, 1.4, Hash(float2(cycle, 3.1)));
                float bandA = Band(uv.y, centerA, height * 0.5);
                float bandB = Band(uv.y, centerB, height * 0.3) * 0.55;
                float tearMask = saturate(bandA + bandB) * pulse * motion;
                float direction = Hash(float2(cycle, 5.9)) * 2.0 - 1.0;
                // Keep the authored pixel size proportional to viewport height at every resolution.
                float pixelX = abs(_MainTex_TexelSize.x) * abs(_MainTex_TexelSize.w) / 1080.0;
                float offset = (direction * _SignalParams.x * tearMask
                    + sin(uv.y * 90.0 + _FlowParams.x * 5.0) * sweep * _TimeParams.y * motion * 3.0) * pixelX * blend;
                float2 tornUV = SafeUV(warpedUV + float2(offset, 0.0));
                float fringe = (_SignalParams.z * tearMask + edge * _FlowParams.w * motion) * pixelX * blend;
                float4 color = tex2D(_MainTex, tornUV);
                float3 present = color.rgb;
                color.r = tex2D(_MainTex, SafeUV(tornUV + float2(fringe, 0.0))).r;
                color.b = tex2D(_MainTex, SafeUV(tornUV - float2(fringe, 0.0))).b;

                float luminance = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
                color.rgb = lerp(color.rgb, luminance.xxx, (1.0 - _RewindParams.y) * blend);
                color.rgb *= lerp(float3(1.0, 1.0, 1.0), float3(0.85, 1.015, 1.09), _RewindParams.z * blend);

                if (_HistoryState.x > 0.5 && _TimeParams.x > 0.0)
                {
                    float3 recent = tex2D(_HistoryTex, tornUV).rgb;
                    float3 ghosts = TimeGhost(recent, present, float3(0.45, 1.05, 1.2));
                    if (_HistoryState.y > 0.5)
                        ghosts += TimeGhost(tex2D(_OlderHistoryTex, tornUV).rgb, present, float3(0.95, 0.55, 1.15)) * 0.55;
                    color.rgb += ghosts * _TimeParams.x * blend;
                }

                // A broad exposure band and a fine leading line sweep upwards like a tape running backwards.
                float sweepAmount = _TimeParams.y * blend * motion;
                color.rgb *= 1.0 + sweep * sweepAmount * 0.16;
                color.rgb += float3(0.25, 0.7, 1.0) * (sweep * 0.025 + sweepLine * 0.045) * sweepAmount;

                float referenceY = uv.y * 1080.0;
                float scanline = 0.5 + 0.5 * sin(referenceY * 2.094395 + _ClockParams.x * 5.0);
                color.rgb *= 1.0 - scanline * _DetailParams.x * blend;
                float grain = Hash(floor(uv * float2(1920.0, 1080.0)) + floor(_ClockParams.x * 24.0)) - 0.5;
                color.rgb += grain * _DetailParams.y * blend;
                float vignette = smoothstep(0.25, 1.65, dot(centered, centered));
                color.rgb *= 1.0 - vignette * _RewindParams.w * blend;

                // Small left-pointing tape marks stay at the corners, away from the playable center.
                float rowCenter = uv.y < 0.5 ? 0.06 : 0.94;
                float rowY = (uv.y - rowCenter) * 20.0;
                float cellX = frac((uv.x + _FlowParams.x * 0.035) * 24.0) - 0.5;
                float chevron = 1.0 - smoothstep(0.025, 0.06, abs(cellX - abs(rowY) + 0.28));
                chevron *= 1.0 - smoothstep(0.23, 0.3, abs(rowY));
                chevron *= 1.0 - smoothstep(0.13, 0.2, min(uv.x, 1.0 - uv.x));
                color.rgb += float3(0.12, 0.45, 0.6) * chevron * _FlowParams.z * blend * motion;
                return float4(max(color.rgb, 0.0), color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
