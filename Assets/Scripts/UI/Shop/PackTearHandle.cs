using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

// 카드팩 개봉의 방아쇠가 되는 "가로로 긋기" 제스처. 긋는 동안은 아무것도 찢기지 않는다 —
// 충분히 그었다고 판정되는 순간 OnTorn 한 번을 쏘고, 그 뒤의 자리잡기·씰 찢기·뽑기는
// PackRevealView가 자동으로 몰고 간다. 이 컴포넌트가 아는 것은 "그었다/못 그었다"뿐이다.
//
// 진행도(OnProgress)는 그림이 아니라 손가락의 상태다 — 몇 %쯤 그었는지를 알려 팩의 부유·광채가
// 손을 대는 동안 멈추게 한다. 임계에 못 미쳐 손을 떼면 0으로 되감겨 그 표현들이 되살아난다.
//
// 진행도는 드래그 시작점 기준의 가로 이동량이다(절대 좌표 아님) — 화면 어디서 시작해도 그을 수 있게.
// 첫 유효 이동의 방향을 부호로 잠가 왼쪽으로 긋는 사람도 똑같이 열 수 있다.
//
// 입력은 화면 전체에서 포인터를 직접 폴링해 받는다 — 팩을 정확히 집어야 열리는 조작은
// 개봉이라는 보상 연출에 어울리지 않는다. 덕분에 콜라이더도 카메라도 필요 없다(팩은 UI Image다).
//
// 이 컴포넌트는 구매·개봉·소유를 모른다 — 제스처 진행도와 "그었다"만 알린다.
// 찢김 그림은 전부 PackTearSkin이 그리고, 언제 찢을지는 PackRevealView가 정한다.
public class PackTearHandle : MonoBehaviour
{
    // 스와이프 진행도(0~1). 매 프레임 갱신되며 되감기 중에도 발화한다. 찢김 그림과는 무관하다.
    public event Action<float> OnProgress;

    // 스와이프 확정(= 개봉 시작). 판정된 프레임에 1회.
    public event Action OnTorn;

    [Header("제스처")]
    [Tooltip("확정에 필요한 가로 이동량(화면 너비 대비). 기기 해상도와 무관하게 같은 손맛이 나온다.")]
    [Range(0.15f, 1f)] [SerializeField] float tearScreenRatio = 0.55f;
    [Tooltip("이 진행도를 넘긴 채 손을 떼면 확정된다. 못 미치면 되감긴다.")]
    [Range(0.1f, 1f)] [SerializeField] float commitThreshold = 0.45f;
    [Tooltip("이 속도(픽셀/초) 이상으로 튕기면 거리가 부족해도 확정된다. 0이면 속도 판정 없음.")]
    [SerializeField] float flickSpeed = 1400f;
    [Tooltip("속도로 확정할 때 최소한 이만큼은 그어야 한다(스치는 터치를 개봉으로 오인하지 않게).")]
    [Range(0f, 1f)] [SerializeField] float flickMinProgress = 0.15f;
    [Tooltip("이만큼(스크린px) 움직여야 제스처가 시작된다. 방향을 잠그는 기준이기도 하다.")]
    [SerializeField] float deadZone = 12f;
    [SerializeField] float rewindDuration = 0.28f;

    bool m_armed;
    bool m_committed;   // 확정 후 재입력 차단
    bool m_dragging;

    Vector2 m_dragStart;
    Vector2 m_lastPointer;
    float m_speed;      // 최근 프레임의 가로 포인터 속도(픽셀/초)
    float m_dirSign;    // 0 = 아직 방향 미정. 첫 유효 이동으로 잠근다.
    float m_progress;

    /// <summary>스와이프 입력을 켠다. 팩이 등장을 마친 뒤 호출.</summary>
    public void Arm()
    {
        m_armed = true;
        m_committed = false;
        m_dragging = false;
        m_dirSign = 0f;
        SetProgress(0f);
    }

    /// <summary>입력을 내린다(뷰 비활성·스킵). 진행 중이던 트윈도 정리한다.</summary>
    public void Disarm()
    {
        m_armed = false;
        m_dragging = false;
        DOTween.Kill(this);
    }

    // armed 상태에서만 도므로 다른 단계의 입력을 훔치지 않는다.
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
        m_dirSign = 0f;
    }

    void UpdateDrag()
    {
        var t_pointer = CurrentPointer();

        float t_dt = Time.unscaledDeltaTime;
        if (t_dt > 0f) m_speed = Mathf.Abs(t_pointer.x - m_lastPointer.x) / t_dt;
        m_lastPointer = t_pointer;

        float t_dx = t_pointer.x - m_dragStart.x;

        // 첫 유효 이동으로 방향을 잠근다 — 이후에는 그 방향으로 간 만큼만 진행도가 오른다.
        if (m_dirSign == 0f)
        {
            if (Mathf.Abs(t_dx) < deadZone) return;
            m_dirSign = Mathf.Sign(t_dx);
            // 데드존만큼은 진행도로 치지 않는다 — 손가락과 찢김 선단이 어긋나 보이지 않게 시작점을 옮긴다.
            m_dragStart.x += deadZone * m_dirSign;
            t_dx = t_pointer.x - m_dragStart.x;
        }

        SetProgress(Mathf.Clamp01(t_dx * m_dirSign / TearDistance()));
    }

    void EndDrag()
    {
        m_dragging = false;

        // 거리가 찼거나, 짧아도 충분히 빠르게 튕겼으면 확정. 둘 다 아니면 되감아 다시 시도하게 둔다.
        bool t_flicked = flickSpeed > 0f && m_speed >= flickSpeed && m_progress >= flickMinProgress;
        if (m_progress >= commitThreshold || t_flicked) Finish();
        else Rewind();
    }

    // 임계를 넘겼다 — 손을 뗀 그 프레임에 확정한다. 뜸을 들이지 않는 이유는
    // 이 뒤가 곧바로 자리잡기 연출이기 때문이다 — 여기서 한 박자를 더 주면 스와이프와 연출 사이가 끊긴다.
    // 진행도는 1에 걸어 둔다 — 부유·광채가 개봉 내내 멈춰 있어야 한다.
    void Finish()
    {
        m_committed = true;
        m_armed = false;

        DOTween.Kill(this);
        SetProgress(1f);
        OnTorn?.Invoke();
    }

    // 임계에 못 미쳤다 — 되감아 다시 시도하게 둔다.
    void Rewind()
    {
        DOTween.Kill(this);
        DOTween.To(() => m_progress, SetProgress, 0f, rewindDuration)
            .SetId(this)
            .SetEase(Ease.OutQuad);
    }

    // 진행도 반영 지점 하나. 통지가 여기서만 갈라져 상태와 구독자가 어긋나지 않는다.
    void SetProgress(float _value)
    {
        m_progress = _value;

        OnProgress?.Invoke(m_progress);
    }

    // 화면 너비에 비례 — 태블릿에서 손가락을 두 배로 끌게 만들지 않는다.
    float TearDistance() => Mathf.Max(1f, Screen.width * tearScreenRatio);

    // 터치·마우스 공통 포인터 위치. Unity가 터치를 mousePosition으로도 흘려주므로 한 경로로 충분하다.
    static Vector2 CurrentPointer() => Input.mousePosition;

    // 레이캐스트를 받는 UI 위인지. 개봉 단계의 RevealPanel은 blocksRaycasts=false라 여기 걸리지 않는다.
    // ⚠ 팩·배경 Image는 raycastTarget을 꺼야 한다 — 켜져 있으면 팩 위에서 시작한 드래그가 여기서 막혀 개봉 자체가 안 된다.
    static bool IsPointerOverUI()
        => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    void OnDisable()
    {
        DOTween.Kill(this);
        m_armed = false;
        m_dragging = false;
    }
}
