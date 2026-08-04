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
    /// <summary>시너지 아이콘 오브젝트에 곱할 배율. 시너지 PNG는 512 캔버스에 배지가 ~60%만 차지해
    /// (나머지는 투명 여백) 같은 슬롯 크기의 키워드 아이콘 옆에서 혼자 작아 보인다.
    /// 이미지를 자르는 대신 오브젝트를 키워 보정한다 — 보이는 배지가 슬롯을 거의 채운다.
    /// 아이콘 PNG의 여백 비율을 바꾸면 이 값도 같이 바꿀 것(시너지 아이콘 크기의 단일 진실원).</summary>
    public const float IconPadCompensation = 1.55f;

    /// <summary>_parent를 비우고 _card의 시너지 아이콘을 깐다.
    /// 아이콘이 없는(icon 미배정) 시너지는 건너뛴다 — 빈 사각형이 뜨는 것보다 낫다.</summary>
    public static void Build(CardData _card, Transform _parent, GameObject _iconPrefab, bool _clearFirst = true)
    {
        if (_parent == null || _iconPrefab == null) return;

        if (_clearFirst) Clear(_parent);

        if (_card?.synergies == null) return;

        var t_shown = new HashSet<SynergyData>();
        foreach (SynergyData t_synergy in _card.synergies)
        {
            if (t_synergy == null) continue;
            if (!t_shown.Add(t_synergy)) continue;   // 카드가 같은 시너지를 중복 나열해도 1개만
            if (t_synergy.activeIcon == null) continue;

            GameObject t_obj = Object.Instantiate(_iconPrefab, _parent);

            Image t_img = t_obj.GetComponent<Image>();
            if (t_img != null)
            {
                t_img.sprite  = t_synergy.activeIcon;
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
        UIPoolManager.Instance?.AddOrUpdateUI<SynergyExplainPopupUI>(new SynergyExplainData
        {
            synergy  = _synergy,
            iconRect = _iconRect,
        });
    }

    static void Hide() => UIPoolManager.Instance?.HideUI<SynergyExplainPopupUI>();
}
