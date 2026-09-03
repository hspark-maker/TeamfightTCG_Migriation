using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 한 칸(버튼)의 <b>선택/비선택 표시를 그 칸 자신이 소유</b>한다. 바꾸는 건 <b>색뿐</b>이다 —
/// 바탕 스프라이트는 프리팹 저작 그대로 둔다(칸마다 다른 그림을 쓰면 선택이 안 옮겨간 것처럼 보이므로
/// 같은 줄의 칸들은 같은 스프라이트로 저작해야 한다).
///
/// 켜고 끄기만 한다 — 어느 칸이 선택인지는 줄을 쥔 쪽(SettingsPanel 등)이 정한다.
/// </summary>
public class SelectionStateView : MonoBehaviour
{
    [Tooltip("색을 바꿀 바탕 이미지. 비우면 Button.targetGraphic 을 쓴다")]
    [SerializeField] Image targetImage;

    [Header("Tint")]
    [SerializeField] Color selectedTint   = Color.white;
    [SerializeField] Color unselectedTint = new Color(0.55f, 0.55f, 0.55f, 1f);

    Button button;
    Image  image;

    public bool IsSelected { get; private set; }

    void Awake()
    {
        this.button = GetComponent<Button>();
        this.image  = this.targetImage != null ? this.targetImage : this.button?.targetGraphic as Image;
    }

    /// <summary>선택 표시 갱신. 같은 값으로 여러 번 불러도 안전하다(창을 다시 열 때마다 통째로 다시 건다).</summary>
    public void SetSelected(bool _on)
    {
        IsSelected = _on;

        Color t_tint = _on ? this.selectedTint : this.unselectedTint;

        if (this.button == null)
        {
            if (this.image != null) this.image.color = t_tint;
            return;
        }

        // ColorTint 버튼이라 targetGraphic.color 를 직접 쓰면 상태 전이가 바로 덮는다 — ColorBlock 을 갈아끼운다.
        ColorBlock t_colors = this.button.colors;
        t_colors.normalColor      = t_tint;
        t_colors.highlightedColor = t_tint;
        t_colors.selectedColor    = t_tint;
        t_colors.pressedColor     = new Color(t_tint.r * 0.75f, t_tint.g * 0.75f, t_tint.b * 0.75f, t_tint.a);
        this.button.colors = t_colors;
    }
}
