using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 매치 진입 직전 화면(MatchDeckPanel.prefab 루트에 부착).
// 책임은 둘뿐이다 — 양쪽 6칸을 각자의 덱으로 그리는 것, 하단 3버튼을 셸에 잇는 것.
// 어떤 저장 슬롯이 선택됐는지는 이 뷰가 아니라 셸(MatchDeckShell)이 안다 — 여기는 상태를 들지 않는 순수 렌더러다.
// 덱 데이터의 진실원은 내 쪽이 DeckSaveManager, 상대 쪽이 DeckConfig.EnemyDeck이고
// 이 뷰는 매 Render마다 거기서 다시 읽는다(사본을 캐시하지 않는다).
// 상대 덱을 "무엇으로 확정할지"는 여기서 정하지 않는다 — 게이트를 열기 전에
// LobbyMatchLauncher.ConfirmEnemyDeck이 확정해 캐리어에 실어둔다(전투가 소비하는 값과 동일).
public class MatchDeckPanelView : MonoBehaviour
{
    [SerializeField] MatchDeckShell   shell;
    [SerializeField] CardVisualView[] mySlots;      // 6칸. MySlot_N 자신이 아니라 자식 MySlot_N/CardUIView를 물린다
    [SerializeField] CardVisualView[] enemySlots;   // 6칸. 같은 규약 — EnemySlot_N/CardUIView를 물린다
    [SerializeField] TMP_Text         myPowerText;      // MyInfoBar/PowerBadge/PowerText
    [SerializeField] TMP_Text         enemyPowerText;   // EnemyInfoBar/PowerBadge/PowerText
    [SerializeField] DeckSynergyStrip mySynergyStrip;      // MyInfoBar 쪽 시너지 줄
    [SerializeField] DeckSynergyStrip enemySynergyStrip;   // EnemyInfoBar 쪽 시너지 줄
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

    // 양쪽 섹션을 한 번에 그린다.
    // OnEnable에서 자동 호출하지 않는 이유: 어느 슬롯이 선택됐는지는 셸만 안다.
    // 패널이 켜질 때마다 뷰가 스스로 그리면 슬롯을 모른 채 0번이나 직전 값으로 그리게 된다 → 셸이 명시적으로 부른다.
    public void Render(int _slotIndex)
    {
        // 한쪽이 미배선이어도 다른 쪽은 그려야 한다 → 여기서 조기 반환하지 않고 각 렌더러가 알아서 건너뛴다.
        RenderMySlots(_slotIndex);
        RenderEnemySlots();
    }

    // 지정 저장 슬롯의 덱을 MySection 6칸에 그린다.
    void RenderMySlots(int _slotIndex)
    {
        // 슬롯 -1(미선택)·불완전 덱은 모두 여기서 걸린다 — IsSlotValid가 범위와 6장 완성을 함께 판정한다.
        bool t_valid = _slotIndex >= 0 && _slotIndex < DeckSaveManager.SLOT_COUNT && DeckSaveManager.IsSlotValid(_slotIndex);

        // 유효한 덱이 없으면 전투를 시작할 수 없다. 표시용 차단이고, 실제 방어는 Confirm 안의 재검사다.
        // 칸이 미배선이어도 이 판정만은 해야 한다 → 버튼 갱신을 칸 렌더보다 앞에 둔다.
        if (battleButton != null) battleButton.interactable = t_valid;

        List<CardData> t_deck = t_valid ? DeckSaveManager.GetSlot(_slotIndex) : null;

        // _owned는 항상 true다. 매치 화면에 올라오는 건 이미 편성된 소유 카드뿐이라 잠금 표시가 뜨면 안 된다.
        BindSlots(mySlots, t_deck, _applyGrowth: true);   // 내 덱이라 강화 반영 체력으로 그린다
        SetPower(myPowerText, t_deck, _applyGrowth: true);
        // 강화·진화는 시너지 소속을 바꾸지 않으므로 칸/파워와 달리 성장 플래그가 필요 없다.
        mySynergyStrip?.Refresh(t_deck);
    }

    // 상대 덱을 EnemySection 6칸에 그린다. 상대는 저장 슬롯이 아니라 씬 캐리어에서 온다.
    // 캐리어가 비어 있으면(=호스트가 확정하지 못한 경우) 전 칸이 빈 칸으로 접힌다.
    // 멀티는 상대 덱이 이 화면보다 늦게(배틀 씬의 SyncInitialDecks) 도착하므로 애초에 이 화면을 열지 않는다.
    void RenderEnemySlots()
    {
        // 표시용 공개이므로 _owned는 내 쪽과 같은 true — 상대 카드를 잠금 실루엣으로 가리지 않는다.
        // 강화는 끈다 — 내 성장은 상대 카드에 붙지 않고, 전투도 AI 적은 마스터 스탯 그대로 쓴다(GameInitializer).
        BindSlots(enemySlots, DeckConfig.EnemyDeck, _applyGrowth: false);
        // 파워 합도 같이 꺼야 한다 — 칸은 마스터 값, 배지만 강화 합이면 6칸 합계와 배지가 어긋난다.
        SetPower(enemyPowerText, DeckConfig.EnemyDeck, _applyGrowth: false);
        // 캐리어가 비어 있으면(=상대 덱 미확정) Refresh(null)이 전 아이콘을 접는다.
        enemySynergyStrip?.Refresh(DeckConfig.EnemyDeck);
    }

    // 덱 파워 표기. 환산식은 DeckPower가 단일 진실원이다(편성 화면의 자동 편성 정렬과 같은 식).
    // 덱이 null이면 0이 찍힌다 — 미선택·불완전 덱을 빈칸이 아니라 0으로 보이게 하는 게 의도다.
    static void SetPower(TMP_Text _text, List<CardData> _deck, bool _applyGrowth)
    {
        if (_text == null) return;   // 미배선 필드는 조용히 건너뛴다

        _text.text = DeckPower.Of(_deck, _applyGrowth).ToString();
    }

    // 칸 배열 하나를 덱으로 채운다. 덱이 짧거나 null이면 남는 칸은 빈 칸이 된다.
    static void BindSlots(CardVisualView[] _slots, List<CardData> _deck, bool _applyGrowth)
    {
        // 미배선 배열은 조용히 건너뛴다 — 부분 배선으로 축소 화면을 만드는 게 이 프로젝트 UI의 관례다.
        if (_slots == null) return;

        int t_count = _deck != null ? Mathf.Min(_slots.Length, _deck.Count) : 0;

        for (int t_i = 0; t_i < _slots.Length; t_i++)
        {
            if (_slots[t_i] == null) continue;

            // Bind(null, ...)이면 CardVisualView가 스스로 gameObject.SetActive(false)로 빈 칸을 숨긴다
            // (CardVisualView.Bind 진입부) → 여기서 빈 칸 숨김/복구를 따로 처리하지 않는다.
            // 카드를 다시 넘기면 같은 자리에서 SetActive(true)로 되살아난다.
            _slots[t_i].Bind(t_i < t_count ? _deck[t_i] : null, true, _applyGrowth);
        }
    }

    void OnEditClicked()
    {
        if (shell != null) shell.OpenEditor();
    }

    // 화면 닫기. 이 오버레이는 로비 위에 떠 있어 닫으면 로비로 돌아갈 뿐이라 확인을 받지 않는다
    // (덱 편집과 달리 잃을 편집분도 없다). 실제로 어디에 남을지는 셸을 await 하는 호스트가 정한다.
    void OnBackClicked()
    {
        if (shell != null) shell.Cancel();
    }

    void OnBattleClicked()
    {
        if (shell != null) shell.Confirm();
    }
}
