using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

// 고정 상대와의 대치 인트로 셸(VersusIntroRoot에 부착). MatchmakingShell의 탐색 없는 사촌이다.
//
// 상대를 구하지 않는다 — 부를 때 이미 정해져 있다. 그래서 탐색 문구·점(...)·스캔 띠·취소 버튼·
// IMatchmaker가 전부 없고, 발견 슬램(MatchmakingFx.BuildFound)도 쓰지 않는다.
// 찾지 않은 상대를 찾은 척하면 그 자리에서 매칭 문법이 거짓이 된다.
//
// 그 대신 진입이 그 몫을 한다: 상대가 눌린 정점 자리에서 커지며 올라와 앉는다.
// 사건의 원인이 화면에 있어야 대치가 켜진 것이 아니라 벌어진 것으로 읽힌다.
//
// 무엇을 어떻게 움직일지는 전부 MatchmakingFx·MatchmakingBgFx·MatchmakingEntryFx·MatchHandoffFx가 쥔다.
// 셸은 언제만 정한다 — MatchmakingShell과 같은 규약이라 두 화면의 결이 갈리지 않는다.
public class VersusIntroShell : MonoBehaviour
{
    [SerializeField] MatchProfileView myProfile;
    [SerializeField] MatchProfileView opponentProfile;
    [SerializeField] TMP_Text         titleText;
    [SerializeField] GameObject       versusRoot;

    [Header("문구")]
    [Tooltip("대치 문구. 이 화면은 상대를 찾지 않으므로 탐색 문구가 없다 — 열리는 순간부터 이 한 줄이다.")]
    [SerializeField] string versusTitle = "도전";

    [Header("연출 박자")]
    [Tooltip("진입이 끝난 뒤 충돌까지의 조임 구간(초). 이 구간은 빈 정지가 아니라 압력이 차오르는 시간이다 " +
             "— 이름을 읽는 시간이기도 하다.\n" +
             "매칭 화면보다 길게 잡아야 한다: 그쪽은 앞에 탐색 대기가 있어 이미 긴장이 쌓인 채 이 구간에 " +
             "들어오는데, 여기는 진입 직후라 쌓인 것이 없다. 짧으면 충돌도 같이 약해진다.")]
    [Min(0f)] [SerializeField] float chargeHold = 0.78f;

    [Tooltip("충돌·정착이 끝난 뒤 갈라짐까지의 여운(초). 마지막 한 박은 완전 정지여야 다음 사건이 새 사건으로 읽힌다.")]
    [Min(0f)] [SerializeField] float afterglowHold = 0.34f;

    [Tooltip("대치할 때 두 프로필이 서로에게 다가가는 거리(px). 0이면 이동 없이 VS만 뜬다.")]
    [SerializeField] float versusApproach = 60f;

    [Header("정점에서 올라오는 상대")]
    [Tooltip("상대 배너가 눌린 정점 자리에서 제자리까지 올라오는 시간(초).")]
    [Min(0.01f)] [SerializeField] float originDuration = 0.34f;

    [Tooltip("정점 자리에서 출발할 때의 배율. 맵의 정점 원판만 한 크기에서 배너 크기로 커진다 — " +
             "1이면 커지는 사건이 없어 그냥 미끄러져 온 것이 된다.")]
    [Min(0.05f)] [SerializeField] float originStartScale = 0.34f;

    [Tooltip("상대가 올라오기 시작하는 시각(초). 배경 두 판이 절반 넘게 맞물린 뒤여야 한다 — " +
             "판이 열려 있는 동안 배너가 보이면 아직 맵인 화면 위에 배너가 떠 있는 꼴이 된다.")]
    [Min(0f)] [SerializeField] float originAt = 0.1f;

    [Header("연출")]
    [SerializeField] MatchmakingFx fx = new MatchmakingFx();

    [Tooltip("맵 위로 이 화면이 덮어 오는 진입. 갈라짐(handoffFx)의 앞자리 짝이다.")]
    [SerializeField] MatchmakingEntryFx entryFx = new MatchmakingEntryFx();

    [Tooltip("배경 두 판(BG/Top·BG/Bottom). 이 화면의 등장은 두 판이 대각으로 맞물리는 것이고 " +
             "퇴장은 그 대각이 갈라지는 것이다.")]
    [SerializeField] MatchmakingBgFx bgFx = new MatchmakingBgFx();

    [Tooltip("덱 화면으로 넘어가는 전환. 커튼으로 덮지 않고 두 화면을 잇는다 — 자세한 규약은 MatchHandoffFx 참고.")]
    [SerializeField] MatchHandoffFx handoffFx = new MatchHandoffFx();

    bool m_running;

    // 대치 연출이 배너를 밀었다 되돌릴 기준 위치. 연출 도중 다시 열려도 어긋난 자리를 홈으로 삼지 않게 Awake에서만 잡는다.
    Vector2 m_myHome;
    Vector2 m_opponentHome;

    // 갈라짐에 함께 실려 나가는 것(제목)과 그 기준 위치. 배너와 같은 이유로 Awake에서만 잡는다.
    RectTransform[] m_riders;
    Vector2[]       m_riderHomes;

    // 지금 화면에 떠 있는 안무. 화면이 내려갈 때 함께 걷지 않으면 파괴된 대상 위에서 계속 돈다.
    Sequence m_stage;

    // 이번 진입이 실제로 끝나는 시각. 정점에서 올라오는 길은 entryFx보다 늦게 끝날 수 있어(정점 착지 0.44 > 진입 0.32)
    // 저작값으로 고정할 수 없다 — 조임이 이 값보다 먼저 시작하면 착지 중인 배너를 두 시퀀스가 함께 붙든다.
    float m_entryEnd;

    void Awake()
    {
        if (myProfile       != null) m_myHome       = myProfile.Rect.anchoredPosition;
        if (opponentProfile != null) m_opponentHome = opponentProfile.Rect.anchoredPosition;

        CaptureRiderHomes();

        fx.Capture();
    }

    void OnDestroy()
    {
        KillStage();

        // 고여 있던 빛은 시퀀스가 아니라 fx가 소유한다 — 무대만 걷으면 자가설치 노드가 남는다.
        fx.ClearCharge();
    }

    /// <summary>
    /// 대치 게이트. 호스트가 덱 화면을 세우기 "전에" 이걸 await 한다. 취소 버튼이 없으므로 끝은 하나뿐이다 —
    /// 끝까지 돌거나 씬이 내려간다. 물러나는 자리는 다음 화면(덱)의 취소 버튼이다.
    /// </summary>
    /// <param name="_originScreenPoint">눌린 정점의 화면 좌표. 없으면 상대도 배너처럼 바깥에서 들어온다.</param>
    public async UniTask PlayVersusAsync(MatchOpponent _opponent, Vector2? _originScreenPoint,
                                         CancellationToken _ct)
    {
        // 이미 진행 중인데 다시 부르면 두 await가 같은 화면을 두고 경쟁한다.
        if (m_running)
        {
            Debug.LogWarning("[VersusIntroShell] 대치가 이미 진행 중이다 — 중복 진입을 무시한다.");

            return;
        }

        m_running = true;

        // 끝까지 돈 경우에만 화면을 남긴다 — 그때만 갈라짐(PlayHandoffAsync)이 이어받아 내려 준다.
        bool t_handedOn = false;
        try
        {
            Open(_opponent, _originScreenPoint);

            // 진입 + 조임. 조임은 진입 시퀀스에 이어 붙어 돌고 있다.
            if (await WaitAsync(m_entryEnd + chargeHold, _ct)) return;

            // 충돌이 무대를 갈아탄다. 조임이 끌어다 놓은 자리에서 그대로 이어받으므로 되돌리지 않는다.
            PlayVersus();

            // 정착 + 여운. 여운이 안무보다 짧으면 VS의 호흡이 잘린 채 갈라짐이 시작된다.
            if (await WaitAsync(fx.VersusDuration + Mathf.Max(fx.AfterglowDuration, afterglowHold), _ct)) return;

            t_handedOn = true;
        }
        finally
        {
            m_running = false;

            // 취소·예외로 빠지면 넘겨받을 화면이 없다. 안 내리면 딤과 배경 두 판이 로비를 덮은 채 남아
            // 터치까지 먹는다 — MatchmakingShell이 finally에서 닫는 것과 같은 규약이다.
            if (!t_handedOn) Close();
        }
    }

    /// <summary>
    /// 덱 화면으로 넘어가는 전환. 호스트가 덱 화면을 세워 두고 이걸 await 한다.
    /// 끝나면 이 화면은 내려가고 모든 축이 저작 상태로 돌아간다 — 전환은 도달하는 과정만 바꾼다.
    /// </summary>
    public async UniTask PlayHandoffAsync(MatchHandoffTargets _targets, CancellationToken _ct)
    {
        KillStage();

        var t_root = (RectTransform)transform;

        m_stage = handoffFx.Build(myProfile, opponentProfile, VersusRect, fx.Dim.Target,
                                  t_root, Riders, fx.RaySprite, in _targets);
        m_stage.SetLink(gameObject);

        // 배경 두 판이 갈라지며 덱 화면이 드러난다. 이 축이 없으면 판이 덱을 가린 채 등장 안무가 진행되다가,
        // 화면을 내리는 프레임에 이미 절반쯤 진행된 덱이 튀어나온다.
        m_stage.Insert(0f, bgFx.BuildPart(t_root));

        // 배너가 다 나가고 배경 판까지 다 열린 프레임에 내려간다. 걷어내는 도중에 끄는 것이 곧 하드컷이다.
        m_stage.InsertCallback(Mathf.Max(handoffFx.CloseAt, bgFx.PartDuration), Close);

        await m_stage.ToUniTask(cancellationToken: _ct).SuppressCancellationThrow();

        // 씬이 내려가는 중이다 — 파괴될 오브젝트를 건드리지 않는다.
        if (_ct.IsCancellationRequested) return;

        Close();   // 안무가 중간에 잘렸을 수 있다(SetActive는 멱등).
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    // 진입. 상대가 이미 정해져 있으므로 두 배너를 처음부터 그린 채 연다.
    void Open(in MatchOpponent _opponent, Vector2? _originScreenPoint)
    {
        gameObject.SetActive(true);

        // 직전 전환이 배너를 화면 밖으로 밀어내고 화면을 줄여 놓은 채 끝났다 — 저작 상태로 되돌린 뒤에 연다.
        KillStage();

        // 배경을 먼저 되돌린다 — 지난 전환이 판을 화면 밖으로 밀어 놓았고, 지난 확정이 밝기 축의 기준 색을
        // 덱 색으로 옮겨 놓았다. 기준을 저작값으로 돌린 뒤라야 이어지는 fx.Reset이 옳은 색으로 칠한다.
        bgFx.Reset(fx.Dim);

        fx.Reset(myProfile, opponentProfile, (RectTransform)transform, VersusRect);
        handoffFx.Reset((RectTransform)transform, VersusRect);

        RestoreHome(myProfile,       m_myHome);
        RestoreHome(opponentProfile, m_opponentHome);
        RestoreRiders();

        if (titleText  != null) titleText.text = versusTitle;
        if (versusRoot != null) versusRoot.SetActive(false);

        if (myProfile       != null) myProfile.Render(MatchProfile.OfLocalPlayer());
        if (opponentProfile != null) opponentProfile.Render(_opponent.Profile);

        var t_root = (RectTransform)transform;

        // 정점에서 올라오는 길이 있으면 상대는 진입 안무에 맡기지 않는다 — 그쪽은 바깥에서 꽂히는 축이라
        // 출발 자리를 자기가 못 박기 때문이다. 이 화면에서 상대의 출발 자리는 눌린 정점이다.
        //
        // 자리를 **먼저** 풀어 두는 이유: 좌표 변환이 실패하면 상대를 진입 안무에 그대로 태워야 하는데,
        // 안무를 지은 뒤에 실패를 알면 상대가 어느 축에도 안 실려 t=0부터 그냥 떠 있게 된다
        // (배경 두 판이 아직 닫히는 중인 화면 위에 배너가 떠 있는 꼴 — originAt 툴팁이 경계한 그림이다).
        Vector2 t_originAnchored = default;
        bool    t_fromNode       = _originScreenPoint.HasValue
                                && opponentProfile != null
                                && TryResolveOriginAnchored(_originScreenPoint.Value, out t_originAnchored);

        // 상대가 정점에서 올라오면 진입은 그 착지까지다. 배너 중 가장 늦게 앉는 것이 진입의 끝이다.
        m_entryEnd = t_fromNode
                   ? Mathf.Max(entryFx.Duration, originAt + originDuration)
                   : entryFx.Duration;

        Sequence t_enter = entryFx.Build(myProfile, t_fromNode ? null : opponentProfile, VersusRect,
                                         fx.Dim.Target, t_root, Riders, bgFx.EnterNormal);

        // 배경 두 판이 맞물려 맵을 덮는 것이 곧 이 화면의 등장이다 — 배너는 그 뒤에 들어온다.
        t_enter.Insert(0f, bgFx.BuildClose(t_root));

        // 상대가 정해졌다는 사실을 화면 전체의 색이 말한다. 그 색이 이미 다음 화면의 색이라
        // 나중에 판이 갈라질 때 두 화면이 색으로 이어진다.
        t_enter.Insert(0f, bgFx.BuildConfirm(fx.Dim));

        // entryFx가 DOKill로 출발 자세를 지운 뒤에 얹는다 — 순서를 뒤집으면 이 자세가 그 자리에서 지워진다.
        if (t_fromNode) StageOriginRise(t_enter, t_originAnchored);

        // 조임은 진입이 끝나는 자리에 이어 붙인다. 별도 무대로 돌리면 그 사이 한 프레임이 완전 정지가 되어,
        // 채우려던 바로 그 공백이 앞으로 옮겨 갈 뿐이다.
        t_enter.Insert(m_entryEnd,
                       fx.BuildCharge(myProfile != null ? myProfile.Rect : null, m_myHome,
                                      opponentProfile != null ? opponentProfile.Rect : null, m_opponentHome,
                                      VersusStep, t_root, VersusAnchored, chargeHold));

        PlayStage(t_enter);
    }

    // 정점의 화면 좌표를 상대 배너의 출발 anchoredPosition으로 옮긴다. 아무것도 건드리지 않는다 —
    // 실패를 안무를 짓기 전에 알아야 폴백(진입 안무에 태우기)이 가능하기 때문이다.
    bool TryResolveOriginAnchored(Vector2 _screenPoint, out Vector2 _anchored)
    {
        _anchored = default;

        RectTransform t_rect   = opponentProfile.Rect;
        var           t_parent = t_rect.parent as RectTransform;

        if (t_parent == null) return false;

        // 오버레이 캔버스는 ScreenSpaceOverlay라 카메라가 null이어야 한다 — 물리면 좌표가 통째로 어긋난다.
        Canvas t_canvas = t_rect.GetComponentInParent<Canvas>();
        Camera t_cam    = t_canvas != null && t_canvas.renderMode != RenderMode.ScreenSpaceOverlay
                        ? t_canvas.worldCamera
                        : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(t_parent, _screenPoint, t_cam,
                                                                    out Vector2 t_local))
            return false;

        // 부모 피벗 기준 로컬 좌표를 anchoredPosition으로 옮긴다 — 앵커를 아무렇게나 저작해도 맞는 산술이다.
        Vector2 t_refPoint = (t_rect.anchorMin + t_rect.anchorMax) * 0.5f - t_parent.pivot;

        _anchored = t_local - new Vector2(t_refPoint.x * t_parent.rect.width,
                                          t_refPoint.y * t_parent.rect.height);

        return true;
    }

    // 상대가 눌린 정점 자리에서 커지며 제자리로 올라온다. 맵의 원판이 배너가 되는 것이라,
    // 이 화면이 어디서 왔는지가 화면 안에 남는다.
    void StageOriginRise(Sequence _seq, Vector2 _fromAnchored)
    {
        RectTransform t_rect  = opponentProfile.Rect;
        CanvasGroup   t_group = opponentProfile.Group;

        t_rect.DOKill();
        t_group.DOKill();

        t_rect.anchoredPosition = _fromAnchored;
        t_rect.localScale       = Vector3.one * originStartScale;
        t_group.alpha           = 0f;

        // 감속이라 자리에 앉았다가 된다. 가속이면 무언가에 밀려 들어온 것으로 보인다.
        _seq.Insert(originAt, t_rect.DOAnchorPos(m_opponentHome, originDuration).SetEase(Ease.OutCubic));
        _seq.Insert(originAt, t_rect.DOScale(1f, originDuration).SetEase(Ease.OutCubic));

        // 앞에서 다 나타난다 — 끝까지 흐리면 제자리에서 생겨난 것으로 보여 올라온 사실이 지워진다.
        _seq.Insert(originAt, t_group.DOFade(1f, originDuration * 0.55f).SetEase(Ease.OutQuad));
    }

    void PlayVersus()
    {
        RectTransform t_vs = VersusRect;
        if (versusRoot != null) versusRoot.SetActive(true);

        // 짓기 전에 먼저 걷는다. PlayStage도 걷지만 그건 인자를 다 만든 뒤라,
        // 안무를 짓는 동안 조임이 아직 살아 있어 같은 배너를 두 시퀀스가 붙들고 있는 순간이 생긴다.
        KillStage();

        PlayStage(fx.BuildVersus(myProfile != null ? myProfile.Rect : null, m_myHome,
                                 opponentProfile != null ? opponentProfile.Rect : null, m_opponentHome,
                                 VersusStep, t_vs, (RectTransform)transform));
    }

    // 미는 방향은 두 배너의 실제 배치에서 구한다 — 어느 쪽이 위인지 프리팹을 몰라도 된다.
    // 조임과 충돌이 같은 걸음을 써야 끌린 방향 그대로 부딪힌다.
    Vector2 VersusStep
    {
        get
        {
            Vector2 t_gap = m_opponentHome - m_myHome;

            return (t_gap.sqrMagnitude > 0.01f ? t_gap.normalized : Vector2.right) * versusApproach;
        }
    }

    // VS가 뜰 자리. 조임의 빛이 여기에 고인다 — VS가 아직 꺼져 있어도 좌표는 읽을 수 있다.
    Vector2 VersusAnchored => VersusRect != null ? VersusRect.anchoredPosition : Vector2.zero;

    RectTransform VersusRect => versusRoot != null ? (RectTransform)versusRoot.transform : null;

    // 배너에 실리지 않은 것. 갈라짐이 이것도 함께 실어 내보낸다 —
    // 아니면 전환 한복판에서 제목이 한 프레임에 증발한다. 취소 버튼이 없어 매칭 화면보다 한 칸 짧다.
    RectTransform[] Riders => m_riders ??= new[]
    {
        titleText != null ? (RectTransform)titleText.transform : null,
    };

    // 한 번에 도는 안무는 하나뿐이다 — 진입이 아직 도는 중에 대치가 겹치면 같은 배너를 두 트윈이 민다.
    void PlayStage(Sequence _seq)
    {
        KillStage();

        if (_seq == null) return;

        m_stage = _seq.SetLink(gameObject);
        m_stage.Play();
    }

    void KillStage()
    {
        m_stage?.Kill();
        m_stage = null;
    }

    // 제목의 저작 자리를 한 번만 잡는다. Riders 프로퍼티가 배열을 세우고 여기가 그 자세를 기록한다.
    void CaptureRiderHomes()
    {
        RectTransform[] t_riders = Riders;

        m_riderHomes = new Vector2[t_riders.Length];

        for (int t_i = 0; t_i < t_riders.Length; t_i++)
            if (t_riders[t_i] != null) m_riderHomes[t_i] = t_riders[t_i].anchoredPosition;
    }

    // 갈라짐이 밀어낸 제목을 제자리로. 되돌리지 않으면 다음 대치가 제목 없는 화면으로 열린다.
    void RestoreRiders()
    {
        if (m_riderHomes == null) return;

        RectTransform[] t_riders = Riders;

        for (int t_i = 0; t_i < t_riders.Length && t_i < m_riderHomes.Length; t_i++)
        {
            RectTransform t_rider = t_riders[t_i];
            if (t_rider == null) continue;

            t_rider.DOKill();
            t_rider.anchoredPosition = m_riderHomes[t_i];

            var t_group = t_rider.GetComponent<CanvasGroup>();
            if (t_group == null) continue;   // 전환을 한 번도 안 탔으면 아직 붙지 않았다

            t_group.DOKill();
            t_group.alpha = 1f;
        }
    }

    static void RestoreHome(MatchProfileView _view, Vector2 _home)
    {
        if (_view == null) return;

        _view.Rect.DOKill();
        _view.Rect.anchoredPosition = _home;
        _view.Rect.localScale       = Vector3.one;

        // 전환이 배너를 통째로 흐려 놓고 끝난다 — 되돌리지 않으면 다음 대치가 투명한 배너로 열린다.
        _view.Group.DOKill();
        _view.Group.alpha = 1f;
    }

    // 취소되면 true. 예외 대신 값으로 받아 호출부가 한 줄로 갈린다.
    static async UniTask<bool> WaitAsync(float _seconds, CancellationToken _ct)
    {
        if (_seconds <= 0f) return _ct.IsCancellationRequested;

        return await UniTask.Delay(TimeSpan.FromSeconds(_seconds), cancellationToken: _ct)
                            .SuppressCancellationThrow();
    }
}
