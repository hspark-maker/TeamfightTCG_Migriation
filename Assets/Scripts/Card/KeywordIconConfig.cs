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

    [Header("폴백")]
    // 표시할 키워드가 하나도 없는 카드가 쓰는 기본 아이콘. 아이콘 자리가 통째로 비면 카드마다
    // 레이아웃이 들쭉날쭉해 보여서, 빈칸 대신 이 아이콘 1개를 그린다. 미배정(null)이면 종전대로 빈칸.
    [SerializeField] Sprite defaultIcon;

    /// <summary>키워드 없는 카드에 그릴 폴백 아이콘. 없으면 null.</summary>
    public Sprite DefaultIcon => this.defaultIcon;

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
