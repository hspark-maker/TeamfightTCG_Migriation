#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>전투 중 카드를 **강제로 죽이는** 디버그 창(F2). 사망 시 발동하는 것들
/// (유산 왕관 비행 · 불사 부활 · 포식자 · 사망 연출)을 판을 끝까지 굴리지 않고 보기 위한 도구다.
///
/// 씬에 배선하지 않는다 — <see cref="Install"/>가 실행 시점에 자기 GameObject를 만든다.
/// 디버그 하나 때문에 전투 씬 YAML을 건드리면 씬 병합 충돌만 늘어난다(VfxDebugWindow는 테스트 씬 전용이라
/// 실제 전투 씬에는 없다). 에디터·개발빌드에서만 컴파일된다.
///
/// ⚠ 멀티에서는 동작하지 않는다. 한쪽 클라에서만 카드를 죽이면 그 순간부터 두 클라의 보드가 갈라진다
///   (결정론 계약 위반). 러너가 살아 있으면 창에 경고만 띄우고 버튼을 잠근다.
///
/// 죽이는 방법은 전투와 같은 경로다: 체력만큼 <see cref="CardInstance.TakeDamage"/> →
/// <see cref="AttackProcessor.RemoveDead"/>. 여기서 슬롯을 직접 비우면 Lethal/Removed 훅이 건너뛰어져
/// "디버그로 죽였을 때만 유산이 안 터지는" 가짜 증상이 생긴다.</summary>
public class BattleDebugKill : MonoBehaviour
{
    const float REF_HEIGHT = 1080f;   // IMGUI는 픽셀 단위 — 고해상도에서 글자가 작아지지 않게 스케일 기준을 둔다.

    static readonly KeyCode ToggleKey = KeyCode.F2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var t_go = new GameObject("[BattleDebugKill]");
        t_go.AddComponent<BattleDebugKill>();
        DontDestroyOnLoad(t_go);
    }

    Rect windowRect = new Rect(16f, 16f, 340f, 320f);
    bool open;

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey)) this.open = !this.open;
    }

    void OnGUI()
    {
        if (!this.open) return;

        // 창 좌표·크기는 1080 기준 "논리 픽셀"로 다루고 실제 픽셀 변환은 여기 한 곳에서(VfxDebugWindow와 같은 규약).
        Matrix4x4 t_prev = GUI.matrix;
        float     t_k    = Screen.height / REF_HEIGHT;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(t_k, t_k, 1f));

        this.windowRect = GUI.Window(GetInstanceID(), this.windowRect, DrawWindow, "카드 죽이기 (F2)");

        GUI.matrix = t_prev;
    }

    void DrawWindow(int _id)
    {
        if (IsMultiplayer())
        {
            GUILayout.Label("멀티 중에는 못 쓴다.\n한쪽만 죽으면 보드가 갈라진다(divergence).");
            GUI.DragWindow();
            return;
        }

        // 필드는 씬에서 찾는다 — 디버그가 GameInitializer 배선에 손을 뻗으면 그쪽 필드를 public으로 열어야 한다.
        BattleFieldView[] t_views = FindObjectsByType<BattleFieldView>(FindObjectsSortMode.None);
        if (t_views.Length == 0)
        {
            GUILayout.Label("전투 필드가 없다(전투 씬에서 열어라).");
            GUI.DragWindow();
            return;
        }

        foreach (BattleFieldView t_view in t_views)
        {
            BattleField t_field = t_view != null ? t_view.Field : null;
            if (t_field == null) continue;

            GUILayout.Label($"— {t_view.name} —");

            for (int t_i = 0; t_i < BattleField.SLOT_COUNT; t_i++)
            {
                CardInstance t_card = t_field.GetSlot(t_i);
                if (t_card == null || !t_card.IsAlive)
                {
                    GUILayout.Label($"  {t_i}: (빈 슬롯)");
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"  {t_i}: {Name(t_card)}  {t_card.hp}(+{t_card.bonusHp})", GUILayout.Width(200f));
                if (GUILayout.Button("죽이기")) Kill(t_field, t_view, t_card);
                GUILayout.EndHorizontal();
            }
        }

        GUI.DragWindow();
    }

    /// <summary>전투와 같은 순서로 죽인다 — 피해를 체력만큼 넣고 필드 정리를 돌린다.
    /// 추가 체력(덩치)까지 함께 깎아야 한 번에 죽는다. 무적·보호막이 각각 한 타를 삼킬 수 있어 최대 세 번 넣는다.</summary>
    static void Kill(BattleField _field, BattleFieldView _view, CardInstance _card)
    {
        _card.TakeDamage(int.MaxValue);
        if (_card.IsAlive) _card.TakeDamage(int.MaxValue);   // 무적/보호막 1회 소진분
        if (_card.IsAlive) _card.TakeDamage(int.MaxValue);   // 둘 다 있었을 때 남은 1회

        // [Lethal] → [Removed] → 슬롯 제거. 전투와 같은 함수라 훅 순서가 갈라지지 않는다.
        AttackProcessor.RemoveDead(_field);

        // 죽인 쪽 필드만 다시 그린다. 회복이 건너편까지 갔더라도(유산) 다음 턴 갱신이 따라잡는다 —
        // 여기서 모든 필드를 훑으면 디버그가 뷰 갱신 규칙의 두 번째 진실원이 된다.
        if (_view != null) _view.Refresh();
    }

    /// <summary>러너가 살아 있으면 멀티다(NetworkSession 없는 씬에서도 안전하게 false).
    /// 판정 기준은 GameInitializer가 모드를 가르는 것과 같은 <c>Runner.IsRunning</c>이다.</summary>
    static bool IsMultiplayer()
        => NetworkSession.Instance != null
        && NetworkSession.Instance.Runner != null
        && NetworkSession.Instance.Runner.IsRunning;

    static string Name(CardInstance _card) => _card != null ? CardCatalog.RequireSpec(_card.cardId).DisplayName : "?";
}
#endif
