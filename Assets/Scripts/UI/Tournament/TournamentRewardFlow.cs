using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

// 정점 클리어 보상의 수령 흐름(앨범 3단 수령과 같은 순서를 따른다).
//
// 지급은 하지 않는다 — 팝업의 확인이 _onConfirm을 부르고 그 안에서 TournamentProgress.ClearNodeAsync가
// 자격 판정 · 지급 · 낙인을 서버 한 트랜잭션으로 끝낸다. 팝업은 그 반환값을 보고 연출 여부를 정한다.
public static class TournamentRewardFlow
{
    const string TITLE_SUFFIX   = " 격파";
    const string TITLE_FALLBACK = "정점 클리어";

    // 대기 표시의 owner. static 흐름이라 인스턴스가 없어 타입 자체를 키로 쓴다(ServerWaitOverlay가 Type을 다룬다).
    // 정점 수령은 한 번에 하나뿐이라 같은 키가 중첩될 경로가 없다.
    static readonly object WAIT_OWNER = typeof(TournamentRewardFlow);

    /// <summary>
    /// 정점 보상 수령을 시작한다. <b>콜백이 오는지</b>를 돌려준다 —
    /// false는 시작조차 하지 않은 경우(빈 정점 id · 이미 클리어)뿐이라 호출부가 뒷정리를 해야 한다.
    /// </summary>
    /// <param name="_onClaimed">서버 응답을 채택한 직후. 이 시점에 정점은 이미 Cleared이므로 결말 연출을 여기서 잇는다.
    /// 거절돼도 부른다 — 호출부가 걸어 둔 갱신 억제를 푸는 자리가 이 콜백이다.</param>
    /// <param name="_onClosed">팝업이 닫힌 뒤. 수령 없이 외부에서 강제로 닫힌 경로의 안전망 전용이다 —
    /// 결말 연출을 여기 매달면 팝업 안무가 끝날 때까지 정점이 옛 그림으로 굳는다.</param>
    public static bool Open(string _nodeId, Action _onClaimed, Action _onClosed = null)
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
            ClaimThenNotifyAsync(_nodeId, _onClaimed).Forget();
            return true;
        }

        // 랭크·앨범과 같은 규약 — [획득] 버튼 없이 배경을 눌러 받는다.
        t_popup.Show(TitleOf(t_index), t_lines, () => ClaimThenNotifyAsync(_nodeId, _onClaimed),
                     _claimOnDim: true, _onClosed: _onClosed);
        return true;
    }

    // 왕복이 끝난 뒤에 알린다 — 던져 두면 그 자리엔 아직 RewardPending이라 호출부가 클리어를 못 본다.
    // 팝업은 이 UniTask를 기다리지 않으므로(ClaimClicked), 수령이 확정된 시점을 알릴 자리가 여기뿐이다.
    static async UniTask<RewardClaimOutcome> ClaimThenNotifyAsync(string _nodeId, Action _onClaimed)
    {
        RewardClaimOutcome t_outcome;

        // Release는 반드시 finally에서 — 한 번이라도 새면 전역 오버레이가 화면을 영영 잠근다.
        ServerWaitOverlay.Hold(WAIT_OWNER);
        try
        {
            t_outcome = await TournamentProgress.ClearNodeAsync(_nodeId);
        }
        finally
        {
            ServerWaitOverlay.Release(WAIT_OWNER);
        }

        // 대기를 걷은 "뒤"에 잇는다. 순서가 뒤집히면 차단막이 켜진 채 결말 연출이 돌아 손이 막힌다.
        _onClaimed?.Invoke();
        return t_outcome;
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
