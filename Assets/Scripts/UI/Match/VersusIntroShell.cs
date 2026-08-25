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
// 이 화면의 사건은 셋뿐이다.
//   ① 커튼 두 판이 위·아래에서 가속 낙하하고, 두 프로필이 그 판에 실려 함께 떨어져 이음매에서 부딪힌다
//   ② 부딪힌 결과로 대립 구도가 선다 — VS가 튀어나오고 화면이 한 박 쉰다
//   ③ 판이 갈라지며 덱 화면이 열린다
//
// 부딪힘은 한 번뿐이다. 내려온 그 힘이 곧 공격이라, 매칭 화면이 갖고 있는 조임 구간
// (둘이 서로에게 끌리다 다시 부딪히는 박자)은 여기에 없다 — 있으면 사건이 두 번이 된다.
// 대신 조임이 만들던 압력(어둠·떨림·고이는 빛)만 낙하 위에 겹쳐 함께 차오른다.
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
    [Tooltip("충돌·정착이 끝난 뒤 갈라짐까지의 여운(초). 마지막 한 박은 완전 정지여야 다음 사건이 새 사건으로 읽힌다.\n" +
             "이름을 읽는 시간이기도 하다 — 이 화면 전체 길이를 조절하는 유일한 손잡이다.\n" +
             "낙하(MatchmakingBgFx.closeDuration)와 충돌(MatchmakingFx.VersusDuration)은 각자의 진실원이 " +
             "따로 있어 여기서 못 만진다.")]
    [Min(0f)] [SerializeField] float afterglowHold = 0.34f;

    [Tooltip("대치할 때 두 프로필이 서로에게 다가가는 거리(px). 0이면 이동 없이 VS만 뜬다.\n" +
             "매칭 화면보다 크게 잡는다 — 그쪽은 물러났다 치는 예비동작이 있지만 여기는 낙하가 그 몫을 " +
             "대신하고 곧바로 관성으로 박히기 때문이다.")]
    [SerializeField] float versusApproach = 90f;

    [Header("연출")]
    [SerializeField] MatchmakingFx fx = new MatchmakingFx();

    [Tooltip("맵 위로 이 화면이 덮어 오는 진입. 갈라짐(handoffFx)의 앞자리 짝이다.\n" +
             "⚠ 이 화면에서는 프로필을 태우지 않는다 — 프로필은 판에 실려 떨어진다. 여기 남은 것은 " +
             "화면 배율과 제목뿐이라 banner* 값은 저작해도 아무 일도 하지 않는다.")]
    [SerializeField] MatchmakingEntryFx entryFx = new MatchmakingEntryFx();

    [Tooltip("배경 두 판(BG/Top·BG/Bottom). 이 화면의 등장은 두 판이 맞물리는 것이고 " +
             "퇴장은 그 맞물림이 갈라지는 것이다. 프로필의 낙하 거리·시간·이징도 전부 여기서 나온다.")]
    [SerializeField] MatchmakingBgFx bgFx = new MatchmakingBgFx();

    [Tooltip("덱 화면으로 넘어가는 전환. 커튼으로 덮지 않고 두 화면을 잇는다 — 자세한 규약은 MatchHandoffFx 참고.")]
    [SerializeField] MatchHandoffFx handoffFx = new MatchHandoffFx();

    bool m_running;

    // 대치 연출이 프로필을 밀었다 되돌릴 기준 위치. 연출 도중 다시 열려도 어긋난 자리를 홈으로 삼지 않게 Awake에서만 잡는다.
    Vector2 m_myHome;
    Vector2 m_opponentHome;

    // 갈라짐에 함께 실려 나가는 것(제목)과 그 기준 위치. 프로필과 같은 이유로 Awake에서만 잡는다.
    RectTransform[] m_riders;
    Vector2[]       m_riderHomes;

    // 조임의 떨림이 밀어 놓을 화면 자리. 같은 이유의 1회 캡처다 — 떨리는 중에 다시 잡으면 어긋난 자리가 홈이 된다.
    Vector2 m_rootHome;

    // 지금 화면에 떠 있는 안무. 화면이 내려갈 때 함께 걷지 않으면 파괴된 대상 위에서 계속 돈다.
    Sequence m_stage;

    // 판이 맞물리는 시각 = 프로필이 부딪히는 시각. 낙하와 충돌이 같은 프레임에서 만나는 자리라
    // 이 화면의 모든 박자가 여기를 기준으로 붙는다.
    float m_landAt;

    void Awake()
    {
        if (myProfile       != null) m_myHome       = myProfile.Rect.anchoredPosition;
        if (opponentProfile != null) m_opponentHome = opponentProfile.Rect.anchoredPosition;

        m_rootHome = ((RectTransform)transform).anchoredPosition;

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
    public async UniTask PlayVersusAsync(MatchOpponent _opponent, CancellationToken _ct)
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
            Open(_opponent);

            // 낙하 → 충돌 → 여운이 한 무대에서 이어 돈다. 무대를 갈아타지 않으므로 기다림도 한 번뿐이다.
            if (await WaitAsync(m_landAt + fx.VersusDuration
                              + Mathf.Max(fx.AfterglowDuration, afterglowHold), _ct))
                return;

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

        // 프로필이 다 나가고 배경 판까지 다 열린 프레임에 내려간다. 걷어내는 도중에 끄는 것이 곧 하드컷이다.
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

    // 진입 = 낙하 = 충돌. 셋이 한 무대에서 이어 돈다.
    //
    // ⚠ 짓는 순서가 계약이다. 부품들이 "짓는 순간" 대상을 DOKill 하거나 자가설치 노드를 세우기 때문에,
    //   순서를 바꾸면 방금 지은 축이 그 자리에서 지워지거나 아직 없는 것을 참조하게 된다.
    //     ① entryFx — StageZoom이 화면을 DOKill 한다
    //     ② 조임     — 여기서 VS 자리의 빛(ChargeGlow)이 실제로 세워진다
    //     ③ 충돌     — 짓는 시점에 ②의 빛을 읽어 폭발을 예약하고, 두 프로필을 DOKill 한다
    //     ④ 낙하     — ③의 DOKill 뒤에 지어야 살아남는다
    void Open(in MatchOpponent _opponent)
    {
        gameObject.SetActive(true);

        // 직전 전환이 프로필을 화면 밖으로 밀어내고 화면을 줄여 놓은 채 끝났다 — 저작 상태로 되돌린 뒤에 연다.
        KillStage();

        // 배경을 먼저 되돌린다 — 지난 전환이 판을 화면 밖으로 밀어 놓았고, 지난 확정이 밝기 축의 기준 색을
        // 덱 색으로 옮겨 놓았다. 기준을 저작값으로 돌린 뒤라야 이어지는 fx.Reset이 옳은 색으로 칠한다.
        bgFx.Reset(fx.Dim);

        fx.Reset(myProfile, opponentProfile, (RectTransform)transform, VersusRect);
        handoffFx.Reset((RectTransform)transform, VersusRect);

        RestoreHome(myProfile,       m_myHome);
        RestoreHome(opponentProfile, m_opponentHome);
        RestoreRiders();
        RestoreRootHome();

        if (titleText  != null) titleText.text = versusTitle;
        if (versusRoot != null) versusRoot.SetActive(false);

        if (myProfile       != null) myProfile.Render(MatchProfile.OfLocalPlayer());
        if (opponentProfile != null) opponentProfile.Render(_opponent.Profile);

        var t_root = (RectTransform)transform;

        // 낙하 거리는 판이 푼다. 실린 것이 판과 같은 거리를 써야 이음매 위에 얹혀 있는 것으로 읽힌다.
        bgFx.SolveTravel(t_root, out float t_up, out float t_down);

        // 판이 맞물리는 시각이 곧 부딪히는 시각이다. 화면이 다 들어오기 전에 부딪히면
        // 아직 로비가 비치는 화면 위에서 사건이 벌어진다.
        m_landAt = Mathf.Max(entryFx.Duration, bgFx.CloseDuration);

        // 프로필을 entryFx에 태우지 않는다 — 바깥에서 따로 꽂히는 축과 판에 실려 떨어지는 축은 배타적이다.
        // 남는 것은 화면 배율과 제목뿐이다.
        Sequence t_enter = entryFx.Build(null, null, VersusRect,
                                         fx.Dim.Target, t_root, Riders, bgFx.EnterNormal);

        // 판 두 장이 맞물려 로비를 덮는다. 이게 이 화면의 등장이다.
        t_enter.Insert(0f, bgFx.BuildClose(t_root));

        // 낙하 위에 압력이 함께 차오른다 — 어둠이 내려가고 화면이 점점 떨리고 VS 자리에 빛이 고인다.
        // 프로필 인자를 비우는 것은 서로에게 끌리는 축(드리프트)만 빼기 위해서다.
        // ⚠ 통째로 빼면 안 된다. 여기서 세워지는 빛이 없으면 아래 충돌의 빛 폭발이 함께 사라진다.
        t_enter.Insert(0f, fx.BuildCharge(null, default, null, default,
                                          VersusStep, t_root, VersusAnchored, m_landAt));

        // 관성 그대로 박힌다. 예비동작 거리를 0으로 저작해 두면 낙하 자체가 그 몫을 한다.
        Sequence t_clash = fx.BuildVersus(myProfile != null ? myProfile.Rect : null, m_myHome,
                                          opponentProfile != null ? opponentProfile.Rect : null, m_opponentHome,
                                          VersusStep, VersusRect, t_root);

        // 충돌이 프로필을 DOKill 한 뒤에 얹는다 — 순서를 뒤집으면 낙하가 그 자리에서 지워져
        // 두 프로필이 t=0부터 제자리에 붙은 채 판만 떨어진다.
        StageRide(t_enter, t_up, t_down);

        // 떨림은 진폭이 t²라 착지 프레임에 가장 크다. 무대를 갈아타지 않아 조임의 자체 복구(OnKill)가
        // 여기서 안 돌므로, 부딪힌 자리에서 화면을 직접 제자리로 돌려놓는다.
        t_enter.InsertCallback(m_landAt, RestoreRootHome);

        // 색이 덱 색으로 옮겨 앉는 것은 부딪힘의 결과다 — 착지 뒤에 시작해야 원인과 결과가 갈린다.
        t_enter.Insert(m_landAt, bgFx.BuildConfirm(fx.Dim));

        // VS는 부딪히는 그 프레임에 켜진다. 미리 켜면 슬램의 출발 배율(1 + vsOvershoot)이 먼저 보여
        // 튀어나온 것이 아니라 줄어든 것이 된다.
        t_enter.InsertCallback(m_landAt + fx.HitAt, ShowVersus);

        t_enter.Insert(m_landAt, t_clash);

        PlayStage(t_enter);
    }

    // 두 프로필이 판에 실려 함께 떨어진다.
    //
    // 부모를 판으로 갈아끼우지 않는다 — 판은 기울어 있고 배율도 1이 아니라, 자식으로 옮기면 프로필이
    // 함께 기울고 줄어든다. 게다가 두 프로필의 좌표계가 서로 갈려 대치 걸음(VersusStep)도
    // 갈라짐의 밀어내기도 전부 무의미해진다.
    // 실려 있다는 말은 부모가 같다는 뜻이 아니라 거리·시간·이징이 같다는 뜻이다.
    void StageRide(Sequence _seq, float _up, float _down)
    {
        // 방향은 순수 수직이다 — 판도 (0, ±travel)로만 움직인다. 이음매 법선을 태우면 프로필이
        // 판 위에서 가로로 미끄러져 실려 있음이 깨진다(법선은 바깥에서 들어오는 제목 몫으로 남는다).
        StageRideOne(_seq, opponentProfile, m_opponentHome,  _up);
        StageRideOne(_seq, myProfile,       m_myHome,       -_down);
    }

    void StageRideOne(Sequence _seq, MatchProfileView _view, Vector2 _home, float _offsetY)
    {
        if (_view == null || Mathf.Approximately(_offsetY, 0f)) return;

        RectTransform t_rect = _view.Rect;

        t_rect.anchoredPosition = _home + new Vector2(0f, _offsetY);

        _seq.Insert(0f, t_rect.DOAnchorPos(_home, bgFx.CloseDuration).SetEase(bgFx.CloseEase));
    }

    void ShowVersus()
    {
        if (versusRoot != null) versusRoot.SetActive(true);
    }

    // 미는 방향은 두 프로필의 실제 배치에서 구한다 — 어느 쪽이 위인지 프리팹을 몰라도 된다.
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

    // 프로필에 실리지 않은 것. 갈라짐이 이것도 함께 실어 내보낸다 —
    // 아니면 전환 한복판에서 제목이 한 프레임에 증발한다. 취소 버튼이 없어 매칭 화면보다 한 칸 짧다.
    RectTransform[] Riders => m_riders ??= new[]
    {
        titleText != null ? (RectTransform)titleText.transform : null,
    };

    // 한 번에 도는 안무는 하나뿐이다 — 진입이 아직 도는 중에 다른 축이 겹치면 같은 프로필을 두 트윈이 민다.
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

    // 떨림이 밀어 놓은 화면을 제자리로. 조임이 자체 복구(OnKill)를 갖고 있지만 이 화면은 무대를
    // 갈아타지 않아 그것이 안무가 다 끝난 뒤에야 돈다 — 부딪힌 자리에서 셸이 직접 되돌린다.
    void RestoreRootHome()
    {
        ((RectTransform)transform).anchoredPosition = m_rootHome;
    }

    static void RestoreHome(MatchProfileView _view, Vector2 _home)
    {
        if (_view == null) return;

        _view.Rect.DOKill();
        _view.Rect.anchoredPosition = _home;
        _view.Rect.localScale       = Vector3.one;

        // 전환이 프로필을 통째로 흐려 놓고 끝난다 — 되돌리지 않으면 다음 대치가 투명한 프로필로 열린다.
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
