using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 도감·토너먼트 구성(SO 저작)을 스펙 표로 옮겨 Firestore에 올린다.
/// 서버가 "페이지 완성"·"챕터 완주"를 판정할 근거 표이며, 커밋 규약은 SpecData 표와 같은 경로를 그대로 탄다.
/// </summary>
public static partial class SpecFirestoreUploader
{
    /// <summary>도감 구성 표 이름(테마 → 페이지 → 칸).</summary>
    public const string ALBUM_ENTRY_TABLE = "AlbumEntry";

    /// <summary>토너먼트 구성 표 이름(챕터 → 정점).</summary>
    public const string TOURNAMENT_CHAPTER_TABLE = "TournamentChapter";

    // 열 순서 = 필드 선언 순서다(TryBuildSnapshotFrom이 GetFields로 읽는다). 첫 열은 반드시 int id.
    // id는 순회 위치에서 파생하는 일련번호라 안정 키가 아니다 — 소비는 blob payload뿐이고
    // rows/{id} 문서 경로를 참조 키로 쓰면 앞에 행 하나만 끼워도 가리키는 대상이 바뀐다
    sealed class AlbumEntryRow
    {
        public int id;
        public string themeId;
        public string pageId;
        public int cardId;
        public int order;
    }

    sealed class TournamentChapterRow
    {
        public int id;
        public string chapterId;
        public string nodeId;
        public int order;
        public string prevNodeId;
        public int requiredPoints;
    }

    /// <summary>CardAlbumConfig 저작을 AlbumEntry 표로 올린다. 성공하면 보고 줄을, 실패하면 null과 _error를 준다.</summary>
    public static string UploadAlbumEntries(string _envId, out string _error)
    {
        if (!TryBeginCompositionUpload(out string t_projectId, out string t_apiKey, out _error)) return null;
        if (!TryLoadAuthoringAsset<CardAlbumConfig>(out CardAlbumConfig t_config, out _error)) return null;
        if (!TryLoadLiveCardIds(out HashSet<int> t_liveCardIds, out _error)) return null;
        if (!TryBuildAlbumEntryRows(t_config, t_liveCardIds, out List<AlbumEntryRow> t_rows, out int t_skipped, out _error))
            return null;
        if (!TryBuildSnapshotFrom(t_rows, ALBUM_ENTRY_TABLE, out TableSnapshot t_snapshot, out _error)) return null;

        string t_line = UploadSnapshot(t_projectId, t_apiKey, _envId, ALBUM_ENTRY_TABLE, t_snapshot, out _error);
        if (t_line != null && t_skipped > 0) t_line += $" / 소유 불가 칸 {t_skipped}개 제외";
        return t_line;
    }

    /// <summary>TournamentConfig 저작을 TournamentChapter 표로 올린다. 성공하면 보고 줄을, 실패하면 null과 _error를 준다.</summary>
    public static string UploadTournamentChapters(string _envId, out string _error)
    {
        if (!TryBeginCompositionUpload(out string t_projectId, out string t_apiKey, out _error)) return null;
        if (!TryLoadAuthoringAsset<TournamentConfig>(out TournamentConfig t_config, out _error)) return null;
        if (!TryLoadAuthoringAsset<RankConfig>(out RankConfig t_rankConfig, out _error)) return null;
        if (!TryBuildTournamentChapterRows(t_config, t_rankConfig, out List<TournamentChapterRow> t_rows, out _error)) return null;
        if (!TryBuildSnapshotFrom(t_rows, TOURNAMENT_CHAPTER_TABLE, out TableSnapshot t_snapshot, out _error)) return null;

        return UploadSnapshot(t_projectId, t_apiKey, _envId, TOURNAMENT_CHAPTER_TABLE, t_snapshot, out _error);
    }

    // 자격·설정을 SpecData 업로드와 같은 순서로 본다 — 준비를 다 하고 첫 요청에서 403으로 죽지 않게 먼저 막는다
    static bool TryBeginCompositionUpload(out string _projectId, out string _apiKey, out string _error)
    {
        _projectId = null;
        _apiKey = null;
        _error = null;

        if (!SpecAdminAuth.IsSignedIn)
        {
            _error = "관리자 로그인이 필요하다. 데이터 탭에서 로그인한 뒤 다시 시도할 것.";
            return false;
        }

        if (!SpecAdminAuth.HasAdminClaim)
        {
            _error = $"'{SpecAdminAuth.SignedInEmail}' 계정에 admin 클레임이 없다. " +
                     "스펙 쓰기는 규칙에서 거부된다 — functions/scripts/grant-admin.js 로 클레임을 부여할 것.";
            return false;
        }

        return TryReadFirebaseConfig(out _projectId, out _apiKey, out _error);
    }

    static bool TryLoadAuthoringAsset<T>(out T _asset, out string _error) where T : ScriptableObject
    {
        _asset = null;
        _error = null;

        string[] t_guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        if (t_guids.Length == 0)
        {
            _error = $"{typeof(T).Name} 애셋을 프로젝트에서 찾지 못했다.";
            return false;
        }

        var t_paths = new List<string>(t_guids.Length);
        foreach (string t_guid in t_guids) t_paths.Add(AssetDatabase.GUIDToAssetPath(t_guid));
        t_paths.Sort(StringComparer.Ordinal);

        if (t_paths.Count > 1)
        {
            _error = $"{typeof(T).Name} 애셋이 {t_paths.Count}개다 — 어느 쪽이 정본인지 알 수 없어 중단한다: " +
                     string.Join(", ", t_paths);
            return false;
        }

        _asset = AssetDatabase.LoadAssetAtPath<T>(t_paths[0]);
        if (_asset == null)
        {
            _error = $"{t_paths[0]} 을 {typeof(T).Name} 으로 읽지 못했다.";
            return false;
        }
        return true;
    }

    // 도감 칸 대조 기준. CardCatalog는 부트에서만 채워져 에디터 창에서는 비어 있으므로
    // 같은 원본(스펙시트 Card 표 · Live 채널)을 여기서 직접 읽어 같은 기준을 만든다
    static bool TryLoadLiveCardIds(out HashSet<int> _ids, out string _error)
    {
        _ids = null;
        if (!TryLoadManager(out object t_manager, out _error)) return false;

        IReadOnlyList<Card> t_rows = (t_manager as SpecDataManager)?.Card?.All;
        if (t_rows == null || t_rows.Count == 0)
        {
            _error = "스펙시트 Card 표가 비어 도감 칸을 대조할 수 없다.";
            return false;
        }

        _ids = new HashSet<int>();
        foreach (Card t_row in t_rows)
        {
            if (t_row == null || t_row.id <= 0) continue;

            // 채널 값이 깨졌으면 카드 존재 자체를 부정하지 않는다 — 그 진단은 카드 표의 몫이다
            if (Enum.TryParse(t_row.channel, true, out ECardChannel t_channel) && t_channel != ECardChannel.Live)
                continue;

            _ids.Add(t_row.id);
        }
        return true;
    }

    // 안정 키가 없거나 중복인 테마·페이지는 서버가 식별할 수 없다 — 표에 담지 않고 업로드를 멈춘다.
    // 반대로 "그 칸만 소유 불가"인 결함(id 미부여 · 카탈로그 미존재 · 페이지 내 중복)은 결과가 같으므로
    // 셋을 같은 등급으로 다룬다 — 칸을 빼서 페이지를 완성 가능하게 두고 경고로 알린다
    static bool TryBuildAlbumEntryRows(
        CardAlbumConfig _config, HashSet<int> _liveCardIds,
        out List<AlbumEntryRow> _rows, out int _skippedSlots, out string _error)
    {
        _rows = new List<AlbumEntryRow>();
        _skippedSlots = 0;
        _error = null;

        var t_themeIds = new HashSet<string>(StringComparer.Ordinal);
        var t_pageKeys = new HashSet<string>(StringComparer.Ordinal);
        int t_nextId = 1;

        IReadOnlyList<AlbumThemeDef> t_themes = _config.Themes;
        for (int t_t = 0; t_t < t_themes.Count; t_t++)
        {
            AlbumThemeDef t_theme = t_themes[t_t];
            if (string.IsNullOrEmpty(t_theme.themeId))
            {
                _error = $"themeId 미저작 테마(index {t_t}, '{t_theme.displayName}') — 서버가 식별할 키가 없다.";
                return false;
            }

            if (!t_themeIds.Add(t_theme.themeId))
            {
                _error = $"themeId 중복 '{t_theme.themeId}' — 완성 판정이 한 테마로 합쳐진다.";
                return false;
            }

            List<AlbumPageDef> t_pages = t_theme.pages;
            int t_pageCount = t_pages != null ? t_pages.Count : 0;
            if (t_pageCount == 0)
            {
                _error = $"페이지 0개 테마 '{t_theme.themeId}' — 모수가 없어 서버가 즉시 완성으로 읽는다.";
                return false;
            }

            for (int t_p = 0; t_p < t_pageCount; t_p++)
            {
                AlbumPageDef t_page = t_pages[t_p];
                if (string.IsNullOrEmpty(t_page.pageId))
                {
                    _error = $"pageId 미저작 페이지(테마 '{t_theme.themeId}' index {t_p}) — 서버가 식별할 키가 없다.";
                    return false;
                }

                if (!t_pageKeys.Add(t_theme.themeId + "/" + t_page.pageId))
                {
                    _error = $"페이지 키 중복 '{t_theme.themeId}/{t_page.pageId}' — 완성 판정이 한 페이지로 합쳐진다.";
                    return false;
                }

                IReadOnlyList<int> t_cardIds = t_page.CardIds;
                int t_slotCount = t_cardIds != null ? t_cardIds.Count : 0;
                var t_pageCards = new HashSet<int>();
                int t_order = 0;
                for (int t_c = 0; t_c < t_slotCount; t_c++)
                {
                    int t_cardId = t_cardIds[t_c];
                    string t_defect = null;
                    if (t_cardId <= 0) t_defect = "id 미부여";
                    else if (!_liveCardIds.Contains(t_cardId)) t_defect = "카드 표에 없거나 Live 채널이 아님";
                    else if (!t_pageCards.Add(t_cardId)) t_defect = "같은 페이지 안 중복";

                    if (t_defect != null)
                    {
                        _skippedSlots++;
                        Debug.LogWarning(
                            $"[SpecFirestore] {ALBUM_ENTRY_TABLE}: 칸 제외 — {t_defect} (카드 {t_cardId}, " +
                            $"테마 '{t_theme.themeId}' 페이지 '{t_page.pageId}' 칸 {t_c}). " +
                            "소유로 채울 수 없는 칸이라 표에 담으면 그 페이지가 영구 미완성이 된다.");
                        continue;
                    }

                    _rows.Add(new AlbumEntryRow
                    {
                        id = t_nextId++,
                        themeId = t_theme.themeId,
                        pageId = t_page.pageId,
                        cardId = t_cardId,
                        order = t_order++,
                    });
                }

                if (t_order == 0)
                {
                    _error = $"유효 칸 0개 페이지 '{t_theme.themeId}/{t_page.pageId}' — " +
                             "모수가 없어 서버가 즉시 완성으로 읽는다.";
                    return false;
                }
            }
        }

        if (_rows.Count == 0)
        {
            _error = "도감 저작에서 올릴 칸을 하나도 찾지 못했다(테마·페이지·카드 저작 확인).";
            return false;
        }
        return true;
    }

    // 챕터 랭크 잠금을 서버가 재는 축은 등급이 아니라 점수다 — 서버에는 ERankGrade 가 없고
    // rank.points 만 있어서, 등급 순서를 서버·클라가 따로 들면 조용히 갈린다.
    //
    // 첫 등급은 0 으로 낮춘다: RankConfig.ResolveTierIndex 가 첫 등급 진입 점수에 못 미쳐도 0 을
    // 돌려주므로 points 0 인 신규 계정도 클라에선 첫 등급으로 읽힌다. entryPoints 를 그대로 쓰면
    // 서버만 그 계정을 잠근다.
    static bool TryBuildGradeEntryPoints(
        RankConfig _config, out Dictionary<ERankGrade, int> _entryPoints, out string _error)
    {
        _entryPoints = new Dictionary<ERankGrade, int>();
        _error = null;

        List<RankGradeConfig> t_grades = _config.grades;
        if (t_grades == null || t_grades.Count == 0)
        {
            _error = "RankConfig.grades 가 비어 있다 — 챕터 잠금을 잴 기준이 없다.";
            return false;
        }

        for (int t_g = 0; t_g < t_grades.Count; t_g++)
        {
            RankGradeConfig t_grade = t_grades[t_g];
            if (t_grade == null)
            {
                _error = $"RankConfig.grades[{t_g}] 가 비었다.";
                return false;
            }

            // points 비교가 등급 비교와 등가이려면 두 축이 함께 오름차순이어야 한다.
            // 하나라도 뒤집히면 클라는 통과시키고 서버는 막는 챕터가 생긴다.
            if (t_g > 0)
            {
                RankGradeConfig t_prev = t_grades[t_g - 1];
                if (t_grade.entryPoints <= t_prev.entryPoints)
                {
                    _error = $"RankConfig.grades 의 entryPoints 가 오름차순이 아니다" +
                             $"({t_prev.grade} {t_prev.entryPoints} → {t_grade.grade} {t_grade.entryPoints}).";
                    return false;
                }
                if (t_grade.grade <= t_prev.grade)
                {
                    _error = $"RankConfig.grades 의 등급이 오름차순이 아니다" +
                             $"({t_prev.grade} → {t_grade.grade}).";
                    return false;
                }
            }

            if (!_entryPoints.ContainsKey(t_grade.grade))
                _entryPoints.Add(t_grade.grade, t_g == 0 ? 0 : t_grade.entryPoints);
        }

        return true;
    }

    static bool TryBuildTournamentChapterRows(
        TournamentConfig _config, RankConfig _rankConfig,
        out List<TournamentChapterRow> _rows, out string _error)
    {
        _rows = new List<TournamentChapterRow>();

        if (!TryBuildGradeEntryPoints(_rankConfig, out Dictionary<ERankGrade, int> t_entryPoints, out _error))
            return false;

        var t_chapterIds = new HashSet<string>(StringComparer.Ordinal);
        var t_nodeIds = new HashSet<string>(StringComparer.Ordinal);
        int t_nextId = 1;

        // 사슬은 챕터 경계를 넘는다 — 클라 StateOf 가 평탄 인덱스로 직전 하나만 보기 때문이다.
        string t_prevNodeId = string.Empty;

        IReadOnlyList<TournamentChapterDef> t_chapters = _config.Chapters;
        for (int t_c = 0; t_c < t_chapters.Count; t_c++)
        {
            TournamentChapterDef t_chapter = t_chapters[t_c];
            if (!t_chapter.HasStableKey)
            {
                _error = $"chapterId 미저작 챕터(index {t_c}, '{t_chapter.title}') — 서버가 식별할 키가 없다.";
                return false;
            }

            if (!t_chapterIds.Add(t_chapter.chapterId))
            {
                _error = $"chapterId 중복 '{t_chapter.chapterId}' — 완주 판정이 한 챕터로 합쳐진다.";
                return false;
            }

            int t_nodeCount = t_chapter.NodeCount;
            if (t_nodeCount == 0)
            {
                _error = $"정점 0개 챕터 '{t_chapter.chapterId}' — 모수가 없어 즉시 완주로 읽힌다(완주 보상 누수).";
                return false;
            }

            if (!t_entryPoints.TryGetValue(t_chapter.requiredGrade, out int t_requiredPoints))
            {
                _error = $"챕터 '{t_chapter.chapterId}' 의 requiredGrade '{t_chapter.requiredGrade}' 가 " +
                         "RankConfig.grades 에 없다 — 서버가 잠금을 잴 점수를 만들 수 없다.";
                return false;
            }

            for (int t_n = 0; t_n < t_nodeCount; t_n++)
            {
                TournamentNodeDef t_node = t_chapter.nodes[t_n];
                if (!t_node.HasStableKey)
                {
                    _error = $"nodeId 미저작 정점(챕터 '{t_chapter.chapterId}' index {t_n}) — 서버가 식별할 키가 없다.";
                    return false;
                }

                if (!t_nodeIds.Add(t_node.nodeId))
                {
                    _error = $"nodeId 중복 '{t_node.nodeId}' — 완주 판정 모수가 어긋난다.";
                    return false;
                }

                _rows.Add(new TournamentChapterRow
                {
                    id = t_nextId++,
                    chapterId = t_chapter.chapterId,
                    nodeId = t_node.nodeId,
                    order = t_n,
                    prevNodeId = t_prevNodeId,
                    requiredPoints = t_requiredPoints,
                });

                t_prevNodeId = t_node.nodeId;
            }
        }

        if (_rows.Count == 0)
        {
            _error = "토너먼트 저작에서 올릴 정점을 하나도 찾지 못했다(챕터·정점 저작 확인).";
            return false;
        }
        return true;
    }
}
