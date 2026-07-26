using System;
using DG.Tweening;
using UnityEngine;

// 3D 카드팩 모델(CardPack.prefab)에 부착되는 "가로 드래그로 봉인 뜯기" 인터랙션.
// 입력은 RevealCardView와 같은 월드 방식(BoxCollider3D + OnMouse* + ScreenToWorldPoint) —
// prefab에 BoxCollider가 이미 있어 마우스/터치가 OnMouse 콜백으로 매핑된다. 카메라는 씬의 Camera.main.
// 순수 인터랙션 뷰: 구매·개봉·소유를 모른다. 뜯김이 확정되면 SetTearCallback으로 받은 콜백만 1회 발화하고,
// 다음 단계(스택 빌드)는 PackTearOpenView가 주도한다.
public class PackTearHandle : MonoBehaviour
{
    [Header("바인딩")]
    [Tooltip("뜯길 봉인 조각(CardPack.prefab의 SealStrip). 뜯김 확정 시 옆으로 밀리며 사라진다.")]
    [SerializeField] Transform sealStrip;

    [Header("뜯기 판정")]
    [Tooltip("드래그 시작점 대비 가로 이동거리(월드 유닛)가 이 값 이상이면 뜯김 확정. 미만이면 봉인 원위치.")]
    [SerializeField] float tearThreshold = 1.2f;
    [Tooltip("뜯김 확정 시 봉인이 옆으로 밀려나는 시간(초).")]
    [SerializeField] float tearSlideDuration = 0.35f;
    [Tooltip("봉인이 밀려나는 가로 거리(월드 유닛, 드래그 방향 부호를 따름).")]
    [SerializeField] float tearSlideDistance = 3f;
    [Tooltip("threshold 미달로 놓았을 때 봉인이 제자리로 돌아가는 시간(초).")]
    [SerializeField] float returnDuration = 0.2f;

    // 뜯김 확정 콜백(PackTearOpenView가 스택 빌드로 이어받음).
    Action m_onTorn;

    // 활성/드래그 상태. 뜯김 1회 확정 후엔 입력을 막는다(중복 뜯기 가드).
    bool m_active;
    bool m_torn;
    bool m_dragging;
    float m_dragDepth;               // ScreenToWorldPoint용 깊이(봉인 평면까지 스크린 z), OnMouseDown에서 고정
    Vector3 m_pointerStartWorld;     // 드래그 시작 시 포인터 월드 좌표
    Vector3 m_sealHomeLocalPos;      // 봉인 시작 로컬 좌표(미달 시 복귀 목표)

    // ── 공개 API ──────────────────────────────────────────────

    /// <summary>뜯기 입력을 켜고 봉인 시작 위치를 기억한다. 컨트롤러가 팩을 보인 뒤 호출.</summary>
    public void ArmTear()
    {
        m_active = true;
        m_torn = false;
        m_dragging = false;
        if (sealStrip != null) m_sealHomeLocalPos = sealStrip.localPosition;
    }

    /// <summary>뜯김 확정 콜백 등록.</summary>
    public void SetTearCallback(Action _cb) => m_onTorn = _cb;

    // ── 월드 드래그 입력(BoxCollider 필요) ────────────────────

    void OnMouseDown()
    {
        if (!m_active || m_torn) return;

        var t_cam = Camera.main;
        m_dragDepth = t_cam != null ? t_cam.WorldToScreenPoint(transform.position).z : 0f;
        m_dragging = true;
        m_pointerStartWorld = GetPointerWorld();
        if (sealStrip != null) sealStrip.DOKill();
    }

    void OnMouseDrag()
    {
        if (!m_active || m_torn || !m_dragging || sealStrip == null) return;

        // 가로 성분만 봉인에 반영(뜯는 방향 시각화). 세로 델타는 무시.
        float t_dx = GetPointerWorld().x - m_pointerStartWorld.x;
        sealStrip.localPosition = m_sealHomeLocalPos + new Vector3(t_dx, 0f, 0f);
    }

    void OnMouseUp()
    {
        if (!m_active || m_torn || !m_dragging) return;
        m_dragging = false;

        float t_dx = GetPointerWorld().x - m_pointerStartWorld.x;

        if (Mathf.Abs(t_dx) >= tearThreshold)
            ConfirmTear(Mathf.Sign(t_dx));
        else if (sealStrip != null)
        {
            sealStrip.DOKill();
            sealStrip.DOLocalMove(m_sealHomeLocalPos, returnDuration).SetEase(Ease.OutBack);
        }
    }

    // 뜯김 확정: 입력 잠금 → 봉인을 드래그 방향으로 밀어내며 페이드 → 완료 콜백 1회.
    void ConfirmTear(float _dir)
    {
        m_torn = true;
        m_active = false;

        if (sealStrip != null)
        {
            sealStrip.DOKill();
            Vector3 t_target = m_sealHomeLocalPos + new Vector3(_dir * tearSlideDistance, 0f, 0f);
            var t_seq = DOTween.Sequence();
            t_seq.Append(sealStrip.DOLocalMove(t_target, tearSlideDuration).SetEase(Ease.InQuad));
            FadeRenderer(sealStrip, tearSlideDuration, t_seq);
            t_seq.OnComplete(() =>
            {
                if (sealStrip != null) sealStrip.gameObject.SetActive(false);
            });
        }

        m_onTorn?.Invoke();
    }

    // 봉인 조각 렌더러 페이드(MeshRenderer 머티리얼 알파). 머티리얼이 알파를 안 받으면 시각만 생략(동작 무해).
    void FadeRenderer(Transform _t, float _dur, Sequence _seq)
    {
        var t_rend = _t.GetComponent<Renderer>();
        if (t_rend == null || t_rend.material == null) return;
        var t_mat = t_rend.material;
        if (!t_mat.HasProperty("_Color")) return;
        _seq.Join(t_mat.DOFade(0f, _dur));
    }

    Vector3 GetPointerWorld()
    {
        var t_cam = Camera.main;
        if (t_cam == null) return transform.position;
        Vector3 t_sp = Input.mousePosition;
        t_sp.z = m_dragDepth;
        return t_cam.ScreenToWorldPoint(t_sp);
    }

    // 트윈 진행 중 파괴 시 좀비 트윈 방지.
    void OnDestroy()
    {
        m_dragging = false;
        if (sealStrip != null) sealStrip.DOKill();
    }
}
