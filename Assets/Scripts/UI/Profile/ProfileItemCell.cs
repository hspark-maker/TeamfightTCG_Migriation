using System;
using UnityEngine;
using UnityEngine.UI;

// 프로필 편집 팝업의 선택 칸 하나(아바타·프레임 공용). 그림은 ProfileAvatarView가 통째로 그린다 —
// 칸은 "무엇을 보여줄지"만 정하고 판·얼굴·링의 층 구조는 알지 않는다.
//
// 칸은 자기 축만 보여준다 — 아바타 칸엔 아바타 그림만, 프레임 칸엔 프레임 링만.
// 둘을 겹친 실제 조합은 팝업 위쪽 미리보기가 맡는다.
//
// 미소유 룩(딤 + 클릭 차단)은 지금 호출측이 항상 소유(true)로만 부르지만 미리 심어 둔다 —
// 잠금 상품이 생기는 순간 이 클래스를 다시 열지 않기 위해서다.
public class ProfileItemCell : MonoBehaviour
{
    [Tooltip("판·얼굴·링 한 덩어리.")]
    [SerializeField] ProfileAvatarView avatarView;
    [Tooltip("선택 배지(체크). 선택된 칸에서만 켠다.")]
    [SerializeField] GameObject selectedMark;
    [Tooltip("선택 칸을 감싸는 초점 링. 밑판이 가운데를 덮어 삐져나온 테두리만 보이므로 밑판보다 커야 한다.")]
    [SerializeField] GameObject focusRing;
    [SerializeField] Button button;
    [Tooltip("미소유 딤. 칸 프리팹에 배선돼 있다 — 미배선이면 딤 없이 버튼만 잠근다.")]
    [SerializeField] CanvasGroup dimGroup;

    [Range(0f, 1f)]
    [Tooltip("미소유일 때의 알파.")]
    [SerializeField] float dimAlpha = 0.4f;

    [Header("누름 반응")]
    [Tooltip("누름 입력 중계. 미배선이면 누름 연출 없이 클릭만 동작한다.")]
    [SerializeField] CardPressRelay pressRelay;

    [Tooltip("누르고 있는 동안 오므라드는 정도. 밑판은 제자리에 남아 이만큼이 눌린 깊이로 읽힌다.")]
    [SerializeField] float pressShrink = 0.08f;

    [Tooltip("오므라드는 데 걸리는 시간(초). 손가락을 따라오는 느낌이라 짧아야 한다.")]
    [SerializeField] float pressDuration = 0.07f;

    [Tooltip("뗄 때 한 번 부푸는 정도.")]
    [SerializeField] float releasePop = 0.10f;

    [Tooltip("부푸는 데 걸리는 시간(초).")]
    [SerializeField] float popDuration = 0.10f;

    [Tooltip("원래 크기로 돌아오는 데 걸리는 시간(초).")]
    [SerializeField] float settleDuration = 0.14f;

    string m_id;
    Action<string> m_onClick;

    public Button Button => this.button;

    public string Id => this.m_id;

    /// <summary>칸에 항목 하나를 묶는다. 클릭 리스너는 매번 갈아 끼운다(재바인딩 시 중복 등록 방지).</summary>
    public void Bind(string _id, in ProfileLook _look, EProfileAxis _axis, bool _owned, Action<string> _onClick)
    {
        this.m_id = _id;

        if (this.avatarView != null)
        {
            this.avatarView.Render(_look);
            this.avatarView.ShowOnly(_axis);
        }

        if (this.dimGroup != null) this.dimGroup.alpha = _owned ? 1f : this.dimAlpha;

        this.m_onClick = _onClick;

        if (this.pressRelay != null)
        {
            this.pressRelay.onPressStart = this.HandlePressStart;   // 대입이라 재바인딩해도 중복되지 않는다
            this.pressRelay.onPressEnd   = this.HandlePressEnd;
            this.pressRelay.SetInteractable(_owned);
        }

        if (this.button != null)
        {
            this.button.onClick.RemoveAllListeners();
            this.button.interactable = _owned;
            if (_owned && _onClick != null) this.button.onClick.AddListener(this.HandleClick);
        }

        this.SetSelected(false);
    }

    /// <summary>선택 표시를 켜고 끈다. 선택은 패널이 단일 진실원으로 쥔다 — 칸이 스스로 뒤집지 않는다.</summary>
    public void SetSelected(bool _on)
    {
        if (this.selectedMark != null) this.selectedMark.SetActive(_on);
        if (this.focusRing != null) this.focusRing.SetActive(_on);
    }

    // 누르는 동안 오므리고, 떼면 부풀렸다 되돌린다. 밑판은 어느 쪽에서도 움직이지 않는다.
    void HandlePressStart()
    {
        if (this.avatarView != null) this.avatarView.PressDown(this.pressShrink, this.pressDuration);
    }

    void HandlePressEnd()
    {
        if (this.avatarView != null) this.avatarView.PressUp(this.releasePop, this.popDuration, this.settleDuration);
    }

    void HandleClick()
    {
        if (this.m_onClick != null) this.m_onClick(this.m_id);
    }
}
