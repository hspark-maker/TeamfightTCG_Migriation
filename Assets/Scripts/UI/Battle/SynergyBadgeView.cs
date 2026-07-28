using UnityEngine;
using DG.Tweening;

// 카드 1개의 시너지 소속 배지(월드 스페이스). SynergyData.icon 스프라이트 표시.
// CardView가 배치/리바인드 시점에만 세팅한다. 시너지는 덱 확정 스냅샷(BattleField.Synergy)이라
// 전투 중 재계산·변동 없음 → 배지도 Set 이후 스스로 갱신하지 않는다.
public class SynergyBadgeView : MonoBehaviour
{
    [SerializeField] SpriteRenderer icon; // 시너지 아이콘(SynergyData.icon). 텍스트 대신 표시.

    [Header("Active / Inactive BG")]
    // 활성/비활성 구분은 알파/RGB dim이 아니라 오브젝트 토글로 인코딩한다.
    // CardAnimator.FadeView/Deal/Death가 자식 SpriteRenderer/TMP_Text의 알파를 tween으로 덮어쓰므로
    // 알파/RGB dim은 카드 페이드 복귀 시 사라진다. 서로 다른 BG 오브젝트를 켜고 끄면
    // 페이드(알파)와 무관하게 활성/비활성 구분이 유지된다.
    [Header("Active Pop")]
    // 효과 발동 순간 튀는 연출. 기준스케일(=배치 시 프리팹 로컬스케일) 기준 punch.
    // Set 시 자동 재생 안 함 — CardView.PopSynergyBadge가 실제 발동 게이트에서만 호출한다.
    [SerializeField]
    float popScale = 1.6f; // 기준스케일 대비 최대 배율(효과 발동시 큰 pop)

    [SerializeField] float popDuration = 0.25f; // pop 전체 시간(초)

    Vector3 _baseScale; // 기준 로컬스케일. 첫 Set 전에 캐시.
    bool _baseCached;

    /// <summary>이 배지가 표시 중인 시너지. PopSynergyBadge가 대상 배지를 매칭하는 데 사용.</summary>
    public SynergyData Synergy { get; private set; }

    /// <summary>배지를 특정 시너지로 세팅. _synergy null이면 비활성화(빈 태그 슬롯).</summary>
    public void Set(SynergyData _synergy, bool _active)
    {
        CacheBaseScale();

        this.Synergy = _synergy;

        if (_synergy == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        if (this.icon != null)
        {
            if (_active)
                this.icon.sprite = _synergy.activeIcon;
            else
                this.icon.sprite = _synergy.inactiveIcon;
        }


        // 자동 pop 없음. 활성/비활성 무관하게 기준스케일로 정렬(효과 발동시에만 PlayPop).
        if (!_active)
            RestoreBaseScale();
    }

    /// <summary>효과 발동 순간의 pop 연출을 재생한다. 실제 발동 게이트에서 CardView.PopSynergyBadge가 호출.</summary>
    public void PlayPop()
    {
        CacheBaseScale();

        // DOTween 규약: 재생 전 이전 tween 정리 + SetLink로 파괴 시 접근방지.
        this.transform.DOKill();
        this.transform.localScale = this._baseScale;
        this.transform
            .DOPunchScale(this._baseScale * (this.popScale - 1f), this.popDuration)
            .SetLink(this.gameObject);
    }

    void RestoreBaseScale()
    {
        this.transform.DOKill();
        this.transform.localScale = this._baseScale;
    }

    void CacheBaseScale()
    {
        if (this._baseCached) return;
        this._baseScale = this.transform.localScale;
        this._baseCached = true;
    }
}