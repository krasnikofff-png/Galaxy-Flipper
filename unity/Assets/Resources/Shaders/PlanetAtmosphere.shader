// Ореол атмосферы: сфера больше планеты, рисуется изнанкой и складывается со светом.
// Ярче всего у самого края планеты, к внешнему краю гаснет; на солнечной стороне ярче.
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
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Front
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Inner, _Power, _Strength;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 posW : TEXCOORD0;
                float3 normalW : TEXCOORD1;
                float3 centerW : TEXCOORD2;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.posW = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normalW = UnityObjectToWorldNormal(v.normal);
                o.centerW = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Направление на камеру; у ортографической (карта сектора) оно одно для всех точек.
                float3 V = unity_OrthoParams.w > 0.5 ? normalize(UNITY_MATRIX_V[2].xyz) : normalize(_WorldSpaceCameraPos - i.posW);
                float3 n = normalize(i.normalW);
                // Изнанка: в центре диска d≈1, у внешнего края 0. Край планеты — при d = sqrt(1 - inner²).
                float d = saturate(dot(-n, V));
                float edge = sqrt(1 - _Inner * _Inner);
                float glow = pow(saturate(d / edge), _Power);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float side = saturate(dot(normalize(i.posW - i.centerW), L) * 0.6 + 0.55);
                return fixed4(_Color.rgb * glow * side * _Strength, 1);
            }
            ENDCG
        }
    }
}
