// 카드팩 "찢김" UI 셰이더. 하나의 찢김선(들쭉날쭉한 곡선)을 네 가지 역할이 공유한다.
//
// 핵심은 찢김선을 기하(마스크 사각형)가 아니라 픽셀 단위 함수로 정의한 것이다 —
// RectMask2D로는 직선밖에 못 그어 "봉지를 뜯었다"가 아니라 "윗도리를 잘랐다"로 읽힌다.
//   line(u) = _TearY + 노이즈(u)   ← u마다 높이가 달라져 종이가 찢긴 결이 생긴다
//   torn(u) = u < _TearProgress    ← 손가락이 지나간 곳만 뜯긴다(오른쪽은 아직 붙어 있다)
//
// 몸통과 조각이 같은 line·torn을 쓰므로 뜯긴 조각과 구멍은 언제나 정확히 맞물린다.
// 즉 조각을 따로 그리거나 아트를 새로 만들 필요가 없다 — 같은 스프라이트를 모드만 바꿔 두 번 그린다.
//
// ⚠ 전제: 아틀라스에 묶이지 않은 단독 스프라이트(UV가 0~1 전체를 덮는다). 팩 스프라이트를 아틀라스에
//   넣으면 texcoord가 부분 rect가 되어 찢김선이 엉뚱한 높이에 생긴다.
Shader "UI/PackTear"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // 빛(모드 3)만 가산 합성으로 쓰려고 블렌드를 재질 단으로 뺐다.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10

        [Header(Tear)]
        // R = 굵은 결, G = 잔결. 런타임에 PackTearSkin이 시드로 생성해 물린다(아트 의존 없음).
        _JagTex ("Jag Noise (R coarse, G fine)", 2D) = "gray" {}
        _TearProgress ("Tear Progress", Range(0,1)) = 0
        _TearY ("Tear Line V", Range(0,1)) = 0.874
        _JagAmpA ("Jag Amp Coarse", Range(0,0.2)) = 0.028
        _JagAmpB ("Jag Amp Fine", Range(0,0.1)) = 0.008
        // 굵은 결은 노이즈를 폭에 정확히 한 바퀴 감는다(= 텍스처 폭이 곧 결의 개수).
        // 잔결은 같은 패턴을 정수가 아닌 배수로 여러 바퀴 감아 굵은 결과 마디가 겹치지 않게 한다.
        _JagFreqA ("Jag Freq Coarse", Float) = 1
        _JagFreqB ("Jag Freq Fine", Float) = 4.37
        // 0 = 팩 몸통 / 1 = 뜯긴 조각 / 2 = 입구 그늘 / 3 = 찢김선 빛
        _TearMode ("Mode", Float) = 0
        _MouthDepth ("Mouth Shadow Depth", Range(0.001,0.5)) = 0.055
        _GlowWidth ("Glow Line Width", Range(0.001,0.2)) = 0.010
        _HeadWidth ("Glow Head Width", Range(0.001,0.5)) = 0.05
        _FrontFeather ("Tear Front Feather", Range(0.0005,0.2)) = 0.006
        _EdgeSoft ("Tear Edge Soft", Range(0.0002,0.02)) = 0.0015
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend [_SrcBlend] [_DstBlend]
        ColorMask [_ColorMask]

        Pass
        {
            Name "PACKTEAR"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            sampler2D _JagTex;
            float _TearProgress, _TearY;
            float _JagAmpA, _JagAmpB, _JagFreqA, _JagFreqB;
            float _TearMode, _MouthDepth, _GlowWidth, _HeadWidth, _FrontFeather, _EdgeSoft;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 tex = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half4 col = tex * IN.color;

                float u = IN.texcoord.x;
                float v = IN.texcoord.y;

                // 굵은 결 + 잔결. 두 겹을 섞어야 톱니가 아니라 종이 찢긴 결로 읽힌다.
                float jag = (tex2D(_JagTex, float2(u * _JagFreqA, 0.5)).r - 0.5) * _JagAmpA
                          + (tex2D(_JagTex, float2(u * _JagFreqB, 0.5)).g - 0.5) * _JagAmpB;
                float tearLine = _TearY + jag;

                // 선단을 페더만큼 넓힌 범위로 재매핑 — 진행도 0에서 완전히 닫히고 1에서 끝까지 열린다.
                float pr    = lerp(-_FrontFeather, 1.0 + _FrontFeather, _TearProgress);
                float torn  = 1.0 - smoothstep(pr - _FrontFeather, pr, u);
                float above = smoothstep(tearLine - _EdgeSoft, tearLine + _EdgeSoft, v);
                float hole  = above * torn;   // 몸통에서 실제로 뚫린 영역

                if (_TearMode < 0.5)
                {
                    // 몸통: 뚫린 곳을 지운다. 카드는 이 뒤에 있으므로 지운 만큼만 드러난다.
                    col.a *= 1.0 - hole;
                }
                else if (_TearMode < 1.5)
                {
                    // 뜯긴 조각: 몸통이 지운 그 영역만 남긴다(둘이 정확히 맞물린다).
                    col.a *= hole;
                }
                else if (_TearMode < 2.5)
                {
                    // 입구 그늘: 찢김선 바로 "위"를 어둡게 — 봉지에서 막 나온 카드 밑동에 그늘이 없으면
                    // 카드가 팩 앞에 얹힌 것으로 보인다. 아래쪽은 어차피 몸통이 덮으므로 그릴 이유가 없다.
                    col.a *= torn * above * saturate(1.0 - (v - tearLine) / _MouthDepth);
                }
                else
                {
                    // 찢김선 빛: 뜯긴 선을 따라 번지고, 지금 찢고 있는 선단이 가장 밝다.
                    float band = saturate(1.0 - abs(v - tearLine) / _GlowWidth) * torn;
                    float head = saturate(1.0 - abs(u - pr) / _HeadWidth)
                               * saturate(1.0 - abs(v - tearLine) / (_GlowWidth * 3.0));
                    // 스프라이트 색이 아니라 지정색으로 빛난다. 알파만 팩 실루엣 안으로 가둔다.
                    col.rgb = IN.color.rgb;
                    col.a = tex.a * IN.color.a * saturate(band + head);
                }

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
        ENDCG
        }
    }
}
