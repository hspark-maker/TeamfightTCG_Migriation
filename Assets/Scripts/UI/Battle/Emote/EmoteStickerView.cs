using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

/// <summary>
/// 감정표현 스티커 한 자리. 그림은 대각선 peel 메시로 붙고 떼어지며, 선택 AnimationClip이 있으면
/// 표시 수명 동안 반복한다. 클립은 숨겨진 원본 Image에만 재생하고 결과 sprite만 peel 판으로 복사한다.
/// 따라서 잘못 저작된 Transform·Color 커브가 peel 메시나 스티커 루트와 싸우지 않는다.
/// </summary>
public class EmoteStickerView : MonoBehaviour
{
    [SerializeField] CanvasGroup group;

    [Tooltip("선택 클립을 샘플링하는 숨김 Image 프록시이자 정지 그림의 색·비율 기준.")]
    [SerializeField] Image image;

    [Tooltip("그림과 클립이 모두 없을 때 보이는 문자 스티커.")]
    [SerializeField] TMP_Text label;

    Tween m_tween;
    StickerPeelGraphic m_peel;

    PlayableGraph m_clipGraph;
    AnimationClipPlayable m_clipPlayable;
    AnimationClip m_clip;
    float m_clipTime;
    Animator m_proxyAnimator;

    Color m_proxyColor = Color.white;
    Vector3 m_proxyScale = Vector3.one;
    Quaternion m_proxyRotation = Quaternion.identity;
    Vector2 m_proxyPosition;
    Vector2 m_proxySize;

    void Awake()
    {
        if (this.image != null)
        {
            this.m_proxyColor = this.image.color;
            var t_rect = this.image.rectTransform;
            this.m_proxyScale = t_rect.localScale;
            this.m_proxyRotation = t_rect.localRotation;
            this.m_proxyPosition = t_rect.anchoredPosition;
            this.m_proxySize = t_rect.sizeDelta;
        }

        if (this.group != null) this.group.alpha = 0f;

        // ⚠ 여기서 gameObject.SetActive(false)를 부르면 안 된다.
        //   이 노드는 프리팹에서 이미 꺼진 상태로 저작돼 있고, 꺼진 오브젝트의 Awake는 **처음 켜지는 순간**
        //   실행된다 — Play가 SetActive(true) 한 바로 그 프레임에 Awake가 돌아 자기를 다시 꺼버린다.
        //   증상은 "처음 낸 감정표현 한 번만 안 보인다"였다(두 번째부터는 Awake가 이미 소진돼 정상).
        //   초기 상태의 주인은 프리팹 저작이다.
    }

    void Update()
    {
        if (this.m_clip == null || !this.m_clipGraph.IsValid()) return;

        float t_length = this.m_clip.length;
        if (t_length <= 0.0001f) return;

        this.m_clipTime = Mathf.Repeat(this.m_clipTime + Time.unscaledDeltaTime, t_length);
        this.SampleClip(this.m_clipTime);
    }

    public void Play(EmoteEntry _entry, EmoteCatalog _catalog)
    {
        if (_entry == null || _catalog == null) return;

        this.StopClip();
        Tween t_previous = this.m_tween;
        this.m_tween = null;
        t_previous?.Kill();
        transform.DOKill();
        transform.localScale = Vector3.one;

        this.RestoreProxy(_entry.sprite);
        gameObject.SetActive(true);

        // 클립을 먼저 한 번 샘플링해 실제 그림이 나오는지 판정한다. 표시 경로를 정한 뒤에만
        // peel/text 트윈을 시작해야 잘못된 클립이 두 트윈의 수명 핸들을 겹쳐 쓰지 않는다.
        bool t_clipReady = this.PlayClip(_entry.clip);
        Sprite t_visual = t_clipReady && this.image != null ? this.image.sprite : _entry.sprite;
        if (t_visual != null)
        {
            if (this.label != null) this.label.gameObject.SetActive(false);
            this.PlayPeel(t_visual, _catalog);
            return;
        }

        // 잘못된 클립은 계속 평가할 이유가 없다. 정지 폴백도 없으면 문자로 안전하게 내려간다.
        this.StopClip();
        if (this.label != null)
        {
            this.label.gameObject.SetActive(true);
            this.label.text = _entry.label;
        }
        if (this.m_peel != null)
        {
            this.m_peel.SetPeel(1f);
            this.m_peel.gameObject.SetActive(false);
        }
        this.PlayTextFallback(_catalog);
    }

    void PlayPeel(Sprite _sprite, EmoteCatalog _catalog)
    {
        this.EnsurePeel();
        if (this.m_peel == null)
        {
            if (this.image != null)
            {
                this.image.enabled = true;
                this.image.color = this.m_proxyColor;
            }
            if (this.group != null) this.group.alpha = 1f;

            this.m_tween = DOTween.Sequence().SetUpdate(true).SetLink(gameObject)
                                  .AppendInterval(_catalog.showDuration)
                                  .OnComplete(this.Hide);
            return;
        }

        this.m_peel.sprite = _sprite;
        this.m_peel.preserveAspect = this.image == null || this.image.preserveAspect;
        this.m_peel.color = this.m_proxyColor;
        this.m_peel.Configure(_catalog.peelCurlRadius, _catalog.peelSegments);
        this.m_peel.SetPeel(_catalog.peelStartAmount);
        this.m_peel.gameObject.SetActive(true);
        if (this.group != null) this.group.alpha = 1f;

        float t_in = Mathf.Max(0f, _catalog.peelInDuration);
        float t_out = Mathf.Max(0f, _catalog.peelOutDuration);
        float t_total = Mathf.Max(_catalog.showDuration, t_in + t_out);
        float t_hold = Mathf.Max(0f, t_total - t_in - t_out);

        Sequence t_seq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
        if (t_in > 0f)
            t_seq.Append(DOTween.To(() => this.m_peel.Amount, this.m_peel.SetPeel, 0f, t_in).SetEase(Ease.OutCubic));
        else
            this.m_peel.SetPeel(0f);

        t_seq.AppendInterval(t_hold);
        if (t_out > 0f)
            t_seq.Append(DOTween.To(() => this.m_peel.Amount, this.m_peel.SetPeel, 1f, t_out).SetEase(Ease.InCubic));
        else
            t_seq.AppendCallback(() => this.m_peel.SetPeel(1f));

        t_seq.OnComplete(this.Hide);
        this.m_tween = t_seq;
    }

    bool PlayClip(AnimationClip _clip)
    {
        if (_clip == null || this.image == null) return false;

        this.EnsureAnimationProxy();
        if (this.m_proxyAnimator == null) return false;

        this.m_clipGraph = PlayableGraph.Create($"EmoteClip_{name}");
        this.m_clipGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var t_output = AnimationPlayableOutput.Create(this.m_clipGraph, "Emote", this.m_proxyAnimator);
        this.m_clipPlayable = AnimationClipPlayable.Create(this.m_clipGraph, _clip);
        this.m_clipPlayable.SetApplyFootIK(false);
        this.m_clipPlayable.SetApplyPlayableIK(false);
        t_output.SetSourcePlayable(this.m_clipPlayable);
        this.m_clipGraph.Play();

        this.m_clip = _clip;
        this.m_clipTime = 0f;
        this.SampleClip(0f);
        return this.image.sprite != null;
    }

    void SampleClip(float _time)
    {
        if (!this.m_clipGraph.IsValid()) return;

        this.m_clipPlayable.SetTime(_time);
        this.m_clipGraph.Evaluate(0f);
        if (this.image != null && this.image.sprite != null) this.SetFrame(this.image.sprite);
    }

    void SetFrame(Sprite _frame)
    {
        if (this.m_peel != null && this.m_peel.gameObject.activeSelf)
        {
            this.m_peel.sprite = _frame;
            return;
        }
        if (this.image != null) this.image.sprite = _frame;
    }

    void EnsureAnimationProxy()
    {
        if (this.image == null) return;

        this.image.gameObject.SetActive(true);
        this.image.enabled = false;
        if (this.m_proxyAnimator == null) this.m_proxyAnimator = this.image.GetComponent<Animator>();
        if (this.m_proxyAnimator == null) this.m_proxyAnimator = this.image.gameObject.AddComponent<Animator>();
        this.m_proxyAnimator.runtimeAnimatorController = null;
        this.m_proxyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        this.m_proxyAnimator.enabled = true;
    }

    void StopClip()
    {
        this.m_clip = null;
        this.m_clipTime = 0f;
        if (this.m_clipGraph.IsValid()) this.m_clipGraph.Destroy();
        this.m_clipPlayable = default;
        if (this.m_proxyAnimator != null) this.m_proxyAnimator.enabled = false;
    }

    void RestoreProxy(Sprite _sprite)
    {
        if (this.image == null) return;

        var t_rect = this.image.rectTransform;
        t_rect.localScale = this.m_proxyScale;
        t_rect.localRotation = this.m_proxyRotation;
        t_rect.anchoredPosition = this.m_proxyPosition;
        t_rect.sizeDelta = this.m_proxySize;
        this.image.color = this.m_proxyColor;
        this.image.sprite = _sprite;
        this.image.enabled = false;
        this.image.gameObject.SetActive(true);
    }

    void PlayTextFallback(EmoteCatalog _catalog)
    {
        float t_fade = Mathf.Min(_catalog.fadeDuration, _catalog.showDuration * 0.4f);
        float t_hold = Mathf.Max(0f, _catalog.showDuration - t_fade * 2f);

        if (this.group != null) this.group.alpha = 0f;
        if (_catalog.popScale > 1f)
        {
            transform.DOPunchScale(Vector3.one * (_catalog.popScale - 1f), t_fade + t_hold * 0.2f,
                                   vibrato: 1, elasticity: 0.6f)
                     .SetUpdate(true)
                     .SetLink(gameObject);
        }

        Sequence t_seq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
        if (this.group != null)
        {
            t_seq.Append(this.group.DOFade(1f, t_fade).SetEase(Ease.OutQuad));
            t_seq.AppendInterval(t_hold);
            t_seq.Append(this.group.DOFade(0f, t_fade).SetEase(Ease.InQuad));
        }
        else
        {
            t_seq.AppendInterval(_catalog.showDuration);
        }
        t_seq.OnComplete(this.Hide);
        this.m_tween = t_seq;
    }

    void EnsurePeel()
    {
        if (this.m_peel != null) return;

        var t_go = new GameObject("Sticker_Peel",
                                  typeof(RectTransform), typeof(CanvasRenderer), typeof(StickerPeelGraphic));
        var t_rect = (RectTransform)t_go.transform;
        t_rect.SetParent(transform, false);
        t_rect.anchorMin = Vector2.zero;
        t_rect.anchorMax = Vector2.one;
        t_rect.offsetMin = Vector2.zero;
        t_rect.offsetMax = Vector2.zero;
        if (this.image != null) t_rect.SetSiblingIndex(this.image.transform.GetSiblingIndex() + 1);

        this.m_peel = t_go.GetComponent<StickerPeelGraphic>();
        this.m_peel.raycastTarget = false;
        t_go.SetActive(false);
    }

    public void Hide()
    {
        this.ResetVisuals();
        gameObject.SetActive(false);
    }

    void OnDisable() => this.ResetVisuals();

    void OnDestroy() => this.StopClip();

    void ResetVisuals()
    {
        Tween t_tween = this.m_tween;
        this.m_tween = null;
        t_tween?.Kill();
        this.StopClip();

        transform.DOKill();
        transform.localScale = Vector3.one;
        if (this.group != null) this.group.alpha = 0f;
        if (this.m_peel != null)
        {
            this.m_peel.SetPeel(1f);
            this.m_peel.gameObject.SetActive(false);
        }
        if (this.image != null)
        {
            this.RestoreProxy(null);
            this.image.gameObject.SetActive(false);
        }
    }
}
