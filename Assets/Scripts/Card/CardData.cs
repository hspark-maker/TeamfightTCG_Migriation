using UnityEngine;
using UnityEngine.Video;

/// <summary>카드 한 상태(미진화 / 진화 N단계)의 아트 묶음. 진화 단계마다 이 세 장이 세트로 바뀐다.
/// 단계별로 Sprite 필드를 평평하게 늘어놓으면(evolved2BattleImage…) 단계 추가 때마다 필드가 3개씩 늘고
/// 호출부가 단계→필드 매핑을 손으로 분기하게 된다 → 세트를 배열 원소로 만들어 단계는 인덱스로만 다룬다.</summary>
[System.Serializable]
public class CardArtSet
{
    public Sprite fullImage;
    public Sprite portrait;
    public Sprite battleImage;
}

[CreateAssetMenu(fileName = "NewCard", menuName = "Card Battle/Card Data")]
public class CardData : ScriptableObject
{
    public string displayName;
    public CardKeyword keywords;
    public int maxHp;
    public int bonusHp;
    // 카드가 가진 시너지들(가변 개수). main/sub 구분은 개념적일 뿐 같은 종류의 synergy.
    // 같은 SynergyData가 중복 나열돼도 카운트/적용/배지에서는 1회로 취급(소비측 Distinct).
    public SynergyData[] synergies;
    public Sprite fullImage;
    public Sprite portrait;
    public Sprite battleImage;
    public Sprite deckPreview;

    /// <summary>진화 최대 단계. 1단계=최초 진화 … 3단계=최종. 0단계(미진화) 아트는 위 기본 필드다.
    /// 단계 수의 단일 진실원 — 진화 규칙·UI·에디터가 전부 이걸 보고, 숫자 3을 각자 적지 않는다.</summary>
    public const int MaxEvolutionStage = 3;

    [Header("Evolution Art")]
    // 진화 단계별 아트. index 0 = 1단계, index 2 = 3단계(GetEvolvedArt로 접근).
    // 비어 있는 슬롯은 정상이다 — 모든 카드가 3단계 아트를 다 갖진 않는다. 호출부는 미진화 아트로 폴백하고
    // 렌더러를 끄면 안 된다. 폴백 규칙 자체는 CardVisualRules에 둔다(로비/전투가 갈라지지 않게).
    // deckPreview는 대응 필드를 두지 않는다: 덱 대표 배너는 아웃게임 정적 표시라 전투 중 진화 상태와 무관하다.
    public CardArtSet[] evolvedArts = new CardArtSet[MaxEvolutionStage];

    [Header("Evolution / Cinematic (임시 입력 — 등급 시스템 들어오면 세이브로 이관)")]
    // 0=미진화. 등급 획득/성장 시스템이 없으므로 지금은 이 값이 런타임 진화 단계의 유일한 입력원이다.
    // CardInstance 생성 시 1회 복사되며, 세이브 연동이 들어오면 이 필드 대신 세이브가 주입한다.
    public int defaultEvolutionStage;
    // 등장 컷씬. null이면 컷씬 없음(대부분의 카드가 여기 해당 — 판정은 CardCinematicRules 단일 지점).
    public VideoClip appearCinematic;

    [Header("Weapon")]
    public GameObject weaponPrefab;
    public Vector3    enemyWeaponEuler = new Vector3(0f, 0f, 180f);
    public AttackEffect attackEffect;
    public CardPassive  passive;

    [Header("Voice")]
    public AudioClip[] spawnVoices;
    public AudioClip[] attackVoices;
    public AudioClip[] killVoices;
    public AudioClip[] deathVoices;
    public AudioClip[] effectVoices;

    [Header("Effect SFX")]
    public AudioClip[] effectClips;

    public string cardExplain;
    public CardKeyword explainKeywords;

    public bool HasKeyword(CardKeyword _kw) => (this.keywords & _kw) != 0;

    /// <summary>진화 _stage단계(1~MaxEvolutionStage)의 아트 세트. 범위를 벗어나거나 미배정이면 null.
    /// 여기서는 인덱싱만 한다 — "비었으면 무엇으로 폴백하나"는 표시 규칙이라 CardVisualRules 몫이다.
    /// 0단계(미진화)를 넣으면 null이 나온다. 미진화는 배열이 아니라 fullImage/portrait/battleImage 쪽이다.</summary>
    public CardArtSet GetEvolvedArt(int _stage)
    {
        int t_index = _stage - 1;
        if (this.evolvedArts == null || t_index < 0 || t_index >= this.evolvedArts.Length) return null;
        return this.evolvedArts[t_index];
    }
}
