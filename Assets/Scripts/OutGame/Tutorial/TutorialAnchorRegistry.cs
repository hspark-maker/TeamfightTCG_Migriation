using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 튜토리얼 타깃 위젯의 static 등록소. 씬의 TutorialAnchor가 자기 수명주기로 등록/해제한다.
// 타깃은 전부 씬 고정 단일 인스턴스라 키 하나당 1개만 보관한다(id 구분자 불필요).
// Button을 함께 보관하는 이유: 게이트가 스텝 완료를 onClick 리스너로 감지한다.
public static class TutorialAnchorRegistry
{
    // 등록된 타깃 1건. Button은 없을 수 있다(클릭 대상이 아닌 순수 하이라이트 타깃).
    struct Entry
    {
        public RectTransform rect;
        public Button button;
    }

    static readonly Dictionary<EOutgameTutorialAnchor, Entry> s_entries = new Dictionary<EOutgameTutorialAnchor, Entry>();

    // 등록 통지 — 앵커가 나중에 등장하는 경우(탭 전환·개봉 완료 노출)를 게이트가 기다렸다 켜지게 한다.
    public static event Action<EOutgameTutorialAnchor> OnRegistered;

    public static void Register(EOutgameTutorialAnchor _key, RectTransform _rect, Button _button)
    {
        if (_key == EOutgameTutorialAnchor.None) return;
        if (_rect == null) return;

        s_entries[_key] = new Entry { rect = _rect, button = _button };
        OnRegistered?.Invoke(_key);
    }

    public static void Unregister(EOutgameTutorialAnchor _key)
    {
        if (_key == EOutgameTutorialAnchor.None) return;

        s_entries.Remove(_key);
    }

    // 미등록·파괴된 타깃이면 false. 게이트는 false일 때 대기 상태로 남는다.
    public static bool TryGet(EOutgameTutorialAnchor _key, out RectTransform _rect, out Button _button)
    {
        _rect = null;
        _button = null;

        if (!s_entries.TryGetValue(_key, out var t_entry)) return false;

        // 씬 언로드로 OnDisable 없이 파괴된 stale 항목 정리(Unity의 fake-null 판정).
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
