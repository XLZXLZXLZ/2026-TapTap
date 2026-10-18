Shader "TapTap/UI/Tutorial Panel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Panel (white becomes transparent)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            sampler2D _MainTex;
            fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                half3 source = tex2D(_MainTex, i.uv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                    source = LinearToGammaSpace(source);
                #endif
                // Preserve the supplied panel's soft edges without a white rectangular backdrop.
                return fixed4(i.color.rgb, (1 - dot(source, half3(0.299,0.587,0.114))) * i.color.a);
            }
            ENDCG
        }
    }
}
