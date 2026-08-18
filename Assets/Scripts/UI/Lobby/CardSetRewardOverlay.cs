using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 카드 여러 장을 한 묶음으로 보여주고 [받기]로 받게 하는 보상 오버레이(CardRewardOverlay의 N장 판).
// 표시와 확인 콜백만 담당하고 지급은 호출자가 한다 — 그래서 출처(튜토리얼 기본 세트든 그 밖이든)를 알 필요가 없다.
// 씬에 저작하지 않고 Addressables 타입 색인에서 독립 Canvas로 세운다(CardRewardOverlay와 같은 규약) — 로비 캔버스에 중첩하면
// 그 프리팹을 저장할 때마다 다른 탭의 저작이 함께 흔들린다.
//
// ⚠ 딤을 눌러 닫히지 않는다. 받아야 넘어가는 자리에 쓰는 물건이라 나가는 문은 [받기] 하나뿐이다.
//
// 낱장 보상(CardRewardOverlay)을 확장하지 않고 따로 세운 이유는 그쪽 안무가 전부 1장 전제이기 때문이다 —
// 위에서 내려꽂히는 슬램·무대 킥·광채 버스트는 카드 하나를 사건으로 만드는 장치라 격자에는 걸리지 않는다.
// 여기서 카드가 서는 리듬은 PackResultGrid의 순차 팝이 대신 쥔다.
public class CardSetRewardOverlay : SingletonOverlay<CardSetRewardOverlay>
{
    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] TMP_Text titleText;
    [SerializeField] Button claimButton;

    [Tooltip("카드가 놓이는 격자. 팩 개봉 결과판과 같은 물건이라 순차 팝·신규 강조가 이미 배선돼 있다.")]
    [SerializeField] PackResultGrid grid;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("카드가 다 선 뒤 [받기]가 열리기까지의 뜸. 이 구간이 무엇을 받았는지 읽는 시간이다 — " +
             "0이면 손이 카드보다 빨라져 못 보고 넘어간다. 격자의 순차 팝이 끝나는 시간보다 길어야 한다.")]
    [SerializeField] float claimDelay = 0.8f;

    [Tooltip("[받기]가 밝아지는 시간.")]
    [SerializeField] float claimFadeDuration = 0.16f;

    // 진행 중 등장 안무. 받기·닫기가 도중에 와도 저작 상태로 되돌린 뒤 이어가야 한다.
    Sequence m_intro;

    // 받기 콜백. 한 번 쓰면 비워 연타를 막는다. 지급이 실패하든 말든 화면은 닫힌다 —
    // 받아야 넘어가는 자리라 여기서 가두면 탈출로가 없다.
    Action m_onClaim;

    CanvasGroup m_claimGroup;

    // 표시할 때마다 새로 만들지 않는다 — 한 화면에 한 번 쓰는 목록이라 재사용으로 족하다.
    readonly List<DrawnCard> m_drawn = new List<DrawnCard>();

    /// <summary>카드가 서 있는 자리. 받은 뒤 이어지는 비행이 여기서 출발해야
    /// "방금 본 그 카드들이 도감으로 갔다"가 한 줄로 이어진다.</summary>
    public RectTransform CardAnchor => this.grid != null ? (RectTransform)this.grid.transform : null;

    /// <summary>보상 오버레이를 얻는다. 씬에 저작해 두지 않고 Addressables 타입 색인에서 세운다.
    /// 평소 꺼져 있는 노드라 이미 선 것을 찾을 때는 비활성까지 뒤진다(CardRewardOverlay와 같은 규약).</summary>
    public static bool TryGet(out CardSetRewardOverlay _overlay)
        => TryGetOrCreate(RuntimeOverlayPrefabs.Get<CardSetRewardOverlay>, out _overlay);

    /// <summary>카드 묶음을 띄운다. _onClaim은 [받기]를 누른 <b>즉시</b> 불린다 —
    /// 그때 지급하고, 이어지는 획득 연출도 그쪽이 튼다(화면은 그 연출에 자리를 넘기고 걷힌다).</summary>
    public void Show(string _title, IReadOnlyList<CardData> _cards, Action _onClaim)
    {
        this.m_onClaim = _onClaim;
        this.KillIntro();

        if (this.titleText != null) this.titleText.text = _title;

        // 보상으로 주는 카드는 언제나 새 카드로 세운다 — 중복 표식(탈채도·환급 칩)이 설 자리가 아니다.
        this.m_drawn.Clear();
        if (_cards != null)
            for (int t_i = 0; t_i < _cards.Count; t_i++)
                if (_cards[t_i] != null) this.m_drawn.Add(new DrawnCard(_cards[t_i], true, 0L));

        if (this.claimButton != null)
        {
            this.claimButton.onClick.RemoveAllListeners();   // 재표시마다 중복 등록 방지
            this.claimButton.onClick.AddListener(this.OnClaimClicked);
        }

        IsOpen = true;
        this.SetVisible(true);

        // 격자 생성은 화면이 켜진 뒤여야 한다 — 꺼진 부모 밑에서는 자리 계산이 도는 동안 캔버스가 갱신되지 않는다.
        if (this.grid != null) this.grid.Show(this.m_drawn);

        // 등장이 도는 동안은 손을 막는다 — 카드가 다 서기 전에 눌러 닫히면 무엇을 받았는지 못 본다.
        this.SetInputEnabled(false);

        this.m_intro = this.BuildIntro();
        this.m_intro.Play();
    }

    public void Hide()
    {
        this.m_onClaim = null;
        this.KillIntro();

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        this.SetVisible(false);
        if (this.grid != null) this.grid.Hide();

        if (t_wasOpen) RaiseClosed();
    }

    // 잠금은 등장 안무가 푼다. Show를 거치지 않고 뜨는 경로(부모가 다시 켜짐)에서는 그 안무가 없어
    // [받기]가 잠긴 모달로 남으므로, 켜질 때 일단 열어 둔다(Show는 이 뒤에 다시 잠근다).
    void OnEnable()
    {
        this.SetInputEnabled(true);
    }

    // 오버레이는 자기 자신이 토글 대상이라 OnDisable이 정상 동작한다 — 잘린 퇴장 마무리를 여기서 위임한다.
    void OnDisable()
    {
        this.transition.HandleDisabled(this.ResolveTarget());
        this.KillIntro();

        // 꺼진 화면은 떠 있는 것이 아니다. Hide를 거치지 않고 꺼지는 경로(부모 비활성·씬 언로드)에서
        // 이 플래그가 남으면 "로비 표면이 보이는가" 판정이 영영 false가 되어 뒤의 안내가 서지 못한다.
        IsOpen = false;
    }

    void OnClaimClicked()
    {
        // 콜백을 먼저 비워 연타로 두 번 지급되는 경로를 막는다(호출자 가드와 이중 방어).
        var t_callback = this.m_onClaim;
        this.m_onClaim = null;
        if (t_callback == null) return;

        this.SetInputEnabled(false);
        this.KillIntro();

        // 화면을 먼저 걷고 지급한다. 순서가 뒤집히면 지급이 트는 획득 연출의 종료 신호를 기다리는 쪽이
        // "보상 화면이 떠 있는 동안 온 신호"로 오인해 흘려보낸다(OutgameTutorialBridge.OnCardGainFinished).
        // 딤이 남아 있으면 그 아래에서 출발한 비행 카드가 가려지는 문제도 같은 순서로 함께 풀린다.
        this.Hide();

        t_callback.Invoke();
    }

    // 등장 안무. 카드가 서는 리듬은 격자가 쥐고 있으니 여기서는 [받기]가 열리는 시각만 정한다.
    Sequence BuildIntro()
    {
        var t_seq = DOTween.Sequence().SetLink(this.gameObject);

        var t_group = this.ClaimGroup();
        if (t_group != null)
        {
            t_group.alpha = 0f;
            t_seq.Insert(this.claimDelay, t_group.DOFade(1f, this.claimFadeDuration));
        }

        // 손은 버튼이 다 뜬 뒤에 돌려준다. 잠금을 푸는 곳이 여기뿐이라, 빠지면 [받기]가 영영 잠긴 모달이 된다.
        t_seq.InsertCallback(this.claimDelay + this.claimFadeDuration, () => this.SetInputEnabled(true));

        t_seq.OnComplete(() => this.m_intro = null);
        return t_seq;
    }

    void KillIntro()
    {
        if (this.m_intro != null && this.m_intro.IsActive()) this.m_intro.Kill();
        this.m_intro = null;

        // 다음 표시가 반투명한 버튼에서 시작하지 않게 원복.
        var t_group = this.ClaimGroup();
        if (t_group != null)
        {
            t_group.DOKill();
            t_group.alpha = 1f;
        }
    }

    void SetInputEnabled(bool _enabled)
    {
        if (this.claimButton != null) this.claimButton.interactable = _enabled;
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;

    // 프리팹에 없으면 붙여 준다 — 배선 여부와 무관하게 안무가 성립해야 한다.
    CanvasGroup ClaimGroup()
    {
        if (this.m_claimGroup != null) return this.m_claimGroup;
        if (this.claimButton == null) return null;

        var t_go = this.claimButton.gameObject;
        this.m_claimGroup = t_go.GetComponent<CanvasGroup>();
        if (this.m_claimGroup == null) this.m_claimGroup = t_go.AddComponent<CanvasGroup>();

        return this.m_claimGroup;
    }
}
