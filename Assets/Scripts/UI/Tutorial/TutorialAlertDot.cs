using System;
using UnityEngine;
using UnityEngine.UI;

// "이 탭에 아직 볼 튜토리얼이 남았다"를 가리키는 알림 점.
// 판정 근거는 TriggeredTutorialRunner.HasPending 하나뿐 — 발화 조건과 표시 조건이 갈리지 않는다.
// 잠긴 대상에는 띄우지 않는다 — 못 들어가는 곳으로 부르는 유인이 된다.
public class TutorialAlertDot : AlertDotView
{
    [Tooltip("이 점이 가리키는 트리거. None이면 아무것도 뜨지 않는다")]
    [SerializeField] EOutgameTutorialTrigger trigger;

    [Tooltip("이 대상을 여는 기능 키(옵션). 잠겨 있는 동안은 점을 숨긴다")]
    [SerializeField] EOutgameFeature unlockFeature;

    [Tooltip("런타임에 깐 점의 크기(px). 배선된 점 노드를 쓸 때는 무시된다")]
    [SerializeField] Vector2 dotSize = new Vector2(44f, 44f);

    [Tooltip("런타임에 깐 점의 우상단 모서리 기준 오프셋(px). 0이면 모서리에 반쯤 걸친다 — " +
             "아이콘 얼굴을 덮지 않으려면 안쪽으로 밀지 않는 편이 낫다. 배선된 점 노드를 쓸 때는 무시된다")]
    [SerializeField] Vector2 dotOffset = Vector2.zero;

    protected override bool ShouldShow => TriggeredTutorialRunner.HasPending(this.trigger)
                                       && OutgameFeatureLock.IsUnlocked(this.unlockFeature);

    // 해금이 풀리는 순간에도 떠야 하므로 잠금 통지까지 함께 듣는다.
    protected override void Subscribe(Action _handler)
    {
        TriggeredTutorialRunner.OnChanged += _handler;
        OutgameFeatureLock.OnChanged      += _handler;
    }

    protected override void Unsubscribe(Action _handler)
    {
        TriggeredTutorialRunner.OnChanged -= _handler;
        OutgameFeatureLock.OnChanged      -= _handler;
    }

    /// <summary>런타임 부착용(탭 버튼처럼 프리팹에 컴포넌트를 못 붙이는 대상).
    /// 점 노드도 여기서 깐다 — 부착 대상이 nested 프리팹 인스턴스라 자식을 미리 저작해 둘 수 없다.</summary>
    public void Bind(EOutgameTutorialTrigger _trigger, EOutgameFeature _unlockFeature, GameObject _dotPrefab)
    {
        this.trigger       = _trigger;
        this.unlockFeature = _unlockFeature;

        var t_parent = this.transform as RectTransform;
        if (_dotPrefab == null || t_parent == null) return;

        var t_dot = Instantiate(_dotPrefab, t_parent, false);
        t_dot.name = "TutorialAlertDot";
        t_dot.SetActive(false);

        if (t_dot.transform is RectTransform t_rect)
        {
            t_rect.anchorMin        = Vector2.one;
            t_rect.anchorMax        = Vector2.one;
            t_rect.pivot            = new Vector2(0.5f, 0.5f);
            t_rect.sizeDelta        = this.dotSize;
            t_rect.anchoredPosition = this.dotOffset;
            t_rect.localScale       = Vector3.one;
        }

        // 점이 버튼 위를 덮으면 그만큼 탭이 안 눌린다 — 표시 전용이라 레이캐스트를 통째로 비운다.
        var t_graphics = t_dot.GetComponentsInChildren<Graphic>(true);
        for (int t_i = 0; t_i < t_graphics.Length; t_i++) t_graphics[t_i].raycastTarget = false;

        t_dot.transform.SetAsLastSibling();   // 버튼 그래픽 위에 그린다

        this.BindDot(t_dot);
    }
}
