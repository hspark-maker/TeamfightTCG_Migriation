using System;
using DG.Tweening;
using UnityEngine;

/// <summary>성장 한 번을 보여주는 연출의 공통 뼈대(강화 = 담금질, 진화 = 탈각).
///
/// 여기가 지는 것은 호출부와의 약속 하나뿐이다 — 세 콜백이 스킵·중단·재진입 어느 경로로든
/// 각각 정확히 한 번, 이 순서로 간다는 것. 무엇을 보여줄지는 파생이 정한다.
///
/// 이 계약을 파생마다 다시 구현하지 않는 이유: 스킵·"한 번 더"·카드 전환이 겹치는 경로가
/// 이 클래스에서 가장 미묘한 부분이라, 복제하면 두 연출의 규약이 조용히 갈린다.
///
/// 연출은 <b>두 토막</b>이다 — 성패를 모르는 앞 구간(<see cref="PlayLead"/>)과 성패가 필요한
/// 결말(<see cref="Commit"/>). 성패는 서버 주사위라 결말을 앞당길 수 없으므로, 그 왕복을 앞 구간이 덮는다.
/// 앞 구간은 누른 프레임에 시작하고, 답은 그것이 도는 사이에 도착한다.</summary>
public abstract class CardGrowthRitualView : MonoBehaviour
{
    protected readonly EnhanceRitualHandoff m_handoff = new EnhanceRitualHandoff();   // 세 신호의 순서·1회 보장

    // 네 경로(스킵·중단·복귀·이어받기)가 어느 자리에서 들어왔는지로 동작이 갈린다.
    // 불리언을 늘리지 않는 이유: 앞 구간이 생기면서 조합이 여섯 자리로 늘었고, 그것을 플래그로 나누면
    // "앞 구간이 도는 중이면서 결과를 기다리는 중" 같은 있을 수 없는 상태가 표현돼 버린다.
    enum EPhase
    {
        Idle,       // 아무것도 서 있지 않다
        Lead,       // 앞 구간이 도는 중(답을 아직 모른다)
        Waiting,    // 앞 구간이 끝났는데 답이 아직 없다 — 덮인 채 제자리 숨만 돈다
        Finale,     // 결말이 도는 중
        Await,      // 결과를 무대에 남긴 채 복귀 신호를 기다리는 중
        Return,     // 무대를 되돌리는 중(복귀 또는 접힘)
    }

    Sequence m_seq;                 // 지금 도는 시퀀스(앞 구간·제자리 숨·결말·복귀 중 하나)
    EPhase   m_phase;
    bool     m_cancelling;          // 잘라내는 중. 콜백이 호출부를 타고 PlayReturn으로 되돌아오는 것을 막는다
    bool     m_stageRetracted;      // 무대를 걷은 채 다음 연출을 기다리는 중("한 번 더" 경로)
    bool     m_unwindingLead;       // 접힘(AbortLead)의 되감기가 도는 중. 이 길의 마무리는 오직 접힘 콜백뿐이다

    Action m_onLeadAborted;         // 앞 구간만 돌다 접힌 뒤 조작을 되살릴 자리. 정확히 한 번 흐른다

    // 답. 앞 구간이 도는 사이에 도착하면 여기 담기고, 앞 구간의 OnComplete가 그것을 보고 결말을 잇는다.
    bool            m_committed;
    EEnhanceOutcome m_outcome;
    bool            m_awaitReturn;

    /// <summary>연출이 진행 중인가(답을 기다리는 동안도, 결과를 남긴 채 기다리는 동안도 포함).
    /// 호출부는 이 동안 재입력·카드 넘기기·닫기를 막는다.</summary>
    public bool IsPlaying => this.m_phase != EPhase.Idle;

    /// <summary>연출을 걸 무대가 배선돼 있는가. 없으면 몸짓 없이 콜백만 흐른다 —
    /// 배선 실패가 소프트락이 되면 안 된다.</summary>
    protected abstract bool HasStage { get; }

    /// <summary>복귀 구간의 길이.</summary>
    protected abstract float ReturnDuration { get; }

    /// <summary>성패를 모르는 앞 구간을 지금 태운다. 누른 프레임에 부르는 것이 이 메서드의 존재 이유다 —
    /// 성패는 서버가 굴리므로, 그 왕복을 카드가 빛에 덮이는 동안으로 덮는다.
    ///
    /// _onLeadAborted는 답이 "아무 일도 없었다"로 왔을 때(<see cref="AbortLead"/>) 되감기가 끝나는 자리다.
    /// 되감기는 연출이 맡고 조작 복구·통지는 이 콜백 하나가 한 곳에서 한다 — 잘려도, 무대가 없어도 정확히 한 번.</summary>
    public void PlayLead(Action _onLeadAborted)
    {
        // 앞 판이 아직 무대를 쥐고 있다면 잘라낸다 — 두 연출이 같은 노드를 두고 싸우면 카드가 굳는다.
        // 다만 앞 판의 콜백은 삼키지 않는다(CancelImmediate가 남은 것을 전부 흘린다).
        if (IsPlaying) CancelImmediate();

        // 앞 판의 접힘 신호가 아직 남아 있으면 새 판의 것으로 덮기 전에 흘린다 —
        // 덮어 버리면 앞 판을 기다리던 호출부가 영영 잠금을 못 푼다(정상 경로에선 Commit이 이미 비웠다).
        FlushLeadAborted();

        this.m_onLeadAborted = _onLeadAborted;
        this.m_committed     = false;

        // 걷힌 무대를 그대로 물려받는가("한 번 더"). 플래그는 여기서 소비한다 — 이어받는 것은 이 한 번뿐이다.
        // 소비 자리가 결말이 아니라 여기인 이유: _chained를 보는 곳은 진입 구간 하나이고 그것이 앞 토막에 있다.
        bool t_chained        = this.m_stageRetracted;
        this.m_stageRetracted = false;

        if (!HasStage)
        {
            // 보여줄 무대가 없다. 몸짓 없이 "답을 기다리는 중"만 세운다 — Commit이 오면 그 자리에서 마무리한다.
            this.m_phase = EPhase.Waiting;
            return;
        }

        CaptureBase();

        // 이어받는 경우엔 즉시 원복하지 않는다 — 앞 결과가 남긴 표면을 진입 구간이 데려온다.
        if (!t_chained) RestoreVisual();

        // 연출 재질은 여기서부터 걸친다(RestoreVisual이 벗기므로 순서가 뒤바뀌면 안 된다).
        AttachLayers();

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);
        float    t_at  = BuildLead(t_seq, t_chained);

        // 절단면을 못 박는다 — 앞 구간의 트윈이 전부 미배선이면 시퀀스가 여기 닿기 전에 끝나 버린다.
        t_seq.InsertCallback(t_at, () => { });

        // ⚠ 답이 앞 구간 도중에 도착해도 여기서만 이어간다. Commit이 결말로 점프하면
        //   카드가 반쯤 덮인 채 값이 바뀐다 — 빠른 네트워크일수록 더 크게 보인다.
        //   (OnKill이 아니라 OnComplete인 것도 계약이다: 잘려 나간 길은 이어붙이지 않는다.)
        t_seq.OnComplete(() =>
        {
            this.m_seq = null;

            if (this.m_committed) StartFinale();
            else                  StartWait();
        });

        this.m_phase = EPhase.Lead;
        this.m_seq   = t_seq;
        t_seq.Play();   // 재생 책임을 코드에 남긴다(PopupTransition과 같은 결).
    }

    /// <summary>답이 왔다 — 결말을 예약한다. _outcome은 Success/Failed만 온다(나머지는 결제 전 차단이라 보여줄 것이 없다).
    ///
    /// 앞 구간이 아직 도는 중이면 <b>예약만</b> 하고 자르지 않는다. 대기 중이면 곧바로 결말이 시작된다.
    /// 앞 구간이 이미 잘려 나갔으면(닫힘·카드 전환) <b>false</b> — 이어붙일 무대가 없으므로
    /// 호출부가 무대 없이 마무리해야 한다(그래야 재화만 나가고 화면이 굳는 갈래가 안 생긴다).
    ///
    /// _onReveal은 값을 화면에 반영할 시점 — 어느 프레임이 그 자리인지는 파생이 정한다(담금질은 백열 아래,
    /// 탈각은 옛 껍질 아래). _onSettled는 카드 위 연출이 다 끝난 시점 — 호출부가 여기서 결과판을 띄운다.
    ///
    /// _awaitReturn이면 결과를 무대에 남긴 채 멈추고 <see cref="PlayReturn"/>을 기다린다.
    /// 아니면 스스로 걷고 _onFinished까지 이어간다.</summary>
    public bool Commit(EEnhanceOutcome _outcome, bool _awaitReturn, Action _onReveal, Action _onSettled, Action _onFinished)
    {
        if (this.m_phase != EPhase.Lead && this.m_phase != EPhase.Waiting) return false;

        this.m_outcome     = _outcome;
        this.m_awaitReturn = _awaitReturn;
        this.m_committed   = true;

        // 답이 온 이상 접히는 길은 없다 — 이 판의 마무리는 이제 세 신호가 진다.
        this.m_onLeadAborted = null;

        this.m_handoff.Arm(_onReveal, _onSettled, _onFinished);

        if (this.m_phase == EPhase.Lead) return true;   // 예약만. 앞 구간의 OnComplete가 잇는다

        // 답을 기다리던 중이었다 — 제자리 숨을 걷고 결말로 넘어간다.
        Sequence t_wait = this.m_seq;
        this.m_seq = null;
        t_wait?.Kill();

        StartFinale();
        return true;
    }

    /// <summary>답이 "아무 일도 없었다"로 왔다(서버 거절) — 앞 구간만 돈 무대를 조용히 되감는다.
    /// 사유를 문구로 그리지 않는 것이 규약이다(한계돌파와 같은 결) — 연출을 걷고 값만 제자리로 돌린다.
    ///
    /// 되돌릴 무대가 없거나 이미 잘려 나간 뒤여도 접힘 콜백은 반드시 흐른다 —
    /// 그 하나가 조작 잠금을 푸는 유일한 못이라, 유실되면 버튼이 죽은 채 굳는다.</summary>
    public void AbortLead()
    {
        if (this.m_phase != EPhase.Lead && this.m_phase != EPhase.Waiting)
        {
            FlushLeadAborted();
            return;
        }

        Sequence t_prev = this.m_seq;
        this.m_seq       = null;
        this.m_committed = false;
        t_prev?.Kill();   // 앞 구간엔 OnKill이 없다 — 잘라도 결말로 이어지지 않는다

        if (!HasStage)
        {
            this.m_phase = EPhase.Idle;
            RestoreVisual();
            FlushLeadAborted();
            return;
        }

        // 기다리는 사이 어떤 경로로든 벗겨졌을 수 있다 — 되감기도 이 재질 위에서 돈다.
        AttachLayers();

        float t_dur = Mathf.Max(0.05f, ReturnDuration);

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);
        BuildReturn(t_seq, 0f, t_dur, t_dur);

        t_seq.OnKill(() =>
        {
            this.m_seq           = null;
            this.m_phase         = EPhase.Idle;
            this.m_unwindingLead = false;

            RestoreVisual();
            FlushLeadAborted();
        });

        this.m_phase         = EPhase.Return;
        this.m_unwindingLead = true;
        this.m_seq           = t_seq;
        t_seq.Play();
    }

    /// <summary>남은 구간을 최종 상태로 끌어당긴다. 콜백은 순서대로 그대로 실행된다.
    /// 끌어당길 것이 있었으면 <b>true</b> — 부른 쪽은 "이 탭은 스킵으로 쓰였다"로 읽는다.
    ///
    /// 당길 것이 없는 두 단계가 서로 다른 답을 내는 것이 이 메서드의 요점이다.
    /// 답을 기다리는 동안(Waiting)은 <b>false</b>다 — 당길 것이 서버에 있지 탭에 있지 않고,
    /// 여기서 탭을 삼키면 왕복이 길어질 때 화면에서 나가는 문이 없어진다(대기 상한이 네트워크다).
    /// 반대로 결과를 무대에 남기고 기다리는 동안(Await)은 <b>true</b>다 — 그 구간의 입력은 결과판의 것이라
    /// false를 흘리면 부른 쪽이 배경 탭으로 읽어 창을 닫아 버린다("한 번 더" 연타가 끊긴다).</summary>
    public bool RequestSkip()
    {
        if (this.m_phase == EPhase.Waiting) return false;
        if (this.m_phase == EPhase.Await)   return true;
        if (this.m_seq == null || !this.m_seq.IsActive()) return false;

        this.m_seq.Complete(true);
        return true;
    }

    /// <summary>결과를 걷고 무대를 원래대로 되돌린다(결과판이 닫힐 때 호출부가 부른다).
    /// 기다리는 중이 아니면 남은 콜백만 흘린다 — 어느 경로로 와도 조작이 죽은 채 굳지 않게.</summary>
    public void PlayReturn()
    {
        // 잘라내는 중에 콜백을 타고 되돌아온 것이다 — 남은 콜백은 CancelImmediate가 마저 흘린다.
        if (this.m_cancelling) return;

        // 결과판이 뜬 적이 없는데 복귀가 들어왔다 = 앞 구간만 도는 중이었다(순서가 어긋났다).
        // 걷힌 무대를 남기지 않으려면 여기서 잘라내는 수밖에 없다.
        if (this.m_phase == EPhase.Lead || this.m_phase == EPhase.Waiting)
        {
            CancelImmediate();
            return;
        }

        // 결과판이 무대보다 먼저 닫힐 수 있다(스킵 경로). 남은 구간을 끌어당겨야 복귀가 결과 자세에서 출발한다.
        if (this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);

        if (this.m_phase != EPhase.Await)
        {
            // 기다린 적이 없는데 불렸다 = 어딘가에서 순서가 어긋났다.
            this.m_handoff.FlushAll();
            return;
        }

        this.m_phase = EPhase.Return;

        // 기다리는 사이 어떤 경로로든 벗겨졌을 수 있다 — 복귀 구간도 이 재질 위에서 돈다.
        AttachLayers();

        if (!HasStage)
        {
            this.m_phase = EPhase.Idle;
            RestoreVisual();
            this.m_handoff.Finished();
            return;
        }

        float t_dur = Mathf.Max(0.05f, ReturnDuration);

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);
        BuildReturn(t_seq, 0f, t_dur, t_dur);

        t_seq.OnKill(() =>
        {
            this.m_seq   = null;
            this.m_phase = EPhase.Idle;

            RestoreVisual();
            this.m_handoff.Finished();
        });

        this.m_seq = t_seq;
        t_seq.Play();
    }

    /// <summary>결과판이 "한 번 더"로 걷혔다 — 무대를 되돌리지 않고 대기만 푼다.
    /// 걷힌 패널·가라앉은 딤·결과 자세가 그대로 남으므로 **곧바로 <see cref="PlayLead"/>로 이어야 한다**.
    /// 이을 수 없게 됐다면 <see cref="CancelImmediate"/>로 무대를 되돌릴 것.
    ///
    /// ⚠ 이어받을 수 있는 것은 <b>같은 연출</b>뿐이다 — 걷힌 자세는 이 인스턴스의 것이라,
    ///   다음 한 방을 다른 연출이 맡는다면 호출부가 <see cref="PlayReturn"/>으로 무대를 되돌려야 한다.</summary>
    public void EndAwaitForChain()
    {
        if (this.m_cancelling) return;

        // 앞 구간만 도는 중이면 이어받을 결과 자세가 아예 없다 — 걷힌 무대를 남기지 않고 잘라낸다.
        // (m_stageRetracted를 세우지 않는다: 물려줄 자세가 없는데 세우면 다음 판이 원복 없이 출발한다.)
        if (this.m_phase == EPhase.Lead || this.m_phase == EPhase.Waiting)
        {
            CancelImmediate();
            return;
        }

        if (this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);

        if (this.m_phase != EPhase.Await)
        {
            // 기다린 적이 없다 = 이어받을 무대도 없다(PlayReturn과 같은 결).
            this.m_handoff.FlushAll();
            return;
        }

        this.m_phase          = EPhase.Idle;
        this.m_stageRetracted = true;   // 신호보다 먼저 — 호출부가 Finished 안에서 곧바로 PlayLead를 되받아 부른다.

        this.m_handoff.Finished();
    }

    /// <summary>연출을 잘라내고 화면만 원복한다(카드 전환·닫힘 경로).
    /// 어느 단계에서 잘렸든 남은 콜백을 전부 흘린다 — 안 그러면 호출부의 값 갱신 유예가 영영 풀리지 않는다.
    ///
    /// 앞 구간만 돌다 잘린 경우엔 아직 무장된 신호가 없어 <see cref="EnhanceRitualHandoff.FlushAll"/>이 무해하게 지나가고,
    /// 뒤늦게 도착한 <see cref="Commit"/>이 false를 받아 호출부가 무대 없이 마무리한다.</summary>
    public void CancelImmediate()
    {
        if (this.m_cancelling) return;
        this.m_cancelling = true;

        Sequence t_seq = this.m_seq;
        this.m_seq = null;
        t_seq?.Kill();   // 결말이 돌던 중이면 그 OnKill이 공개·정착 콜백을 흘린다

        // 상태는 Kill **뒤에** 못 박는다 — 결말의 OnKill이 자기 단계(Await)를 다시 세우고 지나가기 때문이다.
        this.m_seq       = null;
        this.m_phase     = EPhase.Idle;
        this.m_committed = false;

        RestoreVisual();

        // 결과를 남긴 채 기다리다 잘린 경우엔 아직 안 나간 신호가 있다. 이미 나간 것은 무해하게 지나간다.
        this.m_handoff.FlushAll();

        // 접힘의 되감기 도중에 잘렸다면 그 마무리를 이어받을 곳이 없다 — 접힘 신호는 여기서 흘린다.
        // (앞 구간이 도는 중에 잘린 경우는 여기서 흘리지 않는다: 그 길의 마무리는 뒤늦은 Commit이 false를 받아
        //  호출부가 직접 짓거나, 뒤따라오는 AbortLead가 신호를 마저 흘린다.)
        if (this.m_unwindingLead)
        {
            this.m_unwindingLead = false;
            FlushLeadAborted();
        }

        this.m_cancelling = false;
    }

    protected virtual void OnDisable()
    {
        // 잘린 채 굳은 자세·빛·딤이 다음 열기로 새지 않게.
        CancelImmediate();
    }

    /// <summary>무대의 authoring 자리를 1회만 잡아 둔다(중간값을 기준으로 잡으면 반복할수록 밀린다).</summary>
    protected abstract void CaptureBase();

    /// <summary>연출 재질을 카드에 얹는다. 연출 동안에만 얹는다 — 평상시까지 물려 두면
    /// 카드가 기본 UI 셰이더가 아니라 연출 셰이더로 그려진다.</summary>
    protected abstract void AttachLayers();

    /// <summary>결과를 모르는 앞 구간을 짜고 <b>카드가 빛에 완전히 덮이는 시각</b>(절단면)을 돌려준다.
    ///
    /// ⚠ 이 안에 성패를 읽는 것이 하나라도 있으면 안 된다 — 이 토막이 도는 동안 답은 아직 서버에 있다.
    ///   구간을 넘어가는 트윈도 두지 말 것(대기가 끼면 그 트윈만 홀로 잘린다).</summary>
    protected abstract float BuildLead(Sequence _seq, bool _chained);

    /// <summary>덮인 자세에서 출발하는 결말을 짜고 <b>복귀가 시작될 시각</b>을 돌려준다.
    /// 값 반영(<c>m_handoff.Reveal</c>) 시점을 이 안에서 못 박는 것도 파생 몫이다.
    ///
    /// _at은 이 토막의 원점이다(앞 구간과 다른 시퀀스라 언제나 0이지만, 시각 계산을 이 값 기준으로 두면
    /// 두 토막을 다시 한 줄로 합칠 때 손댈 곳이 없다).</summary>
    protected abstract float BuildFinale(Sequence _seq, EEnhanceOutcome _outcome, float _at);

    /// <summary>답을 기다리는 동안 도는 제자리 숨. null이면 얼어붙은 채 기다린다.
    ///
    /// ⚠ 만질 수 있는 축은 <b>결말이 첫 몇 프레임에 절대값으로 다시 잡는 것</b>뿐이다.
    ///   그러지 않은 축을 밀면 "어느 프레임에 답이 왔느냐"가 결말의 출발 자세를 바꾼다.
    ///   좌표(anchoredPosition)와 덮개(Cover)는 어느 파생에서도 만지지 않는다.</summary>
    protected abstract Sequence BuildWaitLoop();

    /// <summary>결과 자세에서 평상으로 되돌리는 구간. _end는 길이를 못 박을 자리다
    /// (모든 트윈이 미배선이면 시퀀스가 거기 닿기 전에 끝나 버린다).</summary>
    protected abstract void BuildReturn(Sequence _seq, float _at, float _dur, float _end);

    /// <summary>축을 전부 평상으로 되돌리고 재질을 벗는다. 캡처 전이면 건드릴 것도 없다.</summary>
    protected abstract void OnRestoreVisual();

    // 답을 기다리는 자리. 몸짓이 없어도(파생이 null을 주어도) 단계는 선다 — Commit이 그것을 보고 결말로 넘어간다.
    void StartWait()
    {
        this.m_phase = EPhase.Waiting;

        Sequence t_loop = BuildWaitLoop();
        if (t_loop == null) return;

        this.m_seq = t_loop;
        t_loop.Play();
    }

    // 덮인 자세에서 출발하는 결말. 앞 구간과 다른 시퀀스인 이유는 대기가 그 사이에 끼기 때문이고,
    // 그래서 이 토막은 언제나 0에서 시작한다.
    void StartFinale()
    {
        bool t_await = this.m_awaitReturn;

        if (!HasStage)
        {
            // 보여줄 것이 없어도 값 반영까지 막지는 않는다. 결과판을 기다리는 경우엔 그 닫힘(PlayReturn)이 마무리를 이어받는다.
            this.m_phase = t_await ? EPhase.Await : EPhase.Idle;

            this.m_handoff.Reveal();
            this.m_handoff.Settled();
            if (!t_await) this.m_handoff.Finished();
            return;
        }

        // 기다리는 사이 어떤 경로로든 벗겨졌을 수 있다 — 결말도 이 재질 위에서 돈다.
        AttachLayers();

        float t_back = Mathf.Max(0.05f, ReturnDuration);

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);
        float    t_at  = BuildFinale(t_seq, this.m_outcome, 0f);

        // 결과를 남기고 멈추는 경우엔 이 시각이 곧 시퀀스의 끝이므로 신호를 OnKill로 미룬다 —
        // 시퀀스가 죽은 **뒤**에 흘려야 호출부가 곧바로 PlayReturn을 되받아 불러도 재진입이 없다.
        // (여기 콜백은 시퀀스 길이를 못 박는 역할만 한다.)
        if (t_await)
        {
            t_seq.InsertCallback(t_at, () => { });
        }
        else
        {
            t_seq.InsertCallback(t_at, this.m_handoff.Settled);
            BuildReturn(t_seq, t_at, t_back, t_at + t_back);
        }

        // 정상 종료든 스킵이든 중단이든 여기로 온다 — 콜백 유실과 굳은 화면을 동시에 막는 안전망이다.
        // ⚠ 세 신호가 정확히 한 번씩 흐르는 것을 지탱하는 유일한 못이다. 손대지 말 것.
        t_seq.OnKill(() =>
        {
            // 신호보다 상태가 먼저다 — 호출부가 Settled 안에서 PlayReturn을 부를 수 있고,
            // 그때 이미 "기다리는 중"이어야 복귀가 정상 경로를 탄다.
            this.m_seq   = null;
            this.m_phase = t_await ? EPhase.Await : EPhase.Idle;

            this.m_handoff.Reveal();
            this.m_handoff.Settled();

            if (t_await) return;

            RestoreVisual();
            this.m_handoff.Finished();
        });

        this.m_phase = EPhase.Finale;
        this.m_seq   = t_seq;
        t_seq.Play();
    }

    // 접힘 신호는 흘린 즉시 비운다 — 되감기와 중단이 같은 판을 두고 겹쳐도 두 번 나가지 않는다.
    void FlushLeadAborted()
    {
        Action t_cb = this.m_onLeadAborted;
        this.m_onLeadAborted = null;
        t_cb?.Invoke();
    }

    // 무대가 제자리로 돌아오는 모든 길이 여기를 지난다 — 이어받을 자세도 여기서 무효가 된다.
    void RestoreVisual()
    {
        this.m_stageRetracted = false;
        OnRestoreVisual();
    }
}
