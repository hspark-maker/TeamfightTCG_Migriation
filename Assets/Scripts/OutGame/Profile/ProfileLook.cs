using UnityEngine;

// 프로필 한 사람의 그리기 값 묶음. 판·얼굴·링 3층이 한 벌로 움직인다.
public readonly struct ProfileLook
{
    // 스프라이트는 null을 허용한다 — 뷰가 프리팹에 저작된 그림을 그대로 유지한다.
    public readonly Sprite Plate;        // 얼굴 뒤 판(흰 마스터)
    public readonly Color  PlateColor;   // 판을 칠할 색 = 그 아바타의 색
    public readonly Sprite Face;         // 판 위에 얹는 얼굴
    public readonly Sprite Ring;         // 그 위에 덧씌우는 속 뚫린 테두리
    public readonly Color  RingColor;

    public ProfileLook(Sprite _plate, Color _plateColor, Sprite _face, Sprite _ring, Color _ringColor)
    {
        Plate = _plate;
        Face  = _face;
        Ring  = _ring;

        // 색을 안 넘긴 자리는 default(투명 검정)로 들어온다 — 그대로 칠하면 판·링이 검게 죽으므로 틴트 없음(white)으로 되돌린다.
        PlateColor = _plateColor == default(Color) ? Color.white : _plateColor;
        RingColor  = _ringColor  == default(Color) ? Color.white : _ringColor;
    }

    public bool IsEmpty => Plate == null && Face == null && Ring == null;
}
