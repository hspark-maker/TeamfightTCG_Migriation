using System;
using System.Collections.Generic;
using UnityEngine;

public enum ERewardOwnerType { Album, Adventure, Rank, Battle }
public enum ERewardType { Currency }

// 모든 정적 보상의 단일 조회 창구. 수령 여부는 각 기능의 기존 세이브 키가 계속 소유한다.
public static class RewardSpec
{
    static bool s_loaded;
    static readonly Dictionary<string, List<AlbumRewardDef>> s_rewards =
        new Dictionary<string, List<AlbumRewardDef>>(StringComparer.Ordinal);

    /// <summary>전투 보상 계수의 ownerId. 이 셋은 없으면 게임이 매판 0을 지급하므로 초기화에서 존재를 검사한다.</summary>
    public const string BattleWinPerCard = "win.perCard";
    public const string BattleWinFloor   = "win.floor";
    public const string BattleLoseFlat   = "lose.flat";

    public static void Init() => EnsureLoaded();

    /// <summary>보상 행 하나를 값으로 꺼낸다(같은 ownerId에 여러 줄이면 첫 줄).
    /// Battle 행처럼 "지급량이 아니라 계수"인 자리에서 쓴다.</summary>
    public static bool TryGetSingle(ERewardOwnerType _ownerType, string _ownerId, out AlbumRewardDef _def)
    {
        _def = default;
        if (!TryGetRewards(_ownerType, _ownerId, out List<AlbumRewardDef> t_rewards) || t_rewards.Count == 0)
            return false;

        _def = t_rewards[0];
        return true;
    }

    /// <summary>표가 통째로 비었거나 전투 보상 계수가 빠졌는가. 초기화가 이걸 보고 복구 화면으로 보낸다 —
    /// 조용히 0을 지급하면 유저 신고가 오기 전까지 아무도 모른다.</summary>
    public static bool TryValidateRequired(out string _error)
    {
        EnsureLoaded();

        if (s_rewards.Count == 0)
        {
            _error = "Reward 표가 비어 있다(스펙 로드 실패).";
            return false;
        }

        foreach (string t_ownerId in new[] { BattleWinPerCard, BattleWinFloor, BattleLoseFlat })
        {
            if (TryGetSingle(ERewardOwnerType.Battle, t_ownerId, out _)) continue;

            _error = $"Reward 표에 Battle/{t_ownerId} 행이 없다.";
            return false;
        }

        _error = null;
        return true;
    }

    public static bool TryGetRewards(ERewardOwnerType _ownerType, string _ownerId, out List<AlbumRewardDef> _rewards)
    {
        EnsureLoaded();
        _rewards = null;
        return !string.IsNullOrEmpty(_ownerId)
            && s_rewards.TryGetValue(KeyOf(_ownerType, _ownerId), out _rewards);
    }

    public static bool TryConvert(string _currency, long _amount, string _where, out AlbumRewardDef _def)
    {
        _def = default;
        if (!CurrencyCode.TryParse(_currency, out ECurrencyType t_type))
        {
            Debug.LogWarning($"[RewardSpec] {_where}: 알 수 없는 재화 '{_currency}' 행을 건너뜁니다.");
            return false;
        }
        if (_amount <= 0) return false;
        _def.currency = t_type;
        _def.amount = _amount;
        return true;
    }

    static string KeyOf(ERewardOwnerType _ownerType, string _ownerId)
        => string.Concat(_ownerType.ToString(), "\n", _ownerId);

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;

        IReadOnlyList<Reward> t_rows = SpecSource.Manager?.Reward?.All;
        if (t_rows == null) return;

        var t_sorted = new List<Reward>(t_rows);
        t_sorted.Sort(CompareRows);
        string t_lastOrderKey = null;

        for (int t_i = 0; t_i < t_sorted.Count; t_i++)
        {
            Reward t_row = t_sorted[t_i];
            if (t_row == null || string.IsNullOrEmpty(t_row.ownerId)) continue;

            if (!Enum.TryParse(t_row.ownerType, false, out ERewardOwnerType t_ownerType))
            {
                Debug.LogWarning($"[RewardSpec] Reward id {t_row.id}: 알 수 없는 ownerType '{t_row.ownerType}' 행을 건너뜁니다.");
                continue;
            }
            if (!Enum.TryParse(t_row.rewardType, false, out ERewardType t_rewardType)
                || t_rewardType != ERewardType.Currency)
            {
                Debug.LogWarning($"[RewardSpec] Reward id {t_row.id}: 지원하지 않는 rewardType '{t_row.rewardType}' 행을 건너뜁니다.");
                continue;
            }

            string t_orderKey = string.Concat(KeyOf(t_ownerType, t_row.ownerId), "\n", t_row.order.ToString());
            if (string.Equals(t_lastOrderKey, t_orderKey, StringComparison.Ordinal))
            {
                Debug.LogError($"[RewardSpec] {t_row.ownerType}/{t_row.ownerId}: order {t_row.order}가 중복되어 Reward id {t_row.id}를 건너뜁니다.");
                continue;
            }
            t_lastOrderKey = t_orderKey;

            if (!TryConvert(t_row.rewardId, t_row.amount, $"Reward id {t_row.id}", out AlbumRewardDef t_def)) continue;
            string t_key = KeyOf(t_ownerType, t_row.ownerId);
            if (!s_rewards.TryGetValue(t_key, out List<AlbumRewardDef> t_list))
                s_rewards[t_key] = t_list = new List<AlbumRewardDef>();
            t_list.Add(t_def);
        }
    }

    static int CompareRows(Reward _a, Reward _b)
    {
        int t_owner = string.CompareOrdinal(_a?.ownerType, _b?.ownerType);
        if (t_owner != 0) return t_owner;
        int t_id = string.CompareOrdinal(_a?.ownerId, _b?.ownerId);
        if (t_id != 0) return t_id;
        int t_order = (_a?.order ?? 0).CompareTo(_b?.order ?? 0);
        return t_order != 0 ? t_order : (_a?.id ?? 0).CompareTo(_b?.id ?? 0);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_loaded = false;
        s_rewards.Clear();
    }
}
