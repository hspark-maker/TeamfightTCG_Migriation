using UnityEngine;

[CreateAssetMenu(fileName = "KeywordIconConfig", menuName = "BurgerMonster/Keyword Icon Config")]
public class KeywordIconConfig : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public CardKeyword keyword;
        public Sprite      icon;
        public string      displayName;
        public string      explain;
        public string      effectLabel;
        public Color       glowStartColor;
        public Color       glowEndColor;
    }

    [SerializeField] Entry[] entries;

    public Sprite GetIcon(CardKeyword _keyword)
    {
        foreach (Entry t_e in this.entries)
            if (t_e.keyword == _keyword) return t_e.icon;
        return null;
    }

    public bool TryGetEntry(CardKeyword _keyword, out Entry _entry)
    {
        foreach (Entry t_e in this.entries)
        {
            if (t_e.keyword != _keyword) continue;
            _entry = t_e;
            return true;
        }
        _entry = default;
        return false;
    }

    public bool TryGetGlowColors(CardKeyword _keyword, out Color _start, out Color _end)
    {
        foreach (Entry t_e in this.entries)
        {
            if (t_e.keyword != _keyword) continue;
            _start = t_e.glowStartColor;
            _end   = t_e.glowEndColor;
            return true;
        }
        _start = Color.white;
        _end   = Color.clear;
        return false;
    }
}
