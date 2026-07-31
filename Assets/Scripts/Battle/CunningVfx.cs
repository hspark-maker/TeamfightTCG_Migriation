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

    public static async UniTask PlayExit(CardView _view)
    {
        if (_view == null) return;

        Transform  t_tr    = _view.transform;
        Vector3    t_home  = _view.SlotPosition;
        Quaternion t_rot0  = t_tr.localRotation;
        Vector3    t_scale = t_tr.localScale;

        BattleVfx.Play(BattleVfxId.CunningFog, t_tr.position, _view.VfxSortingLayerId);

        float t_lead = GameTiming.Battle.CunningFogLead;
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
        Vector3 t_to = DeckPoint(_view, t_home.z);

        // 축소는 이동보다 먼저 끝난다 — 작아진 뒤 남은 거리를 미끄러져야 덱에 빨려 들어간 것으로 보인다.
        float t_shrink = Mathf.Max(0.05f, t_exit * GameTiming.Battle.CunningShrinkRatio);
        t_tr.DOScale(t_scale * EXIT_SCALE, t_shrink).SetEase(Ease.InQuad).SetLink(_view.gameObject);

        await t_tr.DOMove(t_to, t_exit).SetEase(Ease.InQuad).SetLink(_view.gameObject).ToUniTask();

        // 슬롯 뷰는 **재사용된다** — 자세를 원복하지 않으면 다음에 이 슬롯을 쓰는 카드가
        // 화면 밖에서 뒤집힌 채 시작한다(보충이 없어 PlayDealAnim이 안 도는 경우엔 영영 그대로).
        if (_view == null) return;
        t_tr.DOKill();
        t_tr.position      = t_home;
        t_tr.localRotation = t_rot0;
        t_tr.localScale    = t_scale;
    }

    /// <summary>교대해 들어온 카드의 등장. **보충 등장과 완전히 같은 시퀀스**(CardAppearSequence)를 쓴다 —
    /// 중앙 정지·등장 컷씬까지 포함이다. 여기서 배치 연출만 직접 부르면 같은 "덱에서 온 카드"인데
    /// 교활로 들어온 쪽만 컷씬이 빠진다(실제로 그랬다).
    /// 슬롯 뷰에 들어온 카드가 이미 그려져 있어야 한다(호출 전 Refresh) — 컷씬 자격 판정이 BoundCard 기준.</summary>
    public static UniTask PlayEnter(CardView _view)
    {
        if (_view == null) return UniTask.CompletedTask;

        Vector3 t_dest = _view.SlotPosition;
        Vector3 t_from = DeckPoint(_view, t_dest.z);
        Vector3 t_mid  = CameraUtil.ScreenFractionToWorld(0.5f, 0.5f, t_dest.z);

        return CardAppearSequence.Play(_view, _view.BoundCard, t_from, t_mid, t_dest,
                                       GameTiming.Battle.CardDealDuration);
    }

    /// <summary>카드가 빨려 들어가는 지점. **화면 기준 고정** — 아군은 오른쪽 아래, 적은 왼쪽 위 바깥.
    /// 덱 UI 좌표를 따라가 봤지만 캔버스 스케일·앵커 조합에 따라 엉뚱한 자리로 튀어서, 화면 모서리로 고정한다
    /// (덱 더미가 각 진영 그 방향에 있으므로 결과는 같고, 씬 배선에 의존하지 않는다).</summary>
    static Vector3 DeckPoint(CardView _view, float _z)
        => _view.IsEnemySide
            ? CameraUtil.ScreenFractionToWorld(-0.15f, 1.15f, _z)
            : CameraUtil.ScreenFractionToWorld( 1.15f, -0.15f, _z);
}
