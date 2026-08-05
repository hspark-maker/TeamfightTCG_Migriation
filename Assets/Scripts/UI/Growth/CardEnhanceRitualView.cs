using System;
using Coffee.UIEffects;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 카드 강화 한 번의 연출(CardDetailOverlay 루트에 부착).
// 카드가 움츠러들며 힘을 모으다(고조) 한순간 멎고(정적) 터져 나온다(공개) — 압축이 곧 기대이고, 폭발이 곧 보상이다.
// 실패는 그 압축이 터지지 못하고 힘없이 풀리는 모습으로 갈린다.
//
// 판정은 하지 않는다 — 강화는 CardGrowthManager.TryEnhance가 이미 원자적으로 끝낸 거래이고,
// 여기서는 그 결과를 보여줄 뿐이다(PackRevealView와 같은 결).
//
// ⚠ 값 반영 시점의 진실원은 호출부다. 연출 중에 Lv·HP가 먼저 튀면 공개할 것이 없으므로,
//   호출부가 갱신을 유예했다가 _onReveal에서 한 번에 반영한다.
//
// ⚠ 파티클을 쓰지 않는다 — 이 캔버스가 Overlay라 ParticleSystem이 렌더되지 않는다(PackCardView 주석 참고).
//   표면 연출 수단은 Image + DOTween + UIEffect뿐이다.
public class CardEnhanceRitualView : MonoBehaviour
{
    [Header("무대 (미배선이면 연출 없이 콜백만 즉시 흘린다)")]
    [Tooltip("압축·진동·낙하를 받는 노드(CardSlot). 카드 그림의 부모여야 카드가 통째로 움직인다.\n" +
             "⚠ LayoutGroup에 구동되지 않는 노드여야 한다 — 매 프레임 좌표가 되돌려지면 진동이 보이지 않는다.")]
    [SerializeField] RectTransform cardStage;

    [Tooltip("화면을 덮은 딤. 알파는 건드리지 않고 색만 민다 — 알파를 내리면 구도가 바뀐다(PackScreenFlash와 같은 규칙).")]
    [SerializeField] Graphic dim;

    [Header("걷히는 패널 (선택)")]
    [Tooltip("연출 동안 사라졌다 돌아올 패널들(DetailPanel·BottomBar).\n" +
             "⚠ SetActive로 끄지 않는다 — 루트 VerticalLayoutGroup에서 CardArea가 남는 높이를 전부 먹어 카드 크기가 튄다.")]
    [SerializeField] CanvasGroup[] retractGroups;

    [Header("카드 위 연출 (선택 — 미배선 축은 조용히 건너뛴다)")]
    [Tooltip("카드 뒤에서 조여드는 빛. 에셋 후보: Sprites/CardPack/Glow_Radial.")]
    [SerializeField] Graphic backGlow;
    [Tooltip("공개 순간의 섬광. 에셋 후보: Sprites/CardPack/Shine_Radial.")]
    [SerializeField] Graphic flash;

    [Tooltip("카드 전체를 덮는 광채(Transition-Shiny)이자 실패의 탈채도(Tone-Grayscale).\n" +
             "⚠ 배선 전제: 카드 테두리(CardUIView>Background>Frame)에 UIEffect를 얹고, " +
             "Portrait에는 UIEffectReplica를 걸어 useTargetTransform으로 이 rect를 공유해야 한다 — " +
             "그래야 띠가 조각나지 않고 카드를 한 번에 가로지른다(PackCard.prefab과 같은 배선).")]
    [SerializeField] UIEffect cardEffect;
    [SerializeField] Color    gleamColor  = new Color(1f, 0.95f, 0.75f, 1f);
    [Tooltip("고조 동안 카드를 훑는 횟수. 등속이 아니라 점점 빨라진다.")]
    [SerializeField] int      gleamSweeps = 6;

    [Header("결과 문구 (선택)")]
    [SerializeField] TMP_Text resultText;
    [SerializeField] Color    successColor   = new Color(0.45f, 1f, 0.55f, 1f);
    [SerializeField] Color    failColor      = new Color(1f, 0.45f, 0.4f, 1f);
    [SerializeField] string   successMessage = "강화 성공!";
    [SerializeField] string   failMessage    = "강화 실패";

    [Header("성공 화면 덮개 (선택)")]
    [Tooltip("성공에만 쏜다. 실패까지 화면이 반응하면 성공의 대비가 사라진다.")]
    [SerializeField] bool             useScreenFlash = true;
    [SerializeField] ScreenFlashCover successFlash   = new ScreenFlashCover { rise = 0.05f, hold = 0.02f, fall = 0.3f, peak = 0.55f };

    [Header("박자")]
    [SerializeField] float enterDuration   = 0.15f;
    [SerializeField] float buildUpDuration = 1.2f;
    [Tooltip("정점에서 모든 것이 멈추는 시간. 이 정적이 없으면 고조가 결과로 흘러들어 판정 순간이 뭉개진다.")]
    [SerializeField] float holdDuration    = 0.25f;
    [SerializeField] float resultHold      = 0.7f;
    [SerializeField] float returnDuration  = 0.35f;

    [Header("세기 — 카드는 조여들었다가 터진다")]
    [Tooltip("진입에서 당겨지는 배율. 1보다 작아야 '움츠린다'로 읽힌다.")]
    [SerializeField] float enterScale    = 0.92f;
    [Tooltip("고조 끝의 압축 배율. 여기까지 쉬지 않고 조여든다.")]
    [SerializeField] float compressScale = 0.78f;
    [Tooltip("정적 구간의 최대 압축. 이 한 뼘이 폭발의 반동을 만든다.")]
    [SerializeField] float holdScale     = 0.74f;
    [Tooltip("성공 순간 튀어오르는 배율. 즉시 이 크기가 되었다가 제자리로 회수된다.")]
    [SerializeField] float burstScale    = 1.35f;
    [Tooltip("폭발이 제자리로 회수되는 시간.")]
    [SerializeField] float burstSettle   = 0.4f;

    [Tooltip("고조 마지막 구간의 진동 폭(px). 앞 구간은 이 값의 1/4, 1/2로 커진다.")]
    [SerializeField] float shakeStrength    = 9f;
    [SerializeField] float failDrop         = 20f;
    [SerializeField] float failShake        = 6f;
    [Range(0f, 1f)]
    [SerializeField] float failDesaturation = 0.8f;
    [Range(0f, 1f)]
    [SerializeField] float glowPeakAlpha    = 0.9f;
    [SerializeField] float glowStartScale   = 1.6f;
    [SerializeField] float flashPeakAlpha   = 0.85f;

    [Header("딤 색")]
    [SerializeField] Color dimDarkColor   = new Color(0.02f, 0.02f, 0.05f, 1f);
    [SerializeField] Color dimBrightColor = new Color(0.30f, 0.28f, 0.45f, 1f);

    const float FlashRise = 0.06f;
    const float FlashFall = 0.25f;

    // 실패의 낙하(0.25) + 착지 흔들림(0.28)이 끝나기 전에 복귀가 시작되면 두 트윈이 같은 좌표를 다툰다.
    const float FailSettle = 0.55f;

    // 정적 구간에 광채를 카드 한복판에 세워 둔다. 0이나 1이면 띠가 카드 밖에 있어 "멈춘 빛"이 보이지 않는다.
    const float GleamParked = 0.5f;

    Sequence m_seq;
    Action   m_onReveal;
    Action   m_onFinished;

    // cardStage·딤의 authoring 상태. 연출 중간값을 기준으로 잡으면 반복할수록 자리가 밀린다 → 1회만 캡처한다.
    Vector2 m_baseAnchored;
    Color   m_baseDim;
    bool    m_baseCaptured;

    // 지금 딤이 어느 쪽으로 얼마나 밀려 있는지(-1 어둠 ~ +1 빛). 트윈이 이어 붙을 때의 출발점이다.
    float m_dimLevel;

    /// <summary>연출이 진행 중인가. 호출부는 이 동안 강화 재입력·카드 넘기기·닫기를 막는다.</summary>
    public bool IsPlaying => this.m_seq != null && this.m_seq.IsActive();

    /// <summary>강화 결과를 한 번 보여준다. _outcome은 Success/Failed만 온다(나머지는 결제 전 차단이라 보여줄 것이 없다).
    /// _onReveal은 공개 섬광 시점 — 호출부가 여기서 값을 화면에 반영한다.
    /// _onFinished는 복귀 완료 — 호출부가 여기서 조작을 되살린다.
    /// 두 콜백은 스킵·중단·재진입 어느 경로로든 각각 정확히 한 번 온다.</summary>
    public void Play(EEnhanceOutcome _outcome, int _hpGain, Action _onReveal, Action _onFinished)
    {
        // 재진입은 호출부가 막지만 여기서도 닫는다 — 두 연출이 같은 노드를 두고 싸우면 카드가 굳는다.
        // 다만 콜백은 삼키지 않는다. 삼키면 호출부의 갱신 유예가 영영 풀리지 않아 버튼이 죽는다.
        if (IsPlaying)
        {
            _onReveal?.Invoke();
            _onFinished?.Invoke();
            return;
        }

        this.m_onReveal   = _onReveal;
        this.m_onFinished = _onFinished;

        if (this.cardStage == null)
        {
            // 무대가 없으면 보여줄 것이 없다. 값 반영까지 막지는 않는다(배선 실패가 소프트락이 되지 않게).
            FireReveal();
            FireFinished();
            return;
        }

        CaptureBase();
        RestoreVisual();

        bool t_success = _outcome == EEnhanceOutcome.Success;

        // 저작값이 0이나 음수여도 구간이 서로를 넘지 않게 여기서 한 번 정리한다 — 아래 시간축은 이 값들만 쓴다.
        float t_enterDur  = Mathf.Max(0.01f, this.enterDuration);
        float t_riseDur   = Mathf.Max(0.06f, this.buildUpDuration);
        float t_stillDur  = Mathf.Max(0.02f, this.holdDuration);
        float t_resultDur = Mathf.Max(t_success ? 0.1f : FailSettle, this.resultHold);
        float t_backDur   = Mathf.Max(0.05f, this.returnDuration);

        float t_rise   = t_enterDur;
        float t_still  = t_rise + t_riseDur;
        float t_reveal = t_still + t_stillDur;
        float t_result = t_reveal + FlashRise;
        float t_return = t_result + t_resultDur;
        float t_end    = t_return + t_backDur;

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);

        BuildEnter(t_seq, t_enterDur);
        BuildRise(t_seq, t_rise, t_riseDur);
        BuildStill(t_seq, t_still, t_stillDur);
        BuildReveal(t_seq, t_reveal);

        if (t_success) BuildSuccess(t_seq, t_result);
        else           BuildFail(t_seq, t_result);

        BuildResultText(t_seq, t_result, t_success, _hpGain);
        BuildReturn(t_seq, t_return, t_backDur, t_end);

        // 정상 종료든 스킵이든 중단이든 여기로 온다 — 콜백 유실과 굳은 화면을 동시에 막는 안전망이다.
        t_seq.OnKill(() =>
        {
            this.m_seq = null;
            FireReveal();
            RestoreVisual();
            FireFinished();
        });

        this.m_seq = t_seq;
        t_seq.Play();   // 재생 책임을 코드에 남긴다(PopupTransition과 같은 결).
    }

    /// <summary>남은 구간을 최종 상태로 끌어당긴다. 콜백은 순서대로 그대로 실행된다.</summary>
    public void RequestSkip()
    {
        if (IsPlaying) this.m_seq.Complete(true);
    }

    /// <summary>연출을 잘라내고 화면만 원복한다(카드 전환·닫힘 경로). 콜백은 OnKill이 마저 흘린다 —
    /// 안 그러면 호출부의 값 갱신 유예가 영영 풀리지 않는다.</summary>
    public void CancelImmediate()
    {
        if (this.m_seq == null)
        {
            RestoreVisual();
            return;
        }

        this.m_seq.Kill();   // OnKill이 RestoreVisual까지 돌린다.
        this.m_seq = null;
    }

    void OnDisable()
    {
        // 잘린 채 굳은 압축·회색·오프셋이 다음 열기로 새지 않게.
        CancelImmediate();
    }

    // ── 구간 ─────────────────────────────────────────────

    // 패널이 걷히고 카드가 한 번 움츠러든다.
    void BuildEnter(Sequence _seq, float _dur)
    {
        _seq.InsertCallback(0f, () => SetRetractBlocking(false));

        if (this.retractGroups != null)
            foreach (CanvasGroup t_g in this.retractGroups)
            {
                if (t_g == null) continue;
                _seq.Insert(0f, t_g.DOFade(0f, _dur));
            }

        _seq.Insert(0f, this.cardStage.DOScale(this.enterScale, _dur).SetEase(Ease.OutCubic));
        _seq.Insert(0f, DimTween(-1f, _dur));
    }

    // 고조. 카드는 쉬지 않고 조여들고, 진동은 세 마디로 폭과 잔떨림을 함께 키운다 —
    // 한 마디로 이으면 세기가 변하는 것이 안 읽힌다.
    void BuildRise(Sequence _seq, float _at, float _dur)
    {
        _seq.Insert(_at, this.cardStage.DOScale(this.compressScale, _dur).SetEase(Ease.InQuad));

        float t_seg = _dur / 3f;
        for (int t_i = 0; t_i < 3; t_i++)
        {
            float t_strength = this.shakeStrength * (t_i == 0 ? 0.25f : t_i == 1 ? 0.5f : 1f);
            int   t_vibrato  = 8 + t_i * 7;

            _seq.Insert(_at + t_seg * t_i,
                        this.cardStage.DOShakeAnchorPos(t_seg, t_strength, t_vibrato, 90f, false, false));
        }

        if (this.backGlow != null)
        {
            _seq.Insert(_at, this.backGlow.DOFade(this.glowPeakAlpha, _dur).SetEase(Ease.InQuad));
            _seq.Insert(_at, this.backGlow.rectTransform.DOScale(1f, _dur).SetEase(Ease.OutQuad));
        }

        // 광채 주기를 코드가 민다. phase를 가속시켜 굴리면 스윕 하나하나가 점점 짧아진다(등속이면 고조가 아니라 배경이 된다).
        if (this.cardEffect != null)
        {
            _seq.InsertCallback(_at, () => this.cardEffect.transitionColor = this.gleamColor);

            float t_phase = 0f;
            _seq.Insert(_at, DOTween.To(() => t_phase,
                                        _v => { t_phase = _v; this.cardEffect.transitionRate = Mathf.Repeat(_v, 1f); },
                                        Mathf.Max(1, this.gleamSweeps), _dur).SetEase(Ease.InQuad));
        }

        // 어둠에서 출발해 빛으로 차오른다.
        _seq.Insert(_at, DimTween(0.6f, _dur).SetEase(Ease.InQuad));
    }

    // 정적. 진동이 멎고 카드가 한 뼘 더 눌린다 — 이 멈춤이 다음 프레임을 기다리게 만든다.
    void BuildStill(Sequence _seq, float _at, float _dur)
    {
        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, Mathf.Min(0.08f, _dur)).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOScale(this.holdScale, _dur).SetEase(Ease.OutQuad));

        // 훑던 빛을 카드 한복판에 세운다. 흐르던 것이 멈추면 그 자체로 "곧 터진다"가 된다.
        if (this.cardEffect != null)
            _seq.Insert(_at, DOTween.To(() => this.cardEffect.transitionRate,
                                        _v => this.cardEffect.transitionRate = _v, GleamParked, Mathf.Min(0.1f, _dur)));
    }

    // 공개. 섬광이 최대인 순간에 값이 바뀐다 — 눈이 숫자가 바뀌는 과정을 보지 못한다.
    void BuildReveal(Sequence _seq, float _at)
    {
        if (this.flash != null)
        {
            _seq.Insert(_at, this.flash.DOFade(this.flashPeakAlpha, FlashRise).SetEase(Ease.OutQuad));
            _seq.Insert(_at + FlashRise, this.flash.DOFade(0f, FlashFall).SetEase(Ease.InQuad));
        }

        _seq.InsertCallback(_at + FlashRise, FireReveal);
    }

    // 폭발. 눌려 있던 것이 한 프레임에 터져 나왔다가 제자리로 회수된다 —
    // 부풀어 오르는 과정을 트윈에 맡기면 타격이 뭉개진다(PackCardView.PlayPunch와 같은 규칙).
    void BuildSuccess(Sequence _seq, float _at)
    {
        float t_settle = Mathf.Max(0.05f, this.burstSettle);

        _seq.InsertCallback(_at, () =>
        {
            if (this.cardStage != null) this.cardStage.localScale = Vector3.one * this.burstScale;
        });
        _seq.Insert(_at, this.cardStage.DOScale(1f, t_settle).SetEase(Ease.OutQuint));

        if (this.backGlow != null)
        {
            _seq.Insert(_at, this.backGlow.rectTransform.DOScale(this.glowStartScale, t_settle).SetEase(Ease.OutQuad));
            _seq.Insert(_at, this.backGlow.DOFade(0f, t_settle).SetEase(Ease.OutQuad));
        }

        // 세워뒀던 띠가 카드를 마저 빠져나간다 — 빛이 풀려나는 것으로 읽힌다.
        if (this.cardEffect != null)
            _seq.Insert(_at, DOTween.To(() => this.cardEffect.transitionRate,
                                        _v => this.cardEffect.transitionRate = _v, 1f, t_settle).SetEase(Ease.OutQuad));

        if (this.useScreenFlash && ScreenFlash.TryGet(out ScreenFlash t_flash))
        {
            Sequence t_cover = t_flash.BuildCover(this.successFlash);
            if (t_cover != null) _seq.Insert(_at, t_cover);
        }
    }

    // 압축이 터지지 못하고 풀린다. 빛이 먼저 죽어야 "놓쳤다"가 되고, 그 다음에 카드가 떨어진다.
    void BuildFail(Sequence _seq, float _at)
    {
        if (this.backGlow != null) _seq.Insert(_at, this.backGlow.DOFade(0f, 0.03f));

        if (this.cardEffect != null)
        {
            _seq.Insert(_at, DOTween.To(() => this.cardEffect.transitionRate,
                                        _v => this.cardEffect.transitionRate = _v, 1f, 0.08f));

            _seq.InsertCallback(_at, () => this.cardEffect.toneFilter = ToneFilter.Grayscale);
            _seq.Insert(_at, DOTween.To(() => this.cardEffect.toneIntensity,
                                        _v => this.cardEffect.toneIntensity = _v, this.failDesaturation, 0.1f));
        }

        _seq.Insert(_at, this.cardStage.DOScale(1f, 0.3f).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOAnchorPosY(this.m_baseAnchored.y - this.failDrop, 0.25f).SetEase(Ease.OutQuad));

        // 흔들림은 낙하가 끝난 뒤 — 같은 시간에 겹치면 두 트윈이 같은 좌표를 두고 싸운다.
        _seq.Insert(_at + 0.25f, this.cardStage.DOShakeAnchorPos(0.28f, this.failShake, 12, 90f, false, true));
    }

    void BuildResultText(Sequence _seq, float _at, bool _success, int _hpGain)
    {
        if (this.resultText == null) return;

        _seq.InsertCallback(_at, () =>
        {
            if (this.resultText == null) return;

            this.resultText.gameObject.SetActive(true);
            this.resultText.color = _success ? this.successColor : this.failColor;
            this.resultText.text  = _success && _hpGain > 0
                                  ? $"{this.successMessage}  +{_hpGain}"
                                  : _success ? this.successMessage : this.failMessage;
            this.resultText.alpha = 0f;
        });

        _seq.Insert(_at, TextFade(1f, 0.15f));
    }

    void BuildReturn(Sequence _seq, float _at, float _dur, float _end)
    {
        if (this.cardEffect != null)
        {
            _seq.Insert(_at, DOTween.To(() => this.cardEffect.toneIntensity,
                                        _v => this.cardEffect.toneIntensity = _v, 0f, Mathf.Min(0.2f, _dur)));
            _seq.InsertCallback(_end, () => this.cardEffect.transitionRate = 0f);
        }

        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOScale(1f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, DimTween(0f, _dur));

        if (this.resultText != null) _seq.Insert(_at, TextFade(0f, _dur));

        if (this.retractGroups != null)
            foreach (CanvasGroup t_g in this.retractGroups)
            {
                if (t_g == null) continue;
                _seq.Insert(_at, t_g.DOFade(1f, _dur));
            }

        // 길이를 못 박는다 — 위 트윈이 전부 미배선이면 시퀀스가 여기 닿기 전에 끝나 버린다.
        _seq.InsertCallback(_end, () => SetRetractBlocking(true));
    }

    // ── 상태 ─────────────────────────────────────────────

    void FireReveal()
    {
        Action t_cb = this.m_onReveal;
        this.m_onReveal = null;
        t_cb?.Invoke();
    }

    void FireFinished()
    {
        Action t_cb = this.m_onFinished;
        this.m_onFinished = null;
        t_cb?.Invoke();
    }

    void CaptureBase()
    {
        if (this.m_baseCaptured) return;

        this.m_baseCaptured = true;
        this.m_baseAnchored = this.cardStage.anchoredPosition;
        if (this.dim != null) this.m_baseDim = this.dim.color;
    }

    // 걷힌 패널은 투명해도 여전히 입력을 먹는다 — 그대로 두면 그 위를 탭해도 스킵이 안 되는 죽은 영역이 생긴다.
    void SetRetractBlocking(bool _on)
    {
        if (this.retractGroups == null) return;

        foreach (CanvasGroup t_g in this.retractGroups)
        {
            if (t_g == null) continue;
            t_g.blocksRaycasts = _on;
        }
    }

    // 다음 연출이 중간값(압축·회색·반투명)에서 출발하지 않게 원복. 캡처 전이면 건드릴 것도 없다.
    void RestoreVisual()
    {
        if (!this.m_baseCaptured) return;

        if (this.cardStage != null)
        {
            this.cardStage.anchoredPosition = this.m_baseAnchored;
            this.cardStage.localScale       = Vector3.one;
        }

        this.m_dimLevel = 0f;
        if (this.dim != null) this.dim.color = this.m_baseDim;

        SetGraphicAlpha(this.backGlow, 0f);
        if (this.backGlow != null) this.backGlow.rectTransform.localScale = Vector3.one * this.glowStartScale;

        SetGraphicAlpha(this.flash, 0f);

        if (this.cardEffect != null)
        {
            this.cardEffect.transitionRate = 0f;
            this.cardEffect.toneIntensity  = 0f;
        }

        if (this.resultText != null)
        {
            this.resultText.alpha = 0f;
            this.resultText.gameObject.SetActive(false);
        }

        if (this.retractGroups != null)
            foreach (CanvasGroup t_g in this.retractGroups)
            {
                if (t_g == null) continue;
                t_g.alpha = 1f;
            }

        SetRetractBlocking(true);
    }

    // getter는 트윈이 시작할 때 한 번 읽힌다 — 그래서 앞 구간이 남긴 밝기에서 이어 출발한다(PackScreenFlash와 같은 관용구).
    Tween DimTween(float _level, float _duration)
    {
        return DOTween.To(() => this.m_dimLevel, SetDim, _level, _duration);
    }

    // -1이면 가장 어둡고 +1이면 가장 밝다. 알파는 언제나 원래 값 — 어둠의 두께는 그대로 두고 색만 민다.
    void SetDim(float _level)
    {
        this.m_dimLevel = _level;

        if (this.dim == null || !this.m_baseCaptured) return;

        Color t_c = _level < 0f ? Color.Lerp(this.m_baseDim, this.dimDarkColor, -_level)
                                : Color.Lerp(this.m_baseDim, this.dimBrightColor, _level);
        t_c.a = this.m_baseDim.a;
        this.dim.color = t_c;
    }

    // TMP의 알파를 직접 민다 — DOFade는 DOTween의 TMP 모듈에 의존하고, 이 프로젝트는 그 축을 쓰지 않는다.
    Tween TextFade(float _to, float _duration)
    {
        return DOTween.To(() => this.resultText != null ? this.resultText.alpha : 0f,
                          _v => { if (this.resultText != null) this.resultText.alpha = _v; },
                          _to, _duration);
    }

    static void SetGraphicAlpha(Graphic _g, float _a)
    {
        if (_g == null) return;

        Color t_c = _g.color;
        t_c.a = _a;
        _g.color = t_c;
    }
}
