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

        [Tooltip("이 키워드 글로우 유지 시간(초). 0 이하면 BattleTimingConfig.keywordGlowHold(전역)를 쓴다. " +
                 "배속(SpeedFactor)은 읽는 쪽에서 곱한다 — 여기 값은 항상 raw 초.")]
        public float glowHoldOverride;

        [Tooltip("이 키워드 전용 글로우 프리팹. 비우면 CardView의 기본 프리팹을 쓴다.")]
        public GameObject glowPrefabOverride;

        [Tooltip("이 키워드 글로우 크기 배율. 0 이하면 아래 defaultGlowScale(전역)을 쓴다.")]
        public float glowScaleOverride;
    }

    /// <summary>글로우 1개를 그리는 데 필요한 값 묶음. 색은 항상 있고(미등록이면 흰→투명),
    /// hold/prefab은 "미지정"을 0/null로 표현해 호출부가 전역 기본값으로 폴백한다.
    /// 병렬 out 파라미터 대신 한 덩어리로 돌려주는 이유: 셋이 항상 같이 쓰여서 따로 조회하면 순회가 3배가 된다.</summary>
    public readonly struct GlowSpec
    {
        public readonly Color      Start;
        public readonly Color      End;
        public readonly float      HoldOverride;    // 0 이하 = 전역 keywordGlowHold
        public readonly GameObject PrefabOverride;  // null = CardView 기본 프리팹
        public readonly float      ScaleOverride;   // 0 이하 = 전역 defaultGlowScale

        public GlowSpec(Color _start, Color _end, float _hold, GameObject _prefab, float _scale)
        {
            this.Start = _start; this.End = _end;
            this.HoldOverride = _hold; this.PrefabOverride = _prefab;
            this.ScaleOverride = _scale;
        }

        public static GlowSpec Default => new GlowSpec(Color.white, Color.clear, 0f, null, 0f);
    }

    [SerializeField] Entry[] entries;

    [Header("폴백")]
    // 표시할 키워드가 하나도 없는 카드가 쓰는 기본 아이콘. 아이콘 자리가 통째로 비면 카드마다
    // 레이아웃이 들쭉날쭉해 보여서, 빈칸 대신 이 아이콘 1개를 그린다. 미배정(null)이면 종전대로 빈칸.
    [SerializeField] Sprite defaultIcon;

    [Header("글로우 전역 기본값 (Entry에서 0이면 이 값)")]
    [Tooltip("글로우 프리팹 크기 배율. 프리팹 자체를 키우지 않고 여기서 조절한다.")]
    [SerializeField] float defaultGlowScale = 1.6f;

    [Header("키워드 아이콘 Pop (글로우와 같은 프레임에 터진다)")]
    [Tooltip("아이콘이 튀는 최대 배율. 1 이하면 Pop 없음.")]
    [SerializeField] float iconPopScale = 1.35f;
    [Tooltip("Pop 시간(초). raw 값 — 배속은 읽는 쪽에서 곱한다.")]
    [SerializeField] float iconPopDuration = 0.25f;

    /// <summary>키워드 없는 카드에 그릴 폴백 아이콘. 없으면 null.</summary>
    public Sprite DefaultIcon => this.defaultIcon;

    /// <summary>Entry가 오버라이드를 안 줬을 때 쓰는 글로우 크기 배율. 0 이하 배선은 1로 막는다(글로우 소멸 방지).</summary>
    public float DefaultGlowScale => this.defaultGlowScale > 0f ? this.defaultGlowScale : 1f;

    /// <summary>아이콘 Pop 최대 배율. 1 이하면 Pop을 돌리지 않는다.</summary>
    public float IconPopScale => this.iconPopScale;

    /// <summary>아이콘 Pop 시간(raw 초).</summary>
    public float IconPopDuration => this.iconPopDuration;

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

    /// <summary>키워드 글로우 설정. 미등록 키워드는 <see cref="GlowSpec.Default"/>(흰→투명, 오버라이드 없음).</summary>
    public GlowSpec GetGlow(CardKeyword _keyword)
    {
        foreach (Entry t_e in this.entries)
        {
            if (t_e.keyword != _keyword) continue;
            return new GlowSpec(t_e.glowStartColor, t_e.glowEndColor,
                                t_e.glowHoldOverride, t_e.glowPrefabOverride, t_e.glowScaleOverride);
        }
        return GlowSpec.Default;
    }
}
