using UnityEngine;

/// <summary>
/// 카드 콘텐츠(Body)를 이 RectTransform 크기에 맞춰 **균일 localScale**로 축소/확대한다.
/// 앵커 비례화(rect)만으로는 TMP 폰트·런타임 아이콘 같은 고정 px 요소가 안 줄어드는 문제를 해결한다.
/// Body는 고정 네이티브 크기(nativeSize)를 유지하고, 마운트(예: GridLayout 셀)가 준 rect에 맞춰 통째로 scale.
/// 결과: 텍스트/아이콘/아트가 한 덩어리로 비례 축소되어 어떤 크기에서도 동일 비율.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class CardAutoScale : MonoBehaviour
{
    [Tooltip("실제 카드 콘텐츠 루트(고정 네이티브 크기 유지). 이 오브젝트를 scale 한다.")]
    [SerializeField] RectTransform body;

    [Tooltip("Body의 기준(네이티브) 크기. 이 크기를 마운트 rect에 맞춰 축소/확대.")]
    [SerializeField] Vector2 nativeSize = new Vector2(275f, 450f);

    RectTransform rt;

    void OnEnable()
    {
        this.rt = (RectTransform)transform;
        Apply();
    }

    void OnRectTransformDimensionsChange() => Apply();

    void Apply()
    {
        if (this.body == null) return;
        if (this.rt == null) this.rt = (RectTransform)transform;
        if (this.nativeSize.x <= 0f || this.nativeSize.y <= 0f) return;

        Vector2 s = this.rt.rect.size;
        float k = Mathf.Min(s.x / this.nativeSize.x, s.y / this.nativeSize.y);   // 안쪽에 맞춤(fit)
        if (k <= 0f || float.IsNaN(k) || float.IsInfinity(k)) return;

        this.body.localScale = new Vector3(k, k, 1f);
    }
}
