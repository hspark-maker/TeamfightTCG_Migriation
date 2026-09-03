using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>매칭 한 번을 <b>화면보다 오래 살게</b> 돌린다.
///
/// <para>취소를 눌러도 곧바로 멈출 수 없는 왕복이 있다 — Photon 로비 참가·방 생성·서버 프로필 조회는
/// 취소 토큰을 받지 않아 그 왕복이 끝나야 토큰이 보인다. 셸이 그걸 끝까지 기다리면 취소가 몇 초씩 늦는다.
/// 그래서 셸은 기다리기를 그만두고 화면을 접고, 매칭은 여기서 끝까지 돌며 스스로 정리한다
/// (토큰이 이미 취소돼 있으므로 정리 뒤 Canceled 로 끝난다).</para>
///
/// <para>대신 "끝나지 않은 매칭"이 뒤에 남는 구간이 생긴다. 그 사이 다시 대전에 들어가면 세션이 겹치므로
/// 재진입은 <see cref="IsPending"/>·<see cref="WaitAsync"/> 로 막는다.</para></summary>
public static class MatchmakingRun
{
    static UniTaskCompletionSource s_done;

    /// <summary>버려진 매칭이 아직 정리 중인가. 참이면 새 매칭을 열지 말고 <see cref="WaitAsync"/>로 기다린다.</summary>
    public static bool IsPending => s_done != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_done = null;

    /// <summary>매칭을 시작한다. 돌려주는 것은 결과를 받을 창구일 뿐이라,
    /// 호출자가 이 await 를 버려도(AttachExternalCancellation) 매칭 자체는 계속 돈다.</summary>
    public static UniTask<MatchOpponent?> Run(IMatchmaker _matchmaker, CancellationToken _ct)
    {
        var t_result = new UniTaskCompletionSource<MatchOpponent?>();

        // 앞선 매칭이 아직 정리 중이면 그 완료 신호를 덮지 않는다 — 덮으면 기다리던 쪽이 영영 안 깨어난다.
        // 정상 흐름에서는 호출자가 IsPending 으로 막으므로 여기 오지 않는다.
        if (s_done == null) s_done = new UniTaskCompletionSource();

        RunInner(_matchmaker, _ct, t_result).Forget();

        return t_result.Task;
    }

    /// <summary>정리가 끝날 때까지 기다린다. 남은 매칭이 없으면 즉시 돌아온다.</summary>
    public static UniTask WaitAsync() => s_done?.Task ?? UniTask.CompletedTask;

    static async UniTaskVoid RunInner(
        IMatchmaker _matchmaker, CancellationToken _ct, UniTaskCompletionSource<MatchOpponent?> _result)
    {
        UniTaskCompletionSource t_done = s_done;
        try
        {
            _result.TrySetResult(await _matchmaker.FindOpponentAsync(_ct));
        }
        catch (Exception t_exception)
        {
            // 버려진 매칭의 예외를 삼키면 세션이 어떤 상태로 끝났는지 알 수 없다 — 남기고 결과로도 넘긴다.
            Debug.LogWarning($"[MatchmakingRun] 매칭이 예외로 끝났다: {t_exception.GetBaseException().Message}");
            _result.TrySetException(t_exception);
        }
        finally
        {
            // 내가 세운 신호일 때만 걷는다(그 사이 새 매칭이 시작됐으면 그쪽 신호를 건드리지 않는다).
            if (ReferenceEquals(s_done, t_done)) s_done = null;
            t_done?.TrySetResult();
        }
    }
}
