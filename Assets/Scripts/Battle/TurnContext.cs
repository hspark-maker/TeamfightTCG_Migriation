using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;

public readonly struct TurnFillResult
{
    public readonly List<CardInstance> PlayerPlaced;
    public readonly List<CardInstance> EnemyPlaced;

    public TurnFillResult(List<CardInstance> _playerPlaced, List<CardInstance> _enemyPlaced)
    {
        PlayerPlaced = _playerPlaced;
        EnemyPlaced = _enemyPlaced;
    }
}

/// <summary>턴 규칙이 소비하는 가변 전투 상태.</summary>
public sealed class TurnRuleContext
{
    public BattleField playerField;
    public BattleField enemyField;

    public TurnFillResult FillSlots() => new TurnFillResult(
        playerField.FillEmptySlots(), enemyField.FillEmptySlots());
}

/// <summary>턴 상태를 Unity UI에 표시하는 프레젠테이션 컨텍스트.</summary>
public sealed class TurnViewContext
{
    public BattleFieldView playerFieldView;
    public BattleFieldView enemyFieldView;
    public TMP_Text turnLabel;
    public DeckPileUI playerDeckUI;
    public DeckPileUI enemyDeckUI;
    public TurnBannerUI turnBanner;
    public MulliganOverlayUI mulliganOverlay;

    public void RefreshViews()
    {
        playerFieldView.Refresh();
        enemyFieldView.Refresh();
        playerDeckUI?.Refresh();
        enemyDeckUI?.Refresh();
    }

    public async UniTask AnimateFilled(TurnFillResult _filled)
    {
        RefreshViews();
        await playerFieldView.PlayFillAnim(_filled.PlayerPlaced);
        await enemyFieldView.PlayFillAnim(_filled.EnemyPlaced);
    }

    public void ClearAllHighlights()
    {
        playerFieldView.ClearAllHighlights();
        enemyFieldView.ClearAllHighlights();
    }
}

/// <summary>
/// 규칙과 연출을 함께 수행하는 기존 TurnBase 구현용 호환 facade.
/// 순수 루프는 이 타입 대신 TurnRuleContext만 소비한다.
/// </summary>
public sealed class TurnContext
{
    public readonly TurnRuleContext Rules;
    public readonly TurnViewContext Views;

    public TurnContext(TurnRuleContext _rules, TurnViewContext _views)
    {
        Rules = _rules;
        Views = _views;
    }

    public BattleField playerField => Rules.playerField;
    public BattleField enemyField => Rules.enemyField;
    public BattleFieldView playerFieldView => Views.playerFieldView;
    public BattleFieldView enemyFieldView => Views.enemyFieldView;
    public TMP_Text turnLabel => Views.turnLabel;
    public DeckPileUI playerDeckUI => Views.playerDeckUI;
    public DeckPileUI enemyDeckUI => Views.enemyDeckUI;
    public TurnBannerUI turnBanner => Views.turnBanner;
    public MulliganOverlayUI mulliganOverlay => Views.mulliganOverlay;

    public void RefreshViews() => Views.RefreshViews();
    public void ClearAllHighlights() => Views.ClearAllHighlights();

    public async UniTask FillAndAnimate()
    {
        TurnFillResult t_filled = Rules.FillSlots();
        await Views.AnimateFilled(t_filled);
    }
}
