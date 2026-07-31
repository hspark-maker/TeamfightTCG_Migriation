using System.Collections.Generic;
using UnityEngine;

/// <summary>덱 자동 편성 버튼 클릭 대기. 편성 결과를 지정 카드로 덮어써 튜토리얼 덱을 저작대로 고정한다.</summary>
[CreateAssetMenu(fileName = "Step_DeckAutoEquip", menuName = "Card Battle/Outgame Tutorial/Step/Deck Auto Equip")]
public class DeckAutoEquipStep : OutgameTutorialStep
{
    [Tooltip("클릭을 기다릴 자동 편성 버튼")]
    [SerializeField] EOutgameTutorialAnchor anchor;

    [Tooltip("이 스텝에서 자동 편성이 채울 카드팩. 팩 풀 앞에서부터 덱을 메운다. 비우면 일반 규칙(소유 카드)으로 채운다")]
    [SerializeField] CardPackData pack;

    public override EOutgameTutorialAnchor Anchor => anchor;
    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Click;

    public override bool Enter(OutgameTutorialStepContext _context) => true;

    // 앞의 6장만 쓰이는 셈이다 — 빈 칸이 떨어지면 채우기가 스스로 멈춘다(DeckEditController.AutoEquip).
    // 풀을 잘라 넘기지 않는 이유: 덱 크기를 여기서 한 번 더 정의하면 진실원이 둘이 된다.
    public override bool TryGetForcedDeck(out IReadOnlyList<CardData> _cards)
    {
        // 빈 팩은 "지정 없음"과 같다 — 그대로 넘기면 지정이 있는 셈 치고 일반 규칙이 밀린다.
        _cards = pack != null && pack.PoolCount > 0 ? pack.Pool : null;
        return _cards != null;
    }
}
