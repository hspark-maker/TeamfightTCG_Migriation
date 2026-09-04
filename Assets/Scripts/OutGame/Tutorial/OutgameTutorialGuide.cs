using System;
using Cysharp.Threading.Tasks;

// "지금 안내가 시키고 있는 일"을 묻는 단일 창구.
// 두 러너(온보딩·트리거)를 합쳐서 보는 이유: 같은 안내가 챕터에서 트리거로 옮겨 다니는데,
// 묻는 쪽(성장 비용·도감 화면·결과 화면)은 그것이 어느 러너의 것인지 알 필요가 없다.
// 둘은 겹칠 수 있다 — 트리거 문이 졸업 낙인 **전에**(첫 랭크 승급 연출 종료) 열리는데
// 온보딩은 그 뒤로도 카드 강화 편이 남아 있기 때문이다.
// 겹칠 땐 트리거가 답이다: 그쪽은 로비 안에서 시작해 로비 안에서 끝나므로 지금 화면을 쥔 것이 언제나 트리거다.
// 순서를 뒤집으면 온보딩 스텝이 트리거 스텝을 가려 freeOfCharge·anchorCard가 조용히 무시된다.
public static class OutgameTutorialGuide
{
    // 안내가 대준 무료 한 방을 이미 쓴 스텝(세이브하지 않는다 — 재시작하면 다시 한 방).
    // 플래그가 아니라 스텝 참조인 이유: 무료를 저작하는 스텝이 여럿이라(카드 강화·키워드 강화)
    // 하나로 묶으면 앞 스텝이 쓴 한 방 때문에 뒤 스텝의 저작이 조용히 무시된다.
    static TutorialStepDef s_freeSpentStep;

    /// <summary>서버 소진 표식이 바뀌었다. 안내가 서 있는 스텝을 다시 판정시키는 자리다(브리지가 구독한다).</summary>
    // 중계인 이유: 구독자가 클라우드 창구를 직접 참조하지 않게 이 창구 하나로 묶는다.
    public static event Action OnFreeShotSpentChanged
    {
        add    => TutorialGrantsCloud.OnChanged += value;
        remove => TutorialGrantsCloud.OnChanged -= value;
    }

    public static bool IsCurrentAction(EOutgameTutorialAction _action)
        => TriggeredTutorialRunner.IsCurrentAction(_action)
        || OutgameTutorialRunner.IsCurrentAction(_action);

    /// <summary>지금 안내가 지목한 카드. 도감처럼 같은 종류의 자리가 여럿인 화면이 "어느 칸인가"를 여기서 받는다.
    /// 저작이 비었으면 false — 그때는 화면이 스스로 고른다(안내가 멈추지 않게).</summary>
    public static bool TryGetAnchorCard(out int _cardId)
    {
        _cardId = TryGetCurrentStep(out var t_step) ? t_step.AnchorCardId : 0;

        return _cardId > 0;
    }

    /// <summary>지금 이 한 방을 안내가 대신 내주는가 = 저작이 무료라고 말한 스텝에 서 있고, 그 스텝이 아직 안 썼다.
    /// 무엇이 무료인지는 코드가 아니라 스텝의 freeOfCharge가 정한다.
    /// _axis를 받는 이유: 안내가 시킨 것이 카드 강화인데 유저가 키워드 강화를 하면 그쪽이 공짜가 되고
    /// 소진 표식까지 가져가 정작 안내가 시킨 강화에 값이 붙는다(그 반대도 같다).</summary>
    // 클라 표식(세션 내·스텝 단위)과 서버 표식(영구·축 단위)을 둘 다 본다 — 응답을 잃으면 서버만 소진을 알기 때문이다.
    public static bool HasFreeShot(EOutgameTutorialAction _axis)
        => !IsFreeShotSpentOnServer(_axis)
        && TryGetCurrentStep(out var t_step)
        && t_step.Action == _axis
        && t_step.FreeOfCharge
        && t_step != s_freeSpentStep;

    /// <summary>이 축의 무료 한 방을 서버가 이미 소진했는가. 응답을 잃어 안내만 남은 자리를 여기서 가른다.</summary>
    // 묻는 쪽이 클라우드 창구를 직접 참조하지 않게 축 매핑을 여기 가둔다.
    public static bool IsFreeShotSpentOnServer(EOutgameTutorialAction _axis)
    {
        switch (_axis)
        {
            case EOutgameTutorialAction.WaitEnhance:        return TutorialGrantsCloud.EnhanceCardSpent;
            case EOutgameTutorialAction.WaitKeywordEnhance: return TutorialGrantsCloud.EnhanceKeywordSpent;
            default:                                        return false;
        }
    }

    /// <summary>서버 소진 표식을 다시 읽는다. 무료를 청구했는데 서버가 막은 자리처럼
    /// "서버는 이미 소진으로 안다"가 드러난 순간에만 부른다 — 왕복이 그때만 늘어난다.
    /// 표식이 실제로 바뀌면 <see cref="OnFreeShotSpentChanged"/>가 뒤따른다.</summary>
    public static async UniTask RefreshFreeShotSpentAsync() => await TutorialGrantsCloud.RefreshAsync();

    /// <summary>무료 한 방을 지금 스텝에서 소진한다. **성공한 자리에서만** 부른다 —
    /// 실패로 닫아 버리면 안내가 시키는 성장을 유저 돈으로 다시 해야 한다.</summary>
    public static void ConsumeFreeShot()
    {
        if (TryGetCurrentStep(out var t_step)) s_freeSpentStep = t_step;
    }

    // 성장을 처음부터 다시 보는 상태라 안내가 대주던 한 방도 되살린다(디버그 전용)
    public static void ResetFreeShotForDebug() => s_freeSpentStep = null;

    /// <summary>되감기가 서버의 소진 표식(grants 문서)을 지운 직후. 클라 세션 표식과 서버 채택분을 함께 걷는다.</summary>
    // 서버만 지우고 클라 캐시를 두면 부팅 읽기가 물어 둔 옛 값이 남아, 브리지의 통과 판정이
    // "이미 소진했다"고 보고 강화 스텝을 그냥 넘긴다 — 그 챕터를 다시 볼 수 없게 된다.
    public static void ResetFreeShotForRewind()
    {
        s_freeSpentStep = null;
        TutorialGrantsCloud.ResetForRewind();
    }

    /// <summary>지금 서 있는 스텝. 둘 다 돌고 있으면 트리거 쪽이다(클래스 주석 참고).
    /// 무료 한 방의 소진 표식처럼 "그 스텝 하나"를 식별해야 하는 쪽도 이 참조를 그대로 쓴다.</summary>
    public static bool TryGetCurrentStep(out TutorialStepDef _step)
        => TriggeredTutorialRunner.TryGetCurrentStep(out _step)
        || OutgameTutorialRunner.TryGetCurrentStep(out _step);
}
