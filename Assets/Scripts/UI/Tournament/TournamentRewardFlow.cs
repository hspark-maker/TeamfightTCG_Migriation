using System;
using System.Collections.Generic;

// 정점 클리어 보상의 수령 흐름(앨범 3단 수령과 같은 순서를 따른다).
//
// 지급은 하지 않는다 — 팝업의 확인이 _onConfirm을 부르고 그 안에서 TournamentProgress.ClearNode가
// 자격 판정 · 지급 · 낙인 · 영속을 한 트랜잭션으로 끝낸다. 팝업은 그 반환값을 보고 연출 여부를 정한다.
public static class TournamentRewardFlow
{
    const string TITLE_SUFFIX   = " 격파";
    const string TITLE_FALLBACK = "정점 클리어";

    /// <summary>
    /// 보상 팝업을 연다. <b>팝업이 실제로 떴는지</b>를 돌려준다 —
    /// 폴백(보상 0건·팝업 미배선)으로 지급만 끝난 경우 _onClosed가 영영 오지 않으므로 호출부가 알아야 한다.
    /// </summary>
    public static bool Open(string _nodeId, Action _onClosed = null)
    {
        if (string.IsNullOrEmpty(_nodeId)) return false;

        // 재도전 승리는 지급이 없다 — 정점 보상은 최초 1회다. 빈 상자를 세우지 않게 팝업 이전에 끊는다.
        if (TournamentProgress.IsCleared(_nodeId)) return false;

        int t_index = TournamentProgress.IndexOf(_nodeId);

        // 매번 새 리스트 — 팝업이 Show 시점 스냅샷을 들고 있다가 나중에 소비하므로 공용 버퍼를 넘기면 stale이 된다.
        var t_lines = new List<RewardLine>();
        TournamentProgress.FillRewards(t_index, t_lines);

        // 보여줄 것이 없으면 팝업을 세우지 않는다. 보상 미저작 정점은 해금만 넘기는 것이 저작 규약이고,
        // 빈 목록으로 열면 제목만 있는 빈 상자가 뜬다(앨범은 빈 보상을 Claimable로 치지 않아 이 경우가 없다).
        // 팝업이 씬에 없을 때도 같은 자리로 떨어진다(앨범·랭크와 같은 폴백 — 배선 전에도 루프가 닫히도록).
        if (t_lines.Count == 0 || !RewardClaimPopup.TryGet(out var t_popup))
        {
            TournamentProgress.ClearNode(_nodeId);
            return false;
        }

        // 랭크·앨범과 같은 규약 — [획득] 버튼 없이 배경을 눌러 받는다.
        t_popup.Show(TitleOf(t_index), t_lines, () => TournamentProgress.ClearNode(_nodeId),
                     _claimOnDim: true, _onClosed: _onClosed);
        return true;
    }

    // 표시명이 비었거나 저작에서 사라진 정점이면 상대를 특정하지 않는 문구로 내려간다(빈 제목으로 새지 않게).
    static string TitleOf(int _index)
    {
        if (!TournamentProgress.TryGetNode(_index, out TournamentNodeDef t_node)
            || string.IsNullOrEmpty(t_node.displayName))
            return TITLE_FALLBACK;

        return t_node.displayName + TITLE_SUFFIX;
    }
}
