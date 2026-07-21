using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;

public class TurnContext
{
    public BattleField playerField;
    public BattleField enemyField;
    public BattleFieldView playerFieldView;
    public BattleFieldView enemyFieldView;
    public TMP_Text turnLabel;
    public DeckPileUI playerDeckUI;
    public DeckPileUI enemyDeckUI;
    public TurnBannerUI playerTurnBanner;
    public TurnBannerUI enemyTurnBanner;

    public void RefreshViews()
    {
        this.playerFieldView.Refresh();
        this.enemyFieldView.Refresh();
        this.playerDeckUI?.Refresh();
        this.enemyDeckUI?.Refresh();
    }

    public async UniTask FillAndAnimate()
    {
        List<CardInstance> t_playerPlaced = this.playerField.FillEmptySlots();
        List<CardInstance> t_enemyPlaced  = this.enemyField.FillEmptySlots();
        RefreshViews();
        await UniTask.WhenAll(
            this.playerFieldView.PlayFillAnim(t_playerPlaced),
            this.enemyFieldView.PlayFillAnim(t_enemyPlaced));
    }

    public void ClearAllHighlights()
    {
        this.playerFieldView.ClearAllHighlights();
        this.enemyFieldView.ClearAllHighlights();
    }
}
