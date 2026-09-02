using UnityEngine;

// 디버그 재화 버튼 어댑터. 인스펙터 Button OnClick 에 Grant* 를 연결한다.
// 클래스 자체는 릴리스에도 컴파일된다 — 프리팹에 붙어 있어 지우면 "missing script" 가 되기 때문이다.
// 대신 릴리스에서는 Awake 가 오브젝트를 끄고 본문도 비어 있어 아무 일도 하지 않는다.
public class DebugCurrencyButton : MonoBehaviour
{
    void Awake()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        this.gameObject.SetActive(false);
#endif
    }

    public void GrantGold()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        OutgameDebugActions.GrantGold();
#endif
    }

    public void GrantDiamond()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        OutgameDebugActions.GrantDiamond();
#endif
    }

    public void GrantEnergy()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        OutgameDebugActions.GrantEnergy();
#endif
    }

    public void GrantShard()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        OutgameDebugActions.GrantShard();
#endif
    }
}
