using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>감정표현 버튼 하나 + 2×3 선택 표. 열고 · 고르고 · 닫는 것까지만 한다 —
/// 무엇이 뜨는지·어디에 뜨는지는 <see cref="EmoteDirector"/>가 정한다(발화 지점 하나).
///
/// 칸 6개는 **씬에서 저작**하고 여기서는 켜고 끄기만 한다(UI 자식 런타임 생성 금지 규약).
/// 목록이 6개보다 짧으면 남는 칸은 꺼진다 — 빈 버튼을 눌러 아무 일도 안 일어나는 상태를 만들지 않는다.
///
/// 표는 한 번 고르면 닫힌다. 열어 둔 채로 연타하게 두면 스티커가 계속 갈아 끼워져
/// "내가 뭘 냈는지"가 화면에 안 남는다.</summary>
public class EmotePickerUI : MonoBehaviour
{
    [Tooltip("표를 여는 버튼(전투 화면에 상시 노출).")]
    [SerializeField] Button openButton;

    [Tooltip("2×3 표 루트. 시작은 꺼진 상태다.")]
    [SerializeField] GameObject panel;

    [Tooltip("표 바깥을 눌러 닫는 투명 버튼. 없으면 다시 openButton을 눌러야 닫힌다.")]
    [SerializeField] Button closeBlocker;

    [Tooltip("칸 6개(왼쪽 위 → 오른쪽 아래 순서). 카탈로그 순서와 1:1이다.")]
    [SerializeField] Button[] slots = new Button[EmoteCatalog.Capacity];

    [Tooltip("칸에 그림을 그릴 Image 6개(칸과 같은 순서). 비워도 된다 — 그때는 글자만 쓴다.")]
    [SerializeField] Image[] slotIcons = new Image[EmoteCatalog.Capacity];

    [Tooltip("칸에 글자를 그릴 TMP 6개(칸과 같은 순서). 그림이 있는 감정표현은 자동으로 꺼진다.")]
    [SerializeField] TMP_Text[] slotLabels = new TMP_Text[EmoteCatalog.Capacity];

    void Awake()
    {
        if (this.openButton    != null) this.openButton.onClick.AddListener(Toggle);
        if (this.closeBlocker  != null) this.closeBlocker.onClick.AddListener(Close);

        for (int t_i = 0; t_i < this.slots.Length; t_i++)
        {
            if (this.slots[t_i] == null) continue;
            int t_index = t_i;   // 클로저가 루프 변수를 붙잡지 않게 복사
            this.slots[t_i].onClick.AddListener(() => Pick(t_index));
        }

        Close();
    }

    void OnDestroy()
    {
        if (this.openButton   != null) this.openButton.onClick.RemoveListener(Toggle);
        if (this.closeBlocker != null) this.closeBlocker.onClick.RemoveListener(Close);
    }

    public void Toggle()
    {
        if (this.panel == null) return;

        if (this.panel.activeSelf) { Close(); return; }
        Open();
    }

    /// <summary>표를 열면서 칸을 지금 카탈로그로 다시 그린다 — 에셋을 고쳐도 씬을 다시 저작하지 않게.</summary>
    public void Open()
    {
        EmoteCatalog t_catalog = EmoteDirector.Instance != null ? EmoteDirector.Instance.Catalog : null;

        for (int t_i = 0; t_i < this.slots.Length; t_i++)
        {
            EmoteEntry t_entry = t_catalog != null ? t_catalog.Get(t_i) : null;
            bool       t_on    = t_entry != null;

            if (this.slots[t_i] != null) this.slots[t_i].gameObject.SetActive(t_on);
            if (!t_on) continue;

            bool t_hasSprite = t_entry.sprite != null;

            if (t_i < this.slotIcons.Length && this.slotIcons[t_i] != null)
            {
                this.slotIcons[t_i].gameObject.SetActive(t_hasSprite);
                if (t_hasSprite) this.slotIcons[t_i].sprite = t_entry.sprite;
            }
            if (t_i < this.slotLabels.Length && this.slotLabels[t_i] != null)
            {
                this.slotLabels[t_i].gameObject.SetActive(!t_hasSprite);
                if (!t_hasSprite) this.slotLabels[t_i].text = t_entry.label;
            }
        }

        if (this.panel        != null) this.panel.SetActive(true);
        if (this.closeBlocker != null) this.closeBlocker.gameObject.SetActive(true);
    }

    public void Close()
    {
        if (this.panel        != null) this.panel.SetActive(false);
        if (this.closeBlocker != null) this.closeBlocker.gameObject.SetActive(false);
    }

    void Pick(int _index)
    {
        Close();
        EmoteDirector.Instance?.PlayLocal(_index);
    }
}
