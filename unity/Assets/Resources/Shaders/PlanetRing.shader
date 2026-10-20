// Кольца планеты: плоское кольцо с полосами и просветами (текстура по радиусу), освещено солнцем с обеих сторон,
// на солнечной стороне ярче; на кольцо падает тень планеты — проверка, закрывает ли шар солнце. URP.
Shader "PlanetTrader/PlanetRing"
{
    Properties
    {
        _MainTex ("Полосы по радиусу (RGB — цвет, A — плотность)", 2D) = "white" {}
        _Brightness ("Яркость", Float) = 1
        _Saturation ("Насыщенность", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
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
                float _Brightness, _Saturation;
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
                float3 positionWS : TEXCOORD1;
                float3 centerWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float radiusWS : TEXCOORD4;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.centerWS = TransformObjectToWorld(float3(0, 0, 0));
                o.normalWS = TransformObjectToWorldNormal(float3(0, 1, 0));
                // Радиус планеты в мире: кольцо в пространстве планеты, её радиус — 1.
                o.radiusWS = length(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0)));
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                float4 band = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(i.uv.x, 0.5));
                Light light = GetMainLight();
                float3 L = normalize(light.direction);
                float lit = 0.35 + 0.65 * abs(dot(normalize(i.normalWS), L));
                // Тень планеты: луч от точки к солнцу проходит через шар?
                float3 d = i.positionWS - i.centerWS;
                float t = dot(d, L);
                float miss = length(d - t * L) - i.radiusWS;
                float shadow = t < 0 ? smoothstep(-0.03, 0.03, miss / i.radiusWS) : 1;
                float3 col = band.rgb * lit * (0.2 + 0.8 * shadow) * light.color * _Brightness;
                float gray = dot(col, float3(0.3, 0.59, 0.11));
                return float4(lerp(gray.xxx, col, _Saturation), band.a);
            }
            ENDHLSL
        }
    }
}
