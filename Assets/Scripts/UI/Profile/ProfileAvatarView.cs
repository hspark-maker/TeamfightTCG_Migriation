using UnityEngine;
using UnityEngine.UI;

// 프로필 그림 한 덩어리(판·얼굴·링). 이걸 쓰는 화면은 층을 따로 알 필요가 없다.
//
// 판·링 색은 스프라이트와 한 쌍으로 들어온다 — 흰 마스터를 틴트해 쓰는 아트가 섞여 있어서다.
// 완성 아트면 색을 흰색으로 두면 그대로 나온다.
// 층의 앞뒤는 프리팹이 잡는다(코드에서 형제 순서를 건드리지 않는다).
public class ProfileAvatarView : MonoBehaviour
{
    [Tooltip("맨 아래 — 원형 마스크 겸 얼굴 뒤 판(아바타 색). 이 스프라이트 모양이 곧 얼굴이 잘리는 모양이다.")]
    [SerializeField] Image plateImage;
    [Tooltip("가운데 — 아바타 그림. 판을 꽉 채우고 판의 Mask에 원형으로 잘린다.")]
    [SerializeField] Image faceImage;
    [Tooltip("맨 위 — 아바타 위에 덧씌우는 속이 뚫린 테두리 링(프레임 색).")]
    [SerializeField] Image ringImage;

    /// <summary>세 층을 한 벌로 그린다.</summary>
    public void Render(in ProfileLook _look)
    {
        ApplyLayer(this.plateImage, _look.Plate, _look.PlateColor);
        ApplyLayer(this.faceImage, _look.Face, null);
        ApplyLayer(this.ringImage, _look.Ring, _look.RingColor);
    }

    /// <summary>한 축만 남기고 끈다. 선택 칸처럼 "그 항목 자체"만 보여야 하는 자리가 쓴다.</summary>
    public void ShowOnly(EProfileAxis _axis)
    {
        SetLayerActive(this.plateImage, _axis == EProfileAxis.Avatar);
        SetLayerActive(this.faceImage,  _axis == EProfileAxis.Avatar);
        SetLayerActive(this.ringImage,  _axis == EProfileAxis.Frame);
    }

    static void SetLayerActive(Image _image, bool _on)
    {
        if (_image != null) _image.gameObject.SetActive(_on);
    }

    // 한 층. 스프라이트가 없으면 그 층은 저작값 그대로 두고 지나간다 — 설정 미배선이 빈 칸으로 드러나지 않게.
    static void ApplyLayer(Image _image, Sprite _sprite, Color? _color)
    {
        if (_image == null || _sprite == null) return;

        _image.sprite = _sprite;
        if (_color.HasValue) _image.color = _color.Value;
    }
}
