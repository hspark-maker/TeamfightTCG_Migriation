using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 로비 진입 시 "직전 씬에서 무엇을 얻었는지"를 한 번 보여주는 연출 브레인.
// 전투(BattleRewardHandoff)와 카드팩(CardPackRewardHandoff) 캐리어를 소비해
//   골드 → 재화 텍스트로 코인이 빨려들며 숫자가 오르고 튄다
//   카드 → 도감 탭으로 카드가 빨려들며 탭이 튄다
// 두 단계를 동시에 재생한다(획득 하나를 두 번에 걸쳐 알리지 않는다).
// 카드는 신규만 온다 — 중복분은 환급 골드로 코인 쪽에 이미 섞여 있다(PackAcquireController가 걸러 싣는다).
//
// 경계: 지급·저장은 각 씬이 이미 끝냈다. 이 클래스는 표시만 하고 재화를 건드리지 않는다.
// 배선을 비워두면 이름으로 자동 탐색한다 — 로비 프리팹 수정 없이도 동작하게(자동 탐색 실패 시 그 단계만 건너뛴다).
public class LobbyGainEffectDirector : MonoBehaviour
{
    [Header("배선 (비우면 자동 탐색)")]
    [Tooltip("골드 수치 HUD. 코인의 도착지이자 숫자가 오르는 대상.")]
    [SerializeField] GoldHud goldHud;
    [Tooltip("코인 스프라이트. 비우면 골드 수치 옆 아이콘 Image의 스프라이트를 그대로 쓴다.")]
    [SerializeField] Sprite coinSprite;
    [Tooltip("카드가 빨려들 도감 탭 버튼. 비우면 collectionTabName으로 찾는다.")]
    [SerializeField] RectTransform collectionTabTarget;
    [Tooltip("도감 탭 버튼 오브젝트 이름(자동 탐색용).")]
    [SerializeField] string collectionTabName = "Button_Collection";
    [Tooltip("도감 탭이 선택돼 원 버튼이 꺼져 있을 때 대신 쓸 오브젝트 이름.")]
    [SerializeField] string tabFocusName = "Button_Focus";

    [Header("연출 값")]
    [Tooltip("코인 장수 범위. 획득 골드량을 이 사이로 클램프해 장수를 정한다.")]
    [SerializeField] int coinCountMin = 4;
    [SerializeField] int coinCountMax = 12;
    [Tooltip("코인이 흩어지는 부채꼴(도). 기본값은 수치 아래쪽으로 퍼뜨려 화면 밖으로 나가지 않게.")]
    [SerializeField] float coinAngleStart = 195f;
    [SerializeField] float coinAngleSpan = 150f;
    [SerializeField] float goldPunch = 0.35f;
    [SerializeField] float tabPunch = 0.3f;

    // 런타임에 만든 하위 연출기(직렬화 배선이 있으면 그것을 쓴다).
    CoinBurstEffect m_coinBurst;
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
        if (!t_goldStaged && !t_cardStaged)
        {
            t_master.Kill();
            yield break;
        }

        // 연출이 어떤 이유로 끊겨도 수치 고정은 반드시 풀린다(중간 도착 통지가 빠지는 경우의 안전망).
        if (t_goldStaged) t_master.OnKill(() => { if (this.goldHud != null) this.goldHud.ReleaseDisplay(); });
    }

    bool TryStageGold(Sequence _master, long _gold)
    {
        if (this.goldHud == null) this.goldHud = FindFirstObjectByType<GoldHud>(FindObjectsInactive.Include);

        var t_textRect = this.goldHud != null ? this.goldHud.TextRect : null;
        if (t_textRect == null)
        {
            Debug.LogWarning("[LobbyGainEffectDirector] GoldHud를 찾지 못해 골드 연출을 건너뛴다.");
            return false;
        }

        if (this.coinSprite == null) this.coinSprite = FindIconSpriteNear(t_textRect);
        if (this.coinSprite == null)
        {
            Debug.LogWarning("[LobbyGainEffectDirector] 코인 스프라이트를 찾지 못해 골드 연출을 건너뛴다.");
            return false;
        }

        // 잔액은 이미 최종값이다 — 획득분만큼 되돌려 놓고 코인 도착에 맞춰 다시 올린다.
        long t_start = CurrencyManager.Gold - _gold;
        int  t_count = (int)Mathf.Clamp(_gold, this.coinCountMin, this.coinCountMax);

        var t_burst = EnsureCoinBurst();
        // 수치 근처에서 생겨나 수치로 되돌아온다 — 원점과 목적지가 같고 흩어짐만 부채꼴로 준다.
        t_burst.Configure(this.coinSprite, t_textRect, t_textRect, t_count, this.coinAngleStart, this.coinAngleSpan);

        // 되돌리기는 지금 바로 — 시퀀스는 이 프레임에 재생을 시작하므로 콜백으로 미룰 이유가 없다.
        this.goldHud.HoldDisplay(t_start);

        // 카드 단계와 같은 0초에 꽂아 동시에 돌린다.
        _master.Insert(0f, t_burst.BuildBurst((_arrived, _total) => OnCoinArrived(t_start, _gold, _arrived, _total)));
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

    // 코인 한 장이 닿을 때마다 숫자를 그만큼 올리고 텍스트를 튀긴다. 마지막 장에서 실제 잔액으로 확정.
    void OnCoinArrived(long _start, long _gold, int _arrived, int _total)
    {
        if (this.goldHud == null) return;

        if (_arrived >= _total) this.goldHud.ReleaseDisplay();
        else this.goldHud.HoldDisplay(_start + (long)(_gold * (_arrived / (float)_total)));

        UiPunch.Play(this.goldHud.TextRect, this.goldPunch);
    }

    void OnCardArrived()
    {
        UiPunch.Play(PunchTargetOf(this.collectionTabTarget), this.tabPunch);
    }

    CoinBurstEffect EnsureCoinBurst()
    {
        if (m_coinBurst == null) m_coinBurst = GetComponent<CoinBurstEffect>();
        if (m_coinBurst == null) m_coinBurst = gameObject.AddComponent<CoinBurstEffect>();
        return m_coinBurst;
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

    // 골드 수치와 같은 묶음에 놓인 아이콘 Image에서 코인 스프라이트를 빌린다(별도 에셋 배선 없이).
    static Sprite FindIconSpriteNear(RectTransform _textRect)
    {
        var t_group = _textRect.parent;
        if (t_group == null) return null;

        var t_images = t_group.GetComponentsInChildren<Image>(true);
        Sprite t_any = null;
        for (int t_i = 0; t_i < t_images.Length; t_i++)
        {
            var t_sprite = t_images[t_i].sprite;
            if (t_sprite == null) continue;
            if (t_images[t_i].name.Contains("Icon")) return t_sprite;
            t_any ??= t_sprite;
        }

        return t_any;
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
