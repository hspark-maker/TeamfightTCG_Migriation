using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

// ────────────────────────────────────────────────────────────────────────
// ⚠️ 임시 공격 연출 (아트/파티클 도착 전 placeholder). 삭제하기 쉽게 격리함.
//   삭제 방법: 이 파일 통째로 지우고, AttackSequence.cs 의 "TEMP_ATTACK_MOTION"
//   주석 달린 호출 1줄만 제거하면 원상복구.
// ────────────────────────────────────────────────────────────────────────
public static class TempAttackMotion
{
    // 공격자가 방어자 쪽으로 확 돌진해 부딪히고 제자리(시작 위치)로 튕겨 돌아온다.
    // 이미 시네마로 둘만 앞에 나온 상태에서 호출됨. RNG·게임상태 무관(순수 연출).
    public static async UniTask Lunge(CardView _attacker, CardView _defender)
    {
        if (_attacker == null || _defender == null) return;

        Transform t_atk = _attacker.transform;
        Vector3 t_start = t_atk.position;
        Vector3 t_target = _defender.transform.position;

        // 방어자에 완전히 겹치지 않고 60% 지점까지만 돌진(부딪히는 느낌). Z는 유지.
        Vector3 t_impact = Vector3.Lerp(t_start, t_target, 0.6f);
        t_impact.z = t_start.z;

        const float t_inDur = 0.09f;   // 돌진(빠르게)
        const float t_outDur = 0.14f;  // 복귀(살짝 느리게)

        t_atk.DOKill();
        var t_seq = DOTween.Sequence().SetLink(_attacker.gameObject)
            .Append(t_atk.DOMove(t_impact, t_inDur).SetEase(Ease.InQuad))
            .Append(t_atk.DOMove(t_start, t_outDur).SetEase(Ease.OutQuad));

        await t_seq.ToUniTask(cancellationToken: _attacker.GetCancellationTokenOnDestroy())
                   .SuppressCancellationThrow();
    }
}
