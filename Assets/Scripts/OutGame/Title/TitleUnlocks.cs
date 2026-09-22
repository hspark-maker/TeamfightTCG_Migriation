using System.Collections.Generic;
using Newtonsoft.Json;

internal sealed class TitleUnlockDefinition
{
    [JsonProperty("titleId")] public string TitleId { get; set; }
    [JsonProperty("description")] public string Description { get; set; }
}

internal sealed class EnsureTitlesResult : ServerCommandResult
{
    [JsonProperty("changed")] public bool Changed { get; set; }
    [JsonProperty("definitions")] public List<TitleUnlockDefinition> Definitions { get; set; }
}

internal static class TitleUnlocks
{
    static readonly Dictionary<string, string> s_descriptions = new Dictionary<string, string>();

    internal static void Adopt(IReadOnlyList<TitleUnlockDefinition> _definitions)
    {
        s_descriptions.Clear();
        if (_definitions == null) return;
        foreach (TitleUnlockDefinition t_definition in _definitions)
            if (t_definition != null && !string.IsNullOrEmpty(t_definition.TitleId))
                s_descriptions[t_definition.TitleId] = t_definition.Description ?? string.Empty;
    }

    internal static string Description(string _titleId, string _fallback)
    {
        return _titleId != null && s_descriptions.TryGetValue(_titleId, out string t_description)
            ? t_description : _fallback;
    }

    internal static void ResetSession() => s_descriptions.Clear();
}
