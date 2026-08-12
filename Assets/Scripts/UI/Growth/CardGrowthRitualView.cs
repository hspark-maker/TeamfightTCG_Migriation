using System;
using DG.Tweening;
using UnityEngine;

/// <summary>성장 한 번을 보여주는 연출의 공통 뼈대(강화 = 담금질, 진화 = 탈각).
///
/// 여기가 지는 것은 호출부와의 약속 하나뿐이다 — 세 콜백이 스킵·중단·재진입 어느 경로로든
/// 각각 정확히 한 번, 이 순서로 간다는 것. 무엇을 보여줄지는 파생이 정한다.
///
/// 이 계약을 파생마다 다시 구현하지 않는 이유: 스킵·"한 번 더"·카드 전환이 겹치는 경로가
/// 이 클래스에서 가장 미묘한 부분이라, 복제하면 두 연출의 규약이 조용히 갈린다.</summary>
public abstract class CardGrowthRitualView : MonoBehaviour
{
    protected readonly EnhanceRitualHandoff m_handoff = new EnhanceRitualHandoff();   // 세 신호의 순서·1회 보장

    Sequence m_seq;
    bool     m_awaitingReturn;   // 결과를 남긴 채 복귀 신호를 기다리는 중. 이 동안에도 재진입은 막혀야 한다
    bool     m_cancelling;       // 잘라내는 중. 콜백이 호출부를 타고 PlayReturn으로 되돌아오는 것을 막는다
    bool     m_stageRetracted;   // 무대를 걷은 채 다음 연출을 기다리는 중("한 번 더" 경로)

    /// <summary>연출이 진행 중인가(결과를 남긴 채 기다리는 동안도 포함).
    /// 호출부는 이 동안 재입력·카드 넘기기·닫기를 막는다.</summary>
    public bool IsPlaying => this.m_awaitingReturn || (this.m_seq != null && this.m_seq.IsActive());

    /// <summary>연출을 걸 무대가 배선돼 있는가. 없으면 몸짓 없이 콜백만 흐른다 —
    /// 배선 실패가 소프트락이 되면 안 된다.</summary>
    protected abstract bool HasStage { get; }

    /// <summary>복귀 구간의 길이.</summary>
    protected abstract float ReturnDuration { get; }

    /// <summary>성장 결과를 한 번 보여준다. _outcome은 Success/Failed만 온다(나머지는 결제 전 차단이라 보여줄 것이 없다).
    ///
    /// _onReveal은 값을 화면에 반영할 시점 — 어느 프레임이 그 자리인지는 파생이 정한다(담금질은 백열 아래,
    /// 탈각은 옛 껍질 아래). _onSettled는 카드 위 연출이 다 끝난 시점 — 호출부가 여기서 결과판을 띄운다.
    ///
    /// _awaitReturn이면 결과를 무대에 남긴 채 멈추고 <see cref="PlayReturn"/>을 기다린다.
    /// 아니면 스스로 걷고 _onFinished까지 이어간다.</summary>
    public void Play(EEnhanceOutcome _outcome, bool _awaitReturn, Action _onReveal, Action _onSettled, Action _onFinished)
    {
        // 재진입은 호출부가 막지만 여기서도 닫는다 — 두 연출이 같은 노드를 두고 싸우면 카드가 굳는다.
        // 다만 콜백은 삼키지 않는다. 삼키면 호출부의 갱신 유예가 영영 풀리지 않아 버튼이 죽는다.
        if (IsPlaying)
        {
            _onReveal?.Invoke();
            _onSettled?.Invoke();
            _onFinished?.Invoke();
            return;
        }

        // 걷힌 무대를 그대로 물려받는가("한 번 더"). 플래그는 여기서 소비한다 — 이어받는 것은 이 한 번뿐이다.
        bool t_chained        = this.m_stageRetracted;
        this.m_stageRetracted = false;

        this.m_handoff.Arm(_onReveal, _onSettled, _onFinished);

        if (!HasStage)
        {
            // 보여줄 것이 없어도 값 반영까지 막지는 않는다. 결과판을 기다리는 경우엔 그 닫힘(PlayReturn)이 마무리를 이어받는다.
            this.m_awaitingReturn = _awaitReturn;
            this.m_handoff.Reveal();
            this.m_handoff.Settled();
            if (!_awaitReturn) this.m_handoff.Finished();
            return;
        }

        CaptureBase();

        // 이어받는 경우엔 즉시 원복하지 않는다 — 앞 결과가 남긴 표면을 진입 구간이 데려온다.
        if (!t_chained) RestoreVisual();

        // 연출 재질은 여기서부터 걸친다(RestoreVisual이 벗기므로 순서가 뒤바뀌면 안 된다).
        AttachLayers();

        Sequence t_seq   = DOTween.Sequence().SetLink(gameObject).SetId(this);
        float    t_at    = BuildStage(t_seq, _outcome, t_chained);
        float    t_back  = Mathf.Max(0.05f, ReturnDuration);

        // 결과를 남기고 멈추는 경우엔 이 시각이 곧 시퀀스의 끝이므로 신호를 OnKill로 미룬다 —
        // 시퀀스가 죽은 **뒤**에 흘려야 호출부가 곧바로 PlayReturn을 되받아 불러도 재진입이 없다.
        // (여기 콜백은 시퀀스 길이를 못 박는 역할만 한다.)
        if (_awaitReturn)
        {
            t_seq.InsertCallback(t_at, () => { });
        }
        else
        {
            t_seq.InsertCallback(t_at, this.m_handoff.Settled);
            BuildReturn(t_seq, t_at, t_back, t_at + t_back);
        }

        // 정상 종료든 스킵이든 중단이든 여기로 온다 — 콜백 유실과 굳은 화면을 동시에 막는 안전망이다.
        t_seq.OnKill(() =>
        {
            // 신호보다 상태가 먼저다 — 호출부가 Settled 안에서 PlayReturn을 부를 수 있고,
            // 그때 이미 "기다리는 중"이어야 복귀가 정상 경로를 탄다.
            this.m_seq            = null;
            this.m_awaitingReturn = _awaitReturn;

            this.m_handoff.Reveal();
            this.m_handoff.Settled();

            if (_awaitReturn) return;

            RestoreVisual();
            this.m_handoff.Finished();
        });

        this.m_seq = t_seq;
        t_seq.Play();   // 재생 책임을 코드에 남긴다(PopupTransition과 같은 결).
    }

    /// <summary>남은 구간을 최종 상태로 끌어당긴다. 콜백은 순서대로 그대로 실행된다.
    /// 결과를 남기고 기다리는 동안은 끌어당길 것이 없다 — 그때의 입력은 결과판이 받는다.</summary>
    public void RequestSkip()
    {
        if (this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);
    }

    /// <summary>결과를 걷고 무대를 원래대로 되돌린다(결과판이 닫힐 때 호출부가 부른다).
    /// 기다리는 중이 아니면 남은 콜백만 흘린다 — 어느 경로로 와도 조작이 죽은 채 굳지 않게.</summary>
    public void PlayReturn()
    {
        // 잘라내는 중에 콜백을 타고 되돌아온 것이다 — 남은 콜백은 CancelImmediate가 마저 흘린다.
        if (this.m_cancelling) return;

        // 결과판이 무대보다 먼저 닫힐 수 있다(스킵 경로). 남은 구간을 끌어당겨야 복귀가 결과 자세에서 출발한다.
        if (this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);

        if (!this.m_awaitingReturn)
        {
            // 기다린 적이 없는데 불렸다 = 어딘가에서 순서가 어긋났다.
            this.m_handoff.FlushAll();
            return;
        }

        this.m_awaitingReturn = false;

        // 기다리는 사이 어떤 경로로든 벗겨졌을 수 있다 — 복귀 구간도 이 재질 위에서 돈다.
        AttachLayers();

        if (!HasStage)
        {
            RestoreVisual();
            this.m_handoff.Finished();
            return;
        }

        float t_dur = Mathf.Max(0.05f, ReturnDuration);

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);
        BuildReturn(t_seq, 0f, t_dur, t_dur);

        t_seq.OnKill(() =>
        {
            this.m_seq = null;
            RestoreVisual();
            this.m_handoff.Finished();
        });

        this.m_seq = t_seq;
        t_seq.Play();
    }

    /// <summary>결과판이 "한 번 더"로 걷혔다 — 무대를 되돌리지 않고 대기만 푼다.
    /// 걷힌 패널·가라앉은 딤·결과 자세가 그대로 남으므로 **곧바로 <see cref="Play"/>로 이어야 한다**.
    /// 이을 수 없게 됐다면 <see cref="CancelImmediate"/>로 무대를 되돌릴 것.
    ///
    /// ⚠ 이어받을 수 있는 것은 <b>같은 연출</b>뿐이다 — 걷힌 자세는 이 인스턴스의 것이라,
    ///   다음 한 방을 다른 연출이 맡는다면 호출부가 <see cref="PlayReturn"/>으로 무대를 되돌려야 한다.</summary>
    public void EndAwaitForChain()
    {
        if (this.m_cancelling) return;

        if (this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);

        if (!this.m_awaitingReturn)
        {
            // 기다린 적이 없다 = 이어받을 무대도 없다(PlayReturn과 같은 결).
            this.m_handoff.FlushAll();
            return;
        }

        this.m_awaitingReturn = false;
        this.m_stageRetracted = true;   // 신호보다 먼저 — 호출부가 Finished 안에서 곧바로 Play를 되받아 부른다.

        this.m_handoff.Finished();
    }

    /// <summary>연출을 잘라내고 화면만 원복한다(카드 전환·닫힘 경로).
    /// 어느 단계에서 잘렸든 남은 콜백을 전부 흘린다 — 안 그러면 호출부의 값 갱신 유예가 영영 풀리지 않는다.</summary>
    public void CancelImmediate()
    {
        if (this.m_cancelling) return;
        this.m_cancelling = true;

        this.m_seq?.Kill();   // OnKill이 공개·정착 콜백을 흘린다.
        this.m_seq            = null;
        this.m_awaitingReturn = false;

        RestoreVisual();

        // 결과를 남긴 채 기다리다 잘린 경우엔 아직 안 나간 신호가 있다. 이미 나간 것은 무해하게 지나간다.
        this.m_handoff.FlushAll();

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

    /// <summary>무대 위 연출을 짜고 <b>복귀가 시작될 시각</b>을 돌려준다.
    /// 값 반영(<c>m_handoff.Reveal</c>) 시점을 이 안에서 못 박는 것도 파생 몫이다.</summary>
    protected abstract float BuildStage(Sequence _seq, EEnhanceOutcome _outcome, bool _chained);

    /// <summary>결과 자세에서 평상으로 되돌리는 구간. _end는 길이를 못 박을 자리다
    /// (모든 트윈이 미배선이면 시퀀스가 거기 닿기 전에 끝나 버린다).</summary>
    protected abstract void BuildReturn(Sequence _seq, float _at, float _dur, float _end);

    /// <summary>축을 전부 평상으로 되돌리고 재질을 벗는다. 캡처 전이면 건드릴 것도 없다.</summary>
    protected abstract void OnRestoreVisual();

    // 무대가 제자리로 돌아오는 모든 길이 여기를 지난다 — 이어받을 자세도 여기서 무효가 된다.
    void RestoreVisual()
    {
        this.m_stageRetracted = false;
        OnRestoreVisual();
    }
}
