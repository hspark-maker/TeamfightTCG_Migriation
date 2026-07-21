using UnityEngine;

[CreateAssetMenu(fileName = "NewCard", menuName = "Card Battle/Card Data")]
public class CardData : ScriptableObject
{
    public string displayName;
    public CardKeyword keywords;
    public int maxHp;
    public int bonusHp;
    public SynergyData mainSynergy;
    public SynergyData subClass;
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
