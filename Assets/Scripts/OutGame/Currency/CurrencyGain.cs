/// <summary>재화 획득 한 건(종류 + 액수). 지급원에서 연출까지 종류가 끊기지 않게 쌍으로 운반한다.</summary>
public readonly struct CurrencyGain
{
    public static readonly CurrencyGain None = default;

    public readonly ECurrencyType Type;
    public readonly long Amount;

    public bool HasAmount => this.Amount > 0;

    public CurrencyGain(ECurrencyType _type, long _amount)
    {
        Type = _type;
        Amount = _amount;
    }
}

/// <summary>
/// 여러 재화가 섞이는 합산 지점 전용(종류당 1칸 고정). 같은 종류는 더해지고 순회 순서는 항상 enum 순이다.
/// 캐리어가 재사용하는 가변 싱크라 struct가 아니다.
/// </summary>
public class CurrencyGainBucket
{
    readonly long[] m_amounts = new long[(int)ECurrencyType.Count];

    public long this[ECurrencyType _type] => m_amounts[(int)_type];

    public bool IsEmpty
    {
        get
        {
            for (int t_i = 0; t_i < m_amounts.Length; t_i++)
                if (m_amounts[t_i] > 0) return false;

            return true;
        }
    }

    public void Add(ECurrencyType _type, long _amount)
    {
        if (_amount <= 0) return;
        m_amounts[(int)_type] += _amount;
    }

    public void Add(CurrencyGain _gain) => this.Add(_gain.Type, _gain.Amount);

    /// <summary>_other의 내용을 이쪽으로 합친다(캐리어 드레인).</summary>
    public void Add(CurrencyGainBucket _other)
    {
        if (_other == null) return;

        for (int t_i = 0; t_i < m_amounts.Length; t_i++)
            m_amounts[t_i] += _other.m_amounts[t_i];
    }

    public void Clear()
    {
        for (int t_i = 0; t_i < m_amounts.Length; t_i++)
            m_amounts[t_i] = 0L;
    }
}
