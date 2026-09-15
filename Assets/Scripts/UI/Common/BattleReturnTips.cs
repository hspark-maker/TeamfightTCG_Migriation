using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>전투 복귀마다 팁 하나를 선택하고 세션 동안 직전 문구를 기억한다.</summary>
internal static class BattleReturnTips
{
    const string PREFIX = "Tip :";
    static Random _random = new Random();
    static string _previousText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        _random = new Random();
        _previousText = null;
    }

    /// <summary>로드된 팁이 없거나 조회에 실패하면 프리팹 문구를 그대로 표시한다.</summary>
    public static string Next(string fallback)
    {
        string selected = null;
        try
        {
            selected = Select(SpecSource.Manager?.LoadingTip?.All, _previousText, _random);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[BattleReturnTips] Could not select a loading tip: {exception.Message}");
        }

        string displayed = selected == null ? fallback : PREFIX + " " + selected;
        _previousText = Normalize(displayed);
        return displayed;
    }

    internal static string Select(IReadOnlyList<LoadingTip> rows, string previousText, Random random)
    {
        var candidates = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        if (rows != null)
        {
            foreach (var row in rows)
            {
                if (row == null || row.enabled != 1) continue;
                string text = Normalize(row.text);
                if (text.Length > 0 && unique.Add(text)) candidates.Add(text);
            }
        }

        if (candidates.Count > 1) candidates.Remove(Normalize(previousText));
        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
    }

    static string Normalize(string text)
    {
        text = text?.Trim() ?? string.Empty;
        while (text.StartsWith(PREFIX, StringComparison.Ordinal))
            text = text.Substring(PREFIX.Length).TrimStart();
        return text;
    }
}
