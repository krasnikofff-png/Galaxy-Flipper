// Ореол атмосферы: сфера больше планеты, рисуется изнанкой и складывается со светом.
// Ярче всего у самого края планеты, к внешнему краю гаснет; на солнечной стороне ярче. URP.
Shader "PlanetTrader/PlanetAtmosphere"
{
    Properties
    {
        _Color ("Цвет", Color) = (0.3, 0.65, 1, 1)
        _Inner ("Радиус планеты / радиус ореола", Range(0.5, 1)) = 0.877
        _Power ("Спад", Float) = 3
        _Strength ("Сила", Float) = 0.9
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Front
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Inner, _Power, _Strength;
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 centerWS : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.centerWS = TransformObjectToWorld(float3(0, 0, 0));
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                // Направление на камеру; у ортографической (карта сектора) оно одно для всех точек.
                float3 V = unity_OrthoParams.w > 0.5 ? normalize(UNITY_MATRIX_V[2].xyz) : normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 n = normalize(i.normalWS);
                // Изнанка: в центре диска d≈1, у внешнего края 0. Край планеты — при d = sqrt(1 - inner²).
                float d = saturate(dot(-n, V));
                float edge = sqrt(1 - _Inner * _Inner);
                float glow = pow(saturate(d / edge), _Power);
                float3 L = normalize(GetMainLight().direction);
                float side = saturate(dot(normalize(i.positionWS - i.centerWS), L) * 0.6 + 0.55);
                return float4(OUT(GAM(_Color.rgb) * glow * side * _Strength), 1);
            }
            ENDHLSL
        }
    }
}
