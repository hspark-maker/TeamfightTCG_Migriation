using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeckPileUI : MonoBehaviour
{
    [SerializeField] BattleField field;
    [SerializeField] TMP_Text countText;
    [SerializeField] Button deckButton;
    [SerializeField] GameObject panel;
    [SerializeField] Transform cardListRoot;

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

    void Start()
    {
        this.deckButton.onClick.AddListener(Toggle);
        // 배경(패널 루트) 클릭 = 닫기. 목록·카드는 이 버튼의 자식이라 그쪽 클릭은 여기까지 내려오지 않는다.
        if (this.backgroundCloseButton != null) this.backgroundCloseButton.onClick.AddListener(Close);
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
        this.panel.SetActive(true);
        // 카드 조작만 막는다. InputAllowed를 끄면 닫을 때 false→true 엣지가 생겨
        // 생각시간 타이머가 예산을 리셋한다(덱을 열었다 닫으면 시간이 만땅으로 돌아가던 버그).
        TurnState.UiBlocking = true;
        currentOpen = this;
        PopulateList();
    }

    void Close()
    {
        this.panelOpen = false;
        this.panel.SetActive(false);
        TurnState.UiBlocking = false;
        UIPoolManager.Instance?.HideUI<PooledCardElement>();   // 카드 정보창이 떠 있으면 같이 정리
        if (currentOpen == this) currentOpen = null;
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
