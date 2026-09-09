using System;
using UnityEngine;

// 카드 테두리(프레임) 그림의 단일 진실원.
//
// 프레임은 "등급 × 그 카드가 가진 시너지 개수" 로 갈린다. 예전에는 프리팹마다 프레임 스프라이트를
// 손으로 꽂아 카드가 무엇이든 같은 테두리가 떴다 — 프리팹이 여럿이라(전투 CardView / 아웃게임
// CardVisualView 계열) 표를 코드나 프리팹에 흩으면 곧바로 갈라진다. 그래서 표는 이 SO 하나에만 둔다.
//
// 여기에 넣지 않는 것: 어떤 렌더러에 꽂을지(SpriteRenderer/Image), 좌표·크기·정렬.
// 전부 "어떻게 그리는가"라서 뷰 책임이다. 고르는 규칙은 CardVisualRules.PickFrame 하나가 갖는다.
[CreateAssetMenu(fileName = "CardFrameConfig", menuName = "BurgerMonster/Card Frame Config")]
public class CardFrameConfig : ScriptableObject
{
    /// <summary>시너지 개수 축의 상한. 이보다 많은 시너지를 가진 카드도 이 칸의 그림을 쓴다
    /// (그림이 0/1/2 세 벌뿐이라 표를 늘리지 않고 여기서 잘라낸다).</summary>
    public const int MaxSynergyStep = 2;

    [Serializable]
    public struct Entry
    {
        public ECardGrade grade;

        [Tooltip("시너지 0개")] public Sprite noSynergy;
        [Tooltip("시너지 1개")] public Sprite oneSynergy;
        [Tooltip("시너지 2개 이상")] public Sprite twoSynergy;
    }

    [SerializeField] Entry[] entries;

    /// <summary>등급·시너지 개수에 맞는 프레임. 행이 없거나 그 칸이 비었으면 시너지 개수를 낮춰가며
    /// 폴백하고, 그래도 없으면 false — 호출부는 프리팹에 저작된 그림을 그대로 둔다.</summary>
    public bool TryGetFrame(ECardGrade _grade, int _synergyCount, out Sprite _frame)
    {
        _frame = null;
        if (this.entries == null) return false;

        for (int t_i = 0; t_i < this.entries.Length; t_i++)
        {
            if (this.entries[t_i].grade != _grade) continue;

            _frame = PickStep(this.entries[t_i], _synergyCount);
            return _frame != null;
        }

        return false;
    }

    // 칸이 비어 있으면 한 단계씩 내려온다 — 저작이 덜 된 등급에서 흰 사각형이 뜨는 것보다 낫다.
    static Sprite PickStep(Entry _entry, int _synergyCount)
    {
        int t_step = _synergyCount < 0 ? 0 : _synergyCount > MaxSynergyStep ? MaxSynergyStep : _synergyCount;

        if (t_step >= 2 && _entry.twoSynergy != null) return _entry.twoSynergy;
        if (t_step >= 1 && _entry.oneSynergy != null) return _entry.oneSynergy;
        return _entry.noSynergy;
    }
}
