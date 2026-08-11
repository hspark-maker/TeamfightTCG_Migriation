using UnityEngine;

/// <summary>
/// 보상 한 건의 표시 단위(아이콘 + 재화 획득). 저작 포맷은 출처마다 다르지만(랭크 티어·앨범 완성 …)
/// 수령 연출은 이 모양으로만 받는다 — 팝업이 출처를 알 필요가 없게.
/// </summary>
public readonly struct RewardLine
{
    public readonly CurrencyGain Gain;
    public readonly Sprite Icon;

    public RewardLine(CurrencyGain _gain, Sprite _icon)
    {
        Gain = _gain;
        Icon = _icon;
    }
}
