using Cysharp.Threading.Tasks;
using UnityEngine;

public class AttackAnimTester : MonoBehaviour
{
    [SerializeField] CardView playerView;
    [SerializeField] CardView enemyView;
    [SerializeField] CardData playerCard;
    [SerializeField] CardData enemyCard;

    CardInstance playerInstance;
    CardInstance enemyInstance;

    void Start()
    {
        this.playerInstance = new CardInstance(this.playerCard, 0);
        this.playerInstance.isRevealed = true;
        this.playerInstance.slotIndex  = 0;

        this.enemyInstance = new CardInstance(this.enemyCard, 1);
        this.enemyInstance.isRevealed = true;
        this.enemyInstance.slotIndex  = 0;

        this.playerView.Render(this.playerInstance);
        this.enemyView.Render(this.enemyInstance);

        TurnState.InputAllowed  = true;
        CardView.OnAttack     += HandleAttack;
    }

    void OnDestroy()
    {
        CardView.OnAttack -= HandleAttack;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E) && TurnState.InputAllowed)
            RunEnemyAttack().Forget();
    }

    void HandleAttack(CardView _attacker, CardView _target)
    {
        if (_attacker != this.playerView || _target != this.enemyView) return;
        RunPlayerAttack().Forget();
    }

    async UniTask RunPlayerAttack()
    {
        TurnState.InputAllowed = false;
        await AttackSequence.PlaySingle(this.playerView, this.enemyView,
            this.playerInstance.data.attackEffect);
        TurnState.InputAllowed = true;
    }

    async UniTask RunEnemyAttack()
    {
        TurnState.InputAllowed = false;
        await AttackSequence.PlaySingle(this.enemyView, this.playerView,
            this.enemyInstance.data.attackEffect);
        TurnState.InputAllowed = true;
    }
}
