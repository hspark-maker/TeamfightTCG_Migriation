using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 매치 진입 직전 화면(MatchDeckPanel.prefab 루트에 부착).
// 책임은 둘뿐이다 — MySection 6칸을 지정 슬롯의 덱으로 그리는 것, 하단 3버튼을 셸에 잇는 것.
// 어떤 저장 슬롯이 선택됐는지는 이 뷰가 아니라 셸(MatchDeckShell)이 안다 — 여기는 상태를 들지 않는 순수 렌더러다.
// 덱 데이터의 진실원은 DeckSaveManager이고 이 뷰는 매 Render마다 거기서 다시 읽는다(사본을 캐시하지 않는다).
public class MatchDeckPanelView : MonoBehaviour
{
    [SerializeField] MatchDeckShell   shell;
    [SerializeField] CardVisualView[] mySlots;      // 6칸. MySlot_N 자신이 아니라 자식 MySlot_N/CardUIView를 물린다
    [SerializeField] Button           editButton;
    [SerializeField] Button           backButton;
    [SerializeField] Button           battleButton;

    void Awake()
    {
        // 미배선 필드는 조용히 건너뛴다 — 이 프로젝트의 UI는 부분 배선으로 축소 화면을 만드는 게 관례다.
        if (editButton != null)
        {
            editButton.onClick.RemoveAllListeners();   // 프리팹에 남은 배선·중복 등록 방지
            editButton.onClick.AddListener(OnEditClicked);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackClicked);
        }

        if (battleButton != null)
        {
            battleButton.onClick.RemoveAllListeners();
            battleButton.onClick.AddListener(OnBattleClicked);
        }
    }

    // 지정 저장 슬롯의 덱을 6칸에 그린다.
    // OnEnable에서 자동 호출하지 않는 이유: 어느 슬롯이 선택됐는지는 셸만 안다.
    // 패널이 켜질 때마다 뷰가 스스로 그리면 슬롯을 모른 채 0번이나 직전 값으로 그리게 된다 → 셸이 명시적으로 부른다.
    public void Render(int _slotIndex)
    {
        if (mySlots == null) return;

        // 슬롯 -1(미선택)·불완전 덱은 모두 여기서 걸린다 — IsSlotValid가 범위와 6장 완성을 함께 판정한다.
        bool t_valid = _slotIndex >= 0 && _slotIndex < DeckSaveManager.SLOT_COUNT && DeckSaveManager.IsSlotValid(_slotIndex);

        List<CardData> t_deck  = t_valid ? DeckSaveManager.GetSlot(_slotIndex) : null;
        int            t_count = t_deck != null ? Mathf.Min(mySlots.Length, t_deck.Count) : 0;

        for (int t_i = 0; t_i < mySlots.Length; t_i++)
        {
            if (mySlots[t_i] == null) continue;

            // Bind(null, ...)이면 CardVisualView가 스스로 gameObject.SetActive(false)로 빈 칸을 숨긴다
            // (CardVisualView.Bind 진입부) → 여기서 빈 칸 숨김/복구를 따로 처리하지 않는다.
            // 카드를 다시 넘기면 같은 자리에서 SetActive(true)로 되살아난다.
            //
            // _owned는 항상 true다. 매치 화면에 올라오는 건 이미 편성된 소유 카드뿐이라 잠금 표시가 뜨면 안 된다.
            mySlots[t_i].Bind(t_i < t_count ? t_deck[t_i] : null, true);
        }

        // 유효한 덱이 없으면 전투를 시작할 수 없다. 표시용 차단이고, 실제 방어는 Confirm 안의 재검사다.
        if (battleButton != null) battleButton.interactable = t_valid;
    }

    void OnEditClicked()
    {
        if (shell != null) shell.OpenEditor();
    }

    // 전투 포기. 어디로 돌아갈지는 셸을 await 하는 호스트가 정한다 — 이 뷰는 씬을 모른다.
    void OnBackClicked()
    {
        if (shell != null) shell.Cancel();
    }

    void OnBattleClicked()
    {
        if (shell != null) shell.Confirm();
    }
}
