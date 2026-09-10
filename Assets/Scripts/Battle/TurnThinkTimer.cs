using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 매 턴 생각시간과 UI 표시의 단일 소스. 플레이어 입력 감시와 AI 대기 시간을 공유한다.
/// Watch는 싱글 PlayerTurn / 멀티 MultiplayerPlayerTurn의 입력 시간을 감시한다.
/// limit 초과 시 _onTimeout 호출. 발화 후에도 계속 감시하여 Execution 연쇄로 열리는
/// 후속 입력 창도 보호(엣지 리셋으로 창별 fresh 예산). _isDone()/ct 로만 종료.
///
/// - TurnState.InputAllowed 로 게이팅: true 구간만 시간 누적.
/// - InputAllowed false→true 엣지에서 elapsed 리셋 → 턴 시작 + Execution 연속공격
///   재무장 창 모두에 매번 fresh 한 생각시간 예산을 부여.
/// - 배속과 무관하게(공정성) unscaledDeltaTime 누적. 호출측이 raw limit(TurnThinkTime) 전달.
/// - _isDone()==true(=turnDone) 또는 ct 취소 시 자연 종료 → 워처 누수 방지.
/// </summary>
public static class TurnThinkTimer
{
    // Active=플레이어 입력 대기 또는 AI 생각시간 카운트 중.
    // Remaining=남은 초. 표시 로직이 별도로 시간을 재지 않게 하여 이중 소스/드리프트 방지.
    public static bool  Active    { get; private set; }
    public static float Remaining { get; private set; }

    /// <summary>이번 창의 생각시간 총량(초). 링 게이지처럼 비율이 필요한 UI가 자기 상수로 30을 적지 않게 노출.</summary>
    public static float Limit { get; private set; }

    /// <summary>남은 비율 1→0. Limit이 0(워처 미기동)이면 0.</summary>
    public static float Normalized => Limit > 0f ? Mathf.Clamp01(Remaining / Limit) : 0f;

    // 직전 턴 워처가 한 프레임 늦게 종료되어 새 턴의 표시를 끄지 않도록 소유자를 구분한다.
    static int s_generation;

    /// <summary>AI 대기와 남은 시간 표시를 함께 진행한다. 입력 허용 상태와 전투 배속에는 영향 없다.</summary>
    public static async UniTask WaitForEnemy(float _limitSec, CancellationToken _ct)
    {
        _ct.ThrowIfCancellationRequested();
        int t_generation = ++s_generation;
        Limit = _limitSec;
        Remaining = _limitSec;
        Active = true;
        double t_end = Time.realtimeSinceStartupAsDouble + _limitSec;

        try
        {
            while (Remaining > 0f && t_generation == s_generation)
            {
                await UniTask.Yield(_ct);
                if (t_generation != s_generation) return;
                Remaining = Mathf.Max(0f, (float)(t_end - Time.realtimeSinceStartupAsDouble));
            }
        }
        finally
        {
            if (t_generation == s_generation) Active = false;
        }
    }

    public static async UniTaskVoid Watch(float _limitSec, Func<bool> _isDone, Action _onTimeout, CancellationToken _ct)
    {
        int t_generation = ++s_generation;
        Limit = _limitSec;   // UI 비율 계산 기준. 워처가 값의 유일 소유자.

        float t_elapsed     = 0f;
        bool  t_prevAllowed = false;

        try
        {
            while (true)
            {
                if (t_generation != s_generation) return;
                if (_ct.IsCancellationRequested) return;
                if (_isDone()) return;

                bool t_allowed = TurnState.InputAllowed;

                // false→true 엣지: 새 입력 창 시작 → 예산 리셋
                if (t_allowed && !t_prevAllowed) t_elapsed = 0f;
                t_prevAllowed = t_allowed;

                if (t_allowed)
                {
                    t_elapsed += Time.unscaledDeltaTime;
                    if (t_elapsed >= _limitSec)
                    {
                        _onTimeout();
                        // 종료하지 않고 예산만 리셋 → 자동공격이 Execution 연쇄로 새 입력 창을
                        // 열고 그 창에서도 idle이면 다시 타임아웃. 발화 직후 _onTimeout이
                        // InputAllowed=false 로 세팅하므로 재무장(true 엣지) 전까지 재발화 없음.
                        t_elapsed = 0f;
                    }

                    Active    = true;
                    Remaining = Mathf.Max(0f, _limitSec - t_elapsed);
                }
                else
                {
                    // 연출 중(InputAllowed=false)엔 카운트 정지 → UI 숨김
                    Active = false;
                }

                await UniTask.Yield(_ct);
            }
        }
        catch (OperationCanceledException) { /* 씬 파괴/취소 안전 삼킴 */ }
        finally
        {
            if (t_generation == s_generation) Active = false;
        }
    }
}
