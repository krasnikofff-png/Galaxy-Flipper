// Картинка автора в 2,5D: на сетке, выдавленной по карте глубины (белое — ближе).
// Свет не нужен: картинка уже нарисована со светом. Углы кадра срезаны, как у рамки интерфейса:
// сильно — левый верхний и правый нижний, чуть-чуть — два других. Там прозрачно, и видна карточка. URP.
Shader "PlanetTrader/PortraitDepth"
{
    Properties
    {
        _MainTex ("Портрет", 2D) = "white" {}
        _DepthTex ("Глубина", 2D) = "black" {}
        _Depth ("Сила рельефа", Float) = 0.65
        _Cut ("Срез углов, доля ширины кадра", Float) = 0.15
        _Aspect ("Высота кадра к ширине", Float) = 1.222
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_DepthTex); SAMPLER(sampler_DepthTex);
            CBUFFER_START(UnityPerMaterial)
                float _Depth, _Cut, _Aspect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float d = SAMPLE_TEXTURE2D_LOD(_DepthTex, sampler_DepthTex, v.uv, 0).r;
                // Ближнее — к камере (она смотрит вдоль +Z).
                v.positionOS.z -= d * _Depth;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                // Координаты кадра 0…1 (как ComputeScreenPos в Built-in): y переворачивается там, где клип-пространство вверх ногами.
                o.screen = float4((o.positionCS.xy * float2(1, _ProjectionParams.x) + o.positionCS.w) * 0.5, o.positionCS.zw);
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                // Срез углов — в координатах кадра, чтобы совпасть с рамкой интерфейса при любом покачивании.
                float2 s = i.screen.xy / i.screen.w;
                float top = (1 - s.y) * _Aspect, bottom = s.y * _Aspect;
                if (s.x + top < _Cut || (1 - s.x) + bottom < _Cut) discard;
                if ((1 - s.x) + top < _Cut * 0.35 || s.x + bottom < _Cut * 0.35) discard;
                return float4(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb, 1);
            }
            ENDHLSL
        }
    }
}
