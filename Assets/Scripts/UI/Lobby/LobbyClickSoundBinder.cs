using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 버튼에 공통 클릭음을 한 번에 걸어 준다 — 버튼마다 UIClickSound를 붙이지 않아도 되게.</summary>
public static class LobbyClickSoundBinder
{
    static readonly HashSet<Button> s_bound = new HashSet<Button>();

    /// <summary>_root 아래의 버튼에 공통 클릭음을 건다. 여러 번 불러도 같은 버튼에 두 번 걸리지 않는다.</summary>
    /// <param name="_except">자기 소리를 따로 내는 가지(탭바 등). 이 아래 버튼에는 공통음을 얹지 않는다.</param>
    public static void Bind(Transform _root, Transform _except = null)
    {
        if (_root == null) return;

        // 꺼져 있는 탭 패널의 버튼까지 한 번에 잡는다 — 탭을 열 때마다 다시 훑지 않아도 되게.
        Button[] t_buttons = _root.GetComponentsInChildren<Button>(includeInactive: true);

        foreach (Button t_button in t_buttons)
        {
            if (t_button == null || !s_bound.Add(t_button)) continue;

            // 이 버튼만의 소리를 이미 저작해 뒀으면 공통음을 얹지 않는다.
            if (t_button.GetComponent<UIClickSound>() != null) continue;
            if (_except != null && t_button.transform.IsChildOf(_except)) continue;

            t_button.onClick.AddListener(PlayClick);
        }
    }

    /// <summary>배선 기록을 비운다. 로비 씬을 다시 열 때 부른다 — 파괴된 버튼이 쌓이지 않게.</summary>
    public static void Clear() => s_bound.Clear();

    static void PlayClick() => SoundManager.Instance?.PlayCue(EOutgameSound.ButtonPress);
}
