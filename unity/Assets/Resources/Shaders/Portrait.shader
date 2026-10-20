// Портрет магната: цвет из вершин (как у построек), своя студийная схема света —
// тёплый ключевой свет, холодный заполняющий и контровой в цвет магната. Свет сцены не нужен. URP.
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
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Accent, _KeyDir, _KeyColor, _FillDir, _FillColor, _RimColor, _Ambient;
                float _Glow;
            CBUFFER_END
            // Освещение подобрано в гамме (как смотрит глаз). В линейном проекте входы переводим обратно в sRGB (GAM),
            // считаем по-старому и один раз переводим итог в линейный цвет (OUT) — облик не меняется.
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #ifdef UNITY_COLORSPACE_GAMMA
            #define GAM(c) (c)
            #define OUT(c) (c)
            #else
            #define GAM(c) LinearToSRGB(c)
            #define OUT(c) SRGBToLinear(c)
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float shine : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;   // цвет вершин — в sRGB, как нарисован: шейдер считает в нём
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.shine = v.uv.x;
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                float role = i.color.a;
                float isLight = step(role, 0.125);
                float isAccent = step(0.375, role) * step(role, 0.625);
                float3 base = i.color.rgb * lerp(float3(1, 1, 1), GAM(_Accent.rgb), isAccent);

                float3 n = normalize(i.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 K = normalize(_KeyDir.xyz);
                float3 F = normalize(_FillDir.xyz);

                // Мягкий ключевой свет с «заворотом» в тень, как у кожи и ткани; заполняющий — полутона.
                float key = saturate(dot(n, K) * 0.75 + 0.25);
                key = key * key * (3 - 2 * key);
                float fill = saturate(dot(n, F) * 0.5 + 0.5);
                float3 col = base * (GAM(_Ambient.rgb) + key * GAM(_KeyColor.rgb) + fill * GAM(_FillColor.rgb) * 0.7);

                // Блик: чем выше блеск детали (UV.x), тем он ярче и уже — золото, стекло, жемчуг.
                float3 H = normalize(K + V);
                float spec = pow(saturate(dot(n, H)), 10 + 140 * i.shine * i.shine) * i.shine;
                col += spec * GAM(_KeyColor.rgb) * 1.1;

                // Контровой свет по краю силуэта — цвет магната, как в рамке.
                float rim = pow(1 - saturate(dot(n, V)), 3);
                float rimSide = saturate(dot(n, normalize(float3(0.4, 0.5, 0.8))) * 0.5 + 0.6);
                col += GAM(_RimColor.rgb) * rim * rimSide * 0.85;

                return float4(OUT(lerp(col, base * _Glow, isLight)), 1);
            }
            ENDHLSL
        }
    }
}
