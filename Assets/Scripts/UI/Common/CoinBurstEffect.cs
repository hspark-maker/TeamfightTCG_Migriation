using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 보상 코인이 중앙에서 흩어졌다가 수치 쪽으로 빨려 들어가는 UI 연출.
// 코인은 재생할 때 만들고 끝나면 지운다 — 한 번만 도는 결과 화면을 위한 최소 구현(풀링 없음).
//
// 시퀀스를 재생하지 않고 만들어서 돌려준다(BuildBurst). 호출자가 자기 연출 시퀀스에 붙여야
// 스킵 한 번으로 코인까지 함께 최종 상태로 끌어당길 수 있다.
// 도착 통지는 InsertCallback으로 시간축에 박는다 — 중첩 트윈의 콜백 동작에 기대지 않는다.
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

    readonly List<GameObject> m_coins = new List<GameObject>();

    /// <summary>연출 전체 길이(초).</summary>
    public float TotalDuration
        => Mathf.Max(0, this.coinCount - 1) * this.coinInterval + this.scatterDuration + this.gatherDuration;

    /// <summary>
    /// 분출→수렴 시퀀스를 만들어 돌려준다(재생은 호출자 시퀀스에 맡긴다).
    /// _onArrived(도착한 장수, 전체 장수)는 코인이 목적지에 닿을 때마다 불린다 — 수치 증가를 여기에 맞물린다.
    /// </summary>
    public Sequence BuildBurst(Action<int, int> _onArrived)
    {
        ClearCoins();

        var t_seq = DOTween.Sequence().SetLink(gameObject);

        // 스프라이트 미배선/장수 0 = 연출 없음. 그래도 수치는 최종값으로 확정해 진행을 막지 않는다.
        if (this.coinSprite == null || this.coinCount <= 0)
        {
            t_seq.AppendCallback(() => _onArrived?.Invoke(1, 1));
            return t_seq;
        }

        var t_self = (RectTransform)transform;
        Vector2 t_from = this.spawnCenter != null ? ToSelfLocal(this.spawnCenter) : Vector2.zero;
        Vector2 t_to   = this.target != null ? ToSelfLocal(this.target) : Vector2.zero;

        for (int i = 0; i < this.coinCount; i++)
        {
            var t_coin = CreateCoin(t_self, t_from);
            var t_rt   = (RectTransform)t_coin.transform;

            // 각도를 균등 분할해 흩뿌린다 — 난수 없이도 고르게 퍼지고 매번 같은 그림이 나온다.
            float t_angle = 360f / this.coinCount * i + 18f;
            float t_reach = this.scatterRadius * (0.7f + 0.15f * (i % 3));
            Vector2 t_dir = new Vector2(Mathf.Cos(t_angle * Mathf.Deg2Rad), Mathf.Sin(t_angle * Mathf.Deg2Rad));
            Vector2 t_mid = t_from + t_dir * t_reach;

            float t_delay = i * this.coinInterval;

            t_seq.Insert(t_delay, t_rt.DOScale(1f, this.popDuration).SetEase(Ease.OutBack));
            t_seq.Insert(t_delay, t_rt.DOAnchorPos(t_mid, this.scatterDuration).SetEase(Ease.OutQuad));
            // 수렴은 InBack — 잠깐 뒤로 물렸다 빨려드는 느낌.
            t_seq.Insert(t_delay + this.scatterDuration,
                         t_rt.DOAnchorPos(t_to, this.gatherDuration).SetEase(Ease.InBack));

            int t_index = i + 1;   // 클로저가 루프 변수를 붙잡지 않게 복사.
            t_seq.InsertCallback(t_delay + this.scatterDuration + this.gatherDuration, () =>
            {
                if (t_coin != null) t_coin.SetActive(false);
                _onArrived?.Invoke(t_index, this.coinCount);
            });
        }

        // 정상 종료든 스킵(Complete)이든 여기서 코인을 걷는다.
        t_seq.AppendCallback(ClearCoins);
        return t_seq;
    }

    void OnDisable()
    {
        // 연출 도중 꺼지면 시퀀스의 마지막 콜백이 오지 않는다 — 남은 코인은 여기서 정리.
        ClearCoins();
    }

    GameObject CreateCoin(RectTransform _parent, Vector2 _at)
    {
        var t_go = new GameObject("Coin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rt = (RectTransform)t_go.transform;
        t_rt.SetParent(_parent, false);
        t_rt.anchorMin = t_rt.anchorMax = new Vector2(0.5f, 0.5f);
        t_rt.pivot     = new Vector2(0.5f, 0.5f);
        t_rt.sizeDelta = new Vector2(this.coinSize, this.coinSize);
        t_rt.anchoredPosition = _at;
        t_rt.localScale = Vector3.zero;

        var t_img = t_go.GetComponent<Image>();
        t_img.sprite = this.coinSprite;
        t_img.raycastTarget = false;   // 코인이 팝업 터치(스킵/이동)를 가로채지 않게.
        t_img.preserveAspect = true;

        m_coins.Add(t_go);
        return t_go;
    }

    // 다른 좌표계의 RectTransform 위치를 이 오브젝트의 로컬(anchoredPosition 기준)로 옮긴다.
    Vector2 ToSelfLocal(RectTransform _rt)
        => transform.InverseTransformPoint(_rt.position);

    void ClearCoins()
    {
        for (int i = 0; i < m_coins.Count; i++)
        {
            if (m_coins[i] == null) continue;
            m_coins[i].transform.DOKill();
            Destroy(m_coins[i]);
        }
        m_coins.Clear();
    }
}
