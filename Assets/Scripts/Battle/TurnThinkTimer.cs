using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 매 턴 생각시간 감시 공통 로직(싱글 PlayerTurn / 멀티 MultiplayerPlayerTurn 공유).
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
    // UI 표시용 단일 소스(워처가 유일 소유). Active=생각시간 카운트 중(내 턴, InputAllowed).
    // Remaining=남은 초. 표시 로직이 별도로 시간을 재지 않게 하여 이중 소스/드리프트 방지.
    public static bool  Active    { get; private set; }
    public static float Remaining { get; private set; }

    public static async UniTaskVoid Watch(float _limitSec, Func<bool> _isDone, Action _onTimeout, CancellationToken _ct)
    {
        float t_elapsed     = 0f;
        bool  t_prevAllowed = false;

        try
        {
            while (true)
            {
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
        finally { Active = false; }   // 턴 종료/취소 시 항상 숨김
    }
}
