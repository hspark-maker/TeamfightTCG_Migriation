using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

// 3D 카드팩의 "봉인 뜯기" 제스처. 손가락을 그은 만큼 찢어지고,
// 임계를 넘기거나 충분히 빠르게 튕기면 손을 떼도 나머지가 자동으로 완주한다. 못 미치면 되감겨 다시 시도할 수 있다.
//
// 입력은 화면 전체에서 받는다(콜라이더 위가 아니어도 된다) — 팩을 정확히 집어야 열리는 조작은
// 개봉이라는 보상 연출에 어울리지 않는다. 그래서 OnMouse*(콜라이더 히트 전제)를 쓰지 않고 포인터를 직접 폴링한다.
// 방향도 가리지 않는다: 어느 쪽으로 그어도 그은 거리만큼 찢긴다.
//
// 이 컴포넌트는 구매·개봉·소유를 모른다 — 제스처 진행도와 "뜯겼다"만 알린다(PackClickHandle의 성격 계승).
// 찢김 표현은 sealRoot 훅으로 최소한만 제공한다. 표현을 갈아끼우려면 이 훅 대신 OnProgress를 구독하면 된다.
public class PackTearHandle : MonoBehaviour
{
    // 뜯기 진행도(0~1). 매 프레임 갱신되며 되감기·자동완주 중에도 발화한다.
    public event Action<float> OnProgress;

    // 뜯기 확정. 자동 완주가 끝난 시점에 1회.
    public event Action OnTorn;

    [Header("제스처")]
    [Tooltip("완전히 찢기까지 필요한 스크린 이동 픽셀(방향 무관 — 그은 거리 그대로).")]
    [SerializeField] float tearDistance = 160f;
    [Tooltip("이 진행도를 넘기면 손을 떼도 자동 완주한다.")]
    [Range(0.1f, 1f)] [SerializeField] float commitThreshold = 0.4f;
    [Tooltip("이 속도(픽셀/초) 이상으로 튕기면 거리가 부족해도 완주한다. 0이면 속도 판정 없음.")]
    [SerializeField] float flickSpeed = 900f;
    [Tooltip("속도로 완주할 때 최소한 이만큼은 그어야 한다(스치는 터치를 뜯기로 오인하지 않게).")]
    [Range(0f, 1f)] [SerializeField] float flickMinProgress = 0.12f;
    [SerializeField] float finishDuration = 0.18f;
    [SerializeField] float rewindDuration = 0.25f;

    [Header("찢김 표현 (옵션)")]
    [Tooltip("진행도에 따라 밀려나는 봉인 조각. 미배선이면 표현 없이 제스처만 동작한다.")]
    [SerializeField] Transform sealRoot;
    [Tooltip("완전히 찢겼을 때 sealRoot가 밀려나는 로컬 오프셋.")]
    [SerializeField] Vector3 sealTornOffset = new Vector3(0f, 0.35f, 0f);

    bool m_armed;
    bool m_committed;   // 뜯기 확정 후 재입력 차단
    bool m_dragging;

    Vector2 m_dragStart;
    Vector2 m_lastPointer;
    float m_speed;      // 최근 프레임의 포인터 속도(픽셀/초)
    float m_progress;

    // sealRoot 원위치(되감기 기준). Awake에서 1회 캡처.
    Vector3 m_sealHome;
    bool m_sealHomeCaptured;

    void Awake()
    {
        CaptureSealHome();
    }

    /// <summary>뜯기 입력을 켠다. 팩이 등장을 마친 뒤 호출.</summary>
    public void Arm()
    {
        m_armed = true;
        m_committed = false;
        m_dragging = false;
        SetProgress(0f);
    }

    /// <summary>입력을 내린다(뷰 비활성·스킵). 진행 중이던 트윈도 정리한다.</summary>
    public void Disarm()
    {
        m_armed = false;
        m_dragging = false;
        DOTween.Kill(this);
    }

    /// <summary>연출을 건너뛰고 즉시 뜯긴 상태로 만든다. OnTorn은 발화하지 않는다(호출부가 흐름을 이어받는다).</summary>
    public void ForceTornInstant()
    {
        Disarm();
        m_committed = true;
        SetProgress(1f);
    }

    // 화면 어디서 시작한 스와이프든 받는다. armed 상태에서만 도므로 다른 단계의 입력을 훔치지 않는다.
    void Update()
    {
        if (!m_armed || m_committed) return;

        if (!m_dragging)
        {
            // 버튼 등 실제 UI를 누른 것은 그쪽 몫이다 — 제스처가 가로채지 않는다.
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI()) BeginDrag();
            return;
        }

        UpdateDrag();

        // 손을 뗐거나(정상) 포인터가 사라졌으면(터치 취소) 확정 판정으로 넘긴다.
        if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)) EndDrag();
    }

    void BeginDrag()
    {
        DOTween.Kill(this);   // 되감기 중에 다시 잡으면 그 자리에서 이어 긋는다.

        m_dragging = true;
        m_dragStart = CurrentPointer();
        m_lastPointer = m_dragStart;
        m_speed = 0f;
    }

    void UpdateDrag()
    {
        var t_pointer = CurrentPointer();

        float t_dt = Time.unscaledDeltaTime;
        if (t_dt > 0f) m_speed = Vector2.Distance(t_pointer, m_lastPointer) / t_dt;
        m_lastPointer = t_pointer;

        // 방향을 가리지 않는다 — 그은 거리가 그대로 진행도다.
        // 시작점으로 되돌아오면 자연히 줄어들어 되감기가 손에 붙는다.
        float t_drawn = (t_pointer - m_dragStart).magnitude;
        SetProgress(Mathf.Clamp01(t_drawn / Mathf.Max(1f, tearDistance)));
    }

    void EndDrag()
    {
        m_dragging = false;

        // 거리가 찼거나, 짧아도 충분히 빠르게 튕겼으면 완주. 둘 다 아니면 되감아 다시 시도하게 둔다.
        bool t_flicked = flickSpeed > 0f && m_speed >= flickSpeed && m_progress >= flickMinProgress;
        if (m_progress >= commitThreshold || t_flicked) Finish();
        else Rewind();
    }

    // 임계를 넘겼다 — 나머지를 자동으로 그어 완주시킨 뒤 확정을 알린다.
    void Finish()
    {
        m_committed = true;
        m_armed = false;

        DOTween.Kill(this);
        DOTween.To(() => m_progress, SetProgress, 1f, finishDuration)
            .SetId(this)
            .SetEase(Ease.OutCubic)
            .OnComplete(() => OnTorn?.Invoke());
    }

    // 임계에 못 미쳤다 — 되감아 다시 시도하게 둔다.
    void Rewind()
    {
        DOTween.Kill(this);
        DOTween.To(() => m_progress, SetProgress, 0f, rewindDuration)
            .SetId(this)
            .SetEase(Ease.OutQuad);
    }

    // 진행도 반영 지점 하나. 표현·통지가 여기서만 갈라져 상태와 화면이 어긋나지 않는다.
    void SetProgress(float _value)
    {
        m_progress = _value;

        if (sealRoot != null)
        {
            CaptureSealHome();
            sealRoot.localPosition = m_sealHome + sealTornOffset * m_progress;
        }

        OnProgress?.Invoke(m_progress);
    }

    void CaptureSealHome()
    {
        if (m_sealHomeCaptured || sealRoot == null) return;
        m_sealHome = sealRoot.localPosition;
        m_sealHomeCaptured = true;
    }

    // 터치·마우스 공통 포인터 위치. Unity가 터치를 mousePosition으로도 흘려주므로 한 경로로 충분하다.
    static Vector2 CurrentPointer() => Input.mousePosition;

    // 레이캐스트를 받는 UI 위인지. 개봉 단계의 RevealPanel은 blocksRaycasts=false라 여기 걸리지 않는다.
    static bool IsPointerOverUI()
        => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    void OnDisable()
    {
        DOTween.Kill(this);
        m_armed = false;
        m_dragging = false;
    }
}
