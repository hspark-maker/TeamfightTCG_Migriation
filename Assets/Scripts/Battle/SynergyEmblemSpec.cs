using System;
using UnityEngine;

/// <summary>
/// 엠블럼(시너지 상징) 한 벌 — **그림 + 그 그림이 어떻게 움직이는가**.
///
/// 몸짓마다 필요한 값이 다르다(들썩임은 횟수·배율, 반짝임은 기울기·띠 두께). 한 클래스에 다 담으면
/// 몸짓이 늘 때마다 안 쓰는 칸이 같이 늘어서, 어느 시너지 에셋을 열어도 절반은 의미 없는 숫자가 된다.
/// 그래서 **몸짓 하나 = 자식 클래스 하나**로 나눈다. 공통(그림·크기·불투명도·정렬)만 여기 베이스에 남는다.
///
/// SynergyVfxConfig가 <c>[SerializeReference]</c>로 들고 있어서 인스펙터에서 몸짓 타입을 골라 꽂는다
/// — 고른 타입의 칸만 뜬다(타입 자체가 곧 스타일이라 style enum이 따로 없다).
///
/// 재생 길이도 여기 있다(<see cref="duration"/>) — 몸짓마다, 시너지마다 알맞은 길이가 다르다.
/// **저장값은 언제나 raw 초**이고 전역 배속은 읽는 쪽(<see cref="Duration"/>)에서 곱한다:
/// 배속이 먹은 값을 저장해 두면 배속을 바꿔도 안 따라오는 연출이 조용히 생긴다.
/// 곱하는 출구는 BattleTimingConfig.Scaled 하나뿐이다(KeywordIconConfig와 같은 규약).
///
/// **순수 연출** — 게임상태/RNG 무접촉.
/// </summary>
[Serializable]
public abstract class SynergyEmblemSpec
{
    [Header("그림")]
    // 카드 앞에 크게 떠오르는 1회성 상징. 배지 아이콘(SynergyData.activeIcon)과 축이 다르다 —
    // 저건 상시 표시용 작은 UI 아이콘이라 같은 그림을 쓰면 둘 중 하나가 반드시 어색해진다.
    // 비어 있으면 그 시너지는 엠블럼 연출 자체를 건너뛴다(무동작 안전).
    public Sprite sprite;

    [Header("공통 시간")]
    [Tooltip("등장~소멸 전체 길이(초, raw). 자식들의 구간 비율이 이 값을 나눠 쓴다. " +
             "배속(SpeedFactor)은 읽는 쪽에서 곱하므로 여기 값은 배속 1 기준으로 적는다.")]
    [Min(0.05f)] public float duration = 1.1f;

    [Header("공통 겉모습")]
    [Range(0.2f, 2f)] public float heightRatio = 0.95f;   // 카드 높이 대비 엠블럼 높이
    [Range(0f, 1f)]   public float alpha       = 0.70f;   // 최대 불투명도(카드 아트를 가리는 정도)
    // 카드 루트에 SortingGroup(order 1)이 걸려 있어, 그룹 밖 오브젝트는 씬 레벨에서 그 order와 비교된다.
    // 2면 모든 카드보다 앞. 피격/투사체 파티클(order 20~30)보다는 낮게 둬서 전투 연출이 계속 위에 뜬다.
    public int sortingOrder = 2;

    /// <summary>띄울 그림이 있는가. 배선 판정(<see cref="SynergyEmblemEntry.Covers"/>)이 이걸 본다.
    /// 그림을 여러 장 쓰는 몸짓(성벽 StackUpEmblem)은 자기 목록으로 다시 답한다 —
    /// 여기서 <c>sprite</c> 하나만 보면 그런 몸짓이 배선돼도 줄 전체가 꺼진다.</summary>
    public virtual bool HasArt => this.sprite != null;

    /// <summary>배속이 적용된 실제 재생 길이(초). 자식 구현과, 연출이 끝나길 기다리는 호출부
    /// (무리 선피해 → 볼리)가 같은 이 값을 본다 — 대기 시간과 실제 길이가 갈라지지 않게.</summary>
    public float Duration => GameTiming.Battle.Scaled(Mathf.Max(0.05f, this.duration));

    /// <summary>몸짓 재생. 그림을 만드는 것까지는 베이스가 하고(<see cref="Create"/>),
    /// 그 뒤 무엇을 하는지는 자식이 정한다. 생성된 오브젝트의 파괴 책임도 자식에 있다
    /// (몸짓마다 언제 끝나는지가 다르므로 — 시퀀스 OnComplete에서 지운다).</summary>
    public abstract void Play(CardView _view, SynergyData _synergy);

    /// <summary>공통 뼈대: 카드 자리에 엠블럼 스프라이트를 하나 만들고 기준 배율을 돌려준다.
    /// 카드 자식으로 붙이지 않는다 — 카드 쪽 DOKill/FadeView가 이 연출을 조용히 잘라먹는다.
    /// 자리는 SlotPosition 기준이라 카드가 공격 연출로 나가 있어도 엠블럼은 그 자리에 남는다.</summary>
    protected GameObject Create(CardView _view, SynergyData _synergy,
        out SpriteRenderer _sr, out float _baseScale)
    {
        var t_go = new GameObject("SynergyEmblem");
        t_go.transform.position = _view.SlotPosition;

        _sr = t_go.AddComponent<SpriteRenderer>();
        _sr.sprite         = this.sprite;
        _sr.sortingLayerID = _view.VfxSortingLayerId;
        _sr.sortingOrder   = this.sortingOrder;
        _sr.color          = Tint(_synergy, 0f);

        // 기준 크기: 스프라이트 원본 크기와 무관하게 카드 높이에 맞춘다(에셋 교체에 안 흔들리게).
        float t_srcH = _sr.sprite.bounds.size.y;
        _baseScale = t_srcH > 0.001f
            ? (_view.SlotWorldBounds.size.y * this.heightRatio) / t_srcH
            : 1f;
        return t_go;
    }

    /// <summary>시너지 고유색을 옅게 섞은 틴트. 색이 없으면 흰색(원화 그대로).</summary>
    protected Color Tint(SynergyData _synergy, float _alpha)
    {
        Color t_c = _synergy != null ? _synergy.TintOrWhite : Color.white;
        t_c.a = _alpha;
        return t_c;
    }
}
