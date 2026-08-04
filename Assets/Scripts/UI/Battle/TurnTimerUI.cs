using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 내 턴 생각시간 남은 초 + 바깥 링 게이지 표시. 시간은 TurnThinkTimer(단일 소스)가 소유하고,
/// 여기선 읽어서 표시만 한다(자체 카운트 금지 → 드리프트 방지).
/// TurnThinkTimer.Active(내 턴 InputAllowed 구간)일 때만 보이고 그 외엔 숨김.
/// 표시 전용이라 결정론/멀티와 무관 — 프레임률·배속이 달라도 게임 상태에 영향 없다.
///
/// 링은 Image.type=Filled + fillMethod=Radial360. 남은 비율(TurnThinkTimer.Normalized)을
/// fillAmount에 그대로 꽂으므로 링이 "시계 반대로 줄어드는" 방향은 Image의 fillClockwise/fillOrigin
/// 인스펙터 설정으로 정한다(코드가 방향을 강제하지 않는다).
///
/// 연출은 DOTween을 쓰지 않고 Update 보간으로 처리한다 — 매 프레임 값이 갱신되는 게이지라
/// 트윈을 걸면 서로 덮어써 튀고, 파괴 시 DOKill 규약까지 따라붙는다.
/// </summary>
public class TurnTimerUI : MonoBehaviour
{
    [SerializeField] TMP_Text label;

    [Header("Ring")]
    [SerializeField] Image ring;        // Filled + Radial360. 남은 비율이 곧 fillAmount
    [SerializeField] Image ringTrack;   // 뒤에 깔리는 고정 링(옵션)
    [SerializeField] Image ringGlow;    // 위험 구간 발광(옵션)
    // 링 뒤 배경판(옵션). 색은 TurnSideTint가 턴 주인에 따라 칠하고, 여기선 링과 같이 켜고 끄기만 한다.
    [SerializeField] Image ringBg;

    [Header("Color")]
    // 여유 → 경고 → 위험. 구간 사이는 보간해서 색이 뚝 끊기지 않게 한다.
    [SerializeField] Color normalColor = new Color(0.45f, 0.85f, 1f);
    [SerializeField] Color warnColor   = new Color(1f, 0.78f, 0.25f);
    [SerializeField] Color dangerColor = new Color(1f, 0.28f, 0.28f);
    [SerializeField] float warnSeconds   = 10f;
    [SerializeField] float dangerSeconds = 5f;

    [Header("Motion")]
    // 펀치 대상은 pivot이 (0.5,0.5)여야 중앙 기준으로 커진다 — 위쪽 pivot이면 아래로만 늘어나 부자연스럽다.
    [SerializeField] RectTransform punchTarget;          // 초가 바뀔 때 튀는 대상(미지정이면 이 오브젝트)
    [SerializeField] float punchSeconds      = 15f;      // 이 시간 이하부터만 초마다 펀치(여유 구간은 조용히)
    [SerializeField] float punchScale        = 1.18f;
    [SerializeField] float punchDuration     = 0.14f;
    [SerializeField] float dangerPulsePerSec = 3f;       // 위험 구간 맥박 횟수/초
    [SerializeField] float dangerPulseAmount = 0.06f;    // 맥박 스케일 진폭

    int   m_lastSec      = -1;
    float m_punchElapsed = float.MaxValue;   // 초기값=펀치 없음
    bool  m_visible      = true;             // 첫 Update에서 무조건 한 번 동기화되게 반대값

    void Awake()
    {
        if (this.label == null)       this.label       = GetComponent<TMP_Text>();
        if (this.punchTarget == null) this.punchTarget = transform as RectTransform;
    }

    void Update()
    {
        bool t_show = TurnThinkTimer.Active;
        if (t_show != this.m_visible) ApplyVisible(t_show);

        if (!t_show)
        {
            // 다음 턴에 첫 초부터 다시 펀치가 들어가게 리셋.
            this.m_lastSec      = -1;
            this.m_punchElapsed = float.MaxValue;
            return;
        }

        float t_remain = TurnThinkTimer.Remaining;
        int   t_sec    = Mathf.CeilToInt(t_remain);

        if (t_sec != this.m_lastSec)
        {
            bool t_first = this.m_lastSec < 0;            // 턴 첫 표시는 튀지 않는다(등장과 겹쳐 산만해짐)
            this.m_lastSec = t_sec;
            if (this.label != null) this.label.text = t_sec.ToString();

            // 남은 시간이 punchSeconds 이하일 때만 초마다 펀치.
            if (!t_first && t_remain <= this.punchSeconds) this.m_punchElapsed = 0f;
        }

        if (this.ring != null) this.ring.fillAmount = TurnThinkTimer.Normalized;

        Color t_color = ResolveColor(t_remain);
        if (this.label != null) this.label.color = t_color;
        if (this.ring  != null) this.ring.color  = t_color;

        ApplyGlow(t_remain, t_color);
        ApplyScale(t_remain);
    }

    /// <summary>여유/경고/위험 구간 색. 구간 경계에서 튀지 않게 인접 색끼리 보간한다.</summary>
    Color ResolveColor(float _remain)
    {
        if (_remain > this.warnSeconds) return this.normalColor;

        if (_remain > this.dangerSeconds)
            return Color.Lerp(this.warnColor, this.normalColor,
                              Mathf.InverseLerp(this.dangerSeconds, this.warnSeconds, _remain));

        // 위험 구간: 0초로 갈수록 dangerColor에 수렴.
        float t_k = this.dangerSeconds > 0f ? Mathf.Clamp01(_remain / this.dangerSeconds) : 0f;
        return Color.Lerp(this.dangerColor, this.warnColor, t_k);
    }

    void ApplyGlow(float _remain, Color _color)
    {
        if (this.ringGlow == null) return;

        // 위험 구간에서만 맥박. 그 밖에서는 완전 투명(오브젝트는 켜둔 채 알파로만 죽인다).
        float t_alpha = 0f;
        if (_remain <= this.dangerSeconds)
        {
            float t_wave = Mathf.Sin(Time.unscaledTime * this.dangerPulsePerSec * Mathf.PI * 2f) * 0.5f + 0.5f;
            t_alpha = Mathf.Lerp(0.15f, 0.6f, t_wave);
        }

        _color.a = t_alpha;
        this.ringGlow.color = _color;
    }

    void ApplyScale(float _remain)
    {
        if (this.punchTarget == null) return;

        float t_scale = 1f;

        // 초 넘어갈 때 펀치(ease-out).
        if (this.m_punchElapsed < this.punchDuration)
        {
            this.m_punchElapsed += Time.unscaledDeltaTime;
            float t_k = 1f - Mathf.Clamp01(this.m_punchElapsed / this.punchDuration);
            t_scale *= Mathf.Lerp(1f, this.punchScale, t_k * t_k);
        }

        // 위험 구간 상시 맥박(펀치와 곱해져 자연히 합쳐진다).
        if (_remain <= this.dangerSeconds)
        {
            float t_wave = Mathf.Sin(Time.unscaledTime * this.dangerPulsePerSec * Mathf.PI * 2f);
            t_scale *= 1f + t_wave * this.dangerPulseAmount;
        }

        this.punchTarget.localScale = Vector3.one * t_scale;
    }

    void ApplyVisible(bool _show)
    {
        this.m_visible = _show;

        if (this.label     != null) this.label.enabled     = _show;
        if (this.ring      != null) this.ring.enabled      = _show;
        if (this.ringTrack != null) this.ringTrack.enabled = _show;
        if (this.ringGlow  != null) this.ringGlow.enabled  = _show;
        if (this.ringBg    != null) this.ringBg.enabled    = _show;

        // 숨길 때 스케일을 원복 — 위험 구간에서 턴이 끝나면 커진 상태로 굳는다.
        if (!_show && this.punchTarget != null) this.punchTarget.localScale = Vector3.one;
    }
}
