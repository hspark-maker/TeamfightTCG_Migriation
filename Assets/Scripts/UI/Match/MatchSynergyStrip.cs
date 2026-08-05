using System.Collections.Generic;
using UnityEngine;

// 매치 진입 직전 화면의 활성 시너지 줄(인포바에 가로 한 줄). 덱을 받아 아이콘만 깐다.
// 집계를 편성용 SynergyPreview가 아니라 SynergyResolver로 하는 이유: 이 화면이 보여주는 건
// "전투가 실제로 켤 시너지"라 판정이 전투와 같아야 한다(활성만, 미달 진행도 없음).
// 개수·티어는 표시하지 않는다 — 전투 필드 줄(FieldSynergyPanel)과 같은 규약이다.
// 상태를 들지 않는 순수 렌더러 — MatchDeckPanelView가 매 Render마다 덱을 다시 넘긴다.
public class MatchSynergyStrip : MonoBehaviour
{
    [Tooltip("아이콘 줄. HorizontalLayoutGroup + ContentSizeFitter가 붙어 있어야 아이콘 수에 맞춰 스스로 늘고 준다")]
    [SerializeField] Transform       iconParent;
    [Tooltip("아이콘 한 칸 프리팹. 수가 변하는 부분이라 여기만 런타임 생성한다")]
    [SerializeField] SynergyIconView iconPrefab;
    [Tooltip("한 줄에 최대 몇 개까지. 0이면 제한 없음")]
    [SerializeField] int             maxIcons = 6;

    readonly List<SynergyIconView> icons = new List<SynergyIconView>();   // 재사용 풀(파괴하지 않는다)

    // 덱(빈 슬롯 null 허용)을 받아 다시 그린다. null이면 전 아이콘이 접힌다.
    public void Refresh(IEnumerable<CardData> _deck)
    {
        int t_used = 0;

        // 미배선이면 조용히 건너뛴다 — 부분 배선으로 축소 화면을 만드는 게 이 프로젝트 UI의 관례다.
        if (iconParent != null && iconPrefab != null)
        {
            foreach (ActiveSynergy t_active in SynergyResolver.Resolve(_deck).Active)
            {
                if (t_active?.Synergy == null) continue;
                if (maxIcons > 0 && t_used >= maxIcons) break;

                SynergyIconView t_slot = SlotAt(t_used);
                if (t_slot == null) continue;

                t_slot.Bind(t_active.Synergy);   // 그림만 앉는다. pop/glow는 부르지 않으면 안 돈다
                t_slot.gameObject.SetActive(true);
                t_used++;
            }
        }

        // 남는 칸은 끈다(지우지 않는다). 레이아웃 그룹이 켜진 것만 세어 줄 폭을 잡으므로
        // 여기서 크기를 계산할 일이 없다 — 재면 여백·간격 값이 프리팹과 코드 두 곳에 생긴다.
        for (int t_i = t_used; t_i < icons.Count; t_i++)
            if (icons[t_i] != null) icons[t_i].gameObject.SetActive(false);

        // 활성 시너지가 없으면 줄을 통째로 접는다(인포바에 빈 자리가 남지 않게).
        if (iconParent != null) iconParent.gameObject.SetActive(t_used > 0);
    }

    // i번째 칸. 모자라면 그때 하나 더 찍어낸다 — 활성 시너지 수는 덱마다 달라 미리 정해둘 수 없다.
    SynergyIconView SlotAt(int _index)
    {
        while (icons.Count <= _index)
        {
            SynergyIconView t_new = Instantiate(iconPrefab, iconParent);
            t_new.name = "SynergyIcon_" + icons.Count;
            icons.Add(t_new);
        }
        return icons[_index];
    }
}
