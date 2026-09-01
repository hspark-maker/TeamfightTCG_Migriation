using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// 개봉 오버레이 상주 브레인. 캐리어(PackHandoff)로 넘어온 개봉 세션을 뷰에 태우고, 개봉 완료 →
// 획득 버튼 노출 → 획득 클릭 → 보상 캐리어 적재 → (튜토리얼이면 시작 후) 목적지로 나간다.
// 목적지가 지금 있는 씬이면 오버레이를 닫고, 다른 씬이면 씬을 로드한다.
// 덱은 만들지 않는다 — 카드 소유는 구매 시점에 이미 영속됐고, 편성은 덱 화면의 몫이다.
//
// 경계: 목적지 분기는 캐리어 값(NextScene/StartTutorial)으로만 한다 — 첫시작 판정을 여기서 재계산하지 않는다.
//   이것이 구 FirstStartBattleRedirect 같은 별도 리다이렉트 레이어를 없앤 이유(구매한 쪽이 목적지를 이미 결정).
//   Battle 참조는 TutorialConfig.Begin 한 줄뿐(TutorialSetupUI 선례와 동일 방향, 전투 지식 격리).
public class PackAcquireController : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("카드팩 개봉 뷰. BeginOpen으로 세션을 태우고 OnRevealComplete를 수신.")]
    [SerializeField] PackRevealView view;
    [Tooltip("개봉 완료(카드 배치) 후 노출되는 획득 버튼.")]
    [SerializeField] Button acquireButton;
    [Tooltip("StartTutorial일 때 전투 진입 전 시작할 튜토리얼 시나리오. 일반 경로면 미사용.")]
    [SerializeField] TutorialScenarioData scenario;

    [Header("한 번 더 (옵션 — 미배선이면 재개봉 없음)")]
    [Tooltip("결과 화면의 '한 번 더' 버튼. 방금 연 팩을 같은 값에 다시 사서 그 자리에서 재개봉한다.")]
    [SerializeField] Button retryButton;
    [Tooltip("한 번 더 버튼에 상시 띄우는 가격 숫자. 살 수 없을 때도 값은 보여야 유저가 이유를 안다.")]
    [SerializeField] TMP_Text retryPriceText;
    [Tooltip("가격 옆 재화 아이콘. 팩의 결제 재화를 따라 CurrencyLook 표의 그림으로 갈린다 — 표가 비면 프리팹 그림 그대로다.")]
    [SerializeField] Image retryPriceIcon;
    [Tooltip("모자란 가격 숫자의 색. 흑백이 '못 누른다'를 말하고 이 색이 '어디가 모자란가'를 말한다.")]
    [SerializeField] Color shortPriceColor  = new Color(0.95f, 0.30f, 0.28f, 1f);
    [SerializeField] Color normalPriceColor = Color.white;
    [Header("재화 상단바")]
    [Tooltip("결과 화면 상단바(조각·골드·다이아 한 줄). 결과에서만 뜬다 — 개봉 연출 중엔 걷혀 있다.")]
    [SerializeField] GameObject currencyBar;
    [Tooltip("상단바 등장 연출. Panel에 바의 RectTransform을 물릴 것.")]
    [SerializeField] PopupTransition barTransition = new PopupTransition();
    [Tooltip("바 안의 조각 칩. 중복 환급 코인이 여기로 꽂힌다. 'Register As Primary'는 꺼 둘 것.")]
    [SerializeField] CurrencyHud shardHud;
    [Tooltip("환급을 조각 칩으로 흘려보낼 재생기. 개봉 캔버스 안에 둘 것('Shared'는 꺼 둘 것). " +
             "미배선이면 환급을 로비 획득 연출로 넘긴다.")]
    [SerializeField] CurrencyGainEffectPlayer refundEffect;
    [Tooltip("환급이 피어날 빛 그림. Sprites/CardPack/Glow_Radial 권장(앨범 보상이 쓰는 것과 같은 훈김). " +
             "미배선이면 코인 그림이 그대로 흐른다 — 줄기 하나가 가는 그림은 같다.")]
    [SerializeField] Sprite refundLightSprite;

    [Header("재개봉 전환")]
    [Tooltip("재개봉을 덮는 판. 재화 바보다 앞 형제로 둘 것 — 뒤에 두면 바까지 덮여 골드 차감이 안 보인다. " +
             "미배선이면 전역 플래시로 덮는다(바도 함께 덮인다).")]
    [SerializeField] Image retryCover;
    [Tooltip("재개봉 전환 플래시의 생김새. 첫 구매(PackShowcaseController)와 같은 값으로 둘 것.")]
    [SerializeField] ScreenFlashCover retryFlash = new ScreenFlashCover();

    // 캐리어에서 받은 목적지 컨텍스트(Start에서 캡처, 이후 재판정 안 함).
    string m_nextScene;
    bool m_startTutorial;

    // 방금 연 팩. 캐리어는 BeginSession에서 Consume돼 비므로, 되사려면 여기 쥐고 있어야 한다.
    string m_pack;

    // 이번 개봉에서 처음 얻은 카드만(로비 도감 연출 대상). Consume은 1회뿐이라 여기 캐시한다.
    // 중복은 팩 결제 재화로 환급돼 코인 연출로 나가므로 도감 연출거리가 없다.
    readonly List<int> m_newCards = new List<int>();
    // 이번 개봉의 중복 환급 합계(연출 표시용). 카드와 같은 시점에 캐시해야 짝이 갈라지지 않는다.
    CurrencyGain m_refund;

    // 획득이 2회 이상 눌려도 씬 전이는 1회만.
    bool m_left;

    // 잠긴 동안 걸어 둔 흑백 효과의 원복 목록. 밑판 하나에만 걸던 옛 방식은 RetryButton 프리팹에
    // UIEffect 저작이 아예 없어 조작만 막히고 화면은 활성 그대로였다 — 자식까지 전수로 거는 UiGrayscale로 옮겼다.
    List<UiGrayscale.Toned> m_retryToned;

    // 지금 흑백이 걸려 있는가. 같은 판정이 두 번 와도 효과를 겹쳐 걸지 않게 막는다.
    bool m_retryToneOn;

    // 그때 가격 짝(숫자·재화 아이콘)을 색이 살아 있게 빼 두었는가. 잠김 이유가 바뀌면 제외 대상도 갈리므로
    // 켜짐 여부만으로는 다시 걸어야 할 때를 못 가른다.
    bool m_retryTonePriceKept;

    // 재개봉이 도는 중. 서버 왕복이 시작된 순간부터 임팩트가 화면을 덮고 세션을 갈아끼울 때까지를 통째로 덮는다
    // (상점의 s_transitioning과 같은 자리 — 그쪽은 오버레이를 열고, 여기는 세션을 갈아끼운다).
    // 화면이 꺼지면 OnDisable이 내리므로, 왕복이 돌아왔을 때 이 값이 곧 "그 세션이 아직 살아 있는가"다.
    bool m_retrying;

    // 이번 세션의 환급을 이 화면에서 이미 보여줬는가. 보여줬으면 로비로 넘기지 않는다 —
    // 같은 코인을 두 화면에서 두 번 받는 그림이 되고, 유저는 두 배를 받은 줄로 읽는다.
    bool m_refundShown;


    /// <summary>캐리어에 실린 개봉 세션을 뷰에 태운다. 오버레이가 열릴 때마다 호출된다
    /// — Start에 두면 오버레이는 한 번만 열리는 화면이 된다(재개봉 불가).</summary>
    public bool BeginSession()
    {
        // 카드 배치 전까지 아래 버튼들 숨김 — OnRevealComplete에서 함께 노출.
        if (acquireButton != null) acquireButton.gameObject.SetActive(false);
        if (retryButton != null) retryButton.gameObject.SetActive(false);
        m_left = false;

        if (!PackHandoff.HasPending)
        {
            // 정상 진입은 상점/초기화가 캐리어를 채우고 오버레이를 연 경우뿐. 그 외는 열 팩이 없음.
            Debug.LogWarning("[PackAcquireController] PackHandoff 없음 — 정상 진입이 아님(열 팩 없음).");
            return false;
        }
        if (view == null)
        {
            Debug.LogWarning("[PackAcquireController] view 미배선 → 개봉 진행 불가.");
            return false;
        }

        // 목적지·팩 컨텍스트를 먼저 캡처한 뒤 Consume(캐리어를 통째로 비움 → 다음 개봉 세션 격리).
        m_nextScene = PackHandoff.NextScene;
        m_startTutorial = PackHandoff.StartTutorial;
        m_pack = PackHandoff.PackId;
        var t_opened = PackHandoff.Consume();

        if (t_opened == null || !t_opened.Success)
        {
            // 정상 진입은 구매가 성립한 뒤뿐이다 — 태울 결과가 없으면 세션을 열지 않는다.
            Debug.LogWarning("[PackAcquireController] 개봉할 결과가 없음 — 세션을 열지 않는다.");
            return false;
        }

        CacheCards(t_opened);
        m_refundShown = false;
        BindRetryPrice();

        // 바는 결과 화면의 것이다 — 세션이 갈리는 지금 연출 없이 즉시 치운다. 재개봉이 여기 올 때는
        // 덮개가 이미 화면을 가린 뒤라(차감 롤다운도 그 앞에서 끝난다) 걷히는 프레임이 드러나지 않는다.
        HideBar();

        view.BeginOpen(t_opened, m_pack);
        return true;
    }

    void OnEnable()
    {
        if (view != null) view.OnRevealComplete += OnRevealComplete;
        if (acquireButton != null) acquireButton.onClick.AddListener(OnAcquirePressed);
        if (retryButton != null) retryButton.onClick.AddListener(OnRetryPressed);
    }

    void OnDisable()
    {
        if (view != null) view.OnRevealComplete -= OnRevealComplete;
        if (acquireButton != null) acquireButton.onClick.RemoveListener(OnAcquirePressed);
        if (retryButton != null) retryButton.onClick.RemoveListener(OnRetryPressed);

        // 화면이 꺼진 뒤 도착하는 임팩트 콜백은 세션을 되살릴 자리가 없다 — 여기서 미리 무효화한다.
        // 남겨 두면 다음에 열렸을 때 이 플래그가 그대로 남아 재구매가 영영 막힌다.
        m_retrying = false;

        // 풀 UI라 같은 오브젝트가 다음 개봉에 다시 쓰인다 — 걸어 둔 흑백을 여기서 걷지 않으면
        // 튜토리얼이 끝난 뒤 열린 화면에서도 버튼이 회색으로 남는다.
        ApplyRetryTone(false, false);

        // 퇴장 중에 오버레이가 닫히면 완료 콜백이 오지 않는다 — 켜진 채 남으면 다음 개봉에서 유령 프레임이 뜬다.
        HideBar();
    }

    IEnumerator CloseNextFrame()
    {
        yield return null;

        if (PackOpenOverlay.Instance != null) PackOpenOverlay.Instance.Close();
    }

    // 신규 획득 카드 + 환급 총액 캐시(null 카드 제외).
    void CacheCards(OpenedPack _opened)
    {
        m_newCards.Clear();
        m_refund = _opened != null ? _opened.TotalRefund : CurrencyGain.None;

        var t_drawn = _opened != null ? _opened.Cards : null;
        if (t_drawn == null) return;

        for (int t_i = 0; t_i < t_drawn.Count; t_i++)
        {
            int t_card = t_drawn[t_i].CardId;
            if (t_card <= 0 || !t_drawn[t_i].IsNew) continue;

            m_newCards.Add(t_card);
        }
    }

    // 개봉 연출(카드 배치)이 끝났을 때 1회 호출 → 아래 버튼들 노출.
    void OnRevealComplete()
    {
        if (acquireButton != null) acquireButton.gameObject.SetActive(true);

        // 한 번 더는 살 수 없어도 자리를 지킨다 — 숨기면 남은 획득 버튼이 한쪽으로 치우쳐 화면이 흔들린다.
        if (retryButton != null) retryButton.gameObject.SetActive(true);
        RefreshRetryLock();

        // 바를 먼저 세운다. SetVisible이 같은 프레임에 SetActive(true)까지 끝내므로 아래 환급 연출이
        // 곧바로 조각 칩을 찾을 수 있고, 되감기(BeginGainRollUp)도 등장과 같은 프레임에 걸린다
        // — 한 프레임이라도 늦으면 환급이 이미 반영된 최종값이 먼저 보였다가 뒤로 떨어진다.
        if (currencyBar != null) barTransition.SetVisible(currencyBar, true);

        PlayRefundGain();
    }

    // 중복 환급을 조각 칩으로 흘려보낸다. 카드가 다 깔린 이 시점이어야 —
    // 넘기는 도중에 쏘면 "이 카드가 중복이었다"는 낱장의 사연이 합계에 묻힌다.
    //
    // 앨범 보상과 같은 축이다(RewardClaimPopup.OnClaimClicked) — 합계 칩이 빛 한 줄기로 피어나 조각 칩으로 흐르고,
    // 칩은 그 빛 밑에서 사그라든다. 코인 다발이 아니라 이동체가 하나여야 눈이 따라갈 대상이 정해진다.
    void PlayRefundGain()
    {
        if (m_refundShown || refundEffect == null || view == null || !m_refund.HasAmount) return;

        // 받을 자리가 이 화면에 서 있을 때만 쏜다. 없으면(팩의 환급 재화가 조각 칩과 다른 경우 등)
        // 그대로 두어 로비 획득 연출이 가져가게 한다 — 여기서 쏘면 빛이 지금 안 보이는 곳으로 날아가
        // 유저는 환급을 한 번도 못 보게 된다.
        var t_hud = ActiveShardHud(m_refund.Type);
        if (t_hud == null) return;

        // 잔액이 이미 최종값이라는 전제(PurchaseAsync가 환급까지 끝냈다) — 재생기가 그만큼 되돌렸다 올린다.
        var t_light = refundEffect.BuildLightGain(m_refund, view.RefundCoinRect, t_hud, refundLightSprite);
        if (t_light == null) return;

        // 빛이 칩 '위'에 떠야 칩이 그 아래서 사라진 것으로 읽힌다(앨범 팝업과 같은 한 줄).
        refundEffect.transform.SetAsLastSibling();

        // 합계가 다 굴러 오른 뒤에 쏜다 — 세는 도중에 칩을 걷으면 얼마를 받았는지 읽을 자리가 사라진다.
        // 사그라듦과 피어남을 같은 시각에 놓는 것이 이 연출의 전부다.
        var t_seq = DOTween.Sequence().SetLink(gameObject);
        t_seq.InsertCallback(view.RefundCountUp, view.DismissRefundBadge);
        t_seq.Insert(view.RefundCountUp, t_light);

        m_refundShown = true;
        t_seq.Play();
    }

    /// <summary>로비로 넘길 환급. 이 화면에서 이미 코인으로 보여줬다면 넘길 것이 없다.
    /// 카드는 언제나 넘긴다 — 도감에 꽂히는 연출은 로비에만 자리가 있다.</summary>
    CurrencyGain PendingRefund() => m_refundShown ? CurrencyGain.None : m_refund;

    // 이 화면에 **떠 있는** 조각 칩. 꺼져 있으면 없는 것과 같다 —
    // CoinBurstEffect는 비활성 노드에서 코인을 즉시 걷어 숫자만 오르는 그림이 된다.
    CurrencyHud ActiveShardHud(ECurrencyType _type)
        => shardHud != null && shardHud.Type == _type && shardHud.isActiveAndEnabled ? shardHud : null;

    // 연출 없이 즉시 치운다.
    void HideBar()
    {
        if (currencyBar == null) return;

        currencyBar.SetActive(false);
        barTransition.HandleDisabled(currencyBar);
    }

    // 살 수 없으면 잠근다(잔액을 버튼 상태로 드러내면 실패 팝업을 볼 일이 없다 — 상점 RefreshBuyLock과 같은 방침).
    // 튜토리얼 중에도 잠근다: 재개봉은 PackRevealView.OnAnyPackOpened를 한 번 더 쏘므로,
    //   "오버레이 1회 열림 : 개봉신호 1회"를 전제로 세는 튜토리얼 스텝이 어긋난다. 이 잠금이 그 유일한 방어다.
    // 잔액 변동을 구독하지 않는 이유 — 환급까지 PurchaseAsync 안에서 이미 끝나 있고, 결과 화면이 떠 있는 동안
    //   잔액을 움직이는 것은 이 버튼 자신뿐이다. 그 직후 새 세션의 이 함수가 다시 판정한다.
    void RefreshRetryLock()
    {
        if (retryButton == null) return;

        bool t_allowed = !string.IsNullOrEmpty(m_pack)
                      && !m_left
                      && !OutgameTutorialRunner.IsRunning
                      && PackUnlockRules.IsUnlocked(m_pack)
                      && OutgameFeatureLock.IsUnlocked(EOutgameFeature.PackBuy);
        bool t_afford = !string.IsNullOrEmpty(m_pack) && CurrencyManager.CanAfford(PackSpec.PriceType(m_pack), PackSpec.Price(m_pack));

        retryButton.interactable = t_allowed && t_afford;

        ApplyRetryTone(!retryButton.interactable, t_allowed && !t_afford);

        // 빨강은 "여기가 모자란다" 한 축에만 쓴다 — 튜토리얼·기능잠금은 모자란 것이 아니다.
        if (retryPriceText != null)
            retryPriceText.color = t_allowed && !t_afford ? shortPriceColor : normalPriceColor;
    }

    // 못 누르는 동안 버튼이 **통째로** 흑백이 된다. 밑판만 무채색으로 갈아끼우면 자식(라벨·가격·동전)이
    // 원색 그대로 남아 오히려 어수선해진다 — 색이 빠지는 일은 버튼 전체에 한 번에 걸려야 한다.
    // 알파를 낮추지 않는 이유: 개봉 화면이 어두워 반투명은 곧 사라짐이 된다.
    //
    // _keepPrice는 가격 숫자와 그 옆 재화 아이콘만 색을 살려 둔다. 흑백이 "못 누른다"를 말하고
    // 그 빨강이 "어디가 모자란가"를 말하는 두 축 구성이라, 함께 눕히면 뒤의 축이 통째로 사라진다.
    // 숫자만 살리고 아이콘을 눕히지 않는 이유 — 모자란 것이 어느 재화인지는 그 둘이 짝으로 말한다.
    void ApplyRetryTone(bool _on, bool _keepPrice)
    {
        if (!_on) _keepPrice = false;   // 꺼진 상태에는 결이 없다

        if (_on == m_retryToneOn && _keepPrice == m_retryTonePriceKept) return;

        // 결이 바뀌는 경우(제외 대상이 늘거나 줄었다)엔 저작값까지 되돌린 뒤 다시 건다.
        UiGrayscale.Restore(m_retryToned);

        if (_on)
        {
            m_retryToned = _keepPrice
                ? UiGrayscale.Apply(retryButton.gameObject,
                                    retryPriceText != null ? retryPriceText.transform : null,
                                    retryPriceIcon != null ? retryPriceIcon.transform : null)
                : UiGrayscale.Apply(retryButton.gameObject);
        }

        m_retryToneOn        = _on;
        m_retryTonePriceKept = _keepPrice;
    }

    // 한 번 더 버튼의 가격 표시. 세션당 한 번만 바뀐다(같은 팩을 되사므로 값이 움직이지 않는다).
    void BindRetryPrice()
    {
        if (retryPriceText != null) retryPriceText.text = !string.IsNullOrEmpty(m_pack) ? $"{PackSpec.Price(m_pack):N0}" : string.Empty;
        if (retryPriceIcon == null) return;

        // 숫자가 비는 상태에선 아이콘도 함께 걷는다(숫자 없이 아이콘만 남는 칸 방지).
        retryPriceIcon.enabled = !string.IsNullOrEmpty(m_pack);

        var t_icon = ResolveCurrencyIcon(m_pack);
        if (t_icon != null) retryPriceIcon.sprite = t_icon;
    }

    // 결제 재화 아이콘. 표에 그림이 없으면 null이고, 그때는 호출부가 프리팹 그림을 그대로 둔다.
    static Sprite ResolveCurrencyIcon(string _packId)
    {
        if (string.IsNullOrEmpty(_packId)) return null;

        return CurrencyLook.IconOf(PackSpec.PriceType(_packId));
    }

    // 한 번 더 클릭: 같은 팩을 되사서 오버레이를 닫지 않고 세션만 갈아끼운다(팩 등장부터 다시).
    // 오버레이를 여닫지 않으므로 OnOpened/OnClosed는 울리지 않는다 — 상점 진열은 얼린 채로 두고,
    // 로비 획득 연출은 마지막에 닫힐 때 누적분을 한 번에 재생한다.
    // PackShowcaseController.OnAnyPurchased도 일부러 울리지 않는다: 그 신호의 유일한 구독자가 튜토리얼이고,
    //   튜토리얼 중엔 이 버튼이 잠겨 있다. 신호를 늘리면 "구매 1회 : 스텝 1칸"이 도리어 어긋난다.
    void OnRetryPressed()
    {
        if (m_left || m_retrying || string.IsNullOrEmpty(m_pack) || view == null) return;

        RetryAsync().Forget();
    }

    // 재구매. 대기 표시는 PackPurchaseFlow가 덮고, 거절 안내도 그쪽이 띄운다 —
    // 여기서는 응답이 성공일 때만 세션을 갈아끼운다(실패면 지금 결과 화면을 그대로 둔다).
    async UniTaskVoid RetryAsync()
    {
        var t_pack = m_pack;

        // 잔액 부족·랭크 잠금은 버튼이 이미 잠가 두지만, 화면을 갈아끼우기 전 마지막으로 한 번 더 묻는다.
        // 같은 거절을 서버가 답하면 팝업이 뜨는데 여기서 걸릴 때만 조용히 지나가면, 유저가 보는 결과가
        // 왕복 타이밍에 따라 갈린다 — 안내는 같은 자리로 모으고 세션만 그대로 둔다(버튼은 다시 판정).
        var t_precheck = CardPackOpener.Precheck(t_pack);
        if (t_precheck != EPackOpenResult.Success)
        {
            Debug.LogWarning($"[PackAcquireController] 재개봉 불가({t_precheck}) — 팩 데이터 확인.");
            PackPurchaseFailurePopup.Show(t_pack, t_precheck);
            RefreshRetryLock();
            return;
        }

        // 결제가 도는 동안의 이탈 차단은 이 플래그 하나로 한다 — 왕복 "전"에 세워야 같은 결제가 여러 번 나가지 않는다
        // (첫 구매도 s_transitioning 플래그로만 막는다 — 같은 사건이 여기서만 달리 보이지 않게).
        m_retrying = true;

        var t_opened = await PackPurchaseFlow.PurchaseAsync(t_pack, this);

        // 왕복 중 이 개봉 세션이 끝났는가. 파괴(this == null)만 보면 부족하다 — 오버레이가 코드로 닫히면
        // OnDisable이 m_retrying을 내리고, 획득을 눌러 떠났으면 m_left가 선다. 어느 쪽이든 갈아끼울 세션이 없다.
        if (this == null || !m_retrying || m_left)
        {
            // 캐리어에 싣지 않는 것은 의도적이다: 다음에 열리는 개봉이 남의 결과를 물려받는 편이 더 나쁘다
            // (PackShowcaseController.BuyAsync와 같은 처방).
            if (t_opened != null)
                Debug.LogWarning("[PackAcquireController] 구매 성립 후 개봉 세션이 사라짐 — 카드는 지급됐으나 연출 생략.");

            // 잠금은 살아 있는 갈래에서만 손으로 내린다. 획득은 되돌려질 수 있어(오버레이 미배선 폴백)
            // 켜 둔 채 남기면 그 뒤로 되사기·획득이 함께 죽는다.
            if (this != null) m_retrying = false;
            return;
        }

        if (t_opened == null)
        {
            // 안내는 PackPurchaseFlow가 이미 띄웠다 — 여기서는 잠금만 풀고 지금 세션을 그대로 둔다.
            m_retrying = false;
            RefreshRetryLock();
            return;
        }

        // 이번 세션 몫을 먼저 싣는다 — 곧 BeginSession이 캐시를 덮으므로, 여기서 놓치면
        // 직전 개봉의 신규 카드·환급이 로비 연출에서 통째로 사라진다(캐리어는 누적이라 겹쳐 실어도 된다).
        CardPackRewardHandoff.Set(PendingRefund(), m_newCards);

        // 목적지 컨텍스트는 그대로 물려준다 — 어느 세션에서 획득을 누르든 나가는 곳은 같아야 한다.
        PackHandoff.Set(t_opened, t_pack, m_nextScene, m_startTutorial);

        // 첫 구매와 같은 임팩트를 같은 순서로 태운다(PackShowcaseController.OnBuyPressed 관용구).
        // 반응할 팩이 화면에 없으므로 눌린 버튼 자신이 그 자리를 대신한다 — 결제의 주체가 곧 버튼이다.
        // 덮개는 전역이 아니라 프리팹 판이다 — 전역으로 덮으면 재화 바까지 가려 골드 차감이 안 보인다.
        // 연출을 세우지 못하면 예전처럼 즉시 갈아끼운다(연출은 있으면 좋은 것이지, 재개봉의 조건이 아니다).
        if (PackPurchaseImpact.TryGet(this, out var t_impact))
            t_impact.Play(retryButton != null ? (RectTransform)retryButton.transform : null,
                          retryFlash, RestartSession, retryCover);
        else
            RestartSession();
    }

    // 화면이 덮인 순간 1회(끊겨도 임팩트가 반드시 부른다). 여기서만 세션을 갈아끼운다 —
    // 결과 격자가 걷히고 팩이 서는 프레임이 덮개 밑에 숨어야 하드컷으로 드러나지 않는다.
    void RestartSession()
    {
        if (!m_retrying) return;
        m_retrying = false;

        // 그 사이 화면을 떠났다면(획득·씬 전이) 되살릴 세션이 없다 — 캐리어는 다음 개봉이 소비한다.
        if (m_left) return;

        view.ResetSession();   // 요약 상태를 되돌려야 BeginOpen의 재진입 가드가 풀린다.
        if (BeginSession()) return;

        // 태우지 못하면 두 버튼이 이미 숨겨진 뒤라 출구 없는 빈 화면이 남는다 — 오버레이째 떨어뜨린다
        // (PackOpenOverlay.Open이 같은 false를 받고 하는 일과 같은 처분).
        Debug.LogWarning("[PackAcquireController] 재개봉 세션 시작 실패 — 오버레이를 닫는다.");
        if (PackOpenOverlay.Instance != null) PackOpenOverlay.Instance.Close();
    }

    // 획득 클릭: 튜토리얼이면 시작 → 목적지 씬으로 이동(1회 가드).
    // 개봉 카드로 덱을 만들지 않는다 — 첫 덱은 초기화의 StarterDeck이 보장하고, 이후 편성은 유저 몫이다.
    void OnAcquirePressed()
    {
        // 재구매가 화면을 덮는 중이면 나갈 수 없다 — 방금 산 팩이 캐리어에 갇힌 채 로비로 돌아간다.
        if (m_left || m_retrying) return;
        m_left = true;
        RefreshRetryLock();   // 나가기로 한 뒤엔 되사기를 막는다(닫히는 프레임에 눌리는 것 차단).

        // 환급·카드가 같은 개봉 세션 결과라 로비 복귀 직전 한 지점에서 함께 싣는다(지급·저장은 이미 끝났다 — 표시량뿐).
        // 카드는 신규만 — 중복분은 환급 재화로 이미 표현된다. 튜토리얼 경로로 전투에 먼저 가면 이후 로비 진입 시 재생된다.
        CardPackRewardHandoff.Set(PendingRefund(), m_newCards);

        // 튜토리얼 세팅은 목적지(전투) 진입 직전 1회. scenario null이면 Begin이 End로 안전 처리.
        if (m_startTutorial) TutorialConfig.Begin(scenario);

        // 목적지가 지금 있는 씬이면 떠날 것이 없다 — 오버레이만 닫아 로비로 돌아간다(일반 구매 경로).
        // 목적지가 비었을 때도 같다: 단독 테스트 씬은 그 자리에 머무는 것이 기대 동작이다.
        // 다른 씬이면 진짜 씬 로드(첫실행의 전투 진입). 분기는 여기 한 곳뿐 — 목적지 판정을 늘리지 않는다.
        if (string.IsNullOrEmpty(m_nextScene) || m_nextScene == SceneManager.GetActiveScene().name)
        {
            if (PackOpenOverlay.Instance == null)
            {
                // 배선 오류로 버튼이 죽어 화면에 갇히는 것을 막는다(재실행돼도 캐리어 재적재뿐이라 부작용 없음).
                // 되사기도 함께 되살린다 — 안 그러면 이탈 잠금만 남아 유일한 다른 출구까지 죽는다.
                m_left = false;
                RefreshRetryLock();
                Debug.LogWarning("[PackAcquireController] PackOpenOverlay 없음 — 닫기 불가(오버레이 배선 확인).");
                return;
            }

            // 같은 버튼에 튜토리얼 게이트가 함께 걸려 있다 — 여기서 바로 닫으면 오버레이 닫힘이
            // 게이트의 완료 커밋보다 먼저 돌아 그 커밋이 유실된다(진행 불가).
            // 씬 로드 시절엔 로드가 프레임 끝이라 저절로 지켜지던 순서라, 그 타이밍을 명시로 되살린다.
            StartCoroutine(CloseNextFrame());
            return;
        }

        SceneManager.LoadScene(m_nextScene);
    }

}
