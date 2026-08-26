using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 덱 대표 이미지 조회 창구. 카탈로그(SO) 주입은 BootInstaller가 한 번만 한다.
/// 세이브는 키 문자열만 알고 스프라이트는 모른다 — 그 사이 변환을 여기 한 곳에 가둔다.
/// </summary>
public static class DeckImages
{
    static DeckImageCatalog s_catalog;

    public static void SetSource(DeckImageCatalog _catalog) => s_catalog = _catalog;

    // 신규 덱에 붙일 이미지 키. 카탈로그 미배선이면 빈 문자열 → 표시는 폴백으로 떨어진다.
    public static string PickRandomKey()
    {
        if (s_catalog == null) return string.Empty;

        var t_images = s_catalog.Images;
        if (t_images.Count == 0) return string.Empty;

        // 슬롯이 6개뿐이라 그림이 겹치면 바로 눈에 띈다 → 미사용 후보를 먼저 소진한다.
        var t_unused = new List<string>();
        var t_all    = new List<string>();
        for (int t_i = 0; t_i < t_images.Count; t_i++)
        {
            if (t_images[t_i] == null) continue;

            string t_key = t_images[t_i].name;
            if (string.IsNullOrEmpty(t_key)) continue;

            t_all.Add(t_key);
            if (!IsKeyInUse(t_key)) t_unused.Add(t_key);
        }

        var t_pool = t_unused.Count > 0 ? t_unused : t_all;   // 전부 쓰였으면 중복을 허용한다

        return t_pool.Count > 0 ? t_pool[Random.Range(0, t_pool.Count)] : string.Empty;
    }

    public static Sprite Resolve(string _key) => s_catalog != null ? s_catalog.Find(_key) : null;

    // 덱 슬롯 하나의 표시용 이미지. 덱 목록·매치 선택 화면이 같은 그림을 보게 하는 단일 진실원이다.
    // 이미지 키가 붙기 전에 저장된 덱(구 세이브)과 카탈로그 미배선 상태는 옛 규칙인 "첫 카드 아트"로 떨어진다.
    public static Sprite ResolveForSlot(int _slotIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= DeckSaveManager.SLOT_COUNT) return null;

        Sprite t_image = Resolve(DeckSaveManager.GetImageKey(_slotIndex));

        return t_image != null ? t_image : ResolveFromFirstCard(DeckSaveManager.GetSlot(_slotIndex));
    }

    static bool IsKeyInUse(string _key)
    {
        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
            if (DeckSaveManager.GetImageKey(t_i) == _key) return true;

        return false;
    }

    // 덱 첫 카드의 deckPreview → 없으면 일반 카드 아트 → 둘 다 없으면 null.
    // 폴백을 CardVisualRules에 맡기는 건 여기서 카드 아트를 직접 적으면
    // 같은 카드가 덱 목록에서만 다른 그림으로 뜨는 드리프트가 생기기 때문이다.
    static Sprite ResolveFromFirstCard(List<CardData> _deck)
    {
        if (_deck == null || _deck.Count == 0) return null;

        var t_first = _deck[0];
        if (t_first == null) return null;

        return CardVisualRules.PickDeckBanner(t_first);
    }
}
