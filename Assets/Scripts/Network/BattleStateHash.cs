using System.Text;

/// <summary>양 클라이언트의 전투 상태가 같은지 한 값으로 비교하기 위한 보드 지문.
///
/// <para><b>이건 카나리아지 판정기가 아니다.</b> 불일치를 발견하면 로그만 남긴다 —
/// 이 해시로 매치를 끊지 않는다. 오탐 하나가 정상 경기를 죽이는 쪽이 divergence를 늦게 아는 것보다 나쁘다.</para>
///
/// <para><b>정렬 규칙</b>: 두 필드를 <see cref="BattleField.OwnerIndex"/> 오름차순으로 접는다.
/// 클라마다 playerField/enemyField가 서로 반대라 로컬 순서로 접으면 정상 상태에서도 무조건 갈린다.</para>
///
/// <para><b>해시 알고리즘</b>: FNV-1a 64비트 직접 구현. <c>object.GetHashCode()</c>는
/// 런타임·실행마다 값이 달라 절대 쓰면 안 된다. bool은 0/1, enum은 명시 int 캐스트.</para>
///
/// <para>마지막에 <see cref="MatchRandom.DrawCount"/>를 접는다 — RNG 소비 횟수 어긋남이
/// desync의 1차 원인이고, 보드 값이 아직 같아도 이 값은 먼저 갈린다.</para></summary>
public static class BattleStateHash
{
    const ulong FNV_OFFSET = 14695981039346656037UL;
    const ulong FNV_PRIME  = 1099511628211UL;

    /// <summary>두 필드 + RNG 소비 횟수의 지문. 0은 "지문 없음" 센티널로 쓰이므로
    /// 계산 결과가 0이면 1로 밀어 올린다(정상 상태를 '비교 생략'으로 오인하지 않게).</summary>
    public static ulong Compute(BattleField _a, BattleField _b)
    {
        OrderByOwner(_a, _b, out BattleField t_first, out BattleField t_second);

        ulong t_hash = FNV_OFFSET;
        FoldField(ref t_hash, t_first);
        FoldField(ref t_hash, t_second);
        FoldInt(ref t_hash, MatchRandom.DrawCount);
        return t_hash == 0UL ? 1UL : t_hash;
    }

    /// <summary>불일치 로그용 사람이 읽는 덤프. 해시가 갈린 뒤 "무엇이" 다른지 양쪽 로그를 눈으로 대조한다.</summary>
    public static string Dump(BattleField _a, BattleField _b)
    {
        OrderByOwner(_a, _b, out BattleField t_first, out BattleField t_second);

        var t_builder = new StringBuilder(512);
        t_builder.Append("draws=").Append(MatchRandom.DrawCount);
        DumpField(t_builder, t_first);
        DumpField(t_builder, t_second);
        return t_builder.ToString();
    }

    /// <summary>접는 순서를 소유자 인덱스로 고정한다. 두 필드의 OwnerIndex가 같거나 한쪽이 null이면
    /// 인자 순서를 그대로 쓴다 — 그 상황 자체가 이미 비정상이고, 해시가 갈리는 것으로 드러나야 한다.</summary>
    static void OrderByOwner(BattleField _a, BattleField _b, out BattleField _first, out BattleField _second)
    {
        bool t_swap = _a != null && _b != null && _b.OwnerIndex < _a.OwnerIndex;
        _first  = t_swap ? _b : _a;
        _second = t_swap ? _a : _b;
    }

    static void FoldField(ref ulong _hash, BattleField _field)
    {
        FoldInt(ref _hash, _field?.OwnerIndex ?? -1);
        FoldInt(ref _hash, _field?.FlowStack ?? -1);
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
            FoldCard(ref _hash, _field?.GetSlot(i));

        FoldInt(ref _hash, _field?.WaitingCount ?? -1);
        if (_field == null) return;
        foreach (CardInstance t_card in _field.GetWaitingCards())
            FoldCard(ref _hash, t_card);
    }

    static void FoldCard(ref ulong _hash, CardInstance _card)
    {
        if (_card == null)
        {
            FoldInt(ref _hash, -1);   // 빈 슬롯 센티널 — 빈칸 개수 차이도 잡힌다
            return;
        }

        FoldInt(ref _hash, _card.cardId);
        FoldInt(ref _hash, _card.slotIndex);
        FoldInt(ref _hash, _card.ownerIndex);
        FoldInt(ref _hash, _card.hp);
        FoldInt(ref _hash, _card.maxHp);
        FoldInt(ref _hash, _card.bonusHp);
        FoldInt(ref _hash, _card.evolutionStage);
        FoldInt(ref _hash, _card.attackCount);
        FoldInt(ref _hash, _card.synergyDmgReduction);
        FoldInt(ref _hash, _card.flowBonus);
        FoldInt(ref _hash, _card.legacyStack);
        FoldInt(ref _hash, (int)_card.unlockedKeywords);
        FoldInt(ref _hash, (int)_card.runtimeKeywords);
        FoldInt(ref _hash, (int)_card.synergyKeywords);
        FoldInt(ref _hash, _card.synergyEnabled     ? 1 : 0);
        FoldInt(ref _hash, _card.hasShield          ? 1 : 0);
        FoldInt(ref _hash, _card.reviveUsed         ? 1 : 0);
        FoldInt(ref _hash, _card.justSpawned        ? 1 : 0);
        FoldInt(ref _hash, _card.returnedFromField  ? 1 : 0);
        FoldInt(ref _hash, _card.isRevealed         ? 1 : 0);
        FoldInt(ref _hash, _card.wasEverRevealed    ? 1 : 0);
    }

    static void FoldInt(ref ulong _hash, int _value)
    {
        unchecked
        {
            uint t_value = (uint)_value;
            for (int i = 0; i < 4; i++)
            {
                _hash ^= (byte)(t_value >> (i * 8));
                _hash *= FNV_PRIME;
            }
        }
    }

    static void DumpField(StringBuilder _builder, BattleField _field)
    {
        _builder.Append("\n  owner=").Append(_field?.OwnerIndex ?? -1)
                .Append(" flow=").Append(_field?.FlowStack ?? -1)
                .Append(" waiting=").Append(_field?.WaitingCount ?? -1);
        if (_field == null) return;

        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
            DumpCard(_builder, "slot" + i, _field.GetSlot(i));
        int t_index = 0;
        foreach (CardInstance t_card in _field.GetWaitingCards())
            DumpCard(_builder, "wait" + t_index++, t_card);
    }

    static void DumpCard(StringBuilder _builder, string _label, CardInstance _card)
    {
        _builder.Append("\n    ").Append(_label).Append('=');
        if (_card == null) { _builder.Append("(빈칸)"); return; }

        _builder.Append("id").Append(_card.cardId)
                .Append(" hp").Append(_card.hp).Append('+').Append(_card.bonusHp)
                .Append('/').Append(_card.maxHp)
                .Append(" atkCnt").Append(_card.attackCount)
                .Append(" kw").Append((int)_card.runtimeKeywords)
                .Append('/').Append((int)_card.synergyKeywords)
                .Append(" dr").Append(_card.synergyDmgReduction)
                .Append(" flow").Append(_card.flowBonus)
                .Append(" legacy").Append(_card.legacyStack)
                .Append(_card.hasShield ? " shield" : string.Empty)
                .Append(_card.reviveUsed ? " revived" : string.Empty)
                .Append(_card.justSpawned ? " justSpawned" : string.Empty);
    }
}
