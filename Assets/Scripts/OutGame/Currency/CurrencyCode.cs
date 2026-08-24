// 스펙시트는 재화를 문자열로 저작한다(Gold/Diamond/Energy/Shard). 그 표기를 읽는 유일한 창구다.
// 실패했을 때 무엇으로 떨어질지는 호출부가 정한다 — 팩 가격은 Gold로 떨어지고, 보상은 그 줄을 버린다.
public static class CurrencyCode
{
    public static bool TryParse(string _value, out ECurrencyType _type)
    {
        _type = ECurrencyType.Gold;
        if (string.IsNullOrEmpty(_value)) return false;
        if (!System.Enum.TryParse(_value, ignoreCase: true, out ECurrencyType t_type)) return false;

        // Enum.TryParse는 "4" 같은 숫자 표기와 Count도 통과시킨다 — 실재 재화만 남긴다
        if (t_type < 0 || t_type >= ECurrencyType.Count) return false;

        _type = t_type;
        return true;
    }
}
