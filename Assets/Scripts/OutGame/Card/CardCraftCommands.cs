using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

public readonly struct CardCraftRecipe
{
    public readonly int CardId;
    public readonly ECurrencyType Currency;
    public readonly long Cost;
    public bool Available => CardId > 0 && Cost > 0;

    internal CardCraftRecipe(int _cardId, ECurrencyType _currency, long _cost)
    { CardId = _cardId; Currency = _currency; Cost = _cost; }
}

public enum ECardCraftOutcome { NotReady, Success, AlreadyOwned, NotAffordable, NotCraftable, Busy, NetworkFailed, Failed }

public readonly struct CardCraftOutcome
{
    public readonly ECardCraftOutcome Outcome;
    public readonly int CardId;
    public readonly ECurrencyType Currency;
    public readonly long Cost;
    public bool Success => Outcome == ECardCraftOutcome.Success;

    internal CardCraftOutcome(ECardCraftOutcome _outcome, int _cardId, ECurrencyType _currency = default, long _cost = 0)
    { Outcome = _outcome; CardId = _cardId; Currency = _currency; Cost = _cost; }
}

/// <summary>제작 가격은 서버 조회, 지급·차감은 공용 서버 명령의 응답 채택만 따른다.</summary>
public static class CardCraftCommands
{
    const double CACHE_SECONDS = 30;
    static readonly Dictionary<int, CardCraftRecipe> s_recipes = new();
    static PlayerSaveCloud.CommandSession s_session;
    static UniTaskCompletionSource<bool> s_refresh;
    static bool s_ready;
    static bool s_crafting;
    static double s_validUntil;
    static int s_generation;

    public static event Action OnChanged;
    static bool IsCurrent => PlayerSaveCloud.IsCommandSessionCurrent(s_session) &&
        s_session.EnvId == ContentProfileConfig.Active?.CloudEnvId;
    public static bool IsReady => s_ready && IsCurrent;
    public static bool IsRefreshing => s_refresh != null && IsCurrent;
    public static bool IsCrafting => s_crafting && IsCurrent;

    public static bool TryGetRecipe(int _cardId, out CardCraftRecipe _recipe)
    {
        _recipe = default;
        return IsReady && s_recipes.TryGetValue(_cardId, out _recipe);
    }

    public static UniTask<bool> RefreshAsync(bool _force = false)
    {
        if (!PlayerSaveCloud.CanRunServerCommand) return UniTask.FromResult(false);
        try
        {
            var t_session = PlayerSaveCloud.CaptureCommandSession();
            if (t_session.EnvId != ContentProfileConfig.Active?.CloudEnvId) return UniTask.FromResult(false);
            if (!IsCurrent)
            {
                ResetSession();
                s_session = t_session;
            }
            if (s_refresh != null) return s_refresh.Task;
            if (!_force && IsReady && Time.realtimeSinceStartupAsDouble < s_validUntil)
                return UniTask.FromResult(true);
            var t_gate = new UniTaskCompletionSource<bool>();
            s_refresh = t_gate;
            NotifyChanged();
            RefreshCoreAsync(t_gate, t_session, s_generation).Forget();
            return t_gate.Task;
        }
        catch (Exception t_error)
        {
            Debug.LogWarning($"[CardCraftCommands] Recipe query unavailable — {t_error.GetBaseException().Message}");
            return UniTask.FromResult(false);
        }
    }

    static async UniTaskVoid RefreshCoreAsync(UniTaskCompletionSource<bool> _gate,
        PlayerSaveCloud.CommandSession _session, int _generation)
    {
        bool t_success = false;
        try
        {
            var t_result = await ServerSaveCommands.InvokeReadOnlyAsync<CardCraftCatalogResponse>(
                "getCardCrafting", new { env = _session.EnvId });
            if (_generation != s_generation || !PlayerSaveCloud.IsCommandSessionCurrent(_session) || !IsCurrent) return;
            if (t_result?.Cards == null || !CurrencyCode.TryParse(t_result.Currency, out var t_currency))
                throw new InvalidOperationException("Invalid crafting catalog response.");
            var t_recipes = new Dictionary<int, CardCraftRecipe>();
            foreach (var t_card in t_result.Cards)
            {
                if (t_card == null || t_card.CardId <= 0 || t_card.Cost <= 0 || t_recipes.ContainsKey(t_card.CardId))
                    throw new InvalidOperationException("Invalid or duplicate crafting recipe.");
                t_recipes.Add(t_card.CardId, new CardCraftRecipe(t_card.CardId, t_currency, t_card.Cost));
            }
            s_recipes.Clear();
            foreach (var t_pair in t_recipes) s_recipes.Add(t_pair.Key, t_pair.Value);
            s_ready = true;
            s_validUntil = Time.realtimeSinceStartupAsDouble + CACHE_SECONDS;
            t_success = true;
        }
        catch (Exception t_error)
        {
            Debug.LogWarning($"[CardCraftCommands] Recipe query failed — {t_error.GetBaseException().Message}");
            if (_generation == s_generation && IsCurrent) { s_ready = false; s_validUntil = 0; }
        }
        finally
        {
            if (ReferenceEquals(s_refresh, _gate))
            {
                s_refresh = null;
                NotifyChanged();
            }
            _gate.TrySetResult(t_success);
        }
    }

    public static async UniTask<CardCraftOutcome> CraftAsync(int _cardId)
    {
        if (!IsReady || !PlayerSaveCloud.CanRunServerCommand)
            return new CardCraftOutcome(ECardCraftOutcome.NotReady, _cardId);
        if (IsCrafting) return new CardCraftOutcome(ECardCraftOutcome.Busy, _cardId);
        if (OwnershipManager.IsOwned(_cardId)) return new CardCraftOutcome(ECardCraftOutcome.AlreadyOwned, _cardId);
        if (!TryGetRecipe(_cardId, out var t_recipe)) return new CardCraftOutcome(ECardCraftOutcome.NotCraftable, _cardId);
        if (!CurrencyManager.CanAfford(t_recipe.Currency, t_recipe.Cost))
            return new CardCraftOutcome(ECardCraftOutcome.NotAffordable, _cardId, t_recipe.Currency, t_recipe.Cost);

        int t_generation = s_generation;
        var t_session = s_session;
        CurrencyPendingTicket t_pending = null;
        s_crafting = true;
        NotifyChanged();
        try
        {
            // 첫 await 전에 표시 잔액을 예약한다. 영수증·재시도·실지급 채택은 공용 명령이 소유한다.
            t_pending = CurrencyPendingTicket.Hold(t_recipe.Currency, -t_recipe.Cost);
            var t_result = await ServerSaveCommands.InvokeAsync<CraftCardResponse>(
                "craftCard", new { env = t_session.EnvId, cardId = _cardId }, t_pending);
            if (t_generation != s_generation || !PlayerSaveCloud.IsCommandSessionCurrent(t_session) || !IsCurrent)
                return new CardCraftOutcome(ECardCraftOutcome.NotReady, _cardId);
            if (t_result == null || t_result.CardId != _cardId || t_result.Cost <= 0 ||
                !CurrencyCode.TryParse(t_result.Currency, out var t_currency))
                throw new InvalidOperationException("Invalid craft response.");
            return new CardCraftOutcome(ECardCraftOutcome.Success, t_result.CardId, t_currency, t_result.Cost);
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            var t_outcome = t_rejected.Reason switch
            {
                "AlreadyOwned" => ECardCraftOutcome.AlreadyOwned,
                "NotAffordable" => ECardCraftOutcome.NotAffordable,
                "CardNotCraftable" => ECardCraftOutcome.NotCraftable,
                _ => ECardCraftOutcome.Failed,
            };
            Debug.LogWarning($"[CardCraftCommands] Craft rejected — {t_rejected.Message}");
            return new CardCraftOutcome(t_outcome, _cardId, t_recipe.Currency, t_recipe.Cost);
        }
        catch (ServerAdoptionException t_error)
        {
            // 세션 복구 안내는 CloudSyncStatusWatcher가 담당한다.
            Debug.LogWarning($"[CardCraftCommands] Craft adoption failed — {t_error.Message}");
            return new CardCraftOutcome(ECardCraftOutcome.NotReady, _cardId);
        }
        catch (Exception t_error)
        {
            Debug.LogWarning($"[CardCraftCommands] Craft failed — {t_error.GetBaseException().Message}");
            bool t_network = CloudFailureClassifier.Classify(t_error) == ECloudFailureKind.Transient;
            return new CardCraftOutcome(t_network ? ECardCraftOutcome.NetworkFailed : ECardCraftOutcome.Failed, _cardId);
        }
        finally
        {
            t_pending?.Settle();
            if (t_generation == s_generation)
            {
                s_crafting = false;
                s_validUntil = 0;
                NotifyChanged();
            }
        }
    }

    static void NotifyChanged()
    {
        try { OnChanged?.Invoke(); }
        catch (Exception t_error) { Debug.LogException(t_error); }
    }

    static void ResetSession()
    {
        unchecked { s_generation++; }
        var t_previous = s_refresh;
        s_refresh = null;
        s_recipes.Clear();
        s_ready = s_crafting = false;
        s_validUntil = 0;
        t_previous?.TrySetResult(false);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        OnChanged = null;
        s_session = default;
        ResetSession();
    }
}

[Preserve]
internal sealed class CardCraftCatalogResponse
{
    [Preserve, JsonProperty("currency")] public string Currency;
    [Preserve, JsonProperty("cards")] public List<CardCraftRecipeResponse> Cards;
}

[Preserve]
internal sealed class CardCraftRecipeResponse
{
    [Preserve, JsonProperty("cardId")] public int CardId;
    [Preserve, JsonProperty("cost")] public long Cost;
}

[Preserve]
internal sealed class CraftCardResponse : ServerCommandResult
{
    [Preserve, JsonProperty("cardId")] public int CardId;
    [Preserve, JsonProperty("currency")] public string Currency;
    [Preserve, JsonProperty("cost")] public long Cost;
}
