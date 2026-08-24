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
    [Tooltip("개봉이 시작되면(자리잡기) 광채가 완전히 걷히는 데 걸리는 시간(초). " +
             "PackRevealView의 packShiftDuration과 맞춰야 빛이 꺼지는 것과 팩이 내려가는 것이 한 동작으로 읽힌다 — " +
             "길면 팩이 다 내려간 뒤에도 빛이 남고, 짧으면 빛만 먼저 꺼져 팩이 맨몸으로 움직인다.")]
    [SerializeField] float glowFadeDuration = 0.4f;

    [Header("개봉 링")]
    [Tooltip("다 찢은 순간 팩 입구에서 링이 퍼진다. 링을 그리는 것은 광채 노드의 재질(UI/PackRing)이다 — " +
             "링만을 위한 UI 자식을 따로 두지 않으려고 같은 그래픽에 얹었다.")]
    [SerializeField] float ringDuration = 0.55f;
    [Tooltip("시작 배율(씬 배치 크기 기준). 작게 시작해야 \"입구에서 터져 나왔다\"가 된다.")]
    [Range(0.01f, 1f)] [SerializeField] float ringStartScale = 0.08f;
    [Tooltip("끝 배율. 화면 밖으로 빠져나갈 만큼 키워야 시선이 카드 쪽으로 끌려간다.")]
    [Range(0.2f, 4f)] [SerializeField] float ringEndScale = 1.6f;
    [Tooltip("가장 셀 때의 밝기(그림 몫). 재질의 _Intensity가 여기에 한 번 더 곱해진다.")]
    [Range(0f, 1f)] [SerializeField] float ringStrength = 1f;
    [Tooltip("그림 위에 얹는 절차적 링의 두께(그래픽 UV). 그림만으로는 두께를 못 바꾼다 — " +
             "선을 굵히려면 이 값을 올려라. 0이면 그림만 쓴다.")]
    [Range(0f, 0.4f)] [SerializeField] float ringThickness = 0.14f;
    [Tooltip("절차적 링의 밝기. 그림 몫과 따로 둔다 — 굵기만 올리고 밝기는 그대로 두고 싶을 때가 있다.")]
    [Range(0f, 1f)] [SerializeField] float ringStrokeStrength = 0.85f;

    [Header("개봉 흔들림")]
    [Tooltip("링이 퍼질 때 무대(팩+카드)가 흔들리는 최대 거리(캔버스 참조px). 0이면 흔들리지 않는다. " +
             "화면 전체가 아니라 무대만 흔든다 — SafeArea는 SafeAreaFitter가 매 프레임 자리를 되잡아 " +
             "거기에 흔들림을 얹으면 그대로 먹힌다.")]
    [SerializeField] float shakeAmplitude = 18f;
    [Tooltip("흔들림이 잦아드는 시간. 링보다 짧아야 \"충격은 한순간\"으로 읽힌다.")]
    [SerializeField] float shakeDuration = 0.28f;
    [Tooltip("초당 흔들리는 횟수에 해당하는 노이즈 주파수. 높을수록 잘게 떤다.")]
    [SerializeField] float shakeFrequency = 26f;
    [Tooltip("희귀 팩 개봉 흔들림 배율. 일반은 기존 진폭 1배를 유지한다.")]
    [Min(0f)] [SerializeField] float rareShakeScale = 1.5f;
    [Tooltip("신비 팩 개봉 흔들림 배율.")]
    [Min(0f)] [SerializeField] float arcaneShakeScale = 2.2f;
    [Tooltip("신화 팩 개봉 흔들림 배율.")]
    [Min(0f)] [SerializeField] float mythicShakeScale = 3f;

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

    // 개봉 링. 밖에서 트윈을 걸 수 없어(ApplyGlow가 매 프레임 glow를 덮는다) 여기서 시간을 센다.
    // 음수 = 재생 안 함. 0 이상이면 그만큼 흐른 시간이다.
    float m_ringTime = -1f;
    // 재질 런타임 사본. 에셋에 직접 쓰면 플레이 중 밝기가 .mat에 눌어붙는다.
    Material m_ringMat;
    ECardGrade m_ringGrade;
    PackGradeFxPalette m_ringPalette;
    Color m_ringHomeColor = Color.white;
    bool m_ringHomeColorCaptured;
    Vector3 m_glowHomeScale = Vector3.one;

    // 흔들림은 링과 같은 시계를 쓰되 더 빨리 끝난다. 무대 오프셋에 더하는 값이라 별도 상태가 없다.
    float m_shakeTime = -1f;

    // 링은 그림(원형 테두리 글로우) + 그 위에 얹는 절차적 선이다. 그림은 결을, 절차적 선은 두께를 준다 —
    // 그림만으로는 두께를 바꿀 수 없어(스프라이트에 새겨져 있다) 굵기 조절이 필요하면 선이 있어야 한다.
    // _RingRadius는 재질이 쥔다(그림의 테두리 반지름과 맞춰 둔 값). 배율은 트랜스폼이 함께 키운다.
    static readonly int ID_SPRITE_AMOUNT = Shader.PropertyToID("_SpriteAmount");
    static readonly int ID_RING_WIDTH    = Shader.PropertyToID("_RingWidth");
    static readonly int ID_RING_STRENGTH = Shader.PropertyToID("_RingStrength");
    static readonly int ID_RING_COLOR    = Shader.PropertyToID("_RingColor");

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
        m_ringTime = -1f;
        m_shakeTime = -1f;
        m_ringGrade = ECardGrade.Unknown;
        m_ringPalette = null;
        if (glow != null) glow.transform.localScale = m_glowHomeScale;
        Material t_mat = RingMaterial();
        if (t_mat != null)
        {
            t_mat.SetFloat(ID_RING_STRENGTH, 0f);
            RestoreRingColor(t_mat);
        }
        Apply();
    }

    /// <summary>이번 개봉 최고 등급을 링에 물린다. 실제 색 평가는 링 재생 중 매 프레임 수행한다.</summary>
    public void SetRingGrade(ECardGrade _grade, PackGradeFxPalette _palette)
    {
        m_ringGrade = _grade;
        m_ringPalette = _palette;

        if (_palette == null || !_palette.TryEvaluate(_grade, out _))
            RestoreRingColor(RingMaterial());
    }

    /// <summary>다 찢은 순간 팩 입구에서 링이 퍼진다(작게 시작해 커지며 얇아진다).
    /// 개봉이 시작되면 평소 광채는 이미 걷혀 있으므로(m_glowFade=0) 이 링만 보인다.
    /// 재생 중에 다시 불러도 처음부터 다시 퍼진다 — 겹쳐 봐야 두 번 터진 것으로 안 읽힌다.</summary>
    public void PlayOpenBurst()
    {
        m_ringTime = 0f;
        m_shakeTime = 0f;
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
        //
        // ⚠ 위 m_settle과 달리 지수 감쇠를 쓰지 않는다. 지수는 0에 닿지 않아 꼬리가 남는데,
        //   그 꼬리가 곧 "팩은 이미 다 내려갔는데 빛만 늦게 꺼진다"로 보인다.
        //   여기 필요한 것은 부드러운 수렴이 아니라 이동과 같은 시각에 끝나는 유한한 시간이라 등속으로 민다.
        float t_glowTarget = m_idleAllowed ? 1f : 0f;
        m_glowFade = glowFadeDuration > 0.001f
            ? Mathf.MoveTowards(m_glowFade, t_glowTarget, t_dt / glowFadeDuration)
            : t_glowTarget;

        if (m_ringTime >= 0f)
        {
            m_ringTime += t_dt;
            if (m_ringTime > ringDuration) m_ringTime = -1f;   // 다 퍼지면 평소 경로로 돌려준다
        }

        if (m_shakeTime >= 0f)
        {
            m_shakeTime += t_dt;
            if (m_shakeTime > shakeDuration) m_shakeTime = -1f;
        }

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
            stage.anchoredPosition = m_stageHome + StageOffset
                                   + new Vector2(0f, t_float * floatDistance)
                                   + ShakeOffset();
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

        float t_ambient = Mathf.Lerp(glowAlphaMin, glowAlphaMax, _pulse01) * m_glowFade;

        Material t_mat = RingMaterial();
        if (t_mat != null)
        {
            // 그래픽 알파는 1로 고정하고, 평소 방사광의 밝기는 재질이 따로 쥔다.
            // 한 알파로 둘을 조절하면 링을 보이려고 알파를 올리는 순간 꺼 둔 방사광까지 살아난다.
            var t_c = glow.color;
            t_c.a = 1f;
            glow.color = t_c;
            t_mat.SetFloat(ID_SPRITE_AMOUNT, t_ambient);
        }
        else
        {
            // 링 재질이 아니면(다른 재질로 갈아 끼운 경우) 예전처럼 그래픽 알파로만 조절한다.
            var t_c = glow.color;
            t_c.a = t_ambient;
            glow.color = t_c;
        }

        ApplyRing();

        // 회전에는 m_settle도 m_glowFade도 곱하지 않는다 — 누적 각도라 비율을 곱하면 멈출 때
        // 빛살이 0도로 되감긴다. 손가락이 닿아 아이들이 서면 m_time이 멈추므로 회전도 그 자리에 선다(그게 의도한 정지다).
        if (Mathf.Abs(glowSpinPeriod) > 0.01f)
            glow.transform.localRotation = Quaternion.Euler(0f, 0f, m_time * 360f / glowSpinPeriod);
    }

    // 개봉 충격. 랜덤 대신 펄린 노이즈를 쓴다 — 프레임마다 튀는 난수는 떨림이 아니라 지직거림으로 보인다.
    // 세기는 제곱으로 잦아든다(한 번 세게 치고 빨리 죽는다). 두 축에 서로 다른 노이즈 줄을 태워
    // x와 y가 같은 파형으로 움직이는(= 대각선으로만 흔들리는) 그림을 피한다.
    Vector2 ShakeOffset()
    {
        float t_peak = shakeAmplitude * ShakeScale();
        if (m_shakeTime < 0f || t_peak <= 0.0001f) return Vector2.zero;

        float t_p = Mathf.Clamp01(m_shakeTime / Mathf.Max(0.0001f, shakeDuration));
        float t_amp = t_peak * (1f - t_p) * (1f - t_p);
        float t_n = m_shakeTime * shakeFrequency;

        return new Vector2(
            (Mathf.PerlinNoise(t_n, 0.37f) * 2f - 1f) * t_amp,
            (Mathf.PerlinNoise(0.71f, t_n) * 2f - 1f) * t_amp);
    }

    float ShakeScale()
    {
        if (m_ringGrade == ECardGrade.Mythic) return mythicShakeScale;
        if (m_ringGrade == ECardGrade.Arcane) return arcaneShakeScale;
        if (m_ringGrade == ECardGrade.Rare) return rareShakeScale;
        return 1f;
    }

    // 링 그림(원형 테두리 글로우)을 작게 시작해 크게 부풀린다.
    //   배율: 3제곱 이즈아웃 — 초반에 확 벌어지고 끝에서 눕는다("팍"의 정체가 이 곡선이다).
    //   밝기: 뒤로 갈수록 사그라든다. 배율과 같은 곡선을 쓰면 다 퍼진 순간에도 밝아 잔상이 남는다.
    //
    // 밝기를 그래픽 알파가 아니라 재질의 스프라이트 몫으로 내는 이유는 ApplyGlow 주석 그대로다 —
    // 알파 하나로 평소 방사광과 링을 함께 조절하면 링을 켜는 순간 꺼 둔 방사광까지 살아난다.
    void ApplyRing()
    {
        Material t_mat = RingMaterial();

        if (m_ringTime < 0f)
        {
            // 끝났으면 씬 배치 크기로 돌려둔다 — 부푼 채로 두면 다음 개봉이 큰 원에서 시작한다.
            if (glow != null) glow.transform.localScale = m_glowHomeScale;
            if (t_mat != null)
            {
                t_mat.SetFloat(ID_RING_STRENGTH, 0f);
                RestoreRingColor(t_mat);
            }
            return;   // 스프라이트 몫은 ApplyGlow가 이미 평소 값으로 밀어 뒀다.
        }

        float t_p = Mathf.Clamp01(m_ringTime / Mathf.Max(0.0001f, ringDuration));
        float t_ease = 1f - Mathf.Pow(1f - t_p, 3f);

        if (glow != null)
            glow.transform.localScale = m_glowHomeScale * Mathf.Lerp(ringStartScale, ringEndScale, t_ease);

        if (t_mat != null)
        {
            if (m_ringPalette != null && m_ringPalette.TryEvaluate(m_ringGrade, out Color t_ringColor))
                t_mat.SetColor(ID_RING_COLOR, t_ringColor);
            else
                RestoreRingColor(t_mat);

            float t_fade = (1f - t_p) * (1f - t_p);
            t_mat.SetFloat(ID_SPRITE_AMOUNT, ringStrength * t_fade);
            t_mat.SetFloat(ID_RING_WIDTH, Mathf.Max(ringThickness, 0.001f));
            t_mat.SetFloat(ID_RING_STRENGTH, ringStrokeStrength * t_fade);
        }
    }

    // 재질 사본. UI Graphic은 material에 대입해야 사본이 물린다(MaterialPropertyBlock가 안 먹는다).
    Material RingMaterial()
    {
        if (glow == null) return null;
        if (m_ringMat != null) return m_ringMat;

        var t_src = glow.material;
        if (t_src == null || t_src.shader == null || !t_src.shader.name.Contains("PackRing")) return null;

        m_ringMat = new Material(t_src);
        if (m_ringMat.HasProperty(ID_RING_COLOR))
        {
            m_ringHomeColor = m_ringMat.GetColor(ID_RING_COLOR);
            m_ringHomeColorCaptured = true;
        }
        glow.material = m_ringMat;
        return m_ringMat;
    }

    void RestoreRingColor(Material _material)
    {
        if (_material != null && m_ringHomeColorCaptured && _material.HasProperty(ID_RING_COLOR))
            _material.SetColor(ID_RING_COLOR, m_ringHomeColor);
    }

    void OnDestroy()
    {
        if (m_ringMat != null) Destroy(m_ringMat);
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

        m_glowHomeScale = glow != null ? glow.transform.localScale : Vector3.one;

        m_shadowHome = shadow != null ? shadow.anchoredPosition : Vector2.zero;
        m_shadowHomeScale = shadow != null ? shadow.localScale : Vector3.one;

        var t_g = shadow != null ? shadow.GetComponent<Graphic>() : null;
        m_shadowHomeAlpha = t_g != null ? t_g.color.a : 1f;

        m_homeCaptured = true;
    }
}
