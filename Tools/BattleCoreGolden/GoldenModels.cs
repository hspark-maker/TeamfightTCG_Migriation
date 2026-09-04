using System.Collections.Generic;

internal sealed class GoldenDocument
{
    public int SchemaVersion { get; set; }
    public bool Eligible { get; set; }
    public string ExclusionReason { get; set; }
    public string SeedHex { get; set; }
    public int FirstOwner { get; set; }
    public List<GoldenDeck> Decks { get; set; }
    public List<GoldenCardSpec> CardSpecs { get; set; }
    public string CommandLog { get; set; }
    public List<GoldenCheckpoint> Checkpoints { get; set; }
    public string FinalStateHash { get; set; }
    public int FinalDrawCount { get; set; }
    public int[] Remaining { get; set; }
    public int WinnerOwner { get; set; }
    public bool Draw { get; set; }
}

internal sealed class GoldenDeck
{
    public int OwnerIndex { get; set; }
    public List<GoldenCardSnapshot> Cards { get; set; }
    public List<int> BoardOrder { get; set; }
}

internal sealed class GoldenCardSnapshot
{
    public int CardId { get; set; }
    public int Level { get; set; }
    public int HpBonus { get; set; }
    public int EvolutionStage { get; set; }
    public int UnlockedKeywords { get; set; }
    public bool SynergyUnlocked { get; set; }
}

internal sealed class GoldenCardSpec
{
    public int Id { get; set; }
    public int MaxHp { get; set; }
    public int Keywords { get; set; }
    public int KeywordUnlockLevel { get; set; }
    public int DefaultEvolutionStage { get; set; }
    public List<string> Synergies { get; set; }
    public int Hp2 { get; set; }
    public int Hp3 { get; set; }
    public int Hp4 { get; set; }
}

internal sealed class GoldenCheckpoint
{
    public int Turn { get; set; }
    public int ActingOwner { get; set; }
    public string StateHash { get; set; }
    public int DrawCount { get; set; }
}
