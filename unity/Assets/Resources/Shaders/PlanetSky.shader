// Фон: тёмный космос со звёздами и туманностью; текстура запекается в коде или рисуется автором.
// В витрине — изнанка большой сферы, на карте сектора — плоский прямоугольник (_Cull = 0). URP, без света.
Shader "PlanetTrader/PlanetSky"
{
    Properties
    {
        _MainTex ("Небо", 2D) = "black" {}
        _Dim ("Яркость (картинка автора приглушается, чтобы не спорить с планетами)", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Какую сторону не рисовать", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull [_Cull]
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Dim, _Cull;
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

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                float4 sky = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return float4(OUT(GAM(sky.rgb) * _Dim), sky.a);
            }
            ENDHLSL
        }
    }
}
