using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 카드 등장 연출 중 **번개 구체 등장**의 순서만 소유한다:
/// 화면 중앙에서 구체가 태어나 살짝 커브를 그리며 슬롯으로 날아가고 → 도착 지점에서 구체가 사라지며 카드가 튀어나온다.
///
/// 프리팹 = BattleVfxLibrary(BattleVfxId.CinemaEnergyOrb) — **시네마 돌진과 똑같은 구체를 공유한다**.
/// 어느 카드가 이 연출인지 = CardData.cinemaAttackStyle(EnergyOrbDash) — 공격과 등장이 한 축이라 따로 켜고 끌 수 없다.
/// 여기엔 둘 다 두지 않는다.
///
/// **순수 연출이다** — 카드 배치(슬롯 상태)는 호출 전에 이미 끝나 있고 여기선 보여주기만 늦춘다.
/// 기존 배치 연출(CardAnimator.PlayDealAnim)과 시작/끝 상태가 같아야 한다:
/// 끝나면 카드는 슬롯 좌표 · 알파 1 · 스케일 1 · 회전 0.
/// </summary>
public static class CardAppearVfx
{
    const float ORB_CURVE_HEIGHT = 1.1f;   // 직선에서 제어점을 밀어내는 거리(0이면 직선) — "살짝" 도는 정도
    const float ORB_POP_RATIO    = 0.35f;  // 전체 시간 중 도착 후 카드가 튀어나오는 데 쓰는 비율
    const float ORB_MORPH_RATIO  = 0.3f;   // 중앙에 선 카드가 구체로 바뀌는 데 쓰는 비율(_morphFromCard 전용)
    const float ORB_Z_LIFT       = 0.1f;   // 구체는 카드보다 살짝 앞에서 난다(다른 슬롯에 가리지 않게)

    /// <summary>중앙(_mid)에서 슬롯(_dest)까지 구체 비행 후 카드 등장. 구체가 미배선이면
    /// 비행 없이 슬롯에서 카드만 튀어나온다(무동작 안전 — 등장 자체는 절대 건너뛰지 않는다).
    ///
    /// <paramref name="_morphFromCard"/>=true면 <b>카드가 이미 중앙에 서 있는 상태</b>로 들어온다
    /// (PlayDealToMid가 앞 토막을 돌린 뒤). 이 경우 구체를 맨땅에서 만들지 않고 카드가 줄어드는 동안
    /// 구체가 자라는 교대(변신)를 먼저 돌린다 — 안 그러면 중앙의 카드가 한 프레임에 사라져 보인다.
    /// false면 종전대로 카드가 화면 밖에 있다고 보고 자세부터 정리한다.</summary>
    public static async UniTask PlayOrbCurve(CardView _view, Vector3 _mid, Vector3 _dest, float _duration,
        bool _morphFromCard = false)
    {
        if (_view == null) return;

        Transform t_tr = _view.transform;
        var       t_ct = _view.GetCancellationTokenOnDestroy();

        if (!_morphFromCard)
        {
            // 소리는 "덱에서 나오는 순간"에 한 번. 변신 경로는 앞 토막(PlayDealToMid)이 이미 냈으므로 여기선 안 낸다.
            SoundManager.Instance?.PlayDealCard();
            SoundManager.Instance?.PlaySpawnVoice(_view.BoundCard?.data?.spawnVoices);

            // 카드는 아직 화면 밖(호출부가 미리 치워둔 상태)이다. 여기서 자세를 미리 정리해 둔다 —
            // **알파 복원이 특히 중요**: 죽은 카드가 있던 슬롯은 알파 0으로 남아 있고,
            // 기존 경로에선 PlayDealAnim이 그 복원을 맡았다(이 경로도 같은 책임을 진다).
            _view.FadeView(1f, 0f);
            t_tr.DOKill();
            t_tr.localRotation = Quaternion.identity;
            t_tr.localScale    = Vector3.zero;   // 슬롯으로 옮겨도 보이지 않게(구체가 도착할 때까지)
        }

        float t_pop   = Mathf.Max(0.05f, _duration * ORB_POP_RATIO);
        float t_morph = _morphFromCard ? Mathf.Max(0.05f, _duration * ORB_MORPH_RATIO) : 0f;
        float t_fly   = Mathf.Max(0.05f, _duration - t_pop - t_morph);
        float t_orbZ  = _dest.z - ORB_Z_LIFT;

        // 변신 경로의 구체 출발점은 _mid가 아니라 **카드가 실제로 서 있는 자리**다 —
        // 앞 토막이 z를 앞으로 당겨 놨어서 _mid를 그대로 쓰면 구체가 카드에서 어긋난 채 태어난다.
        Vector3 t_origin = _morphFromCard ? t_tr.position : _mid;
        Vector3 t_start  = new Vector3(t_origin.x, t_origin.y, t_orbZ);
        Vector3 t_end    = new Vector3(_dest.x, _dest.y, t_orbZ);

        VfxHandle t_orb   = BattleVfx.Spawn(BattleVfxId.CinemaEnergyOrb, t_start, _view.VfxSortingLayerId);
        Transform t_orbTr = t_orb.Valid ? t_orb.Go.transform : null;
        Vector3   t_orbScale = t_orbTr != null ? t_orbTr.localScale : Vector3.one;

        if (_morphFromCard)
        {
            // 카드 축소와 구체 확대를 같은 시간에 — 한쪽이 먼저 끝나면 중앙이 잠깐 비어 보인다.
            if (t_orbTr != null) t_orbTr.localScale = Vector3.zero;
            t_tr.DOKill();
            Tween t_shrink = t_tr.DOScale(Vector3.zero, t_morph).SetEase(Ease.InBack).SetLink(t_tr.gameObject);
            if (t_orbTr != null)
                t_orbTr.DOScale(t_orbScale, t_morph).SetEase(Ease.OutBack).SetLink(t_orbTr.gameObject);

            bool t_morphCanceled = await t_shrink.ToUniTask(cancellationToken: t_ct).SuppressCancellationThrow();
            if (t_morphCanceled || _view == null)
            {
                ReleaseOrb(t_orb, t_orbTr, t_orbScale);
                return;
            }
            t_tr.localRotation = Quaternion.identity;   // 앞 토막이 남긴 회전 정리(도착 자세 규약)
        }

        if (t_orbTr != null)
        {
            bool t_canceled = await Travel(t_orbTr, t_start, t_end, t_fly, t_ct);
            if (t_canceled)
            {
                ReleaseOrb(t_orb, t_orbTr, t_orbScale);
                return;
            }
        }

        // 도착. 카드를 슬롯에 놓고(스케일 0이라 아직 안 보인다) 구체와 교대시킨다.
        if (_view == null) { ReleaseOrb(t_orb, t_orbTr, t_orbScale); return; }
        t_tr.position = _dest;

        Tween t_cardPop = t_tr.DOScale(Vector3.one, t_pop).SetEase(Ease.OutBack).SetLink(t_tr.gameObject);
        if (t_orbTr != null)
            t_orbTr.DOScale(Vector3.zero, t_pop).SetEase(Ease.InQuad).SetLink(t_orbTr.gameObject);

        await t_cardPop.ToUniTask(cancellationToken: t_ct).SuppressCancellationThrow();

        ReleaseOrb(t_orb, t_orbTr, t_orbScale);

        // 중간에 끊겼어도 카드는 반드시 정상 자세로 남는다(다음 연출이 찌그러진 카드로 시작하지 않게).
        if (_view == null) return;
        t_tr.position      = _dest;
        t_tr.localRotation = Quaternion.identity;
        t_tr.localScale    = Vector3.one;
    }

    /// <summary>구체 반납. **스케일을 원복한 뒤** 돌려보낸다 — 안 하면 풀에서 다시 나올 때 0 크기로 나온다.</summary>
    static void ReleaseOrb(VfxHandle _orb, Transform _orbTr, Vector3 _baseScale)
    {
        if (_orbTr != null)
        {
            _orbTr.DOKill();
            _orbTr.localScale = _baseScale;
        }
        _orb.Release();   // 자기반납형 프리팹이면 무동작
    }

    /// <summary>2차 베지어 비행. 트윈(DOPath) 대신 프레임 보간 — 커브 형태를 코드로 쥐고 있는 편이
    /// 슬롯마다 달라지는 방향을 다루기 쉽다(HealVfx.Travel과 같은 규약).
    /// 반환 true = **카드가 사라져** 등장 자체를 접어야 하는 경우뿐. 구체가 중간에 없어진 것(풀 flush)은
    /// 비행만 끝내고 false를 준다 — 연출이 깨져도 카드는 반드시 슬롯에 나타나야 한다.</summary>
    static async UniTask<bool> Travel(Transform _tr, Vector3 _start, Vector3 _end, float _dur,
        System.Threading.CancellationToken _ct)
    {
        Vector3 t_ctrl = ControlPoint(_start, _end);
        float   t_time = 0f;

        while (t_time < _dur)
        {
            if (_tr == null) return false;   // 구체만 소실 → 남은 비행 생략, 카드 등장은 그대로 진행

            t_time      += Time.deltaTime;
            _tr.position = Bezier(_start, t_ctrl, _end, Mathf.Clamp01(t_time / _dur));

            bool t_canceled = await UniTask.Yield(PlayerLoopTiming.Update, _ct).SuppressCancellationThrow();
            if (t_canceled) return true;   // 카드 파괴(씬 전환 등)
        }
        return false;
    }

    static Vector3 Bezier(Vector3 _a, Vector3 _ctrl, Vector3 _b, float _t)
    {
        float t_inv = 1f - _t;
        return (t_inv * t_inv * _a) + (2f * t_inv * _t * _ctrl) + (_t * _t * _b);
    }

    /// <summary>직선 중점을 화면 수직 방향으로 밀어낸 제어점. 방향이 슬롯 위치에 따라 자연히 뒤집혀
    /// 중앙 좌우로 가는 카드가 서로 반대로 휜다(모두 같은 쪽으로 도는 것보다 덜 기계적이다).</summary>
    static Vector3 ControlPoint(Vector3 _start, Vector3 _end)
    {
        Vector3 t_line = _end - _start;
        Vector3 t_perp = new Vector3(-t_line.y, t_line.x, 0f);
        t_perp = t_perp.sqrMagnitude > 1e-6f ? t_perp.normalized : Vector3.up;

        Vector3 t_ctrl = ((_start + _end) * 0.5f) + (t_perp * ORB_CURVE_HEIGHT);
        t_ctrl.z = _start.z;   // 평면 유지 — z가 흔들리면 정렬이 카드 앞뒤로 튄다
        return t_ctrl;
    }
}
