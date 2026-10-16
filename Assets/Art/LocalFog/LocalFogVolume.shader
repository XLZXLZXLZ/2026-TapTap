Shader "TapTap/LocalFog/BuiltInVolume"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.72,0.8,0.85,1)
        _Density ("Density", Float) = 0.25
        _EdgeFade ("Edge Fade", Range(0,1)) = 0.3
        _Shape ("Shape", Float) = 0
        _Steps ("Steps", Float) = 48
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+50" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front ZWrite Off ZTest Always
            // Premultiplied RGB; preserve destination alpha for downstream compositing.
            Blend One OneMinusSrcAlpha, Zero One
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float _LocalFogDepthAvailable;
            float4x4 _LocalFogOpaqueVP;
            TEXTURE2D(_LocalFogOpaqueDepth); SAMPLER(sampler_LocalFogOpaqueDepth);
            float _LocalFogHasForegroundMask;
            float4x4 _LocalFogMaskVP;
            TEXTURE2D(_LocalFogForegroundMask); SAMPLER(sampler_LocalFogForegroundMask);
            struct appdata_base { float4 vertex : POSITION; };
            float4 _FogColor;
            float _Density, _EdgeFade, _Shape, _Steps;
            struct v2f { float4 pos : SV_POSITION; float4 screen : TEXCOORD0; float3 world : TEXCOORD1; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.screen = ComputeScreenPos(o.pos);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float3 direction = unity_OrthoParams.w > 0.5 ? forward : normalize(i.world - _WorldSpaceCameraPos);
                float3 origin = _WorldSpaceCameraPos;
                if (unity_OrthoParams.w > 0.5)
                    origin = i.world - forward * dot(i.world - origin, forward);
                float3 ro = mul(unity_WorldToObject, float4(origin, 1)).xyz;
                float3 rd = mul((float3x3)unity_WorldToObject, direction);
                float start, finish;
                if (_Shape > 0.5)
                {
                    float a = dot(rd, rd), b = dot(ro, rd), c = dot(ro, ro) - 0.25;
                    float discriminant = b*b - a*c;
                    if (discriminant <= 0) discard;
                    float root = sqrt(discriminant);
                    start = (-b-root)/a; finish = (-b+root)/a;
                }
                else
                {
                    // Preserve the sign for rays parallel to a slab.
                    float3 safeRd = float3(rd.x >= 0 ? max(rd.x,1e-8) : min(rd.x,-1e-8), rd.y >= 0 ? max(rd.y,1e-8) : min(rd.y,-1e-8), rd.z >= 0 ? max(rd.z,1e-8) : min(rd.z,-1e-8));
                    float3 t0 = (-0.5-ro)/safeRd, t1 = (0.5-ro)/safeRd;
                    float3 lo = min(t0,t1), hi = max(t0,t1);
                    start = max(max(lo.x,lo.y),lo.z);
                    finish = min(min(hi.x,hi.y),hi.z);
                }
                float eyeFactor = max(1e-5, dot(direction,forward));
                // Never substitute far clip for missing depth: that paints fog over foreground.
                if (_LocalFogDepthAvailable < 0.5) discard;
                if (_LocalFogHasForegroundMask > 0.5)
                {
                    float4 maskClip = mul(_LocalFogMaskVP, float4(i.world, 1));
                    float2 maskUV = maskClip.xy / maskClip.w * 0.5 + 0.5;
                    #if UNITY_UV_STARTS_AT_TOP
                        maskUV.y = 1 - maskUV.y;
                    #endif
                    if (SAMPLE_TEXTURE2D(_LocalFogForegroundMask, sampler_LocalFogForegroundMask, maskUV).r > 0.5) discard;
                }
                float2 uv = i.pos.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(uv);
                // Reconstruct the actual opaque surface in world space for both projection types.
                float deviceDepth = rawDepth;
                #if !UNITY_REVERSED_Z
                    deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 scenePosition = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                float sceneDistance = max(0, dot(scenePosition - origin, direction));
                if (_LocalFogDepthAvailable > 1.5)
                {
                    float4 depthClip = mul(_LocalFogOpaqueVP, float4(i.world, 1));
                    float2 depthUV = depthClip.xy / depthClip.w * 0.5 + 0.5;
                    #if UNITY_UV_STARTS_AT_TOP
                        depthUV.y = 1 - depthUV.y;
                    #endif
                    float normalizedDepth = SAMPLE_TEXTURE2D(_LocalFogOpaqueDepth, sampler_LocalFogOpaqueDepth, depthUV).r;
                    sceneDistance = normalizedDepth * _ProjectionParams.z / eyeFactor;
                }
                start = max(start, _ProjectionParams.y / eyeFactor);
                finish = min(finish, sceneDistance);
                if (finish <= start) discard;
                int count = (int)clamp(_Steps,8,96);
                float stepLength = (finish-start)/count;
                float integral = 0;
                [loop] for (int s=0;s<96;s++)
                {
                    if (s>=count) break;
                    float3 p = ro + rd * (start+(s+0.5)*stepLength);
                    float radius = _Shape > 0.5 ? length(p)*2 : max(max(abs(p.x),abs(p.y)),abs(p.z))*2;
                    float weight = _EdgeFade < 0.0001 ? 1 : smoothstep(0, _EdgeFade, saturate(1-radius));
                    integral += weight * stepLength;
                }
                float alpha = 1-exp(-max(0,_Density)*integral*saturate(_FogColor.a));
                return float4(_FogColor.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            Cull Front ZWrite Off ZTest Always
            // Premultiplied RGB; preserve destination alpha for downstream compositing.
            Blend One OneMinusSrcAlpha, Zero One
            ColorMask RGB
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _FogColor;
            float _Density, _EdgeFade, _Shape, _Steps;
            struct v2f { float4 pos : SV_POSITION; float4 screen : TEXCOORD0; float3 world : TEXCOORD1; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float3 direction = unity_OrthoParams.w > 0.5 ? forward : normalize(i.world - _WorldSpaceCameraPos);
                float3 origin = _WorldSpaceCameraPos;
                if (unity_OrthoParams.w > 0.5)
                    origin = i.world - forward * dot(i.world - origin, forward);
                float3 ro = mul(unity_WorldToObject, float4(origin, 1)).xyz;
                float3 rd = mul((float3x3)unity_WorldToObject, direction);
                float start, finish;
                if (_Shape > 0.5)
                {
                    float a = dot(rd, rd), b = dot(ro, rd), c = dot(ro, ro) - 0.25;
                    float discriminant = b*b - a*c;
                    if (discriminant <= 0) discard;
                    float root = sqrt(discriminant);
                    start = (-b-root)/a; finish = (-b+root)/a;
                }
                else
                {
                    // Preserve the sign for rays parallel to a slab.
                    float3 safeRd = float3(rd.x >= 0 ? max(rd.x,1e-8) : min(rd.x,-1e-8), rd.y >= 0 ? max(rd.y,1e-8) : min(rd.y,-1e-8), rd.z >= 0 ? max(rd.z,1e-8) : min(rd.z,-1e-8));
                    float3 t0 = (-0.5-ro)/safeRd, t1 = (0.5-ro)/safeRd;
                    float3 lo = min(t0,t1), hi = max(t0,t1);
                    start = max(max(lo.x,lo.y),lo.z);
                    finish = min(min(hi.x,hi.y),hi.z);
                }
                float eyeFactor = max(1e-5, dot(direction,forward));
                float rawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen));
                float eyeDepth = LinearEyeDepth(rawDepth);
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        rawDepth = 1 - rawDepth;
                    #endif
                    eyeDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                }
                float sceneDistance = eyeDepth / eyeFactor;
                start = max(start, _ProjectionParams.y / eyeFactor);
                finish = min(finish, sceneDistance);
                if (finish <= start) discard;
                int count = (int)clamp(_Steps,8,96);
                float stepLength = (finish-start)/count;
                float integral = 0;
                [loop] for (int s=0;s<96;s++)
                {
                    if (s>=count) break;
                    float3 p = ro + rd * (start+(s+0.5)*stepLength);
                    float radius = _Shape > 0.5 ? length(p)*2 : max(max(abs(p.x),abs(p.y)),abs(p.z))*2;
                    float weight = _EdgeFade < 0.0001 ? 1 : smoothstep(0, _EdgeFade, saturate(1-radius));
                    integral += weight * stepLength;
                }
                float alpha = 1-exp(-max(0,_Density)*integral*saturate(_FogColor.a));
                return float4(_FogColor.rgb * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
