using UnityEngine;
using UnityEngine.UI;

// 프로필 그림 한 덩어리(판·얼굴·링). 이걸 쓰는 화면은 층을 따로 알 필요가 없다.
//
// 판·링은 흰 마스터를 Image.color로 틴트해 쓴다(SuperCasual 팩엔 색칠된 원형 에셋이 없다) —
// 그래서 스프라이트와 색은 항상 한 쌍으로 들어간다.
// 층의 앞뒤는 프리팹이 잡는다(코드에서 형제 순서를 건드리지 않는다).
public class ProfileAvatarView : MonoBehaviour
{
    [Tooltip("가운데 — 얼굴 뒤 원판(아바타 색). 여기 붙은 Mask가 얼굴을 원형으로 잘라낸다 — 스프라이트를 갈면 잘리는 모양도 같이 바뀐다.")]
    [SerializeField] Image plateImage;
    [Tooltip("맨 위 — 얼굴 부품. 판의 Mask에 원형으로 잘린다.")]
    [SerializeField] Image faceImage;
    [Tooltip("맨 아래 — 프레임 원판(프레임 색). 앞의 판이 가운데를 덮어 바깥 테두리만 링으로 드러난다.")]
    [SerializeField] Image ringImage;

    /// <summary>세 층을 한 벌로 그린다.</summary>
    public void Render(in ProfileLook _look)
    {
        ApplyLayer(this.plateImage, _look.Plate, _look.PlateColor);
        ApplyLayer(this.faceImage, _look.Face, null);
        ApplyLayer(this.ringImage, _look.Ring, _look.RingColor);
    }

    // 한 층. 스프라이트가 없으면 그 층은 저작값 그대로 두고 지나간다 — 설정 미배선이 빈 칸으로 드러나지 않게.
    static void ApplyLayer(Image _image, Sprite _sprite, Color? _color)
    {
        if (_image == null || _sprite == null) return;

        _image.sprite = _sprite;
        if (_color.HasValue) _image.color = _color.Value;
    }
}
