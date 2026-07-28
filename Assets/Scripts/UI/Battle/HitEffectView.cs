using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// 피격/회복 연출. CardView 프리팹의 자식(루트)으로, 붐 스프라이트 팝 + 숫자 플로팅을 함께 재생.
/// 구조: 루트(HitEffectView) ─ Boom(SpriteRenderer) / DmgText(TMP). 붐 스케일이 텍스트에 영향 안 가게 자식 분리.
/// CardAnimator.PlayHitAnim(_damage)가 매 피격마다 Play(damage) 호출. 슬롯에 새 카드가 들어오면 Stop()으로 잔여 제거.
/// isHeal=1인 인스턴스(HealEffect)는 같은 연출을 "+N" 부호로 재생 — 회복 시 CardAnimator.PlayHealEffect가 호출.
/// </summary>
public class HitEffectView : MonoBehaviour
{
    [SerializeField] Transform      boom;      // 붐 스프라이트 트랜스폼(스케일 팝 대상).
    [SerializeField] SpriteRenderer sr;        // 붐 스프라이트(페이드 대상).
    [SerializeField] TMP_Text       dmgText;   // 데미지 숫자.
    [SerializeField] float dur       = 0.22f;  // 붐 팝 시간.
    [SerializeField] float boomScale = 1.4f;   // 붐 최대 스케일.
    [SerializeField] float textFloat = 0.6f;   // 데미지 텍스트 상승 거리.
    [SerializeField] float textDurMul = 1.6f;  // 텍스트 지속 = dur*배율.
    [SerializeField] bool  isHeal;             // true면 숫자 부호를 "+"로(회복 연출용 인스턴스).

    bool     cached;
    float    baseAlpha = 1f;
    Vector3  textHome;
    Sequence playSeq;

    public void Play(int _amount = 0)
    {
        if (this.sr == null && this.boom != null) this.sr = this.boom.GetComponent<SpriteRenderer>();
        if (!this.cached)
        {
            this.baseAlpha = this.sr != null ? this.sr.color.a : 1f;
            if (this.dmgText != null) this.textHome = this.dmgText.transform.localPosition;
            this.cached = true;
        }

        Stop();   // 이전 연출 잔여 제거(같은 슬롯 연속 피격/카드 교체 대비).

        gameObject.SetActive(true);
        float t_textDur = this.dur * this.textDurMul;

        this.playSeq = DOTween.Sequence().SetLink(gameObject);

        // 붐: 스케일 팝 + 페이드.
        if (this.boom != null)
        {
            this.boom.localScale = Vector3.one * 0.5f;
            this.playSeq.Insert(0f, this.boom.DOScale(this.boomScale, this.dur).SetEase(Ease.OutBack));
        }
        if (this.sr != null)
        {
            Color t_c = this.sr.color; t_c.a = this.baseAlpha; this.sr.color = t_c;
            this.playSeq.Insert(0f, this.sr.DOFade(0f, this.dur));
        }

        // 숫자: 위로 뜨며 페이드. 0 이하면 숨김. 부호는 isHeal에 따라 +/-.
        if (this.dmgText != null)
        {
            if (_amount > 0)
            {
                this.dmgText.gameObject.SetActive(true);
                this.dmgText.text = this.isHeal ? $"+{_amount}" : $"-{_amount}";
                Transform t_tt = this.dmgText.transform;
                t_tt.localPosition = this.textHome;
                Color t_col = this.dmgText.color; t_col.a = 1f; this.dmgText.color = t_col;
                this.playSeq.Insert(0f, t_tt.DOLocalMoveY(this.textHome.y + this.textFloat, t_textDur).SetEase(Ease.OutCubic));
                this.playSeq.Insert(0f, this.dmgText.DOFade(0f, t_textDur));
            }
            else this.dmgText.gameObject.SetActive(false);
        }

        // 가장 긴 연출 후 루트 비활성(Kill되면 미호출 → Stop이 이미 처리).
        this.playSeq.OnComplete(() => gameObject.SetActive(false));
    }

    /// <summary>진행 중인 피격 연출을 즉시 중단·숨김. 슬롯에 새 카드가 들어오기 전 CardAnimator가 호출.</summary>
    public void Stop()
    {
        this.playSeq?.Kill();
        this.playSeq = null;
        if (this.boom != null) this.boom.DOKill();
        if (this.sr != null) this.sr.DOKill();
        if (this.dmgText != null)
        {
            this.dmgText.DOKill();
            this.dmgText.transform.DOKill();
            this.dmgText.gameObject.SetActive(false);
        }
        gameObject.SetActive(false);
    }
}
