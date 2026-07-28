using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 공격 "연출만" 확인용 테스터(3v3, BattleScene 구성 재사용). 실제 데미지 없음(_onEffect 미전달)
/// → HP 불변·사망 없음 → 무한 반복. 두 BattleFieldView의 슬롯에 카드를 직접 렌더해 채운다.
/// 조작: [P] 플레이어 슬롯0 → 적 슬롯0 / [E] 적 슬롯0 → 플레이어 슬롯0 / 카드 탭·드래그 제스처로 임의 대상.
/// 인스펙터에서 연출 종류(일반/특별)와 박치기 각 단계 초/거리/각을 Play 중 실시간 조정.
/// </summary>
public class AttackAnimTester : MonoBehaviour
{
    [Header("Field Views (3v3)")]
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;

    [Header("Cards (슬롯 0~2)")]
    [SerializeField] CardData[] playerCards = new CardData[3];
    [SerializeField] CardData[] enemyCards  = new CardData[3];

    [Header("연출 종류")]
    [Tooltip("체크=특별(시네마 1vs1), 해제=일반(박치기).")]
    [SerializeField] bool useSpecialCinema = false;

    [Header("Test Damage (체력 깎임/데미지 숫자 확인용)")]
    [SerializeField] int sampleDamage        = 10;   // 방어자에게.
    [SerializeField] int sampleCounterDamage = 5;    // 공격자에게(반격). 0이면 반격 없음.

    [Header("박치기 타이밍(초) / 거리 / 각도  — Play 중 조정 가능")]
    [SerializeField] float windDur    = 0.07f;
    [SerializeField] float windDist   = 0.22f;
    [SerializeField] float inDur      = 0.09f;
    [SerializeField] float recoilDur  = 0.09f;
    [SerializeField] float recoilDist = 0.35f;
    [SerializeField] float outDur     = 0.16f;
    [Range(0f, 1f)]  [SerializeField] float lungeT  = 0.62f;
    [Range(0f, 80f)] [SerializeField] float maxLean = 40f;

    bool busy;

    void Start()
    {
        TurnState.LocalOwnerIndex = 0;   // 플레이어(owner 0)가 로컬 → 탭 무장 대상.
        TurnState.InputAllowed    = true;

        // 부트스트랩(GameInitializer) off라 애니메이터 캐시 초기화가 안 됨 → 첫 연출/가이드 누락 방지 위해 직접 호출.
        this.playerFieldView.InitializeAnimators();
        this.enemyFieldView.InitializeAnimators();

        RenderField(this.playerFieldView, this.playerCards, 0);
        RenderField(this.enemyFieldView,  this.enemyCards,  1);

        CardView.OnAttack += HandleAttack;
    }

    void OnDestroy() => CardView.OnAttack -= HandleAttack;

    void RenderField(BattleFieldView _fv, CardData[] _cards, int _owner)
    {
        if (_fv == null) return;
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardView t_cv = _fv.GetSlotView(i);
            if (t_cv == null) continue;

            CardData t_data = (_cards != null && i < _cards.Length) ? _cards[i] : null;
            if (t_data == null) { t_cv.Render(null); continue; }

            var t_inst = new CardInstance(t_data, _owner) { isRevealed = true, slotIndex = i };
            t_cv.Render(t_inst);
        }
    }

    void Update()
    {
        if (this.busy) return;
        if (Input.GetKeyDown(KeyCode.P)) TryAttack(this.playerFieldView, 0, this.enemyFieldView, 0);
        if (Input.GetKeyDown(KeyCode.E)) TryAttack(this.enemyFieldView, 0, this.playerFieldView, 0);
    }

    void TryAttack(BattleFieldView _atkFv, int _atkSlot, BattleFieldView _defFv, int _defSlot)
    {
        CardView t_atk = _atkFv?.GetSlotView(_atkSlot);
        CardView t_def = _defFv?.GetSlotView(_defSlot);
        if (t_atk == null || t_def == null || t_atk.BoundCard == null || t_def.BoundCard == null) return;
        RunAttack(t_atk, t_def).Forget();
    }

    void HandleAttack(CardView _attacker, CardView _target)
    {
        if (this.busy) return;
        RunAttack(_attacker, _target).Forget();
    }

    async UniTask RunAttack(CardView _attacker, CardView _defender)
    {
        this.busy = true;
        TurnState.InputAllowed = false;

        PushTuning();
        AttackEffect t_effect = _attacker.BoundCard?.data?.attackEffect;

        // _onEffect = 접촉 시 방어자에 sampleDamage, 공격자에 sampleCounterDamage(반격) 적용
        //             → 양쪽 체력 깎임 + HitEffect(붐+데미지 숫자) 표시.
        await AttackSequence.PlaySingle(_attacker, _defender, t_effect,
            _onEffect: () =>
            {
                ApplyTestDamage(_defender, this.sampleDamage);
                ApplyTestDamage(_attacker, this.sampleCounterDamage);
            },
            _forceSpecial: this.useSpecialCinema);

        // 사망하면 다시 채워 반복 가능하게.
        if (_defender.BoundCard != null && _defender.BoundCard.hp <= 0) Respawn(_defender);
        if (_attacker.BoundCard != null && _attacker.BoundCard.hp <= 0) Respawn(_attacker);

        TurnState.InputAllowed = true;
        this.busy = false;
    }

    void ApplyTestDamage(CardView _def, int _dmg)
    {
        CardInstance t_c = _def.BoundCard;
        if (t_c == null || _dmg <= 0) return;
        int t_d = _dmg;
        int t_fromBonus = Mathf.Min(t_c.bonusHp, t_d);
        t_c.bonusHp -= t_fromBonus; t_d -= t_fromBonus;
        t_c.hp = Mathf.Max(0, t_c.hp - t_d);
    }

    void Respawn(CardView _cv)
    {
        CardInstance t_old = _cv.BoundCard;
        if (t_old == null) return;
        var t_fresh = new CardInstance(t_old.data, t_old.ownerIndex) { isRevealed = true, slotIndex = t_old.slotIndex };
        _cv.Render(t_fresh);
    }

    void PushTuning()
    {
        AttackSequence.Normal = new AttackSequence.NormalTuning
        {
            windDur    = this.windDur,
            windDist   = this.windDist,
            inDur      = this.inDur,
            recoilDur  = this.recoilDur,
            recoilDist = this.recoilDist,
            outDur     = this.outDur,
            lungeT     = this.lungeT,
            maxLean    = this.maxLean,
        };
    }

    void OnGUI()
    {
        GUI.Label(new Rect(12, 10, 760, 24),
            $"[P] 플레이어 공격   [E] 적 공격   |   연출: {(this.useSpecialCinema ? "특별(시네마)" : "일반(박치기)")}   |   카드 탭/드래그도 가능");
        GUI.Label(new Rect(12, 34, 760, 24),
            $"박치기 초: wind {this.windDur:0.00} / in {this.inDur:0.00} / recoil {this.recoilDur:0.00} / out {this.outDur:0.00}");
    }
}
