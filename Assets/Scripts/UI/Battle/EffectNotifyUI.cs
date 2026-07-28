using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EffectNotifyUI : PooledUIBase
{
    [SerializeField] Image portrait;
    [SerializeField] TextMeshProUGUI cardNameText;
    [SerializeField] TextMeshProUGUI effectLabelText;
    [SerializeField] RectTransform panel;
    [SerializeField] float hiddenOffsetX = 400f;

    // 타이밍은 BattleTimingConfig 단일 진실원(전역 배속 적용). 프리팹 개별 값 없음.
    static float DisplayDuration => GameTiming.Battle.EffectNotifyDisplay;
    static float SlideDuration   => GameTiming.Battle.EffectNotifySlide;

    readonly Queue<EffectNotifyData> _queue = new Queue<EffectNotifyData>();
    bool _isPlaying;

    public override void Initialization(UIData _data)
    {
        if (_data is EffectNotifyData t_d)
            Enqueue(t_d);
    }

    public void Enqueue(EffectNotifyData _data)
    {
        _queue.Enqueue(_data);
        if (!_isPlaying)
            PlayQueue().Forget();
    }

    async UniTaskVoid PlayQueue()
    {
        _isPlaying = true;
        while (_queue.Count > 0)
            await PlayOne(_queue.Dequeue());
        _isPlaying = false;
        Hide();
    }

    async UniTask PlayOne(EffectNotifyData _data)
    {
        if (this.portrait != null)
        {
            this.portrait.sprite = _data.portrait;
            // 카드 초상화는 배너 프레임을 꽉 채우지만 시너지/키워드 아이콘은 정사각이라
            // 그대로 늘리면 찌그러진다. 소스가 아이콘일 때만 비율 유지.
            this.portrait.preserveAspect = _data.preserveAspect;
        }
        if (this.cardNameText != null) this.cardNameText.text = _data.cardName;
        if (this.effectLabelText != null) this.effectLabelText.text = _data.effectLabel;

        Show();

        Vector2 t_shown = Vector2.zero;
        Vector2 t_hidden = new Vector2(this.hiddenOffsetX, 0f);
        float t_dur   = _data.displayDuration > 0f ? _data.displayDuration : DisplayDuration;   // 호출부 지정이 있으면 우선.
        float t_slide = SlideDuration;

        this.panel.anchoredPosition = t_hidden;
        await this.panel.DOAnchorPos(t_shown, t_slide).SetEase(Ease.OutCubic).ToUniTask();
        await UniTask.Delay((int)(t_dur * 1000));
        await this.panel.DOAnchorPos(t_hidden, t_slide).SetEase(Ease.InCubic).ToUniTask();
    }

    public override void Show()
    {
        this.contents.SetActive(true);
        this.isShow = true;
    }

    public override void Hide()
    {
        this.contents.SetActive(false);
        this.isShow = false;
    }
}

public class EffectNotifyData : UIData
{
    public Sprite portrait;
    public string cardName;
    public string effectLabel;
    public float displayDuration = 0f; // 0 = 컴포넌트 기본값 사용
    /// <summary>portrait가 아이콘(정사각)일 때 true. 카드 초상화면 false로 두어 기존 꽉찬 표시 유지.</summary>
    public bool preserveAspect;
}
