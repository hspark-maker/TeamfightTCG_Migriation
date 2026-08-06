using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CardData SO ↔ 와이어 ID 매핑. **와이어 ID는 <see cref="CardData.id"/>다.**
///
/// **게임에 존재하는 카드 전체 목록의 단일 진실원이기도 하다.** 덱 편성 컬렉션(DeckBuilderUI),
/// 세이브 복원(DeckSaveManager)이 전부 이 목록을 참조한다 — 씬 컴포넌트가 카드 목록을 따로
/// 들고 있으면 카드 추가 시 한쪽만 갱신되어 조용히 어긋난다(실제로 그랬음).
///
/// **배열 순서는 의미가 없다.** 번호를 카드가 직접 들고 있으므로 재정렬·중간 삽입·빈 칸 제거가 안전하다.
/// 대신 **부여한 번호를 바꾸면 안 된다** — 그게 양 클라가 카드를 지목하는 유일한 축이다.
/// </summary>
[CreateAssetMenu(fileName = "CardRegistry", menuName = "Card Battle/Card Registry")]
public class CardRegistry : ScriptableObject
{
    [SerializeField] CardData[] allCards;

    /// <summary>등록된 카드 전체. null 칸이 섞여 있을 수 있으니 소비측에서 걸러라.</summary>
    public IReadOnlyList<CardData> All => this.allCards ?? System.Array.Empty<CardData>();

    /// <summary>전체 목록은 그대로 둔 채 현재 실행 프로필에 노출할 카드만 반환한다.</summary>
    public IEnumerable<CardData> Available(bool _includeTestCards)
    {
        foreach (CardData t_card in All)
        {
            if (t_card == null) continue;
            if (_includeTestCards || t_card.channel == ECardChannel.Live)
                yield return t_card;
        }
    }

    readonly Dictionary<int, CardData> idToData = new Dictionary<int, CardData>();

    bool indexed;

    public void Initialize()
    {
        this.idToData.Clear();
        this.indexed = true;

        if (this.allCards == null) return;

        for (int i = 0; i < this.allCards.Length; i++)
        {
            CardData t_card = this.allCards[i];
            if (t_card == null) continue;

            if (t_card.id <= 0)
            {
                Debug.LogError($"[CardRegistry] '{t_card.name}'에 번호가 없다 — 멀티에서 이 카드가 나오면 상대가 해석하지 못한다. 카드 표(Excel) 가져오기로 번호를 부여할 것.");
                continue;
            }
            if (this.idToData.ContainsKey(t_card.id))
            {
                Debug.LogError($"[CardRegistry] 번호 {t_card.id} 중복 — '{this.idToData[t_card.id].name}' 유지, '{t_card.name}' 제외. 표에서 번호를 고칠 것.");
                continue;
            }
            this.idToData[t_card.id] = t_card;
        }
    }

    /// <summary>와이어로 보낼 카드 번호. null·미부여는 -1(수신측이 해석 실패로 버린다).</summary>
    public int GetId(CardData _data) => _data != null && _data.id > 0 ? _data.id : -1;

    public CardData GetData(int _id)
    {
        if (_id <= 0) return null;

        // 배틀 씬 밖(테스터 등)에서 Initialize를 안 거쳐도 조회되게 지연 색인한다.
        if (!this.indexed) Initialize();

        this.idToData.TryGetValue(_id, out CardData t_data);
        return t_data;
    }
}
