using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 카드가 슬리브에 꽂히는 **그 프레임**에 터지는 축하 연출(광채 + 색종이).
// 세션(AlbumInsertSession.Seat)이 위장을 걷어 칸에 카드가 나타나는 순간에만 불린다.
//
// MonoBehaviour가 아니라 [Serializable] 순수 클래스다 — 세션이 필드로 들고, 배선은 세션의
// SerializeField에 남는다(RewardRevealFx·AlbumGaugeView와 같은 계열).
//
// ⚠ layer/glow 노드를 삽입 패널(Panel_AlbumInsert) 밑으로 옮기지 말 것.
//   세션은 카드마다 SetGroupAlpha(0)으로 패널을 지우고, 끝날 때 패널을 SetActive(false) 한다 —
//   패널 안에 두면 색종이가 뜨자마자 투명해지거나 마지막 장에서 통째로 사라진다.
//   두 노드 모두 Panel_PageOverlay 소속(Layer_InsertFanfare)이어야 한다.
//
// ⚠ 광채는 슬롯 그리드 **뒤**에 두면 안 된다. 도감 칸 바닥(Sleeve_Back)이 불투명이라
//   칸 사이 틈으로만 새어 나와 "안 뜬 것"으로 보인다(2026-08-19 그렇게 배선했다가 되돌렸다).
//
// 궤적 규칙은 직접 갖지 않는다 — UiConfettiBurst(솟았다 떨어진다)가 단일 진실원이고
// 여기 남는 건 "어디서·몇 개·얼마나 크게" 뿐이다. 그래서 보상 팝업(RewardRevealFx)과 손맛이 안 갈린다.
[System.Serializable]
public class AlbumInsertFanfareFx
{
    [SerializeField] RectTransform layer;
    [Tooltip("칸 뒤에서 번지는 광채. 미배선이면 색종이만 터진다.")]
    [SerializeField] Image glow;
    [SerializeField] Sprite confettiSprite;

    [Header("색종이 조각")]
    [Tooltip("조각 한 변의 기준 크기(px). 조각마다 여기에 sizeStep을 0~2배 더한다.")]
    [SerializeField] float pieceSize = 58f;
    [Tooltip("조각별 크기 차이. 0이면 전부 같은 크기라 한 덩어리로 읽힌다.")]
    [SerializeField] float pieceSizeStep = 12f;
    [Tooltip("조각 색 표. 비우면 흰색으로 튄다.")]
    [SerializeField] Color[] pieceColors =
    {
        new Color(1f, 0.82f, 0.2f, 1f),
        new Color(1f, 0.42f, 0.28f, 1f),
        new Color(0.35f, 0.86f, 1f, 1f),
        new Color(0.72f, 0.48f, 1f, 1f),
    };

    [Header("광채")]
    [Tooltip("가장 밝을 때의 알파.")]
    [Range(0f, 1f)] [SerializeField] float glowAlpha = 0.85f;
    [SerializeField] float glowStartScale = 0.5f;
    [SerializeField] float glowEndScale   = 1.75f;
    [Tooltip("모두 넣기(자동)에서 광채를 줄이는 배율. 장마다 터지므로 수동보다 얌전해야 한다.")]
    [Range(0.1f, 1f)] [SerializeField] float quickGlowScale = 0.7f;

    [Header("수동 삽입")]
    [SerializeField] int manualCount = 34;
    [SerializeField] float manualRadius = 300f;
    [SerializeField] float manualFallDistance = 380f;

    [Header("모두 넣기")]
    [SerializeField] int quickCount = 16;
    [SerializeField] float quickRadius = 220f;
    [SerializeField] float quickFallDistance = 280f;

    readonly List<GameObject> m_pieces = new List<GameObject>();
    Sequence m_sequence;

    static readonly Color[] FallbackColors = { Color.white };

    public void Play(RectTransform _slotRect, bool _quick)
    {
        this.Reset();
        if (_slotRect == null) return;

        bool t_hasConfetti = this.layer != null && this.confettiSprite != null;
        bool t_hasGlow = this.glow != null;
        if (!t_hasConfetti && !t_hasGlow) return;

        Vector2 t_center = t_hasConfetti ? UiGainBurst.ToLayerLocal(this.layer, _slotRect) : Vector2.zero;
        var t_settings = _quick
                       ? new UiConfettiBurst.Settings(this.quickCount, this.quickRadius, 0.18f,
                                                      this.quickFallDistance, 0.4f, 0.08f, 0.06f, 240f)
                       : new UiConfettiBurst.Settings(this.manualCount, this.manualRadius, 0.25f,
                                                      this.manualFallDistance, 0.65f, 0.12f, 0.12f, 300f);

        GameObject t_link = this.layer != null ? this.layer.gameObject : this.glow.gameObject;
        this.m_sequence = DOTween.Sequence().Pause().SetUpdate(true).SetLink(t_link);

        if (t_hasConfetti)
        {
            var t_burst = UiConfettiBurst.Build(this.layer, t_center, in t_settings,
                                                 _spawn: this.CreatePiece,
                                                 _despawn: _rt =>
                                                 {
                                                     if (_rt != null) _rt.gameObject.SetActive(false);
                                                 });
            t_burst.Pause();
            this.m_sequence.Insert(0f, t_burst);
        }

        if (t_hasGlow)
        {
            var t_glowRect = (RectTransform)this.glow.transform;
            var t_glowLayer = t_glowRect.parent as RectTransform;
            t_glowRect.anchoredPosition = t_glowLayer != null
                                          ? UiGainBurst.ToLayerLocal(t_glowLayer, _slotRect)
                                          : Vector2.zero;
            // 자동 진행은 장마다 터지므로 같은 크기·같은 밝기로 두면 화면이 계속 하얗다.
            float t_glowScale = _quick ? this.quickGlowScale : 1f;
            float t_alpha     = this.glowAlpha * (_quick ? this.quickGlowScale : 1f);

            t_glowRect.localScale = Vector3.one * (this.glowStartScale * t_glowScale);
            var t_glowColor = this.glow.color;
            this.glow.color = new Color(t_glowColor.r, t_glowColor.g, t_glowColor.b, 0f);
            this.glow.gameObject.SetActive(true);

            this.m_sequence.Insert(0f, this.glow.DOFade(t_alpha, 0.12f).SetEase(Ease.OutQuad));
            this.m_sequence.Insert(0f, t_glowRect.DOScale(this.glowEndScale * t_glowScale, 0.58f).SetEase(Ease.OutCubic));
            this.m_sequence.Insert(0.18f, this.glow.DOFade(0f, 0.4f).SetEase(Ease.InQuad));
        }

        this.m_sequence.OnComplete(this.ClearVisuals).Play();
    }

    public void Reset()
    {
        if (this.m_sequence != null && this.m_sequence.IsActive()) this.m_sequence.Kill();
        this.m_sequence = null;
        this.ClearVisuals();
    }

    RectTransform CreatePiece(int _index)
    {
        var t_go = new GameObject("AlbumInsertConfetti", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rect = (RectTransform)t_go.transform;
        t_rect.sizeDelta = Vector2.one * (this.pieceSize + _index % 3 * this.pieceSizeStep);

        var t_colors = this.pieceColors != null && this.pieceColors.Length > 0 ? this.pieceColors : FallbackColors;

        var t_image = t_go.GetComponent<Image>();
        t_image.sprite = this.confettiSprite;
        t_image.preserveAspect = true;
        t_image.raycastTarget = false;   // 조각이 도감 칸 터치를 가로채지 않게.
        t_image.color = t_colors[_index % t_colors.Length];

        this.m_pieces.Add(t_go);
        return t_rect;
    }

    void ClearVisuals()
    {
        if (this.glow != null)
        {
            this.glow.DOKill();
            this.glow.transform.DOKill();
            this.glow.gameObject.SetActive(false);
        }

        for (int t_i = 0; t_i < this.m_pieces.Count; t_i++)
        {
            if (this.m_pieces[t_i] == null) continue;
            this.m_pieces[t_i].transform.DOKill();
            Object.Destroy(this.m_pieces[t_i]);
        }

        this.m_pieces.Clear();
    }
}
