using System.Collections.Generic;

/// <summary>가이드 미션의 현재 미션·막·이동 목적지를 답하는 단일 지점. 트래커·목록·시너지 소개가 같은 답을 쓴다.</summary>
internal static class GuideMissionTrack
{
    internal const string PERIOD = "guide";

    internal const string EVENT_DECK_SAVED = "Guide.DeckSaved6";
    internal const string EVENT_CARETAKER_CARDS_STAR1 = "Guide.CaretakerCardsAtStar1";
    internal const string EVENT_CARETAKER_DECK_STAR2 = "Guide.CaretakerDeckAtStar2";
    internal const string EVENT_CARETAKER_TRACE_DECK = "Guide.CaretakerTraceDeck";
    internal const string EVENT_DECK_CARDS_STAR3 = "Guide.DeckCardsAtStar3";
    internal const string EVENT_DECK_CARDS_STAR2 = "Guide.DeckCardsAtStar2";
    internal const string EVENT_ADVENTURE_NODE_PREFIX = "Guide.AdventureNode";
    internal const string EVENT_ADVENTURE_CHAPTER01 = "Guide.AdventureChapter01";

    const string ADVENTURE_CHAPTER01_NODE_ID = "node_06";

    internal enum ERouteKind { None, DeckEditor, AdventureNode, CollectionEnhance, CardGrowth }

    /// <summary>"이동" 버튼이 여는 화면. 모험은 정점 id 문자열로 들고 화면이 index 로 바꾼다.</summary>
    internal readonly struct GuideRoute
    {
        internal ERouteKind Kind { get; }
        internal string NodeId { get; }

        internal GuideRoute(ERouteKind _kind, string _nodeId = null)
        {
            Kind = _kind;
            NodeId = _nodeId ?? string.Empty;
        }
    }

    /// <summary>표시용 막. 시트에는 없고 클라 상수다 — sortOrder 구간으로 나눈다.</summary>
    internal readonly struct GuideAct
    {
        internal int Number { get; }
        internal string Name { get; }
        internal int FirstOrder { get; }
        internal int LastOrder { get; }

        internal GuideAct(int _number, string _name, int _firstOrder, int _lastOrder)
        {
            Number = _number;
            Name = _name;
            FirstOrder = _firstOrder;
            LastOrder = _lastOrder;
        }

        internal bool Contains(MissionDefinition _definition)
            => _definition != null && _definition.SortOrder >= FirstOrder && _definition.SortOrder <= LastOrder;

        internal string Label => $"{Number}막 · {Name}";
    }

    static readonly GuideAct[] s_acts =
    {
        new GuideAct(1, "출발", 1, 2),
        new GuideAct(2, "돌보미 결성", 3, 5),
        new GuideAct(3, "두 가지 힘", 6, 7),
        new GuideAct(4, "에이스", 8, 13),
    };

    internal static IReadOnlyList<GuideAct> Acts => s_acts;

    internal static bool IsGuide(MissionDefinition _definition)
        => _definition != null && _definition.Period == PERIOD;

    /// <summary>미수령 가이드 중 순서가 가장 빠른 것. 전부 받았으면 null.</summary>
    internal static MissionDefinition Current
    {
        get
        {
            IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
            for (int i = 0; i < t_definitions.Count; i++)
                if (IsGuide(t_definitions[i]) && !MissionManager.IsClaimed(t_definitions[i].Id)) return t_definitions[i];
            return null;
        }
    }

    /// <summary>주어진 미션 바로 다음 순서의 가이드. 마지막이면 null.</summary>
    internal static MissionDefinition NextOf(MissionDefinition _definition)
    {
        if (!IsGuide(_definition)) return null;
        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        MissionDefinition t_next = null;
        for (int i = 0; i < t_definitions.Count; i++)
        {
            MissionDefinition t_candidate = t_definitions[i];
            if (!IsGuide(t_candidate) || t_candidate.SortOrder <= _definition.SortOrder) continue;
            if (t_next == null || t_candidate.SortOrder < t_next.SortOrder) t_next = t_candidate;
        }
        return t_next;
    }

    internal static MissionDefinition FindByEvent(string _event)
    {
        if (string.IsNullOrEmpty(_event)) return null;
        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        for (int i = 0; i < t_definitions.Count; i++)
            if (IsGuide(t_definitions[i]) && t_definitions[i].Event == _event) return t_definitions[i];
        return null;
    }

    internal static bool TryGetAct(MissionDefinition _definition, out GuideAct _act)
    {
        for (int i = 0; i < s_acts.Length; i++)
        {
            if (!s_acts[i].Contains(_definition)) continue;
            _act = s_acts[i];
            return true;
        }
        _act = default;
        return false;
    }

    /// <summary>막의 마지막 미션인지. 수령 연출 제목을 "N막 완료"로 바꾸는 판정.</summary>
    internal static bool IsActFinale(MissionDefinition _definition)
        => TryGetAct(_definition, out GuideAct t_act) && t_act.LastOrder == _definition.SortOrder;

    /// <summary>막 안의 (수령 수, 전체 수).</summary>
    internal static (int claimed, int total) CountOf(GuideAct _act)
    {
        int t_claimed = 0, t_total = 0;
        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        for (int i = 0; i < t_definitions.Count; i++)
        {
            if (!IsGuide(t_definitions[i]) || !_act.Contains(t_definitions[i])) continue;
            t_total++;
            if (MissionManager.IsClaimed(t_definitions[i].Id)) t_claimed++;
        }
        return (t_claimed, t_total);
    }

    internal static GuideRoute RouteOf(MissionDefinition _definition)
    {
        string t_event = _definition?.Event ?? string.Empty;
        switch (t_event)
        {
            case EVENT_DECK_SAVED:
            case EVENT_CARETAKER_DECK_STAR2:
            case EVENT_CARETAKER_TRACE_DECK:
                return new GuideRoute(ERouteKind.DeckEditor);
            case EVENT_CARETAKER_CARDS_STAR1:
                return new GuideRoute(ERouteKind.CollectionEnhance);
            case EVENT_DECK_CARDS_STAR3:
            case EVENT_DECK_CARDS_STAR2:
                return new GuideRoute(ERouteKind.CardGrowth);
            case EVENT_ADVENTURE_CHAPTER01:
                return new GuideRoute(ERouteKind.AdventureNode, ADVENTURE_CHAPTER01_NODE_ID);
        }

        // 서버 판정과 같은 규칙이다: Guide.AdventureNodeNN ↔ node_NN.
        if (t_event.StartsWith(EVENT_ADVENTURE_NODE_PREFIX) && t_event.Length > EVENT_ADVENTURE_NODE_PREFIX.Length)
            return new GuideRoute(ERouteKind.AdventureNode, "node_" + t_event.Substring(EVENT_ADVENTURE_NODE_PREFIX.Length));

        return new GuideRoute(ERouteKind.None);
    }

    /// <summary>성장 미션이 가리킬 카드. 선택 덱이 서버 판정 조건(6장·전부 소유)을 못 채우면 0.</summary>
    internal static int PickGrowthCard(MissionDefinition _definition)
    {
        if (_definition == null || !CardGrowthManager.IsReady) return 0;
        int t_slot = DeckSaveManager.SelectedSlot;
        if (!DeckSaveManager.IsSlotValid(t_slot)) return 0;
        List<int> t_cards = DeckSaveManager.GetSlot(t_slot);
        if (t_cards == null) return 0;

        int t_targetStar = _definition.Event == EVENT_DECK_CARDS_STAR3 ? 3
            : _definition.Event == EVENT_DECK_CARDS_STAR2 ? 2 : 0;
        if (t_targetStar == 0) return 0;

        // 3성 목표는 이미 2성인 카드부터(문서: "앞서 키운 2성 카드 중 선택"), 2성 목표는 아직 못 미친 첫 장.
        int t_pick = 0, t_pickStar = -1;
        for (int i = 0; i < t_cards.Count; i++)
        {
            int t_star = StarOf(t_cards[i]);
            if (t_star >= t_targetStar) continue;
            if (t_targetStar == 2) return t_cards[i];
            if (t_star > t_pickStar)
            {
                t_pick = t_cards[i];
                t_pickStar = t_star;
            }
        }
        return t_pick;
    }

    /// <summary>서버 가이드 판정과 같은 별 축(레벨 - 기본 레벨).</summary>
    internal static int StarOf(int _cardId) => GrowthStar.FromLevel(CardGrowthManager.LevelOf(_cardId));
}
