using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 보상 코인이 원점에서 흩어졌다가 수치 쪽으로 빨려 들어가는 UI 연출.
// 궤적 규칙은 UiGainBurst가 갖고, 이 컴포넌트는 "코인을 만들고 걷는" 일만 한다(카드 연출과 궤적이 갈라지지 않게).
// 코인은 재생할 때 만들고 끝나면 지운다 — 한 번만 도는 결과 화면을 위한 최소 구현(풀링 없음).
//
// 시퀀스를 재생하지 않고 만들어서 돌려준다(BuildBurst). 호출자가 자기 연출 시퀀스에 붙여야
// 스킵 한 번으로 코인까지 함께 최종 상태로 끌어당길 수 있다.
public class CoinBurstEffect : MonoBehaviour
{
    [Header("배선")]
    [Tooltip("코인 아이콘 스프라이트. 비우면 연출을 건너뛰고 수치만 즉시 확정한다.")]
    [SerializeField] Sprite coinSprite;
    [SerializeField] RectTransform spawnCenter;   // 분출 원점(미배선이면 자기 자신)
    [SerializeField] RectTransform target;        // 코인이 모이는 목적지(보통 골드 수치)

    [Header("연출 값")]
    [SerializeField] int coinCount = 10;
    [SerializeField] float coinSize = 96f;
    [Tooltip("흩어지는 거리(이 오브젝트의 로컬 = 캔버스 참조px).")]
    [SerializeField] float scatterRadius = 240f;
    [SerializeField] float scatterDuration = 0.28f;
    [SerializeField] float gatherDuration = 0.32f;
    [Tooltip("코인 한 장씩 출발이 밀리는 간격. 0이면 전부 동시에 튄다.")]
    [SerializeField] float coinInterval = 0.06f;
    [SerializeField] float popDuration = 0.12f;   // 코인이 생겨나며 커지는 시간
    [Tooltip("흩어지는 부채꼴의 시작 각(도).")]
    [SerializeField] float angleStart = 18f;
    [Tooltip("흩어지는 부채꼴의 각도 폭. 360이면 전방위.")]
    [SerializeField] float angleSpan = 360f;
    [Tooltip("목적지로 빨려들 때 직선에서 부풀어 오르는 폭(px). 0이면 직선으로 간다.")]
    [SerializeField] float arcHeight = 0f;

    readonly List<GameObject> m_coins = new List<GameObject>();

    /// <summary>연출 전체 길이(초).</summary>
    public float TotalDuration => BuildSettings().TotalDuration;

    /// <summary>
    /// 배선을 런타임에 갈아 끼운다(프리팹에 미리 꽂아둘 수 없는 상황용 — 로비 획득 연출이 이 경로를 쓴다).
    /// 넘기지 않은 값(_coinCount 음수, 각도 null)은 직렬화된 값을 유지한다.
    /// </summary>
    public void Configure(Sprite _coinSprite, RectTransform _spawnCenter, RectTransform _target,
                          int _coinCount = -1, float? _angleStart = null, float? _angleSpan = null,
                          float? _scatterRadius = null, float? _gatherDuration = null,
                          float? _coinSize = null, float? _coinInterval = null,
                          float? _scatterDuration = null, float? _arcHeight = null)
    {
        this.coinSprite  = _coinSprite;
        this.spawnCenter = _spawnCenter;
        this.target      = _target;
        if (_coinCount >= 0) this.coinCount = _coinCount;
        if (_angleStart.HasValue) this.angleStart = _angleStart.Value;
        if (_angleSpan.HasValue) this.angleSpan = _angleSpan.Value;
        // 코인 말고 다른 알갱이(해금 연출의 빛)를 태울 때 크기·간격까지 주입해야 한 인스턴스가 두 연출을 오갈 수 있다.
        if (_coinSize.HasValue) this.coinSize = _coinSize.Value;
        if (_coinInterval.HasValue) this.coinInterval = _coinInterval.Value;
        // 출발과 목적지가 멀면 흩어짐은 좁게·수렴은 길게 가야 한다 — 한 인스턴스로 가까운/먼 연출을 오가려면 이 둘도 주입돼야 한다.
        if (_scatterRadius.HasValue) this.scatterRadius = _scatterRadius.Value;
        if (_gatherDuration.HasValue) this.gatherDuration = _gatherDuration.Value;
        // 터짐은 빠르게·궤적은 휘어서. 이 둘도 모드마다 갈리므로 인스턴스에 남은 직전 값이 새지 않게 함께 주입한다.
        if (_scatterDuration.HasValue) this.scatterDuration = _scatterDuration.Value;
        if (_arcHeight.HasValue) this.arcHeight = _arcHeight.Value;
    }

    /// <summary>
    /// 분출→수렴 시퀀스를 만들어 돌려준다(재생은 호출자 시퀀스에 맡긴다).
    /// _onArrived(도착한 장수, 전체 장수)는 코인이 목적지에 닿을 때마다 불린다 — 수치 증가를 여기에 맞물린다.
    /// </summary>
    public Sequence BuildBurst(Action<int, int> _onArrived)
    {
        ClearCoins();

        // 스프라이트 미배선/장수 0 = 연출 없음. 그래도 수치는 최종값으로 확정해 진행을 막지 않는다.
        if (this.coinSprite == null || this.coinCount <= 0)
        {
            var t_empty = DOTween.Sequence().SetLink(gameObject);
            t_empty.AppendCallback(() => _onArrived?.Invoke(1, 1));
            return t_empty;
        }

        var t_self = (RectTransform)transform;
        Vector2 t_from = this.spawnCenter != null ? UiGainBurst.ToLayerLocal(t_self, this.spawnCenter) : Vector2.zero;
        Vector2 t_to   = this.target != null ? UiGainBurst.ToLayerLocal(t_self, this.target) : Vector2.zero;

        var t_seq = UiGainBurst.Build(t_self, t_from, t_to, BuildSettings(),
                                      _spawn: _i => (RectTransform)CreateCoin().transform,
                                      _despawn: _rt => { if (_rt != null) _rt.gameObject.SetActive(false); },
                                      _onArrived: _onArrived);
        t_seq.SetLink(gameObject);

        // 정상 종료든 스킵(Complete)이든 여기서 코인을 걷는다.
        t_seq.AppendCallback(ClearCoins);
        return t_seq;
    }

    void OnDisable()
    {
        // 연출 도중 꺼지면 시퀀스의 마지막 콜백이 오지 않는다 — 남은 코인은 여기서 정리.
        ClearCoins();
    }

    UiGainBurst.Settings BuildSettings()
        => new UiGainBurst.Settings(this.coinCount, this.scatterRadius, this.scatterDuration, this.gatherDuration,
                                    this.coinInterval, this.popDuration, this.angleStart, this.angleSpan,
                                    _arcHeight: this.arcHeight);

    GameObject CreateCoin()
    {
        var t_go = new GameObject("Coin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ((RectTransform)t_go.transform).sizeDelta = new Vector2(this.coinSize, this.coinSize);

        var t_img = t_go.GetComponent<Image>();
        t_img.sprite = this.coinSprite;
        t_img.raycastTarget = false;   // 코인이 팝업 터치(스킵/이동)를 가로채지 않게.
        t_img.preserveAspect = true;

        m_coins.Add(t_go);
        return t_go;
    }

    void ClearCoins()
    {
        for (int t_i = 0; t_i < m_coins.Count; t_i++)
        {
            if (m_coins[t_i] == null) continue;
            m_coins[t_i].transform.DOKill();
            Destroy(m_coins[t_i]);
        }
        m_coins.Clear();
    }
}
