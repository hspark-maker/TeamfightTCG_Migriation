using Coffee.UIEffects;
using DG.Tweening;
using UnityEngine;

// UI 그래픽 묶음을 아래부터 삭혀 없앤다(UIEffect Dissolve). 알파 페이드는 "지워진다"로 읽히고,
// 이건 "부서진다"로 읽힌다 — 차이를 만드는 것은 갉히는 경계선 하나다.
//
// 카드는 아트·프레임 여러 장으로 쪼개져 있어서 효과마다 자기 rect를 UV로 쓰면 조각마다 따로 삭는다.
// 그래서 전부 같은 customRoot(=카드 뿌리)를 보게 만든다 → 여러 장이 한 덩어리로 부서진다.
// 이게 UIEffect를 쓰는 이유이기도 하다 — AllIn1(UiMask)의 FADE 축은 이 공유를 못 해서
// 카드 한 장이 조각 수만큼 갈라져 삭는다.
//
// 패턴은 자산이 아니라 코드가 굽는다. 세로 그라디언트 × 값노이즈를 알파에 구운 텍스처 한 장이면 되는데,
// 자산으로 두면 배선이 하나 늘고(미배선이면 조용히 균일하게 사라져 버그로 안 보인다)
// "셰이더는 알파 채널만 읽는다"는 규약이 임포트 설정에 숨는다.
//
// ⚠ 셰이더 변이(…TRANSITION_DISSOLVE 조합)는 UIEffect가 에디터 플레이 중 자동 등록한다.
//   한 번 돌린 뒤 Assets/ProjectSettings/UIEffectProjectSettings.asset의 diff를 커밋해야
//   빌드에서도 보인다 — 등록되지 않은 변이는 스트립되고, 그러면 카드가 그냥 안 사라진다.
public static class UiCrumble
{
    const int   PatternSize = 128;

    // 알파 상한. 1을 그대로 쓰면 rate=1에서도 그 텍셀이 실오라기처럼 남는다
    // (셰이더가 frac(rate * 0.9999)를 쓰기 때문에 정확히 1에 닿지 않는다).
    const float PatternMaxAlpha = 0.94f;

    // 세로 그라디언트와 노이즈의 배합. 노이즈가 세지면 어디부터 삭는지가 안 읽히고,
    // 그라디언트만 남기면 자를 대고 지운 듯 일직선으로 걷힌다.
    const float NoiseWeight = 0.42f;

    // 노이즈 두 겹의 칸 수. 굵은 겹이 부서지는 덩어리 크기를, 잔 겹이 경계의 거칠기를 만든다.
    const int CoarseCells = 6;
    const int FineCells   = 14;

    static Texture2D s_pattern;

    /// <summary>삭는 순서를 담은 패턴(알파 낮은 곳이 먼저 사라진다). 앱 수명 동안 한 장을 공유한다.</summary>
    public static Texture2D Pattern => s_pattern != null ? s_pattern : (s_pattern = BuildPattern());

    /// <summary>
    /// 삭을 준비만 시킨다(아직 멀쩡한 상태 = rate 0). <paramref name="_root"/>를 공유해야
    /// 여러 조각이 한 덩어리로 부서진다 — 보통 카드 타일의 뿌리를 넘긴다.
    /// 톤·색 축(흑백·물들이기)은 건드리지 않는다 — 부르는 쪽이 이미 걸어 뒀을 수 있다.
    /// </summary>
    public static void Arm(UIEffect _fx, RectTransform _root, Color _edgeColor,
                           float _edgeWidth = 0.2f, float _softness = 0.5f)
    {
        if (_fx == null) return;

        _fx.customRoot = _root;

        _fx.transitionFilter      = TransitionFilter.Dissolve;
        _fx.transitionTexture     = Pattern;
        _fx.transitionRate        = 0f;   // ⚠ 컴포넌트 기본값이 0.5다 — 안 세우면 붙는 순간 반쯤 삭은 채로 뜬다.
        _fx.transitionWidth       = _edgeWidth;
        _fx.transitionSoftness    = _softness;
        _fx.transitionColorFilter = ColorFilter.Additive;   // 경계는 밑색에 얹는 빛이다
        _fx.transitionColor       = _edgeColor;
    }

    /// <summary>0(멀쩡) → 1(다 부서짐). 뒤로 갈수록 빨라진다 — 등속이면 무너지는 게 아니라 지워지는 것으로 읽힌다.
    /// 넘긴 것이 없으면 null(부르는 쪽이 이 축을 건너뛴다).</summary>
    public static Tween BuildTween(UIEffect[] _fx, float _duration)
    {
        if (_fx == null || _fx.Length == 0) return null;

        return DOTween.To(() => 0f, _v => SetRate(_fx, _v), 1f, _duration).SetEase(Ease.InQuad);
    }

    /// <summary>0(멀쩡) ~ 1(다 부서짐)을 묶음 전체에 같은 값으로 민다.</summary>
    public static void SetRate(UIEffect[] _fx, float _rate)
    {
        if (_fx == null) return;

        for (int t_i = 0; t_i < _fx.Length; t_i++)
        {
            // 타일이 먼저 걷히고 트윈이 한 프레임 늦게 도는 경우가 있다.
            if (_fx[t_i] == null) continue;

            _fx[t_i].transitionRate = _rate;
        }
    }

    // 아래(알파 0)에서 위(알파 1)로 오르는 그라디언트에 노이즈를 섞는다 →
    // 셰이더가 알파 낮은 곳부터 지우므로 밑에서부터 우툴두툴하게 갉힌다.
    static Texture2D BuildPattern()
    {
        var t_raw = new float[PatternSize * PatternSize];

        float t_min = float.MaxValue;
        float t_max = float.MinValue;

        for (int t_y = 0; t_y < PatternSize; t_y++)
        {
            float t_v = t_y / (float)(PatternSize - 1);

            for (int t_x = 0; t_x < PatternSize; t_x++)
            {
                float t_u = t_x / (float)(PatternSize - 1);

                float t_noise = Noise(t_u, t_v, CoarseCells) * 0.65f + Noise(t_u, t_v, FineCells) * 0.35f;
                float t_value = Mathf.Lerp(t_v, t_noise, NoiseWeight);

                t_raw[t_y * PatternSize + t_x] = t_value;

                if (t_value < t_min) t_min = t_value;
                if (t_value > t_max) t_max = t_value;
            }
        }

        // 실제로 나온 범위를 0~상한으로 펴 준다. 노이즈를 섞으면 값이 가운데로 몰리는데(양끝은 노이즈가
        // 극단으로 가야 나온다), 그대로 두면 트윈의 앞뒤 구간에서 화면이 아무 일도 안 하고 멈춰 있다.
        // 배합(NoiseWeight)을 다시 만져도 시간 배분이 따라오게 하려고 상수가 아니라 측정값으로 편다.
        float t_span = Mathf.Max(0.0001f, t_max - t_min);

        var t_pixels = new Color32[t_raw.Length];

        for (int t_i = 0; t_i < t_raw.Length; t_i++)
        {
            float t_alpha = (t_raw[t_i] - t_min) / t_span * PatternMaxAlpha;
            t_pixels[t_i] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(t_alpha) * 255f));
        }

        // 셰이더는 알파 채널만 읽는다 → 알파 한 장이면 충분하다.
        var t_tex = new Texture2D(PatternSize, PatternSize, TextureFormat.Alpha8, false)
        {
            name      = "UiCrumblePattern",
            wrapMode  = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        t_tex.SetPixels32(t_pixels);
        t_tex.Apply(false, true);   // CPU 사본은 버린다(다시 읽을 일이 없다)

        return t_tex;
    }

    // 값노이즈. Random을 쓰지 않는 이유는 전역 시드를 건드리지 않으려는 것도 있지만,
    // 같은 화면이 매 실행 다르게 부서지면 연출을 눈으로 맞출 수가 없기 때문이다.
    static float Noise(float _u, float _v, int _cells)
    {
        float t_fx = _u * _cells;
        float t_fy = _v * _cells;

        int t_x0 = Mathf.FloorToInt(t_fx);
        int t_y0 = Mathf.FloorToInt(t_fy);

        float t_tx = Smooth(t_fx - t_x0);
        float t_ty = Smooth(t_fy - t_y0);

        float t_low  = Mathf.Lerp(Hash(t_x0, t_y0),     Hash(t_x0 + 1, t_y0),     t_tx);
        float t_high = Mathf.Lerp(Hash(t_x0, t_y0 + 1), Hash(t_x0 + 1, t_y0 + 1), t_tx);

        return Mathf.Lerp(t_low, t_high, t_ty);
    }

    static float Smooth(float _t) => _t * _t * (3f - 2f * _t);

    static float Hash(int _x, int _y)
    {
        int t_h = _x * 374761393 + _y * 668265263;
        t_h = (t_h ^ (t_h >> 13)) * 1274126177;

        return ((t_h ^ (t_h >> 16)) & 0xFFFF) / 65535f;
    }
}
