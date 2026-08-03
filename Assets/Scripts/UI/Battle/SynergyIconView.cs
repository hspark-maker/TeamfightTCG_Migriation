using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>시너지 줄의 아이콘 한 칸 — 그림 + 발동 순간의 pop·글로우.
///
/// 칸 하나가 자기 연출을 소유한다: 줄(<see cref="FieldSynergyPanel"/>)은 "어느 칸이 터졌나"까지만 알고,
/// 어떻게 튀는지는 여기가 정한다. 칸에 연출이 늘어도 줄 코드는 그대로다.
///
/// 글로우는 아이콘 **위에** 겹치는 빛무리다(UI는 자식이 항상 부모 위라 아래로 못 깐다).
/// 그래서 평소엔 알파 0으로 죽여 두고 발동 순간에만 잠깐 켠다 — 상시 켜 두면 아이콘이 씻긴다.
///
/// 순수 표시 — 상태·RNG 무접촉.</summary>
public class SynergyIconView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] Image icon;
    [Tooltip("발동 순간에만 번지는 빛무리. 비우면 pop만 한다")]
    [SerializeField] Image glow;

    [Header("발동 pop")]
    [Tooltip("아이콘이 커지는 배율")]
    [SerializeField] float popScale = 1.5f;
    [Tooltip("pop 전체 시간(초, raw). 배속은 재생할 때 곱한다")]
    [SerializeField] float popDuration = 0.25f;

    [Header("글로우")]
    [SerializeField] float glowAlpha = 0.85f;
    [Tooltip("빛무리가 퍼지는 최대 배율(아이콘 크기 대비)")]
    [SerializeField] float glowScale = 1.9f;

    /// <summary>이 칸이 맡은 시너지. 줄이 발동 pop 대상을 찾을 때 쓴다.</summary>
    public SynergyData Synergy { get; private set; }

    /// <summary>이 필드에 깔린 소속 카드 수. 설명 팝업의 ●/○ 마커가 쓴다(음수면 문맥 없음).</summary>
    public int OwnedCount { get; private set; } = -1;

    /// <summary>이 칸을 누르고 있는 동안만 설명이 뜬다. 무엇을 할지는 줄(<see cref="FieldSynergyPanel"/>)이 정한다 —
    /// 칸은 자기 그림과 몸짓만 알고, 설명 팝업·카드 확대는 판 전체를 봐야 하는 일이다.</summary>
    public event Action<SynergyIconView> Pressed;
    public event Action<SynergyIconView> Released;

    public void OnPointerDown(PointerEventData _e) => this.Pressed?.Invoke(this);

    /// <summary>손을 뗀 순간. 누른 채 칸 밖으로 끌고 나가도 **누르기 시작한 칸**이 이걸 받으므로
    /// (Unity 입력 규약) 설명이 열린 채 남는 경우가 없다.</summary>
    public void OnPointerUp(PointerEventData _e) => this.Released?.Invoke(this);

    /// <summary>칸에 시너지를 앉힌다. 이전 판의 트윈을 끊고 기준 상태로 되돌린다 —
    /// 재사용 풀이라 pop 중이던 칸이 그대로 다음 판에 넘어올 수 있다.</summary>
    public void Bind(SynergyData _synergy, int _ownedCount = -1)
    {
        this.Synergy    = _synergy;
        this.OwnedCount = _ownedCount;

        transform.DOKill();
        transform.localScale = Vector3.one;

        if (this.icon != null) this.icon.sprite = _synergy != null ? _synergy.activeIcon : null;
        ResetGlow();
    }

    /// <summary>효과가 실제로 일한 순간. punch + 빛무리 1회.</summary>
    public void Pop()
    {
        float t_dur = GameTiming.Battle.Scaled(Mathf.Max(0.05f, this.popDuration));

        // 카드 배지와 같은 규약: 이전 트윈을 끊고 기준 크기로 되돌린 뒤 punch(연달아 터져도 안 커진다).
        // localScale만 건드린다 — RectTransform 크기를 만지면 레이아웃 그룹이 다시 돌아 줄이 출렁인다.
        transform.DOKill();
        transform.localScale = Vector3.one;
        transform.DOPunchScale(Vector3.one * (this.popScale - 1f), t_dur).SetLink(gameObject);

        if (this.glow == null) return;

        ResetGlow();
        this.glow.gameObject.SetActive(true);

        var t_seq = DOTween.Sequence().SetLink(this.glow.gameObject);
        t_seq.Append(this.glow.DOFade(this.glowAlpha, t_dur * 0.3f));
        t_seq.Join(this.glow.transform.DOScale(this.glowScale, t_dur * 0.75f).SetEase(Ease.OutQuad));
        t_seq.Append(this.glow.DOFade(0f, t_dur * 0.7f));
        t_seq.OnComplete(ResetGlow);
    }

    void ResetGlow()
    {
        if (this.glow == null) return;
        this.glow.transform.DOKill();
        this.glow.DOKill();
        this.glow.transform.localScale = Vector3.one;

        Color t_c = this.glow.color;
        t_c.a = 0f;
        this.glow.color = t_c;
    }
}
