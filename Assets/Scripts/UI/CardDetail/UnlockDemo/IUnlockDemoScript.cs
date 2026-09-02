using System;
using System.Threading;
using Cysharp.Threading.Tasks;

// 해금 안내가 보여주는 연출 한 갈래. "무엇을 보여줄지"는 대본이 쥐고, "어디에 세울지"와
// "언제 걷을지"는 무대(UnlockDemoStage)가 쥔다.
//
// ⚠ 규칙을 돌리지 않는다. 빈 BattleEvent 목록은 피해 0으로 읽혀 모션과 파티클만 재생한다.
//   시너지 대본은 여기에 한 줄을 더한다: SynergyEffect 파생 클래스와 SynergyTriggers 디스패처를
//   부르지 않고, CardInstance의 **상태 변경 메서드**(Heal · GrantShield · ClearShield)도 부르지 않는다 —
//   그것들은 BattleEventStream.Emit을 타므로, 로비에서 부르면 전투 이벤트 스트림에 로비발 사건이 흐른다.
//   보여줄 숫자가 필요하면 CardView의 표시 전용 API(OverrideHpDisplay · DeferHpDisplay ·
//   PlayHealEffect · SetShieldVisible)로만 낸다 — 그 창구가 DemoHpDisplay다.
//
//   딱 하나 예외가 비늘 대본이다(DemoHpDisplay.ShowReducedHit): 감쇄 수치를 데모 카드에 얹어 두고
//   감쇄 식 자체는 CardInstance에 맡긴다 — ApplySynergy는 필드 가산이라 Emit을 타지 않고,
//   이 카드는 매 바퀴 새로 세워진다.
//
// 숫자로 보이는 축은 배틀과 같이 **체력과 추가 생명력 둘뿐이다**(CardView에 공격력 텍스트가 없다).
// 그래서 공격력·스택 축의 시너지(흐름)는 배틀에서도 숫자가 안 나오므로 여기서도 내지 않는다.
// 수치의 정본은 스펙시트(SynergyEffectDef.parameters)이고, 대본이 쓰는 것은 그 값을 베낀 상수다 —
// 어느 칸과 짝인지와 그 대가는 UnlockDemoNumbers의 주석에 모아 두었다.
public interface IUnlockDemoScript
{
    /// <summary>이 대본이 무대에 요구하는 배역. 세울 수 없으면 false —
    /// 무대는 그대로 접히고 부른 쪽은 띠를 끄고 글자만 보여준다.
    ///
    /// ⚠ 판정 전용이라 무대를 만지지 않는다. 여기서 카드를 세우면 실패한 대본의 CardInstance까지
    /// BattleBoardView 정적 레지스트리에 등록돼, 전투에 들어갔을 때 CardView.FadeAll이 그것들도 흐리게 만든다.</summary>
    bool TryBuildCast(int _card, KeywordDemoConfig _config, out UnlockDemoCast _cast);

    /// <summary>배역이 선 무대 위에서 한 판을 재생한다.
    ///
    /// ⚠ 취소로 예외를 던지지 않는다. 기다림은 <c>IUnlockDemoStage.Hold</c>나
    /// <c>SuppressCancellationThrow()</c>로만 한다 — 예외가 이 문을 뚫으면 무대의 재생 루프가 조용히
    /// 끝나 "판이 도는 중" 표시가 true로 굳고, 걷으라는 지시를 받은 무대가 영원히 부서지지 않는다.
    ///
    /// ⚠ 배역은 첫 줄에서 지역변수로 한 번만 잡는다. <c>Ally</c>·<c>Neighbor</c>는 부를 때마다 다시
    /// 조회되고, <c>Swing</c>의 _afterHit 콜백은 AttackSequence가 취소를 안 받는 탓에 **취소된 뒤에도
    /// 반드시 실행된다** — 콜백 안에서 무대를 다시 물으면 이미 파괴된 CardView를 만질 수 있다.</summary>
    UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token);
}

/// <summary>대본이 만질 수 있는 무대의 전부. 무대의 수명·텍스처·전역 빌리기는 이 문 밖에 있다.
///
/// 진영은 자리가 정한다 — **윗줄은 적, 아랫줄은 아군이다**. 이 화면에서 편을 가르는 단서는 줄(y)뿐이라,
/// 아군을 윗줄에 세우면 힐러가 적을 살리는 그림이 된다.</summary>
public interface IUnlockDemoStage
{
    /// <summary>앞자리. 방금 열린 그 카드가 선다 — 대개 공격자이고, 도발·비늘·수호자에서만 맞는 쪽이 된다.</summary>
    CardView Attacker { get; }

    /// <summary>맞은편. 언제나 적이고, 도발에서만 치러 오는 쪽이 된다.</summary>
    CardView Defender { get; }

    /// <summary>윗줄 곁자리(적). 배역이 없으면 null.</summary>
    CardView Neighbor { get; }

    /// <summary>아랫줄 곁자리(아군). 배역이 없으면 null.</summary>
    CardView Ally { get; }

    /// <summary>공격 한 번. 체력은 바꾸지 않는다(빈 이벤트 목록을 넘기는 것이 이 무대의 규약이다).
    ///
    /// ⚠ _afterHit은 취소돼도 반드시 실행된다 — AttackSequence가 취소 토큰을 받지 않기 때문이다.
    /// 그래서 콜백은 이미 잡아 둔 지역변수만 캡처해야 한다.</summary>
    UniTask Swing(CardView _atk, CardView _def, CardView _splash,
                  CancellationToken _token, Func<UniTask> _afterHit = null);

    /// <summary>_seconds만큼 쉰다. 취소되면 즉시 돌아오되 예외를 던지지 않는다.</summary>
    UniTask Hold(float _seconds, CancellationToken _token);
}
