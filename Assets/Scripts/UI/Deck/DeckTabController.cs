using System;
using UnityEngine;

// 덱 탭 루트(Tab_Deck에 부착). 목록/편집 두 패널의 SetActive 전환만 담당한다(씬 로드 없음).
// 탭 셸(LobbyTabController)이 단순 SetActive 토글이라 라이프사이클 훅이 없으므로,
// "탭이 켜지면 항상 목록부터"를 OnEnable로 보장한다.
public class DeckTabController : MonoBehaviour
{
    [SerializeField] GameObject         listPanel;
    [SerializeField] GameObject         editPanel;
    [SerializeField] DeckEditController editController;   // 옵션 — 미배선이면 패널 토글만 한다

    // 탭 셸(LobbyRoot)은 이 오브젝트의 상위 계층에 있다 — 인스펙터 배선 없이 첫 사용 시 찾아 캐시한다.
    LobbyTabController m_lobbyTabs;

    // 덱 탭을 단독 배치한 테스트 씬에서는 셸이 없을 수 있다 → 호출측은 항상 null을 감안한다.
    LobbyTabController LobbyTabs
        => m_lobbyTabs != null ? m_lobbyTabs : (m_lobbyTabs = GetComponentInParent<LobbyTabController>(true));

    void OnEnable()
    {
        // 편집 중 탭이 꺼졌다 켜지면 여기서 무저장 폐기된다.
        // 편집은 DeckEditController의 메모리 사본에서만 일어나고 세이브는 손대지 않으므로
        // 손실은 "이번 편집분"뿐이고 기존 덱은 온전하다 — 그래서 확인 팝업 없이 목록으로 되돌려도 안전하다.
        ShowList();
    }

    // 탭 전환이 아닌 경로(로비 캔버스 비활성·씬 전환)로 덱 탭이 꺼지면 ShowList를 거치지 않는다 →
    // 가드가 셸에 남아 이후 모든 탭 전환이 죽은 편집기에게 넘어간다.
    void OnDisable()
    {
        if (LobbyTabs != null) LobbyTabs.ClearLeaveGuard();
    }

    // 기존 덱 편집 진입. _slotIndex는 DeckSaveManager 슬롯 좌표.
    public void OpenEditor(int _slotIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= DeckSaveManager.SLOT_COUNT) return;

        ShowEditor();
        if (editController != null) editController.Open(_slotIndex);
    }

    // 신규 덱 진입. 좌표는 저장이 확정되는 순간(TryInsertFront)에 생기므로 여기서는 만석만 막는다.
    public void OpenNewDeckEditor()
    {
        if (DeckSaveManager.IsFull) return;

        ShowEditor();
        if (editController != null) editController.OpenNew();
    }

    // 편집 종료 = 편집 진입 이전 화면(덱 탭 목록)으로 복귀. 탭은 건드리지 않는다.
    public void CloseEditor()
    {
        ShowList();
    }

    // 탭 셸이 넘긴 이탈 요청. 저장 판정과 미완성 확인은 편집기가 하고(경로가 뒤로가기와 한 벌이어야 한다),
    // 허가가 떨어지면 목록으로 되돌린 뒤 유저가 원래 누른 탭으로 보낸다.
    void OnLobbyTabLeave(Action _proceed)
    {
        if (editController == null)
        {
            _proceed();

            return;
        }

        editController.RequestLeave(() =>
        {
            CloseEditor();
            _proceed();
        });
    }

    void ShowEditor()
    {
        if (listPanel != null) listPanel.SetActive(false);
        if (editPanel != null) editPanel.SetActive(true);

        // 탭 버튼은 편집 패널 위에 그대로 노출돼 있다 → 뒤로가기와 같은 확인을 거치게 가로챈다.
        if (LobbyTabs != null) LobbyTabs.SetLeaveGuard(OnLobbyTabLeave);
    }

    void ShowList()
    {
        // 가드를 먼저 내린다 — 허가 경로가 이 뒤에 원래 탭 전환을 재개하는데, 남아 있으면 그게 다시 가드로 들어온다.
        if (LobbyTabs != null) LobbyTabs.ClearLeaveGuard();

        if (editController != null) editController.Close();
        if (editPanel != null) editPanel.SetActive(false);
        if (listPanel != null) listPanel.SetActive(true);
    }
}
