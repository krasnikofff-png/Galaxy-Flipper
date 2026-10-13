// Кольца планеты: плоское кольцо с полосами и просветами (текстура по радиусу), освещено солнцем с обеих сторон,
// на солнечной стороне ярче; на кольцо падает тень планеты — проверка, закрывает ли шар солнце.
Shader "PlanetTrader/PlanetRing"
{
    Properties
    {
        _MainTex ("Полосы по радиусу (RGB — цвет, A — плотность)", 2D) = "white" {}
        _Brightness ("Яркость", Float) = 1
        _Saturation ("Насыщенность", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float _Brightness, _Saturation;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 posW : TEXCOORD1;
                float3 centerW : TEXCOORD2;
                float3 normalW : TEXCOORD3;
                float radiusW : TEXCOORD4;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.posW = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.centerW = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                o.normalW = UnityObjectToWorldNormal(float3(0, 1, 0));
                // Радиус планеты в мире: кольцо в пространстве планеты, её радиус — 1.
                o.radiusW = length(mul((float3x3)unity_ObjectToWorld, float3(1, 0, 0)));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 band = tex2D(_MainTex, float2(i.uv.x, 0.5));
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float lit = 0.35 + 0.65 * abs(dot(normalize(i.normalW), L));
                // Тень планеты: луч от точки к солнцу проходит через шар?
                float3 d = i.posW - i.centerW;
                float t = dot(d, L);
                float miss = length(d - t * L) - i.radiusW;
                float shadow = t < 0 ? smoothstep(-0.03, 0.03, miss / i.radiusW) : 1;
                float3 col = band.rgb * lit * (0.2 + 0.8 * shadow) * _LightColor0.rgb * _Brightness;
                float gray = dot(col, float3(0.3, 0.59, 0.11));
                return fixed4(lerp(gray.xxx, col, _Saturation), band.a);
            }
            ENDCG
        }
    }
}
