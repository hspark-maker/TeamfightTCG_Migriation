using UnityEngine;

// 크기가 제각각인 칸에 "비율이 고정된 콘텐츠"를 얹기 위한 균등 스케일러.
// 이 컴포넌트가 붙은 rect(= 칸)에 맞춰 content를 **가로세로 같은 배율로** 줄이거나 늘린다.
//
// uGUI의 스트레치 앵커는 칸의 종횡비가 원본과 다르면 그림을 늘려 버린다. 카드 칸은 화면마다
// 종횡비가 다르므로(도감 300x380, 덱편집 270x360, 팩개봉 1000x1230) 스트레치로는 인게임 카드 비율을
// 유지할 수 없다. 그래서 content는 고정 크기로 두고 배율만 바꾼다 — 남는 쪽엔 여백이 생길 뿐 왜곡은 없다.
//
// content가 고정 크기라는 점이 이 구조의 진짜 이득이다: 그 안쪽은 전부 평범한 픽셀 앵커/오프셋으로
// 배치할 수 있어 인스펙터에서 그대로 만질 수 있다(비율 앵커는 소수점 값을 직접 타이핑해야 했다).
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class UniformFitContent : MonoBehaviour
{
    [SerializeField] RectTransform content;   // 배율만 조절할 고정 크기 자식. 기준 크기 = 이 rect의 sizeDelta.

    void OnEnable() => Apply();

    // 칸 크기가 확정되는 시점은 프레임마다 다르다(LayoutGroup 첫 프레임, 부모가 켜지는 순간 등).
    // 이 콜백이 그때마다 다시 불러주므로 별도의 갱신 호출이 필요 없다.
    void OnRectTransformDimensionsChange() => Apply();

    void Apply()
    {
        if (this.content == null) return;
        if (!(this.transform is RectTransform t_self)) return;

        Vector2 t_frame = t_self.rect.size;
        Vector2 t_ref   = this.content.rect.size;

        // 레이아웃 전(크기 0)이면 건너뛴다. 여기서 0을 곱해두면 카드가 사라진 채로 남고,
        // 크기가 확정될 때 위 콜백이 다시 부른다.
        if (t_frame.x <= 0f || t_frame.y <= 0f || t_ref.x <= 0f || t_ref.y <= 0f) return;

        float t_scale = Mathf.Min(t_frame.x / t_ref.x, t_frame.y / t_ref.y);
        if (t_scale <= 0f) return;

        // 같은 값을 다시 써도 Unity는 트랜스폼 변경으로 취급해 레이아웃을 재계산한다 → 값이 바뀔 때만 쓴다.
        if (!Mathf.Approximately(this.content.localScale.x, t_scale))
            this.content.localScale = new Vector3(t_scale, t_scale, 1f);
    }
}
