using UnityEngine;

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
}
