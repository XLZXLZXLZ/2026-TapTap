Shader "Hidden/TapTap/RewindSignal"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "white" {}
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
            float4 _MainTex_TexelSize;
            float4 _RewindParams; // blend, saturation, cold tint, vignette
            float4 _SignalParams; // tear pixels at 1080p, band height, RGB pixels, motion
            float4 _DetailParams; // scanline strength, grain strength
            float4 _ClockParams;  // unscaled seconds, continuous signal cycle
            float _TearDuty;

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

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float blend = _RewindParams.x;
                float motion = _SignalParams.w;
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
                float offset = direction * _SignalParams.x * pixelX * tearMask * blend;
                float2 tornUV = SafeUV(uv + float2(offset, 0.0));
                float fringe = _SignalParams.z * pixelX * tearMask * blend;
                float4 color = tex2D(_MainTex, tornUV);
                color.r = tex2D(_MainTex, SafeUV(tornUV + float2(fringe, 0.0))).r;
                color.b = tex2D(_MainTex, SafeUV(tornUV - float2(fringe, 0.0))).b;

                float luminance = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
                color.rgb = lerp(color.rgb, luminance.xxx, (1.0 - _RewindParams.y) * blend);
                color.rgb *= lerp(float3(1.0, 1.0, 1.0), float3(0.85, 1.015, 1.09), _RewindParams.z * blend);

                float referenceY = uv.y * 1080.0;
                float scanline = 0.5 + 0.5 * sin(referenceY * 2.094395 + _ClockParams.x * 5.0);
                color.rgb *= 1.0 - scanline * _DetailParams.x * blend;
                float grain = Hash(floor(uv * float2(1920.0, 1080.0)) + floor(_ClockParams.x * 24.0)) - 0.5;
                color.rgb += grain * _DetailParams.y * blend;
                float2 centered = (uv - 0.5) * 2.0;
                float vignette = smoothstep(0.25, 1.65, dot(centered, centered));
                color.rgb *= 1.0 - vignette * _RewindParams.w * blend;
                return float4(max(color.rgb, 0.0), color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
