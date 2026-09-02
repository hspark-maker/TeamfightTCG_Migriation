using UnityEngine;

// 디버그 재화 버튼 어댑터. 인스펙터 Button OnClick 에 Grant* 를 연결한다.
// 빌드 종류로 가리지 않는다 — 출시 전까지는 라이브 빌드에서도 손으로 확인할 수단이 필요하다.
// 출시 직전에 DebugOnlyObject 를 DebugPanel 에 붙여 한 번에 끈다.
public class DebugCurrencyButton : MonoBehaviour
{
    public void GrantGold()    => OutgameDebugActions.GrantGold();

    public void GrantDiamond() => OutgameDebugActions.GrantDiamond();

    public void GrantEnergy()  => OutgameDebugActions.GrantEnergy();

    public void GrantShard()   => OutgameDebugActions.GrantShard();
}
