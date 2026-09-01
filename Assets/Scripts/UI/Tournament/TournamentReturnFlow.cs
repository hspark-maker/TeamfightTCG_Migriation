using System;
using Cysharp.Threading.Tasks;

// 정점 전투 결과를 로비에서 소비해 "떠났던 화면으로 돌아간다"로 잇는 진입점.
//
// 화면 복원과 보상 수령은 박자가 다르다 — 복원은 로비가 서는 즉시(첫 프레임부터 맵이 떠 있어야 한다),
// 수령은 서버가 낙인을 세운 뒤다. 자격을 재는 쪽이 서버라, 그전에 팝업을 열면 눌러도 튕긴다.
// 그 사이에 다른 연출을 끼우지 않는다 — 유저가 방금 이긴 정점의 보상은 복귀의 결말이지 별도의 사건이 아니다.
//
// 팝업을 세우는 일은 맵이 진다(TournamentMapOverlayView.OpenReturnReward).
public static class TournamentReturnFlow
{
    /// <summary>화면 복원 요청(탭 + 맵). (정점 키, 승리 여부)</summary>
    public static event Action<string, bool> ReturnRequested;

    /// <summary>보상 팝업 요청(승리만). 서버 낙인이 선 뒤에 온다.</summary>
    public static event Action<string> RewardClaimRequested;

    /// <summary>로비가 선 직후 1회. 캐리어를 비우고 화면을 되돌린 뒤 곧바로 수령으로 잇는다.</summary>
    public static void Restore()
    {
        if (!TournamentResultHandoff.TryConsume(out string t_nodeId, out bool t_won)) return;

        ReturnRequested?.Invoke(t_nodeId, t_won);

        if (t_won) ClaimWhenReported(t_nodeId).Forget();
    }

    // 낙인이 서기 전에 팝업을 열면 수령이 튕긴다. 전투 씬이 이미 한 번 신고했으므로 대개 낙인은 서 있고,
    // 그럴 때는 왕복을 걸지 않고 그 프레임에 연다 — 이것이 "복귀하면 곧바로"를 지키는 자리다.
    // 서 있지 않다면 그때 네트워크가 없었다는 뜻이라, 이 두 번째 신고가 그 자리를 메운다.
    static async UniTaskVoid ClaimWhenReported(string _nodeId)
    {
        if (!IsRewardPending(_nodeId)) await TournamentWinCommand.ReportWinAsync(_nodeId);

        // 신고가 실패해도 조건 없이 낸다 — 이 신호가 수령을 여는 유일한 열쇠라,
        // 걸러 버리면 그 정점은 손으로 눌러야만 받을 수 있는 자리로 남는다.
        RewardClaimRequested?.Invoke(_nodeId);
    }

    // 진행도는 인덱스로 묻는다 — 캐리어가 나르는 것은 정점 키뿐이라 여기서 옮긴다.
    static bool IsRewardPending(string _nodeId)
    {
        int t_index = TournamentProgress.IndexOf(_nodeId);

        return t_index >= 0 && TournamentProgress.IsRewardPending(t_index);
    }
}
