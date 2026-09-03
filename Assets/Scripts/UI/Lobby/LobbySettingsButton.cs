using UnityEngine;
using UnityEngine.UI;

/// 로비 상단바의 메뉴 버튼 → 프로필 요약 판(LobbySettingPanel). 설정창으로 가는 길은 그 판의 버튼이 쥔다.
///
/// 버튼의 onClick 영속 호출로 배선하지 않는 이유: 이 판은 씬에 놓인 오브젝트가 아니라 풀에서 꺼내
/// 띄우는 것(UIPoolManager)이라 인스펙터에서 가리킬 대상이 없다. 로비 탭 버튼들도 같은 이유로
/// LobbyTabController가 코드에서 AddListener 한다.
///
/// 전투 전용 항목(항복·디버그 승리)은 여기서 따로 끄지 않는다 — SettingsPanel.RefreshBattleButtons가
/// TurnRunner.Instance 유무로 매번 판정하므로, TurnRunner가 없는 로비에서는 자동으로 숨는다.
/// 판정을 이쪽에도 복제하면 기준이 두 곳으로 갈라진다.
[RequireComponent(typeof(Button))]
public class LobbySettingsButton : MonoBehaviour
{
    Button m_button;

    void Awake()
    {
        this.m_button = GetComponent<Button>();
        this.m_button.onClick.AddListener(Open);
    }

    void OnDestroy()
    {
        if (this.m_button != null) this.m_button.onClick.RemoveListener(Open);
    }

    /// 이미 떠 있으면 UIPoolManager가 맨 앞으로 올리고 Show를 다시 태운다(중복 생성 없음).
    void Open() => UIPoolManager.Instance?.AddOrUpdateUI<LobbySettingPanel>();
}
