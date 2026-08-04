using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도감 테마 데이터
/// </summary>
[CreateAssetMenu(fileName = "CollectionThemeConfig", menuName = "Card Battle/Collection Theme Config")]
public class CollectionThemeConfig : ScriptableObject
{
    // ── 테마 목록 (순서 = 도감 표시 순서) ──
    [Header("도감 테마 목록 (순서 = 표시 순서). 테마마다 카드 수는 자유.")]
    [SerializeField] List<CollectionThemeDef> themes = new List<CollectionThemeDef>();

    // 테마 정의 총 개수. null 방어.
    public int ThemeDefCount => themes != null ? themes.Count : 0;

    // 읽기 전용 테마 목록. null이면 빈 목록(미authoring 상태 안전 처리).
    public IReadOnlyList<CollectionThemeDef> Themes
        => themes != null ? themes : (IReadOnlyList<CollectionThemeDef>)System.Array.Empty<CollectionThemeDef>();

    // static 파사드엔 ContextMenu를 못 다니 이 SO가 저작자용 호출 창구가 된다(플레이 중 우클릭).
    [ContextMenu("테마 배치 검증")]
    void ValidateThemes() => CollectionThemes.ValidateThemes();
}

/// <summary>
/// 도감 테마 하나의 authoring 데이터(인스펙터 입력용)
/// </summary>
[System.Serializable]
public struct CollectionThemeDef
{
    [Tooltip("테마 안정 키. 표시명 리네임·순서 변경에도 불변이어야 한다.")]
    public string themeId;
    public string displayName;
    [Tooltip("헤더 좌측 아이콘(선택). 미지정 허용.")]
    public Sprite icon;

    [Header("테마 카드 (순서 = 슬롯 번호 순서, index+1 = 001,002…)")]
    public List<CardData> cards;
}
