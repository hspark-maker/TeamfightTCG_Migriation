using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 팩 찢김 표현의 단일 창구. 진행도(0~1) 하나를 받아 "UI/PackTear"를 쓰는 네 그래픽에 똑같이 물린다.
//   몸통(구멍이 뚫린다) · 조각(뜯겨 들린다) · 입구 그늘(구멍에 깊이를 준다) · 찢김선 빛(선단이 밝다)
//
// 넷이 같은 찢김선 파라미터를 공유하는 것이 이 클래스의 존재 이유다 — 재질을 따로 만지면
// 조각과 구멍의 결이 어긋나 "뜯었다"가 즉시 깨진다. 진행도를 쓰는 지점을 여기 하나로 모은다.
//
// 찢김 결(노이즈)은 아트가 아니라 시드로 생성한다. 아트 의존이 없어 팩 스프라이트를 갈아끼워도
// 그대로 동작하고, 시드가 고정이라 매 개봉이 같은 결로 찢긴다(연출 재현성).
//
// ⚠ 재질은 런타임 사본으로 갈아끼운다. 에셋을 직접 쓰면 플레이 중에 쓴 진행도가 .mat 파일에 눌어붙는다.
public class PackTearSkin : MonoBehaviour
{
    [Header("찢김을 공유하는 그래픽 (전부 UI/PackTear 재질)")]
    [Tooltip("팩 몸통(_TearMode 0). 카드가 이 뒤에 있어야 구멍으로만 보인다.")]
    [SerializeField] Graphic front;
    [Tooltip("팩 뒷면(_TearMode 0). 봉지는 앞뒤가 함께 찢긴다 — 뒷면이 안 찢기면 " +
             "카드 뒤에 검은 판이 그대로 서서 \"뜯긴 봉지\"가 아니라 \"창이 뚫린 판\"이 된다. " +
             "다만 찢김선을 앞면보다 조금 높게(_TearY 크게) 잡아야 그 차이만큼 봉지 안쪽 입구가 드러난다.")]
    [SerializeField] Graphic back;
    [Tooltip("뜯겨 나가는 조각(_TearMode 1). 몸통이 지운 영역만 남는다.")]
    [SerializeField] Graphic lid;
    [Tooltip("입구 안쪽 그늘(_TearMode 2). 카드 위에, 몸통 아래에 그려야 한다.")]
    [SerializeField] Graphic mouthShadow;
    [Tooltip("찢김선을 따라 새는 빛(_TearMode 3). 가산 합성 재질을 쓴다. " +
             "이번 개봉의 최고 등급에 따라 색과 세기가 바뀐다(SetGlowGrade).")]
    [SerializeField] Graphic tearGlow;

    [Header("등급 빛")]
    [Tooltip("손가락 진행도를 주는 찢기 제스처. 이 진행도가 실제 찢김과 등급 빛을 함께 구동한다.")]
    [SerializeField] PackTearHandle tearHandle;
    [Tooltip("빛이 찢김선에서 얼마나 멀리 뻗는가. 재질의 _GlowRise를 덮어쓴다 — " +
             "색·세기와 같은 인스펙터에서 길이까지 잡으려고 여기로 끌어왔다.")]
    [Range(0.02f, 1f)] [SerializeField] float glowReach = 0.16f;
    [Tooltip("모든 등급이 공유하는 시작 색. 초반에는 등급을 숨겨야 중간부터 물드는 색이 신호가 된다.")]
    [SerializeField] Color baseGlow = new Color(0.88f, 0.9f, 0.92f, 1f);
    [Tooltip("등급 색이 배어 나오기 시작하는 진행도. 여기까지는 어느 등급이든 시작 색 그대로다.")]
    [Range(0f, 1f)] [SerializeField] float gradeTintStart = 0.5f;
    [Tooltip("등급 색이 완전히 드러나는 진행도. 시작점과 같거나 작으면 그 지점에서 즉시 바뀐다.")]
    [Range(0f, 1f)] [SerializeField] float gradeTintFull = 0.95f;
    [Tooltip("골드 등급의 빛 색. 알파는 씬에 저작된 빛의 알파를 상한으로 쓴다.")]
    [SerializeField] Color goldGlow = new Color(1f, 0.82f, 0.35f, 1f);
    [Tooltip("프리즘 등급 색상환이 도는 속도(1 = 초당 한 바퀴). 0이면 무지개가 멈춘 채 물든다.")]
    [SerializeField] float prismCycleSpeed = 0.5f;
    [Range(0f, 1f)] [SerializeField] float prismSaturation = 0.8f;
    [Range(0f, 1f)] [SerializeField] float prismValue = 1f;
    [Tooltip("진행도(0~1)를 빛 세기(0~1)로 바꾸는 곡선. 끝에서 급히 밝아져야 \"터지기 직전\"으로 읽힌다.")]
    [SerializeField] AnimationCurve glowRamp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("조각 들림")]
    [Tooltip("조각이 도는 축. 위치는 진행도에서 계산해 매 프레임 찢김 선단(아직 붙어 있는 지점)에 붙인다 " +
             "— 축을 고정하면 아직 안 뜯긴 자리까지 들려서 조각이 공중에 뜬 것처럼 보인다.")]
    [SerializeField] RectTransform lidPivot;
    [Tooltip("다 뜯었을 때 조각이 들린 각도(도). 조각 띠가 축의 왼쪽으로 뻗어 있어 부호가 곧 방향이다 — " +
             "음수(시계)라야 위로 들리고, 양수(반시계)면 되레 아래로 처져 제가 뚫은 입구를 도로 덮는다.")]
    [SerializeField] float peelAngle = -30f;
    [Tooltip("다 뜯었을 때 조각이 떠오른 거리(부모 로컬 = 캔버스 참조px).")]
    [SerializeField] float peelLift = 40f;

    [Header("조각 비산")]
    [Tooltip("날아가는 방향·거리(껍데기 로컬). 조각 띠가 통째로 화면 밖에 나가야 카드를 가리지 않는다 " +
             "— 껍데기 배율이 곱해지므로 띠 길이보다 넉넉히 잡는다.")]
    [SerializeField] Vector2 flyOffset = new Vector2(-220f, 700f);
    [Tooltip("날아가며 더 도는 각도(도). 축이 조각 띠의 오른쪽 끝이라 부호가 곧 방향이다 — " +
             "양수(반시계)면 왼쪽으로 뻗은 띠가 아래로 쓸려 카드를 덮고, 음수(시계)면 위로 젖혀진다.")]
    [SerializeField] float flySpin = -60f;
    [SerializeField] float flyDuration = 0.28f;
    [Tooltip("조각을 사라지게 할 CanvasGroup. 미배선이면 날아가기만 한다.")]
    [SerializeField] CanvasGroup lidGroup;

    [Header("찢김 결")]
    [Tooltip("팩 폭을 가로지르는 굵은 결의 개수. 작을수록 크게 뜯긴다.")]
    [Range(4, 64)] [SerializeField] int jagSegments = 14;
    [Tooltip("결 생성 시드. 고정이라 매 개봉이 같은 모양으로 찢긴다.")]
    [SerializeField] int jagSeed = 20260729;

    static readonly int ID_TEAR_PROGRESS = Shader.PropertyToID("_TearProgress");
    static readonly int ID_TEAR_DIRECTION = Shader.PropertyToID("_TearDirection");
    static readonly int ID_GLOW_RISE      = Shader.PropertyToID("_GlowRise");
    static readonly int ID_JAG_TEX       = Shader.PropertyToID("_JagTex");
    static readonly int ID_TEAR_Y        = Shader.PropertyToID("_TearY");

    // 런타임 사본 재질(에셋 오염 방지). 파괴 시 함께 정리한다.
    Material[] m_mats;

    Texture2D m_jag;

    // 조각의 원알파(개봉마다 되돌리는 기준). 자리·회전은 진행도에서 계산하므로 캡처할 것이 없다.
    float m_lidHomeAlpha = 1f;
    bool m_homeCaptured;

    // 찢기 상태와 등급 빛은 같은 진행도 하나를 공유한다.
    ECardGrade m_glowGrade;
    float m_tearProgress;
    float m_tearDirection = 1f;
    Color m_glowHome = Color.white;
    bool  m_glowHomeCaptured;

    /// <summary>찢김선의 기준 높이(스프라이트 UV v). 카드를 입구에 맞춰 놓을 때 참고.</summary>
    public float TearLineV => m_mats != null && m_mats.Length > 0 && m_mats[0] != null
        ? m_mats[0].GetFloat(ID_TEAR_Y)
        : 0.874f;

    void Awake()
    {
        // 재질을 먼저 물려야 TearLineV가 실제 값을 돌려준다(조각 축 계산이 이 값을 쓴다).
        BuildJagTexture();
        InstanceMaterials();
        CaptureHome();
        CaptureGlowHome();   // 세기를 물리기 전에 씬 저작 색을 잡아 둔다(상한의 기준).
        ApplyGlowReach();
        SetProgress(0f);
    }

    // 손가락 진행도는 다른 소품(PackShellRig·PackSpecularSweep)과 같은 방식으로 직접 받는다 —
    // 진행자(PackRevealView)를 거치면 빛 하나 때문에 단계 전이에 배관이 생긴다.
    void OnEnable()
    {
        if (tearHandle != null) tearHandle.OnProgress += HandleTearProgress;
    }

    void OnDisable()
    {
        if (tearHandle != null) tearHandle.OnProgress -= HandleTearProgress;
    }

    // 무지개는 색이 계속 돌아야 무지개로 읽힌다 — 프리즘일 때만, 그리고 빛이 켜져 있을 때만 다시 칠한다.
    void Update()
    {
        if (m_glowGrade != ECardGrade.Prism || tearGlow == null) return;
        if (GradeTint() <= 0f) return;   // 아직 시작 색 구간이면 다시 칠할 것이 없다

        ApplyGlow();
    }

    void OnDestroy()
    {
        if (m_mats != null)
            for (int t_i = 0; t_i < m_mats.Length; t_i++)
                if (m_mats[t_i] != null) Destroy(m_mats[t_i]);

        if (m_jag != null) Destroy(m_jag);
    }

    /// <summary>팩 그림을 이번에 산 팩의 것으로 갈아끼운다(개봉 시작마다). null이면 씬에 배치된 기본 그림을 유지.
    /// 다섯 그래픽이 같은 스프라이트를 써야 한다 — 그늘·빛은 팩 실루엣의 알파로 자기 범위를 잡으므로
    /// 한 장만 갈면 몸통과 윤곽이 어긋난다. 찢김 결은 시드 생성이라 그림이 바뀌어도 그대로 동작한다.</summary>
    public void ApplyPackArt(Sprite _art)
    {
        if (_art == null) return;

        var t_targets = TearTargets();
        for (int t_i = 0; t_i < t_targets.Length; t_i++)
            if (t_targets[t_i] is Image t_image) t_image.sprite = _art;
    }

    /// <summary>진행도 반영 지점 하나. 네 그래픽과 조각 들림이 여기서만 갈라져 결이 어긋나지 않는다.</summary>
    public void SetProgress(float _value)
    {
        float t_progress = Mathf.Clamp01(_value);

        if (m_mats != null)
            for (int t_i = 0; t_i < m_mats.Length; t_i++)
                if (m_mats[t_i] != null)
                {
                    m_mats[t_i].SetFloat(ID_TEAR_PROGRESS, t_progress);
                    m_mats[t_i].SetFloat(ID_TEAR_DIRECTION, m_tearDirection);
                }

        ApplyPeel(t_progress);

        m_tearProgress = t_progress;
        ApplyGlow();
    }

    /// <summary>이번 개봉에서 나올 <b>최고 등급</b>을 물려 찢김선 빛의 색과 세기를 정한다.
    /// 골드=금빛, 프리즘=무지갯빛. 그 아래 등급은 씬에 저작된 원래 빛 그대로다 —
    /// 평범한 팩에서도 빛이 새면 "고등급이 온다"는 신호 자체가 죽는다.
    ///
    /// <see cref="ResetTear"/> <b>뒤에</b> 부를 것. 되돌리기가 세기를 0으로 내리므로 순서가 뒤집히면
    /// 등급을 물린 첫 프레임이 곧바로 지워진다.</summary>
    public void SetGlowGrade(ECardGrade _grade)
    {
        CaptureGlowHome();
        m_glowGrade = _grade;
        ApplyGlow();
    }

    /// <summary>다 뜯긴 조각을 날려 보낸다. 반환 시퀀스를 호출부 흐름에 끼우면 스킵 한 번에 함께 끝난다.</summary>
    public Sequence PlayLidFly()
    {
        if (lidPivot == null) return null;

        CaptureHome();
        lidPivot.DOKill();

        // 지금 있는 자리에서 이어 날아간다 — 다 뜯긴 시점의 축은 진행도가 이미 제자리에 놓아 두었다.
        var t_seq = DOTween.Sequence().SetLink(gameObject);
        Vector2 t_flyOffset = new Vector2(flyOffset.x * m_tearDirection, flyOffset.y);
        float t_flyAngle = (peelAngle + flySpin) * m_tearDirection;
        t_seq.Join(lidPivot.DOAnchorPos(lidPivot.anchoredPosition + t_flyOffset, flyDuration).SetEase(Ease.OutCubic));
        t_seq.Join(lidPivot.DOLocalRotate(new Vector3(0f, 0f, t_flyAngle), flyDuration).SetEase(Ease.OutCubic));

        if (lidGroup != null)
        {
            lidGroup.DOKill();
            // 카드 영역을 벗어나기 시작하는 즉시 옅어진다 — 늦게 지우면 솟아오르는 카드를 조각이 스친다.
            // 그렇다고 0에서 시작하면 뜯긴 조각을 볼 새가 없으므로 초반 한 뼘은 또렷이 남긴다.
            t_seq.Insert(flyDuration * 0.25f, lidGroup.DOFade(0f, flyDuration * 0.75f).SetEase(Ease.InQuad));
        }

        return t_seq;
    }

    /// <summary>조각을 즉시 감춘다(비산 연출을 건너뛴 스킵 경로).</summary>
    public void HideLid()
    {
        if (lidPivot != null) lidPivot.DOKill();

        if (lidGroup != null) { lidGroup.DOKill(); lidGroup.alpha = 0f; }
        else if (lid != null) lid.gameObject.SetActive(false);
    }

    /// <summary>붙어 있던 처음 상태로 되돌린다(개봉 시작마다 — 지난 개봉이 날려 보낸 자리를 물려받지 않게).</summary>
    public void ResetTear()
    {
        CaptureHome();

        if (lidPivot != null) lidPivot.DOKill();   // 자리·회전은 바로 아래 SetProgress(0)이 다시 잡는다
        if (lid != null) lid.gameObject.SetActive(true);

        if (lidGroup != null)
        {
            lidGroup.DOKill();
            lidGroup.alpha = m_lidHomeAlpha;
        }

        m_tearProgress = 0f;
        m_tearDirection = 1f;
        SetProgress(0f);   // 빛 세기도 여기서 함께 0으로 돌아간다.
    }

    // 뜯긴 만큼 조각이 들린다 — 축이 찢김 선단(아직 붙어 있는 지점)을 따라가야 그 지점은 붙은 채로,
    // 이미 지나온 왼쪽만 벌어진다. 축을 오른쪽 끝에 고정하면 선단까지 들려 조각이 통째로 떠 보인다.
    void ApplyPeel(float _progress)
    {
        if (lidPivot == null) return;
        if (DOTween.IsTweening(lidPivot)) return;   // 비산 중에는 진행도가 자리를 되돌리지 않게 둔다.

        var t_pivot = PivotAt(_progress);
        lidPivot.anchoredPosition = t_pivot + new Vector2(0f, peelLift * _progress);
        lidPivot.localRotation = Quaternion.Euler(0f, 0f, peelAngle * _progress * m_tearDirection);

        // 축이 움직인 만큼 조각을 되밀어야 그림이 팩 위에 정확히 겹친 채로 남는다.
        if (lid != null) ((RectTransform)lid.transform).anchoredPosition = -t_pivot;
    }

    // 진행도에 대응하는 찢김 선단의 자리(껍데기 로컬). 조각 rect가 곧 팩 rect라 거기서 폭·높이를 얻는다.
    Vector2 PivotAt(float _progress)
    {
        var t_rect = lid != null ? ((RectTransform)lid.transform).rect : new Rect(0f, 0f, 677f, 1015.5f);
        return new Vector2(t_rect.width * (_progress - 0.5f) * m_tearDirection,
                           (TearLineV - 0.5f) * t_rect.height);
    }

    void HandleTearProgress(float _value)
    {
        if (tearHandle != null && tearHandle.DirectionSign != 0f)
            m_tearDirection = Mathf.Sign(tearHandle.DirectionSign);

        SetProgress(_value);
    }

    // 등급 색을 빛에 칠한다. 세기는 알파로만 낸다 — 크기를 키우면 찢김선과 어긋나 빛이 선에서 떠 보인다.
    void ApplyGlow()
    {
        if (tearGlow == null) return;
        CaptureGlowHome();

        // 어느 등급이든 밝은 회색으로 시작한다 — 첫 빛에서 등급이 드러나면 찢는 내내 볼 것이 없다.
        // 중간을 넘기면서 색이 배어 나오는 그 변화가 곧 "무엇이 나오는가"의 신호다.
        Color t_color = Color.Lerp(baseGlow, GradeColor(), GradeTint());

        // 알파 상한은 씬에 저작된 빛의 알파다 — 코드가 그 위로 올리면 저작자가 잡은 밝기가 무의미해진다.
        t_color.a = m_glowHome.a * Mathf.Clamp01(glowRamp.Evaluate(m_tearProgress));
        tearGlow.color = t_color;
    }

    // 등급 색이 얼마나 배었는가(0~1). 등급 미달은 끝까지 0 — 시작 색 그대로 간다.
    float GradeTint()
    {
        if (m_glowGrade != ECardGrade.Gold && m_glowGrade != ECardGrade.Prism) return 0f;
        if (m_tearProgress <= gradeTintStart) return 0f;
        if (gradeTintFull <= gradeTintStart) return 1f;   // 구간이 없으면 그 지점에서 즉시 물든다

        return Mathf.Clamp01((m_tearProgress - gradeTintStart) / (gradeTintFull - gradeTintStart));
    }

    Color GradeColor() => m_glowGrade == ECardGrade.Prism
        // 시각을 위상으로 쓴다 — 개봉마다 상태를 들고 다니지 않아도 색이 이어서 돈다.
        ? Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * prismCycleSpeed, 1f), prismSaturation, prismValue)
        : goldGlow;

    // 빛 길이는 **런타임 사본에만** 쓴다 — 재질 에셋에 직접 쓰면 플레이 중 값이 .mat에 눌어붙는다
    // (진행도를 사본으로 돌리는 이유와 같다).
    void ApplyGlowReach()
    {
        Material t_mat = GlowMaterial();
        if (t_mat != null) t_mat.SetFloat(ID_GLOW_RISE, glowReach);
    }

    // 빛 그래픽의 재질 사본. 인덱스를 박아 두면 TearTargets 순서가 바뀔 때 조용히 다른 재질을 만진다.
    Material GlowMaterial()
    {
        if (m_mats == null || tearGlow == null) return null;

        var t_targets = TearTargets();
        for (int t_i = 0; t_i < t_targets.Length && t_i < m_mats.Length; t_i++)
            if (t_targets[t_i] == tearGlow) return m_mats[t_i];

        return null;
    }

#if UNITY_EDITOR
    // 플레이 중 슬라이더를 끌면 그 자리에서 보인다. 에디터 정지 상태에서는 사본이 없어 무동작 —
    // 그때는 재질 에셋의 _GlowRise가 그대로 쓰인다.
    void OnValidate()
    {
        if (Application.isPlaying) ApplyGlowReach();
    }
#endif

    void CaptureGlowHome()
    {
        if (m_glowHomeCaptured || tearGlow == null) return;

        m_glowHome = tearGlow.color;
        m_glowHomeCaptured = true;
    }

    void CaptureHome()
    {
        if (m_homeCaptured) return;

        m_lidHomeAlpha = lidGroup != null ? lidGroup.alpha : 1f;
        m_homeCaptured = true;
    }

    // 찢김선을 공유하는 그래픽 묶음. 재질 사본과 그림 교체가 같은 목록을 봐야 한 장만 빠지는 일이 없다.
    Graphic[] TearTargets() => new[] { front, back, lid, mouthShadow, tearGlow };

    // 네 그래픽의 재질을 사본으로 갈고, 공유 노이즈를 물린다.
    void InstanceMaterials()
    {
        var t_targets = TearTargets();
        m_mats = new Material[t_targets.Length];

        for (int t_i = 0; t_i < t_targets.Length; t_i++)
        {
            var t_g = t_targets[t_i];
            if (t_g == null) continue;

            var t_src = t_g.material;
            if (t_src == null || t_src.shader == null || !t_src.shader.name.Contains("PackTear"))
            {
                Debug.LogWarning($"[PackTearSkin] {t_g.name}의 재질이 UI/PackTear가 아니다 — 찢김이 적용되지 않는다.");
                continue;
            }

            var t_inst = new Material(t_src);
            if (m_jag != null) t_inst.SetTexture(ID_JAG_TEX, m_jag);

            t_g.material = t_inst;
            m_mats[t_i] = t_inst;
        }
    }

    // 찢김 결. R = 굵은 결, G = 잔결. 폭이 곧 굵은 결의 개수다(셰이더가 u에 그대로 한 바퀴 감는다).
    // 텍스처로 만드는 이유: 셰이더 안에서 난수를 만들면 GPU마다 결이 달라진다.
    void BuildJagTexture()
    {
        int t_w = Mathf.Max(4, jagSegments);

        m_jag = new Texture2D(t_w, 1, TextureFormat.RGBA32, false)
        {
            name = "PackTearJag",
            wrapMode = TextureWrapMode.Repeat,   // 잔결은 같은 패턴을 여러 바퀴 감아 쓴다.
            filterMode = FilterMode.Bilinear,    // 텍셀 사이 선형 보간 = 모서리가 살아 있는 지그재그.
            hideFlags = HideFlags.HideAndDontSave,
        };

        var t_rng = new System.Random(jagSeed);
        var t_px = new Color32[t_w];
        for (int t_i = 0; t_i < t_w; t_i++)
            t_px[t_i] = new Color32((byte)t_rng.Next(256), (byte)t_rng.Next(256), 0, 255);

        m_jag.SetPixels32(t_px);
        m_jag.Apply(false, false);
    }
}
