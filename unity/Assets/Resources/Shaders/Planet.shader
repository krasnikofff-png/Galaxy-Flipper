// Поверхность планеты: запечённый цвет и рельеф, мягкий край ночи, блик на воде и льду,
// свечение (кристаллы, разломы аномалии) и подсветка атмосферы по краю диска.
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
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex, _NormalTex, _EmissionTex;
            float4 _AtmoColor;
            float _RimPower, _Ambient, _SpecPower, _SpecStrength, _Saturation, _Brightness;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 posW : TEXCOORD1;
                float3 normalW : TEXCOORD2;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.posW = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normalW = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float4 albedo = tex2D(_MainTex, i.uv);
                float3 nObj = tex2D(_NormalTex, i.uv).xyz * 2 - 1;
                float3 n = normalize(UnityObjectToWorldNormal(nObj));
                float3 geo = normalize(i.normalW);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                // Направление на камеру; у ортографической (карта сектора) оно одно для всех точек.
                float3 V = unity_OrthoParams.w > 0.5 ? normalize(UNITY_MATRIX_V[2].xyz) : normalize(_WorldSpaceCameraPos - i.posW);

                float sun = saturate(dot(geo, L) * 1.8 + 0.25);      // мягкий терминатор
                float diff = saturate(dot(n, L) * 0.8 + 0.2);         // рельеф с «заворотом» света
                float3 col = albedo.rgb * (_Ambient + (1 - _Ambient) * diff * sun) * _LightColor0.rgb;

                float3 H = normalize(L + V);
                col += pow(saturate(dot(n, H)), _SpecPower) * albedo.a * sun * _SpecStrength * _LightColor0.rgb;

                float3 glow = tex2D(_EmissionTex, i.uv).rgb;
                col += glow * (0.6 + 0.4 * (1 - sun));

                float fres = pow(1 - saturate(dot(geo, V)), _RimPower);
                col += _AtmoColor.rgb * fres * (0.7 * sun + 0.12);

                // Силуэт на карте — серый, выработанная планета — тусклая.
                float gray = dot(col, float3(0.3, 0.59, 0.11));
                col = lerp(gray.xxx, col, _Saturation) * _Brightness;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
