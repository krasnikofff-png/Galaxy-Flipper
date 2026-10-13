// Постройки на планетах (модели из tools/buildings). Цвет — в цвете вершин, альфа — роль детали:
// 1 — обычная, 0.5 — акцент (цвет владельца или ресурса из _Accent), 0 — огни (светятся и ночью).
Shader "PlanetTrader/Building"
{
    Properties
    {
        _Accent ("Цвет владельца", Color) = (0.3, 0.9, 0.45, 1)
        _Glow ("Сила огней", Float) = 1.3
        _Pulse ("Мигание огней", Range(0, 1)) = 0
        _Ambient ("Освещённость ночной стороны", Range(0, 0.5)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            float4 _Accent;
            float _Glow, _Pulse, _Ambient;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float3 normalW : TEXCOORD0;
                float3 upW : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.normalW = UnityObjectToWorldNormal(v.normal);
                // «Вверх» постройки — от центра планеты: по нему видно, день здесь или ночь.
                o.upW = UnityObjectToWorldNormal(float3(0, 1, 0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float role = i.color.a;
                float isLight = step(role, 0.25);
                float isAccent = step(0.25, role) * step(role, 0.75);
                float3 base = lerp(i.color.rgb, _Accent.rgb * i.color.rgb, isAccent);

                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float day = saturate(dot(normalize(i.upW), L) * 2.5 + 0.35);   // на ночной стороне планеты темно
                float diff = saturate(dot(normalize(i.normalW), L) * 0.7 + 0.3);
                float3 col = base * (_Ambient + (1 - _Ambient) * diff * day) * _LightColor0.rgb;

                float pulse = 1 - _Pulse * (0.5 + 0.5 * sin(_Time.y * 4));
                float3 glow = base * _Glow * pulse;
                return fixed4(lerp(col, glow, isLight), 1);
            }
            ENDCG
        }
    }
}
