using System.Collections.Generic;
using UnityEngine;

// 캐러셀의 페이지 인디케이터(점). Layer Lab PageNavi 프리팹에는 제어 스크립트가 없고
// 자식 Off/Off(1)/Off(2)/Focus가 정적으로 박혀 있을 뿐이라, "점 개수 = 페이지 개수"를 맞추는 최소한의 제어만 여기서 쥔다.
//
// 점 하나 = dotTemplate 복제이고, 켜짐 표시는 그 자식 Focus의 활성 여부다.
// 프리팹이 이미 그 구조이므로 스프라이트 교체·색 보간 같은 새 규칙을 만들지 않는다 — 아트 저작이 진실원.
//
// 이 컴포넌트는 팩도 캐러셀도 모른다 — "몇 개 중 몇 번째"만 안다.
// 구동은 PackCarouselView가 한다(점은 도메인 내용이 0이라 구매를 쥔 컨트롤러에 올리면 경계 오염).
public class PackCarouselDotsView : MonoBehaviour
{
    [Tooltip("HorizontalLayoutGroup이 붙은 점 컨테이너. 간격·정렬은 레이아웃이 정한다.")]
    [SerializeField] RectTransform dotRoot;
    [Tooltip("비활성 원본 점. 자식 Focus가 켜짐 표시.")]
    [SerializeField] RectTransform dotTemplate;
    [SerializeField] string focusChildName = "Focus";
    [Tooltip("페이지가 하나뿐일 때(튜토리얼 고정) 점 하나는 정보가 아니라 노이즈다.")]
    [SerializeField] bool hideWhenSingle = true;

    readonly List<GameObject> m_dots = new List<GameObject>();
    readonly List<GameObject> m_focus = new List<GameObject>();   // m_dots와 인덱스가 나란한 병렬 캐시.

    int m_index = -1;

    // 점 개수를 맞춘다. 남는 건 지우고 모자란 만큼만 만든다 — 매번 전부 파괴하면 레이아웃이 한 프레임 튄다.
    public void Rebuild(int _count)
    {
        if (dotTemplate == null) return;

        dotTemplate.gameObject.SetActive(false);

        int t_want = Mathf.Max(0, _count);

        for (int t_i = m_dots.Count - 1; t_i >= t_want; t_i--)
        {
            // Destroy는 프레임 끝에 실행된다 — 먼저 꺼야 남은 점이 레이아웃 자리를 한 프레임 더 차지하지 않는다.
            if (m_dots[t_i] != null)
            {
                m_dots[t_i].SetActive(false);
                Destroy(m_dots[t_i]);
            }
            m_dots.RemoveAt(t_i);
            m_focus.RemoveAt(t_i);
        }

        var t_parent = dotRoot != null ? dotRoot : (RectTransform)dotTemplate.parent;
        while (m_dots.Count < t_want)
        {
            var t_dot = Instantiate(dotTemplate, t_parent);
            t_dot.gameObject.SetActive(true);
            t_dot.name = $"Dot_{m_dots.Count}";

            var t_focus = t_dot.Find(focusChildName);
            if (t_focus == null)
                Debug.LogWarning($"[PackCarouselDotsView] dotTemplate에 '{focusChildName}' 자식이 없다 — 선택 표시가 뜨지 않는다.", this);

            m_dots.Add(t_dot.gameObject);
            m_focus.Add(t_focus != null ? t_focus.gameObject : null);
        }

        gameObject.SetActive(!(hideWhenSingle && t_want <= 1));

        m_index = -1;   // 개수가 바뀌면 이전 선택은 의미가 없다.
    }

    // _index 점만 켠다. 범위 밖이면 전부 끈다.
    public void SetIndex(int _index)
    {
        if (m_index == _index) return;
        m_index = _index;

        for (int t_i = 0; t_i < m_focus.Count; t_i++)
            if (m_focus[t_i] != null) m_focus[t_i].SetActive(t_i == _index);
    }
}
