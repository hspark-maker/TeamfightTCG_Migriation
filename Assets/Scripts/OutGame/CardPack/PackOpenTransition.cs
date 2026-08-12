using UnityEngine;

// 팩 1종의 구매→개봉 전환 저작. 팩 에셋이 쥔다 — 전환이 곧 그 팩의 서명이라
// 진열 뷰가 한 벌을 갖고 모든 팩에 같은 것을 씌우면 이 축의 값이 사라진다.
//
// 전환 패키지의 enum(TransitionScreenType)은 참조하지 않고 프리팹을 직접 가리킨다 —
// 그 enum은 패키지의 데모 폴더에 있어 갱신 때 사라질 수 있다.
[System.Serializable]
public class PackOpenTransition
{
    [Tooltip("전환 화면 프리팹. Assets/Plugins/Transition screen package/Prefabs 아래 TS1~TS16의 " +
             "Normal/Outlined 32개 중 하나를 그대로 끼운다(사본을 만들 필요 없다 — 정렬·비율·입력 보정은 런타임이 한다). " +
             "비우면 이 팩은 예전 흰 플래시로 돈다. 그건 오류가 아니라 폴백이다.")]
    public GameObject screenPrefab;

    [Tooltip("전환을 채우는 색. 모양은 프리팹이 정하고 색은 여기가 정한다. " +
             "알파를 1 미만으로 내리면 완전히 덮이지 않아 화면이 갈아치워지는 프레임이 비친다.")]
    public Color fillColor = new Color(0.05f, 0.06f, 0.1f, 1f);

    [Tooltip("채움에 깔 그림(선택). 비우면 단색이다 — 그게 기본이고 대개 정답이다.\n\n" +
             "⚠ 반드시 사각형 전체가 불투명한 그림만 쓸 것. 알파가 비는 그림(글로우·빛 스프라이트류)을 넣으면 " +
             "전환이 화면을 완전히 덮지 못해 화면이 갈아치워지는 프레임이 그 틈으로 비친다. " +
             "셰이더 효과에 쓸 텍스처는 여기가 아니라 머티리얼(.mat) 안의 자기 슬롯에 넣는다.")]
    public Sprite fillSprite;

    [Tooltip("채움에 물릴 머티리얼(선택). 반드시 AllIn1SpriteShaderUiMask 계열이어야 한다 — " +
             "일반 AllIn1SpriteShader는 스텐실을 읽지 않아 마스크를 무시하고 화면 전체를 덮는다. " +
             "효과 키워드는 런타임에 켜지 않고 .mat에 미리 구워 둔다(Materials/Growth/CardRitual*.mat와 같은 규약). " +
             "아우트라인·글로우·섀도는 스프라이트의 알파 경계를 읽는 축이라 여기서는 전환 모양이 아닌 화면 사각 테두리에 걸린다 " +
             "— 경계 장식은 셰이더가 아니라 Outlined 프리팹으로 얻을 것.")]
    public Material fillMaterial;

    [Tooltip("재생 속도 배수. 패키지 원본은 다 덮이기까지 1.0~1.33초라 그대로 쓰면 전환이 느리다 — " +
             "3이면 0.33~0.44초로, 예전 흰 플래시(0.42초)와 체감 길이가 같다. " +
             "이 값을 팩마다 다르게 잡는 것만으로도 등급 차이가 읽힌다(싼 팩은 빠르게, 비싼 팩은 느리게).")]
    [Range(0.5f, 8f)] public float speed = 3f;

    /// <summary>저작됐는가. 프리팹이 비면 호출부는 예전 흰 플래시로 돈다.</summary>
    public bool IsAuthored => screenPrefab != null;
}
