#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>플레이 종료까지 계정 쓰기를 봉인하는 온보딩 연습 세션.</summary>
public static class OnboardingPlayTest
{
    const long TEST_BALANCE = 100000;

    public static bool IsActive { get; private set; }
    public static bool IsPreparing { get; private set; }

    /// <summary>현재 계정의 메모리 사본으로 전환하고 가상 재화를 준비한다.</summary>
    public static async UniTask BeginAsync()
    {
        if (!Application.isPlaying || !GameInitialization.IsReady)
            throw new InvalidOperationException("Play 모드의 로비 초기화 후 온보딩 테스트를 시작하세요.");
        if (IsActive) return;
        if (IsPreparing || ServerSaveCommands.IsInFlight)
            throw new InvalidOperationException("서버 요청이 끝난 뒤 온보딩 테스트를 시작하세요.");

        IsPreparing = true;
        try
        {
            await PlayerSaveCloud.SuspendUploadsAsync();
            // 이후 준비가 실패해도 Play 종료 전에는 실제 계정 쓰기를 다시 열지 않는다.
            IsActive = true;
            UserSaveData t_copy = JsonConvert.DeserializeObject<UserSaveData>(
                DataSaveManager.CreateSnapshot(), DataSaveManager.SaveSerializerSettings);
            DataSaveManager.AdoptRemote(t_copy);
            OwnershipManager.Init();
            KeywordGrowthManager.Init();
            PrepareGrowth(false);
            DeckSaveManager.LoadFromSave();
            Dictionary<string, long> t_balances = new Dictionary<string, long>();
            for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
                t_balances[((ECurrencyType)t_i).ToString()] = TEST_BALANCE;
            CurrencyManager.ClearPending();
            CurrencyManager.Adopt(t_balances);
        }
        finally
        {
            IsPreparing = false;
        }
    }

    /// <summary>보유 카드 전체를 미강화 또는 2성 상태로 준비한다.</summary>
    public static void PrepareGrowth(bool _alreadyGrown)
    {
        RequireActive();
        int t_level = CardGrowth.BaseLevel;
        if (_alreadyGrown)
        {
            while (t_level < GrowthRules.MaxLevel && GrowthStar.FromLevel(t_level) < 2) t_level++;
        }
        SetOwnedGrowth(t_level);
    }

    /// <summary>보유 카드가 시너지를 사용할 수 있는 성장 상태를 준비한다.</summary>
    public static void PrepareSynergy()
    {
        RequireActive();
        DataSaveManager.Data.Ownership.CardIds = new List<int>(CardCatalog.AllIds);
        OwnershipManager.Init();
        int t_level = CardGrowth.BaseLevel;
        while (t_level < GrowthRules.MaxLevel && !GrowthRules.SynergyUnlockedAt(t_level)) t_level++;
        SetOwnedGrowth(t_level);
        DataSaveManager.Data.Deck = new DeckSaveData();
        DeckSaveManager.LoadFromSave();
    }

    /// <summary>가상 지갑에서만 강화 비용을 차감한다.</summary>
    public static void Spend(ECurrencyType _currency, long _amount)
    {
        RequireActive();
        Dictionary<string, long> t_balances = new Dictionary<string, long>();
        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
        {
            ECurrencyType t_currency = (ECurrencyType)t_i;
            t_balances[t_currency.ToString()] = CurrencyManager.GetServerBalance(t_currency)
                - (t_currency == _currency ? _amount : 0);
        }
        CurrencyManager.Adopt(t_balances);
    }

    static void SetOwnedGrowth(int _level)
    {
        CardGrowthSaveData t_growth = new CardGrowthSaveData();
        foreach (int t_id in OwnershipManager.OwnedIds)
            t_growth.Entries[t_id.ToString()] = new CardGrowthEntry { Level = _level };
        DataSaveManager.Data.CardGrowth = t_growth;
        CardGrowthManager.Init();
        CardGrowthManager.NotifyCostRuleChanged();
    }

    static void RequireActive()
    {
        if (!IsActive) throw new InvalidOperationException("온보딩 테스트 세션이 시작되지 않았습니다.");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        IsActive = false;
        IsPreparing = false;
    }
}
#endif
