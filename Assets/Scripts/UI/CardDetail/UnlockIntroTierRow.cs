using TMPro;
using UnityEngine;

/// <summary>해금 안내에서 시너지 한 단계를 적는 줄(왼쪽 배지에 요구 장수, 오른쪽에 효과 요약).</summary>
[RequireComponent(typeof(RectTransform))]
public class UnlockIntroTierRow : MonoBehaviour
{
    [Tooltip("배지 안에 들어가는 요구 장수(\"3장\"). 미배선이면 이 축만 빠진다.")]
    [SerializeField] TMP_Text countText;

    [Tooltip("그 단계의 효과 요약. 미배선이면 이 축만 빠진다.")]
    [SerializeField] TMP_Text effectText;

    /// <summary>줄을 채운다. _effect가 비면 그 칸을 끄고 배지만 남긴다.</summary>
    public void Bind(string _requirement, string _effect)
    {
        if (this.countText != null) this.countText.text = _requirement;

        if (this.effectText == null) return;

        this.effectText.text = _effect;
        this.effectText.gameObject.SetActive(!string.IsNullOrEmpty(_effect));
    }
}
