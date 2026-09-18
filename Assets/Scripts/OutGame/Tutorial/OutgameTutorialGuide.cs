using System;
using Cysharp.Threading.Tasks;

// "지금 안내가 시키고 있는 일"을 묻는 단일 창구.
// 강제 커서와 자율 커서를 합쳐서 보는 이유: 같은 안내가 강제 챕터에서 자율 챕터로 옮겨 다니는데,
// 묻는 쪽(성장 비용·도감 화면·결과 화면)은 그것이 어느 커서의 것인지 알 필요가 없다.
// 자율 안내는 졸업 뒤에만 열리므로 두 커서가 동시에 서는 일은 없다 — 자율을 먼저 묻는 것은 순서 규약일 뿐이다.
public static class OutgameTutorialGuide
{
    // 안내가 대준 무료 한 방을 이미 쓴 스텝(세이브하지 않는다 — 재시작하면 다시 한 방).
    // 플래그가 아니라 스텝 참조인 이유: 무료를 저작하는 스텝이 여럿이라(카드 강화·키워드 강화)
    // 하나로 묶으면 앞 스텝이 쓴 한 방 때문에 뒤 스텝의 저작이 조용히 무시된다.
    static TutorialStepDef s_freeSpentStep;
    static int s_enhanceCard;
    static bool s_enhanceFree;
    static bool s_growthAlreadyReached;

    public static int TargetCardId => s_enhanceCard;
    public static int TargetLevel => GuideResume.Record?.TargetLevel > 0 && GuideResume.Record.CardId == s_enhanceCard
        ? GuideResume.Record.TargetLevel : s_enhanceCard > 0 ? CardGrowthManager.LevelOf(s_enhanceCard) + 1 : 0;
    static EOutgameTutorialTrigger GrowthTrigger => GuideResume.HasPending
        ? GuideResume.Trigger : OutgameTutorialRunner.GuidedTrigger;
    public static bool IsGrowthGoalReached => GuideResume.Record?.GoalReached == true
        || (GrowthTrigger == EOutgameTutorialTrigger.SynergyGrowthIntroduction
            ? s_growthAlreadyReached || (s_enhanceCard > 0 && GuideMissionTrack.StarOf(s_enhanceCard) >= 2)
            : GrowthTrigger == EOutgameTutorialTrigger.CollectionTabFirstEnter
                && (TutorialGrantsCloud.EnhanceCardSpent || (s_enhanceCard > 0
                    && GuideResume.Record?.TargetLevel > 0
                    && CardGrowthManager.LevelOf(s_enhanceCard) >= GuideResume.Record.TargetLevel)));

    public static bool IsEnhanceIntroduction => OutgameTutorialRunner.GuidedTrigger == EOutgameTutorialTrigger.CollectionTabFirstEnter
        || OutgameTutorialRunner.GuidedTrigger == EOutgameTutorialTrigger.SynergyGrowthIntroduction;

    public static bool NeedsMoreSynergyGrowth
        => OutgameTutorialRunner.GuidedTrigger == EOutgameTutorialTrigger.SynergyGrowthIntroduction
            && !IsGrowthGoalReached && s_enhanceCard > 0 && GuideMissionTrack.StarOf(s_enhanceCard) < 2;

    /// <summary>시너지 도입의 지정 카드만 2성까지 무료로 성장한다.</summary>
    public static bool CanUseFreeSynergyGrowth(int _cardId)
    {
        if (GrowthTrigger != EOutgameTutorialTrigger.SynergyGrowthIntroduction
            || _cardId <= 0 || _cardId != s_enhanceCard || IsGrowthGoalReached) return false;
        if (!OutgameTutorialRunner.TryGetGuidedChapter(EOutgameTutorialTrigger.SynergyGrowthIntroduction,
            out _, out var t_chapter)) return false;
        for (int t_i = 0; t_i < t_chapter.StepCount; t_i++)
            if (t_chapter.TryGetStep(t_i, out var t_step) && t_step.Action == EOutgameTutorialAction.WaitEnhance)
                return t_step.FreeOfCharge;
        return false;
    }

    public static bool HasFreeCardEnhance(int _cardId)
        => HasFreeShot(EOutgameTutorialAction.WaitEnhance)
            && (GrowthTrigger != EOutgameTutorialTrigger.SynergyGrowthIntroduction || CanUseFreeSynergyGrowth(_cardId));

    /// <summary>서버 소진 표식이 바뀌었다. 안내가 서 있는 스텝을 다시 판정시키는 자리다(브리지가 구독한다).</summary>
    // 중계인 이유: 구독자가 클라우드 창구를 직접 참조하지 않게 이 창구 하나로 묶는다.
    public static event Action OnFreeShotSpentChanged
    {
        add    => TutorialGrantsCloud.OnChanged += value;
        remove => TutorialGrantsCloud.OnChanged -= value;
    }

    public static bool IsCurrentAction(EOutgameTutorialAction _action)
        => OutgameTutorialRunner.IsGuidedAction(_action)
        || OutgameTutorialRunner.IsCurrentAction(_action);

    /// <summary>지금 안내가 지목한 카드. 도감처럼 같은 종류의 자리가 여럿인 화면이 "어느 칸인가"를 여기서 받는다.
    /// 저작이 비었으면 false — 그때는 화면이 스스로 고른다(안내가 멈추지 않게).</summary>
    public static bool TryGetAnchorCard(out int _cardId)
    {
        _cardId = IsEnhanceIntroduction
            ? s_enhanceCard : TryGetCurrentStep(out var t_step) ? t_step.AnchorCardId : 0;

        return _cardId > 0;
    }

    /// <summary>강화 안내의 대상 카드를 세션 시작 시 고정한다.</summary>
    public static bool PrepareEnhanceCard(OutgameTutorialChapter _chapter)
    {
        s_enhanceCard = 0;
        s_enhanceFree = false;
        for (int t_i = 0; t_i < _chapter.StepCount; t_i++)
            if (_chapter.TryGetStep(t_i, out var t_step) && t_step.Action == EOutgameTutorialAction.WaitEnhance)
                s_enhanceFree = t_step.FreeOfCharge && t_step != s_freeSpentStep
                    && !IsFreeShotSpentOnServer(t_step.Action);
        if (GuideResume.IsFor(_chapter.Trigger) && RestoreResumeCard()) return true;
        if (_chapter.Trigger == EOutgameTutorialTrigger.CollectionTabFirstEnter && TutorialGrantsCloud.EnhanceCardSpent)
        {
            GuideResume.MarkGoalReached();
            foreach (int t_card in CardCatalog.AllIds)
                if (CanExplainEnhance(t_card)
                    && (s_enhanceCard == 0 || t_card < s_enhanceCard)) s_enhanceCard = t_card;
            return true;
        }
        string t_event = GuideMissionTrack.Current?.Event;
        bool t_starters = t_event == GuideMissionTrack.EVENT_STARTER_CARDS_STAR1
            || t_event == GuideMissionTrack.EVENT_STARTER_CARDS_STAR2
            || t_event == GuideMissionTrack.EVENT_CARETAKER_CARDS_STAR1;
        if (t_starters)
            foreach (int t_card in GuideMissionPreparation.CardIds)
                if (GuideMissionTrack.StarOf(t_card) < (t_event == GuideMissionTrack.EVENT_STARTER_CARDS_STAR2 ? 2 : 1)
                    && CanGuideEnhance(t_card)) { s_enhanceCard = t_card; return true; }
        int t_growth = GuideMissionTrack.PickGrowthCard(GuideMissionTrack.Current);
        if (CanGuideEnhance(t_growth)) { s_enhanceCard = t_growth; return true; }
        foreach (int t_card in CardCatalog.AllIds)
            if (CanGuideEnhance(t_card) && (s_enhanceCard == 0 || t_card < s_enhanceCard)) s_enhanceCard = t_card;
        return s_enhanceCard > 0;
    }

    /// <summary>시너지 도입에서 안내할 보유 카드를 성장도 순으로 고른다.</summary>
    public static void PrepareSynergyGrowth()
    {
        ClearEnhanceCard();
        if (GuideResume.IsFor(EOutgameTutorialTrigger.SynergyGrowthIntroduction) && RestoreResumeCard())
        {
            s_growthAlreadyReached = IsGrowthGoalReached;
            return;
        }
        foreach (int t_card in CardCatalog.AllIds)
        {
            if (!OwnershipManager.IsOwned(t_card)) continue;
            if (GuideMissionTrack.StarOf(t_card) >= 2) s_growthAlreadyReached = true;
            if (!CanShowGrowthCard(t_card) || GuideMissionTrack.StarOf(t_card) >= 2) continue;
            if (s_enhanceCard == 0 || GuideMissionTrack.StarOf(t_card) > GuideMissionTrack.StarOf(s_enhanceCard)
                || (GuideMissionTrack.StarOf(t_card) == GuideMissionTrack.StarOf(s_enhanceCard) && t_card < s_enhanceCard))
                s_enhanceCard = t_card;
        }
        if (s_growthAlreadyReached)
        {
            foreach (int t_card in CardCatalog.AllIds)
                if (OwnershipManager.IsOwned(t_card) && CanShowGrowthCard(t_card) && GuideMissionTrack.StarOf(t_card) >= 2)
                { s_enhanceCard = t_card; break; }
            GuideResume.MarkGoalReached();
        }
    }

    /// <summary>저장 대상을 복원한다. 이미 달성한 작업은 추가 비용을 요구하지 않는다.</summary>
    public static bool RestoreResumeCard()
    {
        var t_record = GuideResume.Record;
        if (t_record == null) return false;
        s_enhanceCard = t_record.CardId;
        if (IsGrowthGoalReached)
        {
            GuideResume.MarkGoalReached();
            if (!OwnershipManager.IsOwned(s_enhanceCard) || !CanShowGrowthCard(s_enhanceCard)
                || (GrowthTrigger == EOutgameTutorialTrigger.CollectionTabFirstEnter && !CanExplainEnhance(s_enhanceCard)))
                s_enhanceCard = 0;
            if (s_enhanceCard == 0)
                foreach (int t_card in CardCatalog.AllIds)
                    if (OwnershipManager.IsOwned(t_card) && CanShowGrowthCard(t_card)
                        && (GrowthTrigger != EOutgameTutorialTrigger.CollectionTabFirstEnter || CanExplainEnhance(t_card)))
                    { s_enhanceCard = t_card; break; }
            s_growthAlreadyReached = GuideResume.Trigger == EOutgameTutorialTrigger.SynergyGrowthIntroduction;
            return true;
        }
        if (s_enhanceCard > 0 && OwnershipManager.IsOwned(s_enhanceCard) && CanShowGrowthCard(s_enhanceCard)) return true;
        s_enhanceCard = 0;
        return false;
    }

    public static bool ShouldSkipGrowthStep(TutorialStepDef _step)
    {
        if (OutgameTutorialRunner.GuidedTrigger != EOutgameTutorialTrigger.SynergyGrowthIntroduction
            || !s_growthAlreadyReached) return false;
        return _step.Action == EOutgameTutorialAction.WaitEnhance
            || _step.Action == EOutgameTutorialAction.WaitUnlockIntro;
    }

    /// <summary>세션 대상 카드가 여전히 강화 가능한가.</summary>
    public static bool CanContinueEnhance() => IsGrowthGoalReached || CanGuideEnhance(s_enhanceCard);

    /// <summary>강화 안내 종료 시 세션 대상을 걷는다.</summary>
    public static void ClearEnhanceCard() { s_enhanceCard = 0; s_enhanceFree = false; s_growthAlreadyReached = false; }

    /// <summary>강화 문구의 비용 설명을 현재 무료 자격으로 해석한다.</summary>
    public static string MessageOf(TutorialStepDef _step)
        => SynergyBattleGuide.MessageOf((_step?.GuideMessage ?? string.Empty).Replace("{enhanceCost}",
            HasFreeShot(EOutgameTutorialAction.WaitEnhance) ? "이번 강화는 무료예요." : "샤드를 사용해 카드를 성장시켜요."));

    /// <summary>지금 이 한 방을 안내가 대신 내주는가 = 저작이 무료라고 말한 스텝에 서 있고, 그 스텝이 아직 안 썼다.
    /// 무엇이 무료인지는 코드가 아니라 스텝의 freeOfCharge가 정한다.
    /// _axis를 받는 이유: 안내가 시킨 것이 카드 강화인데 유저가 키워드 강화를 하면 그쪽이 공짜가 되고
    /// 소진 표식까지 가져가 정작 안내가 시킨 강화에 값이 붙는다(그 반대도 같다).</summary>
    // 클라 표식(세션 내·스텝 단위)과 서버 표식(영구·축 단위)을 둘 다 본다 — 응답을 잃으면 서버만 소진을 알기 때문이다.
    public static bool HasFreeShot(EOutgameTutorialAction _axis)
        => TryGetCurrentStep(out var t_step)
        && t_step.Action == _axis
        && t_step.FreeOfCharge
        && (_axis == EOutgameTutorialAction.WaitEnhance
            && GrowthTrigger == EOutgameTutorialTrigger.SynergyGrowthIntroduction
                ? CanUseFreeSynergyGrowth(s_enhanceCard)
                : !IsFreeShotSpentOnServer(_axis) && t_step != s_freeSpentStep);

    /// <summary>이 축의 무료 한 방을 서버가 이미 소진했는가. 응답을 잃어 안내만 남은 자리를 여기서 가른다.</summary>
    // 묻는 쪽이 클라우드 창구를 직접 참조하지 않게 축 매핑을 여기 가둔다.
    public static bool IsFreeShotSpentOnServer(EOutgameTutorialAction _axis)
    {
        if (_axis == EOutgameTutorialAction.WaitEnhance
            && GrowthTrigger == EOutgameTutorialTrigger.SynergyGrowthIntroduction) return IsGrowthGoalReached;
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
        if (GrowthTrigger == EOutgameTutorialTrigger.SynergyGrowthIntroduction) return;
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
        => OutgameTutorialRunner.TryGetGuidedStep(out _step)
        || OutgameTutorialRunner.TryGetCurrentStep(out _step);
    static bool CanGuideEnhance(int _cardId)
    {
        if (_cardId <= 0 || !CardGrowthManager.IsReady || !OwnershipManager.IsOwned(_cardId)
            || !CardGrowthManager.TryGetNextStep(_cardId, out var t_step)) return false;
        return CanShowGrowthCard(_cardId) && (CanUseFreeSynergyGrowth(_cardId)
            || (s_enhanceFree && !IsFreeShotSpentOnServer(EOutgameTutorialAction.WaitEnhance))
            || CurrencyManager.CanAfford(t_step.Currency, t_step.Cost));
    }

    static bool CanExplainEnhance(int _cardId) => _cardId > 0 && OwnershipManager.IsOwned(_cardId)
        && CanShowGrowthCard(_cardId) && CardVisualRules.InfoKeywords(_cardId) != CardKeyword.None;

    static bool CanShowGrowthCard(int _cardId)
    {
        bool t_inAlbum = false;
        foreach (var t_theme in CardAlbum.Themes)
        {
            if (t_theme.IsLocked) continue;
            foreach (int t_card in t_theme.CardIds)
                if (t_card == _cardId) { t_inAlbum = true; break; }
            if (t_inAlbum) break;
        }
        return t_inAlbum;
    }

}
