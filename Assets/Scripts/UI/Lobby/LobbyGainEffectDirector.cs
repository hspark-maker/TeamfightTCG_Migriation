using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// 로비 진입 시 "직전 씬에서 무엇을 얻었는지"를 한 번 보여주는 연출 브레인.
// 전투(BattleRewardHandoff)와 카드팩(CardPackRewardHandoff) 캐리어를 소비해
//   골드 → 재화 텍스트로 코인이 빨려들며 숫자가 오르고 튄다(GoldGainEffectPlayer에 위임 — 도감 수확과 같은 손맛)
//   카드 → 도감 탭으로 카드가 빨려들며 탭이 튄다
// 두 단계를 동시에 재생한다(획득 하나를 두 번에 걸쳐 알리지 않는다).
// 카드는 신규만 온다 — 중복분은 환급 골드로 코인 쪽에 이미 섞여 있다(PackAcquireController가 걸러 싣는다).
//
// 경계: 지급·저장은 각 씬이 이미 끝냈다. 이 클래스는 표시만 하고 재화를 건드리지 않는다.
// 배선을 비워두면 이름으로 자동 탐색한다 — 로비 프리팹 수정 없이도 동작하게(자동 탐색 실패 시 그 단계만 건너뛴다).
public class LobbyGainEffectDirector : MonoBehaviour
{
    [Header("배선 (비우면 자동 탐색)")]
    [Tooltip("카드가 빨려들 도감 탭 버튼. 비우면 collectionTabName으로 찾는다.")]
    [SerializeField] RectTransform collectionTabTarget;
    [Tooltip("도감 탭 버튼 오브젝트 이름(자동 탐색용).")]
    [SerializeField] string collectionTabName = "Button_Collection";
    [Tooltip("도감 탭이 선택돼 원 버튼이 꺼져 있을 때 대신 쓸 오브젝트 이름.")]
    [SerializeField] string tabFocusName = "Button_Focus";

    [Header("연출 값")]
    [SerializeField] float tabPunch = 0.3f;

    // 런타임에 만든 하위 연출기(직렬화 배선이 있으면 그것을 쓴다).
    CardGainFlightEffect m_cardFlight;

    void Start()
    {
        StartCoroutine(PlayWhenReady());
    }

    // 레이아웃 그룹이 x좌표를 정하고 LobbyTabController.Start가 탭을 고르기 전에는 목적지 좌표가 확정되지 않는다.
    // 한 프레임 양보 + 캔버스 강제 갱신 후에 위치를 읽는다(RankRewardPanel과 같은 이유).
    IEnumerator PlayWhenReady()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        long t_gold = 0L;
        if (BattleRewardHandoff.TryConsume(out long t_battleGold)) t_gold += t_battleGold;

        IReadOnlyList<CardData> t_cards = null;
        if (CardPackRewardHandoff.TryConsume(out long t_refundGold, out var t_packCards))
        {
            t_gold += t_refundGold;      // 중복 카드 환급도 골드 획득이다 — 전투 보상과 합쳐 한 번에 보여준다.
            t_cards = t_packCards;
        }

        int t_cardCount = t_cards != null ? t_cards.Count : 0;
        if (t_gold <= 0L && t_cardCount <= 0) yield break;

        // 연출 레이어는 캔버스 좌표계 위여야 한다(anchoredPosition으로 날린다).
        if (transform is not RectTransform)
        {
            Debug.LogWarning("[LobbyGainEffectDirector] RectTransform이 아닌 오브젝트에 붙어 있어 연출을 건너뛴다.");
            yield break;
        }

        // 하단 탭 바·상단 바보다 위에 그려져야 카드가 가려지지 않는다.
        transform.SetAsLastSibling();

        var t_master = DOTween.Sequence().SetLink(gameObject);

        bool t_goldStaged = t_gold > 0L && TryStageGold(t_master, t_gold);
        bool t_cardStaged = t_cardCount > 0 && TryStageCards(t_master, t_cards);

        // 붙일 단계가 없으면(배선 탐색 실패) 빈 시퀀스를 남기지 않는다.
        if (!t_goldStaged && !t_cardStaged) t_master.Kill();
    }

    // 골드는 공용 재생기가 조립한다(수치 고정 해제 안전망까지 그 시퀀스에 붙어 온다).
    // 수치 자리에서 튀어 제자리로 돌아오는 모드라 출발점을 주지 않는다.
    bool TryStageGold(Sequence _master, long _gold)
    {
        if (!GoldGainEffectPlayer.TryGet(this, out var t_player)) return false;

        var t_seq = t_player.BuildGoldGain(null, _gold);
        if (t_seq == null) return false;

        // 카드 단계와 같은 0초에 꽂아 동시에 돌린다.
        _master.Insert(0f, t_seq);
        return true;
    }

    bool TryStageCards(Sequence _master, IReadOnlyList<CardData> _cards)
    {
        if (this.collectionTabTarget == null) this.collectionTabTarget = FindTabTarget();
        if (this.collectionTabTarget == null)
        {
            Debug.LogWarning($"[LobbyGainEffectDirector] 도감 탭('{this.collectionTabName}')을 찾지 못해 카드 연출을 건너뛴다.");
            return false;
        }

        var t_flight = EnsureCardFlight();
        t_flight.Configure(this.collectionTabTarget, this.collectionTabTarget);

        _master.Insert(0f, t_flight.BuildFlight(_cards, (_arrived, _total) => OnCardArrived()));
        return true;
    }

    void OnCardArrived()
    {
        UiPunch.Play(PunchTargetOf(this.collectionTabTarget), this.tabPunch);
    }

    CardGainFlightEffect EnsureCardFlight()
    {
        if (m_cardFlight == null) m_cardFlight = GetComponent<CardGainFlightEffect>();
        if (m_cardFlight == null) m_cardFlight = gameObject.AddComponent<CardGainFlightEffect>();
        return m_cardFlight;
    }

    // 탭 버튼은 레이아웃 그룹이 배치하므로 버튼 자체를 튀기면 형제 배치가 흔들려 보인다 — 아이콘 자식이 있으면 그쪽을 튀긴다.
    static Transform PunchTargetOf(RectTransform _tab)
    {
        if (_tab == null) return null;
        return _tab.childCount > 0 ? _tab.GetChild(0) : _tab;
    }

    // 도감 탭 RectTransform 탐색. 선택된 탭은 버튼이 꺼지고 Focus가 그 자리를 대신하므로 그때는 Focus를 쓴다.
    RectTransform FindTabTarget()
    {
        var t_root = GetComponentInParent<Canvas>();
        if (t_root == null) return null;

        var t_tab = FindByName(t_root.transform, this.collectionTabName);
        if (t_tab != null && t_tab.gameObject.activeInHierarchy) return t_tab;

        var t_focus = FindByName(t_root.transform, this.tabFocusName);
        return t_focus != null && t_focus.gameObject.activeInHierarchy ? t_focus : t_tab;
    }

    static RectTransform FindByName(Transform _root, string _name)
    {
        if (string.IsNullOrEmpty(_name)) return null;

        var t_all = _root.GetComponentsInChildren<RectTransform>(true);
        for (int t_i = 0; t_i < t_all.Length; t_i++)
            if (t_all[t_i].name == _name) return t_all[t_i];

        return null;
    }
}
