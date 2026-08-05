using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeckPileUI : MonoBehaviour
{
    // 열림 연출: 덱 버튼이 있는 화면 구석에서 자라난다. 내 덱은 오른쪽 아래, 상대 덱은 왼쪽 위 —
    // 어느 덱을 열었는지가 방향만으로 읽힌다. 배경(dim)은 이 연출에서 빠진다(panel 자신이라 커지면 안 된다).
    const float OpenScaleFrom = 0.35f;
    const float OpenTime      = 0.24f;
    // 닫힘은 열림보다 짧다 — 되돌아가는 몸짓이라 같은 시간을 쓰면 굼떠 보이고,
    // 자동공격이 닫는 경우 뒤따르는 전투 연출을 오래 가린다.
    const float CloseTime     = 0.16f;
    static readonly Vector2 PivotMine    = new Vector2(1f, 0f);   // 우하단
    static readonly Vector2 PivotOpponent = new Vector2(0f, 1f);  // 좌상단

    [SerializeField] BattleField field;
    [SerializeField] TMP_Text countText;
    [SerializeField] Button deckButton;
    [SerializeField] GameObject panel;
    [SerializeField] Transform cardListRoot;

    [Tooltip("목록+제목을 감싼 컨테이너. 배경(dim)을 빼고 이것만 확대 연출한다")]
    [SerializeField] RectTransform contentRoot;

    [Tooltip("패널 배경 버튼. 누르면 닫힌다. 목록은 이 버튼의 자식이라 목록 위 클릭은 닫히지 않는다")]
    [SerializeField] Button backgroundCloseButton;

    [Header("Player")]
    [SerializeField] CardElement cardElementPrefab;

    [Header("Enemy")]
    [SerializeField] GameObject faceDownEntryPrefab;

    static DeckPileUI currentOpen;

    readonly List<CardElement> cardElementPool = new List<CardElement>();
    readonly List<GameObject> faceDownPool = new List<GameObject>();

    bool panelOpen;

    // 닫힘 연출 동안 페이드 + 클릭 차단을 한 곳에서 맡는다. 씬 저작을 강제하지 않으려고
    // 없으면 여기서 붙인다 — 자식을 만드는 게 아니라 패널 자신에 다는 부품이라 배선 대상이 아니다.
    CanvasGroup panelGroup;

    void Start()
    {
        this.deckButton.onClick.AddListener(Toggle);
        // 배경(패널 루트) 클릭 = 닫기. 목록·카드는 이 버튼의 자식이라 그쪽 클릭은 여기까지 내려오지 않는다.
        if (this.backgroundCloseButton != null) this.backgroundCloseButton.onClick.AddListener(Close);

        if (this.panel != null)
        {
            this.panelGroup = this.panel.GetComponent<CanvasGroup>();
            if (this.panelGroup == null) this.panelGroup = this.panel.AddComponent<CanvasGroup>();
        }
    }

    public void Refresh()
    {
        if (this.countText != null)
            this.countText.text = this.field.WaitingCount.ToString();
    }

    /// <summary>열려 있는 덱 패널을 닫는다. 생각시간 초과 자동공격처럼 <b>플레이어 조작 없이</b> 판이 진행될 때
    /// 불러 준다 — 안 닫으면 공격 연출이 패널 뒤에서 돌아 무슨 일이 일어났는지 안 보인다.</summary>
    public static void CloseAny() => currentOpen?.Close();

    void Toggle()
    {
        if (this.panelOpen)
        {
            Close();
        }
        else
        {
            if (currentOpen != null && currentOpen != this)
                currentOpen.Close();
            Open();
        }
    }

    void Open()
    {
        this.panelOpen = true;
        // 닫히는 중에 다시 열면 그 연출을 끊고 기준 상태로 되돌린다 — 반쯤 사라진 채로 열리지 않게.
        CancelCloseVisual();
        this.panel.SetActive(true);
        // 카드 조작만 막는다. InputAllowed를 끄면 닫을 때 false→true 엣지가 생겨
        // 생각시간 타이머가 예산을 리셋한다(덱을 열었다 닫으면 시간이 만땅으로 돌아가던 버그).
        TurnState.UiBlocking = true;
        currentOpen = this;
        PopulateList();
        PlayOpenGrow();
    }

    /// <summary>덱 버튼 쪽 구석에서 자라나는 열림 연출.
    /// 컨테이너가 stretch(전체화면)라 pivot은 배치에 영향이 없다 — 오직 확대 기준점으로만 쓴다.
    /// 목록을 채운 **뒤** 걸어야 한다: 레이아웃이 자리를 잡으며 스케일을 덮지 않게.</summary>
    void PlayOpenGrow()
    {
        if (this.contentRoot == null) return;

        bool t_mine = this.field != null && this.field.OwnerIndex == TurnState.LocalOwnerIndex;
        this.contentRoot.pivot = t_mine ? PivotMine : PivotOpponent;

        this.contentRoot.DOKill();
        this.contentRoot.localScale = Vector3.one * OpenScaleFrom;
        this.contentRoot.DOScale(1f, OpenTime).SetEase(Ease.OutBack, 1.4f)
            .SetLink(this.contentRoot.gameObject);
    }

    /// <summary>닫기. <b>상태는 즉시</b> 닫히고 그림만 뒤따라 줄어든다 —
    /// 자동공격 진행 중 <see cref="CloseAny"/>로도 불리므로 판 진행이 연출을 기다리면 안 된다.
    /// 연출이 도는 동안에도 UiBlocking은 이미 풀렸고 클릭은 막혀 있어(패널 CanvasGroup) 조작과 어긋나지 않는다.</summary>
    void Close()
    {
        this.panelOpen = false;
        TurnState.UiBlocking = false;
        UIPoolManager.Instance?.HideUI<PooledCardElement>();   // 카드 정보창이 떠 있으면 같이 정리
        if (currentOpen == this) currentOpen = null;

        PlayCloseShrink();
    }

    /// <summary>열림의 역재생 — 열 때 정한 구석(pivot)으로 도로 빨려 들어가며 사라진다.
    /// pivot을 여기서 다시 정하지 마라. 열 때와 닫을 때 기준점이 갈린다.</summary>
    void PlayCloseShrink()
    {
        if (this.contentRoot == null || !this.panel.activeSelf)
        {
            FinishClose();
            return;
        }

        // 사라지는 중인 패널이 클릭을 먹지 않게. 알파는 트윈이 0으로 가져간다.
        if (this.panelGroup != null)
        {
            this.panelGroup.DOKill();
            this.panelGroup.blocksRaycasts = false;
            this.panelGroup.DOFade(0f, CloseTime).SetLink(this.panel);
        }

        this.contentRoot.DOKill();
        this.contentRoot.DOScale(OpenScaleFrom, CloseTime).SetEase(Ease.InBack, 1.2f)
            .SetLink(this.contentRoot.gameObject)
            .OnComplete(FinishClose);
    }

    /// <summary>연출이 끝난(또는 연출을 걸 수 없는) 시점의 실제 끄기 + 기준 상태 복구.
    /// 다음 열림이 스케일·알파를 다시 잡지만, 껐다 켜는 사이 한 프레임이라도 잔상이 보이지 않게 여기서 되돌린다.</summary>
    void FinishClose()
    {
        this.panel.SetActive(false);
        if (this.contentRoot != null) this.contentRoot.localScale = Vector3.one;
        if (this.panelGroup != null)
        {
            this.panelGroup.alpha          = 1f;
            this.panelGroup.blocksRaycasts = true;
        }
    }

    /// <summary>닫힘 연출을 도중에 취소한다(다시 열릴 때). 트윈만 끊고 값은 기준으로 되돌린다 —
    /// 완료 콜백을 태우면 <see cref="FinishClose"/>가 방금 켠 패널을 도로 꺼버린다.</summary>
    void CancelCloseVisual()
    {
        this.contentRoot?.DOKill();
        if (this.panelGroup == null) return;

        this.panelGroup.DOKill();
        this.panelGroup.alpha          = 1f;
        this.panelGroup.blocksRaycasts = true;
    }


    /// <summary>목록 카드를 <b>누르고 있는 동안</b>의 상세. 전투 중 롱프레스와 **같은 창**(PooledCardElement)을 쓴다 —
    /// 여기서 별도 상세 UI를 만들면 카드 정보를 보는 방법이 두 벌이 된다.
    /// 시너지 활성 여부는 이 필드의 확정 스냅샷을 그대로 넘긴다(재계산 금지).</summary>
    void ShowCardDetail(CardData _card)
    {
        if (_card == null) return;
        UIPoolManager.Instance?.AddOrUpdateUI<PooledCardElement>(new PooledCardElementData
        {
            card    = _card,
            synergy = this.field != null ? this.field.Synergy : null,
        });
    }

    void HideCardDetail() => UIPoolManager.Instance?.HideUI<PooledCardElement>();

    void PopulateList()
    {
        foreach (CardElement t_e in this.cardElementPool) t_e.gameObject.SetActive(false);
        foreach (GameObject t_e in this.faceDownPool) t_e.SetActive(false);

        int t_ceIdx = 0;
        int t_fdIdx = 0;

        foreach (CardInstance t_card in this.field.GetWaitingCards())
        {
            bool t_showCard = this.field.OwnerIndex == TurnState.LocalOwnerIndex || t_card.wasEverRevealed;

            if (t_showCard)
            {
                CardElement t_entry;
                if (t_ceIdx < this.cardElementPool.Count)
                {
                    t_entry = this.cardElementPool[t_ceIdx];
                    t_entry.gameObject.SetActive(true);
                }
                else
                {
                    t_entry = Instantiate(this.cardElementPrefab, this.cardListRoot);
                    this.cardElementPool.Add(t_entry);
                }
                t_entry.Init(t_card, CardElementMod.Full);

                // 공개된 카드는 **누르고 있는 동안** 상세가 뜬다(떼면 사라진다).
                // 콜백은 대입이라 풀에서 재사용돼도 중복되지 않는다.
                t_entry.SetInteractable(true, false);
                t_entry.onPressStart = ShowCardDetail;
                t_entry.onPressEnd   = HideCardDetail;
                t_ceIdx++;
            }
            else
            {
                GameObject t_entry;
                if (t_fdIdx < this.faceDownPool.Count)
                {
                    t_entry = this.faceDownPool[t_fdIdx];
                    t_entry.SetActive(true);
                }
                else
                {
                    t_entry = Instantiate(this.faceDownEntryPrefab, this.cardListRoot);
                    this.faceDownPool.Add(t_entry);
                }
                t_fdIdx++;
            }
        }
    }
}
