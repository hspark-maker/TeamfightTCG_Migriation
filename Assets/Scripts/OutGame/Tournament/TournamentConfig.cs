using System;
using System.Collections.Generic;
using UnityEngine;

// 모험 저작 데이터 — 챕터 → 정점 2계층의 단일 진실원.
// 소비처가 쓰는 평탄 정점 인덱스는 전부 여기서 파생한다(챕터 경계는 이 애셋만 안다).
[CreateAssetMenu(fileName = "TournamentConfig", menuName = "Card Battle/Tournament Config")]
public class TournamentConfig : ScriptableObject
{
    [Header("챕터 목록 (순서 = 진행 순서, 아래로 갈수록 뒤)")]
    [SerializeField] List<TournamentChapterDef> chapters = new List<TournamentChapterDef>();

    // 평탄화 캐시 — 최초 접근 시 구축, OnValidate에서 무효화
    bool m_built;
    List<TournamentChapterDef> m_chapters;
    List<TournamentNodeDef> m_nodes;
    List<int> m_chapterStarts;
    List<int> m_nodeChapters;
    Dictionary<string, int> m_nodeIndexById;
    Dictionary<string, int> m_chapterIndexById;

    // 전체 정점 수(챕터를 가로지른 합). 소비처는 맵 셀 수를 이 값에서 파생한다
    public int NodeCount
    {
        get
        {
            EnsureBuilt();
            return m_nodes.Count;
        }
    }

    public IReadOnlyList<TournamentNodeDef> Nodes
    {
        get
        {
            EnsureBuilt();
            return m_nodes;
        }
    }

    // 챕터 수
    public int ChapterCount
    {
        get
        {
            EnsureBuilt();
            return m_chapters.Count;
        }
    }

    public IReadOnlyList<TournamentChapterDef> Chapters
    {
        get
        {
            EnsureBuilt();
            return m_chapters;
        }
    }

    // 정점 저작값 조회(범위 밖이면 false + 빈 값)
    public bool TryGetNode(int _index, out TournamentNodeDef _node)
    {
        EnsureBuilt();

        _node = default;
        if (_index < 0 || _index >= m_nodes.Count) return false;

        _node = m_nodes[_index];
        return true;
    }

    // 안정 키 → 평탄 정점 인덱스(미저작·미존재는 -1)
    public int IndexOf(string _nodeId)
    {
        EnsureBuilt();

        if (string.IsNullOrEmpty(_nodeId)) return -1;

        return m_nodeIndexById.TryGetValue(_nodeId, out int t_index) ? t_index : -1;
    }

    // 챕터 저작값 조회(범위 밖이면 false + 빈 값)
    public bool TryGetChapter(int _chapterIndex, out TournamentChapterDef _chapter)
    {
        EnsureBuilt();

        _chapter = default;
        if (_chapterIndex < 0 || _chapterIndex >= m_chapters.Count) return false;

        _chapter = m_chapters[_chapterIndex];
        return true;
    }

    // 평탄 정점 인덱스 → 그 정점이 속한 챕터 인덱스(범위 밖은 -1)
    public int ChapterIndexOfNode(int _nodeIndex)
    {
        EnsureBuilt();

        if (_nodeIndex < 0 || _nodeIndex >= m_nodeChapters.Count) return -1;

        return m_nodeChapters[_nodeIndex];
    }

    // 챕터가 차지하는 평탄 인덱스 구간(범위 밖이면 false + 0)
    public bool TryGetNodeRange(int _chapterIndex, out int _start, out int _count)
    {
        EnsureBuilt();

        _start = 0;
        _count = 0;
        if (_chapterIndex < 0 || _chapterIndex >= m_chapters.Count) return false;

        _start = m_chapterStarts[_chapterIndex];
        _count = m_chapters[_chapterIndex].NodeCount;
        return true;
    }

    // 챕터 안정 키 → 인덱스(미저작·미존재는 -1)
    public int ChapterIndexOf(string _chapterId)
    {
        EnsureBuilt();

        if (string.IsNullOrEmpty(_chapterId)) return -1;

        return m_chapterIndexById.TryGetValue(_chapterId, out int t_index) ? t_index : -1;
    }

    /// <summary>정점 _nodeId의 보상을 _sink에 담는다(Clear는 이 메서드가 한다).
    /// 저작값(<see cref="AlbumRewardDef"/>) → 공용 <see cref="RewardLine"/> 변환의 표준 지점 —
    /// 소비처가 저작 포맷을 직접 읽지 않게 한다.</summary>
    public void FillRewards(string _nodeId, List<RewardLine> _sink)
    {
        if (_sink == null) return;
        _sink.Clear();

        if (TournamentSpec.TryGetRewards(_nodeId, out List<AlbumRewardDef> t_spec)) FillFrom(t_spec, _sink);
    }

    // 챕터 _chapterId의 완주 보상을 _sink에 담는다(변환 규약은 FillRewards와 동일)
    public void FillChapterRewards(string _chapterId, List<RewardLine> _sink)
    {
        if (_sink == null) return;
        _sink.Clear();

        if (TournamentSpec.TryGetRewards(_chapterId, out List<AlbumRewardDef> t_spec)) FillFrom(t_spec, _sink);
    }

    // 저작 변경 즉시 반영 — 평탄화 캐시는 스스로 갱신하지 않는다
    void OnValidate() => m_built = false;

    // 챕터 → 평탄 정점 목록·키 색인 구축
    void EnsureBuilt()
    {
        if (m_built && m_nodes != null) return;

        if (m_chapters == null) m_chapters = new List<TournamentChapterDef>();
        if (m_nodes == null) m_nodes = new List<TournamentNodeDef>();
        if (m_chapterStarts == null) m_chapterStarts = new List<int>();
        if (m_nodeChapters == null) m_nodeChapters = new List<int>();
        if (m_nodeIndexById == null) m_nodeIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        if (m_chapterIndexById == null) m_chapterIndexById = new Dictionary<string, int>(StringComparer.Ordinal);

        m_chapters.Clear();
        m_nodes.Clear();
        m_chapterStarts.Clear();
        m_nodeChapters.Clear();
        m_nodeIndexById.Clear();
        m_chapterIndexById.Clear();

        if (chapters != null) m_chapters.AddRange(chapters);

        for (int t_c = 0; t_c < m_chapters.Count; t_c++)
        {
            TournamentChapterDef t_chapter = m_chapters[t_c];

            m_chapterStarts.Add(m_nodes.Count);
            if (t_chapter.HasStableKey && !m_chapterIndexById.ContainsKey(t_chapter.chapterId))
                m_chapterIndexById.Add(t_chapter.chapterId, t_c);

            int t_count = t_chapter.NodeCount;
            for (int t_n = 0; t_n < t_count; t_n++)
            {
                TournamentNodeDef t_node = t_chapter.nodes[t_n];

                if (t_node.HasStableKey && !m_nodeIndexById.ContainsKey(t_node.nodeId))
                    m_nodeIndexById.Add(t_node.nodeId, m_nodes.Count);

                m_nodes.Add(t_node);
                m_nodeChapters.Add(t_c);
            }
        }

        // 도중에 터지면 반쯤 지어진 캐시가 완성으로 굳는다 — 다 짓고 나서 표시한다
        m_built = true;
    }

    // 액수 0 이하는 지급도 표시도 되지 않으므로 담지 않는다
    static void FillFrom(List<AlbumRewardDef> _rewards, List<RewardLine> _sink)
    {
        if (_rewards == null) return;

        for (int t_i = 0; t_i < _rewards.Count; t_i++)
        {
            AlbumRewardDef t_def = _rewards[t_i];
            if (t_def.amount <= 0) continue;

            _sink.Add(new RewardLine(new CurrencyGain(t_def.currency, t_def.amount)));
        }
    }

#if UNITY_EDITOR
    [ContextMenu("모험 저작 검증")]
    void ValidateTournament() => TournamentValidator.Validate(this);
#endif
}
