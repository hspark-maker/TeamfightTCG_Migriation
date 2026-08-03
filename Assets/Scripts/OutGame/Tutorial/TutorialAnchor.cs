using UnityEngine;
using UnityEngine.UI;

// 튜토리얼 타깃 표식. 오브젝트 수명주기(OnEnable/OnDisable)가 그대로 등록/해제가 된다 —
// 탭 전환·개봉 완료 노출 같은 "타깃이 나타나는 시점"을 감지하는 별도 코드가 필요 없다.
[RequireComponent(typeof(RectTransform))]
public class TutorialAnchor : MonoBehaviour
{
    [Tooltip("이 위젯이 대응하는 튜토리얼 앵커 키. None이면 등록하지 않는다.")]
    [SerializeField] EOutgameTutorialAnchor key;

    RectTransform m_rect;
    Button m_button;   // 없어도 된다(클릭 대상이 아닌 하이라이트 전용 타깃).

    void Awake()
    {
        m_rect = (RectTransform)transform;
        m_button = GetComponent<Button>();
    }

    void OnEnable() => TutorialAnchorRegistry.Register(key, m_rect, m_button);

    // 키가 아니라 자기 자신을 넘긴다 — 같은 키를 공유하는 다른 화면이 이미 등록을 가져갔다면 건드리면 안 된다.
    void OnDisable() => TutorialAnchorRegistry.Unregister(key, m_rect);
}
