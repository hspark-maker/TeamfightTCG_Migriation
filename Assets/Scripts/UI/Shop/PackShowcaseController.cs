using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// Tab_Pack의 카드팩 쇼케이스 컨트롤러. 진열할 팩들을 인스펙터에서 직접 받아 캐러셀에 그림을 공급하고,
// 중앙에 놓인 팩의 이름·가격을 채우고, 구매 버튼 클릭 시 TryPurchase → 캐리어(PackHandoff) → 개봉 오버레이 열기를 수행한다.
// 이 흐름은 튜토리얼 자동 구매 스텝(OutgameTutorialRunner)이 쓰는 경로와 동일하며, 버튼 트리거로 재현한 것.
// 경계: 구매·소유·차감은 TryPurchase가 원자 영속하고, 뷰는 표시·결과 분기·전환만 담당한다.
// 진열 목록(packs)과 중복 환급액(duplicateRefundGold)은 이 뷰가 직접 소유해 TryPurchase에 넘긴다
//   — 상점 SO 미개입(목록이 비면 구매 잠금).
// 제스처·스냅은 PackCarouselView가 쥔다. 그쪽은 팩을 모르고 "그림 N장 중 몇 번째"만 안다 —
//   돈을 쥔 이 클래스가 포인터 물리까지 소유하지 않게 한 분리다.
//
// 튜토리얼 구매 스텝 중에는 저작된 팩이 진열 "목록 자체"를 대체한다(ResolveDisplay).
//   우선순위 규칙으로 두면 캐러셀은 팩 A를, 가격·결제는 팩 B를 가리키는 상태가 생긴다 —
//   목록으로 흡수하면 그림·이름·가격·결제가 전부 한 곳에서 나오므로 갈릴 여지가 구조적으로 없다.
public class PackShowcaseController : MonoBehaviour
{
    // 구매가 실제로 성립한 순간 발화(클릭이 아니라 결과). 구독자는 모른다 — "일어난 일"만 알린다.
    public static event Action OnAnyPurchased;

    [SerializeField] Button buyButton;              // 구매 → 개봉 오버레이 열기 트리거.
    [SerializeField] TextMeshProUGUI packNameText;  // 중앙 팩 표시명(옵션 — 미배선 무시).
    [SerializeField] TextMeshProUGUI priceText;     // 가격(Gold, 옵션 — 미배선 무시).
    [Tooltip("캐러셀에 진열할 팩들. 순서가 곧 페이지 순서. 비어 있으면 구매 잠금.")]
    [SerializeField] List<CardPackData> packs = new List<CardPackData>();
    [Tooltip("좌우 넘김을 담당하는 캐러셀. 미배선이면 목록 첫 팩만 진열된다.")]
    [SerializeField] PackCarouselView carousel;
    [Tooltip("이 팩에서 이미 소유한 카드를 뽑았을 때(중복) 되돌려주는 Gold.")]
    [Min(0)] [SerializeField] long duplicateRefundGold = 10;

    // ScreenFlash·PackPurchaseImpact는 둘 다 런타임 자가설치라 프리팹에 배선할 자리가 없다 —
    // 개봉 화면으로 갈아치울 때 쓰는 플래시의 값·에셋은 여기가 유일한 노출 창구다.
    [Header("구매 → 개봉 전환 플래시")]
    [Tooltip("전환을 덮는 플래시의 생김새. 손대지 않으면 코드 기본값으로 돈다. " +
             "빛 스프라이트를 비우면 예전처럼 단색 판만 남는다(전환 은폐 기능은 그대로).")]
    [SerializeField] ScreenFlashCover purchaseFlash = new ScreenFlashCover();

    // 전환은 1회만(같은 프레임 멀티탭 이중결제 차단). 개봉 오버레이가 닫힐 때 해제된다 —
    // 씬을 떠나지 않으므로 OnEnable만으로는 영영 잠긴 채로 남는다(탭이 계속 활성이라 다시 돌지 않는다).
    static bool s_transitioning;

    readonly List<CardPackData> m_display = new List<CardPackData>();   // 실제 진열 목록(해석 결과).
    readonly List<CardPackData> m_built = new List<CardPackData>();     // 캐러셀에 마지막으로 넘긴 목록.
    readonly List<Sprite> m_arts = new List<Sprite>();

    int m_index;
    bool m_forced;        // 튜토리얼이 진열을 덮어썼는가.
    long m_forcedRefund;

    // 구매는 끝났고 개봉 화면만 아직 열지 않은 상태. 임팩트가 화면을 덮는 사이의 짧은 구간이다.
    bool m_openPending;

    void OnEnable()
    {
        s_transitioning = false;

        if (buyButton != null)
        {
            buyButton.onClick.RemoveListener(OnBuyPressed);
            buyButton.onClick.AddListener(OnBuyPressed);
        }
        if (carousel != null)
        {
            carousel.OnIndexChanged -= OnPageChanged;
            carousel.OnIndexChanged += OnPageChanged;
        }

        CurrencyManager.OnCurrencyChanged    += OnCurrencyChanged;
        OutgameTutorialRunner.OnStepChanged  += Refresh;
        OutgameFeatureLock.OnChanged         += Refresh;
        PackOpenOverlay.OnClosed             += OnOverlayClosed;
        Refresh();
    }

    void OnDisable()
    {
        if (buyButton != null) buyButton.onClick.RemoveListener(OnBuyPressed);
        if (carousel != null) carousel.OnIndexChanged -= OnPageChanged;

        CurrencyManager.OnCurrencyChanged    -= OnCurrencyChanged;
        OutgameTutorialRunner.OnStepChanged  -= Refresh;
        OutgameFeatureLock.OnChanged         -= Refresh;
        PackOpenOverlay.OnClosed             -= OnOverlayClosed;

        // 임팩트가 화면을 덮기 전에 탭이 꺼지면 시퀀스가 끊겨 개봉 화면을 여는 콜백이 오지 않는다.
        // 카드는 이미 지급됐으므로 연출은 잃더라도 화면은 반드시 띄운다.
        OpenOverlay();
    }

    // 개봉이 끝나면 다시 살 수 있다. 씬을 떠나던 시절엔 OnEnable이 해주던 일 — 이제 아무도 해주지 않는다.
    void OnOverlayClosed()
    {
        s_transitioning = false;
        RefreshBuyLock();   // 개봉으로 잔액이 줄었다 — 다음 구매 가능 여부를 다시 판정한다.
    }

    void OnCurrencyChanged(ECurrencyType _type, long _balance)
    {
        // 진열 팩마다 결제 재화가 다를 수 있어 종류를 가리지 않는다(판정은 RefreshBuyLock이 팩 기준으로 한다).
        RefreshBuyLock();
    }

    // 페이지가 바뀌면 표시와 잠금이 함께 따라간다 — 둘 중 하나만 갱신하면 "보이는 팩과 살 팩"이 갈린다.
    void OnPageChanged(int _index)
    {
        m_index = _index;
        Bind();
        RefreshBuyLock();
    }

    // 진열 갱신. 탭을 여는 시점과 스텝이 바뀌는 시점이 다르므로(탭 활성화가 스텝 커밋보다 먼저다)
    // 두 시점 모두에서 다시 해석해야 표시와 결제가 갈리지 않는다.
    void Refresh()
    {
        ResolveDisplay();
        SyncCarousel();
        Bind();
        RefreshBuyLock();
    }

    // 진열 목록 해석. 튜토리얼 구매 스텝이 팩을 지정했으면 그 팩 하나만 진열한다 —
    // 잠그기는 그 다음 문제일 뿐, 표시·결제 일치는 목록이 지킨다.
    void ResolveDisplay()
    {
        m_display.Clear();
        m_forced = OutgameTutorialRunner.TryGetForcedPack(out var t_forced, out m_forcedRefund);

        if (m_forced)
        {
            if (t_forced != null) m_display.Add(t_forced);
            return;
        }

        for (int t_i = 0; t_i < packs.Count; t_i++)
            if (packs[t_i] != null) m_display.Add(packs[t_i]);   // 미할당 슬롯이 빈 페이지가 되지 않게 거른다.
    }

    // 캐러셀 동기화. 목록이 실제로 달라졌을 때만 재구축한다 —
    // OnStepChanged는 자주 도는데 매번 다시 세우면 유저가 보던 페이지를 잃는다.
    void SyncCarousel()
    {
        if (carousel == null)
        {
            m_index = m_display.Count > 0 ? Mathf.Clamp(m_index, 0, m_display.Count - 1) : 0;
            return;
        }

        if (!SameAsBuilt(carousel.PageCount))
        {
            m_arts.Clear();
            for (int t_i = 0; t_i < m_display.Count; t_i++) m_arts.Add(m_display[t_i].PackArt);
            carousel.Build(m_arts);

            m_built.Clear();
            m_built.AddRange(m_display);
        }

        // 튜토리얼 중엔 페이지가 하나뿐이라 넘길 것도 없지만, 잠금을 명시해 화살표·드래그를 함께 죽인다.
        carousel.SetInteractable(!m_forced && OutgameFeatureLock.IsUnlocked(EOutgameFeature.PackCarousel));
        m_index = carousel.Index;
    }

    // 캐러셀 실물 페이지 수까지 함께 본다 — 캐시만 믿으면 실물과 어긋난 상태를 그대로 통과시킨다.
    bool SameAsBuilt(int _pageCount)
    {
        if (_pageCount != m_display.Count) return false;
        if (m_built.Count != m_display.Count) return false;

        for (int t_i = 0; t_i < m_built.Count; t_i++)
            if (m_built[t_i] != m_display[t_i]) return false;

        return true;
    }

    // 구매 대상 해석. 캐러셀이 가리키는 페이지가 곧 결제 대상이다.
    void ResolvePack(out CardPackData _pack, out long _refundGold)
    {
        _pack = m_index >= 0 && m_index < m_display.Count ? m_display[m_index] : null;
        _refundGold = m_forced ? m_forcedRefund : duplicateRefundGold;
    }

    // 팩 미할당·잔액 부족이면 구매 잠금. 잔액을 버튼 상태로 드러내면 실패 팝업을 볼 일이 없고,
    // 튜토리얼 게이트도 이 상태를 보고 딤을 자동으로 걷어 유저가 골드를 벌러 나갈 수 있다(소프트락 방지).
    void RefreshBuyLock()
    {
        if (buyButton == null) return;

        ResolvePack(out var t_pack, out _);
        buyButton.interactable = t_pack != null
                              && PackOpenOverlay.Instance != null
                              && OutgameFeatureLock.IsUnlocked(EOutgameFeature.PackBuy)
                              && CurrencyManager.CanAfford(t_pack.PriceType, t_pack.Price);
    }

    // 중앙 팩의 표시명·가격을 UI에 반영(참조는 전부 옵션).
    void Bind()
    {
        ResolvePack(out var t_pack, out _);

        if (packNameText != null) packNameText.text = t_pack != null ? t_pack.DisplayName : string.Empty;
        if (priceText != null) priceText.text = t_pack != null ? $"{t_pack.Price:N0}" : string.Empty;
    }

    // 구매 클릭: 성공이면 캐리어에 실어 개봉 오버레이로, 실패면 사유별 팝업(전역 1회 가드).
    void OnBuyPressed()
    {
        if (s_transitioning) return;

        ResolvePack(out var t_pack, out long t_refundGold);
        if (t_pack == null) return;

        // 열 화면이 없으면 사지 않는다 — 구매는 원자 영속이라 되돌릴 수 없고, 튜토리얼 구매 스텝이면
        // 커밋만 남고 개봉 신호가 영영 오지 않아 진행이 막힌다. 결제 앞에서 끊는 것이 유일한 안전판이다.
        if (PackOpenOverlay.Instance == null)
        {
            Debug.LogError("[PackShowcaseController] 개봉 오버레이 미배치 — 구매를 막는다(로비 씬 배선 확인).");
            return;
        }

        var t_opened = CardPackOpener.TryPurchase(t_pack, t_refundGold);
        if (t_opened != null && t_opened.Success)
        {
            s_transitioning = true;
            // 구독자가 개봉 화면이 뜨기 전에 결과를 처리할 수 있도록 먼저 알린다(튜토리얼이 이 순서를 센다).
            OnAnyPurchased?.Invoke();
            // 일반 구매 목적지는 지금 이 씬(오버레이만 닫고 제자리), 튜토리얼 없음(첫실행 경로와 구분).
            PackHandoff.Set(t_opened, t_pack, SceneManager.GetActiveScene().name, false);

            // 개봉 화면은 구매 임팩트가 화면을 플래시로 덮은 순간에 연다 — 그래야 전환 프레임이 드러나지 않는다.
            // 연출을 세우지 못하면 예전처럼 즉시 연다(연출은 있으면 좋은 것이지, 개봉의 조건이 아니다).
            m_openPending = true;
            if (PackPurchaseImpact.TryGet(this, out var t_impact)) t_impact.Play(ResolvePackRect(), purchaseFlash, OpenOverlay);
            else OpenOverlay();
            return;
        }

        // 실패는 차감 없이 반환됨(TryPurchase 보장) — 사유만 안내하고 진열은 그대로 둔다.
        ShowFailPopup(t_opened != null ? t_opened.Result : (EPackOpenResult?)null);
    }

    // 개봉 화면을 연다. 임팩트가 화면을 덮은 순간 불리고, 그 전에 탭이 꺼지면 OnDisable이 대신 부른다 — 어느 쪽이든 1회.
    void OpenOverlay()
    {
        if (!m_openPending) return;
        m_openPending = false;

        if (PackOpenOverlay.TryOpen()) return;

        // 구매·소유는 이미 원자 영속됐다 — 잃는 것은 개봉 연출뿐이므로 되돌리지 않고 잠금만 푼다.
        s_transitioning = false;
        Debug.LogWarning("[PackShowcaseController] 개봉 오버레이를 열지 못함 — 카드는 지급됐으나 연출 생략(오버레이 배선 확인).");
    }

    // 임팩트가 반응시킬 팩 노드. 캐러셀이 가리키는 페이지가 곧 방금 산 팩이다(미배선이면 구매 버튼으로 폴백).
    RectTransform ResolvePackRect()
    {
        var t_page = carousel != null ? carousel.CurrentPage : null;
        if (t_page != null) return t_page;

        return buyButton != null ? (RectTransform)buyButton.transform : null;
    }

    // 실패 사유를 사용자 메시지로 갈라 SimpleYNPopup 표시(LobbyMatchLauncher 팝업 관용구).
    void ShowFailPopup(EPackOpenResult? _result)
    {
        // 잔액 부족 문구는 그 팩의 결제 재화를 따라간다(팩마다 다를 수 있다).
        ResolvePack(out var t_pack, out _);
        string t_currency = t_pack != null && t_pack.PriceType == ECurrencyType.Diamond ? "다이아" : "골드";

        string t_message = _result == EPackOpenResult.InsufficientGold
            ? $"{t_currency}가 부족합니다."
            : "구매할 수 없습니다.";

        UIPoolManager.instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = t_message,
            yesText   = "확인",
            noText    = "닫기",
        });
    }
}
