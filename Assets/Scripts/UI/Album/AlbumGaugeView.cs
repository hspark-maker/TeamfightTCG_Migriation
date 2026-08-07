using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 앨범 3계층(전체·테마·페이지) 공용 진행 게이지 — MonoBehaviour가 아니라 뷰가 필드로 소유한다
[System.Serializable]
public class AlbumGaugeView
{
    [Tooltip("Image Type=Filled 전제. 9-slice 스프라이트라면 대신 아래 fillRect를 배선할 것.")]
    [SerializeField] Image fill;
    [Tooltip("마스크형 게이지의 Fill RectTransform. 배선하면 fillAmount 대신 이쪽 폭으로 채운다(9-slice 끝단 유지용).")]
    [SerializeField] RectTransform fillRect;
    [SerializeField] TMP_Text label;

    public void Set(int _owned, int _total)
    {
        float t_ratio = _total > 0 ? Mathf.Clamp01((float)_owned / _total) : 0f;

        // 9-slice Fill은 Type=Filled를 못 쓴다(끝단이 늘어난다) — 마스크 안에서 폭을 줄여 채운다
        if (fillRect != null)
        {
            fillRect.anchorMin = new Vector2(0f, fillRect.anchorMin.y);
            fillRect.anchorMax = new Vector2(t_ratio, fillRect.anchorMax.y);
            fillRect.offsetMin = new Vector2(0f, fillRect.offsetMin.y);
            fillRect.offsetMax = new Vector2(0f, fillRect.offsetMax.y);
            // 폭 0에서도 9-slice 최소 너비(좌우 border)가 남아 조각이 보인다
            fillRect.gameObject.SetActive(t_ratio > 0f);
        }
        else if (fill != null) fill.fillAmount = t_ratio;

        if (label != null) label.text = $"{_owned}/{_total}";
    }
}
