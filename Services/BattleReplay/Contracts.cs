using System.Globalization;
using System.Text.RegularExpressions;

internal sealed class ReplayRequest
{
    public const int SupportedRulesetVersion = 2;

    public string Env { get; set; } = string.Empty;
    public int RulesetVersion { get; set; }
    public string ContentFingerprint { get; set; } = string.Empty;
    public string SeedHex { get; set; } = string.Empty;
    public List<ReplayDeckDto> Decks { get; set; } = new();
    public string CommandLog { get; set; } = string.Empty;
    public Dictionary<string, ReplaySpecPinDto> SpecPins { get; set; } = new(StringComparer.Ordinal);

    public bool TryBuildInput(out BattleReplayInput? _input, out string _error)
    {
        _input = null;
        _error = string.Empty;
        if (!Regex.IsMatch(Env ?? string.Empty, "^[A-Za-z0-9_-]{1,40}$"))
        { _error = "env_invalid"; return false; }
        if (RulesetVersion != SupportedRulesetVersion) { _error = "ruleset_version_unsupported"; return false; }
        if (!Regex.IsMatch(ContentFingerprint ?? string.Empty, "^[0-9a-fA-F]{64}$"))
        { _error = "content_fingerprint_invalid"; return false; }
        string[] t_requiredPins = { "Card", "SynergyDef", "SynergyTierDef", "SynergyEffectDef" };
        if (SpecPins == null || SpecPins.Count != t_requiredPins.Length)
        { _error = "spec_pins_invalid"; return false; }
        foreach (string t_table in t_requiredPins)
        {
            if (!SpecPins.TryGetValue(t_table, out ReplaySpecPinDto? t_pin) ||
                !Regex.IsMatch(t_pin.PayloadHash ?? string.Empty, "^[0-9a-fA-F]{16}$") ||
                !t_pin.BlobPath.StartsWith($"envs/{Env}/specs/", StringComparison.Ordinal))
            { _error = "spec_pin_invalid:" + t_table; return false; }
        }

        string t_seed = SeedHex ?? string.Empty;
        if (t_seed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t_seed = t_seed[2..];
        if (!ulong.TryParse(t_seed, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong t_seedValue))
        { _error = "seed_invalid"; return false; }
        if (Decks == null || Decks.Count != 2) { _error = "decks_invalid"; return false; }

        byte[] t_log;
        try { t_log = Convert.FromBase64String(CommandLog ?? string.Empty); }
        catch (FormatException) { _error = "command_log_base64"; return false; }

        var t_decks = new List<BattleReplayDeck>(2);
        foreach (ReplayDeckDto t_deck in Decks)
        {
            if (t_deck.OwnerIndex is < 0 or > 1 || t_deck.Cards == null || t_deck.Cards.Count == 0)
            { _error = "deck_invalid"; return false; }
            var t_cards = new List<BattleReplayCard>(t_deck.Cards.Count);
            foreach (ReplayCardDto t_card in t_deck.Cards)
            {
                if (t_card.CardId <= 0) { _error = "card_id_invalid"; return false; }
                t_cards.Add(new BattleReplayCard(t_card.CardId,
                    new CardGrowth(t_card.Level, t_card.HpBonus, t_card.EvolutionStage,
                        (CardKeyword)t_card.UnlockedKeywords, t_card.SynergyUnlocked)));
            }
            t_decks.Add(new BattleReplayDeck
            {
                OwnerIndex = t_deck.OwnerIndex,
                Cards = t_cards,
                BoardOrder = t_deck.BoardOrder,
            });
        }

        _input = new BattleReplayInput { Seed = t_seedValue, Decks = t_decks, CommandLog = t_log };
        return true;
    }
}

internal sealed class ReplaySpecPinDto
{
    public string BlobPath { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
}

internal sealed class ReplayDeckDto
{
    public int OwnerIndex { get; set; }
    public List<ReplayCardDto> Cards { get; set; } = new();
    public List<int>? BoardOrder { get; set; }
}

internal sealed class ReplayCardDto
{
    public int CardId { get; set; }
    public int Level { get; set; }
    public int HpBonus { get; set; }
    public int EvolutionStage { get; set; }
    public int UnlockedKeywords { get; set; }
    public bool SynergyUnlocked { get; set; }
}

internal sealed record ReplayResponse(
    bool Ok,
    string Reason,
    int FirstOwner,
    int WinnerOwner,
    bool Draw,
    int[] Remaining,
    int[] DestroyedByOwner,
    string FinalStateHash,
    int DrawCount)
{
    public static ReplayResponse From(BattleReplayResult _result) => new(
        _result.Ok,
        _result.Reason,
        _result.FirstOwner,
        _result.WinnerOwner,
        _result.Draw,
        _result.Remaining,
        _result.DestroyedByOwner,
        _result.FinalStateHash.ToString("x16", CultureInfo.InvariantCulture),
        _result.DrawCount);
}
