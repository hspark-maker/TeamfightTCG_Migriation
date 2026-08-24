using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 씬 전환을 덮는 커튼. 위/아래 두 판이 대각으로 맞물려 화면을 덮고, 그 밑에서 씬을 갈아치운 뒤 다시 열린다.
// 복귀(배틀 → 로비)는 LoadingCoverView가 맡는다 — 가는 길은 빠르고 단호하게, 오는 길은 여유 있게.
//
// 이 컴포넌트는 "덮고 → 갈아치우고 → 연다"만 안다. 무엇이 갈리는지는 주입된 ICurtainSwap이 안다.
// 지금 구현은 씬 로드(SceneLoadSwap) 하나뿐이지만, 커튼이 씬을 직접 알던 시절엔 씬 로드가 이 코루틴에
// 섞여 있어 폴백이 두 곳으로 갈리고 씬 없는 전환을 표현할 방법이 없었다.
//
// 아트의 진실원은 동기 UI 카탈로그의 SceneCurtain 프리팹이다. 색·기울기·이음매 위치·두께는 전부 거기서 읽고,
// 코드는 화면 크기에 맞춰 판을 키우고 움직이기만 한다. 판의 색·대각은 매치 덱 확인 화면에서 가져왔다 —
// 덱을 확인하던 화면이 그대로 접히는 것처럼 보이도록.
//
// ⚠ 프리팹 루트는 독립 캔버스여야 한다. 중첩 캔버스는 sortingOrder를 올려도 부모 루트가 그려지는 자리 안에서만 정렬된다.
// ⚠ DontDestroyOnLoad로 씬을 넘어간다 — 씬이 갈린 뒤에 열려야 하므로. 그 대가로 어떻게 빠져나가든 반드시 스스로를
//   파괴해야 한다. 남기면 커튼의 sortingOrder와 입력 차단판이 이후 모든 씬을 영구 입력 불가로 잠근다.
public class CurtainView : MonoBehaviour
{
    // 커튼은 씬 전환 직전에 동기 생성되어야 하므로 Boot가 직렬화한 카탈로그에서 직접 얻는다.
    [Tooltip("위 판(상대색). 아랫변이 이음매다.\n"
           + "저작해서 쓰는 값: 색·스프라이트 / 기울기(회전 Z) / 이음매 세로 위치(앵커 Y).\n"
           + "⚠ 크기와 위치는 런타임이 화면에 맞춰 다시 잡는다 — 프리팹의 Width·Height·Pos는 편집 중 미리보기일 뿐 무시된다.\n"
           + "⚠ pivot은 반드시 (0.5, 0) — 회전이 pivot을 중심으로 돌기 때문에, 이음매가 될 변 위에 pivot이 있어야 "
           + "해상도와 무관하게 아래 판의 변과 정확히 겹친다. 이 규약이 깨지면 대각선에 틈이 생긴다.")]
    [SerializeField] RectTransform top;

    [Tooltip("아래 판(내색). 윗변이 이음매다. 크기·위치는 위 판과 같이 런타임이 잡는다.\n"
           + "⚠ pivot은 반드시 (0.5, 1). 앵커·회전은 위 판과 같은 값이어야 한다(다르면 진입 시 경고가 뜬다).")]
    [SerializeField] RectTransform bottom;

    [Tooltip("맞물리는 순간 번쩍이는 이음매 선. 미배선이면 이 축을 통째로 건너뛴다.\n"
           + "커튼은 매치 덱 화면과 같은 색이라 닫힘이 '색의 변화'로는 읽히지 않는다 — 이 선이 맞물림을 알리는 신호다.\n"
           + "색과 두께(Height)는 저작한 값을 그대로 쓴다. 저작 알파가 번쩍임의 최대 밝기이고, 코드는 그 사이를 오갈 뿐이다.\n"
           + "⚠ 가로 길이(Width)만은 런타임이 판과 같은 값으로 덮어쓴다.")]
    [SerializeField] Image seam;

    [Header("타이밍")]
    [Tooltip("두 판이 맞물리는 시간(초).")]
    [Min(0f)] [SerializeField] float close = 0.22f;

    [Tooltip("맞물린 채 머무는 시간(초). 갈아치울 것이 덜 준비됐으면 이 시간을 넘겨 더 기다린다.")]
    [Min(0f)] [SerializeField] float hold = 0.08f;

    [Tooltip("두 판이 다시 열리는 시간(초).\n⚠ close보다 길게 유지할 것 — 닫힘이 열림보다 빨라야 "
           + "'던져 넣어진 뒤 눈을 뜨는' 순서로 읽힌다.")]
    [Min(0f)] [SerializeField] float open = 0.35f;

    [SerializeField] Ease closeEase = Ease.InCubic;
    [SerializeField] Ease openEase  = Ease.InCubic;

    [Tooltip("이음매 선이 터졌다 사라지는 시간(초). 0이면 번쩍임을 생략한다.")]
    [Min(0f)] [SerializeField] float seamFlash = 0.08f;

    [Header("기하")]
    [Tooltip("판을 화면 밖까지 넉넉히 키우는 여유(px).")]
    [Min(0f)] [SerializeField] float pad = 64f;

    [Tooltip("위 판이 이음매보다 더 내려와 아래 판 밑에 깔리는 양(px). 대각선 래스터라이즈에서 생기는 1px 틈을 막는다.")]
    [Min(0f)] [SerializeField] float seamOverlap = 2f;

    [Tooltip("갈아치울 것이 준비되지 않아도 이 시간(초)이 지나면 그대로 진행한다 — 무한 대기 방지.")]
    [Min(0f)] [SerializeField] float maxWait = 10f;

    // 씬을 넘어 사는 물건이라 가드도 씬 파괴에 묶이지 않아야 한다(BattleCleanup.s_loading과 같은 논리).
    static bool s_busy;

    ICurtainSwap m_swap;

    // 프리팹을 못 얻어 판 없이 세워진 커튼. 미배선 오류와 구분하려고 든다 — 그쪽은 이미 경고를 냈다.
    bool m_panelless;

    // 프리팹에 저작된 이음매 값. 코드가 크기를 다시 잡을 때 덮어쓰지 않도록 시작 시 떠 둔다.
    Color m_seamColor;
    float m_seamThickness;

    float m_travelUp;
    float m_travelDown;

    /// <summary>커튼이 도는 중인가. 진입 연출이 끝나기를 기다리는 쪽이 묻는 창구다.</summary>
    public static bool IsBusy => s_busy;

    // 판을 못 얻은 커튼도 교체는 끝까지 책임진다(하드컷) — 연출 때문에 화면이 갇히면 탈출로가 없다.
    bool HasPanels => top != null && bottom != null;

    /// <summary>커튼이 닫혀 화면을 덮은 뒤 _swap이 갈아치우고, 새 화면 위에서 커튼이 열린다.
    /// 무엇이 갈리든 커튼은 가리지 않는 원형 창구다.</summary>
    public static void Play(ICurtainSwap _swap)
    {
        if (!TryPlay(_swap)) _swap?.Abort();
    }

    // 커튼이 닫히는 0.22초에 맞춘 길이 — 소리가 커튼과 같이 빠져야 화면만 덮이고 소리는 남는 꼴이 안 된다.
    const float BgmFadeOutSeconds = 0.25f;

    /// <summary>커튼이 덮은 사이 _scene으로 갈아탄다. 로비 → 배틀 진입 전용(복귀는 LoadingCoverView.LoadScene).</summary>
    /// <param name="_onBeforeLoad">씬 교체 직전 1회 호출. 계약은 SceneLoadSwap 참고.</param>
    public static void LoadScene(string _scene, Action _onBeforeLoad = null)
    {
        // 씬을 벗어나는 모든 호출부가 이 창구를 지나므로 BGM 퇴장은 여기 한 곳에서 책임진다.
        SoundManager.Instance?.FadeOutBGM(BgmFadeOutSeconds);

        Play(new SceneLoadSwap(_scene, _onBeforeLoad));
    }

    // 커튼을 세워 연출을 건다. 걸지 못했으면 false — 호출부가 교체를 직접 책임진다.
    static bool TryPlay(ICurtainSwap _swap)
    {
        if (_swap == null)
        {
            Debug.LogError("[CurtainView] 갈아치울 것이 없습니다 — 커튼을 걸지 않습니다.");

            return false;
        }

        if (s_busy) return false;   // 두 번째 클릭이 전환을 두 번 걸지 못하게

        var t_prefab = SyncUiPrefabs.Get(ESyncUiPrefab.SceneCurtain);
        var t_view   = t_prefab != null ? Instantiate(t_prefab).GetComponent<CurtainView>() : null;

        // 판을 못 얻어도 전환 자체는 되게 한다. 빈 오브젝트를 세워 코루틴 호스트로만 쓰면
        // 폴백이 "판 없는 커튼" 한 갈래로 모인다 — 여기서 교체를 직접 돌리면 경로가 둘로 갈린다.
        if (t_view == null)
        {
            Debug.LogWarning("[CurtainView] 동기 UI 카탈로그에서 커튼 프리팹을 찾지 못해 커튼 없이 전환합니다.");
            t_view = new GameObject("Curtain (판 없음)").AddComponent<CurtainView>();
            t_view.m_panelless = true;
        }

        s_busy = true;

        // Instantiate는 Awake를 그 자리에서 돌리지만 Start는 프레임 끝에 온다 — 이 대입이 연출 시작보다 먼저다.
        t_view.m_swap = _swap;

        return true;
    }

    /// <summary>화면 크기와 이음매 각도만으로 판의 크기·이동거리를 푼다.
    /// MonoBehaviour에 의존하지 않는 순수 함수라 플레이 모드 없이 그대로 검증할 수 있다.</summary>
    public static void Solve(float _w, float _h, float _seamY, float _angleDeg, float _pad,
                             out Vector2 _size, out float _travelUp, out float _travelDown)
    {
        // 기울기 부호는 이음매가 어느 쪽으로 올라가느냐일 뿐, 필요한 크기·이동거리는 양쪽이 같다.
        float t_rad = Mathf.Abs(_angleDeg) * Mathf.Deg2Rad;
        float t_sin = Mathf.Sin(t_rad);
        float t_cos = Mathf.Cos(t_rad);
        float t_tan = Mathf.Tan(t_rad);

        float t_half = _w * 0.5f;
        float t_up   = _h * (1f - _seamY);   // 이음매 위쪽 높이
        float t_down = _h * _seamY;          // 이음매 아래쪽 높이
        float t_far  = Mathf.Max(t_up, t_down);

        // 판 하나가 "이음매에서 가장 먼 화면 모서리"까지 덮는 최소 크기. 두 판이 같은 크기를 쓰도록 먼 쪽으로 잡는다.
        _size = new Vector2(_w * t_cos + 2f * t_far * t_sin + 2f * _pad,
                            t_half * t_sin + t_far * t_cos + _pad);

        // tan 항은 기울어진 이음매가 화면 좌우 끝에서 내려앉는 양이다 — 빼먹으면 판 귀퉁이가 화면에 남는다.
        _travelUp   = t_up   + t_half * t_tan + _pad;
        _travelDown = t_down + t_half * t_tan + _pad;
    }

    void Start()
    {
        if (!HasPanels)
        {
            // 프리팹은 얻었는데 판이 미배선인 경우. 하드컷으로 진행하되 저작 실수를 알린다.
            if (!m_panelless)
                Debug.LogError("[CurtainView] 판이 미배선이라 커튼을 칠 수 없습니다 — 커튼 없이 전환합니다.");
        }
        else
        {
            WarnOnMisauthoredPanels();

            if (seam != null)
            {
                m_seamColor     = seam.color;
                m_seamThickness = seam.rectTransform.sizeDelta.y;
                SetSeamAlpha(0f);
            }
        }

        StartCoroutine(CoRun());
    }

    void OnDisable()
    {
        // 연출 도중 꺼지면 코루틴의 finally가 오지 않는다.
        Finish();
    }

    void OnDestroy()
    {
        Finish();
    }

    // 커튼의 전부. 무엇이 갈리는지는 m_swap만 알고, 여기는 그 네 박자에 판의 움직임을 맞출 뿐이다.
    IEnumerator CoRun()
    {
        // 닫힘 연출로 가릴 수 있는 준비는 여기서 시작된다(씬 비동기 로드 등).
        m_swap.Prepare();

        // 교체가 씬을 갈아탈 수 있다 — 그래도 커튼이 살아남아 열림을 마쳐야 한다.
        DontDestroyOnLoad(gameObject);

        try
        {
            if (HasPanels)
            {
                yield return WaitSeq(BuildClose());
                PlaySeamFlash();
            }

            float t_held = 0f;
            while (t_held < hold || !m_swap.IsReady)
            {
                if (t_held >= maxWait)
                {
                    Debug.LogWarning($"[CurtainView] 교체 준비가 {maxWait}초 안에 끝나지 않아 그대로 진행합니다.");
                    break;
                }

                t_held += Time.unscaledDeltaTime;   // 화면 전환을 덮는 물건이라 timeScale을 신뢰하지 않는다
                yield return null;
            }

            yield return m_swap.Commit();

            if (HasPanels) yield return WaitSeq(BuildOpen());
        }
        finally
        {
            Finish();
            if (this != null) Destroy(gameObject);
        }
    }

    // 시퀀스가 정상 종료하든 외부에서 잘리든(DOTween.KillAll) 같은 자리로 빠져나온다.
    static IEnumerator WaitSeq(Sequence _seq)
    {
        if (_seq == null) yield break;

        while (_seq.IsActive() && !_seq.IsComplete()) yield return null;
    }

    Sequence BuildClose()
    {
        ApplyGeometry(_closed: false);

        var t_seq = DOTween.Sequence().SetLink(gameObject).SetUpdate(true);
        t_seq.Insert(0f, top   .DOAnchorPosY(-seamOverlap, close).SetEase(closeEase));
        t_seq.Insert(0f, bottom.DOAnchorPosY(0f,           close).SetEase(closeEase));

        // 반쯤 닫힌 채 굳으면 그 틈으로 교체 순간이 샌다.
        t_seq.OnKill(SnapClosed);

        return t_seq.Play();   // 재생 책임을 코드에 남긴다(전역 autoPlay 설정에 기대지 않게)
    }

    Sequence BuildOpen()
    {
        ApplyGeometry(_closed: true);   // 교체 사이 해상도가 달라졌을 수 있어 다시 푼다

        var t_seq = DOTween.Sequence().SetLink(gameObject).SetUpdate(true);
        t_seq.Insert(0f, top   .DOAnchorPosY( m_travelUp,   open).SetEase(openEase));
        t_seq.Insert(0f, bottom.DOAnchorPosY(-m_travelDown, open).SetEase(openEase));

        // 잘리면 활짝 열린 채로 굳힌다 — 반쯤 닫힌 커튼이 다음 화면을 가리지 않게.
        t_seq.OnKill(SnapOpen);

        return t_seq.Play();
    }

    // 맞물림을 알리는 흰 선. 유지 구간 안에서 끝나므로 기다리지 않는다.
    void PlaySeamFlash()
    {
        if (seam == null || seamFlash <= 0f) return;

        float t_rise = seamFlash * 0.35f;

        var t_seq = DOTween.Sequence().SetLink(gameObject).SetUpdate(true);
        t_seq.Append(seam.DOFade(m_seamColor.a, t_rise).SetEase(Ease.OutQuad));
        t_seq.Append(seam.DOFade(0f, seamFlash - t_rise).SetEase(Ease.InQuad));
        t_seq.OnKill(() => { if (seam != null) SetSeamAlpha(0f); });
        t_seq.Play();
    }

    void ApplyGeometry(bool _closed)
    {
        var t_root = (RectTransform)transform;

        // 캔버스가 한 번도 갱신되지 않은 프레임에는 rect가 0이다. 스케일러가 없어 화면 크기가 곧 그 값이다.
        float t_w = t_root.rect.width;
        float t_h = t_root.rect.height;
        if (t_w <= 0f) t_w = Screen.width;
        if (t_h <= 0f) t_h = Screen.height;

        // 이음매의 위치·기울기는 프리팹에 저작된 값이 진실원이다.
        Solve(t_w, t_h, top.anchorMin.y, Mathf.DeltaAngle(0f, top.localEulerAngles.z), pad,
              out Vector2 t_size, out m_travelUp, out m_travelDown);

        top.sizeDelta    = t_size;
        bottom.sizeDelta = t_size;

        if (seam != null) seam.rectTransform.sizeDelta = new Vector2(t_size.x, m_seamThickness);

        if (_closed) SnapClosed();
        else         SnapOpen();
    }

    void SnapClosed()
    {
        if (top    != null) top.anchoredPosition    = new Vector2(0f, -seamOverlap);
        if (bottom != null) bottom.anchoredPosition = Vector2.zero;
    }

    void SnapOpen()
    {
        if (top    != null) top.anchoredPosition    = new Vector2(0f,  m_travelUp);
        if (bottom != null) bottom.anchoredPosition = new Vector2(0f, -m_travelDown);
    }

    void SetSeamAlpha(float _a)
    {
        var t_c = m_seamColor;
        t_c.a = _a;
        seam.color = t_c;
    }

    // 어떻게 빠져나가든 여기를 지난다. 교체가 붙잡은 것을 놓고, 가드를 되돌리고, 기다리는 쪽을 깨운다 —
    // 셋 중 하나라도 놓치면 화면이 영영 갇힌다(붙잡힌 씬 활성화 / 다시 못 도는 커튼 / 안 깨어나는 진입 체인).
    void Finish()
    {
        if (m_swap != null)
        {
            m_swap.Abort();
            m_swap = null;
        }

        s_busy = false;
    }

    // 이음매는 "두 판의 pivot이 같은 점에 있고 같은 각으로 돈다"는 것만으로 성립한다.
    // 프리팹에서 한쪽만 손대면 화면 한가운데에 틈이 생기므로, 눈으로 찾기 전에 로그로 잡는다.
    void WarnOnMisauthoredPanels()
    {
#if UNITY_EDITOR
        if (!Mathf.Approximately(top.anchorMin.y, bottom.anchorMin.y))
            Debug.LogWarning($"[CurtainView] 두 판의 앵커가 다릅니다(위 {top.anchorMin.y} ≠ 아래 {bottom.anchorMin.y}) — 이음매가 어긋납니다.");

        if (Mathf.Abs(Mathf.DeltaAngle(top.localEulerAngles.z, bottom.localEulerAngles.z)) > 0.01f)
            Debug.LogWarning($"[CurtainView] 두 판의 기울기가 다릅니다(위 {top.localEulerAngles.z} ≠ 아래 {bottom.localEulerAngles.z}) — 이음매가 어긋납니다.");

        if (!Mathf.Approximately(top.pivot.y, 0f) || !Mathf.Approximately(bottom.pivot.y, 1f))
            Debug.LogWarning($"[CurtainView] pivot 규약 위반(위 {top.pivot} 은 y=0, 아래 {bottom.pivot} 은 y=1이어야 함) — 이음매가 어긋납니다.");
#endif
    }
}
