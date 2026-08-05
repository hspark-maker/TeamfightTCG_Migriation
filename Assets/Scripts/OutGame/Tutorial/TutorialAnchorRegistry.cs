using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 튜토리얼 타깃 위젯의 static 등록소(키 하나당 1건, Button은 없을 수 있다)
public static class TutorialAnchorRegistry
{
    // 등록된 타깃 1건
    struct Entry
    {
        public RectTransform rect;
        public Button button;
    }

    static readonly Dictionary<EOutgameTutorialAnchor, Entry> s_entries = new Dictionary<EOutgameTutorialAnchor, Entry>();

    // 앵커가 나중에 등장하는 경우(탭 전환·개봉 완료 노출)를 게이트가 기다렸다 켜지게 하는 등록 통지
    public static event Action<EOutgameTutorialAnchor> OnRegistered;

    // 타깃 등록(같은 키는 나중 등록이 가져간다)
    public static void Register(EOutgameTutorialAnchor _key, RectTransform _rect, Button _button)
    {
        if (_key == EOutgameTutorialAnchor.None) return;
        if (_rect == null) return;

        s_entries[_key] = new Entry { rect = _rect, button = _button };
        OnRegistered?.Invoke(_key);
    }

    // 키만 보고 해제
    public static void Unregister(EOutgameTutorialAnchor _key)
    {
        if (_key == EOutgameTutorialAnchor.None) return;

        s_entries.Remove(_key);
    }

    // 지금 등록된 주인이 _rect일 때만 해제(같은 키를 공유하는 다른 화면의 등록을 날리지 않게)
    public static void Unregister(EOutgameTutorialAnchor _key, RectTransform _rect)
    {
        if (_key == EOutgameTutorialAnchor.None) return;
        if (!s_entries.TryGetValue(_key, out var t_entry)) return;
        if (t_entry.rect != _rect) return;

        s_entries.Remove(_key);
    }

    // 등록된 타깃 조회 — 미등록·파괴됐으면 false
    public static bool TryGet(EOutgameTutorialAnchor _key, out RectTransform _rect, out Button _button)
    {
        _rect = null;
        _button = null;

        if (!s_entries.TryGetValue(_key, out var t_entry)) return false;

        if (t_entry.rect == null)
        {
            s_entries.Remove(_key);
            return false;
        }

        _rect = t_entry.rect;
        _button = t_entry.button;
        return true;
    }
}
