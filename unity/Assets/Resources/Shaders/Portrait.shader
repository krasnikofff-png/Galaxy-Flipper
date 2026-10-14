// Портрет магната: цвет из вершин (как у построек), своя студийная схема света —
// тёплый ключевой свет, холодный заполняющий и контровой в цвет магната. Свет сцены не нужен.
Shader "PlanetTrader/Portrait"
{
    Properties
    {
        _Accent ("Цвет магната", Color) = (1, 0.3, 0.3, 1)
        _KeyDir ("Ключевой свет: откуда", Vector) = (-0.55, 0.6, -0.6, 0)
        _KeyColor ("Ключевой свет", Color) = (1.12, 1.03, 0.92, 1)
        _FillDir ("Заполняющий: откуда", Vector) = (0.75, 0.15, -0.6, 0)
        _FillColor ("Заполняющий", Color) = (0.2, 0.26, 0.4, 1)
        _RimColor ("Контровой", Color) = (1, 0.3, 0.3, 1)
        _Ambient ("Окружение", Color) = (0.1, 0.1, 0.13, 1)
        _Glow ("Сила свечения", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Accent, _KeyDir, _KeyColor, _FillDir, _FillColor, _RimColor, _Ambient;
            float _Glow;

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
                float3 posW : TEXCOORD1;
                float shine : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.normalW = UnityObjectToWorldNormal(v.normal);
                o.posW = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.shine = v.uv.x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float role = i.color.a;
                float isLight = step(role, 0.125);
                float isAccent = step(0.375, role) * step(role, 0.625);
                float3 base = i.color.rgb * lerp(float3(1, 1, 1), _Accent.rgb, isAccent);

                float3 n = normalize(i.normalW);
                float3 V = normalize(_WorldSpaceCameraPos - i.posW);
                float3 K = normalize(_KeyDir.xyz);
                float3 F = normalize(_FillDir.xyz);

                // Мягкий ключевой свет с «заворотом» в тень, как у кожи и ткани; заполняющий — полутона.
                float key = saturate(dot(n, K) * 0.75 + 0.25);
                key = key * key * (3 - 2 * key);
                float fill = saturate(dot(n, F) * 0.5 + 0.5);
                float3 col = base * (_Ambient.rgb + key * _KeyColor.rgb + fill * _FillColor.rgb * 0.7);

                // Блик: чем выше блеск детали (UV.x), тем он ярче и уже — золото, стекло, жемчуг.
                float3 H = normalize(K + V);
                float spec = pow(saturate(dot(n, H)), 10 + 140 * i.shine * i.shine) * i.shine;
                col += spec * _KeyColor.rgb * 1.1;

                // Контровой свет по краю силуэта — цвет магната, как в рамке.
                float rim = pow(1 - saturate(dot(n, V)), 3);
                float rimSide = saturate(dot(n, normalize(float3(0.4, 0.5, 0.8))) * 0.5 + 0.6);
                col += _RimColor.rgb * rim * rimSide * 0.85;

                return fixed4(lerp(col, base * _Glow, isLight), 1);
            }
            ENDCG
        }
    }
}
