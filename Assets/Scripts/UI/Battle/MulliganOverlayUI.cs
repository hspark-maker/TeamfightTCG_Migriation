using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>멀리건 문구와 스킵 버튼을 표시한다. 전체 화면 딤은 Canvas 형제인 ScreenDim이 소유한다.</summary>
public class MulliganOverlayUI : MonoBehaviour
{
    [SerializeField] TMP_Text instructionText;
    [SerializeField] Button skipButton;

    public bool SkipPressed { get; private set; }

    bool wired;

    public void Show(string _message, bool _showSkip = true)
    {
        this.SkipPressed = false;
        gameObject.SetActive(true);

        if (this.instructionText != null) this.instructionText.text = _message;
        if (this.skipButton == null) return;

        this.skipButton.gameObject.SetActive(_showSkip);
        if (this.wired) return;
        this.skipButton.onClick.AddListener(() => this.SkipPressed = true);
        this.wired = true;
    }

    /// <summary>지정한 화면 영역만 비워 두고 나머지를 공용 딤으로 가린다.</summary>
    public void SetFocusHole(Rect _screenRect)
    {
        if (_screenRect.width <= 0f || _screenRect.height <= 0f) return;
        ScreenDim.ShowWithHole(this, _screenRect);
    }

    public void Hide()
    {
        this.SkipPressed = false;
        ScreenDim.Hide(this);
        gameObject.SetActive(false);
    }

    void OnDisable() => ScreenDim.Hide(this);
}
