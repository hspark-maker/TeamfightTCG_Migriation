using UnityEngine;

/// <summary>
/// "한쪽 끝 그늘" 띠 스프라이트 한 장. 왼쪽 끝 불투명 → 오른쪽 끝 투명인 가로 그라디언트다.
///
/// 책 넘김에서 접히는 쪽(책등)이 빛을 못 받아 어두워지는 걸 흉내 내는 용도다.
/// 반대쪽 책등에 쓰려면 스프라이트를 또 굽지 말고 RectTransform의 localScale.x를 -1로 뒤집어라.
///
/// <see cref="ShineBandSprite"/>와 형제지만 그쪽은 sin²라 좌우 대칭이다 — 대칭 그라디언트를
/// 책등 그늘로 쓰면 "가운데가 접힌 자국"으로 읽힌다. 그래서 형태를 공유하지 않는다.
///
/// ppu=1이라 스프라이트 크기는 <see cref="UnitWidth"/> × 1 유닛이지만, UI Image로 쓰면
/// RectTransform 크기가 이기므로 신경 쓸 필요 없다.
/// </summary>
public static class EdgeShadeSprite
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
            // 지수 > 1 = 책등 가까이로 그늘이 몰린다. 선형이면 종이 전체가 회색으로 뜬다
            t_tex.SetPixel(i, 0, new Color(1f, 1f, 1f, Mathf.Pow(1f - t_u, 1.5f)));
        }
        t_tex.Apply();

        s_sprite = Sprite.Create(t_tex, new Rect(0f, 0f, UnitWidth, 1f), new Vector2(0.5f, 0.5f), 1f);
        s_sprite.hideFlags = HideFlags.HideAndDontSave;
        return s_sprite;
    }
}
