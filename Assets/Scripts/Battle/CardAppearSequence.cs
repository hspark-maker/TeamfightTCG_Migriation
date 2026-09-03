using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 카드 한 장이 덱에서 나와 슬롯에 앉기까지의 **단일 시퀀스**.
/// 덱에서 나옴 → 중앙에서 확대·정지(카드 정보) → (자격되면 등장 컷씬) → 슬롯 착지.
///
/// 호출부는 둘이다: 보충 등장(BattleFieldView.PlayFillAnim)과 교활 교대 등장(CunningVfx.PlayEnter).
/// 두 곳이 각자 흐름을 짜면 "덱에서 온 같은 카드"인데 등장이 갈린다 — 실제로 교활로 들어온 카드만
/// 컷씬이 통째로 빠져 있었다. 그래서 순서는 여기 한 곳에만 둔다.
///
/// 컷씬 자격 판정은 CardCinematicRules 단독(여기서 stage/래치를 비교하지 말 것).
/// 자격이 없으면 앞/뒤 토막이 끊기지 않고 이어져 예전 흐름과 같다.
/// 카드별 등장 방식(일반 배치 / 에너지 구체)의 분기는 CardView가 소유한다 — 여기선 모른다.
/// </summary>
public static class CardAppearSequence
{
    /// <summary><paramref name="_card"/>는 컷씬 자격 판정 대상(= 이 슬롯에 들어온 인스턴스).
    /// 뷰의 BoundCard와 같아야 한다 — 호출 전 Refresh/Render가 끝나 있어야 하는 이유.
    ///
    /// <paramref name="_playAppearVfx"/>는 <b>등장 반짝임(CardAppear)</b>을 켠다. 발화 지점은
    /// 카드가 덱에서 나와 <b>중앙에 선 순간</b> 하나다 — 교체(퇴장) 쪽이 아니다.
    /// 교활 교대·멀리건 교체가 이걸 켠다: 두 경우 모두 "덱에서 새 카드가 나왔다"가 사건이라
    /// 반짝임은 들어오는 카드에 붙어야 읽힌다(물러나는 카드에 붙이면 교체 자체가 강조된다).</summary>
    public static async UniTask Play(CardView _view, CardInstance _card,
        Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration, bool _playAppearVfx = false)
    {
        if (_view == null) return;

        if (!_playAppearVfx)
        {
            await _view.PlayDealAnim(_from, _mid, _dest, _duration);
            return;
        }

        // 컷씬은 **중앙에 멈춘 채** 본다. 끝나거나 스킵된 그 시점에 슬롯으로 들어간다.
        await _view.PlayDealToMid(_from, _mid, _dest, _duration);
        if (_view == null) return;

        // 중앙 도착 = 등장 반짝임 발화점.
        BattleVfx.Play(BattleVfxId.CardAppear, _view.transform.position, _view.VfxSortingLayerId);

        bool t_cancelled = await UniTask.Delay((int)(GameTiming.Battle.DealMidPause * 1000),
                cancellationToken: _view.GetCancellationTokenOnDestroy())
            .SuppressCancellationThrow();
        if (t_cancelled) return;

        if (_view == null) return;
        await _view.PlayDealToSlot(_mid, _dest, _duration);
    }
}
