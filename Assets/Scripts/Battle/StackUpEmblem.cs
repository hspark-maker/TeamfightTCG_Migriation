using System;
using DG.Tweening;
using UnityEngine;

/// <summary>여러 단으로 잘린 그림이 **아래에서부터 한 단씩 쌓여 올라가는** 몸짓(수호자).
///
/// 다른 몸짓과 달리 그림이 하나가 아니다 — 그래서 베이스의 <see cref="SynergyEmblemSpec.Create"/>
/// (스프라이트 하나짜리 루트)를 쓰지 않고, 빈 루트 아래에 단마다 렌더러를 하나씩 깐다.
/// 단 목록은 <see cref="pieces"/>에 **아래→위 순서**로 넣는다. 베이스 <c>sprite</c>(그림 한 장)는
/// 이 몸짓에선 안 쓴다 — 배선 판정도 <see cref="HasArt"/>가 목록만 보고 답하므로 비워 둔다.
///
/// 크기 기준도 단 하나가 아니라 **쌓인 전체 높이**다 — 단 수를 바꿔도 카드 높이를 넘지 않게.
/// 순수 연출(상태/RNG 무접촉).</summary>
[Serializable]
public class StackUpEmblem : SynergyEmblemSpec
{
    [Header("단 — 아래→위 순서 (위 sprite 칸은 이 몸짓에서 안 쓴다. 비워도 됨)")]
    public Sprite[] pieces;

    [Tooltip("모든 이음매의 기본 간격(아래 단 높이 대비). 잘라낸 칸에 여백이 있으면 음수로 겹쳐 붙인다.")]
    [Range(-0.9f, 0.5f)] public float stackGapRatio = -0.18f;

    [Tooltip("이음매별 간격(아래→위 순서, 단 수보다 1개 적다). 비어 있는 칸은 위 기본값을 쓴다.\n" +
             "단마다 잘린 여백이 다르므로 총안(위)만 더 겹치는 식의 조정이 필요하다.")]
    public JointGap[] jointGaps;

    /// <summary>이음매 하나의 겹침. 배열 인덱스만으로는 어느 이음매인지 안 보여서
    /// <see cref="use"/>를 켠 칸만 기본값을 덮게 한다(끄면 그 이음매는 기본값 그대로).</summary>
    [Serializable]
    public class JointGap
    {
        [Tooltip("체크해야 이 이음매에 아래 값이 적용된다")]
        public bool use = true;
        [Tooltip("아래 단 높이 대비 간격. 음수 = 겹침")]
        [Range(-0.9f, 0.5f)] public float gapRatio = -0.18f;
    }

    [Header("구간 비율 (합 1, 나머지가 소멸 구간)")]
    [Range(0f, 1f)] public float buildRatio = 0.62f;   // 단이 전부 쌓이는 구간
    [Range(0f, 1f)] public float holdRatio  = 0.18f;   // 다 쌓인 수호자 엠블럼을 보여주는 정지 구간

    [Header("형태")]
    public float riseHeightRatio = 0.9f;    // 각 단이 아래에서 올라오는 거리(그 단 높이 대비)
    [Range(0f, 0.9f)] public float pieceOverlapRatio = 0.25f;   // 앞 단이 끝나기 전 다음 단이 출발하는 비율
    [Range(0f, 0.5f)] public float landPunch = 0.09f;   // 한 단이 얹힐 때 스택 전체가 눌리는 정도

    public override void Play(CardView _view, SynergyData _synergy)
    {
        Sprite[] t_pieces = Pieces();
        if (t_pieces.Length == 0) return;

        float t_total = Duration;   // 배속 적용은 베이스가 한다(raw 초 × SpeedFactor)

        // 루트는 그림이 없는 자리 표시자다. 카드 자식으로 붙이지 않는 이유는 베이스와 같다
        // (카드 쪽 DOKill/FadeView가 이 연출을 잘라먹지 않게).
        var t_root = new GameObject("SynergyEmblem");
        t_root.transform.position = _view.SlotPosition;

        // 단 높이는 전부 같다고 보지 않는다 — 잘린 칸 높이가 다를 수 있으므로 각자 자기 높이를 쓴다.
        // 겹침도 이음매마다 따로다(칸마다 남은 여백이 다르다) — 기준은 그 이음매의 **아래 단** 높이.
        float t_stackH = 0f;
        for (int i = 0; i < t_pieces.Length; i++)
        {
            float t_h = t_pieces[i].bounds.size.y;
            t_stackH += t_h;
            if (i < t_pieces.Length - 1) t_stackH += t_h * GapRatioAt(i);
        }

        // 기준 배율: 쌓인 전체가 카드 높이 × heightRatio에 맞는다(원본 해상도·단 수에 안 흔들리게).
        float t_base = t_stackH > 0.001f
            ? (_view.SlotWorldBounds.size.y * this.heightRatio) / t_stackH
            : 1f;
        t_root.transform.localScale = Vector3.one * t_base;

        var   t_srs  = new SpriteRenderer[t_pieces.Length];
        var   t_endY = new float[t_pieces.Length];
        float t_y    = -t_stackH * 0.5f;   // 스택 전체를 슬롯 중앙에 맞춘다

        for (int i = 0; i < t_pieces.Length; i++)
        {
            var t_sr = new GameObject("Piece" + i).AddComponent<SpriteRenderer>();
            t_sr.transform.SetParent(t_root.transform, false);
            t_sr.sprite         = t_pieces[i];
            t_sr.sortingLayerID = _view.VfxSortingLayerId;
            // 아랫단이 앞에 온다 — 원화에서 아래 단이 위 단을 살짝 덮고 있다.
            t_sr.sortingOrder   = this.sortingOrder + (t_pieces.Length - 1 - i);
            t_sr.color          = Tint(_synergy, 0f);

            float t_h = t_pieces[i].bounds.size.y;
            t_endY[i] = t_y + t_h * 0.5f;
            t_y      += t_h + (i < t_pieces.Length - 1 ? t_h * GapRatioAt(i) : 0f);

            t_srs[i] = t_sr;
            t_sr.transform.localPosition = new Vector3(0f, t_endY[i] - t_h * this.riseHeightRatio, 0f);
        }

        float t_build = t_total * this.buildRatio;
        float t_hold  = t_total * this.holdRatio;
        float t_exit  = Mathf.Max(0.05f, t_total - t_build - t_hold);

        // 단 하나가 올라오는 시간. 앞 단이 끝나기 전에 다음 단이 출발하므로(overlap) 겹친 만큼 길게 잡는다 —
        // 그래야 전체 쌓기 시간이 buildRatio 그대로다.
        int   t_n    = t_pieces.Length;
        float t_step = t_n > 1 ? t_build / (t_n - (t_n - 1) * this.pieceOverlapRatio) : t_build;
        float t_gapT = t_step * (1f - this.pieceOverlapRatio);

        Color t_on  = Tint(_synergy, this.alpha);
        var   t_seq = DOTween.Sequence().SetLink(t_root);

        for (int i = 0; i < t_n; i++)
        {
            SpriteRenderer t_sr = t_srs[i];
            float t_at = t_gapT * i;

            // 1) 아래에서 제자리로 — OutBack으로 살짝 지나쳤다 얹힌다.
            t_seq.Insert(t_at, t_sr.transform.DOLocalMoveY(t_endY[i], t_step).SetEase(Ease.OutBack));
            t_seq.Insert(t_at, t_sr.DOColor(t_on, t_step * 0.45f));

            // 2) 얹히는 순간 스택 전체가 한 번 눌린다("쿵"). 다음 단 착지 전에 끝나도록 짧게.
            if (this.landPunch > 0f)
                t_seq.Insert(t_at + t_step * 0.75f, t_root.transform
                    .DOPunchScale(new Vector3(t_base * this.landPunch, -t_base * this.landPunch, 0f),
                        Mathf.Min(t_step * 0.5f, t_gapT * 0.9f), 1, 0.5f));
        }

        // 3) 소멸 — 다 쌓인 채로 잠깐 서 있다가 그대로 투명해진다.
        for (int i = 0; i < t_n; i++)
            t_seq.Insert(t_build + t_hold, t_srs[i].DOFade(0f, t_exit));

        t_seq.OnComplete(() => { if (t_root != null) UnityEngine.Object.Destroy(t_root); });
    }

    /// <summary>이음매 <paramref name="_index"/>(아래에서 _index번째 단과 그 위 단 사이)의 간격 비율.
    /// 그 칸이 없거나 꺼져 있으면 기본값(<see cref="stackGapRatio"/>).</summary>
    float GapRatioAt(int _index)
    {
        if (this.jointGaps == null || _index < 0 || _index >= this.jointGaps.Length) return this.stackGapRatio;
        JointGap t_j = this.jointGaps[_index];
        return (t_j != null && t_j.use) ? t_j.gapRatio : this.stackGapRatio;
    }

    /// <summary>이 몸짓은 단이 하나라도 있으면 뜬다 — 베이스 <c>sprite</c>는 보지 않는다.</summary>
    public override bool HasArt => Pieces().Length > 0;

    /// <summary>아래→위 순서의 단 목록. 빈 칸은 건너뛴다.
    /// **베이스 <c>sprite</c>(그림 한 장)는 이 몸짓에서 안 쓴다** — 그걸 맨 아랫단으로 얹으면
    /// 통짜 그림 위에 단이 쌓이는 꼴이 돼 엠블럼이 두 겹으로 보인다. 단은 오직 <see cref="pieces"/>다.</summary>
    Sprite[] Pieces()
    {
        int t_count = 0;
        if (this.pieces != null)
            for (int i = 0; i < this.pieces.Length; i++)
                if (this.pieces[i] != null) t_count++;

        var t_out = new Sprite[t_count];
        int t_at  = 0;
        if (this.pieces != null)
            for (int i = 0; i < this.pieces.Length; i++)
                if (this.pieces[i] != null) t_out[t_at++] = this.pieces[i];
        return t_out;
    }
}
