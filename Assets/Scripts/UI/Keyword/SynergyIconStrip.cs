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
    /// <summary>_parent를 비우고 _card의 시너지 아이콘을 깐다.
    /// 아이콘이 없는(icon 미배정) 시너지는 건너뛴다 — 빈 사각형이 뜨는 것보다 낫다.</summary>
    public static void Build(CardData _card, Transform _parent, GameObject _iconPrefab, bool _clearFirst = true)
    {
        if (_parent == null || _iconPrefab == null) return;

        if (_clearFirst)
            foreach (Transform t_child in _parent)
                Object.Destroy(t_child.gameObject);

        if (_card?.synergies == null) return;

        var t_shown = new HashSet<SynergyData>();
        foreach (SynergyData t_synergy in _card.synergies)
        {
            if (t_synergy == null) continue;
            if (!t_shown.Add(t_synergy)) continue;   // 카드가 같은 시너지를 중복 나열해도 1개만
            if (t_synergy.icon == null) continue;

            GameObject t_obj = Object.Instantiate(_iconPrefab, _parent);

            Image t_img = t_obj.GetComponent<Image>();
            if (t_img != null)
            {
                t_img.sprite  = t_synergy.icon;
                t_img.enabled = true;
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
            t_btn.onPointerDown = () => Show(t_captured, t_rt);
            t_btn.onPointerUp   = Hide;
        }
    }

    static void Show(SynergyData _synergy, RectTransform _iconRect)
    {
        UIPoolManager.Instance?.AddOrUpdateUI<SynergyExplainPopupUI>(new SynergyExplainData
        {
            synergy  = _synergy,
            iconRect = _iconRect,
        });
    }

    static void Hide() => UIPoolManager.Instance?.HideUI<SynergyExplainPopupUI>();
}
