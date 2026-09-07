using UnityEngine;

/// <summary>모험 전투에만 챕터 배경을 갈아끼운다. 다른 모드(랭크전·튜토리얼)는 씬 저작값 그대로다.
///
/// Awake에서 한 번만 읽는 이유: <see cref="AdventureRun"/>은 static이라 씬을 넘어 살아 있고,
/// 그 값은 전투가 끝날 때 <c>TurnRunner.Cleanup</c>의 <see cref="AdventureRun.End"/>가 지운다.
/// 나중에 다시 읽으면 이미 비워진 뒤라 배경이 도로 저작값으로 튄다.
///
/// 되돌리는 경로를 두지 않는 이유: 배틀 씬은 판마다 새로 로드되므로 저작값이 저절로 복구된다.
/// 에디터 플레이 중 바꾼 sprite도 플레이 모드가 끝나면 Unity가 버린다(씬이 더러워지지 않는다).</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class BattleBackgroundView : MonoBehaviour
{
    void Awake()
    {
        Sprite t_background = AdventureRun.BattleBackground;
        if (t_background == null) return;   // 모험이 아니거나 챕터에 배경 미저작 — 저작값 유지

        SpriteRenderer t_renderer = GetComponent<SpriteRenderer>();
        if (t_renderer == null) return;

        WarnIfSizeMismatch(t_renderer.sprite, t_background);

        t_renderer.sprite = t_background;
    }

    // Battle_BG의 localScale은 저작 배경 하나에 맞춰 비균등으로 늘려 둔 값이라 코드가 손대지 않는다.
    // 크기·비율이 다른 그림을 끼우면 그 스케일 그대로 늘어나므로, 화면에서만 드러나기 전에 여기서 말한다.
    // 규약 위반을 막지는 않는다 — 배경은 곁들이라 진입을 세울 이유가 없다.
    static void WarnIfSizeMismatch(Sprite _authored, Sprite _next)
    {
#if UNITY_EDITOR
        if (_authored == null || _next == null) return;

        Vector2 t_authored = _authored.bounds.size;
        Vector2 t_next     = _next.bounds.size;
        if (Mathf.Approximately(t_authored.x, t_next.x) && Mathf.Approximately(t_authored.y, t_next.y)) return;

        Debug.LogWarning(
            $"[BattleBackgroundView] 챕터 배경 '{_next.name}'의 크기가 저작 배경 '{_authored.name}'과 다르다 "
          + $"({t_next.x:F2}x{t_next.y:F2} vs {t_authored.x:F2}x{t_authored.y:F2}) — "
          + "Battle_BG의 비균등 스케일이 그대로 먹어 화면에서 늘어난다. 같은 픽셀 크기·PPU로 저작해라.");
#endif
    }
}
