// 카드팩 "찢김" UI 셰이더. 하나의 찢김선(들쭉날쭉한 곡선)을 네 가지 역할이 공유한다.
//
// 핵심은 찢김선을 기하(마스크 사각형)가 아니라 픽셀 단위 함수로 정의한 것이다 —
// RectMask2D로는 직선밖에 못 그어 "봉지를 뜯었다"가 아니라 "윗도리를 잘랐다"로 읽힌다.
//   line(u) = _TearY + 노이즈(u)   ← u마다 높이가 달라져 종이가 찢긴 결이 생긴다
//   torn(u) = directedU < _TearProgress ← 손가락 진행 방향으로 지나간 곳만 뜯긴다
//
// 빛(모드 3)만 선이 아니라 면이다 — 찢김선에서 위로 뻗는 빛이라 도달거리·확산·감쇠를 따로 쥔다.
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
        _TearDirection ("Tear Direction", Float) = 1
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
        _GlowWidth ("Glow Root Width", Range(0.001,0.2)) = 0.010
        _HeadWidth ("Glow Head Width", Range(0.001,0.5)) = 0.05
        // 새어 나오는 빛: 찢김선에서 위로 뻗는다. 높이는 진행도만큼 자란다.
        _GlowRise ("Glow Reach", Range(0.01,2)) = 0.55
        _GlowFalloff ("Glow Falloff (exp)", Range(0.5,8)) = 2.2
        _GlowHalo ("Glow Halo Strength", Range(0,2)) = 0.7
        _GlowSpread ("Glow Spread By Height", Range(0,4)) = 1.6
        _GlowStreak ("Glow Streak (god ray)", Range(0,1)) = 0.55
        // 줄기는 도달 거리와 따로 논다 — 헤일로는 멀리 퍼져도 줄기만 짧게 끊고 싶을 때 쓴다.
        _GlowStreakLength ("Glow Streak Length", Range(0.05,1)) = 0.5
        // 줄기가 좌우로 흐르는 속도(초당 노이즈 u 이동). 음수면 반대 방향.
        _GlowStreakScroll ("Glow Streak Scroll (u per sec)", Float) = 0.12
        // 빛이 완전히 꺼지는 지점. 지수 감쇠는 0에 닿지 않아 이 창이 없으면 꼭대기까지 잔영이 남는다.
        // 값 = 도달 거리 중 어디서부터 꺼지기 시작하는가(1에서 완전히 0).
        _GlowEnd ("Glow End Fade Start", Range(0,0.99)) = 0.45
        // 빛 전용 페더. 찢김 마스크(_EdgeSoft)는 종이가 갈라진 경계라 날카로워야 하지만,
        // 빛은 같은 값을 쓰면 밑동에서 뚝 켜지고 좌우 끝에서 뚝 잘린다.
        _GlowStartSoft ("Glow Start Softness (v)", Range(0.0005,0.2)) = 0.02
        _GlowSideSoft ("Glow Side Softness (u)", Range(0,0.5)) = 0.08
        _GlowRiseByProgress ("Glow Rise By Progress", Range(0,1)) = 1
        _GlowRippleFreq ("Glow Streak Freq", Float) = 6.3
        _GlowSourceVScale ("Glow Source V Scale", Float) = 1
        // 빛의 그림. 절차적 감쇠 위에 곱해 실제 광원 텍스처의 결을 입힌다(미지정이면 흰색 = 절차적 그대로).
        _GlowTex ("Glow Texture", 2D) = "white" {}
        _GlowTexAmount ("Glow Texture Amount", Range(0,1)) = 1
        _GlowTexTile ("Glow Texture Tiling (u,v)", Vector) = (1,1,0,0)
        _GlowTexScroll ("Glow Texture Scroll (u,v per sec)", Vector) = (0,-0.25,0,0)
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
            float _TearProgress, _TearDirection, _TearY;
            float _JagAmpA, _JagAmpB, _JagFreqA, _JagFreqB;
            float _TearMode, _MouthDepth, _GlowWidth, _HeadWidth, _FrontFeather, _EdgeSoft;
            float _GlowRise, _GlowFalloff, _GlowRiseByProgress, _GlowRippleFreq, _GlowSourceVScale;
            float _GlowHalo, _GlowSpread, _GlowStreak;
            float _GlowStreakLength, _GlowStreakScroll, _GlowEnd;
            float _GlowStartSoft, _GlowSideSoft;
            sampler2D _GlowTex;
            float _GlowTexAmount;
            float4 _GlowTexTile, _GlowTexScroll;

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
                float directedU = _TearDirection < 0.0 ? 1.0 - u : u;

                // 굵은 결 + 잔결. 두 겹을 섞어야 톱니가 아니라 종이 찢긴 결로 읽힌다.
                float jag = (tex2D(_JagTex, float2(u * _JagFreqA, 0.5)).r - 0.5) * _JagAmpA
                          + (tex2D(_JagTex, float2(u * _JagFreqB, 0.5)).g - 0.5) * _JagAmpB;
                float tearLine = _TearY + jag;

                // 선단을 페더만큼 넓힌 범위로 재매핑 — 진행도 0에서 완전히 닫히고 1에서 끝까지 열린다.
                float pr    = lerp(-_FrontFeather, 1.0 + _FrontFeather, _TearProgress);
                float torn  = 1.0 - smoothstep(pr - _FrontFeather, pr, directedU);
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
                    // 새어 나오는 빛: 뜯긴 선에서 **위로 뻗는다**. 선을 따라 번지기만 하면 "금이 갔다"로 읽히고,
                    // 봉지 안에서 무언가 새어 나온다는 신호가 되지 않는다.
                    //
                    // 커튼(균일한 면)이 아니라 빛으로 읽히게 하는 것은 세 가지다:
                    //   ① 지수 감쇠 두 겹 — 좁고 센 코어 + 넓고 옅은 헤일로. 선형 감쇠는 벽으로 보인다.
                    //   ② 높이에 따라 좌우로 벌어짐 — 폭이 일정하면 천, 벌어지면 퍼지는 빛.
                    //   ③ 세로 줄기(god ray) — 결 노이즈를 세로로 길게 늘여 굵기를 흩는다.
                    float grow   = lerp(1.0 - _GlowRiseByProgress, 1.0, _TearProgress);
                    float reach  = max(_GlowRise * grow, 1e-4);

                    float rise = max(v - tearLine, 0.0);
                    float t    = rise / reach;              // 0 = 선, 1 = 도달 거리 끝

                    // ① 코어 + 헤일로
                    float core = exp(-t * _GlowFalloff);
                    float halo = exp(-t * _GlowFalloff * 0.35) * _GlowHalo;

                    // ② 위로 갈수록 선단 경계가 무뎌진다 = 빛이 뜯긴 끝을 넘어 번진다.
                    float spread   = _FrontFeather * (1.0 + t * _GlowSpread * 8.0);
                    float tornSoft = 1.0 - smoothstep(pr - spread, pr + spread, directedU);

                    // ③ 세로 줄기. 위로 갈수록만 갈라진다 — 밑동까지 갈라지면 찢김선이 끊겨 보인다.
                    // 노이즈를 시간에 따라 가로로 흘려 줄기가 좌우로 살아 움직인다.
                    // ⚠ 흘리는 것은 이 샘플뿐이다 — 찢김선(tearLine)까지 흘리면 뜯긴 결이 출렁인다.
                    float streakN = tex2D(_JagTex, float2(u * _GlowRippleFreq + _Time.y * _GlowStreakScroll, 0.5)).g;
                    // 기둥마다 길이가 다르다. 짧은 줄기는 먼저 끊겨 빛이 다발로 읽힌다.
                    float strandReach = max(lerp(_GlowStreakLength, 1.0, streakN), 1e-4);
                    float strandFade  = saturate(1.0 - t / strandReach);
                    float streak  = lerp(1.0, (0.45 + 1.1 * streakN) * strandFade, _GlowStreak * saturate(t));

                    // 빛의 그림. 세로는 도달 거리(t), 가로는 팩 폭에 맞춘다 —
                    // 텍스처를 화면 UV에 그대로 물리면 빛이 팩과 따로 놀고 찢김선에서 떨어져 보인다.
                    float2 gUV = float2(u, saturate(t)) * _GlowTexTile.xy + _GlowTexScroll.xy * _Time.y;
                    // 스크롤이 0~1 밖으로 나가면 텍스처 임포트가 Clamp인 순간 가장자리가 늘어붙어 흐름이 멈춘다.
                    // 여기서 직접 감는다 — 왼쪽 끝으로 나간 픽셀이 오른쪽 끝에서 다시 들어온다.
                    // (임포트 설정에 기대면 이 텍스처를 쓰는 다른 연출까지 바꿔야 한다.)
                    gUV = frac(gUV);
                    // RGB에 모양이 담긴 가산용 텍스처와 알파에 담긴 텍스처가 섞여 있다 — 둘을 곱하면
                    // 어느 쪽이든 모양이 살아남는다(흰 RGB+알파 모양 / 모양 RGB+알파 1 모두 통과).
                    float4 gSample = tex2D(_GlowTex, gUV);
                    float  gTex = gSample.r * gSample.a;
                    float  shape = lerp(1.0, gTex, _GlowTexAmount);

                    // 빛의 시작(찢김선)과 좌우 끝은 종이 경계와 달리 부드럽게 물려야 한다.
                    //   세로: 밑동에서 서서히 켜진다(_EdgeSoft를 쓰면 선 위에서 뚝 켜진다).
                    //   가로: 뜯긴 구간의 시작(팩 가장자리)에서도 서서히 들어온다 —
                    //         선단(tornSoft)만 페더하면 반대쪽 끝이 칼로 자른 듯 남는다.
                    float aboveGlow = smoothstep(tearLine - _GlowStartSoft, tearLine + _GlowStartSoft, v);
                    float sideFade  = smoothstep(0.0, max(_GlowSideSoft, 1e-4), directedU);

                    float beam = (core + halo) * streak * shape * tornSoft * sideFade * aboveGlow;

                    // 지금 찢고 있는 선단이 가장 세다 — 손가락 자리에서 빛이 터져 나온다.
                    float head = saturate(1.0 - abs(directedU - pr) / _HeadWidth);
                    // 선단 덩어리도 같은 가로 마스크를 탄다 — 안 그러면 뜯긴 끝 바깥에
                    // 마스크 없는 밝은 점이 따로 떠 있는 것으로 보인다.
                    beam += head * exp(-t * _GlowFalloff * 0.6) * shape * sideFade * aboveGlow;

                    // 도달 거리 끝에서 확실히 끊는다. exp는 0에 닿지 않아 이 창이 없으면 그래픽 상단까지
                    // 옅은 잔영이 남고, 실루엣을 찢김선 높이에서 뽑는 탓에 그 잔영이 팩 어깨 모양으로 보인다.
                    float endFade = 1.0 - smoothstep(_GlowEnd, 1.0, t);
                    beam *= endFade * endFade;   // 제곱 = 끝으로 갈수록 더 완만하게 사라진다

                    // 밑동은 선 위에 또렷하게 남긴다 — 빛만 있으면 어디가 벌어졌는지 안 보인다.
                    float root = saturate(1.0 - abs(v - tearLine) / _GlowWidth) * torn;

                    // 실루엣은 **찢김선 높이**에서 뽑는다. 픽셀 위치에서 뽑으면 위로 뻗은 빛이
                    // 팩 알파 밖으로 나가는 순간 잘려 빛이 선에 도로 붙는다.
                    float sourceTearV = saturate(tearLine * _GlowSourceVScale);
                    // 팩 알파는 가장자리에서 거의 계단이라 그대로 쓰면 빛의 좌우 끝이 칼로 자른 듯 보인다.
                    // 가로로 다섯 번 떠서 흐린다. 폭은 높이에 비례해 벌어진다 — 위로 갈수록 더 퍼진 빛.
                    float sideW = max(_GlowSideSoft, 1e-4) * (0.35 + t * _GlowSpread * 0.5);
                    float su = IN.texcoord.x;
                    float silhouette =
                        ( tex2D(_MainTex, float2(su - sideW,        sourceTearV)).a
                        + tex2D(_MainTex, float2(su - sideW * 0.5,  sourceTearV)).a
                        + tex2D(_MainTex, float2(su,                sourceTearV)).a
                        + tex2D(_MainTex, float2(su + sideW * 0.5,  sourceTearV)).a
                        + tex2D(_MainTex, float2(su + sideW,        sourceTearV)).a ) * 0.2;

                    // 스프라이트 색이 아니라 지정색으로 빛난다(등급 색이 여기로 들어온다).
                    col.rgb = IN.color.rgb;
                    col.a = silhouette * IN.color.a * saturate(beam + root);
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
