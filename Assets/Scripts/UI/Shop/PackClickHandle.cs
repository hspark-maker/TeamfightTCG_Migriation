using UnityEngine;

// 3D 카드팩 모델(CardPack.prefab 루트, BoxCollider 필요)의 "클릭 1회" 인터랙션.
// 순수 인터랙션 뷰 — 구매·개봉·소유·덱을 모른다. 클릭 확정 시 콜백만 1회 발화한다.
public class PackClickHandle : MonoBehaviour
{
    // 클릭 확정 콜백(PackRevealView가 다음 단계로 이어받음).
    System.Action m_onClicked;

    bool m_armed;
    bool m_clicked;

    /// <summary>클릭 입력을 켜고 콜백을 등록한다. 뷰가 팩을 보인 뒤 호출.</summary>
    public void Arm(System.Action _onClicked)
    {
        m_onClicked = _onClicked;
        m_armed = true;
        m_clicked = false;
    }

    /// <summary>클릭 입력을 내리고 콜백을 버린다(뷰 비활성 시 — 다음 Arm으로 다시 켠다).</summary>
    public void Disarm()
    {
        m_armed = false;
        m_onClicked = null;
    }

    // OnMouse* 는 Collider + 씬 카메라만으로 동작(Camera.main 직접 참조 없음).
    void OnMouseUpAsButton()
    {
        if (!m_armed || m_clicked) return;

        // 중복 클릭 가드: 발화 전에 입력을 잠근다.
        m_clicked = true;
        m_armed = false;

        var t_cb = m_onClicked;
        m_onClicked = null;   // 뷰 참조를 붙들지 않는다.
        t_cb?.Invoke();
    }
}
