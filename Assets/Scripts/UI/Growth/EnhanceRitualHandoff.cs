using System;

// 강화 연출이 호출부에 돌려주는 세 신호. 각각 정확히 한 번, 이 순서로 나간다 —
// 흘린 것을 즉시 비우므로 스킵·중단·재진입 어느 경로가 중복해서 불러도 두 번 나가지 않는다.
//
// ⚠ 하나라도 유실되면 호출부의 값 갱신 유예가 영영 풀리지 않아 화면이 잠긴 채 굳는다.
//   그래서 잘려 나가는 모든 길이 <see cref="FlushAll"/>을 지난다.
public class EnhanceRitualHandoff
{
    Action m_onReveal;                      // 카드가 빛에 완전히 덮인 시점 — 값 반영이 여기서 일어난다
    Action m_onSettled;                     // 카드 위 연출이 끝난 시점 — 결과판이 여기서 뜬다
    Action m_onFinished;                    // 복귀까지 끝난 시점 — 조작이 여기서 되살아난다

    public void Arm(Action _onReveal, Action _onSettled, Action _onFinished)
    {
        this.m_onReveal   = _onReveal;
        this.m_onSettled  = _onSettled;
        this.m_onFinished = _onFinished;
    }

    public void Reveal()
    {
        Action t_cb = this.m_onReveal;
        this.m_onReveal = null;
        t_cb?.Invoke();
    }

    public void Settled()
    {
        Action t_cb = this.m_onSettled;
        this.m_onSettled = null;
        t_cb?.Invoke();
    }

    public void Finished()
    {
        Action t_cb = this.m_onFinished;
        this.m_onFinished = null;
        t_cb?.Invoke();
    }

    /// <summary>남은 신호를 계약 순서대로 전부. 중간 단계에서 잘렸을 때 쓴다 —
    /// 여기서 Finished만 흘리면 값 반영을 못 받은 채 잠금만 풀려 화면과 세이브가 갈린다.</summary>
    public void FlushAll()
    {
        Reveal();
        Settled();
        Finished();
    }
}
