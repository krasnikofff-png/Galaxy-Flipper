// Облака: отдельная прозрачная сфера чуть больше планеты, освещённая солнцем.
Shader "PlanetTrader/PlanetClouds"
{
    Properties
    {
        _MainTex ("Облака (A — плотность)", 2D) = "black" {}
        _Color ("Оттенок", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalW : TEXCOORD1;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.normalW = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a;
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float light = 0.2 + 0.9 * saturate(dot(normalize(i.normalW), L) * 0.9 + 0.35);
                return fixed4(_Color.rgb * light * _LightColor0.rgb, a);
            }
            ENDCG
        }
    }
}
