using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 덱 편집 화면 전용 시너지 표시(DeckEditPanel / MatchDeckEditPanel 배리언트에 부착).
//
// **로비 타이틀 옆의 DeckSynergyStrip과 일부러 다른 물건이다.** 그쪽은 "덱 목록에서 어느 덱인지"를
// 훑는 압축 표시라 칸을 덱에 맞춰 늘였다 줄이고, 여기는 편성 중 매 클릭마다 다시 그려지는 판이라
// 칸이 움직이면 방금 보던 아이콘이 옆으로 밀린다. 그래서 이 뷰는 **칸을 고정 저작한다** —
// cells에 시너지를 하나씩 박아두면 덱이 어떻게 바뀌어도 아이콘 위치가 그대로다.
// (덤으로 "이 게임에 어떤 시너지가 있는가"가 편성 내내 보인다 — 목표를 세울 수 있어야 편성이 된다.)
//
// 상태를 들지 않는다. 진실원은 DeckEditController.m_working 하나이고 이 뷰는 Refresh로 받아 그리기만 한다.
// 집계는 SynergyPreview 단독 — 활성 판정 규칙이 두 벌이 되면 편집 화면과 전투가 갈린다.
public class DeckEditSynergyView : MonoBehaviour
{
    // 칸 하나 = 시너지 하나. MonoBehaviour로 쪼개지 않고 [Serializable]로 묶은 이유는
    // 배선 실수를 막기 위해서다 — Image[]와 SynergyData[]를 따로 두면 인덱스가 어긋나도 아무도 모른다.
    [Serializable]
    class Cell
    {
        [Tooltip("이 칸이 맡을 시너지. 비워두면 칸 전체가 꺼진다(칸을 미리 여유 있게 만들어 둘 때).")]
        public SynergyData synergy;

        public Image      icon;
        public TMP_Text   countText;    // "2/4" 표기(선택)
        public GameObject activeMark;   // 활성일 때만 켜는 장식(글로우 등, 선택)
    }

    [Tooltip("시너지 칸들. 순서·개수는 여기서 저작한다 — 코드가 늘리거나 줄이지 않는다.")]
    [SerializeField] Cell[] cells;

    [Tooltip("체크하면 덱에 한 장도 없는 시너지는 칸을 통째로 숨긴다. " +
             "해제(기본)면 0장 시너지도 비활성 아이콘으로 자리를 지킨다.")]
    [SerializeField] bool hideWhenAbsent = false;

    /// <summary>편성 상태를 받아 전 칸을 다시 그린다. 빈 칸(null)이 섞여 있어도 된다 —
    /// 집계가 알아서 무시한다. 편집 중 매 변경마다 불리므로 할당을 최소로 유지할 것.</summary>
    public void Refresh(IEnumerable<CardData> _deck)
    {
        if (cells == null) return;

        // 덱에 실제로 등장한 시너지만 진행도가 나온다. 칸은 그보다 많을 수 있으므로(0장 시너지)
        // 조회 실패를 정상 경로로 다룬다 — 아래 Bind가 null을 "0장"으로 그린다.
        Dictionary<SynergyData, SynergyProgress> t_byData = BuildLookup(_deck);

        for (int t_i = 0; t_i < cells.Length; t_i++)
        {
            Cell t_cell = cells[t_i];
            if (t_cell == null) continue;

            t_byData.TryGetValue(t_cell.synergy, out SynergyProgress t_progress);
            Bind(t_cell, t_progress);
        }
    }

    /// <summary>전 칸을 0장 상태로 되돌린다. 편집을 닫을 때 직전 덱이 남아 보이지 않게.</summary>
    public void Clear() => Refresh(null);

    // 진행도를 시너지로 찾을 수 있게 뒤집는다. SynergyPreview가 리스트로 주는 이유는
    // 그쪽 소비자(스트립)가 정렬 순서를 그대로 쓰기 때문인데, 여기는 칸이 고정이라 순서를 안 쓴다.
    static Dictionary<SynergyData, SynergyProgress> BuildLookup(IEnumerable<CardData> _deck)
    {
        var t_map = new Dictionary<SynergyData, SynergyProgress>();

        List<SynergyProgress> t_all = SynergyPreview.Resolve(_deck);
        for (int t_i = 0; t_i < t_all.Count; t_i++)
        {
            SynergyProgress t_p = t_all[t_i];
            if (t_p?.Synergy == null) continue;

            t_map[t_p.Synergy] = t_p;
        }

        return t_map;
    }

    // _progress가 null = 덱에 이 시너지 카드가 한 장도 없다. 비활성과 같은 그림이되 숫자만 0으로 간다.
    void Bind(Cell _cell, SynergyProgress _progress)
    {
        // 시너지 미배정 칸은 그릴 게 없다. 저작 중 빈 칸을 남겨둘 수 있게 끄기만 하고 넘어간다.
        if (_cell.synergy == null)
        {
            SetCellActive(_cell, false);
            return;
        }

        int  t_count  = _progress?.Count ?? 0;
        bool t_active = _progress?.IsActive ?? false;

        if (hideWhenAbsent && t_count <= 0)
        {
            SetCellActive(_cell, false);
            return;
        }
        SetCellActive(_cell, true);

        if (_cell.icon != null)
        {
            // 활성/비활성을 **그림으로** 가른다(알파를 깎지 않는다) — 아이콘 에셋이 쌍으로 있는 이유가 그거고,
            // 실루엣이 같은 채 흐리기만 하면 편성 중 곁눈질로 열림/안 열림이 안 갈린다.
            // 비활성 그림 미배정 시너지는 활성 그림으로 떨어진다(무동작 안전).
            Sprite t_sprite = t_active ? _cell.synergy.activeIcon
                                       : (_cell.synergy.inactiveIcon ?? _cell.synergy.activeIcon);

            _cell.icon.sprite  = t_sprite;
            _cell.icon.enabled = t_sprite != null;
        }

        if (_cell.countText != null)
            _cell.countText.text = BuildCount(_cell.synergy, _progress, t_count);

        if (_cell.activeMark != null) _cell.activeMark.SetActive(t_active);
    }

    // "현재/필요" 표기. 필요치는 아직 못 연 다음 티어가 기준이고, 최고 티어까지 열었으면 그 티어의 요구치를 쓴다
    // (Goal은 다음 티어가 없을 때 0이라 그대로 쓰면 "6/0"이 된다).
    // 덱에 한 장도 없어 진행도 자체가 없는 칸은 티어 정의에서 최소 요구치를 직접 꺼낸다 — "0/2"가 보여야
    // 몇 장부터 열리는지 알 수 있다. 티어가 없는 시너지는 분모를 못 만드니 숫자만 찍는다.
    static string BuildCount(SynergyData _synergy, SynergyProgress _progress, int _count)
    {
        int t_required = _progress?.NextTier?.requiredCount
                      ?? _progress?.ActiveTier?.requiredCount
                      ?? LowestRequirement(_synergy);

        return t_required > 0 ? $"{_count}/{t_required}" : _count.ToString();
    }

    // 가장 먼저 열리는 티어의 요구 장수. 티어 배열은 오름차순 "권장"일 뿐이라 순서에 기대지 않고 최소값을 찾는다.
    static int LowestRequirement(SynergyData _synergy)
    {
        SynergyTier[] t_tiers = _synergy != null ? _synergy.tiers : null;
        if (t_tiers == null) return 0;

        int t_min = 0;
        for (int t_i = 0; t_i < t_tiers.Length; t_i++)
        {
            SynergyTier t_tier = t_tiers[t_i];
            if (t_tier == null || t_tier.requiredCount <= 0) continue;

            if (t_min == 0 || t_tier.requiredCount < t_min) t_min = t_tier.requiredCount;
        }

        return t_min;
    }

    // 칸의 루트를 켜고 끈다. 아이콘이 미배선이면 끌 대상이 없으므로 조용히 넘어간다
    // (부분 배선으로 축소 화면을 만드는 게 이 프로젝트 UI의 관례다).
    static void SetCellActive(Cell _cell, bool _on)
    {
        GameObject t_root = _cell.icon != null ? _cell.icon.gameObject : null;
        if (t_root == null) return;

        if (t_root.activeSelf != _on) t_root.SetActive(_on);
    }
}
