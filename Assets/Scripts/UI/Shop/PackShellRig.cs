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
// 축이 둘이다. Stage축은 팩과 카드를 함께 움직이고(등장·자리잡기·부유), Shell축은 껍데기만 움직인다(퇴장).
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
    [Tooltip("빛살이 한 바퀴 도는 데 걸리는 시간(초). 음수면 반대 방향, 0이면 회전 없음. " +
             "배경 기척이라 눈이 좇을 수 없을 만큼 느려야 한다 — 24초면 초당 15도다.\n" +
             "⚠ 광채 노드의 rect가 정사각이어야 한다. 가로세로가 다르면 늘어난 타원이 통째로 도는 게 보여 " +
             "빛살이 도는 게 아니라 후광이 흔들리는 것으로 읽힌다.")]
    [SerializeField] float glowSpinPeriod = 24f;
    [Tooltip("개봉이 시작되면(자리잡기) 광채가 걷히는 속도(초당 비율). settleSpeed보다 느리게 잡아야 " +
             "팩이 내려가는 동안 빛이 뒤따라 사그라든다 — 같이 빠르면 빛만 먼저 사라져 팩이 맨몸으로 움직인다.")]
    [SerializeField] float glowFadeSpeed = 3.5f;

    [Header("연결")]
    [Tooltip("뜯기 진행도를 구독해 손가락이 닿은 동안 아이들을 멈춘다. 미배선이면 항상 동작.")]
    [SerializeField] PackTearHandle tearHandle;
    [Tooltip("아이들 정지 시 원위치로 걷히는 속도(초당 비율). 클수록 즉각적.")]
    [SerializeField] float settleSpeed = 12f;

    // ── 연출이 쓰는 축 ──────────────────────────────────────────
    /// <summary>무대 오프셋(팩+카드 함께). 등장·자리잡기 트윈이 쓴다.</summary>
    public Vector2 StageOffset { get; set; }
    /// <summary>무대 배율(팩+카드 함께). 자리잡기에서 팩을 키워 시선을 모으는 데 쓴다.
    /// 펄스와 곱해지므로 여기 1이 곧 씬 배치 크기다.</summary>
    public float StageScale { get; set; } = 1f;
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
    Vector2 m_shadowHome;
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

    // 광채가 살아 있는 비율(1 = 완전, 0 = 꺼짐). m_settle과 나란히 두지 않고 따로 두는 이유는 기준이 다르기 때문이다 —
    // 부유·펄스는 손가락이 닿는 동안 멈춰야 하지만(m_touching), 광채는 그때 꺼지면 안 된다.
    // 스와이프하려고 팩을 만질 때마다 후광이 깜빡이면 그게 개봉보다 더 눈에 띈다.
    // 광채를 걷는 사건은 "개봉이 시작됐다"(뷰의 SetIdle(false)) 하나뿐이라 m_idleAllowed만 본다.
    float m_glowFade = 1f;

    void Awake() => CaptureHome();

    void OnEnable()
    {
        CaptureHome();
        m_time = 0f;
        m_settle = 1f;
        // 손가락은 비활성 사이에 이미 떨어졌다. 뷰의 판단(m_idleAllowed)은 건드리지 않는다 —
        // 뽑기 도중 재활성됐다고 부유가 되살아나면 카드가 빠져나가는 중에 팩이 흔들린다.
        m_touching = false;
        // 광채도 같은 이유로 뷰의 판단을 따라간다 — 뽑기 도중 재활성됐다고 걷어 둔 빛이 도로 켜지면 안 된다.
        // 페이드 없이 즉시 맞춘다(재활성 순간에 빛이 차오르는 것도 되살아나는 것으로 보인다).
        m_glowFade = m_idleAllowed ? 1f : 0f;
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
        StageScale = 1f;
        ShellOffset = Vector2.zero;
        ShellSquash = Vector2.one;
        ShellAngle = 0f;
        m_idleAllowed = true;
        m_touching = false;
        m_settle = 1f;
        m_glowFade = 1f;   // 개봉 시작마다 광채를 되살린다 — 지난 세션에서 걷어 둔 채로 시작하면 팩이 맨몸으로 등장한다.
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

        // 광채만 IdleRunning이 아니라 m_idleAllowed를 따른다(m_glowFade 선언부 주석 참고).
        float t_glowTarget = m_idleAllowed ? 1f : 0f;
        m_glowFade = Mathf.Lerp(m_glowFade, t_glowTarget, 1f - Mathf.Exp(-glowFadeSpeed * t_dt));

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
            stage.localScale = m_stageHomeScale * StageScale * Mathf.Lerp(1f, pulseScale, t_pulse);
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
    //
    // 자리는 무대 오프셋만 따라간다 — 부유는 따르지 않는다. 팩이 위아래로 흔들릴 때 그림자까지 같이 흔들리면
    // 뜬 게 아니라 판 전체가 움직이는 그림이 되지만, 팩이 화면 아래로 옮겨 갈 때 그림자만 남으면 주인 없는 얼룩이 된다.
    void ApplyShadow(float _up01)
    {
        if (shadow == null) return;

        // 무대 배율은 자리에도 곱해야 한다 — 그림자는 무대 밖이라, 크기만 키우면 팩 밑바닥에서 떨어져 뜬다.
        shadow.anchoredPosition = m_shadowHome * StageScale + StageOffset;
        shadow.localScale = m_shadowHomeScale * StageScale * Mathf.Lerp(1f, shadowScaleAtTop, _up01);

        var t_g = shadow.GetComponent<Graphic>();
        if (t_g == null) return;

        var t_c = t_g.color;
        t_c.a = m_shadowHomeAlpha * Mathf.Lerp(1f, shadowAlphaAtTop, _up01);
        t_g.color = t_c;
    }

    // 밝기는 펄스를 타고, 빛살은 제자리에서 천천히 돈다. 개봉이 시작되면 m_glowFade가 밝기를 걷어
    // 빛이 사그라들고, 회전은 그와 무관하게 남은 밝기만큼만 보인다.
    //
    // 알파를 트윈으로 걷지 않는 이유는 이 클래스의 전제 그대로다(헤더 주석) — 여기가 glow.color를
    // 매 프레임 쓰는 유일한 지점이라, 밖에서 트윈을 걸면 다음 LateUpdate가 그대로 덮어쓴다.
    void ApplyGlow(float _pulse01)
    {
        if (glow == null) return;

        var t_c = glow.color;
        t_c.a = Mathf.Lerp(glowAlphaMin, glowAlphaMax, _pulse01) * m_glowFade;
        glow.color = t_c;

        // 회전에는 m_settle도 m_glowFade도 곱하지 않는다 — 누적 각도라 비율을 곱하면 멈출 때
        // 빛살이 0도로 되감긴다. 손가락이 닿아 아이들이 서면 m_time이 멈추므로 회전도 그 자리에 선다(그게 의도한 정지다).
        if (Mathf.Abs(glowSpinPeriod) > 0.01f)
            glow.transform.localRotation = Quaternion.Euler(0f, 0f, m_time * 360f / glowSpinPeriod);
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

        m_shadowHome = shadow != null ? shadow.anchoredPosition : Vector2.zero;
        m_shadowHomeScale = shadow != null ? shadow.localScale : Vector3.one;

        var t_g = shadow != null ? shadow.GetComponent<Graphic>() : null;
        m_shadowHomeAlpha = t_g != null ? t_g.color.a : 1f;

        m_homeCaptured = true;
    }
}
