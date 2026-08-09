using UnityEngine;

// 드래그 카드를 "지금 꽂을 슬롯" 안으로 옮겨 놓고, 진행도(0~1)를 카드 y로 환산한다.
//
// ⚠ 씰은 여기 없다 — **도감 칸 자체가 슬리브 두 겹**이고, 드래그 카드는 그 사이(`InsertDock`)로 들어간다.
//   번호를 덮으며 비닐(앞면) 뒤로 잠기는 것이 "밀어 넣는" 그림이라, 패널에 씰을 하나 더 띄울 필요가 없다.
//   (2026-08-10 이전에는 패널에 AlbumCardSlot 복제본 `Sleeve_Slot`을 띄워 진짜 칸 위를 덮었다.
//    화면에 씰이 두 벌 존재하는 값이라 폐기했다 — 되돌리지 말 것.)
//
// ⚠ 부모를 옮기는 컴포넌트다 — 스텝마다 대상 칸이 달라지므로 홈 부모(패널)를 Awake에 기억해 두고
//   세션이 끝날 때 `Release()`로 되돌린다. 안 되돌리면 다음 세션이 남의 칸 안에서 시작한다.
public class AlbumSleeveView : MonoBehaviour
{
    [SerializeField] RectTransform panelRect;   // 좌표 변환 기준 레이어(= 삽입 패널 루트)
    [SerializeField] RectTransform cardHolder;  // 드래그 카드 부모

    float     m_cardHeight;   // = 정렬된 슬롯 높이. 진행도 1의 이동 거리
    float     m_homeX;
    float     m_homeY;        // 진행도 0의 카드 y(슬롯 바로 위에 통째로 떠 있는 자리)
    bool      m_layerWarned;  // 배선 누락 경고는 카드마다 쏟지 않고 한 번만
    Transform m_dockHome;     // cardHolder의 원래 부모(= 패널). 옮기기 전에 한 번만 기억한다

    /// <summary>진행도 1이 이동하는 거리(캔버스 단위). 드래그 임계의 기준이 된다.</summary>
    public float CardHeight => this.m_cardHeight;

    public RectTransform CardHolder => this.cardHolder;

    /// <summary>드래그 카드를 대상 칸의 씰 사이(`InsertDock`)로 옮기고 칸 크기에 맞춘다.
    /// GridRatioFitter가 cellSize를 런타임에 정하므로 호출 전에 레이아웃이 확정돼 있어야 한다.
    /// _dock이 null이면 패널 좌표계에 그대로 띄운다(가림 없는 폴백).</summary>
    public void AlignTo(RectTransform _slotRect, RectTransform _dock)
    {
        if (_slotRect == null || this.cardHolder == null) return;

        if (this.m_dockHome == null) this.m_dockHome = this.cardHolder.parent;

        if (_dock != null)
        {
            // 칸 안으로 들어가면 좌표계가 곧 칸이다 — 중앙이 (0,0)이라 레이어 변환이 필요 없다.
            this.cardHolder.SetParent(_dock, false);
            this.Place(_slotRect.rect.size, Vector2.zero);
            return;
        }

        var t_layer = this.ResolveLayer();
        if (t_layer == null) return;

        this.cardHolder.SetParent(t_layer, false);

        // 그리드 셀은 부모 체인에서 배율을 먹는다 — 그 배율을 흡수해야 패널 좌표계의 실제 크기가 나온다.
        float   t_ratio = ResolveScaleRatio(t_layer, _slotRect);
        Vector2 t_size  = _slotRect.rect.size * t_ratio;

        // ToLayerLocal은 대상의 pivot 위치를 준다 — 중앙 정렬로 쓰려면 pivot 차이만큼 되민다.
        Vector2 t_center = UiGainBurst.ToLayerLocal(t_layer, _slotRect)
                         + (new Vector2(0.5f, 0.5f) - _slotRect.pivot) * t_size;

        this.Place(t_size, t_center);
    }

    /// <summary>카드 홀더를 패널로 되돌린다(세션 종료·중단 공통). 멱등이다.</summary>
    public void Release()
    {
        if (this.cardHolder == null || this.m_dockHome == null) return;
        if (this.cardHolder.parent == this.m_dockHome) return;

        this.cardHolder.SetParent(this.m_dockHome, false);
    }

    /// <summary>진행도 → 카드 y. 안착 트윈이 목표값을 물어보는 창구이기도 하다(계산이 두 벌 되지 않게).</summary>
    public float YAt(float _p) => this.m_homeY - this.m_cardHeight * _p;

    public void SetProgress(float _p)
    {
        if (this.cardHolder == null) return;

        this.cardHolder.anchoredPosition = new Vector2(this.m_homeX, this.YAt(Mathf.Clamp01(_p)));
    }

    // 카드는 칸과 같은 크기다 — 진짜 칸의 카드도 칸 전체를 쓰므로 안착 순간 바꿔치기해도 크기가 튀지 않는다.
    void Place(Vector2 _size, Vector2 _center)
    {
        this.m_cardHeight = Mathf.Max(1f, _size.y);
        this.m_homeX      = _center.x;
        this.m_homeY      = _center.y + this.m_cardHeight;   // 진행도 0 = 칸 바로 위, 겹침 0

        Fit(this.cardHolder, _size, new Vector2(this.m_homeX, this.m_homeY));
    }

    // panelRect는 cardHolder의 홈 좌표계다 — 자기 자신으로 폴백하면
    // 변환 기준과 변환 대상이 같아져 좌표가 조용히 무의미해진다(카드가 엉뚱한 자리에 뜬다).
    RectTransform ResolveLayer()
    {
        if (this.panelRect != null) return this.panelRect;

        if (!m_layerWarned)
        {
            m_layerWarned = true;
            Debug.LogError("[AlbumSleeveView] panelRect 배선 누락 — 카드 위치를 계산할 수 없다(cardHolder의 부모를 꽂을 것).", this);
        }
        return null;
    }

    // 대상이 레이어 대비 몇 배로 그려지고 있는가. 0 나눗셈은 1배로 떨어뜨린다.
    static float ResolveScaleRatio(RectTransform _layer, RectTransform _target)
    {
        float t_layerScale = _layer.lossyScale.x;
        if (Mathf.Approximately(t_layerScale, 0f)) return 1f;

        return _target.lossyScale.x / t_layerScale;
    }

    // 중앙 앵커로 못 박고 크기·위치를 실측값으로 덮는다 — 프리팹 저작 앵커가 무엇이든 같은 결과가 나오게.
    static void Fit(RectTransform _rect, Vector2 _size, Vector2 _at)
    {
        if (_rect == null) return;

        _rect.anchorMin        = _rect.anchorMax = _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta        = _size;
        _rect.anchoredPosition = _at;
        _rect.localScale       = Vector3.one;
        _rect.localRotation    = Quaternion.identity;
    }
}
