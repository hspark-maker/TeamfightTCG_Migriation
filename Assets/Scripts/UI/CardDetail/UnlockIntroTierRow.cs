using TMPro;
using UnityEngine;

/// <summary>해금 안내에서 시너지 한 단계를 적는 줄. 왼쪽 배지에 요구 장수, 오른쪽에 그 단계의 효과 요약.
///
/// 효과 요약의 진실원은 스펙시트 <c>SynergyTierDef.effectSummary</c>이다 — 비어 있으면 장수만 남는다.
/// 어느 칸이 미배선이어도 그 축만 빠지고 줄은 성립한다(KeywordExplainItem과 같은 규약).</summary>
[RequireComponent(typeof(RectTransform))]
public class UnlockIntroTierRow : MonoBehaviour
{
    [Tooltip("배지 안에 들어가는 요구 장수(\"3장\"). 미배선이면 이 축만 빠진다.")]
    [SerializeField] TMP_Text countText;

    [Tooltip("그 단계의 효과 요약. 미배선이면 이 축만 빠진다.")]
    [SerializeField] TMP_Text effectText;

    /// <summary>줄을 채운다. _effect가 비면 그 칸을 끄고 배지만 남긴다 —
    /// 빈 글자 칸이 서 있으면 배지가 줄 왼쪽에 치우쳐 보인다.</summary>
    public void Bind(string _requirement, string _effect)
    {
        if (this.countText != null) this.countText.text = _requirement;

        if (this.effectText == null) return;

        this.effectText.text = _effect;
        this.effectText.gameObject.SetActive(!string.IsNullOrEmpty(_effect));
    }
}
