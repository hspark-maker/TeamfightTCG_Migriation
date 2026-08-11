using System;
using System.Collections.Generic;

// 앨범 3단(페이지·테마·앨범) 완성 보상의 수령 흐름. 세 지점이 같은 순서를 밟아야 하므로 한 곳에 둔다.
//
// 지급은 하지 않는다 — 팝업의 [획득]이 _onConfirm을 부르고 그 안에서 AlbumRewardManager가 준다.
// 그래서 팝업이 뜬 사이 상태가 바뀌면 매니저 가드가 잡고, 팝업은 그 반환값을 보고 연출 여부를 정한다.
public static class AlbumRewardClaimFlow
{
    public static void Open(string _title, IReadOnlyList<AlbumRewardDef> _rewards, Func<bool> _onConfirm)
    {
        // 팝업이 씬에 없으면 확인 없이 바로 수령한다(랭크와 같은 폴백 — 배선 전에도 루프가 닫히도록).
        if (!RewardClaimPopup.TryGet(out var t_popup))
        {
            _onConfirm?.Invoke();
            return;
        }

        t_popup.Show(_title, ToLines(_rewards), _onConfirm);
    }

    // 매번 새 리스트 — 팝업이 Show 시점 스냅샷을 들고 있다가 나중에 소비하므로 공용 버퍼를 돌려주면 stale이 된다.
    static List<RewardLine> ToLines(IReadOnlyList<AlbumRewardDef> _rewards)
    {
        var t_lines = new List<RewardLine>();
        if (_rewards == null) return t_lines;

        for (int t_i = 0; t_i < _rewards.Count; t_i++)
        {
            // 0짜리는 칸만 잡는다(AlbumRewardManager.Claim도 같은 기준으로 건너뛴다).
            if (_rewards[t_i].amount <= 0) continue;

            t_lines.Add(new RewardLine(new CurrencyGain(_rewards[t_i].currency, _rewards[t_i].amount),
                                       _rewards[t_i].icon));
        }

        return t_lines;
    }
}
