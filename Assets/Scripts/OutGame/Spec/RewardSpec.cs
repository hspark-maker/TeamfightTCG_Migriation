using UnityEngine;

// 스펙시트 보상 한 줄 → 저작 포맷(AlbumRewardDef) 변환. 토너먼트·앨범이 같은 규약을 쓰게 하는 자리다.
public static class RewardSpec
{
    // 재화 표기가 틀리면 그 줄을 버린다 — 조용히 Gold로 떨어지면 시트 오타가 오지급이 된다.
    public static bool TryConvert(string _currency, long _amount, string _where, out AlbumRewardDef _def)
    {
        _def = default;

        if (!CurrencyCode.TryParse(_currency, out ECurrencyType t_type))
        {
            Debug.LogWarning($"[RewardSpec] {_where}: 알 수 없는 재화 '{_currency}' — 이 줄을 버린다.");
            return false;
        }

        if (_amount <= 0) return false;   // 0 이하는 지급도 표시도 되지 않는다(저작 경로와 같은 기준)

        _def.currency = t_type;
        _def.amount = _amount;
        return true;
    }
}
