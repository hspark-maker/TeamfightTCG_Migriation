using UnityEngine;

/// <summary>
/// "유리 반짝임" 띠 스프라이트 한 장. 양 끝 투명 → 가운데 흰색인 가로 그라디언트다.
///
/// 전용 아트를 두지 않는 이유: 형태가 수식 한 줄(sin²)이라 에셋으로 둘 게 없고,
/// 쓰는 쪽이 월드(SpriteRenderer)와 UI(Image) 양쪽이라 임포트 설정이 갈릴 여지도 없다.
/// **여기가 그 형태의 단일 진실원** — 반짝임을 새로 만들 때 텍스처를 또 굽지 마라.
///
/// ppu=1이라 스프라이트 크기는 <see cref="UnitWidth"/> × 1 유닛이다. 월드에서 쓰려면 그 비율로
/// localScale을 잡고, UI Image로 쓰면 RectTransform 크기가 이기므로 신경 쓸 필요 없다.
/// </summary>
public static class ShineBandSprite
{
    public const int UnitWidth = 64;

    static Sprite s_sprite;

    public static Sprite Get()
    {
        if (s_sprite != null) return s_sprite;

        var t_tex = new Texture2D(UnitWidth, 1, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags  = HideFlags.HideAndDontSave,   // 씬/에셋에 새지 않게(런타임 전용 리소스)
        };
        for (int i = 0; i < UnitWidth; i++)
        {
            float t_u = (i + 0.5f) / UnitWidth;
            float t_a = Mathf.Sin(t_u * Mathf.PI);   // 양 끝 0, 가운데 1
            t_tex.SetPixel(i, 0, new Color(1f, 1f, 1f, t_a * t_a));   // 제곱 = 가운데로 더 모인 얇은 반사
        }
        t_tex.Apply();

        s_sprite = Sprite.Create(t_tex, new Rect(0f, 0f, UnitWidth, 1f), new Vector2(0.5f, 0.5f), 1f);
        s_sprite.hideFlags = HideFlags.HideAndDontSave;
        return s_sprite;
    }
}
