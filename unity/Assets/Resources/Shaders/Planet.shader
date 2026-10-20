// Поверхность планеты: запечённый цвет и рельеф, мягкий край ночи, блик на воде и льду,
// свечение (кристаллы, разломы аномалии) и подсветка атмосферы по краю диска. URP; свет — главный направленный.
Shader "PlanetTrader/Planet"
{
    Properties
    {
        _MainTex ("Цвет (A — блеск)", 2D) = "white" {}
        _NormalTex ("Нормали в пространстве планеты", 2D) = "gray" {}
        _EmissionTex ("Свечение", 2D) = "black" {}
        _AtmoColor ("Цвет атмосферы", Color) = (0.35, 0.75, 1, 1)
        _RimPower ("Ширина подсветки края", Float) = 2.4
        _Ambient ("Освещённость ночной стороны", Range(0, 0.5)) = 0.08
        _SpecPower ("Резкость блика", Float) = 70
        _SpecStrength ("Сила блика", Float) = 0.6
        _Saturation ("Насыщенность (0 — серая)", Range(0, 1)) = 1
        _Brightness ("Яркость", Range(0, 1.5)) = 1
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_EmissionTex); SAMPLER(sampler_EmissionTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _AtmoColor;
                float _RimPower, _Ambient, _SpecPower, _SpecStrength, _Saturation, _Brightness;
            CBUFFER_END

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
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 nObj = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, i.uv).xyz * 2 - 1;
                float3 n = normalize(TransformObjectToWorldNormal(nObj));
                float3 geo = normalize(i.normalWS);
                Light light = GetMainLight();
                float3 L = normalize(light.direction);
                // Направление на камеру; у ортографической (карта сектора) оно одно для всех точек.
                float3 V = unity_OrthoParams.w > 0.5 ? normalize(UNITY_MATRIX_V[2].xyz) : normalize(_WorldSpaceCameraPos - i.positionWS);

                float sun = saturate(dot(geo, L) * 1.8 + 0.25);      // мягкий терминатор
                float diff = saturate(dot(n, L) * 0.8 + 0.2);         // рельеф с «заворотом» света
                float3 col = albedo.rgb * (_Ambient + (1 - _Ambient) * diff * sun) * light.color;

                float3 H = normalize(L + V);
                col += pow(saturate(dot(n, H)), _SpecPower) * albedo.a * sun * _SpecStrength * light.color;

                float3 glow = SAMPLE_TEXTURE2D(_EmissionTex, sampler_EmissionTex, i.uv).rgb;
                col += glow * (0.6 + 0.4 * (1 - sun));

                float fres = pow(1 - saturate(dot(geo, V)), _RimPower);
                col += _AtmoColor.rgb * fres * (0.7 * sun + 0.12);

                // Силуэт на карте — серый, выработанная планета — тусклая.
                float gray = dot(col, float3(0.3, 0.59, 0.11));
                col = lerp(gray.xxx, col, _Saturation) * _Brightness;
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
