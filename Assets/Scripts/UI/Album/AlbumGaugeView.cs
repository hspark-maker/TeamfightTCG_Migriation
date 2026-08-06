using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 앨범 3계층(전체·테마·페이지) 공용 진행 게이지 — MonoBehaviour가 아니라 뷰가 필드로 소유한다
[System.Serializable]
public class AlbumGaugeView
{
    [SerializeField] Image fill;       // Image Type=Filled 전제
    [SerializeField] TMP_Text label;

    public void Set(int _owned, int _total)
    {
        if (fill != null) fill.fillAmount = _total > 0 ? (float)_owned / _total : 0f;
        if (label != null) label.text = $"{_owned}/{_total}";
    }
}
