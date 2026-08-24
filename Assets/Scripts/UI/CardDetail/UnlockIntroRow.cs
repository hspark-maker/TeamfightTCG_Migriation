using UnityEngine;
using UnityEngine.UI;

/// <summary>해금 안내 한 줄. 글자는 KeywordExplainItem에 위임하고, 가운데를 채우는 데모 띠와 시너지 배지를 맡는다.</summary>
[RequireComponent(typeof(RectTransform))]
public class UnlockIntroRow : MonoBehaviour
{
    [Tooltip("아이콘·이름·설명. 미배선이면 글자 없이 띠만 뜬다.")]
    [SerializeField] KeywordExplainItem item;

    [Tooltip("데모 무대가 그려지는 띠(구분선과 설명 사이). 미배선이면 이 축만 빠지고 행은 글자로 성립한다.")]
    [SerializeField] RawImage demoStrip;

    [Tooltip("띠의 비율을 그림에 맞추는 것(선택). 배선해 두면 데모 해상도를 바꿔도 늘어나거나 눌리지 않는다.")]
    [SerializeField] AspectRatioFitter demoFitter;

    [Tooltip("시너지 칸이 데모 띠 대신 쓰는 가운데 배지(임시). 미배선이면 이 축만 빠지고 행은 글자로 성립한다.")]
    [SerializeField] Image synergyBadge;

    /// <summary>글자와 시너지 배지를 채운다. 데모 띠는 <see cref="SetDemo"/>가 따로 정한다.</summary>
    public void Bind(UnlockIntro _intro)
    {
        // 시너지에는 데모 대본이 없어 가운데가 통째로 빈다 — 임시로 아이콘을 크게 세워 그 자리를 메운다.
        SetSynergyBadge(_intro.IsSynergy ? _intro.Icon : null);

        if (this.item == null) return;

        this.item.Init(_intro.Icon, _intro.Name, _intro.Body, _intro.IconScale);
    }

    /// <summary>데모 띠에 그림을 물린다. null이면 띠를 끈다.</summary>
    public void SetDemo(Texture _texture)
    {
        if (this.demoStrip == null) return;

        this.demoStrip.texture = _texture;
        this.demoStrip.gameObject.SetActive(_texture != null);

        if (this.demoFitter != null && _texture != null && _texture.height > 0)
            this.demoFitter.aspectRatio = (float)_texture.width / _texture.height;
    }

    void SetSynergyBadge(Sprite _icon)
    {
        if (this.synergyBadge == null) return;

        this.synergyBadge.sprite = _icon;
        this.synergyBadge.gameObject.SetActive(_icon != null);
    }
}
