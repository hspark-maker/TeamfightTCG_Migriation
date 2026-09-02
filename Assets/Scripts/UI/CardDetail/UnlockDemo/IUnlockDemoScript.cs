using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>해금 안내가 보여주는 연출 한 갈래. 무엇을 보여줄지는 대본이, 어디에 세우고 언제 걷을지는 무대가 쥔다.</summary>
// 규칙은 돌리지 않는다 — SynergyEffect·SynergyTriggers와 CardInstance의 상태 변경 메서드(Heal·GrantShield·ClearShield)를
// 부르면 로비에서 BattleEventStream.Emit이 흐른다. 숫자는 DemoHpDisplay로만 낸다.
public interface IUnlockDemoScript
{
    /// <summary>이 대본이 무대에 요구하는 배역. 세울 수 없으면 false(무대를 접고 글자만 남긴다).</summary>
    // 판정 전용이다 — 여기서 카드를 세우면 실패한 대본까지 BattleBoardView 레지스트리에 남아 전투에서 FadeAll에 걸린다.
    bool TryBuildCast(int _card, KeywordDemoConfig _config, out UnlockDemoCast _cast);

    /// <summary>배역이 선 무대 위에서 한 판을 재생한다.</summary>
    // ⚠ 취소로 예외를 던지지 않는다 — 예외가 이 문을 뚫으면 재생 루프가 죽고 걷힌 무대가 영영 안 부서진다.
    // ⚠ 배역은 첫 줄에서 지역변수로 잡는다 — Swing의 _afterHit은 취소된 뒤에도 실행돼 파괴된 CardView를 만질 수 있다.
    UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token);
}

/// <summary>대본이 만질 수 있는 무대의 전부. 진영은 자리가 정한다 — 윗줄은 적, 아랫줄은 아군.</summary>
public interface IUnlockDemoStage
{
    /// <summary>앞자리. 방금 열린 그 카드 — 도발·비늘·수호자에서만 맞는 쪽이 된다.</summary>
    CardView Attacker { get; }

    /// <summary>맞은편. 언제나 적이고, 도발에서만 치러 오는 쪽이 된다.</summary>
    CardView Defender { get; }

    /// <summary>윗줄 곁자리(적). 배역이 없으면 null.</summary>
    CardView Neighbor { get; }

    /// <summary>아랫줄 곁자리(아군). 배역이 없으면 null.</summary>
    CardView Ally { get; }

    /// <summary>공격 한 번. 빈 이벤트 목록과 _forceSpecial: false가 규약이라 체력은 안 바뀌고 시네마는 꺼진다.</summary>
    // _afterHit은 취소돼도 반드시 실행된다 — AttackSequence가 취소 토큰을 받지 않는다.
    UniTask Swing(CardView _atk, CardView _def, CardView _splash,
                  CancellationToken _token, Func<UniTask> _afterHit = null);

    /// <summary>_seconds만큼 쉰다. 취소되면 즉시 돌아오되 예외를 던지지 않는다.</summary>
    UniTask Hold(float _seconds, CancellationToken _token);
}
