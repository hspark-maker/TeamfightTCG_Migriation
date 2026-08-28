/// <summary>이번 전투가 <b>왜</b> 끝났는가. 결과·보상·연출의 갈림은 전부 이 값 하나에서 파생한다
/// (<see cref="TurnRunner"/>의 결과 게이트가 유일한 소비자).
///
/// <para><b>핵심 규칙: 부전승은 없다.</b> 전투 중 상대가 나가면 로컬 AI가 보드를 인수해 판을 끝까지 두고
/// 그 결과로 정산한다(<see cref="TurnRunner"/>의 인수 경로). 보드가 서기 전 이탈·응답 상한 초과·상태 불일치는
/// 전부 무보상 무효 경기다 — 타임아웃은 상대가 아니라 내 쪽 문제일 수 있고(스테일 러너·러너 미기동·배선 누락),
/// 그걸 승리로 주면 양쪽이 동시에 타임아웃 났을 때 둘 다 보상을 받아 골드·랭크가 부풀어 오른다.
/// 미러 불일치도 같다 — 이미 갈라진 판을 누구의 승리로도 매길 수 없다.</para></summary>
public enum EMatchEndReason
{
    /// <summary>한쪽 필드가 비어 정상적으로 갈린 승패. 여운(BattleResultBeat)을 타는 유일한 사유.</summary>
    Normal,

    /// <summary>항복 = 즉시 패배. 보상·랭크는 정상 패배와 같은 경로를 탄다. 강조할 결정타가 없어 여운은 없다.</summary>
    Surrender,

    /// <summary>보드가 서기 전(덱 교환·시드 합의) 상대가 연결을 끊었다. <b>무효 경기다.</b>
    /// 전투 중 이탈은 여기로 오지 않는다 — 그쪽은 부전승도 무효도 아니고 로컬 AI가 인수해 판을 이어간다
    /// (<see cref="TurnRunner"/>의 인수 경로). 초기화 중에는 인수할 보드 자체가 없어 판을 성립시킬 수 없다.</summary>
    OpponentLeftDuringInit,

    /// <summary>응답 상한 초과(<see cref="NetTimeouts"/>). 무효 경기 — 위 규칙 참조.</summary>
    Timeout,

    /// <summary>원격 미러와 로컬 상태가 어긋났다. 무효 경기.
    /// 공격·스폰·초기 덱 미러 불일치와 손상 패킷 수신 시 MatchAbort로 양쪽에 전파한다.</summary>
    Desync,

    /// <summary>초기화가 예외로 죽었다. 타임아웃과 구분해 둔다 — 배선 누락 NRE가 "네트워크 느림"으로
    /// 보고되면 원인 추적이 막힌다. 결과 처리는 Timeout과 동일(무효).</summary>
    InitError,

    /// <summary>양쪽 보드가 동시에 비었다. 무효 경기가 아니다 — 골드는 양쪽 다 받고 랭크만 그대로 둔다.
    /// 승패가 안 갈렸으므로 랭크를 움직일 근거가 없고, 판은 끝까지 진행됐으므로 무보상도 아니다.</summary>
    Draw,

    /// <summary>에디터 디버그 강제 승리. 보상을 실제로 지급한다(종전 동작 보존).</summary>
    DebugForceWin,
}

/// <summary>종료 사유에서 파생하는 판정들. <b>현재 <see cref="GrantsReward"/>의 거짓 집합과
/// <see cref="IsVoid"/>의 참 집합은 정확히 같다</b> — 무효 경기가 곧 무보상이기 때문이다.
/// 결과 게이트가 IsVoid를 먼저 보고 빠져나가므로 GrantsReward 분기는 실행상 항상 참이다.
/// 두 축을 갈라야 할 때(예: 무효지만 안내 팝업은 띄우기)는 게이트의 분기 순서부터 손봐야 한다 —
/// 술어만 고치면 IsVoid가 먼저 걸려 조용히 무시된다.</summary>
public static class MatchEndReasonExtensions
{
    /// <summary>보상·랭크를 실제로 정산하는가(CaptureResult 호출 여부).</summary>
    public static bool GrantsReward(this EMatchEndReason _reason)
    {
        return _reason == EMatchEndReason.Normal
            || _reason == EMatchEndReason.Surrender
            || _reason == EMatchEndReason.Draw
            || _reason == EMatchEndReason.DebugForceWin;
    }

    /// <summary>결과 팝업 앞에 승패 여운을 한 박자 넣는가. 정리된 보드를 붙잡는 연출이라
    /// 정상 종료에만 의미가 있다 — 이탈·항복·무효는 강조할 결정타가 없다.</summary>
    public static bool PlaysBeat(this EMatchEndReason _reason)
    {
        return _reason == EMatchEndReason.Normal;
    }

    /// <summary>무효 경기인가. 결과 팝업도 보상도 없이 로비로 돌려보낸다.</summary>
    public static bool IsVoid(this EMatchEndReason _reason)
    {
        return _reason == EMatchEndReason.Timeout
            || _reason == EMatchEndReason.OpponentLeftDuringInit
            || _reason == EMatchEndReason.Desync
            || _reason == EMatchEndReason.InitError;
    }
}
