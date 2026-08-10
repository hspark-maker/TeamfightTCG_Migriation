using UnityEngine;

// 드래그 카드를 "지금 꽂을 슬롯" 안으로 옮겨 놓고, 진행도(0~1)를 카드의 위치·기울기로 환산한다.
//
// ⚠ 씰은 여기 없다 — **도감 칸 자체가 슬리브 두 겹**이고, 드래그 카드는 그 사이(`InsertDock`)로 들어간다.
//   번호를 덮으며 비닐(앞면) 뒤로 잠기는 것이 "밀어 넣는" 그림이라, 패널에 씰을 하나 더 띄울 필요가 없다.
//   (2026-08-10 이전에는 패널에 AlbumCardSlot 복제본 `Sleeve_Slot`을 띄워 진짜 칸 위를 덮었다.
//    화면에 씰이 두 벌 존재하는 값이라 폐기했다 — 되돌리지 말 것.)
//
// ⚠ 부모를 옮기는 컴포넌트다 — 스텝마다 대상 칸이 달라지므로 홈 부모(패널)를 첫 이동 직전에 기억해 두고
//   세션이 끝날 때 `Release()`로 되돌린다. 안 되돌리면 다음 세션이 남의 칸 안에서 시작한다.
//
// ■ 이 파일이 만드는 손맛: "카드를 슬리브에 꽂을 때는 한 번에 안 들어간다"
//   ① 한 번의 스와이프로 다 안 들어간다 — 손을 떼도 **제자리에 남고**(세션이 되돌리지 않는다) 다음 스와이프가 이어 민다.
//   ② 스와이프마다 카드가 **까딱 다른 각도**로 걸린다(매번 새 seed).
//   ③ 그 까딱거림의 진폭은 **깊이 들어갈수록 저절로 줄어** 마지막엔 0으로 수렴한다.
//
//   ③이 공짜로 나오는 이유가 이 구조의 전부다 — 각도를 직접 줄이는 코드는 없고,
//   "깊이 d까지 들어간 카드가 씰 입구 폭 안에 남을 수 있는 최대 각도"(`AllowedTilt`)라는 **봉투**가 있을 뿐이다.
//   실제 각도 = 봉투 × seed(-1~1). 봉투가 d에 대해 단조 감소하므로 seed를 아무리 흔들어도 수렴한다.
//
//   콜라이더를 쓰지 않는다 — 이 식은 진행도의 순수 함수라 트윈으로 되감아도 각도가 정확히 되돌아온다.
//   진행도가 그림의 단일 진실원이므로, **y만 따로 트윈하면 각도가 그 자리에 얼어붙는다.**
public class AlbumSleeveView : MonoBehaviour
{
    [SerializeField] RectTransform panelRect;   // 좌표 변환 기준 레이어(= 삽입 패널 루트)
    [SerializeField] RectTransform cardHolder;  // 드래그 카드 부모

    [Header("기울기")]
    [Tooltip("입구 밖(진행도 0 근처)에서 허용하는 최대 기울기(도). 봉투의 천장이다.")]
    [SerializeField] float maxTilt = 14f;
    [Tooltip("스와이프마다 뽑는 기울기 진폭의 하한(봉투 대비). 0이면 가끔 똑바로 들어가 까딱거림이 끊긴다.")]
    [Range(0f, 1f)] [SerializeField] float minTiltAmount = 0.4f;
    [Tooltip("직전과 반대쪽으로 기울 확률. 1이면 좌우가 규칙적으로 번갈아 보인다.")]
    [Range(0f, 1f)] [SerializeField] float flipChance = 0.7f;
    [Tooltip("새 각도로 갈아타는 데 걸리는 삽입 깊이(카드 높이 대비). 0이면 손을 대는 순간 각도가 튄다.")]
    [Range(0.01f, 0.4f)] [SerializeField] float tiltBlendDepth = 0.07f;
    [Tooltip("기울어진 만큼 옆으로도 밀린다(카드 폭 대비). 0이면 항상 칸 정중앙.")]
    [Range(0f, 0.3f)] [SerializeField] float shiftRatio = 0.07f;

    [Header("덜덜거림 (stick-slip)")]
    [Tooltip("한 번에 미끄러져 들어가는 단위(카드 높이 대비). 손가락은 연속으로 움직여도 카드는 이 단위로 툭툭 들어간다.")]
    [Range(0.005f, 0.12f)] [SerializeField] float slipStep = 0.035f;
    [Tooltip("미끄러질 때마다 각도가 튀는 몫(0~1). 0이면 계단만 지고 각도는 얌전하다.")]
    [Range(0f, 1f)] [SerializeField] float slipTiltKick = 0.45f;
    [Tooltip("손가락이 닿아 있는 동안의 잔떨림 각도(도). 밀리지 않고 버티는 순간에도 카드가 떤다.")]
    [SerializeField] float shakeAngle = 0.7f;
    [Tooltip("잔떨림 가로 진폭(카드 폭 대비).")]
    [Range(0f, 0.05f)] [SerializeField] float shakeShift = 0.012f;
    [Tooltip("잔떨림 속도. 높을수록 거칠다.")]
    [SerializeField] float shakeSpeed = 26f;

    [Header("씰 입구")]
    [Tooltip("입구가 카드보다 이만큼 넓다고 본다(카드 폭 대비). 클수록 헐겁게, 작을수록 빡빡하게 끼워진다.")]
    [Range(0.02f, 0.4f)] [SerializeField] float mouthClearance = 0.12f;
    [Tooltip("이 진행도부터 남은 기울기를 마저 편다 — 안착 순간 각도가 정확히 0이어야 바꿔치기가 안 보인다.")]
    [Range(0.4f, 0.95f)] [SerializeField] float uprightFrom = 0.75f;
    [Tooltip("최대로 기울 수 있는 구간에서 깎이는 진행 속도. 0.55면 손가락 이동의 45%만 들어간다(모서리가 걸린 느낌).")]
    [Range(0f, 0.85f)] [SerializeField] float resistanceMax = 0.55f;

    const int RESIST_SAMPLES = 33;   // 저항 적분 해상도. 32구간이면 카드 높이의 3% 단위 — 눈으로는 연속이다

    readonly float[] m_effort = new float[RESIST_SAMPLES];   // 깊이별 누적 수고(정규화). BakeResistance가 굽는다

    float     m_cardHeight;   // = 정렬된 슬롯 높이. 진행도 1의 이동 거리
    float     m_cardWidth;    // = 정렬된 슬롯 폭. 입구 제약 계산의 기준
    float     m_homeX;
    float     m_homeY;        // 진행도 0의 카드 y(슬롯 바로 위에 통째로 떠 있는 자리)
    float     m_seedFrom;     // 직전 스와이프의 기울기 계수(-1~1)
    float     m_seedTo;       // 이번 스와이프의 기울기 계수
    float     m_seedDepth;    // 갈아타기가 시작된 깊이 — 여기부터 tiltBlendDepth만큼 밀면 새 각도가 된다
    float     m_progress;
    Vector2   m_basePos;      // 잔떨림을 얹기 전의 계산된 자세. 떨림은 이 위에 덧칠했다 걷는다
    float     m_baseAngle;
    bool      m_pushing;      // 손가락이 닿아 있는가(잔떨림 스위치)
    bool      m_layerWarned;  // 배선 누락 경고는 카드마다 쏟지 않고 한 번만
    Transform m_dockHome;     // cardHolder의 원래 부모(= 패널). 옮기기 전에 한 번만 기억한다

    /// <summary>진행도 1이 이동하는 거리(캔버스 단위). 드래그 임계의 기준이 된다.</summary>
    public float CardHeight => this.m_cardHeight;

    /// <summary>마지막으로 반영된 진행도. 안착·되밀림 트윈의 시작값이다.</summary>
    public float Progress => this.m_progress;

    public RectTransform CardHolder => this.cardHolder;

    /// <summary>드래그 카드를 대상 칸의 씰 사이(`InsertDock`)로 옮기고 칸 크기에 맞춘다.
    /// GridRatioFitter가 cellSize를 런타임에 정하므로 호출 전에 레이아웃이 확정돼 있어야 한다.
    /// _dock이 null이면 패널 좌표계에 그대로 띄운다(가림 없는 폴백).</summary>
    public void AlignTo(RectTransform _slotRect, RectTransform _dock)
    {
        if (_slotRect == null || this.cardHolder == null) return;

        if (this.m_dockHome == null) this.m_dockHome = this.cardHolder.parent;

        if (_dock != null)
        {
            // 칸 안으로 들어가면 좌표계가 곧 칸이다 — 중앙이 (0,0)이라 레이어 변환이 필요 없다.
            this.cardHolder.SetParent(_dock, false);
            this.Place(_slotRect.rect.size, Vector2.zero);
            return;
        }

        var t_layer = this.ResolveLayer();
        if (t_layer == null) return;

        this.cardHolder.SetParent(t_layer, false);

        // 그리드 셀은 부모 체인에서 배율을 먹는다 — 그 배율을 흡수해야 패널 좌표계의 실제 크기가 나온다.
        float   t_ratio = ResolveScaleRatio(t_layer, _slotRect);
        Vector2 t_size  = _slotRect.rect.size * t_ratio;

        // ToLayerLocal은 대상의 pivot 위치를 준다 — 중앙 정렬로 쓰려면 pivot 차이만큼 되민다.
        Vector2 t_center = UiGainBurst.ToLayerLocal(t_layer, _slotRect)
                         + (new Vector2(0.5f, 0.5f) - _slotRect.pivot) * t_size;

        this.Place(t_size, t_center);
    }

    /// <summary>스와이프가 시작될 때마다 부른다 — 이번에 걸릴 각도를 새로 뽑는다.
    /// 봉투(AllowedTilt)가 깊이에 따라 이미 좁아져 있으므로, **깊을수록 아무리 뽑아도 덜 흔들린다**.
    /// 지금 각도에서 출발해 tiltBlendDepth만큼 밀리는 동안 새 각도로 갈아탄다(손을 대는 순간 튀지 않게).</summary>
    public void NudgeTilt()
    {
        float t_depth = this.DepthAt(this.m_progress);

        this.m_seedFrom  = this.SeedAt(t_depth);
        this.m_seedTo    = RollSeed(this.m_seedFrom, this.minTiltAmount, this.flipChance);
        this.m_seedDepth = t_depth;
    }

    /// <summary>카드 홀더를 패널로 되돌린다(세션 종료·중단 공통). 멱등이다.</summary>
    public void Release()
    {
        if (this.cardHolder == null) return;

        this.m_pushing = false;

        // 기울어진 채로 홈에 돌아가면 다음 카드가 뜨기 전 한 프레임 동안 그 각도가 비친다.
        this.cardHolder.localRotation = Quaternion.identity;
        this.m_progress               = 0f;

        if (this.m_dockHome == null) return;
        if (this.cardHolder.parent == this.m_dockHome) return;

        this.cardHolder.SetParent(this.m_dockHome, false);
    }

    // (진행도 → y를 따로 묻는 창구는 두지 않는다. 저항이 끼어든 뒤로 진행도와 거리가 비례하지 않아
    //  "목표 y만 받아 트윈하는" 사용법이 곧 각도 정지 버그가 된다 — 움직이는 길은 SetProgress 하나뿐이다.)

    /// <summary>손가락이 카드에 닿아 있는가. 켜져 있는 동안 카드가 잔떨림을 낸다
    /// (버티는 순간에도 떨어야 "빡빡하다"로 읽힌다).</summary>
    public void SetPushing(bool _on)
    {
        this.m_pushing = _on;
        if (!_on) this.ApplyPose(0f, 0f);   // 떨림을 걷고 계산된 자세로 되돌린다
    }

    /// <summary>진행도 하나로 위치·기울기·좌우 어긋남을 전부 정한다.
    /// ⚠ 이것이 그림의 단일 진실원이다 — 바깥에서 y만 따로 트윈하면 각도가 그 자리에 얼어붙는다.</summary>
    public void SetProgress(float _p)
    {
        if (this.cardHolder == null) return;

        float t_p = Mathf.Clamp01(_p);

        // ■ stick-slip — 손가락은 연속으로 움직이지만 카드는 slipStep 단위로 **툭툭** 들어간다.
        //   계단을 내림(Floor)으로 잡는 것이 곧 정지마찰이다: 다음 눈금에 닿기 전까지 카드는 버틴다.
        float t_raw   = this.DepthAt(t_p);
        float t_unit  = Mathf.Max(0.001f, this.m_cardHeight * this.slipStep);
        int   t_index = Mathf.FloorToInt(t_raw / t_unit);
        float t_depth = t_p >= 1f ? this.m_cardHeight : Mathf.Min(t_raw, t_index * t_unit);

        // 눈금마다 각도가 튄다 — 미끄러진 순간의 충격이다. 눈금 번호로 결정하므로 되감아도 같은 값이다.
        float t_seed = Mathf.Clamp(Mathf.Lerp(this.SeedAt(t_depth), Hash11(t_index), this.slipTiltKick), -1f, 1f);

        this.m_baseAngle = this.AllowedTilt(t_depth) * t_seed;

        // 좌우 어긋남은 기울기에 매달아 둔다 — 각도가 0으로 수렴하면 x도 같이 칸 중앙으로 회수된다.
        float t_shift = this.m_cardWidth * this.shiftRatio * (this.maxTilt > 0f ? this.m_baseAngle / this.maxTilt : 0f);

        this.m_basePos  = new Vector2(this.m_homeX + t_shift, this.m_homeY - t_depth);
        this.m_progress = t_p;

        this.ApplyPose(0f, 0f);
    }

    // 잔떨림은 진행도와 무관한 덧칠이라 SetProgress의 순수성을 건드리지 않는다 —
    // 계산된 자세(m_base*)는 그대로 두고 그 위에 얹었다 걷는다.
    void LateUpdate()
    {
        if (!this.m_pushing || this.cardHolder == null) return;

        // 봉투가 좁아진 만큼 떨림도 잦아든다 — 다 들어간 카드가 부르르 떨면 안착이 안 끝난 것처럼 보인다.
        float t_env = this.maxTilt > 0f ? this.AllowedTilt(this.m_homeY - this.m_basePos.y) / this.maxTilt : 0f;
        if (t_env <= 0f) return;

        float t_t = Time.unscaledTime * this.shakeSpeed;

        this.ApplyPose((Mathf.PerlinNoise(t_t, 0.37f) * 2f - 1f) * this.shakeAngle * t_env,
                       (Mathf.PerlinNoise(0.71f, t_t) * 2f - 1f) * this.m_cardWidth * this.shakeShift * t_env);
    }

    void ApplyPose(float _angleAdd, float _shiftAdd)
    {
        if (this.cardHolder == null) return;

        this.cardHolder.anchoredPosition = this.m_basePos + new Vector2(_shiftAdd, 0f);
        this.cardHolder.localRotation    = Quaternion.Euler(0f, 0f, this.m_baseAngle + _angleAdd);
    }

    // 눈금 번호 → -1~1. 난수를 쓰면 같은 눈금을 되감을 때 값이 달라져 카드가 지직거린다.
    static float Hash11(int _i)
    {
        float t_v = Mathf.Sin(_i * 127.1f) * 43758.5453f;
        return (t_v - Mathf.Floor(t_v)) * 2f - 1f;
    }

    // 카드는 칸과 같은 크기다 — 진짜 칸의 카드도 칸 전체를 쓰므로 안착 순간 바꿔치기해도 크기가 튀지 않는다.
    // (입구가 카드보다 넓다는 여유분은 크기가 아니라 mouthClearance라는 가정으로만 만든다)
    void Place(Vector2 _size, Vector2 _center)
    {
        this.m_cardHeight = Mathf.Max(1f, _size.y);
        this.m_cardWidth  = Mathf.Max(1f, _size.x);
        this.m_homeX      = _center.x;
        this.m_homeY      = _center.y + this.m_cardHeight;   // 진행도 0 = 칸 바로 위, 겹침 0
        this.m_progress   = 0f;

        // 첫 각도도 스와이프와 같은 방식으로 뽑는다 — 카드마다 다른 각도로 떠 있어야 규칙성이 안 읽힌다.
        this.m_seedTo    = RollSeed(0f, this.minTiltAmount, this.flipChance);
        this.m_seedFrom  = this.m_seedTo;
        this.m_seedDepth = 0f;

        this.BakeResistance();

        Fit(this.cardHolder, _size, new Vector2(this.m_homeX, this.m_homeY));
        this.SetProgress(0f);
    }

    // ■ 봉투 — 깊이 _depth까지 들어간 카드가 씰 입구 폭 안에 남을 수 있는 최대 기울기.
    //
    //   들어간 부분의 가로 반경 ≈ a·cosθ + d·sinθ = R·cos(θ−φ)   (a=카드 반폭, R=√(a²+d²), φ=atan2(d,a))
    //   이것이 입구 반폭 C 이하여야 하므로 θmax = φ − acos(C/R).
    //   d가 커질수록 단조 감소 → **스와이프마다 각도를 새로 뽑아도 진폭이 저절로 수렴한다.**
    float AllowedTilt(float _depth)
    {
        float t_abs = this.maxTilt;
        if (t_abs <= 0f) return 0f;

        if (_depth > 0f)
        {
            float t_a = this.m_cardWidth * 0.5f;
            float t_c = t_a * (1f + this.mouthClearance);
            float t_r = Mathf.Sqrt(t_a * t_a + _depth * _depth);

            // R ≤ C면 아직 입구가 카드를 붙잡지 못한다 — 천장까지 자유롭게 기운다.
            if (t_r > t_c)
            {
                float t_limit = (Mathf.Atan2(_depth, t_a) - Mathf.Acos(Mathf.Clamp01(t_c / t_r))) * Mathf.Rad2Deg;
                t_abs = Mathf.Min(t_abs, Mathf.Max(0f, t_limit));
            }
        }

        // 기하 제약만으로는 끝에서 2~3°가 남는다 — 안착 순간의 각도 0은 바꿔치기 계약이라 여기서 마저 편다.
        float t_p = this.m_cardHeight > 0f ? _depth / this.m_cardHeight : 0f;
        return t_abs * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(this.uprightFrom, 1f, t_p)));
    }

    // 이번 스와이프의 기울기 계수(-1~1). 깊이로 갈아타므로 **되감아도 같은 값이 나온다**(시간을 쓰지 않는다).
    float SeedAt(float _depth)
    {
        if (this.tiltBlendDepth <= 0f || this.m_cardHeight <= 0f) return this.m_seedTo;

        float t_span = this.m_cardHeight * this.tiltBlendDepth;
        float t_t    = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_depth - this.m_seedDepth) / t_span));

        return Mathf.Lerp(this.m_seedFrom, this.m_seedTo, t_t);
    }

    // 직전과 반대쪽으로, 눈에 보일 만큼의 진폭으로. 봉투가 곱해지므로 깊을수록 실제 각도는 작아진다.
    static float RollSeed(float _prev, float _min, float _flip)
    {
        float t_amount = Random.Range(Mathf.Clamp01(_min), 1f);

        float t_sign = _prev > 0f ? 1f : _prev < 0f ? -1f : (Random.value < 0.5f ? -1f : 1f);
        if (Random.value < _flip) t_sign = -t_sign;

        return t_amount * t_sign;
    }

    // ■ 저항 — "입구에서 기울 수 있는 구간일수록 잘 안 들어간다".
    //
    //   실효 속도 = 1 − resistanceMax·(봉투/천장)이므로, 깊이 d까지 가는 데 드는 손가락 이동량(=수고)은
    //   ∫dx/(1−r(x)). 이 적분을 스폰 때 한 번 균등 샘플로 누적해 두고, 런타임에는 정규화한 수고로 역참조만 한다.
    //
    // ⚠ 실제 각도(seed)가 아니라 **봉투**로 저항을 재는 이유: seed는 스와이프마다 바뀌는데
    //   그것이 표에 들어가면 진행도→깊이 매핑이 스와이프마다 달라져 손을 대는 순간 카드가 순간이동한다.
    //   봉투는 깊이만의 함수라 표가 카드 한 장 동안 고정된다.
    void BakeResistance()
    {
        float t_step = this.m_cardHeight / (RESIST_SAMPLES - 1);
        float t_sum  = 0f;

        this.m_effort[0] = 0f;
        for (int t_i = 1; t_i < RESIST_SAMPLES; t_i++)
        {
            // 구간 중앙의 저항으로 그 구간의 수고를 잰다(끝점만 쓰면 첫 구간의 걸림이 통째로 빠진다).
            float t_mid = t_step * (t_i - 0.5f);
            float t_r   = this.maxTilt > 0f ? this.resistanceMax * (this.AllowedTilt(t_mid) / this.maxTilt) : 0f;

            t_sum += t_step / Mathf.Max(0.15f, 1f - t_r);   // 하한 — 저항이 1에 닿으면 영영 안 들어간다
            this.m_effort[t_i] = t_sum;
        }

        // 총 수고를 1로 정규화한다 — 진행도 1 = 카드 높이만큼 민 순간이라는 드래그 계약을 유지하려고.
        if (t_sum <= 0f) return;
        for (int t_i = 1; t_i < RESIST_SAMPLES; t_i++) this.m_effort[t_i] /= t_sum;
    }

    // 진행도(=정규화된 수고) → 실제 삽입 깊이. 표를 훑어 선형 보간한다.
    float DepthAt(float _p)
    {
        if (_p <= 0f) return 0f;
        if (_p >= 1f) return this.m_cardHeight;

        float t_step = this.m_cardHeight / (RESIST_SAMPLES - 1);

        for (int t_i = 1; t_i < RESIST_SAMPLES; t_i++)
        {
            if (this.m_effort[t_i] < _p) continue;

            float t_span = this.m_effort[t_i] - this.m_effort[t_i - 1];
            float t_frac = t_span > 0f ? (_p - this.m_effort[t_i - 1]) / t_span : 0f;
            return t_step * (t_i - 1 + t_frac);
        }

        return this.m_cardHeight;
    }

    // panelRect는 cardHolder의 홈 좌표계다 — 자기 자신으로 폴백하면
    // 변환 기준과 변환 대상이 같아져 좌표가 조용히 무의미해진다(카드가 엉뚱한 자리에 뜬다).
    RectTransform ResolveLayer()
    {
        if (this.panelRect != null) return this.panelRect;

        if (!m_layerWarned)
        {
            m_layerWarned = true;
            Debug.LogError("[AlbumSleeveView] panelRect 배선 누락 — 카드 위치를 계산할 수 없다(cardHolder의 부모를 꽂을 것).", this);
        }
        return null;
    }

    // 대상이 레이어 대비 몇 배로 그려지고 있는가. 0 나눗셈은 1배로 떨어뜨린다.
    static float ResolveScaleRatio(RectTransform _layer, RectTransform _target)
    {
        float t_layerScale = _layer.lossyScale.x;
        if (Mathf.Approximately(t_layerScale, 0f)) return 1f;

        return _target.lossyScale.x / t_layerScale;
    }

    // 중앙 앵커로 못 박고 크기·위치를 실측값으로 덮는다 — 프리팹 저작 앵커가 무엇이든 같은 결과가 나오게.
    static void Fit(RectTransform _rect, Vector2 _size, Vector2 _at)
    {
        if (_rect == null) return;

        _rect.anchorMin        = _rect.anchorMax = _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta        = _size;
        _rect.anchoredPosition = _at;
        _rect.localScale       = Vector3.one;
        _rect.localRotation    = Quaternion.identity;
    }
}
