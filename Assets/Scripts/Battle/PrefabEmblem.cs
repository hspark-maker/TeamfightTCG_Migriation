using System;
using DG.Tweening;
using UnityEngine;

/// <summary>그림 한 장이 아니라 **프리팹 한 벌**을 띄우는 몸짓(덩치 = 몸통+양팔 리그).
///
/// 다른 몸짓은 스프라이트를 트윈으로 움직이지만, 이건 프리팹이 자기 Animator로 움직인다 —
/// 여기 코드가 하는 일은 **꺼내고 · 카드 크기에 맞추고 · 정렬을 맞추고 · 치우는 것**뿐이다.
/// 움직임을 여기서 또 트윈하면 애니메이터와 서로 덮어써서 어느 쪽이 이겼는지 알 수 없게 된다.
///
/// 크기 기준은 프리팹 전체 렌더러를 감싼 실제 높이다 — 프리팹을 고쳐(팔을 키워도) 카드 밖으로 안 넘친다.
/// 정렬은 자식 렌더러 전부에 덮어쓴다: 프리팹은 자기가 어느 씬에 뜰지 모르므로 저작 시점의
/// sortingOrder를 믿으면 카드 뒤로 숨는다.
///
/// 순수 연출 — 상태/RNG 무접촉.</summary>
[Serializable]
public class PrefabEmblem : SynergyEmblemSpec
{
    [Header("리그")]
    [Tooltip("띄울 프리팹(Animator 포함). 비면 이 몸짓은 무동작")]
    public GameObject prefab;
    [Tooltip("슬롯 기준 위치 보정(월드 단위). 프리팹 발밑을 카드 아래에 맞추는 등 미세 조정")]
    public Vector2 offset;

    [Header("등장/퇴장 (합이 duration보다 작아야 한다 — 나머지가 유지 구간)")]
    [Range(0f, 1f)] public float enterRatio = 0.12f;   // 튀어나오는 구간
    [Range(0f, 1f)] public float exitRatio  = 0.20f;   // 사라지는 구간
    public float enterScale = 1.1f;   // 등장 오버슈트 배율(기준 크기 대비)

    /// <summary>스프라이트가 아니라 프리팹이 그림이다 — 베이스의 sprite 칸은 쓰지 않는다.</summary>
    public override bool HasArt => this.prefab != null;

    public override void Play(CardView _view, SynergyData _synergy)
    {
        if (this.prefab == null || _view == null) return;

        float t_total = Duration;   // 배속 적용은 베이스가 한다(raw 초 × SpeedFactor)

        GameObject t_go = UnityEngine.Object.Instantiate(this.prefab);
        t_go.name = "SynergyEmblem_" + this.prefab.name;

        SpriteRenderer[] t_renderers = t_go.GetComponentsInChildren<SpriteRenderer>(true);
        if (t_renderers.Length == 0) { UnityEngine.Object.Destroy(t_go); return; }

        // 정렬: 카드(SortingGroup order 1) 위, 피격 파티클(20~30) 아래.
        foreach (SpriteRenderer t_sr in t_renderers)
        {
            t_sr.sortingLayerID = _view.VfxSortingLayerId;
            t_sr.sortingOrder   = this.sortingOrder;
        }

        // 크기: 프리팹 저작 크기와 무관하게 카드 높이에 맞춘다. 감싸는 상자는 **자식 배치까지 포함**해야
        // 팔이 벌어진 프리팹도 넘치지 않는다. 기준을 잡은 뒤 그 상자의 중심을 슬롯에 맞춘다.
        Bounds t_bounds = t_renderers[0].bounds;
        foreach (SpriteRenderer t_sr in t_renderers) t_bounds.Encapsulate(t_sr.bounds);

        float t_base = t_bounds.size.y > 0.001f
            ? (_view.SlotWorldBounds.size.y * this.heightRatio) / t_bounds.size.y
            : 1f;

        // 루트 원점과 그림의 중심은 대개 어긋나 있다(덩치는 몸통 스프라이트가 루트다).
        // 배율을 먹인 뒤의 중심이 슬롯 한가운데 오도록 루트를 그만큼 밀어준다.
        Vector3 t_root   = t_go.transform.position;
        Vector3 t_scaled = t_root + (t_bounds.center - t_root) * t_base;
        Vector3 t_want   = new Vector3(_view.SlotPosition.x + this.offset.x,
                                       _view.SlotPosition.y + this.offset.y,
                                       _view.SlotPosition.z);

        t_go.transform.localScale = Vector3.one * t_base;
        t_go.transform.position   = t_root + (t_want - t_scaled);

        float t_enter = t_total * this.enterRatio;
        float t_exit  = Mathf.Max(0.05f, t_total * this.exitRatio);
        float t_hold  = Mathf.Max(0f, t_total - t_enter - t_exit);

        var t_seq = DOTween.Sequence().SetLink(t_go);

        // 1) 등장 — 크기만 건드린다(자세는 프리팹 애니메이터가 쥔다).
        if (t_enter > 0.001f)
        {
            t_go.transform.localScale = Vector3.zero;
            t_seq.Append(t_go.transform.DOScale(t_base * this.enterScale, t_enter * 0.7f).SetEase(Ease.OutBack));
            t_seq.Append(t_go.transform.DOScale(t_base, t_enter * 0.3f).SetEase(Ease.OutQuad));
        }

        t_seq.AppendInterval(t_hold);

        // 2) 퇴장 — 전체 렌더러를 같이 흐린다. 알파는 각 렌더러 색을 직접 민다(공용 머티리얼 오염 방지).
        foreach (SpriteRenderer t_sr in t_renderers)
            t_seq.Join(t_sr.DOFade(0f, t_exit).SetEase(Ease.InQuad));

        t_seq.OnComplete(() => { if (t_go != null) UnityEngine.Object.Destroy(t_go); });
    }
}
