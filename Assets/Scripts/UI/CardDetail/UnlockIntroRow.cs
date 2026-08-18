using UnityEngine;
using UnityEngine.UI;

// 해금 안내 한 줄. 글자 부분은 인게임 정보창과 같은 컴포넌트(KeywordExplainItem)에 그대로 위임하고,
// 이 클래스는 그 사이에 끼는 **데모 띠**만 맡는다.
//
// 띠가 설 자리는 저작이 정한다 — 구분선과 설명 글자 사이의 빈 띠가 그 자리다.
// 코드가 자리를 만들지(높이를 늘리거나 글자를 밀지) 않는 이유: 그러면 저작에서 본 모습과
// 런타임 모습이 갈려, 프리팹을 열어 맞춰 놓은 간격이 실행하는 순간 다시 틀어진다.
//
// 띠를 여기서 쥐는 이유는 참조 배선을 지키기 위해서다 — 오버레이가 행 내부 계층을 뒤져 RawImage를 찾게 두면
// 노드 이름을 바꾸는 순간 조용히 꺼진다(KeywordExplainItem이 같은 규약을 쓴다).
[RequireComponent(typeof(RectTransform))]
public class UnlockIntroRow : MonoBehaviour
{
    [Tooltip("아이콘·이름·설명. 미배선이면 글자 없이 띠만 뜬다.")]
    [SerializeField] KeywordExplainItem item;

    [Tooltip("데모 무대가 그려지는 띠(구분선과 설명 사이). 미배선이면 이 축만 빠지고 행은 글자로 성립한다.")]
    [SerializeField] RawImage demoStrip;

    [Tooltip("띠의 비율을 그림에 맞추는 것(선택). 배선해 두면 데모 해상도를 바꿔도 늘어나거나 눌리지 않는다.")]
    [SerializeField] AspectRatioFitter demoFitter;

    /// <summary>글자를 채운다. 띠는 건드리지 않는다 — 무엇을 띄울지는 <see cref="SetDemo"/>가 따로 정한다
    /// (여러 줄이 서도 데모는 하나뿐이기 때문).</summary>
    public void Bind(UnlockIntro _intro)
    {
        if (this.item == null) return;

        this.item.Init(_intro.Icon, _intro.Name, _intro.Body, _intro.IconScale);
    }

    /// <summary>띠에 그림을 물린다. null이면 띠를 끈다 —
    /// 텍스처 없이 켜두면 마지막 프레임이 굳은 채 남거나 빈 사각형이 뜬다.</summary>
    public void SetDemo(Texture _texture)
    {
        if (this.demoStrip == null) return;

        this.demoStrip.texture = _texture;
        this.demoStrip.gameObject.SetActive(_texture != null);

        // 비율은 그림이 정한다 — 저작값으로 못 박아 두면 데모 해상도를 손볼 때마다 여기도 같이 고쳐야 한다.
        if (this.demoFitter != null && _texture != null && _texture.height > 0)
            this.demoFitter.aspectRatio = (float)_texture.width / _texture.height;
    }
}
