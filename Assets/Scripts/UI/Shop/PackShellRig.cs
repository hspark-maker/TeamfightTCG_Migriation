using UnityEngine;
using UnityEngine.UI;

// 팩의 몸짓을 쥐는 단일 창구. 팩은 "카드를 사이에 낀 통"이라 앞뒤 두 껍데기로 나뉘어 있고,
// 그 둘 + 무대 전체 + 바닥 그림자가 항상 한 몸으로 움직여야 한다. 그 합성을 여기 한 곳에서 한다.
//
// 계층 전제(이 구성이 연출의 전부다):
//   PackStage            ← 등장·아이들. 팩과 카드가 함께 산다.
//     ShellBack          ← 팩 뒷면(카드 뒤)
//     CardHost           ← 카드. 앞뒷면 사이에 끼어야 "팩 속"이 성립한다.
//     ShellFront         ← 팩 앞면(카드 앞). 찢긴 구멍으로만 카드가 보인다.
//   PackShadow           ← 바닥 그림자. 무대 밖에 두어야 팩만 뜬다.
//
// 축이 둘이다. Stage축은 팩과 카드를 함께 움직이고(등장·부유), Shell축은 껍데기만 움직인다(퇴장).
// 카드를 빼낸 뒤 팩만 빠져나가는 장면이 Shell축으로 성립하고, 그동안 카드는 CardHost가 따로 몬다
// — 한 노드를 두 연출이 함께 쓰지 않게 하려는 분리다.
//
// 값을 프로퍼티로 두고 LateUpdate에서 합치는 이유: 트윈과 아이들이 같은 anchoredPosition을
// 각자 쓰면 서로 덮어써 떨린다. 쓰는 지점을 하나로 모으면 둘을 그냥 더하면 된다.
public class PackShellRig : MonoBehaviour
{
    [Header("계층")]
    [Tooltip("팩과 카드가 함께 사는 무대. 등장·부유가 이걸 움직인다.")]
    [SerializeField] RectTransform stage;
    [Tooltip("팩 뒷면 묶음(카드 뒤).")]
    [SerializeField] RectTransform shellBack;
    [Tooltip("팩 앞면 묶음(카드 앞).")]
    [SerializeField] RectTransform shellFront;
    [Tooltip("바닥 그림자. 무대 밖(형제)에 두어야 팩만 뜬다.")]
    [SerializeField] RectTransform shadow;
    [Tooltip("팩 뒤 방사광. 펄스에 맞춰 밝기가 오르내린다.")]
    [SerializeField] Graphic glow;

    [Header("부유")]
    [SerializeField] float floatDistance = 22f;
    [Tooltip("왕복 주기(초). 펄스 주기와 어긋나게 잡아야 패턴이 안 읽힌다.")]
    [SerializeField] float floatPeriod = 2.4f;

    [Header("펄스")]
    [SerializeField] float pulseScale = 1.03f;
    [SerializeField] float pulsePeriod = 2.0f;

    [Header("그림자 연동")]
    [Tooltip("팩이 가장 높이 떴을 때의 그림자 배율.")]
    [SerializeField] float shadowScaleAtTop = 0.82f;
    [Tooltip("팩이 가장 높이 떴을 때의 그림자 알파 배율.")]
    [SerializeField] float shadowAlphaAtTop = 0.55f;

    [Header("광채 연동")]
    [SerializeField] float glowAlphaMin = 0.18f;
    [SerializeField] float glowAlphaMax = 0.34f;

    [Header("연결")]
    [Tooltip("뜯기 진행도를 구독해 손가락이 닿은 동안 아이들을 멈춘다. 미배선이면 항상 동작.")]
    [SerializeField] PackTearHandle tearHandle;
    [Tooltip("아이들 정지 시 원위치로 걷히는 속도(초당 비율). 클수록 즉각적.")]
    [SerializeField] float settleSpeed = 12f;

    // ── 연출이 쓰는 축 ──────────────────────────────────────────
    /// <summary>무대 오프셋(팩+카드 함께). 등장 트윈이 쓴다.</summary>
    public Vector2 StageOffset { get; set; }
    /// <summary>껍데기 오프셋(팩만). 퇴장 트윈이 쓴다.</summary>
    public Vector2 ShellOffset { get; set; }
    /// <summary>껍데기 배율(팩만). 카드를 빼낸 뒤 팩이 시드는 표현에 쓴다.</summary>
    public Vector2 ShellSquash { get; set; } = Vector2.one;
    /// <summary>껍데기 기울기(도, 팩만).</summary>
    public float ShellAngle { get; set; }

    // 무대·껍데기의 씬 배치값(합성 기준). 앵커가 중앙이라 rect 드라이브 타이밍과 무관하다.
    Vector2 m_stageHome;
    Vector3 m_stageHomeScale = Vector3.one;
    Vector3 m_backHomeScale = Vector3.one, m_frontHomeScale = Vector3.one;
    Vector2 m_backHome, m_frontHome;
    Vector3 m_shadowHomeScale = Vector3.one;
    float m_shadowHomeAlpha = 1f;
    bool m_homeCaptured;

    float m_time;
    float m_settle = 1f;   // 아이들 반영 비율(1 = 완전 적용, 0 = 정지)

    // 아이들을 멈추는 축이 둘이다. 연출(뷰)의 판단과 손가락이 닿았다는 사실은 서로 다른 사건이라
    // 한 플래그를 두 쪽이 덮어쓰면, 손을 뗀 순간 뷰가 꺼 둔 부유가 되살아난다. 따로 두고 AND 한다.
    bool m_idleAllowed = true;   // 권위는 뷰(SetIdle)
    bool m_touching;             // 제스처가 거는 일시 정지
    bool IdleRunning => m_idleAllowed && !m_touching;

    void Awake() => CaptureHome();

    void OnEnable()
    {
        CaptureHome();
        m_time = 0f;
        m_settle = 1f;
        // 손가락은 비활성 사이에 이미 떨어졌다. 뷰의 판단(m_idleAllowed)은 건드리지 않는다 —
        // 뽑기 도중 재활성됐다고 부유가 되살아나면 카드가 빠져나가는 중에 팩이 흔들린다.
        m_touching = false;
        if (tearHandle != null) tearHandle.OnProgress += HandleTearProgress;
    }

    void OnDisable()
    {
        if (tearHandle != null) tearHandle.OnProgress -= HandleTearProgress;
        ResetPose();
    }

    /// <summary>연출 축을 씬 배치값으로 되돌린다(개봉 시작마다).</summary>
    public void ResetPose()
    {
        StageOffset = Vector2.zero;
        ShellOffset = Vector2.zero;
        ShellSquash = Vector2.one;
        ShellAngle = 0f;
        m_idleAllowed = true;
        m_touching = false;
        m_settle = 1f;
        Apply();
    }

    /// <summary>아이들(부유·펄스) 온오프. 카드를 빼내는 순간부터는 꺼야 뽑는 손맛이 흔들리지 않는다.</summary>
    public void SetIdle(bool _running) => m_idleAllowed = _running;

    /// <summary>팩 껍데기를 통째로 감춘다(퇴장 완료·스킵). 카드는 무대에 그대로 남는다.</summary>
    public void HideShells()
    {
        if (shellBack != null) shellBack.gameObject.SetActive(false);
        if (shellFront != null) shellFront.gameObject.SetActive(false);
        if (shadow != null) shadow.gameObject.SetActive(false);
    }

    /// <summary>감췄던 껍데기를 되살린다(다음 개봉 세션 대비).</summary>
    public void ShowShells()
    {
        if (shellBack != null) shellBack.gameObject.SetActive(true);
        if (shellFront != null) shellFront.gameObject.SetActive(true);
        if (shadow != null) shadow.gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        if (!m_homeCaptured) return;

        // Time.unscaledDeltaTime — 개봉 화면은 일시정지와 무관하게 살아 있어야 한다.
        float t_dt = Time.unscaledDeltaTime;
        if (IdleRunning) m_time += t_dt;

        // 정지/재개를 비율로 섞는다 — 툭 끊기면 그 자체가 눈에 띈다.
        float t_target = IdleRunning ? 1f : 0f;
        m_settle = Mathf.Lerp(m_settle, t_target, 1f - Mathf.Exp(-settleSpeed * t_dt));

        Apply();
    }

    void Apply()
    {
        if (!m_homeCaptured) return;

        float t_floatWave = Mathf.Sin(m_time * Mathf.PI * 2f / Mathf.Max(0.01f, floatPeriod));   // -1~1
        float t_pulseWave = Mathf.Sin(m_time * Mathf.PI * 2f / Mathf.Max(0.01f, pulsePeriod));   // -1~1

        // ⚠ 정지 비율은 "0이 곧 씬 배치값"인 형태로 바꾼 뒤에 곱해야 한다.
        //   -1~1 진동에 먼저 곱하고 0~1로 옮기면 정지 상태가 0이 아니라 0.5가 되어,
        //   팩이 배치값이 아니라 펄스 중간 크기에서 굳는다(그림자·광채도 같은 함정).
        float t_float = t_floatWave * m_settle;                       // 0 = 제자리
        float t_pulse = (t_pulseWave + 1f) * 0.5f * m_settle;          // 0 = 원래 크기
        float t_up01  = (t_floatWave + 1f) * 0.5f * m_settle;          // 0 = 바닥(그림자 원값)

        // 무대: 씬 배치 + 연출 오프셋 + 부유. 팩과 카드가 함께 움직인다.
        if (stage != null)
        {
            stage.anchoredPosition = m_stageHome + StageOffset + new Vector2(0f, t_float * floatDistance);
            stage.localScale = m_stageHomeScale * Mathf.Lerp(1f, pulseScale, t_pulse);
        }

        // 껍데기: 씬 배치 + 퇴장 오프셋. 무대 위에 겹쳐 적용되므로 카드와 어긋나지 않는다.
        ApplyShell(shellBack,  m_backHome,  m_backHomeScale);
        ApplyShell(shellFront, m_frontHome, m_frontHomeScale);

        ApplyShadow(t_up01);
        ApplyGlow(t_pulse);
    }

    void ApplyShell(RectTransform _shell, Vector2 _home, Vector3 _homeScale)
    {
        if (_shell == null) return;

        _shell.anchoredPosition = _home + ShellOffset;
        _shell.localScale = new Vector3(_homeScale.x * ShellSquash.x, _homeScale.y * ShellSquash.y, _homeScale.z);
        _shell.localRotation = Quaternion.Euler(0f, 0f, ShellAngle);
    }

    // 뜰수록 그림자가 작아지고 옅어져야 "떠 있다"가 성립한다. 고정이면 팩은 벽에 붙은 스티커로 읽힌다.
    void ApplyShadow(float _up01)
    {
        if (shadow == null) return;

        shadow.localScale = m_shadowHomeScale * Mathf.Lerp(1f, shadowScaleAtTop, _up01);

        var t_g = shadow.GetComponent<Graphic>();
        if (t_g == null) return;

        var t_c = t_g.color;
        t_c.a = m_shadowHomeAlpha * Mathf.Lerp(1f, shadowAlphaAtTop, _up01);
        t_g.color = t_c;
    }

    void ApplyGlow(float _pulse01)
    {
        if (glow == null) return;

        var t_c = glow.color;
        t_c.a = Mathf.Lerp(glowAlphaMin, glowAlphaMax, _pulse01);
        glow.color = t_c;
    }

    // 진행도가 조금이라도 붙으면 = 손가락이 팩을 잡고 있다.
    void HandleTearProgress(float _progress) => m_touching = _progress > 0.001f;

    void CaptureHome()
    {
        if (m_homeCaptured) return;

        m_stageHome = stage != null ? stage.anchoredPosition : Vector2.zero;
        m_stageHomeScale = stage != null ? stage.localScale : Vector3.one;

        if (shellBack != null)  { m_backHome  = shellBack.anchoredPosition;  m_backHomeScale  = shellBack.localScale; }
        if (shellFront != null) { m_frontHome = shellFront.anchoredPosition; m_frontHomeScale = shellFront.localScale; }

        m_shadowHomeScale = shadow != null ? shadow.localScale : Vector3.one;

        var t_g = shadow != null ? shadow.GetComponent<Graphic>() : null;
        m_shadowHomeAlpha = t_g != null ? t_g.color.a : 1f;

        m_homeCaptured = true;
    }
}
