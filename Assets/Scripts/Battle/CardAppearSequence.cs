using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

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
    /// 뷰의 BoundCard와 같아야 한다 — 호출 전 Refresh/Render가 끝나 있어야 하는 이유.</summary>
    public static async UniTask Play(CardView _view, CardInstance _card,
        Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration)
    {
        if (_view == null) return;

        VideoClip t_clip = CardCinematicRules.Resolve(_card);
        if (t_clip == null)
        {
            await _view.PlayDealAnim(_from, _mid, _dest, _duration);
            return;
        }

        // 컷씬은 **중앙에 멈춘 채** 본다. 끝나거나 스킵된 그 시점에 슬롯으로 들어간다.
        await _view.PlayDealToMid(_from, _mid, _dest, _duration);
        await CardCinematicPlayer.Play(t_clip);
        await _view.PlayDealToSlot(_mid, _dest, _duration);
    }
}
