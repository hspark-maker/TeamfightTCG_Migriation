using DG.Tweening;
using UnityEngine;

/// <summary>카드 한 장에 딸린 **무기 인스턴스의 전 수명**(생성 → 표시/애니메이션 → 파괴)을 소유한다.
/// 카드의 다른 표현(하이라이트/키워드/시너지)과 무관하므로 여기로 떼어냈다.
///
/// MonoBehaviour가 아니라 순수 C# 객체다 — CardView가 필드로 들고 생성한다.
/// 인스펙터 배선(weaponAnchor)은 CardView의 SerializeField에 그대로 남고 값만 생성자로 주입된다
/// (프리팹/씬 YAML 재직렬화 회피).
///
/// 무장 이펙트(<see cref="CardArmedVfx"/>)와는 별개 관심사다: 이쪽은 Instantiate/Destroy,
/// 저쪽은 풀 대여/반납이다.</summary>
public class CardWeaponView
{
    readonly Transform ownerTransform;   // 앵커가 미배선일 때의 폴백 부모(= 카드 루트)
    readonly Transform anchor;

    GameObject weaponInstance;
    Animator   weaponAnimator;
    Quaternion weaponBaseRot;

    public CardWeaponView(Transform _ownerTransform, Transform _anchor)
    {
        this.ownerTransform = _ownerTransform;
        this.anchor         = _anchor;
    }

    /// <summary>무기 교체. 기존 인스턴스는 파괴하고 <paramref name="_data"/>의 weaponPrefab으로 새로 만든다
    /// (데이터/프리팹이 없으면 무기 없는 상태로 끝난다).
    /// <paramref name="_card"/>는 진영 판정용 — 적 진영이면 CardData.enemyWeaponEuler로 회전시킨다.</summary>
    public void Setup(CardData _data, CardInstance _card)
    {
        if (this.weaponInstance != null)
        {
            // 무기 자식 스프라이트가 FadeView tween 대상으로 잡혀있을 수 있음.
            // 루트(SetLink 대상)는 살아있어 kill이 안 걸리므로 파괴 전 직접 DOKill.
            foreach (SpriteRenderer t_sr in this.weaponInstance.GetComponentsInChildren<SpriteRenderer>(true))
                t_sr.DOKill();
            this.weaponInstance.transform.SetParent(null);
            UnityEngine.Object.Destroy(this.weaponInstance);
            this.weaponInstance = null;
            this.weaponAnimator = null;
        }
        if (_data == null || _data.weaponPrefab == null) return;

        Transform t_anchor = this.anchor != null ? this.anchor : this.ownerTransform;
        this.weaponInstance = UnityEngine.Object.Instantiate(_data.weaponPrefab, t_anchor);
        this.weaponInstance.transform.localPosition = Vector3.zero;
        if (_card?.ownerIndex != TurnState.LocalOwnerIndex)
            this.weaponInstance.transform.localRotation = Quaternion.Euler(_data.enemyWeaponEuler);
        this.weaponBaseRot  = this.weaponInstance.transform.localRotation;
        this.weaponAnimator = this.weaponInstance.GetComponent<Animator>();
        this.weaponInstance.SetActive(false);
    }

    /// <summary>무기 조준 표시 토글(무장=true, 해제=false).
    /// 무장 이펙트는 여기 얹지 않는다 — ResolveHits가 접촉 직후 Focus(false)를 부르기 때문에
    /// 같이 묶으면 반동이 끝나기도 전에 이펙트가 꺼진다. 무장/해제 시점에서 CardArmedVfx를 직접 부른다.</summary>
    public void Focus(bool _active)
    {
        if (this.weaponInstance == null) return;
        if (_active)
        {
            this.weaponInstance.SetActive(true);
            if (this.weaponAnimator != null)
            {
                this.weaponAnimator.enabled = true;
                this.weaponAnimator.Rebind();
            }
        }
        else
        {
            if (this.weaponAnimator != null)
            {
                this.weaponAnimator.Rebind();
                this.weaponAnimator.enabled = false;
            }
            this.weaponInstance.SetActive(false);
        }
    }

    /// <summary>공격 타격 모션 재생. 트리거 이름은 카드의 AttackEffect가 소유한다(없으면 무동작).
    /// 재생 전 회전을 <c>weaponBaseRot</c>으로 되돌린다 — 직전 연출로 기울어진 채 시작하지 않게.</summary>
    public void PlayAttackAnim(CardInstance _card)
    {
        if (this.weaponInstance == null) return;
        string t_trigger = _card?.data.attackEffect?.animTrigger;
        if (string.IsNullOrEmpty(t_trigger)) return;
        this.weaponInstance.SetActive(true);
        this.weaponInstance.transform.localRotation = this.weaponBaseRot;
        if (this.weaponAnimator == null) return;
        this.weaponAnimator.enabled = true;
        this.weaponAnimator.Play(t_trigger, 0, 0f);
    }

    /// <summary>소유자(CardView) 파괴 시 정리. 무기 인스턴스는 카드의 자식이라 Unity가 함께 파괴하므로
    /// 여기선 참조만 끊는다 — 파괴 도중 SetParent(null)/Destroy를 다시 걸지 않는다.</summary>
    public void Cleanup()
    {
        this.weaponInstance = null;
        this.weaponAnimator = null;
    }
}
