using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

// 앨범 3단(페이지·테마·앨범) 완성 보상의 수령 흐름. 세 지점이 같은 순서를 밟아야 하므로 한 곳에 둔다.
//
// 지급은 하지 않는다 — 팝업의 [획득]이 _onConfirm을 부르고 그 안에서 AlbumRewardManager가 서버에 묻는다.
// 그래서 팝업이 뜬 사이 상태가 바뀌면 서버가 거절하고, 팝업은 그 반환값을 보고 연출 여부를 정한다.
public static class AlbumRewardClaimFlow
{
    /// <summary>보상 팝업을 연다. 폴백(팝업 미배선)으로 수령만 끝나는 경우가 있어 완료를 기다릴 수 있게 대기를 돌려준다.</summary>
    public static async UniTask Open(string _title, IReadOnlyList<AlbumRewardDef> _rewards,
                                     Func<UniTask<bool>> _onConfirm)
    {
        // 팝업이 씬에 없으면 확인 없이 바로 수령한다(랭크와 같은 폴백 — 배선 전에도 루프가 닫히도록).
        if (!RewardClaimPopup.TryGet(out var t_popup))
        {
            // 폴백도 서버 응답을 기다린다 — 던져 두면 낙인이 서기 전에 호출부가 다음 연출을 이어 붙인다.
            if (_onConfirm != null) await _onConfirm.Invoke();
            return;
        }

        // 랭크 보상과 같은 규약 — [획득] 버튼 없이 **배경을 눌러** 받는다.
        // 세 단(페이지·테마·앨범)이 여기 한 줄을 공유하므로 수령 조작이 갈릴 여지가 없다.
        t_popup.Show(_title, ToLines(_rewards), _onConfirm, _claimOnDim: true);
    }

    // 매번 새 리스트 — 팝업이 Show 시점 스냅샷을 들고 있다가 나중에 소비하므로 공용 버퍼를 돌려주면 stale이 된다.
    static List<RewardLine> ToLines(IReadOnlyList<AlbumRewardDef> _rewards)
    {
        var t_lines = new List<RewardLine>();
        if (_rewards == null) return t_lines;

        for (int t_i = 0; t_i < _rewards.Count; t_i++)
        {
            // 0짜리는 칸만 잡는다 — 표시에서 뺀다(실제 지급 목록은 서버가 정한다).
            if (_rewards[t_i].amount <= 0) continue;

            t_lines.Add(new RewardLine(new CurrencyGain(_rewards[t_i].currency, _rewards[t_i].amount)));
        }

        return t_lines;
    }
}
