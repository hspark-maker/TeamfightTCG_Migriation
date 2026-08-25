using System.Collections.Generic;
using UnityEngine;

// 카드 앨범(신규 도감) 저작 데이터 — 테마 → 페이지 → 칸 3계층 + 보상 3단
[CreateAssetMenu(fileName = "CardAlbumConfig", menuName = "Card Battle/Card Album Config")]
public class CardAlbumConfig : ScriptableObject
{
    [Header("앨범 테마 목록 (순서 = 표시 순서)")]
    [SerializeField] List<AlbumThemeDef> themes = new List<AlbumThemeDef>();

    [Header("앨범 전체 완성 보상 (복수)")]
    [SerializeField] List<AlbumRewardDef> albumRewards = new List<AlbumRewardDef>();

    public int ThemeDefCount => themes != null ? themes.Count : 0;

    public IReadOnlyList<AlbumThemeDef> Themes
        => themes != null ? themes : (IReadOnlyList<AlbumThemeDef>)System.Array.Empty<AlbumThemeDef>();

    public IReadOnlyList<AlbumRewardDef> AlbumRewards
        => albumRewards != null ? albumRewards : (IReadOnlyList<AlbumRewardDef>)System.Array.Empty<AlbumRewardDef>();

#if UNITY_EDITOR
    [ContextMenu("앨범 배치 검증")]
    void ValidateAlbum() => AlbumValidator.Validate();

    // 저작 변경 즉시 반영 — 구조 캐시는 SetSource 외엔 스스로 갱신하지 않는다
    void OnValidate() => CardAlbum.InvalidateIfSource(this);
#endif
}

// 앨범 테마 하나의 저작 항목
[System.Serializable]
public struct AlbumThemeDef
{
    [Tooltip("테마 안정 키 — 리네임·순서 변경에도 불변. 비우면 보상이 영구 잠긴다.")]
    public string themeId;
    public string displayName;

    [Tooltip("셀에 한 줄로 붙는 테마 소개. 비우면 셀의 설명 줄이 빈다.")]
    public string description;

    [Tooltip("준비 중 테마. 켜면 갤러리에 흑백+자물쇠로만 그려지고 열리지 않으며, 앨범 진행도·완성 보상 모수에서도 빠진다.")]
    public bool locked;

    public Sprite icon;
    [Tooltip("셀 썸네일 프레임. 비우면 셀 프리팹에 저작된 스프라이트를 그대로 쓴다(테마마다 색을 달리하려면 저작할 것).")]
    public Sprite frame;
    [Tooltip("셀 이름판 배경. 비우면 셀 프리팹 기본값 유지.")]
    public Sprite namePlate;
    [Tooltip("이 테마 전용 셀 프리팹(AlbumThemeCellView가 붙어 있어야 한다). 비우면 갤러리 기본 셀을 쓴다 — 색만 다르면 위 스킨 3종으로 충분하고, 셀 구조 자체가 다를 때만 저작할 것.")]
    public GameObject cellPrefab;
    public List<AlbumRewardDef> rewards;
    public List<AlbumPageDef> pages;
}

// 앨범 페이지 하나의 저작 항목
[System.Serializable]
public struct AlbumPageDef
{
    [Tooltip("페이지 안정 키(테마 내 유일). 비우면 보상이 영구 잠긴다.")]
    public string pageId;
    public List<AlbumRewardDef> rewards;
    [Tooltip("칸 순서 = 리스트 순서. null 칸 허용(완성 판정 모수에서 제외).")]
    public List<CardData> cards;
}

// 보상 1건 저작값. 그림은 담지 않는다 — 재화 아이콘의 진실원은 CurrencyLook 한 장이고,
// 이 값은 스펙시트로도 저작되므로 에셋 참조를 실을 자리가 없다.
[System.Serializable]
public struct AlbumRewardDef
{
    public ECurrencyType currency;
    public long amount;
}
