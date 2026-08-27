using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 카드의 시너지 아이콘 줄을 깔고 "누르면 설명 팝업" 배선까지 하는 공용 지점.
/// 덱 편성의 <see cref="CardElement"/>와 카드 정보 창의 <see cref="PooledCardElement"/>가 같이 쓴다 —
/// 두 곳에 따로 두면 중복 제거 규칙이나 팝업 배선이 갈린다.
/// </summary>
public static class SynergyIconStrip
{
    /// <summary>시너지 아이콘 오브젝트에 곱할 배율. 시너지 PNG는 512 캔버스에 배지가 일부만 차지하고
    /// 나머지가 투명 여백이라, 같은 슬롯 크기의 키워드 아이콘 옆에서 혼자 작아 보인다.
    /// 이미지를 자르는 대신 오브젝트를 키워 보정한다 — 두 아이콘의 **보이는** 크기가 같아진다.
    ///
    /// 값의 근거는 실측이다: 알파 경계 기준으로 캔버스를 채우는 비율이 키워드 0.869, 시너지 0.624 →
    /// 0.869 / 0.624 ≈ 1.39. (이전 1.55는 시너지가 11% 더 커 보였다.)
    /// 아이콘 PNG의 여백 비율을 바꾸면 이 값도 같이 잴 것(시너지 아이콘 크기의 단일 진실원).</summary>
    public const float IconPadCompensation = 1.39f;

    /// <summary>비활성인데 전용 회색 아이콘(inactiveIcon)이 없을 때 씌우는 색. 채도를 죽여 "아직 아니다"로 읽히게.</summary>
    static readonly Color InactiveTint = new Color(0.45f, 0.45f, 0.5f, 0.75f);

    /// <summary>_parent를 비우고 _card의 시너지 아이콘을 깐다.
    /// 아이콘이 없는(icon 미배정) 시너지는 건너뛴다 — 빈 사각형이 뜨는 것보다 낫다.</summary>
    /// <param name="_state">지금 필드의 확정 시너지 스냅샷. 넘기면 <b>활성이 앞, 비활성이 뒤</b>로 정렬되고
    /// 비활성은 회색(inactiveIcon)으로 그려진다. null이면 전부 활성으로 본다(덱 편성처럼 필드가 없는 화면).</param>
    public static void Build(CardData _card, Transform _parent, GameObject _iconPrefab,
                             SynergyState _state = null, bool _clearFirst = true)
    {
        if (_parent == null || _iconPrefab == null) return;

        if (_clearFirst) Clear(_parent);

        if (_card?.synergies == null) return;

        // 정렬·중복 제거 규칙은 카드 배지와 같은 곳(CardVisualRules) — 두 곳이 갈리면 같은 카드인데
        // 배지 순서와 정보창 순서가 달라진다. 여기선 상한만 풀어(전부 표시) 쓴다.
        List<SynergyData> t_ordered = CardVisualRules.CollectSynergyBadges(
            _card.synergies, _state, _card.synergies.Length);

        foreach (SynergyData t_synergy in t_ordered)
        {
            if (t_synergy == null) continue;

            bool   t_active = _state == null || CardVisualRules.IsSynergyActive(_state, t_synergy);
            Sprite t_sprite = t_active ? t_synergy.activeIcon
                                       : (t_synergy.inactiveIcon != null ? t_synergy.inactiveIcon : t_synergy.activeIcon);
            if (t_sprite == null) continue;

            GameObject t_obj = Object.Instantiate(_iconPrefab, _parent);

            Image t_img = t_obj.GetComponent<Image>();
            if (t_img != null)
            {
                t_img.sprite  = t_sprite;
                t_img.enabled = true;
                // 전용 회색 아이콘이 없는 시너지도 비활성으로 읽히게 — 아트가 채워지면 자연히 원래 색이 산다.
                t_img.color = t_active || t_synergy.inactiveIcon != null ? Color.white : InactiveTint;
            }

            // 키워드 아이콘 프리팹을 재사용하는 경우 롱프레스 배선이 딸려오므로 끊는다.
            LongPressDetector t_lp = t_obj.GetComponent<LongPressDetector>();
            if (t_lp != null) t_lp.OnLongPress = null;
            KeywordIconButton t_kb = t_obj.GetComponent<KeywordIconButton>();
            if (t_kb != null) t_kb.onPointerUp = null;

            SynergyIconButton t_btn = t_obj.GetComponent<SynergyIconButton>();
            if (t_btn == null) t_btn = t_obj.AddComponent<SynergyIconButton>();

            RectTransform t_rt       = t_obj.GetComponent<RectTransform>();
            SynergyData   t_captured = t_synergy;

            // 투명 여백 보정. localScale이라 레이아웃 칸 크기는 그대로 — 보이는 배지만 커진다.
            if (t_rt != null) t_rt.localScale = Vector3.one * IconPadCompensation;
            t_btn.onPointerDown = () => Show(t_captured, t_rt);
            t_btn.onPointerUp   = Hide;
        }
    }

    /// <summary>아이콘 줄 비우기. 시너지를 아예 감춰야 하는 구간(튜토리얼 미도입)에서 쓴다 —
    /// 풀에서 재사용된 창에 직전 카드의 아이콘이 남지 않게 <b>지우는 것까지</b>가 표시 규칙의 일부다.</summary>
    public static void Clear(Transform _parent)
    {
        if (_parent == null) return;
        foreach (Transform t_child in _parent)
            Object.Destroy(t_child.gameObject);
    }

    static void Show(SynergyData _synergy, RectTransform _iconRect)
    {
        ExplainPopupData t_data = ExplainPopupData.ForSynergy(_synergy);
        if (t_data == null) return;

        t_data.iconRect = _iconRect;
        UIPoolManager.Instance?.AddOrUpdateUI<ExplainPopupUI>(t_data);
    }

    static void Hide() => UIPoolManager.Instance?.HideUI<ExplainPopupUI>();
}
