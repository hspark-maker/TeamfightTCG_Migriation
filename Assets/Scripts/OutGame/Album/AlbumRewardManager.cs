using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

// 앨범 3단(페이지·테마·앨범) 완성 보상의 static 수령 창구 — 캐시·Init 없이 세이브 슬롯 직독(초기화 무접촉)
public static class AlbumRewardManager
{
    // 앨범 전체 보상의 낙인 키 — 계층 낙인 키의 유일한 예외 상수(그 외 조립은 AlbumSection 파생만)
    const string AlbumRewardKey = "b";

    // 수령 통지 — 패널이 보상 상태를 다시 그리는 트리거
    public static event Action OnChanged;

    // 세이브 슬롯 직독 — 캐시를 두면 초기화를 안 거친 씬에서 빈 낙인이 기존 기록을 덮어쓴다
    static AlbumRewardSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.AlbumReward == null) t_data.AlbumReward = new AlbumRewardSaveData();
            return t_data.AlbumReward;
        }
    }

    public static AlbumRewardInfo GetPageInfo(AlbumPage _page) => InfoOf(_page);

    public static AlbumRewardInfo GetThemeInfo(AlbumTheme _theme) => InfoOf(_theme);

    // 앨범 진행도는 완성 테마 수 기준(n/N = 완성 테마/열린 테마)
    public static AlbumRewardInfo GetAlbumInfo()
    {
        var t_rewards = CardAlbum.AlbumRewards;
        return new AlbumRewardInfo(
            t_rewards,
            CardAlbum.CompletedThemeCount, CardAlbum.UnlockedThemeCount,
            AlbumState());
    }

    public static bool CanClaimPage(AlbumPage _page) => StateOf(_page) == EAlbumRewardState.Claimable;

    public static bool CanClaimTheme(AlbumTheme _theme) => StateOf(_theme) == EAlbumRewardState.Claimable;

    public static bool CanClaimAlbum() => AlbumState() == EAlbumRewardState.Claimable;

    /// <summary>페이지 완성 보상 수령을 서버에 요청한다. 서버가 준 목록째로 돌려준다(팝업이 이 값으로 연출을 정한다).</summary>
    public static UniTask<RewardClaimOutcome> ClaimPage(AlbumPage _page) => Claim(_page);

    /// <summary>테마 완성 보상 수령을 서버에 요청한다.</summary>
    public static UniTask<RewardClaimOutcome> ClaimTheme(AlbumTheme _theme) => Claim(_theme);

    /// <summary>앨범 전체 완성 보상 수령을 서버에 요청한다.</summary>
    public static UniTask<RewardClaimOutcome> ClaimAlbum()
    {
        if (!CanClaimAlbum()) return UniTask.FromResult(default(RewardClaimOutcome));

        return RequestClaim(AlbumRewardKey, CardAlbum.AlbumRewards);
    }

    static AlbumRewardInfo InfoOf(AlbumSection _section)
    {
        if (_section == null) return default;

        return new AlbumRewardInfo(
            _section.Rewards,
            CardAlbum.OwnedCountOf(_section), CardAlbum.TotalCountOf(_section),
            StateOf(_section));
    }

    // StateOf 선검사는 왕복을 아끼는 낙관 검사다 — 자격의 진실원은 서버이고, 여기서 통과해도 거절될 수 있다.
    static UniTask<RewardClaimOutcome> Claim(AlbumSection _section)
    {
        if (StateOf(_section) != EAlbumRewardState.Claimable) return UniTask.FromResult(default(RewardClaimOutcome));

        return RequestClaim(_section.RewardKey, _section.Rewards);
    }

    // 지급·낙인·영속은 서버가 한 트랜잭션으로 끝낸다 — 응답 채택이 재화·앨범 보상 슬롯을 통째로 갈아끼우므로
    // 클라가 여기서 더 쓸 것이 없다(낙인 키가 곧 서버 Reward.ownerId 라 변환도 없다).
    static async UniTask<RewardClaimOutcome> RequestClaim(string _rewardKey, IReadOnlyList<AlbumRewardDef> _rewards)
    {
        // 첫 await 이전에 걸어야 한다 — 뒤로 밀리면 팝업의 숫자 롤업이 옛 잔액을 목표로 잡아 역주행한다.
        var t_pending = CurrencyPendingTicket.Hold(ToGains(_rewards));

        var t_outcome = await RewardClaimCommand.ClaimAsync(RewardClaimCommand.OwnerAlbum, _rewardKey, t_pending);
        if (!t_outcome.Succeeded) return default;

        OnChanged?.Invoke();
        return t_outcome;
    }

    // 저작값 → 낙관 델타용 획득 목록. 예고와 실지급이 갈려도 보정하지 않는다 — 서버 절대값 채택이 최종 착지다.
    static List<CurrencyGain> ToGains(IReadOnlyList<AlbumRewardDef> _rewards)
    {
        var t_gains = new List<CurrencyGain>();
        if (_rewards == null) return t_gains;

        for (int t_i = 0; t_i < _rewards.Count; t_i++)
        {
            AlbumRewardDef t_def = _rewards[t_i];
            if (t_def.amount <= 0) continue;
            t_gains.Add(new CurrencyGain(t_def.currency, t_def.amount));
        }

        return t_gains;
    }

    static EAlbumRewardState StateOf(AlbumSection _section)
        => _section != null
            ? StateOf(_section.RewardKey, CardAlbum.IsComplete(_section), _section.Rewards.Count > 0)
            : EAlbumRewardState.Locked;

    static EAlbumRewardState AlbumState()
        => StateOf(AlbumRewardKey, CardAlbum.IsAlbumComplete, CardAlbum.AlbumRewards.Count > 0);

    // Claimed 검사가 먼저여야 한다 — 완성 취소 후 재완성 구간에서 재수령이 뚫린다.
    // 빈 보상 리스트는 Claimable 불성립(줄 게 없는 낙인 소모 방지) — 기수령 낙인은 보상이 비어도 Claimed
    static EAlbumRewardState StateOf(string _rewardKey, bool _complete, bool _hasReward)
    {
        if (string.IsNullOrEmpty(_rewardKey)) return EAlbumRewardState.Locked;
        if (Slot.ClaimedKeys.Contains(_rewardKey)) return EAlbumRewardState.Claimed;
        return _complete && _hasReward ? EAlbumRewardState.Claimable : EAlbumRewardState.Locked;
    }
}

// 앨범 보상 상태(3종 배타)
public enum EAlbumRewardState
{
    Locked,
    Claimable,
    Claimed,
}
