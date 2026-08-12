using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("가격 옆 재화 아이콘. 아래 두 스프라이트를 모두 채워야 전환이 돈다(한쪽만 비면 프리팹 그림 그대로).")]
    [SerializeField] Image retryPriceIcon;
    [Tooltip("골드 결제 팩에 쓸 아이콘.")]
    [SerializeField] Sprite goldIcon;
    [Tooltip("다이아 결제 팩에 쓸 아이콘. 그 외 재화는 골드 아이콘을 쓴다.")]
    [SerializeField] Sprite diamondIcon;
    [Tooltip("못 누르는 동안 버튼 밑판에 까는 무채색 그림. 이걸 쓰려면 Button의 Disabled Color를 불투명 흰색으로 " +
             "둘 것 — 틴트가 남아 있으면 무채색 위에 한 번 더 곱해진다. 미배선이면 그림은 건드리지 않는다.")]
    [SerializeField] Sprite disabledPlate;
    [Tooltip("모자란 가격 숫자의 색. 무채색 밑판이 '못 누른다'를 말하고 이 색이 '어디가 모자란가'를 말한다.")]
    [SerializeField] Color shortPriceColor  = new Color(0.95f, 0.30f, 0.28f, 1f);
    [SerializeField] Color normalPriceColor = Color.white;

    // 캐리어에서 받은 목적지 컨텍스트(Start에서 캡처, 이후 재판정 안 함).
    string m_nextScene;
    bool m_startTutorial;

    // 방금 연 팩. 캐리어는 BeginSession에서 Consume돼 비므로, 되사려면 여기 쥐고 있어야 한다.
    CardPackData m_pack;

    // 이번 개봉에서 처음 얻은 카드만(로비 도감 연출 대상). Consume은 1회뿐이라 여기 캐시한다.
    // 중복은 팩 결제 재화로 환급돼 코인 연출로 나가므로 도감 연출거리가 없다.
    readonly List<CardData> m_newCards = new List<CardData>();
    // 이번 개봉의 중복 환급 합계(연출 표시용). 카드와 같은 시점에 캐시해야 짝이 갈라지지 않는다.
    CurrencyGain m_refund;

    // 획득이 2회 이상 눌려도 씬 전이는 1회만.
    bool m_left;

    // 저작된 유채색 밑판. 무채색으로 갈아끼우기 전에 한 번만 잡아 둔다 — 안 그러면 되돌아갈 자리를 잃는다.
    Sprite m_retryPlate;

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
            // 정상 진입은 상점/부트가 캐리어를 채우고 오버레이를 연 경우뿐. 그 외는 열 팩이 없음.
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
        m_pack = PackHandoff.Pack;
        var t_opened = PackHandoff.Consume();
        CacheCards(t_opened);
        BindRetryPrice();

        view.BeginOpen(t_opened, m_pack);
        return true;
    }

    void OnEnable()
    {
        // 저작된 밑판은 갈아끼우기 전에 잡는다. Awake에 두지 않는 이유 — 오버레이가 시작하자마자 content를
        // 끄므로 이 컴포넌트의 Awake는 돌지 않을 수 있다. 열릴 때 반드시 도는 곳은 여기다.
        if (m_retryPlate == null && retryButton != null && retryButton.image != null)
            m_retryPlate = retryButton.image.sprite;

        if (view != null) view.OnRevealComplete += OnRevealComplete;
        if (acquireButton != null) acquireButton.onClick.AddListener(OnAcquirePressed);
        if (retryButton != null) retryButton.onClick.AddListener(OnRetryPressed);
    }

    void OnDisable()
    {
        if (view != null) view.OnRevealComplete -= OnRevealComplete;
        if (acquireButton != null) acquireButton.onClick.RemoveListener(OnAcquirePressed);
        if (retryButton != null) retryButton.onClick.RemoveListener(OnRetryPressed);
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
            var t_card = t_drawn[t_i].Card;
            if (t_card == null || !t_drawn[t_i].IsNew) continue;

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
    }

    // 살 수 없으면 잠근다(잔액을 버튼 상태로 드러내면 실패 팝업을 볼 일이 없다 — 상점 RefreshBuyLock과 같은 방침).
    // 튜토리얼 중에도 잠근다: 재개봉은 PackRevealView.OnAnyPackOpened를 한 번 더 쏘므로,
    //   "오버레이 1회 열림 : 개봉신호 1회"를 전제로 세는 튜토리얼 스텝이 어긋난다. 이 잠금이 그 유일한 방어다.
    // 잔액 변동을 구독하지 않는 이유 — 환급까지 TryPurchase 안에서 이미 끝나 있고, 결과 화면이 떠 있는 동안
    //   잔액을 움직이는 것은 이 버튼 자신뿐이다. 그 직후 새 세션의 이 함수가 다시 판정한다.
    void RefreshRetryLock()
    {
        if (retryButton == null) return;

        bool t_allowed = m_pack != null
                      && !m_left
                      && !OutgameTutorialRunner.IsRunning
                      && OutgameFeatureLock.IsUnlocked(EOutgameFeature.PackBuy);
        bool t_afford = m_pack != null && CurrencyManager.CanAfford(m_pack.PriceType, m_pack.Price);

        retryButton.interactable = t_allowed && t_afford;

        // 못 누르는 동안은 무채색 밑판이 깔린다 — 알파를 낮추면 어두운 개봉 화면에선 그냥 사라진 것으로 읽힌다.
        // 밑판 둘 중 하나라도 없으면 그림은 건드리지 않는다(되돌아갈 자리를 잃느니 틴트만 남는 편이 낫다).
        if (retryButton.image != null && disabledPlate != null && m_retryPlate != null)
            retryButton.image.sprite = retryButton.interactable ? m_retryPlate : disabledPlate;

        // 빨강은 "여기가 모자란다" 한 축에만 쓴다 — 튜토리얼·기능잠금은 모자란 것이 아니다.
        if (retryPriceText != null)
            retryPriceText.color = t_allowed && !t_afford ? shortPriceColor : normalPriceColor;
    }

    // 한 번 더 버튼의 가격 표시. 세션당 한 번만 바뀐다(같은 팩을 되사므로 값이 움직이지 않는다).
    void BindRetryPrice()
    {
        if (retryPriceText != null) retryPriceText.text = m_pack != null ? $"{m_pack.Price:N0}" : string.Empty;
        if (retryPriceIcon == null) return;

        // 숫자가 비는 상태에선 아이콘도 함께 걷는다(숫자 없이 아이콘만 남는 칸 방지).
        retryPriceIcon.enabled = m_pack != null;

        // 한쪽만 배선하면 되돌아올 스프라이트가 없어 아이콘이 눌러붙는다 — 둘 다 있을 때만 바꾼다.
        if (m_pack == null || goldIcon == null || diamondIcon == null) return;

        retryPriceIcon.sprite = m_pack.PriceType == ECurrencyType.Diamond ? diamondIcon : goldIcon;
    }

    // 한 번 더 클릭: 같은 팩을 되사서 오버레이를 닫지 않고 세션만 갈아끼운다(팩 등장부터 다시).
    // 오버레이를 여닫지 않으므로 OnOpened/OnClosed는 울리지 않는다 — 상점 진열은 얼린 채로 두고,
    // 로비 획득 연출은 마지막에 닫힐 때 누적분을 한 번에 재생한다.
    // PackShowcaseController.OnAnyPurchased도 일부러 울리지 않는다: 그 신호의 유일한 구독자가 튜토리얼이고,
    //   튜토리얼 중엔 이 버튼이 잠겨 있다. 신호를 늘리면 "구매 1회 : 스텝 1칸"이 도리어 어긋난다.
    void OnRetryPressed()
    {
        if (m_left || m_pack == null || view == null) return;

        // 차감·소유·환급은 여기 한 곳에서만 일어난다(첫 구매와 같은 길). 실패면 차감 없이 돌아온다.
        var t_opened = CardPackOpener.TryPurchase(m_pack);
        if (t_opened == null || !t_opened.Success)
        {
            // 잔액 부족은 버튼이 이미 잠가 두므로 여기 오는 것은 팩 데이터 이상뿐 — 유저에게 물을 것이 없다.
            Debug.LogWarning($"[PackAcquireController] 재개봉 실패({(t_opened != null ? t_opened.Result.ToString() : "null")}) — 팩 데이터 확인.");
            RefreshRetryLock();
            return;
        }

        // 이번 세션 몫을 먼저 싣는다 — 곧 BeginSession이 캐시를 덮으므로, 여기서 놓치면
        // 직전 개봉의 신규 카드·환급이 로비 연출에서 통째로 사라진다(캐리어는 누적이라 겹쳐 실어도 된다).
        CardPackRewardHandoff.Set(m_refund, m_newCards);

        // 목적지 컨텍스트는 그대로 물려준다 — 어느 세션에서 획득을 누르든 나가는 곳은 같아야 한다.
        PackHandoff.Set(t_opened, m_pack, m_nextScene, m_startTutorial);

        view.ResetSession();   // 요약 상태를 되돌려야 BeginOpen의 재진입 가드가 풀린다.
        BeginSession();
    }

    // 획득 클릭: 튜토리얼이면 시작 → 목적지 씬으로 이동(1회 가드).
    // 개봉 카드로 덱을 만들지 않는다 — 첫 덱은 부트의 StarterDeck이 보장하고, 이후 편성은 유저 몫이다.
    void OnAcquirePressed()
    {
        if (m_left) return;
        m_left = true;
        RefreshRetryLock();   // 나가기로 한 뒤엔 되사기를 막는다(닫히는 프레임에 눌리는 것 차단).

        // 환급·카드가 같은 개봉 세션 결과라 로비 복귀 직전 한 지점에서 함께 싣는다(지급·저장은 이미 끝났다 — 표시량뿐).
        // 카드는 신규만 — 중복분은 환급 재화로 이미 표현된다. 튜토리얼 경로로 전투에 먼저 가면 이후 로비 진입 시 재생된다.
        CardPackRewardHandoff.Set(m_refund, m_newCards);

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
