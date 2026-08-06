using UnityEngine;
using UnityEngine.Video;

public enum ECardChannel
{
    TestOnly = 0,
    Live = 1,
}

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
    public const int MinHpCurveLevel = 0;
    public const int MaxHpCurveLevel = 10;

    /// <summary>카드 고유 번호. 에셋 이름·표 행 순서와 무관하게 카드를 가리키는 안정 키다 —
    /// 리네임·행 이동에도 이 값은 따라가지 않는다. **한 번 부여하면 바꾸지 않는다.**
    /// 0 = 미부여(표 가져오기가 빈 번호를 찾아 채운다). 중복은 표 도구가 경고로 잡는다.</summary>
    [Min(0)] [Tooltip("카드 고유 번호. 부여 후 변경 금지. 0 = 미부여(표 가져오기가 자동 부여).")]
    public int id;

    public string displayName;
    [Tooltip("Live는 실제 실행에도 노출되고, TestOnly는 테스트 실행에서만 노출됩니다.")]
    public ECardChannel channel;
    /// <summary>이 카드의 키워드. <see cref="keywordUnlockLevel"/>에 도달해야 열린다(미지정이면 처음부터).
    /// "강화 키워드"(2차 진화)는 별도 필드가 아니라 **이 키워드가 강화되는 것**이라 여기 한 축만 둔다.</summary>
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

    /// <summary>체력 곡선의 "미지정" 칸. 0을 빈칸으로 쓸 수 없어서 둔다 —
    /// 표의 빈칸은 레벨 하나만 전역식으로 넘기는 뜻이고, 0은 "그 레벨 체력이 0"이라는 저작 값이다.</summary>
    public const int NoMaxHpOverride = -1;

    [Header("Growth HP Curve")]
    [Tooltip("index = 레벨(0~10), 값 = 그 레벨의 최대 HP(절대값). -1 = 미지정(그 레벨만 CardGrowthConfig 전역식). 비어 있으면 전 레벨 전역식.")]
    public int[] maxHpByLevel;

    [Header("Growth Unlock")]
    /// <summary><see cref="keywords"/>가 열리는 강화 레벨. **0(미지정) = 처음부터 열려 있음** —
    /// 해금 레벨을 아직 정하지 않은 카드를 기본 키워드 카드로 취급하기 위한 값이라 0이 기본이어야 한다.
    /// 카드마다 다르므로 여기 둔다(진화 레벨은 전역이라 CardGrowthConfig 소유).</summary>
    [Min(0)] [Tooltip("이 레벨에 도달하면 keywords가 열린다. 0 = 처음부터 열려 있음(기본 키워드 카드).")]
    public int keywordUnlockLevel;

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
    // 시네마 공격(3단계 첫 공격) 연출 종류. 카드마다 다른 연출을 주는 축 — 연출 구현은 AttackSequence 소유.
    // **등장 연출도 이 값이 정한다**(EnergyOrbDash면 슬롯 배치도 같은 구체로 날아온다 — CardAppearVfx).
    // 등장용 축을 따로 두지 않는 이유: 공격과 등장이 같은 구체를 쓰는 한 몸 연출이라 배선이 갈라지면 어긋난다.
    public CinemaAttackStyle cinemaAttackStyle;
    // EnergyOrbDash에서 카드가 변하는 구체 프리팹. 카드마다 테마가 달라 **카드 고유 축**에 둔다
    // (라이브러리는 규칙 기반 연출 전용). 비우면 BattleVfxLibrary의 CinemaEnergyOrb로 떨어진다.
    public GameObject cinemaOrbPrefab;

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

    /// <summary>레벨 _level의 최대 HP를 표가 직접 정해뒀는가. false면 미지정 — 부르는 쪽이 전역식으로 채운다.
    ///
    /// **칸 번호는 레벨이 아니라 강화 횟수다.** hp0 = 미강화 = <see cref="maxHp"/>이므로 곡선이 읽는 첫 칸은 hp1이고,
    /// 레벨 L은 index L-1을 본다(Lv2 = 1강화 = hp1). 레벨은 1부터 세는데 강화는 0회부터 세기 때문.</summary>
    public bool TryGetMaxHp(int _level, out int _maxHp)
    {
        _maxHp = 0;
        if (this.maxHpByLevel == null) return false;

        int t_index = _level - 1;
        if (t_index < MinHpCurveLevel || t_index > MaxHpCurveLevel) return false;
        if (t_index >= this.maxHpByLevel.Length) return false;

        int t_value = this.maxHpByLevel[t_index];
        if (t_value < 0) return false;

        _maxHp = t_value;
        return true;
    }

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
