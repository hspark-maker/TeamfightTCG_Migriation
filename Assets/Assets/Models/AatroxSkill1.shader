Shader "Custom/ParticleColorWipe"
{
    Properties
    {
        _MainTex      ("Texture",         2D)    = "white" {}
        _ColorFrom    ("Color From",      Color) = (1, 0, 0, 1)
        _ColorTo      ("Color To",        Color) = (0, 0, 1, 1)
        _Direction    ("Direction (UV)",  Vector) = (0, 1, 0, 0)
        _EdgeSoftness ("Edge Softness",   Float)  = 0.05
        _Width        ("Sweep Width",     Float)  = 0.1
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _ColorFrom;
            fixed4    _ColorTo;
            float4    _Direction;
            float     _EdgeSoftness;
            float     _Width;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 uv     : TEXCOORD0;  // xy=UV, z=AgePercent
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float  proj  : TEXCOORD1;
                fixed4 color : COLOR;
                float  age   : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv.xy, _MainTex);
                o.color = v.color;
                o.age   = v.uv.z;

                float2 dir = normalize(_Direction.xy);
                o.proj = dot(v.uv.xy - 0.5, dir);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float elapsed  = saturate(i.age);
                float boundary = elapsed - 0.5;

                float soft = max(_EdgeSoftness, 0.001);

                // 경계선 기준 Width 범위만 ColorTo로 변환
                float distFromBoundary = abs(i.proj - boundary);
                float t = 1.0 - smoothstep(_Width - soft, _Width + soft, distFromBoundary);

                fixed4 sweepColor = lerp(_ColorFrom, _ColorTo, t);
                fixed4 tex        = tex2D(_MainTex, i.uv);

                fixed4 col  = tex * sweepColor;
                col.a      *= i.color.a;
                col.rgb    *= i.color.rgb;
                return col;
            }
            ENDCG
        }
    }
}