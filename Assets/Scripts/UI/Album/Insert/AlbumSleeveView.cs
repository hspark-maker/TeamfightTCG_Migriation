using UnityEngine;

// 드래그 카드를 "지금 꽂을 슬롯" 안으로 옮겨 놓고, 진행도(0~1)를 카드 y로 환산한다.
//
// ⚠ 씰은 여기 없다 — **도감 칸 자체가 슬리브 두 겹**이고, 드래그 카드는 그 사이(`InsertDock`)로 들어간다.
//   번호를 덮으며 비닐(앞면) 뒤로 잠기는 것이 "밀어 넣는" 그림이라, 패널에 씰을 하나 더 띄울 필요가 없다.
//   (2026-08-10 이전에는 패널에 AlbumCardSlot 복제본 `Sleeve_Slot`을 띄워 진짜 칸 위를 덮었다.
//    화면에 씰이 두 벌 존재하는 값이라 폐기했다 — 되돌리지 말 것.)
//
// ⚠ 부모를 옮기는 컴포넌트다 — 스텝마다 대상 칸이 달라지므로 홈 부모(패널)를 Awake에 기억해 두고
//   세션이 끝날 때 `Release()`로 되돌린다. 안 되돌리면 다음 세션이 남의 칸 안에서 시작한다.
//
// ■ 기울기(끼워 넣는 체감)
//   카드는 스텝마다 다른 각도로 떠 있다가, 밀어 넣을수록 씰 입구에 모서리가 걸려 저절로 펴진다.
//   콜라이더를 쓰지 않는다 — UGUI 좌표계에서 "입구 폭 안에 들어가는 최대 각도"를 닫힌 식으로 풀 수 있고,
//   그 편이 프레임률과 무관하게 같은 그림을 준다(트윈으로 되감아도 각도가 어긋나지 않는다).
//   진행도가 곧 각도의 단일 진실원이므로, **y만 따로 트윈하면 각도가 얼어붙는다** —
//   안착·복귀도 반드시 SetProgress를 통해 움직일 것.
public class AlbumSleeveView : MonoBehaviour
{
    [SerializeField] RectTransform panelRect;   // 좌표 변환 기준 레이어(= 삽입 패널 루트)
    [SerializeField] RectTransform cardHolder;  // 드래그 카드 부모

    [Header("삽입 전 기울기")]
    [Tooltip("스폰 시 무작위 기울기 범위(도). 좌우 부호도 무작위다.")]
    [SerializeField] float minSpawnTilt = 5f;
    [SerializeField] float maxSpawnTilt = 15f;
    [Tooltip("기울어진 만큼 옆으로도 밀려 뜬다(카드 폭 대비 비율). 0이면 항상 칸 정중앙 위.")]
    [Range(0f, 0.3f)] [SerializeField] float spawnShiftRatio = 0.08f;

    [Header("씰 입구")]
    [Tooltip("입구가 카드보다 이만큼 넓다고 본다(카드 폭 대비). 클수록 헐겁게, 작을수록 빡빡하게 끼워진다.")]
    [Range(0.02f, 0.4f)] [SerializeField] float mouthClearance = 0.12f;
    [Tooltip("이 진행도부터 남은 기울기를 마저 편다 — 안착 순간 각도가 정확히 0이어야 바꿔치기가 안 보인다.")]
    [Range(0.4f, 0.95f)] [SerializeField] float uprightFrom = 0.7f;
    [Tooltip("최대로 기울었을 때 깎이는 진행 속도. 0.55면 그 구간에서 손가락 이동의 45%만 들어간다(모서리가 걸린 느낌).")]
    [Range(0f, 0.85f)] [SerializeField] float resistanceMax = 0.55f;

    const int RESIST_SAMPLES = 33;   // 저항 적분 해상도. 32구간이면 카드 높이의 3% 단위 — 눈으로는 연속이다

    readonly float[] m_effort = new float[RESIST_SAMPLES];   // 깊이별 누적 수고(정규화). BakeResistance가 굽는다

    float     m_cardHeight;   // = 정렬된 슬롯 높이. 진행도 1의 이동 거리
    float     m_cardWidth;    // = 정렬된 슬롯 폭. 입구 제약 계산의 기준
    float     m_homeX;
    float     m_homeY;        // 진행도 0의 카드 y(슬롯 바로 위에 통째로 떠 있는 자리)
    float     m_spawnTilt;    // 이번 카드의 기울기(도, 부호 포함)
    float     m_spawnShift;   // 이번 카드의 가로 어긋남(캔버스 단위)
    float     m_progress;
    bool      m_layerWarned;  // 배선 누락 경고는 카드마다 쏟지 않고 한 번만
    Transform m_dockHome;     // cardHolder의 원래 부모(= 패널). 옮기기 전에 한 번만 기억한다

    /// <summary>진행도 1이 이동하는 거리(캔버스 단위). 드래그 임계의 기준이 된다.</summary>
    public float CardHeight => this.m_cardHeight;

    /// <summary>마지막으로 반영된 진행도. 안착·복귀 트윈의 시작값이다.</summary>
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

    /// <summary>카드 홀더를 패널로 되돌린다(세션 종료·중단 공통). 멱등이다.</summary>
    public void Release()
    {
        if (this.cardHolder == null) return;

        // 기울어진 채로 홈에 돌아가면 다음 카드가 뜨기 전 한 프레임 동안 그 각도가 비친다.
        this.cardHolder.localRotation = Quaternion.identity;
        this.m_progress               = 0f;

        if (this.m_dockHome == null) return;
        if (this.cardHolder.parent == this.m_dockHome) return;

        this.cardHolder.SetParent(this.m_dockHome, false);
    }

    // (진행도 → y를 따로 묻는 창구는 두지 않는다. 저항이 끼어든 뒤로 진행도와 거리가 비례하지 않아
    //  "목표 y만 받아 트윈하는" 사용법이 곧 각도 정지 버그가 된다 — 움직이는 길은 SetProgress 하나뿐이다.)

    /// <summary>진행도 하나로 위치·기울기·좌우 어긋남을 전부 정한다.
    /// ⚠ 이것이 그림의 단일 진실원이다 — 바깥에서 y만 따로 트윈하면 각도가 그 자리에 얼어붙는다.</summary>
    public void SetProgress(float _p)
    {
        if (this.cardHolder == null) return;

        // 하한을 0이 아닌 음수로 둔다 — 복귀 트윈의 OutBack 오버슛(살짝 튕겨 나오는 맛)이 살아야 한다.
        float t_p     = Mathf.Clamp(_p, -0.5f, 1f);
        float t_depth = this.DepthAt(t_p);
        float t_angle = this.TiltAt(t_depth);

        // 기울기가 펴진 만큼 좌우 어긋남도 같이 회수된다 — 안착 시 정확히 칸 중앙이어야 바꿔치기가 안 보인다.
        float t_lean = Mathf.Approximately(this.m_spawnTilt, 0f) ? 0f : t_angle / this.m_spawnTilt;

        this.cardHolder.anchoredPosition = new Vector2(this.m_homeX + this.m_spawnShift * t_lean,
                                                       this.m_homeY - t_depth);
        this.cardHolder.localRotation    = Quaternion.Euler(0f, 0f, t_angle);

        this.m_progress = t_p;
    }

    // 카드는 칸과 같은 크기다 — 진짜 칸의 카드도 칸 전체를 쓰므로 안착 순간 바꿔치기해도 크기가 튀지 않는다.
    // (입구가 카드보다 넓다는 여유분은 크기가 아니라 mouthClearance라는 가정으로만 만든다)
    void Place(Vector2 _size, Vector2 _center)
    {
        this.m_cardHeight = Mathf.Max(1f, _size.y);
        this.m_cardWidth  = Mathf.Max(1f, _size.x);
        this.m_homeX      = _center.x;
        this.m_homeY      = _center.y + this.m_cardHeight;   // 진행도 0 = 칸 바로 위, 겹침 0

        // 기울기는 카드마다 새로 뽑는다 — 안 뽑으면 이번 세션의 모든 카드가 같은 각도로 떠서 규칙성이 읽힌다.
        this.m_spawnTilt  = Random.Range(this.minSpawnTilt, this.maxSpawnTilt) * (Random.value < 0.5f ? -1f : 1f);
        this.m_spawnShift = this.m_cardWidth * Random.Range(-this.spawnShiftRatio, this.spawnShiftRatio);
        this.m_progress   = 0f;

        this.BakeResistance();

        Fit(this.cardHolder, _size, new Vector2(this.m_homeX + this.m_spawnShift, this.m_homeY));
        this.cardHolder.localRotation = Quaternion.Euler(0f, 0f, this.m_spawnTilt);
    }

    // ■ 입구 제약 — 깊이 _depth까지 들어간 카드가 씰 입구 폭 안에 남을 수 있는 최대 기울기.
    //
    //   들어간 부분의 가로 반경 ≈ a·cosθ + d·sinθ = R·cos(θ−φ)   (a=카드 반폭, R=√(a²+d²), φ=atan2(d,a))
    //   이것이 입구 반폭 C 이하여야 하므로 θmax = φ − acos(C/R).
    //   d가 커질수록 단조 감소 → 밀어 넣을수록 카드가 저절로 펴진다. 이것이 "끼워진다"의 실체다.
    //
    // 콜라이더를 쓰지 않는 이유: 이 식은 순수 함수라 진행도를 되감아도(복귀 트윈) 각도가 정확히 되돌아온다.
    float TiltAt(float _depth)
    {
        float t_abs = Mathf.Abs(this.m_spawnTilt);
        if (t_abs <= 0f) return 0f;

        if (_depth > 0f)
        {
            float t_a = this.m_cardWidth * 0.5f;
            float t_c = t_a * (1f + this.mouthClearance);
            float t_r = Mathf.Sqrt(t_a * t_a + _depth * _depth);

            // R ≤ C면 아직 입구가 카드를 붙잡지 못한다 — 스폰 각도 그대로 떠 있다.
            if (t_r > t_c)
            {
                float t_limit = (Mathf.Atan2(_depth, t_a) - Mathf.Acos(Mathf.Clamp01(t_c / t_r))) * Mathf.Rad2Deg;
                t_abs = Mathf.Min(t_abs, Mathf.Max(0f, t_limit));
            }
        }

        // 기하 제약만으로는 끝에서 몇 도가 남는다 — 안착 순간의 각도 0은 바꿔치기 계약이라 여기서 마저 편다.
        float t_p = this.m_cardHeight > 0f ? _depth / this.m_cardHeight : 0f;
        t_abs *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(this.uprightFrom, 1f, t_p));

        return t_abs * Mathf.Sign(this.m_spawnTilt);
    }

    // ■ 저항 — "기울어져 있을수록 잘 안 들어간다".
    //
    //   실효 속도 = 1 − resistanceMax·(현재 기울기/스폰 기울기)이므로, 깊이 d에 도달하는 데 드는
    //   손가락 이동량(=수고)은 ∫dx/(1−r(x)). 이 적분을 스폰 때 한 번 균등 샘플로 누적해 두고,
    //   런타임에는 정규화한 수고(=진행도)로 역참조만 한다.
    //
    // ⚠ 이렇게 굽는 이유: 저항을 프레임마다 적분하면 SetProgress가 이력에 의존해
    //   안착·복귀 트윈이 같은 진행도에 다른 위치를 주게 된다. 표로 만들면 진행도의 순수 함수로 남는다.
    void BakeResistance()
    {
        float t_step = this.m_cardHeight / (RESIST_SAMPLES - 1);
        float t_sum  = 0f;

        this.m_effort[0] = 0f;
        for (int t_i = 1; t_i < RESIST_SAMPLES; t_i++)
        {
            // 구간 중앙의 저항으로 그 구간의 수고를 잰다(끝점만 쓰면 첫 구간의 걸림이 통째로 빠진다).
            float t_mid = t_step * (t_i - 0.5f);
            float t_r   = Mathf.Abs(this.m_spawnTilt) > 0f
                        ? this.resistanceMax * Mathf.Abs(this.TiltAt(t_mid) / this.m_spawnTilt)
                        : 0f;

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
        if (_p <= 0f) return this.m_cardHeight * _p;   // 음수 구간(복귀 오버슛)은 저항이 없다 — 그냥 나온다
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
