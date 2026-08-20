using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보상 토너먼트 경로의 정점 하나(TournamentNode 프리팹 루트에 부착).
// 인덱스만 들고 표시값은 매번 TournamentProgress에서 다시 받는다(스냅샷을 캐싱하면 클리어 후 stale).
public class TournamentNodeView : MonoBehaviour
{
    /// <summary>정점 종류 한 줄. 종류가 늘어도 저작 한 줄만 더하면 된다(CurrencyLook과 같은 관용구).</summary>
    [Serializable]
    public struct KindLook
    {
        public ETournamentNodeKind kind;

        [Tooltip("비우면 그 종류는 표식을 켜지 않는다.")]
        public Sprite badge;
    }

    [SerializeField] Image avatarImage;      // 상대 초상(저작 미배선이면 프리팹 기본 유지)
    [SerializeField] TMP_Text nameText;      // 상대 표시명
    [SerializeField] Button tapButton;       // 정점 = 도전 버튼

    [Tooltip("보상 미리보기 칸(아이콘 + 수량). 저작한 보상이 칸 수보다 적으면 남는 칸은 꺼진다.")]
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;

    [Header("상태 레이어(선택 — 미배선 시 null 가드)")]
    [Tooltip("상태마다 켜지는 '묶음'이다 — 표식 한 장이 아니라 그 상태에서만 보여야 할 것을 통째로 담는다.\n" +
             "잠김: 어두운 베일 + 자물쇠 / 클리어: 금테 + 체크 배지 / 도전 가능: 포커스 링 + 이름표 + 보상칸.\n" +
             "이름표·보상칸을 currentMark 안에 두는 것이 곧 '도전할 정점에만 정보를 편다'는 규칙이다.")]
    [SerializeField] GameObject lockedMark;      // 잠김(베일 + 자물쇠)
    [SerializeField] GameObject clearedMark;     // 클리어(금테 + 체크)
    [SerializeField] GameObject currentMark;     // 지금 도전할 정점(포커스 링 + 이름표 + 보상)
    [SerializeField] CanvasGroup canvasGroup;

    [Header("수령 대기(깼지만 미수령)")]
    [Tooltip("수령 대기 정점의 초상 자리에 놓을 그림. 표식을 따로 얹지 않고 초상 자체가 선물로 바뀐다 —\n" +
             "원판 위에 무엇을 덧대면 그 상태만 형태가 달라져 다른 상태와 한 벌로 안 읽힌다.")]
    [SerializeField] Sprite giftPortrait;

    [Tooltip("선물 등장·대기 흔들림이 미는 대상(보통 Medallion). 비우면 연출 없이 그림만 바뀐다.\n" +
             "초상만 미는 것이 아니라 원판째 밀어야 '정점에 사건이 났다'로 읽힌다.")]
    [SerializeField] RectTransform giftPunchTarget;

    [Tooltip("잠긴 정점의 알파. 무채색화와 병용이라 너무 낮추면 배경에 묻힌다.\n" +
             "클리어는 따로 딤하지 않는다(체크 표식이 상태를 말한다).")]
    [SerializeField] float lockedAlpha = 0.7f;

    [Tooltip("잠긴 정점의 초상을 누를 색. 무채색화만으론 '누구인지'가 그대로 보여 미리보기가 성립하지 않는다.\n" +
             "완전한 검정이 아닌 이유는 원판의 윤곽이 남아야 실루엣으로 읽히기 때문이다.")]
    [SerializeField] Color lockedSilhouette = new Color(0.12f, 0.14f, 0.20f, 1f);

    [Header("정점 종류 표식")]
    [Tooltip("종류 표식이 앉는 칸. 상태 묶음 밖에 두어 잠겨도 보인다 — 잠긴 정점이 말할 수 있는 유일한 정보다.")]
    [SerializeField] Image kindBadge;

    [Tooltip("종류별 표식. 저작되지 않은 종류는 표식을 켜지 않는다(Battle이 그 자리다).\n" +
             "같은 종류가 여러 줄이면 위쪽 줄이 이긴다.")]
    [SerializeField] KindLook[] kindLooks;

    // 보상 조회용 공용 버퍼 — 칸이 값을 즉시 복사하므로 뷰마다 리스트를 들 이유가 없다.
    static readonly List<RewardLine> s_rewardBuffer = new List<RewardLine>();

    static bool s_overflowWarned;

    // 표시 대상 정점. -1 = 미바인딩(Refresh 무시).
    int m_index = -1;

    // 잠김 무채색화를 되돌릴 자리. null = 지금 색이 살아 있다.
    List<UiGrayscale.Toned> m_toned;

    // 실루엣을 되돌릴 자리. 초상의 저작색이 흰색이라는 보장이 없어 처음 볼 때 받아 둔다.
    Color? m_avatarColor0;

    Action<int> m_onTap;

    // 선물 등장(1회)과 대기 흔들림(상시)은 같은 대상을 밀어 한 자리에 모아 죽인다.
    Sequence m_giftSeq;
    Tween m_giftIdle;

    // 초상의 프리팹 저작값. 선물로 갈아끼운 뒤 되돌릴 자리다(저작 초상이 없는 정점이 대부분이라 필요하다).
    Sprite m_portrait0;

    // 등장 연출을 기다리는 중 — 그때까진 선물을 숨긴다(이미 서 있다가 다시 튀어나오면 등장이 아니다).
    bool m_giftArmed;

    // 정점 인덱스 배선 + 리스너 1회 등록(재빌드마다 중복 방지).
    public void Bind(int _index, Action<int> _onTap)
    {
        this.m_index = _index;
        this.m_onTap = _onTap;

        if (this.tapButton != null)
        {
            this.tapButton.onClick.RemoveAllListeners();
            this.tapButton.onClick.AddListener(this.OnTapped);
        }

        this.Refresh();
    }

    // 상태 표시 갱신. 진행 통지(OnChanged)마다 맵이 전 정점에 호출한다.
    public void Refresh()
    {
        if (this.m_index < 0) return;

        TournamentProgress.TryGetNode(this.m_index, out TournamentNodeDef t_node);
        ETournamentNodeState t_state = TournamentProgress.StateOf(this.m_index);

        if (this.nameText != null && !string.IsNullOrEmpty(t_node.displayName)) this.nameText.text = t_node.displayName;

        this.BindRewardSlots();

        bool t_playable = t_state == ETournamentNodeState.Playable;
        bool t_cleared = t_state == ETournamentNodeState.Cleared;
        bool t_locked = t_state == ETournamentNodeState.Locked;
        bool t_gift = t_state == ETournamentNodeState.RewardPending;

        this.ApplyPortrait(t_node, t_gift);

        // 미수령 정점도 눌러야 한다(진입이 아니라 수령이다) — 그래서 탭 자격은 CanEnter에 선물을 더한 값이다.
        if (this.tapButton != null) this.tapButton.interactable = t_gift || TournamentProgress.CanEnter(this.m_index);
        if (this.lockedMark != null) this.lockedMark.SetActive(t_locked);
        if (this.clearedMark != null) this.clearedMark.SetActive(t_cleared);
        if (this.currentMark != null) this.currentMark.SetActive(t_playable);
        if (this.canvasGroup != null) this.canvasGroup.alpha = t_locked ? this.lockedAlpha : 1f;

        this.ApplyKind(t_node.kind);
        this.ApplyGift(t_gift);
        this.ApplyLockedSilhouette(t_locked);
        this.ApplyLockedTone(t_locked);
    }

    // 초상 한 자리. 저작 초상 → 프리팹 저작값 순으로 떨어지고, 수령 대기면 그 위를 선물이 덮는다.
    // 표식을 얹지 않고 초상 자체를 바꾸는 이유: 원판 위에 무엇을 덧대면 그 상태만 형태가 달라져
    // 다른 상태(잠김·도전 가능·클리어)와 한 벌로 안 읽힌다.
    void ApplyPortrait(TournamentNodeDef _node, bool _gift)
    {
        if (this.avatarImage == null) return;

        // 프리팹 저작값은 처음 볼 때 받아 둔다 — 선물로 갈아끼운 뒤 되돌릴 자리가 여기밖에 없다.
        if (this.m_portrait0 == null) this.m_portrait0 = this.avatarImage.sprite;

        // 등장 연출을 기다리는 중이면 아직 평소 얼굴이다(이미 선물이 서 있다가 튀어나오면 등장이 아니다).
        if (_gift && !this.m_giftArmed && this.giftPortrait != null)
        {
            this.avatarImage.sprite = this.giftPortrait;
            return;
        }

        Sprite t_normal = _node.avatar != null ? _node.avatar : this.m_portrait0;
        if (t_normal != null) this.avatarImage.sprite = t_normal;
    }

    // 종류 표식. 상태보다 먼저 세운다 — 잠김 무채색화가 이 표식까지 함께 덮어야 한 덩어리로 읽힌다.
    void ApplyKind(ETournamentNodeKind _kind)
    {
        if (this.kindBadge == null) return;

        Sprite t_badge = this.BadgeOf(_kind);
        this.kindBadge.gameObject.SetActive(t_badge != null);
        if (t_badge != null) this.kindBadge.sprite = t_badge;
    }

    Sprite BadgeOf(ETournamentNodeKind _kind)
    {
        if (this.kindLooks == null) return null;

        for (int t_i = 0; t_i < this.kindLooks.Length; t_i++)
            if (this.kindLooks[t_i].kind == _kind) return this.kindLooks[t_i].badge;

        return null;
    }

    /// <summary>등장 연출이 올 때까지 평소 얼굴을 유지한다(Refresh보다 나중에 불려야 한다).</summary>
    public void ArmGiftReveal()
    {
        this.m_giftArmed = true;

        this.KillGiftTweens();
        this.Refresh();   // 예약을 세운 뒤 다시 그려야 초상이 평소 얼굴로 돌아간다
    }

    /// <summary>선물 등장(복귀 직후 1회). 대기 흔들림은 Refresh가 따로 소유한다.</summary>
    public void PlayGiftReveal()
    {
        this.m_giftArmed = false;

        // 맵을 이미 떠났다면 예약만 풀고 끝낸다 — 꺼진 오브젝트 위에서 무한 루프 트윈을 돌리지 않는다.
        if (!this.isActiveAndEnabled) return;

        if (TournamentProgress.StateOf(this.m_index) != ETournamentNodeState.RewardPending) return;

        this.KillGiftTweens();
        this.Refresh();   // 예약이 풀렸으니 여기서 초상이 선물로 갈린다

        RectTransform t_rect = this.giftPunchTarget;
        if (t_rect == null) return;   // 연출 대상 미배선이면 그림만 바뀌고 끝난다

        // 원판을 통째로 밀어 등장을 만든다. 0에서 키우지 않는 이유: 정점이 사라졌다 나타나면
        // "새로 생긴 정점"으로 읽힌다 — 여기서 말할 것은 "이 정점에 사건이 났다"다.
        t_rect.localScale = Vector3.one;

        this.m_giftSeq = DOTween.Sequence().SetLink(this.gameObject);
        this.m_giftSeq.Append(t_rect.DOScale(1.18f, 0.18f).SetEase(Ease.OutBack));
        this.m_giftSeq.Append(t_rect.DOScale(1f, 0.12f).SetEase(Ease.OutQuad));
        this.m_giftSeq.Append(t_rect.DOPunchScale(Vector3.one * 0.12f, 0.30f, 8, 0.6f));
        this.m_giftSeq.OnComplete(this.StartGiftIdle);
    }

    /// <summary>해금 직후 한 박 튄다(다음 정점이 열렸다는 신호).</summary>
    public void PlayUnlockPunch()
    {
        var t_rect = (RectTransform)this.transform;
        t_rect.DOKill(true);
        t_rect.DOPunchScale(Vector3.one * 0.15f, 0.35f, 8, 0.6f).SetLink(this.gameObject);
    }

    void OnDisable() => this.KillGiftTweens();

    // 수령 대기의 상시 상태(흔들림). 그림 교체는 ApplyPortrait가 쥐고, 등장 연출은 여기서 돌리지 않는다 —
    // 재진입 때마다 다시 튀면 사건이 아니라 소음이다.
    void ApplyGift(bool _gift)
    {
        if (!_gift)
        {
            this.m_giftArmed = false;
            this.KillGiftTweens();
            return;
        }

        // 등장을 예약해 둔 정점은 그 연출이 시작한다.
        if (this.m_giftArmed) return;

        // 등장 연출이 도는 중이면 그 끝에서 흔들림이 이어진다(여기서 덮으면 등장이 끊긴다).
        if (this.m_giftSeq != null && this.m_giftSeq.IsActive()) return;

        this.StartGiftIdle();
    }

    void StartGiftIdle()
    {
        if (this.giftPunchTarget == null) return;
        if (this.m_giftIdle != null && this.m_giftIdle.IsActive()) return;

        this.giftPunchTarget.localScale = Vector3.one;

        this.m_giftIdle = this.giftPunchTarget
            .DOScale(1.06f, 0.7f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetLink(this.gameObject);
    }

    void KillGiftTweens()
    {
        if (this.m_giftSeq != null && this.m_giftSeq.IsActive()) this.m_giftSeq.Kill();
        if (this.m_giftIdle != null && this.m_giftIdle.IsActive()) this.m_giftIdle.Kill();

        this.m_giftSeq = null;
        this.m_giftIdle = null;

        // 원판을 되돌린다 — 흔들림이 중간에 끊기면 커진 채로 굳는다.
        if (this.giftPunchTarget != null) this.giftPunchTarget.localScale = Vector3.one;
    }

    // 잠긴 정점의 초상을 눌러 실루엣으로 만든다. 무채색화는 채도만 빼서 얼굴이 그대로 읽히는데,
    // 잠긴 정점이 말해야 하는 건 "잠겼다"가 아니라 "누구인지 모른다"다 — 그 자리를 '?'가 대신 맡는다.
    void ApplyLockedSilhouette(bool _locked)
    {
        if (this.avatarImage == null) return;

        if (this.m_avatarColor0 == null) this.m_avatarColor0 = this.avatarImage.color;

        this.avatarImage.color = _locked ? this.lockedSilhouette : this.m_avatarColor0.Value;
    }

    // 잠긴 정점은 딤만으로 안 갈린다 — 비활성 버튼과 같은 축으로 노드 전체의 채도를 뺀다.
    // 자물쇠 묶음은 제외한다(잠김을 말하는 표식이 저 혼자 회색이면 읽히지 않는다).
    // 종류 표식도 제외한다 — 실루엣이 "누구인지"를 감춘 자리에서 이것만이 앞을 말해 주는 정보다.
    void ApplyLockedTone(bool _locked)
    {
        if (_locked)
        {
            if (this.m_toned != null) return;
            this.m_toned = UiGrayscale.Apply(this.gameObject,
                this.lockedMark != null ? this.lockedMark.transform : null,
                this.kindBadge != null ? this.kindBadge.transform : null);
        }
        else
        {
            if (this.m_toned == null) return;
            UiGrayscale.Restore(this.m_toned);
            this.m_toned = null;
        }
    }

    void BindRewardSlots()
    {
        if (this.rewardSlots == null || this.rewardSlots.Length == 0) return;

        TournamentProgress.FillRewards(this.m_index, s_rewardBuffer);

        for (int t_i = 0; t_i < this.rewardSlots.Length; t_i++)
        {
            if (this.rewardSlots[t_i] == null) continue;

            if (t_i < s_rewardBuffer.Count)
                this.rewardSlots[t_i].Bind(s_rewardBuffer[t_i].Icon, s_rewardBuffer[t_i].Gain.Amount);
            else
                this.rewardSlots[t_i].Hide();
        }

        // 저작 문제라 정점마다 찍으면 소음이다 — 세션에 한 번이면 족하다.
        if (s_rewardBuffer.Count > this.rewardSlots.Length && !s_overflowWarned)
        {
            s_overflowWarned = true;
            Debug.LogWarning($"[TournamentNodeView] 정점 보상 {s_rewardBuffer.Count}건이 슬롯 {this.rewardSlots.Length}칸을 초과 — 앞칸만 표시한다.", this);
        }
    }

    // 도전 요청은 맵으로 올린다(정점은 씬 전환을 모른다). 잠김 판정은 맵이 한 번 더 본다.
    void OnTapped()
    {
        if (this.m_index < 0) return;
        this.m_onTap?.Invoke(this.m_index);
    }
}
