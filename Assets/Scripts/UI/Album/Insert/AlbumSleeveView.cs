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
//   ④ 카드는 연속이 아니라 slipStep 단위로 **툭툭** 미끄러져 들어간다.
//
// ⚠ ④의 계단은 **불연속이면 안 된다**(2026-08-10 개편). Floor로 잡으면 눈금 경계에서
//   터치 입력의 미세 떨림이 index를 n↔n-1로 왕복시키고, 무상관 해시가 걸린 각도가 매 프레임 최대 진폭으로
//   진동한다("덜덜"이 아니라 "지직"으로 읽히던 원인). 계단·각도킥·가로 어긋남 셋 다 진행도의 **연속 단조 함수**로 두고,
//   툭툭거림은 "대부분 평평하다가 구간 뒤쪽에서만 급히 미끄러지는" 모양으로 낸다.
//
// ■ 목표와 그림은 층이 다르다 — SetProgress는 눈금 **목표**만 적는 순수층(되감기 정확성 담보)이고,
//   그림은 LateUpdate가 slipGlide 동안 SmoothDamp로 미끄러지며 따라간다. 계단이 몸에 닿기 전에 시간으로 뭉개진다.
//   ⚠ 층을 섞지 말 것: 목표 쪽을 늦추면 드래그 임계·트윈 시작값까지 그림 지연에 오염된다.
//   바꿔치기(Seat)는 그림이 목표에 다 따라붙은 뒤에만 유효하다 — 세션이 Settled를 기다린다.
//
//   ③이 공짜로 나오는 이유가 이 구조의 전부다 — 각도를 직접 줄이는 코드는 없고,
//   "깊이 d까지 들어간 카드가 씰 입구 폭 안에 남을 수 있는 최대 각도"(`AllowedTilt`)라는 **봉투**가 있을 뿐이다.
//   실제 각도 = 봉투 × seed(-1~1). 봉투가 d에 대해 단조 감소하므로 seed를 아무리 흔들어도 수렴한다.
//
// ■ 카드가 씰 밖으로 안 나가는 근거는 두 가지뿐이다(둘 중 하나만 빠져도 새어 나간다):
//   ⓐ 회전축이 카드 중앙이 아니라 **입구선**이다(`ApplyPose`). 중앙을 축으로 돌리면 이미 꽂힌 아랫부분이
//      카드 높이 절반 × sinθ 만큼 쓸려 나가 봉투가 아무리 정확해도 소용이 없다.
//   ⓑ 가로 이동(shift·잔떨림)은 **각도가 쓰고 남은 여유**(`LateralSlack`) 안에서만 논다.
//      봉투는 각도만 막을 뿐이라, 그 위에 덧대는 이동은 예산을 따로 받아야 한다.
//
//   콜라이더를 쓰지 않는다 — 이 식은 진행도의 순수 함수라 트윈으로 되감아도 각도가 정확히 되돌아온다.
//   진행도가 그림의 단일 진실원이므로, **y만 따로 트윈하면 각도가 그 자리에 얼어붙는다.**
public class AlbumSleeveView : MonoBehaviour
{
    [SerializeField] RectTransform panelRect;   // 좌표 변환 기준 레이어(= 삽입 패널 루트)
    [SerializeField] RectTransform cardHolder;  // 드래그 카드 부모

    [Header("기울기")]
    [Tooltip("입구 밖(진행도 0 근처)에서 허용하는 최대 기울기(도). 봉투의 천장이다.\n" +
             "회전축이 입구선이라 같은 각도라도 카드 윗부분이 크게 돈다 — 눈에 보이는 흔들림은 이 값보다 커 보인다.")]
    [SerializeField] float maxTilt = 11f;
    [Tooltip("스와이프마다 뽑는 기울기 진폭의 하한(봉투 대비). 0이면 가끔 똑바로 들어가 까딱거림이 끊긴다.")]
    [Range(0f, 1f)] [SerializeField] float minTiltAmount = 0.3f;
    [Tooltip("직전과 반대쪽으로 기울 확률. 1이면 좌우가 규칙적으로 번갈아 보인다.\n" +
             "얕은 구간에서는 아래 flipFromDepth가 이 확률을 0으로 눌러 방향이 유지된다.")]
    [Range(0f, 1f)] [SerializeField] float flipChance = 0.7f;
    [Tooltip("이 깊이(미는 거리 대비)를 지나야 스와이프가 기울기 **방향**을 뒤집는다. 그 전에는 같은 쪽으로 진폭만 새로 뽑는다.\n" +
             "⚠ 0으로 내리지 말 것 — 방향을 뒤집으려면 각도가 반드시 0을 지난다(SeedAt이 이전 값에서 새 값으로 잇는다).\n" +
             "  그 순간 카드가 정확히 수직으로 서는데, 격자의 세로 피치가 카드 높이와 거의 같아 바로 위 칸에 포개지므로\n" +
             "  그 칸에 꽂힌 카드로 읽힌다. 얕을수록 카드가 위 칸을 넓게 덮어 더 심하다.\n" +
             "깊이 들어갈수록 봉투(AllowedTilt)가 각도를 조여 어차피 수직에 가까워지므로, 그 뒤로는 뒤집어도 티가 나지 않는다.\n" +
             "올릴수록 좌우가 번갈아 걸리는 손맛이 뒤로 밀린다 — 진폭은 계속 새로 뽑히므로 까딱거림 자체는 남는다.")]
    [Range(0f, 1f)] [SerializeField] float flipFromDepth = 0.5f;
    [Tooltip("새 각도로 갈아타는 데 걸리는 삽입 깊이(미는 거리 대비). 0이면 손을 대는 순간 각도가 튄다.")]
    [Range(0.01f, 0.4f)] [SerializeField] float tiltBlendDepth = 0.12f;
    [Tooltip("기울어진 만큼 옆으로도 밀린다 — 단위는 카드 폭이 아니라 **남은 입구 여유(slack)의 비율**이다.\n" +
             "1이어도 씰을 넘지 않는다(각도가 이미 쓴 폭을 뺀 나머지만 쓰므로). 0이면 입구가 항상 칸 정중앙.")]
    [Range(0f, 1f)] [SerializeField] float shiftRatio = 0.5f;

    [Header("덜덜거림 (stick-slip)")]
    [Tooltip("한 번에 미끄러져 들어가는 단위(미는 거리 대비). 손가락은 연속으로 움직여도 카드는 이 단위로 툭툭 들어간다.\n" +
             "작을수록 눈금이 잦아 잔진동처럼 읽히고, 클수록 한 번에 크게 미끄러진다.")]
    [Range(0.02f, 0.2f)] [SerializeField] float slipStep = 0.12f;
    [Tooltip("한 눈금 중 실제로 미끄러지는 구간의 비율. 나머지는 버티는(평평한) 구간이다.\n" +
             "0.05면 거의 계단(급격), 1이면 계단이 없어져 그냥 매끄럽게 들어간다.\n" +
             "⚠ 0으로는 못 내린다 — 계단이 불연속이 되는 순간 눈금 경계에서 각도가 지직거린다.\n" +
             "거칠다고 느껴지면 여기부터 올린다(진폭보다 이쪽이 체감을 크게 바꾼다).")]
    [Range(0.05f, 1f)] [SerializeField] float slipSharpness = 0.6f;
    [Tooltip("미끄러짐이 앞으로 쏠리는 정도 — 정지마찰을 이기는 순간 확 나갔다가 감속하며 멈추는 몫.\n" +
             "0이면 대칭(부드럽게 시작해 부드럽게 정지), 1이면 거의 즉발 후 긴 감속.\n" +
             "slip감이 부족할 때 진폭·빈도를 올리기 전에 먼저 만질 값이다 — 거칠어지지 않고 대비만 커진다.")]
    [Range(0f, 1f)] [SerializeField] float slipRelease = 0.65f;
    [Tooltip("그림이 눈금 목표를 따라붙는 시간(초). 목표가 툭 움직여도 카드는 이 시간 동안 미끄러지며 들어간다.\n" +
             "0이면 즉시 반응(계단이 그대로 몸에 닿는다). 너무 크면 손보다 카드가 늦어 헐렁하게 느껴진다.")]
    [Range(0f, 0.3f)] [SerializeField] float slipGlide = 0.1f;
    [Tooltip("미끄러질 때마다 각도가 튀는 몫(0~1). 0이면 계단만 지고 각도는 얌전하다.")]
    [Range(0f, 1f)] [SerializeField] float slipTiltKick = 0.2f;
    [Tooltip("손가락이 닿아 있는 동안의 잔떨림 각도(도). 밀리지 않고 버티는 순간에도 카드가 떤다.")]
    [SerializeField] float shakeAngle = 0.2f;
    [Tooltip("잔떨림 가로 진폭 — shiftRatio와 같이 **남은 입구 여유(slack) 비율**이다. 둘을 합쳐도 씰을 넘지 않는다.")]
    [Range(0f, 1f)] [SerializeField] float shakeShift = 0.12f;
    [Tooltip("잔떨림 속도. 높을수록 거칠다.\n" +
             "⚠ 15를 넘기면 60fps에서 노이즈 격자보다 프레임 간격이 성겨져(에일리어싱) 떨림이 아니라 튐으로 보인다.")]
    [SerializeField] float shakeSpeed = 5f;

    [Header("씰 입구")]
    [Tooltip("진행도 0에서 카드 하단이 이미 씰에 잠겨 있는 깊이(카드 높이 대비). 0이면 칸 위에 통째로 떠 있는 자리에서 출발한다.\n" +
             "0.1이면 하단 10%가 물린 채 시작해 '입구에 걸쳐 놓고 미는' 그림이 된다.\n" +
             "올릴수록 밀어 넣을 거리(PushDistance)가 짧아진다 — 손가락 이동량도 함께 줄어 카드가 손을 계속 따라온다.")]
    [Range(0f, 0.5f)] [SerializeField] float startSunk = 0.1f;

    [Tooltip("입구가 카드보다 이만큼 넓다고 본다(카드 폭 대비). 클수록 헐겁게, 작을수록 빡빡하게 끼워진다.")]
    [Range(0.02f, 0.4f)] [SerializeField] float mouthClearance = 0.12f;
    [Tooltip("이 진행도부터 남은 기울기를 마저 편다 — 안착 순간 각도가 정확히 0이어야 바꿔치기가 안 보인다.")]
    [Range(0.4f, 0.95f)] [SerializeField] float uprightFrom = 0.75f;
    [Tooltip("최대로 기울 수 있는 구간에서 깎이는 진행 속도. 0.4면 손가락 이동의 60%만 들어간다(모서리가 걸린 느낌).\n" +
             "⚠ stick-slip과 이중으로 걸리는 값이다 — 여기를 올리면 계단이 그대로여도 더 거칠게 느껴진다.")]
    [Range(0f, 0.85f)] [SerializeField] float resistanceMax = 0.4f;

    const int RESIST_SAMPLES = 33;   // 저항 적분 해상도. 32구간이면 카드 높이의 3% 단위 — 눈으로는 연속이다

    readonly float[] m_effort = new float[RESIST_SAMPLES];   // 깊이별 누적 수고(정규화). BakeResistance가 굽는다

    float     m_cardHeight;   // = 정렬된 슬롯 높이. 진행도 1의 이동 거리
    float     m_cardWidth;    // = 정렬된 슬롯 폭. 입구 제약 계산의 기준
    float     m_homeX;
    float     m_homeY;        // 깊이 0의 카드 y(칸 위에 통째로 떠 있는 자리). 출발은 여기서 m_startDepth만큼 내려온 자리다
    float     m_startDepth;   // 진행도 0의 삽입 깊이 — 여기부터 m_cardHeight까지가 실제로 밀어 넣는 구간이다
    float     m_seedFrom;     // 직전 스와이프의 기울기 계수(-1~1)
    float     m_seedTo;       // 이번 스와이프의 기울기 계수
    float     m_seedDepth;    // 갈아타기가 시작된 깊이 — 여기부터 tiltBlendDepth만큼 밀면 새 각도가 된다
    float     m_progress;
    Vector2   m_basePos;      // 각도 0·가로이동 0일 때의 자리. 회전 보정과 떨림은 이 위에 얹었다 걷는다
    float     m_baseAngle;
    float     m_depth;        // 그림의 삽입 깊이(시간 보간 후). 봉투·여유를 다시 묻는 창구다
    float     m_targetDepth;  // 눈금 목표 깊이 — SetProgress(순수층)가 적고 LateUpdate가 따라간다
    float     m_targetSeed;   // 목표 기울기 계수(킥 포함, -1~1)
    float     m_visSeed;      // 그림의 기울기 계수
    float     m_depthVel;     // SmoothDamp 속도 버퍼
    float     m_seedVel;
    float     m_arm;          // 카드 중심 → 입구선 거리. 회전축을 입구로 옮기는 팔 길이(ApplyPose)
    float     m_shift;        // 입구선의 가로 이동. m_slack 안으로 클램프된다
    float     m_slack;        // 각도가 쓰고 남은 가로 여유. 이 밖으로는 어떤 성분도 못 나간다
    bool      m_pushing;      // 손가락이 닿아 있는가(잔떨림 스위치)
    bool      m_layerWarned;  // 배선 누락 경고는 카드마다 쏟지 않고 한 번만
    Transform m_dockHome;     // cardHolder의 원래 부모(= 패널). 옮기기 전에 한 번만 기억한다

    /// <summary>진행도 1이 이동하는 거리(캔버스 단위). 드래그 임계의 기준이 된다.
    /// 카드 높이가 아니라 **출발 자리에서 안착까지의 거리**다 — startSunk만큼은 이미 잠겨 있어 밀 필요가 없다.</summary>
    public float PushDistance => this.m_cardHeight - this.m_startDepth;

    /// <summary>마지막으로 반영된 진행도. 안착·되밀림 트윈의 시작값이다.</summary>
    public float Progress => this.m_progress;

    /// <summary>그림이 목표 깊이에 다 따라붙었는가. 트윈은 목표만 끝까지 밀 뿐 그림은 slipGlide만큼 늦다 —
    /// Seat는 이것이 참이 된 뒤에 바꿔치기해야 덜 들어간 카드가 꽂힌 카드로 둔갑하지 않는다.</summary>
    public bool Settled => this.slipGlide <= 0f || Mathf.Abs(this.m_targetDepth - this.m_depth) < 0.25f;

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
        this.m_seedTo    = RollSeed(this.m_seedFrom, this.minTiltAmount, this.FlipChanceAt(t_depth));
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

        // 보간 상태도 지운다 — 남으면 홈으로 돌아간 홀더를 LateUpdate가 옛 좌표계 자세로 끌고 간다.
        this.m_targetDepth = this.m_depth   = 0f;
        this.m_targetSeed  = this.m_visSeed = 0f;
        this.m_depthVel    = this.m_seedVel = 0f;

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

        float t_p   = Mathf.Clamp01(_p);
        float t_raw = this.DepthAt(t_p);

        // ■ stick-slip — 손가락은 연속으로 움직이지만 카드는 slipStep 단위로 **툭툭** 들어간다.
        //   한 눈금 안에서 앞쪽은 버티고(평평) 뒤쪽 slipSharpness 구간에서만 미끄러진다 — 계단이되 연속이다.
        //   ⚠ Floor로 끊으면 눈금 경계에서 손가락 미세 떨림이 그대로 각도 진동이 된다(개편 전 지직거림).
        //   ⚠ 눈금의 원점은 0이 아니라 **출발 깊이**다. 원점이 어긋나면 진행도 0에서 그림이 한 눈금 뒤로 물러나
        //     카드가 잠긴 채로 출발하지 못하고 칸 위로 도로 떠오른다.
        float t_unit = Mathf.Max(0.001f, this.PushDistance * this.slipStep);
        float t_push = t_raw - this.m_startDepth;
        float t_u    = t_push / t_unit;
        int   t_i    = Mathf.FloorToInt(t_u);
        float t_slip = this.SlipCurve(Mathf.InverseLerp(1f - this.slipSharpness, 1f, t_u - t_i));

        // 마지막 구간에선 계단을 편다 — 각도를 마저 펴는 그 구간이다. 계단이 남으면 진행도 1에서
        // 깊이가 한 눈금 모자라 "완전 삽입"이 안 되고, 바꿔치기가 그만큼 어긋나 보인다.
        float t_flat  = SmootherStep(Mathf.InverseLerp(this.uprightFrom, 1f, this.DepthRatio(t_raw)));
        float t_depth = this.m_startDepth + Mathf.Lerp((t_i + t_slip) * t_unit, t_push, t_flat);

        // 각도 킥을 같은 미끄러짐에 실어 보낸다 — 이웃 눈금끼리 보간하므로 카드가 미끄러지는 그 순간에만 튄다.
        // (눈금 번호로만 뽑으면 인접값이 무상관이라 경계에서 최대 진폭으로 튄다.)
        float t_smooth = this.SeedAt(t_depth);
        float t_kick   = Mathf.Lerp(Hash11(t_i), Hash11(t_i + 1), t_slip);
        float t_seed   = Mathf.Clamp(Mathf.Lerp(t_smooth, t_kick, this.slipTiltKick), -1f, 1f);

        this.m_targetDepth = t_depth;
        this.m_targetSeed  = t_seed;
        this.m_progress    = t_p;

        // 여기서는 목표만 적는다 — 그림은 LateUpdate가 slipGlide 동안 미끄러지며 따라간다.
        if (this.slipGlide <= 0f) this.Snap();
    }

    // 깊이·계수 한 쌍을 실제 자세로 편다 — 그림이 되는 길은 이 하나뿐이다.
    // 클램프는 SmoothDamp 오버슈트 방어: 봉투·여유 계산이 정의역을 벗어나면 격리 증명이 깨진다.
    void PoseFromDepth(float _depth, float _seed)
    {
        float t_depth = Mathf.Clamp(_depth, 0f, this.m_cardHeight);
        float t_env   = this.AllowedTilt(t_depth);

        this.m_depth     = t_depth;
        this.m_visSeed   = Mathf.Clamp(_seed, -1f, 1f);
        this.m_baseAngle = t_env * this.m_visSeed;

        // 각도가 쓰고 남은 여유(m_slack) 안에서만 옆으로 민다 — 저주파 seed만 태워 각도 잡음이 가로로 증폭되지 않게.
        // ⚠ 봉투 페이드를 한 번 더 곱하는 이유: 여유는 각도가 0이 되면 오히려 **넓어지므로**,
        //   그것만 보고 밀면 다 꽂힌 카드가 중앙에서 비껴 선 채 끝나 바꿔치기가 어긋나 보인다.
        this.m_arm     = this.m_cardHeight * 0.5f - t_depth;
        this.m_slack   = this.LateralSlack(t_depth, this.m_baseAngle);
        this.m_shift   = this.m_slack * this.shiftRatio * this.SeedAt(t_depth) * this.TiltFade(t_env);
        this.m_basePos = new Vector2(this.m_homeX, this.m_homeY - t_depth);

        this.ApplyPose(0f, 0f);
    }

    // 그림을 목표에 즉시 붙인다 — 새 카드 스폰과 slipGlide 0(보간 끔) 전용.
    void Snap()
    {
        this.m_depthVel = 0f;
        this.m_seedVel  = 0f;
        this.PoseFromDepth(this.m_targetDepth, this.m_targetSeed);
    }

    // 그림층 — 목표를 미끄러지며 따라가는 시간 보간과, 그 위에 얹었다 걷는 잔떨림.
    // 둘 다 SetProgress(목표층)의 순수성을 건드리지 않는다.
    void LateUpdate()
    {
        if (this.cardHolder == null) return;

        // 목표는 눈금대로 즉발로 움직여도 그림은 여기서 슬슬 따라간다 — 계단이 시간으로 뭉개진다.
        if (this.slipGlide > 0f && (this.m_depth != this.m_targetDepth || this.m_visSeed != this.m_targetSeed))
        {
            float t_dt = Time.unscaledDeltaTime;
            this.PoseFromDepth(
                Mathf.SmoothDamp(this.m_depth,   this.m_targetDepth, ref this.m_depthVel, this.slipGlide, Mathf.Infinity, t_dt),
                Mathf.SmoothDamp(this.m_visSeed, this.m_targetSeed,  ref this.m_seedVel,  this.slipGlide, Mathf.Infinity, t_dt));
        }

        if (!this.m_pushing) return;

        // 봉투가 좁아진 만큼 떨림도 잦아든다 — 다 들어간 카드가 부르르 떨면 안착이 안 끝난 것처럼 보인다.
        float t_env = this.TiltFade(this.AllowedTilt(this.m_depth));
        if (t_env <= 0f) return;

        float t_t = Time.unscaledTime * this.shakeSpeed;

        // ⚠ 두 축을 (t, c)와 (c, t)로 뽑으면 안 된다 — Perlin은 대각 대칭이라 각도와 가로가 같이 움직여
        //   "떨림"이 아니라 한 방향으로 쓸리는 흔들림이 된다. 같은 축에서 좌표만 멀리 벌려 뽑는다.
        this.ApplyPose((Mathf.PerlinNoise(t_t, 3.7f) * 2f - 1f) * this.shakeAngle * t_env,
                       (Mathf.PerlinNoise(t_t + 41.3f, 17.9f) * 2f - 1f) * this.m_slack * this.shakeShift * t_env);
    }

    // ■ 회전축을 카드 중앙이 아니라 **입구선**(지금 씰에 걸린 지점)으로 옮긴다.
    //
    //   RectTransform은 pivot(0.5,0.5) 기준으로 도는데, 그러면 기울일 때마다 이미 꽂힌 아랫부분이
    //   팔 길이(카드 높이의 절반)×sinθ 만큼 옆으로 쓸려 씰 밖으로 나간다 — 카드가 세로로 길수록 심하다.
    //   입구를 축으로 돌면 흔들리는 건 씰 밖에 남은 윗부분뿐이고, AllowedTilt의 유도(회전 중심 = 입구)와도
    //   비로소 일치한다. pivot을 런타임에 옮기는 대신 회전이 밀어낸 만큼을 되빼는 쪽이 싸고 되돌리기 쉽다.
    void ApplyPose(float _angleAdd, float _shiftAdd)
    {
        if (this.cardHolder == null) return;

        float t_angle = this.m_baseAngle + _angleAdd;
        float t_rad   = t_angle * Mathf.Deg2Rad;

        // 입구선이 제자리에 남도록 되빼는 양(= 회전이 그 점을 밀어낸 만큼).
        var t_fix = new Vector2(-this.m_arm * Mathf.Sin(t_rad),
                                 this.m_arm * (Mathf.Cos(t_rad) - 1f));

        // 보정 뒤의 가로 성분이 곧 입구선의 위치다 — 여유 밖으로는 어떤 성분도 못 나간다.
        float t_x = Mathf.Clamp(this.m_shift + _shiftAdd, -this.m_slack, this.m_slack);

        this.cardHolder.anchoredPosition = this.m_basePos + t_fix + new Vector2(t_x, 0f);
        this.cardHolder.localRotation    = Quaternion.Euler(0f, 0f, t_angle);
    }

    // 각도가 이미 쓴 폭을 뺀 남은 가로 여유. 봉투(AllowedTilt)는 각도만 막을 뿐이라,
    // 그 위에 얹는 가로 이동은 따로 예산을 받아야 씰을 넘지 않는다(개편 전엔 shift·잔떨림이 예산 밖이었다).
    float LateralSlack(float _depth, float _angle)
    {
        float t_a   = this.m_cardWidth * 0.5f;
        float t_rad = Mathf.Abs(_angle) * Mathf.Deg2Rad;

        return Mathf.Max(0f, t_a * (1f + this.mouthClearance)
                           - (t_a * Mathf.Cos(t_rad) + _depth * Mathf.Sin(t_rad)));
    }

    // ■ 한 눈금을 미끄러지는 모양. 대칭 S커브를 앞으로 쏠리게 비틀어 stick-slip의 물리를 흉내낸다 —
    //   정지마찰을 이기는 순간 확 나갔다가(앞) 운동마찰에 감속하며 멈춘다(뒤).
    //
    //   ⚠ 비틀기는 S커브 **뒤에** 건다. 먼저 걸면 양끝 미분이 0이 아니게 돼 미끄러짐이 시작·끝에서 툭 끊긴다.
    //   (1−(1−s)^n의 미분은 s'에 비례하므로, s'가 양끝에서 0이면 비튼 뒤에도 0으로 남는다.)
    float SlipCurve(float _t)
    {
        float t_s = SmootherStep(_t);
        if (this.slipRelease <= 0f) return t_s;

        return 1f - Mathf.Pow(1f - t_s, 1f + this.slipRelease * 2f);
    }

    // SmoothStep(3t²−2t³)은 양끝에서 가속도가 불연속이라 미끄러짐이 시작·끝에서 "탁" 하고 걸린다.
    static float SmootherStep(float _t)
    {
        float t_t = Mathf.Clamp01(_t);
        return t_t * t_t * t_t * (t_t * (t_t * 6f - 15f) + 10f);
    }

    // 눈금 번호 → -1~1. 난수를 쓰면 같은 눈금을 되감을 때 값이 달라져 카드가 지직거린다.
    // 이웃 눈금끼리는 무상관이라 **반드시 보간해서** 쓴다(SetProgress) — 날것으로 쓰면 그 무상관이 곧 진동이다.
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
        this.m_homeY      = _center.y + this.m_cardHeight;   // 깊이 0의 자리. 실제 출발은 startSunk가 정한다
        this.m_startDepth = this.m_cardHeight * Mathf.Clamp01(this.startSunk);
        this.m_progress   = 0f;

        // 첫 각도도 스와이프와 같은 방식으로 뽑는다 — 카드마다 다른 각도로 떠 있어야 규칙성이 안 읽힌다.
        this.m_seedTo    = RollSeed(0f, this.minTiltAmount, this.flipChance);
        this.m_seedFrom  = this.m_seedTo;
        this.m_seedDepth = 0f;

        this.BakeResistance();

        Fit(this.cardHolder, _size, new Vector2(this.m_homeX, this.m_homeY - this.m_startDepth));
        this.SetProgress(0f);
        this.Snap();   // 새 카드가 이전 카드의 깊이에서 미끄러져 오면 안 된다 — 시작 자리에 즉시 선다
    }

    // ■ 봉투 — 깊이 _depth까지 들어간 카드가 씰 입구 폭 안에 남을 수 있는 최대 기울기.
    //
    //   들어간 부분의 가로 반경 = a·cosθ + d·sinθ = R·cos(θ−φ)   (a=카드 반폭, R=√(a²+d²), φ=atan2(d,a))
    //   ⚠ 이 유도는 **회전축이 입구선일 때만** 성립한다 — ApplyPose의 피벗 보정이 그 전제를 지킨다.
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
        return t_abs * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(this.uprightFrom, 1f, this.DepthRatio(_depth))));
    }

    // 진행도와 같은 축의 0~1이다 — 출발 깊이가 0, 안착이 1. uprightFrom·봉투 페이드·flipFromDepth가 모두
    // 이걸 기준으로 하므로, 출발을 잠근 채 시작해도 각도가 펴지고 방향이 풀리는 지점이 진행도상 같은 자리에 남는다.
    float DepthRatio(float _depth)
    {
        float t_push = this.PushDistance;
        return t_push > 0f ? Mathf.Clamp01((_depth - this.m_startDepth) / t_push) : 0f;
    }

    // 봉투를 천장 대비 0~1로. 깊을수록 0에 수렴하므로 "각도를 따라 사라져야 하는 것"의 공통 스위치다.
    float TiltFade(float _env) => this.maxTilt > 0f ? Mathf.Clamp01(_env / this.maxTilt) : 0f;

    // 이번 스와이프의 기울기 계수(-1~1). 깊이로 갈아타므로 **되감아도 같은 값이 나온다**(시간을 쓰지 않는다).
    float SeedAt(float _depth)
    {
        if (this.tiltBlendDepth <= 0f || this.PushDistance <= 0f) return this.m_seedTo;

        float t_span = this.PushDistance * this.tiltBlendDepth;
        float t_t    = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_depth - this.m_seedDepth) / t_span));

        return Mathf.Lerp(this.m_seedFrom, this.m_seedTo, t_t);
    }

    // 이 깊이에서 방향을 뒤집어도 되는가. 뒤집기는 각도 0을 지나는 일이라 얕은 구간에서는 금지한다
    // — 수직이 되는 순간 카드가 바로 위 칸에 포개진다(flipFromDepth 참고).
    float FlipChanceAt(float _depth)
        => this.DepthRatio(_depth) < this.flipFromDepth ? 0f : this.flipChance;

    // 직전과 반대쪽으로, 눈에 보일 만큼의 진폭으로. 봉투가 곱해지므로 깊을수록 실제 각도는 작아진다.
    // _flip이 0이면 방향은 직전 그대로 남고 진폭만 새로 뽑힌다.
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
        float t_step = this.PushDistance / (RESIST_SAMPLES - 1);
        float t_sum  = 0f;

        this.m_effort[0] = 0f;
        for (int t_i = 1; t_i < RESIST_SAMPLES; t_i++)
        {
            // 구간 중앙의 저항으로 그 구간의 수고를 잰다(끝점만 쓰면 첫 구간의 걸림이 통째로 빠진다).
            float t_mid = this.m_startDepth + t_step * (t_i - 0.5f);
            float t_r   = this.maxTilt > 0f ? this.resistanceMax * (this.AllowedTilt(t_mid) / this.maxTilt) : 0f;

            t_sum += t_step / Mathf.Max(0.15f, 1f - t_r);   // 하한 — 저항이 1에 닿으면 영영 안 들어간다
            this.m_effort[t_i] = t_sum;
        }

        // 총 수고를 1로 정규화한다 — 진행도 1 = PushDistance만큼 민 순간이라는 드래그 계약을 유지하려고.
        if (t_sum <= 0f) return;
        for (int t_i = 1; t_i < RESIST_SAMPLES; t_i++) this.m_effort[t_i] /= t_sum;
    }

    // 진행도(=정규화된 수고) → 실제 삽입 깊이. 표를 훑어 선형 보간한다.
    float DepthAt(float _p)
    {
        if (_p <= 0f) return this.m_startDepth;
        if (_p >= 1f) return this.m_cardHeight;

        float t_step = this.PushDistance / (RESIST_SAMPLES - 1);

        for (int t_i = 1; t_i < RESIST_SAMPLES; t_i++)
        {
            if (this.m_effort[t_i] < _p) continue;

            float t_span = this.m_effort[t_i] - this.m_effort[t_i - 1];
            float t_frac = t_span > 0f ? (_p - this.m_effort[t_i - 1]) / t_span : 0f;
            return this.m_startDepth + t_step * (t_i - 1 + t_frac);
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
