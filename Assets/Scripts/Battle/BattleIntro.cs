using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class BattleIntro : MonoBehaviour
{
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;

    [Header("Camera Intro")]
    // 인트로 줌은 **fit이 계산한 기준 거리 기준의 상대값**이다. 절대 z로 두면 화면 비율에 따라
    // 카메라 거리가 달라졌을 때(BattleCameraFit) 인트로가 끝나는 위치가 기준과 어긋나 카드가 잘린다.
    [SerializeField] float introBackDistance = 9f;    // 시작 시 기준보다 얼마나 더 뒤에서 출발하는가(구 -20 → -11)
    // fit이 없는 씬(테스트 등) 폴백. fit이 있으면 무시된다.
    [SerializeField] float fallbackTargetZ = -11f;

    // 타이밍은 BattleTimingConfig 단일 진실원(배율 적용). 아래 프로퍼티로 위임.
    float cardDealDelay    => GameTiming.Battle.CardDealDelay;
    float cardDealDuration => GameTiming.Battle.CardDealDuration;
    float cameraDuration   => GameTiming.Battle.CameraIntroDuration;

    [Header("Deck Fill Intro")]
    [SerializeField] GameObject playerFaceDownPrefab;
    [SerializeField] GameObject enemyFaceDownPrefab;
    [SerializeField] RectTransform deckIntroParent;
    [SerializeField] RectTransform playerDeckTransform;
    [SerializeField] RectTransform enemyDeckTransform;
    [Tooltip("덱으로 날려 보낼 뒷면 카드 장수 폴백. 정상 경로는 그 필드의 실제 덱 장수(배치 + 대기)를 쓴다")]
    [SerializeField] int deckCardCount = 3;

    [Tooltip("덱에 쌓이는 뒷면 카드 한 장당 어긋나는 양(px). 0이면 전부 같은 자리에 겹쳐 한 장처럼 보인다")]
    [SerializeField] Vector2 deckStackStep = new Vector2(0f, 5f);

    // 덱으로 날아가 **쌓여 있는** 뒷면 카드들. 착지 즉시 지우면 쌓이는 그림이 안 남으므로
    // 더미(DeckPileUI)가 그 자리에 뜬 다음에 한꺼번에 걷는다 — 더미가 이 무더기를 이어받는 모양.
    readonly List<GameObject> m_introFaceDowns = new List<GameObject>();

    float deckDealDelay    => GameTiming.Battle.DeckDealDelay;
    float deckDealDuration => GameTiming.Battle.DeckDealDuration;


    Vector3[] m_playerDests;
    Vector3[] m_enemyDests;

    /// <summary>인트로가 끝나며 카메라가 돌아갈 기준 z. fit이 있으면 화면 비율에 맞춰 계산된 값.</summary>
    float TargetZ
    {
        get
        {
            BattleCameraFit t_fit = Camera.main != null ? Camera.main.GetComponent<BattleCameraFit>() : null;
            return t_fit != null ? t_fit.BaseCameraZ : this.fallbackTargetZ;
        }
    }

    public void Await()
    {
        if (Camera.main == null) return;

        // 인트로가 카메라를 몰기 시작 — fit이 매 프레임 z를 되돌리지 않게 잠근다(PlayCameraIntro 끝에서 해제).
        BattleCameraFit.BeginExternalControl();

        Camera.main.transform.position = new Vector3(0f, 0f, TargetZ - this.introBackDistance);
        Vector3 t_playerFrom = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width * 2f, 0f, 10f));
        Vector3 t_enemyFrom = Camera.main.ScreenToWorldPoint(new Vector3(-Screen.width, Screen.height, 10f));

        this.m_playerDests = CacheAndHide(this.playerFieldView, t_playerFrom);
        this.m_enemyDests = CacheAndHide(this.enemyFieldView, t_enemyFrom);

        // 전투는 "덱이 없는" 화면에서 열린다 — 더미는 나눠주는 연출이 끝난 자리에서 생긴다(Play 참조).
        DeckPileUI.HideAllForIntro();
    }

    /// <summary>카메라 확대 인트로(줌 인). 코인 토스 전에 먼저 실행. 완료까지 대기.
    /// 도착점은 fit의 기준 z — 인트로가 끝나면 보드 전체가 딱 들어오는 거리에 선다.</summary>
    public async UniTask PlayCameraIntro()
    {
        if (Camera.main == null) return;

        float t_target = TargetZ;   // 잠금 해제 전에 읽는다(해제 후엔 fit이 곧바로 덮을 수 있음)
        Vector3 t_pos = Camera.main.transform.position;
        t_pos.z = t_target - this.introBackDistance;
        Camera.main.transform.position = t_pos;

        try
        {
            await Camera.main.transform.DOMoveZ(t_target, this.cameraDuration).ToUniTask();
        }
        finally
        {
            // 이후엔 fit이 다시 카메라 z를 관리(해상도 전환·회전 대응).
            BattleCameraFit.EndExternalControl();
        }
    }

    /// <summary>인트로 배치. 순서는 "덱을 만든다 → 그 덱에서 꺼내 놓는다"다 —
    /// (1) 덱 없는 화면에 뒷면 카드가 날아와 덱 자리에 쌓이고, (2) 그 자리에 덱 더미가 생기고,
    /// (3) 슬롯 카드가 **덱에서** 한 장씩 나온다. 런타임 보충(BattleFieldView.PlayFillAnim)과 출처가 같아진다.</summary>
    public async UniTask Play()
    {
        Vector3 t_playerOffscreen = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width * 2f, 0f, 10f));
        Vector3 t_enemyOffscreen  = Camera.main.ScreenToWorldPoint(new Vector3(-Screen.width, Screen.height, 10f));

        if (this.playerFaceDownPrefab != null || this.enemyFaceDownPrefab != null)
            await PlayDeckFillIntro();

        RevealPile(this.playerFieldView, CountBoundCards(this.playerFieldView));
        RevealPile(this.enemyFieldView, CountBoundCards(this.enemyFieldView));
        ClearIntroFaceDowns();   // 더미가 자리를 넘겨받은 뒤에 무더기를 걷는다

        await DealCards(this.playerFieldView,
                        DeckSpawnPoint(this.playerFieldView, t_playerOffscreen, this.m_playerDests),
                        this.m_playerDests);
        await DealCards(this.enemyFieldView,
                        DeckSpawnPoint(this.enemyFieldView, t_enemyOffscreen, this.m_enemyDests),
                        this.m_enemyDests);
    }

    /// <summary>이 필드 카드가 나오는 자리 = 그 소유자의 덱 버튼. 덱 UI가 없는 씬(테스트·미배선)은
    /// 종전대로 화면 밖에서 날아온다(CunningVfx.DeckExitPoint·BattleFieldView.PlayFillAnim과 같은 규약).</summary>
    Vector3 DeckSpawnPoint(BattleFieldView _fieldView, Vector3 _fallback, Vector3[] _dests)
    {
        DeckPileUI t_pile = PileOf(_fieldView);
        if (t_pile == null) return _fallback;

        float t_z = _dests != null && _dests.Length > 0 ? _dests[0].z : _fallback.z;
        return CameraUtil.ScreenPointToWorld(t_pile.AnchorScreenPoint, t_z);
    }

    void RevealPile(BattleFieldView _fieldView, int _openingCardCount)
        => PileOf(_fieldView)?.RevealFromIntro(_openingCardCount);

    static int CountBoundCards(BattleFieldView _fieldView)
    {
        if (_fieldView == null) return 0;

        int t_count = 0;
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            if (_fieldView.GetSlotView(i).BoundCard != null) t_count++;
        }
        return t_count;
    }

    static DeckPileUI PileOf(BattleFieldView _fieldView)
        => _fieldView != null && _fieldView.Field != null
            ? DeckPileUI.For(_fieldView.Field.OwnerIndex)
            : null;

    async UniTask PlayDeckFillIntro()
    {
        await UniTask.WhenAll(
            DealDeckSide(this.playerFaceDownPrefab, this.playerDeckTransform, _fromLeft: true,
                         _count: DeckFillCount(this.playerFieldView)),
            DealDeckSide(this.enemyFaceDownPrefab, this.enemyDeckTransform, _fromLeft: false,
                         _count: DeckFillCount(this.enemyFieldView)));
    }

    /// <summary>덱으로 들어가는 뒷면 카드 장수 = 그 필드가 실제로 들고 있는 전부(대기 + 이미 슬롯에 배치된 오프닝 카드).
    /// 덱은 이 시점에 이미 나뉘어 있으므로(Initialize가 3장을 슬롯에 꽂았다) 둘을 더해야 원래 덱 장수(6)가 된다.
    /// 그래야 "6장이 들어가고 3장이 나온다"가 화면 숫자(DeckPileUI)와 어긋나지 않는다.</summary>
    int DeckFillCount(BattleFieldView _fieldView)
    {
        if (_fieldView == null || _fieldView.Field == null) return this.deckCardCount;

        return CountBoundCards(_fieldView) + _fieldView.Field.WaitingCount;
    }

    async UniTask DealDeckSide(GameObject _prefab, RectTransform _dest, bool _fromLeft, int _count)
    {
        if (_prefab == null || _dest == null || this.deckIntroParent == null) return;
        if (_count <= 0) return;

        Rect t_rect = this.deckIntroParent.rect;
        Vector2 t_from = _fromLeft
            ? new Vector2(-t_rect.width, 0f)
            : new Vector2(t_rect.width, 0f);

        Canvas t_canvas = this.deckIntroParent.GetComponentInParent<Canvas>();
        Camera t_cam = t_canvas != null ? t_canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            this.deckIntroParent,
            RectTransformUtility.WorldToScreenPoint(t_cam, _dest.position),
            t_cam,
            out Vector2 t_destLocal);

        // 나눠주는 카드 크기 = 실제 덱 카드(더미 이미지) 크기. 프리팹 기본값(200x280)이 덱(300x400)보다
        // 작아 "덱에 안 맞는 작은 카드"로 보였다. 덱 이미지를 리사이즈하면 여기도 따라온다.
        Vector2 t_cardSize = DeckCardSize(_dest);

        for (int i = 0; i < _count; i++)
        {
            // 뒤에 오는 카드일수록 조금씩 어긋나 앉는다 — 같은 점에 겹치면 6장이 한 장으로 보인다.
            FlyOneFaceDown(_prefab, t_from, t_destLocal + this.deckStackStep * i, t_cardSize).Forget();
            if (i < _count - 1)
                await UniTask.Delay((int)(this.deckDealDelay * 1000));
        }
        await UniTask.Delay((int)(this.deckDealDuration * 1000));
    }

    /// <summary>덱 카드 한 장이 목적지 rect와 같은 크기가 되도록 하는 로컬 크기(deckIntroParent 기준).
    /// 크기는 rect로 맞춘다 — localScale로 키우면 자식·테두리까지 같이 늘어난다([[ui-size-not-scale]] 규약).</summary>
    Vector2 DeckCardSize(RectTransform _dest)
    {
        if (_dest == null || this.deckIntroParent == null) return Vector2.zero;

        Vector2 t_size       = _dest.rect.size;
        Vector3 t_destScale  = _dest.lossyScale;
        Vector3 t_parentScale = this.deckIntroParent.lossyScale;

        if (!Mathf.Approximately(t_parentScale.x, 0f)) t_size.x *= t_destScale.x / t_parentScale.x;
        if (!Mathf.Approximately(t_parentScale.y, 0f)) t_size.y *= t_destScale.y / t_parentScale.y;
        return t_size;
    }

    async UniTask FlyOneFaceDown(GameObject _prefab, Vector2 _from, Vector2 _dest, Vector2 _size)
    {
        GameObject t_obj = Instantiate(_prefab, this.deckIntroParent);
        this.m_introFaceDowns.Add(t_obj);

        RectTransform t_rt = t_obj.GetComponent<RectTransform>();
        FitToDeckCard(t_rt, _size);
        t_rt.anchoredPosition = _from;
        // 착지해도 지우지 않는다 — 덱이 쌓이는 그림이 남아야 한다(정리는 ClearIntroFaceDowns).
        await t_rt.DOAnchorPos(_dest, this.deckDealDuration).SetEase(Ease.OutCubic).ToUniTask();
    }

    /// <summary>뒷면 카드를 덱 카드 크기에 맞춘다. **비율은 프리팹 것을 지킨다** — 목적지 rect에 그대로 늘리면
    /// 카드 그림이 찌그러진다(프리팹 5:7, 덱 3:4). 둘 중 작은 배율로 맞춰 덱 칸 안에 들어가게 한다.</summary>
    static void FitToDeckCard(RectTransform _rect, Vector2 _size)
    {
        if (_rect == null || _size.x <= 0f || _size.y <= 0f) return;

        Vector2 t_src = _rect.sizeDelta;
        if (t_src.x <= 0f || t_src.y <= 0f) return;

        _rect.sizeDelta = t_src * Mathf.Min(_size.x / t_src.x, _size.y / t_src.y);
    }

    void ClearIntroFaceDowns()
    {
        for (int i = 0; i < this.m_introFaceDowns.Count; i++)
        {
            GameObject t_obj = this.m_introFaceDowns[i];
            if (t_obj == null) continue;

            t_obj.transform.DOKill();   // 아직 날고 있는 트윈이 파괴된 대상을 만지지 않게
            Destroy(t_obj);
        }
        this.m_introFaceDowns.Clear();
    }

    Vector3[] CacheAndHide(BattleFieldView _fieldView, Vector3 _from)
    {
        Vector3[] t_dests = new Vector3[BattleField.SLOT_COUNT];
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardView t_view = _fieldView.GetSlotView(i);
            t_dests[i] = t_view.transform.position;
            Vector3 t_hide = _from;
            t_hide.z = t_dests[i].z;
            t_view.transform.position = t_hide;
        }
        return t_dests;
    }

    async UniTask DealCards(BattleFieldView _fieldView, Vector3 _from, Vector3[] _dests)
    {
        DeckPileUI t_pile = PileOf(_fieldView);
        Vector3 t_fieldCenter = Vector3.zero;
        int t_cardCount = 0;

        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            if (_fieldView.GetSlotView(i).BoundCard == null) continue;
            t_fieldCenter += _dests[i];
            t_cardCount++;
        }

        if (t_cardCount > 0) t_fieldCenter /= t_cardCount;

        Vector3 t_screenCenter = Camera.main.ScreenToWorldPoint(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 10f));
        t_screenCenter.z = t_fieldCenter.z;

        // 순차 배치: 한 카드 딜 애니가 끝난 뒤 다음 카드 시작(사이에 cardDealDelay 간격).
        bool t_first = true;
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardView t_view = _fieldView.GetSlotView(i);
            if (t_view.BoundCard == null) continue;
            if (!t_first) await UniTask.Delay((int)(this.cardDealDelay * 1000));
            t_first = false;

            // 대기 중에 씬 전환 정리가 보드를 걷어갔을 수 있다 — 남은 카드까지 배치하려 들면 파괴된 뷰를 만진다.
            if (t_view == null) return;

            // 고등급 등장 컷씬: 이 카드가 필드에 나오기 직전. 오프닝 배치도 "필드에 나오는" 순간이라 포함한다
            // (런타임 등장은 BattleFieldView.PlayFillAnim). 자격 판정은 CardCinematicRules 단독 —
            // 일반 카드는 Resolve가 null이라 즉시 통과하고, 같은 인스턴스는 래치로 두 번 재생되지 않는다.
            await CardCinematicPlayer.Play(CardCinematicRules.Resolve(t_view.BoundCard));

            t_pile?.PlayIntroDraw();
            await t_view.PlayDealAnim(_from, t_screenCenter, _dests[i], this.cardDealDuration);
        }
    }
}
