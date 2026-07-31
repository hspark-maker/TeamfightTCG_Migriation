using UnityEngine;

/// <summary>
/// 카드가 Y축으로 돌 때 **실제 뒷면**이 보이게 앞/뒤 오브젝트를 교대로 켠다.
///
/// 왜 필요한가: 스프라이트 셰이더(Sprites/Default)는 Cull Off라 양면이 다 그려진다.
/// 그래서 카드를 180° 돌리면 뒷면이 아니라 **앞면이 좌우 반전된 채** 보인다.
/// 뒷면 오브젝트를 Y 180°로 붙여 두고, 카메라를 향한 쪽만 남기는 게 이 컴포넌트다.
///
/// 기존 방식(<c>CunningVfx</c>가 반 바퀴 지점에서 illustration 스프라이트를 뒷면 그림으로 교체)과 달리
/// 회전 각도와 무관하게 항상 옳다 — 돌다 멈추거나, 도중에 트윈이 잘리거나, 다른 연출이 각도를 바꿔도
/// 보이는 면이 어긋나지 않는다. 스프라이트 교체 경로는 **뒷면 상태(isRevealed=false)** 전용으로 남는다.
///
/// 순수 표시 전용 — 게임 상태/RNG 무접촉. 회전은 이 컴포넌트가 만들지 않고 관찰만 한다.
/// </summary>
[DisallowMultipleComponent]
public class CardFaceFlipper : MonoBehaviour
{
    [Tooltip("앞면 내용 전체(카드 루트). 뒤를 보일 때 끈다")]
    [SerializeField] GameObject frontRoot;

    [Tooltip("뒷면 오브젝트. 로컬 회전 Y=180으로 붙여 둘 것")]
    [SerializeField] GameObject backRoot;

    [Tooltip("관찰할 회전 대상. 비우면 이 오브젝트(연출이 돌리는 트랜스폼)")]
    [SerializeField] Transform spinTarget;

    // -1 = 아직 판정 전. 첫 프레임에 반드시 한 번 적용되게 한다.
    int lastFront = -1;

    void Awake()
    {
        if (this.spinTarget == null) this.spinTarget = transform;
    }

    void OnEnable() => this.lastFront = -1;   // 재사용(슬롯 풀) 시 강제 재적용

    void LateUpdate()
    {
        if (this.backRoot == null) return;

        Camera t_cam = Camera.main;
        if (t_cam == null) return;

        // 카드 앞면 법선(+Z)이 카메라가 보는 방향과 같은 쪽이면 앞면이 보이는 상태.
        // 각도(eulerAngles)로 판정하지 않는 이유: 누적 회전·부모 회전이 섞이면 0~360 랩어라운드에서 틀린다.
        bool t_front = Vector3.Dot(this.spinTarget.forward, t_cam.transform.forward) > 0f;

        int t_flag = t_front ? 1 : 0;
        if (t_flag == this.lastFront) return;   // 바뀔 때만 SetActive — 매 프레임 호출은 계층 전체를 흔든다
        this.lastFront = t_flag;

        if (this.frontRoot != null) this.frontRoot.SetActive(t_front);
        this.backRoot.SetActive(!t_front);
    }

    /// <summary>앞면 강제 복귀(연출 종료·슬롯 재사용 시). 회전이 이미 0이면 LateUpdate가 같은 결과를 내지만,
    /// 비활성 상태로 남은 채 다음 카드가 들어오는 경로를 막기 위해 명시적으로 부를 수 있게 열어 둔다.</summary>
    public void ForceFront()
    {
        if (this.frontRoot != null) this.frontRoot.SetActive(true);
        if (this.backRoot  != null) this.backRoot.SetActive(false);
        this.lastFront = 1;
    }
}
