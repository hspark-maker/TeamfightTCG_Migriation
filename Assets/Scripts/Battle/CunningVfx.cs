using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 교활(Cunning) 퇴장 연출. 공격을 마치고 제자리로 돌아온 카드가 대기 카드와 교대해 **덱으로 물러나는** 그림:
/// 안개가 깔리고 → 한 바퀴 뒤로 돌면서 뒷면으로 바뀌고 → 카드가 들어오던 그 지점(덱 쪽)으로 빨려 들어간다.
///
/// 프리팹 = BattleVfxLibrary(CunningFog), 시간 = BattleTimingConfig. 발동 판정은 여기 없다 —
/// AttackResult.attackerSwapped가 유일한 기준이고 그 판정은 AttackProcessor가 소유한다.
///
/// **호출 시점이 중요하다: 보드 보충(FillEmptySlots/Refresh) 전.**
/// 스왑은 이미 끝나 있지만 슬롯 뷰는 아직 물러나는 카드를 그리고 있어서, 이 창을 놓치면
/// 들어온 카드가 대신 돌아 나가는 그림이 된다. 물러나는 카드의 isRevealed는 스왑 때 이미 false라
/// 재렌더만 하면 뒷면이 된다(연출이 상태를 만들지 않는다).
/// </summary>
public static class CunningVfx
{
    const float EXIT_SCALE = 0.4f;   // 빨려 들어가며 줄어드는 배율

    /// <summary>필드의 카드가 덱으로 물러나는 그림. 교활 교대 말고 <b>멀리건 교체</b>도 같은 연출을 쓴다 —
    /// "필드 카드가 덱으로 돌아간다"는 사건은 하나뿐이라 그림도 하나여야 한다.
    ///
    /// <paramref name="_exitVfx"/>만 경로마다 다르다. 퇴장 안개(CunningFog)는 교활만 깔고,
    /// 그냥 교체(멀리건)는 <see cref="BattleVfxId.None"/>으로 조용히 물러난다.
    /// 같은 안개를 <b>등장</b> 쪽도 쓰지만(CardAppearSequence.PlayMidArrival) 그건 모든 등장 공통이라
    /// 이 퇴장 분기와는 무관하다 —
    /// 반짝임(CardAppear)은 <b>들어오는 카드가 중앙에 설 때</b>로 옮겼다(CardAppearSequence).
    /// 퇴장에 붙이면 "교체됐다"가, 등장에 붙이면 "새 카드가 나왔다"가 읽힌다.</summary>
    public static async UniTask PlayExit(CardView _view, BattleVfxId _exitVfx = BattleVfxId.CunningFog)
    {
        if (_view == null) return;

        Transform  t_tr    = _view.transform;
        Vector3    t_home  = _view.SlotPosition;
        Quaternion t_rot0  = t_tr.localRotation;
        Vector3    t_scale = t_tr.localScale;

        if (_exitVfx != BattleVfxId.None)
            BattleVfx.Play(_exitVfx, t_tr.position, _view.VfxSortingLayerId);

        // 리드 타임은 안개가 깔리는 시간이라 교활 전용이다 — 반짝임은 회전과 같이 터져야 교체 순간으로 읽힌다.
        float t_lead = _exitVfx == BattleVfxId.CunningFog ? GameTiming.Battle.CunningFogLead : 0f;
        if (t_lead > 0f) await UniTask.Delay((int)(t_lead * 1000));
        if (_view == null) return;

        // 한 바퀴. 뒷면 교체는 **딱 반 바퀴 지점** — 카드가 옆으로 서서 두께만 보이는 순간이라
        // 그림이 바뀌는 게 안 보인다(앞뒤에서 바꾸면 툭 튄다).
        float t_spin = Mathf.Max(0.05f, GameTiming.Battle.CunningSpinDuration);
        t_tr.DOKill();
        await DOTween.Sequence().SetLink(_view.gameObject)
            .Append(t_tr.DOLocalRotate(new Vector3(0f, -360f, 0f), t_spin, RotateMode.LocalAxisAdd)
                       .SetEase(Ease.InOutQuad))
            .InsertCallback(t_spin * 0.5f, () => { if (_view != null) _view.Render(_view.BoundCard); })
            .ToUniTask();

        if (_view == null) return;

        float t_exit = Mathf.Max(0.05f, GameTiming.Battle.CunningExitDuration);
        Vector3 t_to = DeckExitPoint(_view, t_home.z);

        // 축소는 이동보다 먼저 끝나고, 덱 도착 직전에는 사라진다. 목표가 화면 안의 덱 버튼이므로
        // 페이드가 없으면 버튼 위에 멈췄다가 툭 사라져 보인다.
        float t_shrink = Mathf.Max(0.05f, t_exit * GameTiming.Battle.CunningShrinkRatio);
        float t_fade   = Mathf.Min(t_exit, Mathf.Max(0.05f, t_exit * 0.25f));
        try
        {
            await DOTween.Sequence().SetLink(_view.gameObject)
                .Join(t_tr.DOMove(t_to, t_exit).SetEase(Ease.InQuad))
                .Join(t_tr.DOScale(t_scale * EXIT_SCALE, t_shrink).SetEase(Ease.InQuad))
                .InsertCallback(t_exit - t_fade, () => { if (_view != null) _view.FadeView(0f, t_fade); })
                .ToUniTask();
        }
        finally
        {
            // 슬롯 뷰는 재사용된다. 중단/취소돼도 자세와 알파를 함께 원복해야 다음 카드가 정상 표시된다.
            if (_view != null)
            {
                t_tr.DOKill();
                t_tr.position      = t_home;
                t_tr.localRotation = t_rot0;
                t_tr.localScale    = t_scale;
                _view.FadeView(1f, 0f);
            }
        }
    }

    /// <summary>교대해 들어온 카드의 등장. **보충 등장과 완전히 같은 시퀀스**(CardAppearSequence)를 쓴다 —
    /// 중앙 정지·등장 컷씬까지 포함이다. 여기서 배치 연출만 직접 부르면 같은 "덱에서 온 카드"인데
    /// 교활로 들어온 쪽만 컷씬이 빠진다(실제로 그랬다).
    /// 슬롯 뷰에 들어온 카드가 이미 그려져 있어야 한다(호출 전 Refresh) — 컷씬 자격 판정이 BoundCard 기준.</summary>
    public static UniTask PlayEnter(CardView _view)
    {
        if (_view == null) return UniTask.CompletedTask;

        Vector3 t_dest = _view.SlotPosition;
        Vector3 t_from = DeckSpawnPoint(_view, t_dest.z);
        Vector3 t_mid  = CameraUtil.ScreenFractionToWorld(0.5f, 0.5f, t_dest.z);

        return CardAppearSequence.Play(_view, _view.BoundCard, t_from, t_mid, t_dest,
                                       GameTiming.Battle.CardDealDuration, _playAppearVfx: true);
    }

    /// <summary>퇴장 목표는 해당 소유자의 실제 덱 버튼. 버튼은 safe-area 하위이므로 화면 형태를 그대로 따른다.
    /// 덱 UI가 없는 테스트 씬/비정상 배선에서는 safe-area 안쪽 모서리로 폴백한다.</summary>
    static Vector3 DeckExitPoint(CardView _view, float _z)
    {
        DeckPileUI t_deck = _view.BoundCard != null ? DeckPileUI.For(_view.BoundCard.ownerIndex) : null;
        if (t_deck != null) return CameraUtil.ScreenPointToWorld(t_deck.AnchorScreenPoint, _z);

        Rect t_safe = Screen.safeArea;
        float t_margin = Mathf.Min(t_safe.width, t_safe.height) * 0.05f;
        Vector2 t_point = _view.IsEnemySide
            ? new Vector2(t_safe.xMin + t_margin, t_safe.yMax - t_margin)
            : new Vector2(t_safe.xMax - t_margin, t_safe.yMin + t_margin);
        return CameraUtil.ScreenPointToWorld(t_point, _z);
    }

    /// <summary>등장 시작점은 기존처럼 화면 밖. 퇴장 목표와 합치면 덱 위에서 카드가 팝업된다.</summary>
    static Vector3 DeckSpawnPoint(CardView _view, float _z)
        => _view.IsEnemySide
            ? CameraUtil.ScreenFractionToWorld(-0.15f, 1.15f, _z)
            : CameraUtil.ScreenFractionToWorld( 1.15f, -0.15f, _z);
}
