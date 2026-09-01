using System.Collections.Generic;
using UnityEngine;

// 카드 앨범(신규 도감)의 그림 저작 — 구조·표시 텍스트·보상은 스펙시트가 진실원이고, 여기엔 스킨만 남는다
[CreateAssetMenu(fileName = "CardAlbumConfig", menuName = "Card Battle/Card Album Config")]
public class CardAlbumConfig : ScriptableObject
{
    [Header("테마 스킨 (themeId로 스펙시트 테마와 이어진다)")]
    [Tooltip("테마의 이름·소개·잠김과 페이지·칸 배치는 스펙시트(AlbumThemeInfo·AlbumEntry)에서 저작한다. " +
             "여기 목록은 그림만 공급하며 순서는 아무 의미가 없다 — 표에 없는 themeId 줄은 그냥 무시된다.")]
    [SerializeField] List<AlbumThemeSkin> themes = new List<AlbumThemeSkin>();

    public int ThemeSkinCount => themes != null ? themes.Count : 0;

    public IReadOnlyList<AlbumThemeSkin> Themes
        => themes != null ? themes : (IReadOnlyList<AlbumThemeSkin>)System.Array.Empty<AlbumThemeSkin>();

#if UNITY_EDITOR
    [ContextMenu("앨범 배치 검증")]
    void ValidateAlbum() => AlbumValidator.Validate(this);

    // 저작 변경 즉시 반영 — 구조 캐시는 SetSource 외엔 스스로 갱신하지 않는다
    void OnValidate() => CardAlbum.InvalidateIfSource(this);
#endif
}

// 앨범 테마 하나의 그림 저작. 표시 속성은 담지 않는다(스펙시트 AlbumThemeInfo 소관)
[System.Serializable]
public struct AlbumThemeSkin
{
    [Tooltip("스펙시트 AlbumThemeInfo.themeId 와 같아야 이어진다. 오타면 그 테마는 그림 없이 그려진다.")]
    public string themeId;

    [Tooltip("갤러리 셀의 테마 썸네일. 비우면 셀 프리팹 기본값 유지.")]
    public Sprite icon;

    [Tooltip("셀 썸네일 프레임. 비우면 셀 프리팹에 저작된 스프라이트를 그대로 쓴다(테마마다 색을 달리하려면 저작할 것).")]
    public Sprite frame;

    [Tooltip("셀 이름판 배경. 비우면 셀 프리팹 기본값 유지.")]
    public Sprite namePlate;

    [Tooltip("이 테마 전용 셀 프리팹(AlbumThemeCellView가 붙어 있어야 한다). 비우면 갤러리 기본 셀을 쓴다 — 색만 다르면 위 스킨 3종으로 충분하고, 셀 구조 자체가 다를 때만 저작할 것.")]
    public GameObject cellPrefab;
}

// 보상 1건 값 타입. 그림은 담지 않는다 — 재화 아이콘의 진실원은 CurrencyLook 한 장이고,
// 값의 진실원은 스펙시트 Reward 표다(RewardSpec이 이 형으로 돌려준다).
[System.Serializable]
public struct AlbumRewardDef
{
    public ECurrencyType currency;
    public long amount;
}
