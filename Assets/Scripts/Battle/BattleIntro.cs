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
    [SerializeField] int deckCardCount = 3;

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

    public async UniTask Play()
    {
        Vector3 t_playerFrom = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width * 2f, 0f, 10f));
        Vector3 t_enemyFrom = Camera.main.ScreenToWorldPoint(new Vector3(-Screen.width, Screen.height, 10f));

        await DealCards(this.playerFieldView, t_playerFrom, this.m_playerDests);
        await DealCards(this.enemyFieldView, t_enemyFrom, this.m_enemyDests);

        if (this.playerFaceDownPrefab != null || this.enemyFaceDownPrefab != null)
            await PlayDeckFillIntro();
    }

    async UniTask PlayDeckFillIntro()
    {
        await UniTask.WhenAll(
            DealDeckSide(this.playerFaceDownPrefab, this.playerDeckTransform, _fromLeft: true),
            DealDeckSide(this.enemyFaceDownPrefab, this.enemyDeckTransform, _fromLeft: false));
    }

    async UniTask DealDeckSide(GameObject _prefab, RectTransform _dest, bool _fromLeft)
    {
        if (_prefab == null || _dest == null || this.deckIntroParent == null) return;

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

        for (int i = 0; i < this.deckCardCount; i++)
        {
            FlyOneFaceDown(_prefab, t_from, t_destLocal).Forget();
            if (i < this.deckCardCount - 1)
                await UniTask.Delay((int)(this.deckDealDelay * 1000));
        }
        await UniTask.Delay((int)(this.deckDealDuration * 1000));
    }

    async UniTask FlyOneFaceDown(GameObject _prefab, Vector2 _from, Vector2 _dest)
    {
        GameObject t_obj = Instantiate(_prefab, this.deckIntroParent);
        RectTransform t_rt = t_obj.GetComponent<RectTransform>();
        t_rt.anchoredPosition = _from;
        await t_rt.DOAnchorPos(_dest, this.deckDealDuration).SetEase(Ease.OutCubic).ToUniTask();
        Destroy(t_obj);
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

            // 고등급 등장 컷씬: 이 카드가 필드에 나오기 직전. 오프닝 배치도 "필드에 나오는" 순간이라 포함한다
            // (런타임 등장은 BattleFieldView.PlayFillAnim). 자격 판정은 CardCinematicRules 단독 —
            // 일반 카드는 Resolve가 null이라 즉시 통과하고, 같은 인스턴스는 래치로 두 번 재생되지 않는다.
            await CardCinematicPlayer.Play(CardCinematicRules.Resolve(t_view.BoundCard));

            await t_view.PlayDealAnim(_from, t_screenCenter, _dests[i], this.cardDealDuration);
        }
    }
}
