using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 카드 한 장을 크게 세워 보여주고 [획득]으로 받게 하는 보상 오버레이.
// 표시와 확인 콜백만 담당하고 지급은 호출자가 한다 — 그래서 출처(튜토리얼 보너스든 그 밖이든)를 알 필요가 없다.
// 씬에 저작하지 않고 Resources에서 세운다(LoadingCover와 같은 규약) — 로비 캔버스에 중첩하면
// 그 프리팹을 저장할 때마다 다른 탭의 저작이 함께 흔들린다.
//
// ⚠ 딤을 눌러 닫히지 않는다. 받아야 넘어가는 자리에 쓰는 물건이라 나가는 문은 [획득] 하나뿐이다.
//
// 안무는 두 박자다 — 화면이 준다(딤 → 제목 → 카드가 내려꽂힌다) → 내가 받는다([획득] → 카드가 도감으로 간다).
// 조각들이 한 덩어리로 페이드인하면 카드가 꽂히기도 전에 답을 다 보여주게 되므로,
// 등장은 조각마다 자기 박자로 들어오고 충격은 꽂히는 한 프레임에 전부 몰아넣는다.
public class CardRewardOverlay : MonoBehaviour
{
    static CardRewardOverlay s_instance;

    /// <summary>보상 화면이 떠 있는가. 로비 쪽 안내가 이 위에 겹치지 않게 볼 때 쓴다.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>닫힌 직후. 이 시점엔 IsOpen이 이미 false다.</summary>
    public static event Action OnAnyClosed;

    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] TMP_Text titleText;
    [SerializeField] Button acquireButton;

    [Tooltip("보여줄 카드. 팩 개봉 낱장과 같은 물건이라 슬램·섬광·림라이트·NEW가 이미 배선돼 있다.")]
    [SerializeField] PackCardView cardView;

    [Header("안무 배선")]
    [Tooltip("카드·제목·버튼을 담은 무대. 카드가 꽂히는 순간 이 판이 통째로 내려앉았다 돌아온다.")]
    [SerializeField] RectTransform stage;
    [Tooltip("카드가 내려꽂히는 대상. 카드 자신(PackCardView)이 아니라 그 부모여야 한다 — 카드 쪽 배율은 펀치가 쥐고 있다.")]
    [SerializeField] RectTransform cardSlot;
    [Tooltip("카드 뒤 광채 묶음. 평소 꺼져 있다가 꽂히는 순간 터진다.")]
    [SerializeField] RectTransform glowRoot;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("카드가 서는 순간 화면이 반응하는 축. dim에 딤 이미지를 물린다(알파는 그대로, 색만 밀린다).")]
    [SerializeField] ScreenDimTint dimTint = new ScreenDimTint();

    [Tooltip("꽂히는 순간 딤이 밝아졌다 돌아오는 시간. 상승 구간은 없다 — 섬광은 즉발이어야 터진 것으로 읽힌다.")]
    [SerializeField] float dimPulseDuration = 0.28f;

    [Header("등장 · 제목")]
    [Tooltip("제목이 위에서 이만큼 내려와 앉는다(px).")]
    [SerializeField] float titleDropDistance = 70f;
    [SerializeField] float titleDelay = 0.04f;
    [SerializeField] float titleDuration = 0.16f;

    [Header("등장 · 카드")]
    [Tooltip("딤이 깔린 뒤 카드가 출발하기까지의 뜸. 이 사이엔 제목만 있고 카드는 화면에 없다.")]
    [SerializeField] float cardDropDelay = 0.2f;
    [Tooltip("카드가 이만큼 위에서 출발한다(px).")]
    [SerializeField] float cardDropDistance = 430f;
    [Tooltip("출발 배율(제자리 대비). 크게 다가와 있어야 꽂히는 순간이 사건이 된다.")]
    [SerializeField] float cardDropScale = 1.5f;
    [Tooltip("내려꽂히는 시간. 길면 '떨어진다'가 되고 짧아야 '꽂힌다'가 된다.")]
    [SerializeField] float cardDropDuration = 0.1f;

    [Header("등장 · 꽂히는 순간")]
    [Tooltip("무대가 이만큼 내려앉았다 돌아온다(px). 카드가 아니라 화면이 반응하는 축이다.")]
    [SerializeField] float stageKick = 20f;
    [SerializeField] float stageKickDuration = 0.14f;
    [Tooltip("광채가 이 배율에서 출발해")]
    [SerializeField] float glowBurstFrom = 0.65f;
    [Tooltip("이만큼 튀어올랐다가 제자리로 가라앉는다.")]
    [SerializeField] float glowBurstTo = 1.06f;
    [SerializeField] float glowBurstUpDuration = 0.1f;
    [SerializeField] float glowBurstSettleDuration = 0.28f;
    [Tooltip("가라앉은 뒤 남는 호흡의 진폭(0이면 멈춘 채 있는다).")]
    [SerializeField] float glowBreathAmount = 0.03f;
    [SerializeField] float glowBreathPeriod = 1.6f;

    [Header("등장 · [획득]")]
    [Tooltip("카드가 꽂힌 뒤 버튼이 올라오기까지의 뜸. 이 구간이 카드를 읽는 시간이다 — " +
             "0이면 손이 카드보다 빨라져 무엇을 받았는지 못 보고 넘어간다.")]
    [SerializeField] float claimDelay = 0.6f;
    [Tooltip("버튼이 이만큼 아래에서 올라와 앉는다(px).")]
    [SerializeField] float claimRiseDistance = 90f;
    [SerializeField] float claimRiseDuration = 0.16f;
    [Tooltip("앉은 뒤 남는 호흡의 진폭(0이면 멈춘 채 있는다).")]
    [SerializeField] float claimBreathAmount = 0.03f;
    [SerializeField] float claimBreathPeriod = 1.2f;

    [Header("넘겨주기")]
    [Tooltip("[획득] 직후 카드가 이만큼 부풀었다가")]
    [SerializeField] float handoffPop = 1.08f;
    [SerializeField] float handoffPopDuration = 0.06f;
    [Tooltip("비행 카드 크기까지 줄며 사라진다. 두 카드가 같은 자리에서 겹쳐야 '그 카드가 날아갔다'로 읽힌다.")]
    [SerializeField] float handoffShrink = 0.34f;
    [SerializeField] float handoffShrinkDuration = 0.14f;
    [Tooltip("제목·버튼·광채가 걷히는 시간. 카드만 남겨 두고 곁가지를 먼저 치운다.")]
    [SerializeField] float handoffPeelDuration = 0.08f;

    // 진행 중 안무(등장 또는 넘겨주기). 획득·닫기가 도중에 와도 저작 상태로 되돌린 뒤 이어가야 한다.
    Sequence m_choreo;

    // 무한 반복이라 시퀀스에 담기지 못하는 여운들. 걷어낼 손잡이를 따로 쥔다.
    Tween m_glowBreath;
    Tween m_claimBreath;

    // 획득 콜백. 한 번 쓰면 비워 연타를 막는다. 지급이 실패하든 말든 화면은 닫힌다 —
    // 받아야 넘어가는 자리라 여기서 가두면 탈출로가 없다.
    Action m_onAcquire;

    // 조각들의 제자리. 프리팹 저작값이 곧 제자리라 최초 1회만 캡처한다.
    Vector2 m_stageHome;
    Vector2 m_cardHome;
    Vector3 m_cardHomeScale = Vector3.one;
    Vector2 m_titleHome;
    Vector2 m_claimHome;
    Vector3 m_claimHomeScale = Vector3.one;
    bool m_homeCaptured;

    // 조각별 알파 손잡이. 프리팹에 없으면 붙여 준다 — 배선 여부와 무관하게 안무가 성립해야 한다.
    CanvasGroup m_cardGroup;
    CanvasGroup m_titleGroup;
    CanvasGroup m_claimGroup;
    CanvasGroup m_glowGroup;

    /// <summary>카드가 서 있는 자리. 획득 뒤 이어지는 비행이 여기서 출발해야
    /// "방금 본 그 카드가 도감으로 갔다"가 한 줄로 이어진다.</summary>
    public RectTransform CardAnchor => this.cardSlot;

    /// <summary>보상 오버레이를 얻는다. 씬에 저작해 두지 않고 Resources에서 세운다 —
    /// 로비 캔버스에 중첩하면 그 프리팹을 저장할 때마다 다른 탭의 저작이 함께 흔들린다(LoadingCover와 같은 이유).
    /// 평소 꺼져 있는 노드라 이미 선 것을 찾을 때는 비활성까지 뒤진다.</summary>
    public static bool TryGet(out CardRewardOverlay _overlay)
    {
        if (s_instance == null)
            s_instance = FindFirstObjectByType<CardRewardOverlay>(FindObjectsInactive.Include);

        if (s_instance == null)
        {
            var t_prefab = RuntimeUiPrefabs.Get(ERuntimeUiPrefab.CardRewardOverlay);
            if (t_prefab == null)
            {
                Debug.LogWarning("[CardRewardOverlay] Boot 카탈로그에서 보상 프리팹을 찾지 못했습니다.");
            }
            else
            {
                var t_go = Instantiate(t_prefab);
                s_instance = t_go.GetComponent<CardRewardOverlay>();

                // 컴포넌트가 없으면 세운 것이 화면을 덮은 채 남는다 — 부를 때마다 한 장씩 쌓이므로 즉시 걷는다.
                if (s_instance == null)
                {
                    Debug.LogWarning("[CardRewardOverlay] 카탈로그 프리팹에 CardRewardOverlay가 없습니다(프리팹 배선 확인).");
                    Destroy(t_go);
                }
            }
        }

        _overlay = s_instance;
        return _overlay != null;
    }

    /// <summary>카드 한 장을 띄운다. _onAcquire는 [획득]을 누른 <b>즉시</b> 불린다 —
    /// 그때 지급하고, 이어지는 획득 연출도 그쪽이 튼다(화면은 그 연출과 겹쳐 걷힌다).</summary>
    public void Show(string _title, CardData _card, Action _onAcquire)
    {
        this.m_onAcquire = _onAcquire;

        // 직전 표시의 안무를 걷는다 — 시퀀스에 중첩된 트윈은 대상의 DOKill이 잡지 못해 새 안무와 같은 노드를 함께 민다.
        this.KillChoreo();

        if (this.titleText != null) this.titleText.text = _title;

        // 보상으로 주는 카드는 언제나 새 카드로 세운다 — 중복 표식(탈채도·환급 칩)이 설 자리가 아니다.
        if (this.cardView != null) this.cardView.Bind(new DrawnCard(_card, true, 0L));

        if (this.acquireButton != null)
        {
            this.acquireButton.onClick.RemoveAllListeners();   // 재표시마다 중복 등록 방지
            this.acquireButton.onClick.AddListener(this.OnAcquireClicked);
        }

        IsOpen = true;
        this.SetVisible(true);
        this.dimTint.Capture();
        this.CaptureHome();

        // 등장이 도는 동안은 손을 막는다 — 카드가 다 서기 전에 눌러 닫히면 무엇을 받았는지 못 본다.
        this.SetInputEnabled(false);

        this.m_choreo = this.BuildIntro();
        this.m_choreo.Play();
    }

    public void Hide()
    {
        this.m_onAcquire = null;
        this.KillChoreo();
        this.dimTint.Reset();

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        this.SetVisible(false);
        this.ResetChoreography();

        if (t_wasOpen) OnAnyClosed?.Invoke();
    }

    // 잠금은 등장 안무가 푼다. Show를 거치지 않고 뜨는 경로(부모가 다시 켜짐)에서는 그 안무가 없어
    // [획득]이 잠긴 모달로 남으므로, 켜질 때 일단 열어 둔다(Show는 이 뒤에 다시 잠근다).
    void OnEnable()
    {
        this.SetInputEnabled(true);
    }

    // 오버레이는 자기 자신이 토글 대상이라 OnDisable이 정상 동작한다 — 잘린 퇴장 마무리를 여기서 위임한다.
    void OnDisable()
    {
        this.transition.HandleDisabled(this.ResolveTarget());
        this.KillChoreo();
        this.dimTint.Reset();
        this.ResetChoreography();

        // 꺼진 화면은 떠 있는 것이 아니다. Hide를 거치지 않고 꺼지는 경로(부모 비활성·씬 언로드)에서
        // 이 플래그가 남으면 "로비 표면이 보이는가" 판정이 영영 false가 되어 뒤의 안내가 서지 못한다.
        IsOpen = false;
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;

        // 열린 채 씬이 바뀌면 플래그가 남아 다음 씬의 안내가 영영 억제된다.
        IsOpen = false;
    }

    void OnAcquireClicked()
    {
        // 콜백을 먼저 비워 연타로 두 번 지급되는 경로를 막는다(호출자 가드와 이중 방어).
        var t_callback = this.m_onAcquire;
        this.m_onAcquire = null;
        if (t_callback == null) return;

        this.SetInputEnabled(false);

        // 지급은 이 프레임에 끝낸다. 뒤에 오는 것은 표시뿐이라, 화면이 도중에 꺼져도 카드는 이미 들어가 있다.
        // IsOpen도 함께 내린다 — 지급이 트는 획득 연출의 종료 신호를 기다리는 쪽이
        // "보상 화면이 떠 있는 동안 온 신호"로 오인해 흘려보낸다(OutgameTutorialBridge.OnCardGainFinished).
        bool t_wasOpen = IsOpen;
        IsOpen = false;

        this.KillChoreo();
        t_callback.Invoke();

        this.m_choreo = this.BuildHandoff(t_wasOpen);
        this.m_choreo.Play();
    }

    // 등장 안무. 딤만 먼저 깔리고 제목·카드·버튼이 각자의 박자로 들어온다.
    Sequence BuildIntro()
    {
        this.PrimeIntro();

        float t_slam = this.cardDropDelay + this.cardDropDuration;

        var t_seq = DOTween.Sequence().SetLink(this.gameObject);

        if (this.titleText != null)
        {
            var t_title = this.titleText.rectTransform;
            t_seq.Insert(this.titleDelay, t_title.DOAnchorPosY(this.m_titleHome.y, this.titleDuration).SetEase(Ease.OutCubic));
            t_seq.Insert(this.titleDelay, this.m_titleGroup.DOFade(1f, this.titleDuration));
        }

        if (this.cardSlot != null)
        {
            t_seq.InsertCallback(this.cardDropDelay, () => this.m_cardGroup.alpha = 1f);
            t_seq.Insert(this.cardDropDelay, this.cardSlot.DOAnchorPosY(this.m_cardHome.y, this.cardDropDuration).SetEase(Ease.InQuad));
            t_seq.Insert(this.cardDropDelay, this.cardSlot.DOScale(this.m_cardHomeScale, this.cardDropDuration).SetEase(Ease.InQuad));
        }

        // 충격은 이 한 프레임에 전부 들어간다. 시간축에 흩으면 약한 사건 넷이 되고, 겹치면 하나의 큰 사건이 된다.
        t_seq.InsertCallback(t_slam, this.PlaySlam);

        // 밝기는 트윈이 시작하는 프레임에 1로 튄 뒤 잦아든다(From) — 올라가는 구간을 두면 섬광이 아니라 점등이 된다.
        // setImmediately는 반드시 false다 — true면 조립하는 순간(t=0) 딤이 밝아져 등장 내내 밝은 채로 있는다.
        // ScreenDimTint.TweenLevel을 쓰지 않는 이유는 그쪽 반환형이 Tween이라 From(float, bool)이 붙지 않기 때문이다.
        t_seq.Insert(t_slam, DOTween.To(() => this.dimTint.Level, _v => this.dimTint.Level = _v, 0f, this.dimPulseDuration)
                                    .From(1f, false).SetEase(Ease.OutQuad));

        float t_claimAt = t_slam + this.claimDelay;
        if (this.acquireButton != null)
        {
            var t_claim = (RectTransform)this.acquireButton.transform;
            t_seq.Insert(t_claimAt, t_claim.DOAnchorPosY(this.m_claimHome.y, this.claimRiseDuration).SetEase(Ease.OutBack));
            t_seq.Insert(t_claimAt, this.m_claimGroup.DOFade(1f, this.claimRiseDuration));
        }

        // 손은 버튼이 다 앉은 뒤에 돌려준다. 잠금을 푸는 곳이 여기뿐이라, 빠지면 [획득]이 영영 잠긴 모달이 된다.
        t_seq.InsertCallback(t_claimAt + this.claimRiseDuration, () =>
        {
            this.SetInputEnabled(true);
            this.PlayClaimBreath();
        });

        t_seq.OnComplete(() => this.m_choreo = null);
        return t_seq;
    }

    // 등장 직전 상태. 딤 말고는 아무것도 화면에 없어야 한다.
    void PrimeIntro()
    {
        this.KillIdle();   // 앞 표시의 여운이 살아 있으면 이 아래 세워 둔 출발 상태를 곧장 덮는다.

        if (this.stage != null) this.stage.anchoredPosition = this.m_stageHome;

        if (this.titleText != null)
        {
            var t_title = this.titleText.rectTransform;
            t_title.anchoredPosition = this.m_titleHome + new Vector2(0f, this.titleDropDistance);
            this.m_titleGroup.alpha = 0f;
        }

        if (this.cardSlot != null)
        {
            this.cardSlot.anchoredPosition = this.m_cardHome + new Vector2(0f, this.cardDropDistance);
            this.cardSlot.localScale = this.m_cardHomeScale * this.cardDropScale;
            this.m_cardGroup.alpha = 0f;
        }

        if (this.acquireButton != null)
        {
            var t_claim = (RectTransform)this.acquireButton.transform;
            t_claim.anchoredPosition = this.m_claimHome - new Vector2(0f, this.claimRiseDistance);
            t_claim.localScale = this.m_claimHomeScale;
            this.m_claimGroup.alpha = 0f;
        }

        if (this.glowRoot != null)
        {
            this.glowRoot.localScale = Vector3.one * this.glowBurstFrom;
            this.m_glowGroup.alpha = 0f;
        }
    }

    // 카드가 꽂히는 한 프레임. 카드 자신의 타격(펀치·섬광·광택·NEW·림)과 화면의 반응을 함께 터뜨린다.
    void PlaySlam()
    {
        this.PlayCardReveal();
        this.PlayStageKick();
        this.PlayGlowBurst();
    }

    // 한 장이 드러나는 순간의 강조. 결과 격자용 ApplyResultContrast는 부르지 않는다 —
    // 그쪽은 "놓여 있는 상태"라 이 자리의 등장 안무를 덮는다(PackRevealView와 같은 호출).
    void PlayCardReveal()
    {
        if (this.cardView != null) this.cardView.PlayRevealAccent();
    }

    void PlayStageKick()
    {
        if (this.stage == null || this.stageKick <= 0f) return;

        this.stage.anchoredPosition = this.m_stageHome - new Vector2(0f, this.stageKick);
        this.stage.DOAnchorPosY(this.m_stageHome.y, this.stageKickDuration).SetEase(Ease.OutQuint).SetLink(this.gameObject);
    }

    void PlayGlowBurst()
    {
        if (this.glowRoot == null) return;

        this.glowRoot.DOKill();
        this.m_glowGroup.alpha = 1f;
        this.glowRoot.localScale = Vector3.one * this.glowBurstFrom;

        DOTween.Sequence().SetLink(this.glowRoot.gameObject)
               .Append(this.glowRoot.DOScale(this.glowBurstTo, this.glowBurstUpDuration).SetEase(Ease.OutQuint))
               .Append(this.glowRoot.DOScale(1f, this.glowBurstSettleDuration).SetEase(Ease.OutSine));

        this.PlayGlowBreath(this.glowBurstUpDuration + this.glowBurstSettleDuration);
    }

    // 여운은 무한 반복이라 시퀀스에 담기지 못한다 — 가라앉는 시각에 맞춰 따로 띄운다.
    void PlayGlowBreath(float _delay)
    {
        this.m_glowBreath?.Kill();
        if (this.glowBreathAmount <= 0f || this.glowBreathPeriod <= 0f) return;

        this.m_glowBreath = this.glowRoot.DOScale(1f + this.glowBreathAmount, this.glowBreathPeriod)
                                         .SetEase(Ease.InOutSine)
                                         .SetLoops(-1, LoopType.Yoyo)
                                         .SetDelay(_delay)
                                         .SetLink(this.glowRoot.gameObject);
    }

    void PlayClaimBreath()
    {
        this.m_claimBreath?.Kill();
        if (this.acquireButton == null || this.claimBreathAmount <= 0f || this.claimBreathPeriod <= 0f) return;

        this.m_claimBreath = this.acquireButton.transform
                                 .DOScale(this.m_claimHomeScale * (1f + this.claimBreathAmount), this.claimBreathPeriod)
                                 .SetEase(Ease.InOutSine)
                                 .SetLoops(-1, LoopType.Yoyo)
                                 .SetLink(this.acquireButton.gameObject);
    }

    // 넘겨주기. 화면을 먼저 지우고 다른 카드를 날리면 "여기서 저기로 갔다"가 끊긴다 —
    // 곁가지를 걷고 카드만 남겨, 비행 카드가 같은 자리에서 튀어나오는 동안 이쪽이 그 크기로 줄며 사라진다.
    Sequence BuildHandoff(bool _wasOpen)
    {
        this.KillIdle();

        var t_seq = DOTween.Sequence().SetLink(this.gameObject);

        if (this.m_titleGroup != null) t_seq.Insert(0f, this.m_titleGroup.DOFade(0f, this.handoffPeelDuration));
        if (this.m_claimGroup != null) t_seq.Insert(0f, this.m_claimGroup.DOFade(0f, this.handoffPeelDuration));
        if (this.m_glowGroup != null) t_seq.Insert(0f, this.m_glowGroup.DOFade(0f, this.handoffPeelDuration));

        if (this.cardSlot != null)
        {
            t_seq.Insert(0f, this.cardSlot.DOScale(this.m_cardHomeScale * this.handoffPop, this.handoffPopDuration)
                                          .SetEase(Ease.OutQuint));
            t_seq.Insert(this.handoffPopDuration,
                         this.cardSlot.DOScale(this.m_cardHomeScale * this.handoffShrink, this.handoffShrinkDuration)
                                      .SetEase(Ease.InQuad));
        }

        // 딤은 카드가 줄기 시작할 때 함께 걷는다. 이 캔버스가 로비 위에 있어, 딤이 남아 있으면
        // 그 아래에서 출발한 비행 카드가 가려진다 — 넘겨주는 그림이 통째로 사라진다.
        //
        // 닫힘 통지도 여기서 낸다. 시퀀스의 OnComplete에 맡기면 퇴장이 끝나 오브젝트가 꺼지는 순간
        // OnDisable이 이 시퀀스를 걷어내(KillChoreo) 통지가 영영 오지 않을 수 있다 — 둘의 시각이 한 프레임 차다.
        t_seq.InsertCallback(this.handoffPopDuration, () =>
        {
            this.SetVisible(false);
            if (_wasOpen) OnAnyClosed?.Invoke();
        });

        t_seq.OnComplete(() => this.m_choreo = null);

        return t_seq;
    }

    // 조각들의 제자리와 알파 손잡이를 확보한다. 프리팹 저작값이 곧 제자리라 최초 1회만.
    void CaptureHome()
    {
        if (this.m_homeCaptured) return;
        this.m_homeCaptured = true;

        if (this.stage != null) this.m_stageHome = this.stage.anchoredPosition;

        if (this.cardSlot != null)
        {
            this.m_cardHome = this.cardSlot.anchoredPosition;
            this.m_cardHomeScale = this.cardSlot.localScale;
            this.m_cardGroup = GroupOf(this.cardSlot.gameObject);
        }

        if (this.titleText != null)
        {
            this.m_titleHome = this.titleText.rectTransform.anchoredPosition;
            this.m_titleGroup = GroupOf(this.titleText.gameObject);
        }

        if (this.acquireButton != null)
        {
            var t_claim = (RectTransform)this.acquireButton.transform;
            this.m_claimHome = t_claim.anchoredPosition;
            this.m_claimHomeScale = t_claim.localScale;
            this.m_claimGroup = GroupOf(this.acquireButton.gameObject);
        }

        if (this.glowRoot != null) this.m_glowGroup = GroupOf(this.glowRoot.gameObject);
    }

    // 다음 표시가 중간값(어긋난 자리·줄어든 배율·반투명)에서 시작하지 않게 원복.
    void ResetChoreography()
    {
        if (!this.m_homeCaptured) return;

        this.KillIdle();

        if (this.stage != null)
        {
            this.stage.DOKill();
            this.stage.anchoredPosition = this.m_stageHome;
        }

        if (this.cardSlot != null)
        {
            this.cardSlot.DOKill();
            this.cardSlot.anchoredPosition = this.m_cardHome;
            this.cardSlot.localScale = this.m_cardHomeScale;
            if (this.m_cardGroup != null) this.m_cardGroup.alpha = 1f;
        }

        if (this.titleText != null)
        {
            this.titleText.rectTransform.DOKill();
            this.titleText.rectTransform.anchoredPosition = this.m_titleHome;
            if (this.m_titleGroup != null) this.m_titleGroup.alpha = 1f;
        }

        if (this.acquireButton != null)
        {
            var t_claim = (RectTransform)this.acquireButton.transform;
            t_claim.DOKill();
            t_claim.anchoredPosition = this.m_claimHome;
            t_claim.localScale = this.m_claimHomeScale;
            if (this.m_claimGroup != null) this.m_claimGroup.alpha = 1f;
        }

        if (this.glowRoot != null)
        {
            this.glowRoot.DOKill();
            this.glowRoot.localScale = Vector3.one;

            // 광채는 꽂히는 순간에만 존재한다 — 알파를 되돌리면 다음 표시가 이미 켜진 광채로 시작한다.
            if (this.m_glowGroup != null) this.m_glowGroup.alpha = 0f;
        }
    }

    void KillChoreo()
    {
        if (this.m_choreo != null && this.m_choreo.IsActive()) this.m_choreo.Kill();
        this.m_choreo = null;
    }

    void KillIdle()
    {
        this.m_glowBreath?.Kill();
        this.m_glowBreath = null;
        this.m_claimBreath?.Kill();
        this.m_claimBreath = null;
    }

    void SetInputEnabled(bool _enabled)
    {
        if (this.acquireButton != null) this.acquireButton.interactable = _enabled;
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;

    static CanvasGroup GroupOf(GameObject _go)
    {
        var t_group = _go.GetComponent<CanvasGroup>();
        return t_group != null ? t_group : _go.AddComponent<CanvasGroup>();
    }
}
