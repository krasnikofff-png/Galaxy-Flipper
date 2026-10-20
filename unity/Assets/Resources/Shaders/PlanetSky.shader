// Фон: тёмный космос со звёздами и туманностью; текстура запекается в коде.
// В витрине — изнанка большой сферы, на карте сектора — плоский прямоугольник (_Cull = 0).
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
        Tags { "Queue" = "Background" "RenderType" = "Background" }
        ZWrite Off
        Cull [_Cull]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float _Dim;
            fixed4 frag (v2f i) : SV_Target { return tex2D(_MainTex, i.uv) * _Dim; }
            ENDCG
        }
    }
}
