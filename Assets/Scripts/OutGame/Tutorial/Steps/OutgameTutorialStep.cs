using System.Collections.Generic;
using UnityEngine;

/// <summary>아웃게임 튜토리얼 시퀀스의 한 칸(SO). 스텝 하나를 에셋으로 저작해 여러 시퀀스·여러 자리에 재사용한다.
/// 스텝은 런타임 상태를 갖지 않는다 — 진행도는 러너가 넘긴 컨텍스트로만 건드리므로 같은 에셋을 여러 칸에 꽂아도 안전하다.</summary>
public abstract class OutgameTutorialStep : ScriptableObject
{
    [Tooltip("게이트 배너 문구. 비우면 배너를 띄우지 않는다")]
    [TextArea][SerializeField] string guideMessage;

    public string GuideMessage => guideMessage;

    /// <summary>안내 타깃 위젯. None이면 딤을 걸 타깃이 없다(자동 스텝·배너 전용 스텝).</summary>
    public virtual EOutgameTutorialAnchor Anchor => EOutgameTutorialAnchor.None;

    /// <summary>무엇이 이 스텝을 완료시키는가.</summary>
    public abstract EOutgameTutorialCompletion Completion { get; }

    /// <summary>완료가 씬 전환을 부른다 → 같은 씬에서 다음 스텝을 이어 걸지 않는다(다음 씬의 브리지가 재개).</summary>
    public virtual bool LeavesScene => false;

    /// <summary>스텝 진입. 반환 true = 이 씬에서 앵커에 게이트를 걸어야 함(false면 자동 처리·씬 전환으로 이 씬의 할 일은 끝).</summary>
    public abstract bool Enter(OutgameTutorialStepContext _context);

    /// <summary>이 스텝이 상점 진열·판매 대상을 덮어쓰면 true — 튜토리얼 중 구매 결과를 저작대로 고정한다.</summary>
    public virtual bool TryGetForcedPack(out CardPackData _pack, out long _refundGold)
    {
        _pack       = null;
        _refundGold = 0;
        return false;
    }

    /// <summary>이 스텝이 덱 자동 편성으로 채울 카드를 지정하면 true — 튜토리얼 중 편성 결과를 저작대로 고정한다.</summary>
    public virtual bool TryGetForcedDeck(out IReadOnlyList<CardData> _cards)
    {
        _cards = null;
        return false;
    }
}
