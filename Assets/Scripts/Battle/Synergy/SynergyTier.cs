// 저작하지 않는다 — SynergySpecSource가 시트 행에서 만들어 꽂는다(인스펙터에 뜨지 않으므로 직렬화 속성도 없다).
public sealed class SynergyTier
{
    public int requiredCount;

    // 이 단계의 별칭(SynergyTierDef.label). 비면 요구 장수만 나오고, 시너지 이름과 같으면 표시하지 않는다.
    public string label;

    // 이 단계가 무엇을 주는지 한 줄(SynergyTierDef.effectSummary). '3장 — 추가 생명력 +1'의 뒷부분이다.
    // 효과 설명문(SynergyData.effectDescription)은 단계별 수치를 담지 않는다 — 그 축의 진실원이 여기다.
    public string effectSummary;

    public SynergyEffect[] effects;
}
