using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 로비 랭크 표시(배지 = 등급, 핍 = 등급 안 단계, 텍스트 = 티어명/포인트).
// 랭크는 전투 씬에서만 변하므로 변경 이벤트 없이 표시 시점에 RankManager를 재조회한다.
public class RankHud : MonoBehaviour
{
    [SerializeField] Image badgeImage;   // 티어 배지
    [SerializeField] TMP_Text descText;  // 티어 표시명("브론즈 1")
    [SerializeField] TMP_Text pointText; // 랭크 포인트

    [Header("단계 핍")]
    [Tooltip("등급 안 단계를 표시하는 칸(왼쪽부터). 개수는 RankConfig.DivisionsPerGrade와 맞춘다 — 모자라면 그만큼만 그린다.")]
    [SerializeField] Image[] divisionPips;
    [SerializeField] Sprite pipOn;
    [SerializeField] Sprite pipOff;

    [Header("승급 연출")]
    [SerializeField] float pipPunch = UiPunch.DEFAULT_SCALE;
    [Tooltip("등급 승급 때 핍이 하나씩 꺼지는 간격.")]
    [SerializeField] float pipStep = 0.1f;
    [Tooltip("핍이 다 꺼진 뒤 배지가 갈리기까지의 뜸.")]
    [SerializeField] float badgeSwapDelay = 0.15f;
    [SerializeField] float badgePunch = 0.4f;

    // 활성 인스턴스(연출 호출자가 찾는 창구). 로비에 하나뿐이지만 탭 토글로 꺼지므로 활성분만 든다.
    static RankHud s_instance;

    // 최초 렌더를 Start로 미루기 위한 표식 — RankConfig 주입(DataLibrary.Awake)보다 OnEnable이 먼저 돌 수 있다.
    bool m_started;

    // 진행 중 승급 연출. 살아있는 동안 Render가 표시를 최종값으로 덮지 않는다(연출이 과거 상태에서 출발한다).
    Sequence m_tierUpSeq;

    /// <summary>활성 랭크 HUD를 얻는다. 꺼져 있으면 false(연출만 건너뛰면 된다).</summary>
    public static bool TryGet(out RankHud _hud)
    {
        _hud = s_instance;
        if (_hud == null)
        {
            s_instance = null;   // 파괴됐는데 OnDisable이 오지 않은 잔재를 걷는다(CurrencyHud.TryGet과 같은 규율).
            return false;
        }

        return true;
    }

    /// <summary>
    /// 티어가 오른 순간을 그린다. 표시는 이미 최종 티어이므로 _prevTierIndex 상태에서 출발해 지금으로 건너온다.
    /// 재생은 호출자 몫 — 끝난 뒤에 무엇을 이을지(보상 패널 등)는 부르는 쪽이 정한다.
    /// _prevTierIndex가 음수면 첫 진입(언랭크 → 첫 티어)으로 본다.
    /// </summary>
    public Sequence BuildTierUp(int _prevTierIndex)
    {
        this.KillTierUp();

        int t_divisions = RankConfig.DivisionsPerGrade;
        var t_info = RankManager.GetInfo();

        int t_prevDivision = _prevTierIndex < 0 ? 0 : _prevTierIndex % t_divisions + 1;
        int t_prevGrade    = _prevTierIndex < 0 ? -1 : _prevTierIndex / t_divisions;
        bool t_gradeUp     = t_info.TierIndex / t_divisions != t_prevGrade;

        this.m_tierUpSeq = DOTween.Sequence().SetLink(this.gameObject);

        // 출발점 = 오르기 직전 화면. 등급까지 바뀌었으면 배지·티어명도 옛것으로 되돌려 두었다가 연출 중에 갈아 끼운다.
        this.m_tierUpSeq.AppendCallback(() =>
        {
            this.RenderPips(t_prevDivision);
            if (t_gradeUp && _prevTierIndex >= 0) this.RenderTier(RankRewardManager.GetInfo(_prevTierIndex));
        });

        // 같은 등급 안 상승(브론즈 1 → 2)도 같은 길을 탄다 — 새로 도달한 칸을 순서대로 켠다(두 칸 이상 뛰어도 다 켜진다).
        if (t_gradeUp) this.StageGradeUp(this.m_tierUpSeq, t_info, t_prevDivision, t_divisions);
        else
            for (int t_i = t_prevDivision; t_i < t_info.Division && t_i < t_divisions; t_i++)
                this.StagePipOn(this.m_tierUpSeq, t_i);

        // 어떤 이유로 끊겨도 표시가 중간 상태로 굳지 않게 한다(연출 가드를 먼저 풀고 정상 규칙으로 되돌린다).
        this.m_tierUpSeq.OnKill(() =>
        {
            this.m_tierUpSeq = null;
            this.Render();
        });

        return this.m_tierUpSeq;
    }

    void Start()
    {
        this.m_started = true;
        this.Render();
    }

    // 탭 재진입(SetActive 토글)만 처리. 첫 활성화는 Start가 담당.
    void OnEnable()
    {
        s_instance = this;

        if (!this.m_started) return;
        this.Render();
    }

    void OnDisable()
    {
        if (s_instance == this) s_instance = null;

        // 꺼지는 동안 트윈만 남으면 다음 활성화가 중간 상태를 물려받는다.
        this.KillTierUp();
    }

    void Render()
    {
        // 연출 중에는 손대지 않는다 — 과거 상태에서 출발하는 연출을 최종값으로 덮어버린다.
        if (this.m_tierUpSeq != null && this.m_tierUpSeq.IsActive()) return;

        var t_info = RankManager.GetInfo();

        this.RenderTier(t_info.DisplayName, t_info.Badge);
        if (this.pointText != null) this.pointText.text = t_info.Points.ToString("N0");

        // 미도달(언랭크)은 아직 한 칸도 딛지 않았다 — 티어 인덱스는 0이지만 핍은 전부 꺼진다.
        this.RenderPips(t_info.IsUnranked ? 0 : t_info.Division);
    }

    // 등급 승급: 채워졌던 핍이 뒤에서부터 하나씩 꺼지고 → 배지가 갈리고 → 새 등급 1단계가 켜진다.
    void StageGradeUp(Sequence _seq, in RankInfo _info, int _prevDivision, int _divisions)
    {
        // in 파라미터는 람다가 잡을 수 없다 — 콜백이 쓸 값만 먼저 떠 둔다.
        string t_name = _info.DisplayName;
        Sprite t_badge = _info.Badge;
        int t_division = _info.Division;

        for (int t_i = _prevDivision - 1; t_i >= 0; t_i--)
        {
            int t_index = t_i;
            _seq.AppendCallback(() => this.SetPip(t_index, false));
            _seq.AppendInterval(this.pipStep);
        }

        _seq.AppendInterval(this.badgeSwapDelay);
        _seq.AppendCallback(() =>
        {
            this.RenderTier(t_name, t_badge);
            UiPunch.Play(this.badgeImage != null ? this.badgeImage.transform : null, this.badgePunch);
        });
        _seq.AppendInterval(this.badgePunch);

        // 새 등급의 1단계부터 다시 쌓기 시작한다(도달 단계가 2 이상인 경우도 순서대로 켠다).
        for (int t_i = 0; t_i < t_division && t_i < _divisions; t_i++)
            this.StagePipOn(_seq, t_i);
    }

    // 핍 하나가 탁 켜지는 단위 동작. _index = 켜질 칸(0-based).
    void StagePipOn(Sequence _seq, int _index)
    {
        _seq.AppendCallback(() =>
        {
            this.SetPip(_index, true);
            UiPunch.Play(this.PipTransform(_index), this.pipPunch);
        });
        _seq.AppendInterval(this.pipStep);
    }

    void RenderTier(in RankRewardInfo _info) => this.RenderTier(_info.DisplayName, _info.Badge);

    // 배지 미저작(null)이면 씬에 배선된 기존 스프라이트를 그대로 둔다.
    void RenderTier(string _displayName, Sprite _badge)
    {
        if (this.badgeImage != null && _badge != null) this.badgeImage.sprite = _badge;
        if (this.descText != null && _displayName != null) this.descText.text = _displayName;
    }

    // _filled칸까지 켜고 나머지는 끈다.
    void RenderPips(int _filled)
    {
        if (this.divisionPips == null) return;

        for (int t_i = 0; t_i < this.divisionPips.Length; t_i++)
            this.SetPip(t_i, t_i < _filled);
    }

    void SetPip(int _index, bool _on)
    {
        if (this.divisionPips == null || _index < 0 || _index >= this.divisionPips.Length) return;

        var t_pip = this.divisionPips[_index];
        if (t_pip == null) return;

        var t_sprite = _on ? this.pipOn : this.pipOff;
        if (t_sprite != null) t_pip.sprite = t_sprite;
    }

    Transform PipTransform(int _index)
    {
        if (this.divisionPips == null || _index < 0 || _index >= this.divisionPips.Length) return null;

        var t_pip = this.divisionPips[_index];
        return t_pip != null ? t_pip.transform : null;
    }

    void KillTierUp()
    {
        if (this.m_tierUpSeq == null) return;

        // Kill이 OnKill을 부르고 그쪽에서 참조를 비운다 — 여기서 먼저 비우면 Render의 연출 가드가 어긋난다.
        this.m_tierUpSeq.Kill();
        this.m_tierUpSeq = null;
    }
}
