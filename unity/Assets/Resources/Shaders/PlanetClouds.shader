// Облака: отдельная прозрачная сфера чуть больше планеты, освещённая солнцем. URP.
Shader "PlanetTrader/PlanetClouds"
{
    Properties
    {
        _MainTex ("Облака (A — плотность)", 2D) = "black" {}
        _Color ("Оттенок", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                Light light = GetMainLight();
                float3 L = normalize(light.direction);
                float lit = 0.2 + 0.9 * saturate(dot(normalize(i.normalWS), L) * 0.9 + 0.35);
                return float4(OUT(GAM(_Color.rgb) * lit * GAM(light.color)), a);
            }
            ENDHLSL
        }
    }
}
