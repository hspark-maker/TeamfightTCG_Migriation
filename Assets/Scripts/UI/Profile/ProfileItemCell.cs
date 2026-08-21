using System;
using UnityEngine;
using UnityEngine.UI;

// 프로필 편집 팝업의 선택 칸 하나(아바타·프레임 공용). 그림은 ProfileAvatarView가 통째로 그린다 —
// 칸은 "무엇을 보여줄지"만 정하고 판·얼굴·링의 층 구조는 알지 않는다.
//
// 아바타 칸은 판·얼굴이 그 칸 것이고 링이 현재 드래프트, 프레임 칸은 그 반대다.
// 어느 쪽이든 칸 하나가 "지금 고르면 이렇게 보인다"를 그대로 보여준다.
//
// 미소유 룩(딤 + 클릭 차단)은 지금 호출측이 항상 소유(true)로만 부르지만 미리 심어 둔다 —
// 잠금 상품이 생기는 순간 이 클래스를 다시 열지 않기 위해서다.
public class ProfileItemCell : MonoBehaviour
{
    [Tooltip("판·얼굴·링 한 덩어리.")]
    [SerializeField] ProfileAvatarView avatarView;
    [Tooltip("선택 테두리. 선택된 칸에서만 켠다.")]
    [SerializeField] GameObject selectedMark;
    [SerializeField] Button button;
    [Tooltip("미소유 딤. 칸 프리팹에 배선돼 있다 — 미배선이면 딤 없이 버튼만 잠근다.")]
    [SerializeField] CanvasGroup dimGroup;

    [Range(0f, 1f)]
    [Tooltip("미소유일 때의 알파.")]
    [SerializeField] float dimAlpha = 0.4f;

    string m_id;

    public Button Button => this.button;

    public string Id => this.m_id;

    /// <summary>칸에 항목 하나를 묶는다. 클릭 리스너는 매번 갈아 끼운다(재바인딩 시 중복 등록 방지).</summary>
    public void Bind(string _id, in ProfileLook _look, bool _owned, Action<string> _onClick)
    {
        this.m_id = _id;

        this.SetLook(_look);

        if (this.dimGroup != null) this.dimGroup.alpha = _owned ? 1f : this.dimAlpha;

        if (this.button != null)
        {
            this.button.onClick.RemoveAllListeners();
            this.button.interactable = _owned;
            if (_owned && _onClick != null) this.button.onClick.AddListener(() => _onClick(_id));
        }

        this.SetSelected(false);
    }

    /// <summary>그림만 갈아 끼운다. 드래프트가 바뀔 때마다 칸을 다시 만들지 않기 위한 창구다.</summary>
    public void SetLook(in ProfileLook _look)
    {
        if (this.avatarView != null) this.avatarView.Render(_look);
    }

    /// <summary>선택 테두리를 켜고 끈다. 선택은 패널이 단일 진실원으로 쥔다 — 칸이 스스로 뒤집지 않는다.</summary>
    public void SetSelected(bool _on)
    {
        if (this.selectedMark != null) this.selectedMark.SetActive(_on);
    }
}
