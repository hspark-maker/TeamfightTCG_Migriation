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
// 진열 목록(packs)은 이 뷰가 직접 소유한다 — 상점 SO 미개입(목록이 비면 구매 잠금).
//   가격·중복 환급은 팩 SO가 쥐므로 이 뷰는 아무것도 넘기지 않는다.
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
    [Tooltip("확률 고지 팝업 열기(옵션 — 미배선 무시). 지금 진열 중인 팩 기준으로 연다.")]
    [SerializeField] Button oddsButton;
    [SerializeField] TextMeshProUGUI packNameText;  // 중앙 팩 표시명(옵션 — 미배선 무시).
    [SerializeField] TextMeshProUGUI priceText;     // 가격 숫자(재화 종류는 팩마다 다르다 — 옵션, 미배선 무시).
    [Tooltip("가격 옆 재화 아이콘. 중앙 팩의 결제 재화를 따라 스프라이트가 바뀐다(옵션 — 미배선 무시).")]
    [SerializeField] Image priceIcon;
    [Tooltip("튜토리얼이 가격 자리 문구(예: \"무료\")를 저작했을 때 대신 켜지는 라벨. 그동안 위 아이콘:숫자 "
           + "쌍은 노드째로 꺼진다 — 쌍은 아이콘 자리를 비워 둔 좌표에 손으로 박혀 있어서, 아이콘만 걷으면 "
           + "문구가 그 자리를 물려받지 못하고 한쪽으로 치우친다. 가운데 정렬로 저작할 것. "
           + "미배선이면 예전처럼 숫자 칸이 문구를 대신 쓴다(치우침도 그대로).")]
    [SerializeField] TextMeshProUGUI forcedPriceText;
    [Tooltip("골드 결제 팩에 쓸 아이콘. 아래 다이아 아이콘과 둘 다 채워야 전환이 돈다(한쪽만 비면 프리팹 그림 그대로).")]
    [SerializeField] Sprite goldIcon;
    [Tooltip("다이아 결제 팩에 쓸 아이콘. 그 외 재화는 골드 아이콘을 쓴다.")]
    [SerializeField] Sprite diamondIcon;
    [Tooltip("캐러셀에 진열할 팩들. 순서가 곧 페이지 순서. 비어 있으면 구매 잠금.")]
    [SerializeField] List<CardPackData> packs = new List<CardPackData>();
    [Tooltip("좌우 넘김을 담당하는 캐러셀. 미배선이면 목록 첫 팩만 진열된다.")]
    [SerializeField] PackCarouselView carousel;

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
    bool m_forced;               // 튜토리얼이 진열을 덮어썼는가.
    string m_forcedPriceLabel;   // 그 스텝이 가격 자리에 대신 띄우라고 저작한 문구(비면 실제 가격).

    // 구매는 끝났고 개봉 화면만 아직 열지 않은 상태. 임팩트가 화면을 덮는 사이의 짧은 구간이다.
    bool m_openPending;

    void OnEnable()
    {
        s_transitioning = false;

        if (buyButton != null)
        {
            buyButton.onClick.RemoveListener(OnBuyPressed);
            buyButton.onClick.AddListener(OnBuyPressed);

            // 잠김 룩은 기능 해금 항만 본다 — RefreshBuyLock의 interactable에는 잔액 부족도 섞여 있고,
            // 그건 유저가 스스로 푸는 정상 대기라 잠김으로 그리면 안 된다(튜토리얼 게이트가 그 전제로 딤을 걷는다).
            FeatureLockView.Attach(buyButton.gameObject, EOutgameFeature.PackBuy);
        }
        if (oddsButton != null)
        {
            oddsButton.onClick.RemoveListener(OnOddsPressed);
            oddsButton.onClick.AddListener(OnOddsPressed);
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
        if (oddsButton != null) oddsButton.onClick.RemoveListener(OnOddsPressed);
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
    // 얼려 둔 진열도 여기서 푼다(Refresh 전체 — 잔액뿐 아니라 목록이 바뀌어 있을 수 있다).
    // 오버레이는 하드컷으로 사라지므로 같은 프레임에 갈아치우면 교체 자체가 보이지 않는다.
    void OnOverlayClosed()
    {
        s_transitioning = false;
        Refresh();
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
        // 구매가 확정된 뒤 개봉이 닫힐 때까지는 진열을 얼린다. 튜토리얼 구매 스텝은 구매 성공 신호로
        // 곧장 다음 칸을 커밋해 강제 진열이 풀리는데, 그 시점은 플래시가 화면을 덮기 전이라
        // 원래 상점으로 갈아치워지는 것이 그대로 보인다. 게다가 캐러셀 재구축은 페이지를 파괴하므로
        // 구매 임팩트가 잡고 있던 팩 노드까지 사라진다.
        if (s_transitioning) return;

        m_display.Clear();
        m_forced = OutgameTutorialRunner.TryGetForcedPack(out var t_forced, out m_forcedPriceLabel);

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
    CardPackData ResolvePack()
        => m_index >= 0 && m_index < m_display.Count ? m_display[m_index] : null;

    // 팩 미할당·잔액 부족이면 구매 잠금. 잔액을 버튼 상태로 드러내면 실패 팝업을 볼 일이 없고,
    // 튜토리얼 게이트도 이 상태를 보고 딤을 자동으로 걷어 유저가 골드를 벌러 나갈 수 있다(소프트락 방지).
    void RefreshBuyLock()
    {
        if (buyButton == null) return;

        var t_pack = ResolvePack();
        buyButton.interactable = t_pack != null
                              && PackOpenOverlay.Instance != null
                              && OutgameFeatureLock.IsUnlocked(EOutgameFeature.PackBuy)
                              && CurrencyManager.CanAfford(t_pack.PriceType, t_pack.Price);
    }

    // 중앙 팩의 표시명·가격·재화 아이콘을 UI에 반영(참조는 전부 옵션).
    void Bind()
    {
        var t_pack = ResolvePack();

        if (packNameText != null) packNameText.text = t_pack != null ? t_pack.DisplayName : string.Empty;

        // 튜토리얼 스텝이 가격 자리 문구를 저작했으면 숫자 대신 그 말을 띄운다(예: "무료").
        bool t_labeled = t_pack != null && m_forced && !string.IsNullOrEmpty(m_forcedPriceLabel);

        // 문구가 설 자리. 별도 라벨이 배선돼 있으면 그쪽이 정본이고, 아이콘:숫자 쌍은 통째로 물러난다 —
        // 쌍은 아이콘 자리를 비워 둔 좌표에 손으로 박혀 있어(레이아웃 그룹 없음) 아이콘만 걷으면
        // 문구가 그 자리를 물려받지 못한다. 정렬을 손보는 대신 표시 주체를 갈아끼우는 이유다.
        TMP_Text t_host = !t_labeled              ? null
                        : forcedPriceText != null ? forcedPriceText
                                                  : priceText;   // 라벨 미배선 폴백(예전 그림 그대로)

        if (forcedPriceText != null)
        {
            forcedPriceText.gameObject.SetActive(t_host == forcedPriceText);
            if (t_host == forcedPriceText) forcedPriceText.text = m_forcedPriceLabel;
        }

        // 숫자와 아이콘은 컴포넌트가 아니라 노드째로 끈다 — 문구가 그 자리에 서는 게 아니라 대신 서는 것이라,
        // 꺼진 쌍이 자리만 차지하고 있으면 라벨을 어디에 두든 화면에는 빈칸이 낀 줄로 남는다.
        if (priceText != null)
        {
            bool t_showNumber = t_pack != null && t_host == null;

            priceText.gameObject.SetActive(t_showNumber || t_host == priceText);
            if (t_showNumber)             priceText.text = $"{t_pack.Price:N0}";
            else if (t_host == priceText) priceText.text = m_forcedPriceLabel;
        }

        if (priceIcon != null)
        {
            // 가격 숫자가 비는 상태에선 아이콘도 함께 걷는다(숫자 없이 아이콘만 남는 칸 방지).
            // 문구로 갈아낀 자리도 마찬가지 — 결제 재화를 말하지 않는 표기에 재화 아이콘만 남으면 어긋난다.
            priceIcon.gameObject.SetActive(t_pack != null && !t_labeled);

            var t_icon = ResolveCurrencyIcon(t_pack);
            if (t_icon != null) priceIcon.sprite = t_icon;
        }
    }

    // 결제 재화 아이콘. 한쪽만 배선하면 되돌아올 스프라이트가 없어 아이콘이 눌러붙는다 — 둘 다 있을 때만 바꾼다.
    Sprite ResolveCurrencyIcon(CardPackData _pack)
    {
        if (_pack == null || goldIcon == null || diamondIcon == null) return null;

        return _pack.PriceType == ECurrencyType.Diamond ? diamondIcon : goldIcon;
    }

    // 확률 고지 클릭: 지금 진열 중인 팩의 등장 확률을 연다. 구매 잠금(잔액·기능잠금)과 무관하게 항상 열린다 —
    // 확률은 살 수 있는지와 별개로 사기 전에 봐야 하는 정보다.
    void OnOddsPressed()
    {
        var t_pack = ResolvePack();
        if (t_pack == null) return;

        UIPoolManager.Instance?.AddOrUpdateUI<PackOddsPopup>(new PackOddsData { pack = t_pack });
    }

    // 구매 클릭: 성공이면 캐리어에 실어 개봉 오버레이로, 실패면 사유별 팝업(전역 1회 가드).
    void OnBuyPressed()
    {
        if (s_transitioning) return;

        var t_pack = ResolvePack();
        if (t_pack == null) return;

        // 열 화면이 없으면 사지 않는다 — 구매는 원자 영속이라 되돌릴 수 없고, 튜토리얼 구매 스텝이면
        // 커밋만 남고 개봉 신호가 영영 오지 않아 진행이 막힌다. 결제 앞에서 끊는 것이 유일한 안전판이다.
        if (PackOpenOverlay.Instance == null)
        {
            Debug.LogError("[PackShowcaseController] 개봉 오버레이 미배치 — 구매를 막는다(로비 씬 배선 확인).");
            return;
        }

        var t_opened = CardPackOpener.TryPurchase(t_pack);
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
        // 열지 못하면 닫힘 신호도 오지 않는다 — 얼려 둔 진열을 여기서 함께 풀지 않으면 영영 굳는다.
        s_transitioning = false;
        Refresh();
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
        var t_pack = ResolvePack();
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
