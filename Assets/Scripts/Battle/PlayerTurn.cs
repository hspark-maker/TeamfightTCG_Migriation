using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class PlayerTurn : TurnBase
{
    CardInstance forcedAttacker;
    bool turnDone;

    public PlayerTurn(TurnContext _ctx) : base(_ctx) { }

    public override void OnEnter()
    {
        if (this.ctx.turnLabel != null) this.ctx.turnLabel.text = "플레이어 턴";
        TurnState.InputAllowed = true;
        CardView.OnAttack    += HandleCardViewAttack;
    }

    public override async UniTask Execute()
    {
        this.turnDone = false;
        await UniTask.WaitUntil(() => this.turnDone);
    }

    public override void OnExit()
    {
        TurnState.InputAllowed   = false;
        TurnState.ForcedAttacker = null;
        CardView.RestoreAllFades();
        CardView.OnAttack      -= HandleCardViewAttack;
        this.ctx.ClearAllHighlights();
        this.forcedAttacker = null;
    }

    void HandleCardViewAttack(CardView _attacker, CardView _target)
    {
        CardInstance t_attCard = _attacker.BoundCard;
        CardInstance t_defCard = _target.BoundCard;

        if (t_attCard == null || t_attCard.ownerIndex != TurnState.LocalOwnerIndex) return;
        if (t_defCard == null || t_defCard.ownerIndex == TurnState.LocalOwnerIndex) return;
        if (this.forcedAttacker != null && t_attCard != this.forcedAttacker) return;

        ExecuteAttack(t_attCard, t_defCard);
    }

    void ExecuteAttack(CardInstance _attacker, CardInstance _defender)
    {
        ExecuteAttackAsync(_attacker, _defender).Forget();
    }

    async UniTask ExecuteAttackAsync(CardInstance _attacker, CardInstance _defender)
    {
        TurnState.InputAllowed = false;

        CardView t_attackerView = this.ctx.playerFieldView.GetSlotView(_attacker.slotIndex);
        CardView t_defenderView = this.ctx.enemyFieldView.GetSlotView(_defender.slotIndex);

        var (t_preSelectedSplash, t_splashView) = AttackFlow.PreSelectSplash(
            _attacker, _defender, this.ctx.enemyField, this.ctx.enemyFieldView);

        AttackResult t_result = default;
        Action t_onEffect = () => t_result = AttackProcessor.Execute(
            _attacker, _defender, this.ctx.playerField, this.ctx.enemyField, t_preSelectedSplash);

        var (t_preKw, t_atKw) = AttackFlow.Keywords(_attacker);

        await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
            _attacker.data.attackEffect, t_onEffect, t_preKw, t_atKw);

        await AttackFlow.RunAfterAttackPassives(_attacker, _defender, this.ctx.playerField, t_result);

        await this.ctx.FillAndAnimate();

        await AttackFlow.PlayResultFlourish(t_attackerView, _attacker, _defender, t_result);

        if (t_result.canAttackAgain && this.ctx.enemyField.IsEmpty)
        {
            this.forcedAttacker     = null;
            TurnState.ForcedAttacker = null;
            CardView.RestoreAllFades();
            this.turnDone = true;
            return;
        }

        if (t_result.canAttackAgain)
        {
            CardPassive.Notify(_attacker, CardKeyword.Execution);
            this.forcedAttacker      = _attacker;
            TurnState.ForcedAttacker  = _attacker;

            CardView.FadeTeam(0.3f, TurnState.LocalOwnerIndex);
            CardView.FadeCards(1f, t_attackerView);

            TurnState.InputAllowed    = true;
            return;
        }

        this.forcedAttacker     = null;
        TurnState.ForcedAttacker = null;
        CardView.RestoreAllFades();
        this.turnDone           = true;
    }
}
