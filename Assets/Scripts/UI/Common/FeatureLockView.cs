using System.Collections.Generic;
using UnityEngine;

// 튜토리얼 진행으로 아직 열리지 않은 UI에 잠김 룩을 얹는 표시 컴포넌트.
// 판정은 갖지 않는다 — OutgameFeatureLock이 단일 진실원이고 여기는 그 결과를 그리기만 한다.
//
// 차단은 이 컴포넌트의 몫이 아니다. interactable을 세우는 주체는 각 화면의 계산식이어야 한다 —
// 여기서 함께 만지면 매 갱신마다 서로 덮어써 어느 쪽이 이겼는지가 호출 순서에 달리게 된다.
//
// 룩은 두 겹이다: 대상 전체 탈채도 + 그 위에 얹는 자물쇠 배지. 탈채도만으로도 "지금은 못 쓴다"가 성립하고
// 배지는 "나중에 열린다"를 덧붙이는 것이라, 배지 프리팹이 없으면 경고 한 번 뒤 탈채도만으로 간다.
public class FeatureLockView : MonoBehaviour
{
    const string BadgePath = "UI/LockBadge";

    [Tooltip("이 UI를 여는 기능 키. None이면 항상 열려 있다")]
    [SerializeField] EOutgameFeature feature;

    [Tooltip("자물쇠 배지를 놓을 자리. 비우면 자기 RectTransform")]
    [SerializeField] RectTransform badgeParent;

    GameObject m_badge;
    bool       m_badgeMissing;   // 프리팹 미배치 경고·재시도는 1회로 끝낸다

    List<UiGrayscale.Toned> m_toned;

    public EOutgameFeature Feature => feature;

    /// <summary>지금 이 UI가 잠겨 있는가. 튜토리얼 게이트가 "왜 타깃이 안 눌리는지"를 진단할 때 읽는다.</summary>
    public bool IsLocked => feature != EOutgameFeature.None && !OutgameFeatureLock.IsUnlocked(feature);

    /// <summary>런타임 부착 창구. 프리팹에 컴포넌트를 못 붙이는 대상(Layer Lab 인스턴스 안의 stripped Button 등)이나
    /// 잠금 대상이 코드로만 정해지는 자리에서 쓴다. 대상마다 거대 프리팹을 열지 않아도 되는 것이 요점이다.
    ///
    /// 한 번만 부르면 된다 — 이후 해금 반영은 이 컴포넌트가 OnChanged를 구독해 스스로 한다.</summary>
    public static void Attach(GameObject _target, EOutgameFeature _feature)
    {
        if (_target == null || _feature == EOutgameFeature.None) return;

        var t_view = _target.GetComponent<FeatureLockView>();
        if (t_view == null) t_view = _target.AddComponent<FeatureLockView>();

        // 저작 시절 꺼둔 채 남은 인스턴스가 있다 — GetComponent는 그것도 찾아오므로 켜 주지 않으면
        // OnEnable이 돌지 않아 구독도 룩도 없이 조용히 넘어간다.
        t_view.enabled = true;
        t_view.Bind(_feature);
    }

    /// <summary>AddComponent 직후엔 OnEnable이 이미 지나갔으므로(구독만 걸린 채 feature가 비어 있다) 여기서 다시 적용한다.</summary>
    public void Bind(EOutgameFeature _feature)
    {
        this.feature = _feature;

        if (isActiveAndEnabled) Apply();
    }

    void OnEnable()
    {
        OutgameFeatureLock.OnChanged += Apply;
        Apply();
    }

    void OnDisable()
    {
        OutgameFeatureLock.OnChanged -= Apply;
    }

    void Apply()
    {
        // 판정을 먼저 받는다 — 조회가 내부에서 OnChanged를 동기 발화해 이 메서드가 스스로 재진입할 수 있다.
        // 상태를 그 뒤에만 만지면 중첩 호출이 먼저 끝나도 바깥이 같은 값으로 다시 세워 결과가 어긋나지 않는다.
        bool t_unlocked = OutgameFeatureLock.IsUnlocked(this.feature);

        // 다시 칠하기 전에 항상 저작값으로 되돌린다 — 잠긴 채로 자식이 늘어났을 수 있어 목록을 매번 새로 뜬다.
        UiGrayscale.Restore(this.m_toned);

        if (t_unlocked)
        {
            if (this.m_badge != null) this.m_badge.SetActive(false);
            return;
        }

        EnsureBadge();
        if (this.m_badge != null) this.m_badge.SetActive(true);

        // 배지는 탈채도에서 제외한다. 자물쇠가 원래 무채색이라 지금은 티가 안 나지만, 생성 순서에 따라
        // 걸리기도 안 걸리기도 하는 상태를 남기면 나중에 색 있는 배지로 갈았을 때 조용히 회색이 된다.
        this.m_toned = UiGrayscale.Apply(gameObject, this.m_badge != null ? this.m_badge.transform : null);
    }

    void EnsureBadge()
    {
        if (this.m_badge != null || this.m_badgeMissing) return;

        var t_parent = this.badgeParent != null ? this.badgeParent : transform as RectTransform;
        if (t_parent == null)
        {
            this.m_badgeMissing = true;
            Debug.LogWarning($"[FeatureLockView] '{name}'이 RectTransform이 아니라 자물쇠를 얹을 자리가 없습니다.");
            return;
        }

        var t_prefab = Resources.Load<GameObject>(BadgePath);
        if (t_prefab == null)
        {
            this.m_badgeMissing = true;
            Debug.LogWarning($"[FeatureLockView] Resources/{BadgePath} 미배치 — '{name}'의 자물쇠를 그리지 못합니다(잠김은 흑백으로만 보입니다).");
            return;
        }

        // 저작된 앵커·비율을 그대로 살린다. 여기서 크기를 다시 잡으면 대상마다 어긋나던 옛 방식으로 돌아간다.
        this.m_badge      = Instantiate(t_prefab, t_parent, false);
        this.m_badge.name = "LockBadge";
        this.m_badge.transform.SetAsLastSibling();
    }
}
