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

    /// <summary>빈 슬롯 보충 + 등장 연출.
    ///
    /// **상태 변경(FillEmptySlots)은 기존 순서 그대로 둘 다 먼저** 처리한다 — 이 순서는
    /// 스폰 트리거·큐 소비 순서라 바꾸면 안 된다. 바뀐 건 연출 순서뿐이다.
    ///
    /// 연출은 **순차**다. PlayFillAnim이 새 카드를 화면 중앙(_mid)을 거쳐 날리는데,
    /// 예전엔 양쪽을 WhenAll로 동시에 돌려서 동시 사망 시 두 진영 카드가 중앙에서 겹쳐 안 보였다.
    /// 멀티(MultiplayerPlayerTurn/OpponentTurn)는 원래 순차라 이 문제가 없었다 — 싱글만 어긋나 있었다.</summary>
    public async UniTask FillAndAnimate()
    {
        List<CardInstance> t_playerPlaced = this.playerField.FillEmptySlots();
        List<CardInstance> t_enemyPlaced  = this.enemyField.FillEmptySlots();
        RefreshViews();

        await this.playerFieldView.PlayFillAnim(t_playerPlaced);
        await this.enemyFieldView.PlayFillAnim(t_enemyPlaced);
    }

    public void ClearAllHighlights()
    {
        this.playerFieldView.ClearAllHighlights();
        this.enemyFieldView.ClearAllHighlights();
    }
}
