using UnityEngine;
using UnityEngine.UI;

// 튜토리얼 타깃 표식 — 오브젝트 수명주기(OnEnable/OnDisable)가 그대로 등록/해제가 된다
[RequireComponent(typeof(RectTransform))]
public class TutorialAnchor : MonoBehaviour
{
    [Tooltip("이 위젯이 대응하는 튜토리얼 앵커 키. None이면 등록하지 않는다.")]
    [SerializeField] EOutgameTutorialAnchor key;

    RectTransform m_rect;
    Button m_button;

    void Awake()
    {
        m_rect = (RectTransform)transform;
        m_button = GetComponent<Button>();
    }

    void OnEnable() => TutorialAnchorRegistry.Register(key, m_rect, m_button);

    // 키가 아니라 자기 자신을 넘긴다 — 같은 키를 공유하는 다른 화면의 등록을 날리지 않게
    void OnDisable() => TutorialAnchorRegistry.Unregister(key, m_rect);
}
