using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class BattleGoldenCardSnapshot
{
    public int cardId;
    public int level;
    public int hpBonus;
    public int evolutionStage;
    public int unlockedKeywords;
    public bool synergyUnlocked;
}

[Serializable]
public sealed class BattleGoldenDeck
{
    public int ownerIndex;

    /// <summary>서버 계약과 같은 cardId 오름차순. 스펙·성장 조회용이다.</summary>
    public BattleGoldenCardSnapshot[] cards;

    /// <summary>셔플이 끝난 **실제 보드 순서**(슬롯 0..2 → 대기열). 재생기는 이걸 그대로 놓아야 한다.
    /// cards 는 정렬돼 있어 셔플 입력 순서를 복원할 수 없다 — 정렬은 되돌릴 수 없는 정보 손실이라,
    /// 재생기가 자기 셔플을 돌리면 같은 시드로도 다른 보드가 나온다. 그 차이는 첫 체크포인트부터
    /// 해시 불일치로만 드러나고 원인을 지목하지 못한다.</summary>
    public int[] boardOrder;
}

[Serializable]
public sealed class BattleGoldenCardSpec
{
    public int id;
    public int maxHp;
    public int keywords;
    public int keywordUnlockLevel;
    public int defaultEvolutionStage;
    public string[] synergies;
    public int hp2;
    public int hp3;
    public int hp4;
}

[Serializable]
public sealed class BattleGoldenCheckpoint
{
    public int turn;
    public int actingOwner;
    public string stateHash;
    public int drawCount;
}

[Serializable]
public sealed class BattleGoldenDocument
{
    public int schemaVersion = 3;   // 3 = 서버 시드 파생 셔플 검증
    public int rulesetVersion;
    public string contentFingerprint;
    public string capturedAtUtc;
    public string unityVersion;
    public bool eligible;
    public string exclusionReason;
    public string matchId;
    public string seedHex;
    public int firstOwner;
    public BattleGoldenDeck[] decks;
    public BattleGoldenCardSpec[] cardSpecs;
    public string commandLog;
    public string commandLogHash;
    public int commandCount;
    public bool commandLogTruncated;
    public BattleGoldenCheckpoint[] checkpoints;
    public string finalStateHash;
    public int finalDrawCount;
    public int[] remaining;
    /// <summary>승자 소유자. 무승부면 -1.</summary>
    public int winnerOwner = -1;

    /// <summary>양쪽 동시 전멸. true면 winnerOwner 는 -1 이다.</summary>
    public bool draw;
}

/// <summary>에디터에서 실제 멀티 규칙 실행 결과를 서버 재생용 골든 JSON으로 기록한다.</summary>
public static class BattleGoldenRecorder
{
    const string EditorPrefKey = "battle.golden.capture.enabled";
    const string CaptureEnvironmentKey = "BATTLE_GOLDEN_CAPTURE";
    const string OutputEnvironmentKey = "BATTLE_GOLDEN_CAPTURE_DIR";

    static BattleField s_firstField;
    static BattleField s_secondField;
    static BattleGoldenDocument s_document;
    static readonly List<BattleGoldenCheckpoint> s_checkpoints = new List<BattleGoldenCheckpoint>();

    public static bool Enabled
    {
        get
        {
#if UNITY_EDITOR
            return string.Equals(Environment.GetEnvironmentVariable(CaptureEnvironmentKey), "1",
                       StringComparison.Ordinal) || UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false);
#else
            return false;
#endif
        }
    }

    public static void Begin(BattleField _firstField, BattleField _secondField, int _firstOwner)
    {
        Reset();
        if (!Enabled || !DeckConfig.IsMultiplayer || _firstField == null || _secondField == null) return;

        try
        {
            int t_rulesetVersion = MultiplayerTurnRunner.Instance?.RulesetVersion ?? 0;
            if (t_rulesetVersion <= 0) return;

            s_firstField = _firstField;
            s_secondField = _secondField;
            BattleGoldenDeck[] t_decks = { CaptureDeck(_firstField), CaptureDeck(_secondField) };
            Array.Sort(t_decks, (a, b) => a.ownerIndex.CompareTo(b.ownerIndex));
            s_document = new BattleGoldenDocument
            {
                rulesetVersion = t_rulesetVersion,
                contentFingerprint = SpecSource.BattleFingerprint?.ToLowerInvariant() ?? string.Empty,
                capturedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                eligible = !TutorialConfig.IsActive,
                exclusionReason = TutorialConfig.IsActive ? "tutorial_rng_contract" : string.Empty,
                matchId = MultiplayerTurnRunner.Instance?.MatchId ?? string.Empty,
                seedHex = ResolveSeedHex(),
                firstOwner = _firstOwner,
                decks = t_decks,
                cardSpecs = CaptureSpecs(t_decks),
            };
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[BattleGolden] begin failed; capture disabled for this match: {t_exception}");
            Reset();
        }
    }

    public static void RecordCheckpoint(int _turn, int _actingOwner, ulong _stateHash)
    {
        if (s_document == null) return;
        s_checkpoints.Add(new BattleGoldenCheckpoint
        {
            turn = _turn,
            actingOwner = _actingOwner,
            stateHash = _stateHash.ToString("x16"),
            drawCount = MatchRandom.DrawCount,
        });
    }

    /// <summary>_localWon = 로컬 플레이어가 이겼는가(FinalizeResult 판정). 승자 소유자 인덱스를 골든에
    /// 남겨 서버 재생기의 승패 판정 자체를 대조할 수 있게 한다 — remaining 만으로는 "누가 이겼는가"가
    /// 검증되지 않는다.</summary>
    public static void Finish(bool _localWon, bool _draw)
    {
        if (s_document == null) return;

        // **본문 전체가 try 안이다.** 여기는 TurnRunner.FinalizeResult 안 ShowResult 바로 앞이라,
        // 캡처가 예외를 던지면 결과 팝업이 안 뜨고 플레이어가 전투에 갇힌다.
        // 캡처는 전투에 영향을 주지 않는다는 게 이 기능의 전제다 — 수집도 쓰기도 전부 삼킨다.
        try
        {
            FinishCore(_localWon, _draw);
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[BattleGolden] capture failed: {t_exception}");
        }
        finally
        {
            Reset();
        }
    }

    static void FinishCore(bool _localWon, bool _draw)
    {
        if (BattleCommandLog.IsFrozen)
        {
            s_document.eligible = false;
            s_document.exclusionReason = "ai_takeover_command_log_frozen";
        }
        if (BattleCommandLog.IsTruncated)
        {
            s_document.eligible = false;
            s_document.exclusionReason = "command_log_truncated";
        }
        s_document.commandLog = BattleCommandLog.SerializeBase64();
        s_document.commandLogHash = BattleCommandLog.HashHex();
        s_document.commandCount = BattleCommandLog.Count;
        s_document.commandLogTruncated = BattleCommandLog.IsTruncated;
        s_document.checkpoints = s_checkpoints.ToArray();
        s_document.finalStateHash = BattleStateHash.Compute(s_firstField.State, s_secondField.State).ToString("x16");
        s_document.finalDrawCount = MatchRandom.DrawCount;
        s_document.remaining = new[] { Remaining(OwnerField(0)), Remaining(OwnerField(1)) };

        int t_localOwner = MultiplayerTurnRunner.Instance?.MyOwnerIndex ?? TurnState.LocalOwnerIndex;
        s_document.draw = _draw;
        s_document.winnerOwner = _draw ? -1 : _localWon ? t_localOwner : 1 - t_localOwner;

        string t_directory = ResolveOutputDirectory();
        Directory.CreateDirectory(t_directory);
        string t_match = string.IsNullOrWhiteSpace(s_document.matchId)
            ? DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") : Sanitize(s_document.matchId);
        string t_path = Path.Combine(t_directory, $"{t_match}-owner{t_localOwner}.json");
        File.WriteAllText(t_path, JsonUtility.ToJson(s_document, true));
        Debug.Log($"[BattleGolden] captured path={t_path} eligible={s_document.eligible}");
    }

    public static void Reset()
    {
        s_firstField = null;
        s_secondField = null;
        s_document = null;
        s_checkpoints.Clear();
    }

    static BattleGoldenDeck CaptureDeck(BattleField _field)
    {
        var t_cards = new List<BattleGoldenCardSnapshot>();
        foreach (CardInstance t_card in _field.GetActiveCards()) AddCard(t_cards, t_card);
        foreach (CardInstance t_card in _field.GetWaitingCards()) AddCard(t_cards, t_card);
        // 정렬 **전**에 실제 보드 순서를 떠 둔다(슬롯 0..2 → 대기열). 위 두 루프가 이미 그 순서다.
        var t_order = new List<int>(t_cards.Count);
        foreach (BattleGoldenCardSnapshot t_snapshot in t_cards) t_order.Add(t_snapshot.cardId);

        t_cards.Sort((a, b) => a.cardId.CompareTo(b.cardId));
        return new BattleGoldenDeck
        {
            ownerIndex = _field.OwnerIndex,
            cards = t_cards.ToArray(),
            boardOrder = t_order.ToArray(),
        };
    }

    static void AddCard(List<BattleGoldenCardSnapshot> _cards, CardInstance _card)
    {
        if (_card == null) return;
        _cards.Add(new BattleGoldenCardSnapshot
        {
            cardId = _card.cardId,
            level = _card.growthLevel,
            hpBonus = _card.maxHp - _card.spec.MaxHp,
            evolutionStage = _card.evolutionStage,
            unlockedKeywords = (int)_card.unlockedKeywords,
            synergyUnlocked = _card.synergyEnabled,
        });
    }

    static BattleGoldenCardSpec[] CaptureSpecs(BattleGoldenDeck[] _decks)
    {
        var t_ids = new SortedSet<int>();
        foreach (BattleGoldenDeck t_deck in _decks)
            foreach (BattleGoldenCardSnapshot t_card in t_deck.cards) t_ids.Add(t_card.cardId);

        var t_result = new List<BattleGoldenCardSpec>(t_ids.Count);
        foreach (int t_id in t_ids)
        {
            CardSpec t_spec = CardCatalog.RequireSpec(t_id);
            t_spec.TryGetHpGain(2, out int t_hp2);
            t_spec.TryGetHpGain(3, out int t_hp3);
            t_spec.TryGetHpGain(4, out int t_hp4);
            var t_synergies = new string[t_spec.SynergyNames.Count];
            for (int i = 0; i < t_synergies.Length; i++) t_synergies[i] = t_spec.SynergyNames[i];
            t_result.Add(new BattleGoldenCardSpec
            {
                id = t_spec.Id,
                maxHp = t_spec.MaxHp,
                keywords = (int)t_spec.Keywords,
                keywordUnlockLevel = t_spec.KeywordUnlockLevel,
                defaultEvolutionStage = t_spec.DefaultEvolutionStage,
                synergies = t_synergies,
                hp2 = t_hp2,
                hp3 = t_hp3,
                hp4 = t_hp4,
            });
        }
        return t_result.ToArray();
    }

    static BattleField OwnerField(int _owner)
        => s_firstField != null && s_firstField.OwnerIndex == _owner ? s_firstField : s_secondField;

    static int Remaining(BattleField _field)
        => _field == null ? 0 : _field.GetActiveCards().Count + _field.WaitingCount;

    static string ResolveSeedHex()
    {
        string t_serverSeed = MultiplayerTurnRunner.Instance?.SeedHex;
        return string.IsNullOrWhiteSpace(t_serverSeed)
            ? MatchRandom.InitialSeed.ToString("x16") : t_serverSeed.ToLowerInvariant();
    }

    static string ResolveOutputDirectory()
    {
        string t_override = Environment.GetEnvironmentVariable(OutputEnvironmentKey);
        if (!string.IsNullOrWhiteSpace(t_override)) return Path.GetFullPath(t_override);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "functions", "testdata", "golden"));
    }

    static string Sanitize(string _value)
    {
        foreach (char t_invalid in Path.GetInvalidFileNameChars()) _value = _value.Replace(t_invalid, '_');
        return _value;
    }
}
