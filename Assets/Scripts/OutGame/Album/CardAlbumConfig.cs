using System.Collections.Generic;
using UnityEngine;

// 카드 앨범(신규 도감) 저작 데이터 — 테마 → 페이지 → 칸 3계층 + 보상 3단
[CreateAssetMenu(fileName = "CardAlbumConfig", menuName = "Card Battle/Card Album Config")]
public class CardAlbumConfig : ScriptableObject
{
    [Header("앨범 테마 목록 (순서 = 표시 순서)")]
    [SerializeField] List<AlbumThemeDef> themes = new List<AlbumThemeDef>();

    [Header("앨범 전체 완성 보상")]
    [SerializeField] AlbumRewardDef albumReward;

    public int ThemeDefCount => themes != null ? themes.Count : 0;

    public IReadOnlyList<AlbumThemeDef> Themes
        => themes != null ? themes : (IReadOnlyList<AlbumThemeDef>)System.Array.Empty<AlbumThemeDef>();

    public AlbumRewardDef AlbumReward => albumReward;

    [ContextMenu("앨범 배치 검증")]
    void ValidateAlbum() => CardAlbum.ValidateAlbum();
}

// 앨범 테마 하나의 저작 항목
[System.Serializable]
public struct AlbumThemeDef
{
    [Tooltip("테마 안정 키 — 리네임·순서 변경에도 불변. 비우면 보상이 영구 잠긴다.")]
    public string themeId;
    public string displayName;
    public Sprite icon;
    public AlbumRewardDef reward;
    public List<AlbumPageDef> pages;
}

// 앨범 페이지 하나의 저작 항목
[System.Serializable]
public struct AlbumPageDef
{
    [Tooltip("페이지 안정 키(테마 내 유일). 비우면 보상이 영구 잠긴다.")]
    public string pageId;
    public AlbumRewardDef reward;
    [Tooltip("칸 순서 = 리스트 순서. null 칸 허용(완성 판정 모수에서 제외).")]
    public List<CardData> cards;
}

// 보상 1건 저작값
[System.Serializable]
public struct AlbumRewardDef
{
    public ECurrencyType currency;
    public long amount;
    public Sprite icon;
}
