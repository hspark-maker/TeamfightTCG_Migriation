// 매칭이 확정한 상대를 덱 화면·전투까지 실어 나르는 씬 캐리어(자체 세이브 없음).
// 읽는 쪽은 아직 없다 — 덱 화면에 상대 닉네임을 붙일 때가 첫 소비처다(그때까지는 싣기만 한다).
public static class MatchOpponentHandoff
{
    static MatchOpponent? s_pending;

    // 상대 싣기(매칭이 확정한 뒤 LobbyMatchLauncher.ConfirmOpponent 한 곳에서만)
    public static void Set(in MatchOpponent _opponent)
    {
        s_pending = _opponent;
    }

    /// <summary>실린 상대를 읽는다. **소비하지 않는다** — 소비처가 될 MatchDeckPanelView.Render는 편집 화면을
    /// 오갈 때마다 다시 그려서, 1회 소비면 두 번째 렌더에 상대 이름이 사라진다. 수명은 TurnRunner.Cleanup의 Clear가 끊는다.</summary>
    public static bool TryGet(out MatchOpponent _opponent)
    {
        if (!s_pending.HasValue)
        {
            _opponent = default;
            return false;
        }

        _opponent = s_pending.Value;
        return true;
    }

    public static void Clear() => s_pending = null;
}
