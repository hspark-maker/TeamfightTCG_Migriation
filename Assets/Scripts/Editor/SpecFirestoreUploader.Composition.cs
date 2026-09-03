using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 모험 구성(SO 저작)을 스펙 표로 옮겨 Firestore에 올린다.
/// 서버가 "챕터 완주"를 판정할 근거 표이며, 커밋 규약은 SpecData 표와 같은 경로를 그대로 탄다.
/// </summary>
public static partial class SpecFirestoreUploader
{
    /// <summary>모험 구성 표 이름(챕터 → 정점).</summary>
    public const string ADVENTURE_CHAPTER_TABLE = "AdventureChapter";

    // 서버 adventureTable.MAX_NODE_ID_LENGTH 와 같은 값이어야 한다 — 넘는 키는 서버 정제가
    // 조용히 버려서, 그 정점이 clearedNodeIds 에 있으면 슬롯 전체 쓰기가 기록을 지운다.
    const int MAX_ADVENTURE_ID_LENGTH = 64;

    // 열 순서 = 필드 선언 순서다(TryBuildSnapshotFrom이 GetFields로 읽는다). 첫 열은 반드시 int id.
    // id는 순회 위치에서 파생하는 일련번호라 안정 키가 아니다 — 소비는 blob payload뿐이고
    // rows/{id} 문서 경로를 참조 키로 쓰면 앞에 행 하나만 끼워도 가리키는 대상이 바뀐다
    sealed class AdventureChapterRow
    {
        public int id;
        public string chapterId;
        public string nodeId;
        public int order;
        public string prevNodeId;
        public long requiredPoints;
        public string aiDeckId;
        public int aiCardLevel;
    }

    /// <summary>AdventureConfig 저작을 AdventureChapter 표로 올린다. 성공하면 보고 줄을, 실패하면 null과 _error를 준다.</summary>
    public static string UploadAdventureChapters(string _envId, out string _error)
    {
        if (!TryBeginCompositionUpload(out string t_projectId, out string t_apiKey, out _error)) return null;
        if (!TryLoadAuthoringAsset<AdventureConfig>(out AdventureConfig t_config, out _error)) return null;
        if (!TryLoadAuthoringAsset<RankConfig>(out RankConfig t_rankConfig, out _error)) return null;
        if (!TryBuildAdventureChapterRows(t_config, t_rankConfig, out List<AdventureChapterRow> t_rows, out _error)) return null;
        if (!TryBuildSnapshotFrom(t_rows, ADVENTURE_CHAPTER_TABLE, out TableSnapshot t_snapshot, out _error)) return null;

        return UploadSnapshot(t_projectId, t_apiKey, _envId, ADVENTURE_CHAPTER_TABLE, t_snapshot, out _error);
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

    // 챕터 랭크 잠금을 서버가 재는 축은 등급이 아니라 점수다 — 서버에는 ERankGrade 가 없고
    // rank.points 만 있어서, 등급 순서를 서버·클라가 따로 들면 조용히 갈린다.
    //
    // 첫 등급은 0 으로 낮춘다: RankConfig.ResolveTierIndex 가 첫 등급 진입 점수에 못 미쳐도 0 을
    // 돌려주므로 points 0 인 신규 계정도 클라에선 첫 등급으로 읽힌다. entryPoints 를 그대로 쓰면
    // 서버만 그 계정을 잠근다.
    static bool TryBuildGradeEntryPoints(
        RankConfig _config, out Dictionary<ERankGrade, long> _entryPoints, out string _error)
    {
        _entryPoints = new Dictionary<ERankGrade, long>();
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

    static bool TryBuildAdventureChapterRows(
        AdventureConfig _config, RankConfig _rankConfig,
        out List<AdventureChapterRow> _rows, out string _error)
    {
        _rows = new List<AdventureChapterRow>();

        if (!TryBuildGradeEntryPoints(_rankConfig, out Dictionary<ERankGrade, long> t_entryPoints, out _error))
            return false;

        var t_chapterIds = new HashSet<string>(StringComparer.Ordinal);
        var t_nodeIds = new HashSet<string>(StringComparer.Ordinal);
        int t_nextId = 1;

        // 사슬은 챕터 경계를 넘는다 — 클라 StateOf 가 평탄 인덱스로 직전 하나만 보기 때문이다.
        string t_prevNodeId = string.Empty;

        IReadOnlyList<AdventureChapterDef> t_chapters = _config.Chapters;
        for (int t_c = 0; t_c < t_chapters.Count; t_c++)
        {
            AdventureChapterDef t_chapter = t_chapters[t_c];
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

            if (t_chapter.chapterId.Length > MAX_ADVENTURE_ID_LENGTH)
            {
                _error = $"chapterId '{t_chapter.chapterId}' 가 {MAX_ADVENTURE_ID_LENGTH}자를 넘는다 — " +
                         "서버 정제가 이 키를 버려 완주 수령이 막힌다.";
                return false;
            }

            int t_nodeCount = t_chapter.NodeCount;
            if (t_nodeCount == 0)
            {
                _error = $"정점 0개 챕터 '{t_chapter.chapterId}' — 모수가 없어 즉시 완주로 읽힌다(완주 보상 누수).";
                return false;
            }

            if (!t_entryPoints.TryGetValue(t_chapter.requiredGrade, out long t_requiredPoints))
            {
                _error = $"챕터 '{t_chapter.chapterId}' 의 requiredGrade '{t_chapter.requiredGrade}' 가 " +
                         "RankConfig.grades 에 없다 — 서버가 잠금을 잴 점수를 만들 수 없다.";
                return false;
            }

            for (int t_n = 0; t_n < t_nodeCount; t_n++)
            {
                AdventureNodeDef t_node = t_chapter.nodes[t_n];
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

                if (t_node.nodeId.Length > MAX_ADVENTURE_ID_LENGTH)
                {
                    _error = $"nodeId '{t_node.nodeId}' 가 {MAX_ADVENTURE_ID_LENGTH}자를 넘는다 — " +
                             "서버 정제가 이 키를 버려 격파 신고가 막히고 클리어 기록도 지워진다.";
                    return false;
                }

                // 전투 수치는 이 표가 진실원이다(AdventureNodeSpec 이 여기서 읽어 런타임 설정을 만든다).
                // 빼먹고 올리면 다음 초기화에서 전 유저가 복구 화면에 갇힌다 — 그래서 업로드를 막는다.
                if (!t_node.HasAiDeckKey)
                {
                    _error = $"aiDeckId 미저작 정점 '{t_node.nodeId}' — 서버 표에 상대 덱 키가 비면 " +
                             "클라 초기화가 이 표를 거부한다(AdventureNodeSpec). AIDeck 표의 덱 키를 저작할 것.";
                    return false;
                }

                _rows.Add(new AdventureChapterRow
                {
                    id = t_nextId++,
                    chapterId = t_chapter.chapterId,
                    nodeId = t_node.nodeId,
                    order = t_n,
                    prevNodeId = t_prevNodeId,
                    requiredPoints = t_requiredPoints,
                    aiDeckId = t_node.aiDeckId,
                    aiCardLevel = t_node.AiCardLevelOrBase,
                });

                t_prevNodeId = t_node.nodeId;
            }
        }

        if (_rows.Count == 0)
        {
            _error = "모험 저작에서 올릴 정점을 하나도 찾지 못했다(챕터·정점 저작 확인).";
            return false;
        }
        return true;
    }
}
