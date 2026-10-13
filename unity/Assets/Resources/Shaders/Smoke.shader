// Дым и огонь над постройками. Вся анимация — на видеокарте: каждый клуб — квадрат, повёрнутый к камере;
// клубы по очереди поднимаются, растут, их сносит ветер, и они тают. Номер клуба и случайное число — во втором UV.
// Время задаёт скрипт (_T), чтобы дым можно было записывать в видео без Play.
Shader "PlanetTrader/Smoke"
{
    Properties
    {
        _MainTex ("Клуб", 2D) = "white" {}
        _Color ("Цвет (A — плотность)", Color) = (0.25, 0.25, 0.27, 0.75)
        _T ("Время, с", Float) = 0
        _Life ("Жизнь клуба, с", Float) = 4
        _Rise ("Подъём", Float) = 1.2
        _Size0 ("Размер в начале", Float) = 0.08
        _Size1 ("Размер в конце", Float) = 0.5
        _Wind ("Ветер (x, z)", Vector) = (0.4, 0, 0.1, 0)
        _Fire ("Огонь (0 — дым)", Range(0, 1)) = 0
        _FireA ("Огонь у основания", Color) = (1, 0.92, 0.55, 1)
        _FireB ("Огонь на конце", Color) = (1, 0.35, 0.08, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Смешивание", Float) = 10
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha [_DstBlend]
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
            float4 _Color, _Wind, _FireA, _FireB;
            float _T, _Life, _Rise, _Size0, _Size1, _Fire;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;      // угол квадрата, −1…1
                float2 puff : TEXCOORD1;    // x — доля в очереди клубов, y — случайное 0…1
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float seed = v.puff.y;
                float age = frac(_T / _Life + v.puff.x);
                // Центр клуба в пространстве постройки: вверх, вбок по ветру (сильнее к концу), с покачиванием.
                float3 c = float3(0, _Rise * age, 0) + float3(_Wind.x, 0, _Wind.z) * (age * age) * _Rise;
                c.x += sin(age * 8.2 + seed * 20) * 0.05 * age * _Rise;
                c.z += cos(age * 6.9 + seed * 13) * 0.05 * age * _Rise;
                float size = lerp(_Size0, _Size1, sqrt(age)) * (0.75 + 0.5 * seed);
                float scale = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
                float3 vp = UnityObjectToViewPos(float4(c, 1));
                float ang = seed * 6.28 + age * (seed - 0.5) * 3;
                float2 corner = float2(v.uv.x * cos(ang) - v.uv.y * sin(ang), v.uv.x * sin(ang) + v.uv.y * cos(ang));
                vp.xy += corner * size * scale * 0.5;
                o.pos = mul(UNITY_MATRIX_P, float4(vp, 1));
                o.uv = v.uv * 0.5 + 0.5;

                float alpha = saturate(age / 0.12) * pow(1 - age, 1.4) * _Color.a;
                // Дым освещён солнцем: на ночной стороне планеты тёмный. Огонь светится сам: жёлтый у горелки, дальше оранжевый.
                float3 up = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0)));
                float day = saturate(dot(up, normalize(_WorldSpaceLightPos0.xyz)) * 2.5 + 0.35);
                float3 smoke = _Color.rgb * (0.2 + 0.8 * day) * _LightColor0.rgb;
                float3 fire = lerp(_FireA.rgb, _FireB.rgb, saturate(age * 1.4)) * 1.6;
                o.color = float4(lerp(smoke, fire, _Fire), alpha);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                return fixed4(i.color.rgb * tex.rgb, i.color.a * tex.a);
            }
            ENDCG
        }
    }
}
