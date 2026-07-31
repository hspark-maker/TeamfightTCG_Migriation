using DG.Tweening;
using UnityEngine;

/// <summary>
/// 시너지 상징(엠블럼)이 카드 <b>뒤쪽</b>에서 떠올라 들썩이다 사라지는 연출.
/// 발동 지점은 두 곳 — 그 시너지 카드가 배치될 때, 그리고 효과가 실제로 터질 때(SynergyTriggers).
///
/// 그림은 SynergyData.emblem 소유(비어 있으면 그 시너지는 이 연출을 건너뛴다).
/// 시간은 BattleTimingConfig.synergyEmblemDuration 하나 — 아래 비율 상수가 그걸 나눠 쓴다.
/// 값을 여기 두지 않는 이유: 개별 상수로 흩으면 전역 배속(SpeedFactor)을 우회한다.
///
/// **순수 연출** — 게임상태/RNG 무접촉. 카드 계층 밖에 만들어 카드 페이드·DOKill에 얽히지 않게 한다.
/// </summary>
public static class SynergyEmblemVfx
{
    // 전체 길이를 1로 봤을 때의 구간 비율.
    const float RiseRatio  = 0.22f;   // 솟아오르며 커지는 구간
    const float ShakeRatio = 0.48f;   // 들썩이는 구간(웃는 것처럼)
    // 나머지(0.30)는 사라지는 구간.

    const int   ShakeCount   = 3;      // 들썩임 횟수
    const float RiseScale    = 1.12f;  // 솟았을 때 배율(기준 크기 대비)
    const float ShakeLow     = 0.90f;  // 들썩임 아래쪽 배율
    const float ExitScale    = 0.72f;  // 사라질 때 줄어드는 배율
    const float HeightRatio  = 1.45f;  // 카드 높이 대비 엠블럼 높이
    const float Alpha        = 0.85f;  // 최대 불투명도(카드가 주인공이라 완전 불투명은 피한다)

    // 카드 루트에 SortingGroup(order 1)이 걸려 있어, 그룹 밖 오브젝트는 씬 레벨에서 그 order와 비교된다.
    // 0이면 모든 카드보다 뒤 → "카드 뒤쪽"이 성립한다.
    const int SortingOrder = 0;

    /// <summary>이 카드 뒤에 해당 시너지 엠블럼을 1회 재생. 뷰/그림이 없으면 무동작.</summary>
    public static void Play(CardInstance _card, SynergyData _synergy)
    {
        if (_card == null || _synergy == null || _synergy.emblem == null) return;
        Play(CardView.GetView(_card), _synergy);
    }

    public static void Play(CardView _view, SynergyData _synergy)
    {
        if (_view == null || _synergy == null || _synergy.emblem == null) return;

        float t_total = Mathf.Max(0.1f, GameTiming.Battle.SynergyEmblemDuration);

        var t_go = new GameObject("SynergyEmblem");
        // 카드 자식으로 붙이지 않는다 — 카드 쪽 DOKill/FadeView가 이 연출을 조용히 잘라먹는다.
        // 자리는 SlotPosition 기준이라 카드가 공격 연출로 나가 있어도 엠블럼은 그 자리에 남는다.
        t_go.transform.position = _view.SlotPosition;

        var t_sr = t_go.AddComponent<SpriteRenderer>();
        t_sr.sprite         = _synergy.emblem;
        t_sr.sortingLayerID = _view.VfxSortingLayerId;
        t_sr.sortingOrder   = SortingOrder;
        t_sr.color          = Tint(_synergy, 0f);

        // 기준 크기: 스프라이트 원본 크기와 무관하게 카드 높이에 맞춘다(에셋 교체에 안 흔들리게).
        float t_srcH = t_sr.sprite.bounds.size.y;
        float t_base = t_srcH > 0.001f ? (_view.SlotWorldBounds.size.y * HeightRatio) / t_srcH : 1f;

        t_go.transform.localScale = Vector3.zero;

        float t_rise  = t_total * RiseRatio;
        float t_shake = t_total * ShakeRatio;
        float t_exit  = Mathf.Max(0.05f, t_total - t_rise - t_shake);
        float t_beat  = t_shake / (ShakeCount * 2f);   // 위·아래 한 번씩이 1회

        Color t_on = Tint(_synergy, Alpha);

        var t_seq = DOTween.Sequence().SetLink(t_go);

        // 1) 솟아오름 — OutBack 오버슈트로 "툭" 튀어나온다.
        t_seq.Append(t_go.transform.DOScale(t_base * RiseScale, t_rise).SetEase(Ease.OutBack));
        t_seq.Join(t_sr.DOColor(t_on, t_rise));

        // 2) 들썩임 — 커졌다 작아졌다 반복. InOutSine이라 양 끝에서 꺾이지 않는다.
        for (int i = 0; i < ShakeCount; i++)
        {
            t_seq.Append(t_go.transform.DOScale(t_base * ShakeLow,  t_beat).SetEase(Ease.InOutSine));
            t_seq.Append(t_go.transform.DOScale(t_base * RiseScale, t_beat).SetEase(Ease.InOutSine));
        }

        // 3) 소멸 — 줄어들며 투명해진다.
        t_seq.Append(t_go.transform.DOScale(t_base * ExitScale, t_exit).SetEase(Ease.InQuad));
        t_seq.Join(t_sr.DOFade(0f, t_exit));

        t_seq.OnComplete(() => { if (t_go != null) Object.Destroy(t_go); });
        // 중간에 씬이 내려가면 SetLink가 트윈을 죽이는데, 그때 오브젝트도 같이 사라지므로 별도 정리 불필요.
    }

    /// <summary>시너지 고유색을 옅게 섞은 틴트. 색이 없으면 흰색(원화 그대로).</summary>
    static Color Tint(SynergyData _synergy, float _alpha)
    {
        Color t_c = _synergy.TintOrWhite;
        t_c.a = _alpha;
        return t_c;
    }
}
