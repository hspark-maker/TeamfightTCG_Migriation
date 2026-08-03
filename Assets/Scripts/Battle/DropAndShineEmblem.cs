using System;
using DG.Tweening;
using UnityEngine;

/// <summary>위에서 떨어져 내려앉은 뒤 사선 흰 띠가 한 번 훑고 지나가는 유리 반짝임.
///
/// 띠는 엠블럼 실루엣 <b>안에서만</b> 보여야 유리처럼 읽힌다 → 같은 스프라이트를 SpriteMask로 깔고
/// 띠 렌더러만 VisibleInsideMask로 둔다. 마스크는 maskInteraction을 켠 렌더러에만 작용하므로
/// 카드/다른 연출에는 영향이 없다(그쪽은 전부 기본값 None).</summary>
[Serializable]
public class DropAndShineEmblem : SynergyEmblemSpec
{
    [Header("구간 비율 (합 1, 나머지가 소멸 구간)")]
    [Range(0f, 1f)] public float dropRatio   = 0.28f;   // 위에서 떨어져 내려앉는 구간
    [Range(0f, 1f)] public float settleRatio = 0.10f;   // 착지 후 멈춤(반짝임이 낙하와 겹쳐 뭉치지 않게)
    [Range(0f, 1f)] public float shineRatio  = 0.37f;   // 흰 띠가 위→아래로 훑는 구간

    [Header("형태")]
    public float dropHeightRatio  = 0.9f;    // 낙하 시작 높이(엠블럼 높이 대비)
    public float shineTiltDeg     = 25f;     // 띠 기울기(사선)
    public float shineWidthRatio  = 0.22f;   // 띠 두께(엠블럼 폭 대비)
    public float shineTravelRatio = 0.95f;   // 띠가 오가는 거리(엠블럼 높이 대비, 위아래 각각)
    [Range(0f, 1f)] public float shineAlpha = 0.85f;    // 띠 자체 불투명도(반사광이라 엠블럼보다 진하다)

    public override void Play(CardView _view, SynergyData _synergy)
    {
        float t_total = Duration;   // 배속 적용은 베이스가 한다(raw 초 × SpeedFactor)

        GameObject t_go = Create(_view, _synergy, out SpriteRenderer t_sr, out float t_base);
        t_go.transform.localScale = Vector3.one * t_base;   // 크기는 고정 — 이 몸짓의 움직임은 낙하다

        // 스프라이트 로컬 단위(부모 배율이 이미 곱해지므로 여기선 원본 크기 기준으로 잡는다).
        Vector3 t_size = t_sr.sprite.bounds.size;
        Vector3 t_land = t_go.transform.position;
        float   t_drop = t_size.y * t_base * this.dropHeightRatio;
        t_go.transform.position = t_land + Vector3.up * t_drop;

        // 실루엣 마스크 + 띠. 둘 다 엠블럼 자식이라 부모 배율/위치를 그대로 따라간다.
        var t_mask = new GameObject("Mask").AddComponent<SpriteMask>();
        t_mask.transform.SetParent(t_go.transform, false);
        t_mask.sprite = this.sprite;

        var t_shine = new GameObject("Shine").AddComponent<SpriteRenderer>();
        t_shine.transform.SetParent(t_go.transform, false);
        t_shine.sprite          = ShineBandSprite.Get();   // 띠 형태는 공용(코인 플립 반짝임과 같은 그림)
        t_shine.sortingLayerID  = t_sr.sortingLayerID;
        t_shine.sortingOrder    = this.sortingOrder + 1;   // 엠블럼 바로 위
        t_shine.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        t_shine.color           = new Color(1f, 1f, 1f, 0f);

        // 띠는 세로로 길게(엠블럼을 완전히 가로지르도록) 눕혀 두고, 위→아래로 지나간다.
        float t_bandLen = Mathf.Sqrt(t_size.x * t_size.x + t_size.y * t_size.y) * 1.4f;
        t_shine.transform.localScale    = new Vector3(
            t_size.x * this.shineWidthRatio / ShineBandSprite.UnitWidth, t_bandLen, 1f);
        t_shine.transform.localRotation = Quaternion.Euler(0f, 0f, this.shineTiltDeg);

        float t_travel = t_size.y * this.shineTravelRatio;
        t_shine.transform.localPosition = new Vector3(0f, t_travel, 0f);

        float t_dropDur   = t_total * this.dropRatio;
        float t_settleDur = t_total * this.settleRatio;
        float t_shineDur  = t_total * this.shineRatio;
        float t_exitDur   = Mathf.Max(0.05f, t_total - t_dropDur - t_settleDur - t_shineDur);

        Color t_on = Tint(_synergy, this.alpha);

        var t_seq = DOTween.Sequence().SetLink(t_go);

        // 1) 낙하 — OutBounce로 툭 내려앉는다. 떨어지는 동안 나타난다.
        t_seq.Append(t_go.transform.DOMove(t_land, t_dropDur).SetEase(Ease.OutBounce));
        t_seq.Join(t_sr.DOColor(t_on, t_dropDur * 0.6f));
        t_seq.AppendInterval(t_settleDur);

        // 2) 반짝임 1회 — 띠가 위에서 아래로 훑고 지나간다. 밝기는 양 끝에서 죽어 "지나갔다"로 읽힌다.
        t_seq.Append(t_shine.transform.DOLocalMoveY(-t_travel, t_shineDur).SetEase(Ease.InOutSine));
        t_seq.Join(DOTween.Sequence()
            .Append(t_shine.DOFade(this.shineAlpha, t_shineDur * 0.3f))
            .AppendInterval(t_shineDur * 0.3f)
            .Append(t_shine.DOFade(0f, t_shineDur * 0.4f)));

        // 3) 소멸 — 제자리에서 투명해진다(크기는 건드리지 않는다).
        t_seq.Append(t_sr.DOFade(0f, t_exitDur));

        t_seq.OnComplete(() => { if (t_go != null) UnityEngine.Object.Destroy(t_go); });
    }
}
