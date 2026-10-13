// Постройки и жители на планетах (модели из tools/buildings). Цвет — в цвете вершин, альфа — роль детали:
// 1 — обычная, 0.875 — отделка одежды жителя (_Cloth2: шапки, шарфы, пояса, обувь), 0.75 — одежда жителя (_Cloth),
// 0.5 — акцент (цвет владельца или ресурса, _Accent),
// 0.25 — кожа жителя (_Skin), 0 — огни (светятся и ночью). Цвет вершины умножается на цвет роли — так остаётся узор.
// UV.x — блеск детали: 0 — матовый бетон, 0.5 — крашеный металл, 0.9 — стекло.
Shader "PlanetTrader/Building"
{
    Properties
    {
        _Accent ("Цвет владельца", Color) = (0.3, 0.9, 0.45, 1)
        _Skin ("Кожа жителя", Color) = (0.6, 0.8, 0.5, 1)
        _Cloth ("Одежда жителя", Color) = (0.35, 0.45, 0.8, 1)
        _Cloth2 ("Отделка одежды жителя", Color) = (0.85, 0.35, 0.3, 1)
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

            float4 _Accent, _Skin, _Cloth, _Cloth2;
            float _Glow, _Pulse, _Ambient;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float3 normalW : TEXCOORD0;
                float3 upW : TEXCOORD1;
                float3 posW : TEXCOORD2;
                float shine : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.normalW = UnityObjectToWorldNormal(v.normal);
                // «Вверх» постройки — от центра планеты: по нему видно, день здесь или ночь.
                o.upW = UnityObjectToWorldNormal(float3(0, 1, 0));
                o.posW = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.shine = v.uv.x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float role = i.color.a;
                float isLight = step(role, 0.125);
                float isSkin = step(0.125, role) * step(role, 0.375);
                float isAccent = step(0.375, role) * step(role, 0.625);
                float isCloth = step(0.625, role) * step(role, 0.8125);
                float isCloth2 = step(0.8125, role) * step(role, 0.9375);
                float3 tint = 1 + isSkin * (_Skin.rgb - 1) + isAccent * (_Accent.rgb - 1) + isCloth * (_Cloth.rgb - 1) + isCloth2 * (_Cloth2.rgb - 1);
                float3 base = i.color.rgb * tint;

                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float day = saturate(dot(normalize(i.upW), L) * 2.5 + 0.35);   // на ночной стороне планеты темно
                float diff = saturate(dot(normalize(i.normalW), L) * 0.7 + 0.3);
                float3 col = base * (_Ambient + (1 - _Ambient) * diff * day) * _LightColor0.rgb;

                // Блик: чем выше блеск детали, тем он ярче и резче. Камера окна планеты перспективная.
                float3 V = normalize(_WorldSpaceCameraPos - i.posW);
                float3 H = normalize(L + V);
                float3 n = normalize(i.normalW);
                float spec = pow(saturate(dot(n, H)), 8 + 120 * i.shine * i.shine) * i.shine * day;
                float fres = pow(1 - saturate(dot(n, V)), 3) * i.shine * 0.25;
                col += (spec * 0.9 + fres * day) * _LightColor0.rgb;

                float pulse = 1 - _Pulse * (0.5 + 0.5 * sin(_Time.y * 4));
                float3 glow = base * _Glow * pulse;
                return fixed4(lerp(col, glow, isLight), 1);
            }
            ENDCG
        }
    }
}
