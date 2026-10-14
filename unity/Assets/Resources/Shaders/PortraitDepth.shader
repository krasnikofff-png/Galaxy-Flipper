// Портрет магната в 2,5D: картинка автора на сетке, выдавленной по карте глубины (белое — ближе).
// Свет не нужен: портрет уже нарисован со светом. Углы кадра срезаны, как у рамки интерфейса:
// сильно — левый верхний и правый нижний, чуть-чуть — два других. Там прозрачно, и видна карточка.
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
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex, _DepthTex;
            float _Depth, _Cut, _Aspect;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float d = tex2Dlod(_DepthTex, float4(v.uv, 0, 0)).r;
                // Ближнее — к камере (она смотрит вдоль +Z).
                v.vertex.z -= d * _Depth;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Срез углов — в координатах кадра, чтобы совпасть с рамкой интерфейса при любом покачивании.
                float2 s = i.screen.xy / i.screen.w;
                float top = (1 - s.y) * _Aspect, bottom = s.y * _Aspect;
                if (s.x + top < _Cut || (1 - s.x) + bottom < _Cut) discard;
                if ((1 - s.x) + top < _Cut * 0.35 || s.x + bottom < _Cut * 0.35) discard;
                return fixed4(tex2D(_MainTex, i.uv).rgb, 1);
            }
            ENDCG
        }
    }
}
