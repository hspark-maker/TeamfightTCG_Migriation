using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class BattleIntro : MonoBehaviour
{
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;
    [SerializeField] float cardDealDelay = 0.15f;
    [SerializeField] float cardDealDuration = 0.6f;

    [Header("Camera Intro")]
    [SerializeField] float cameraStartZ = -20f;
    [SerializeField] float cameraTargetZ = -11f;
    [SerializeField] float cameraDuration = 0.8f;

    [Header("Deck Fill Intro")]
    [SerializeField] GameObject playerFaceDownPrefab;
    [SerializeField] GameObject enemyFaceDownPrefab;
    [SerializeField] RectTransform deckIntroParent;
    [SerializeField] RectTransform playerDeckTransform;
    [SerializeField] RectTransform enemyDeckTransform;
    [SerializeField] int deckCardCount = 3;
    [SerializeField] float deckDealDelay = 0.12f;
    [SerializeField] float deckDealDuration = 0.35f;


    Vector3[] m_playerDests;
    Vector3[] m_enemyDests;

    public void Await()
    {
        Camera.main.transform.position = new Vector3(0, 0, -20f);
        Vector3 t_playerFrom = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width * 2f, 0f, 10f));
        Vector3 t_enemyFrom = Camera.main.ScreenToWorldPoint(new Vector3(-Screen.width, Screen.height, 10f));

        this.m_playerDests = CacheAndHide(this.playerFieldView, t_playerFrom);
        this.m_enemyDests = CacheAndHide(this.enemyFieldView, t_enemyFrom);
    }

    public async UniTask Play()
    {
        if (Camera.main != null)
        {
            Vector3 t_pos = Camera.main.transform.position;
            t_pos.z = this.cameraStartZ;
            Camera.main.transform.position = t_pos;
            Camera.main.transform.DOMoveZ(this.cameraTargetZ, this.cameraDuration);
        }

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

        var t_tasks = new UniTask[BattleField.SLOT_COUNT];
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardView t_view = _fieldView.GetSlotView(i);
            t_tasks[i] = t_view.BoundCard != null
                ? DealWithDelay(t_view, _from, t_screenCenter, _dests[i], i)
                : UniTask.CompletedTask;
        }

        await UniTask.WhenAll(t_tasks);
    }

    async UniTask DealWithDelay(CardView _view, Vector3 _from, Vector3 _mid, Vector3 _dest, int _index)
    {
        if (_index > 0)
            await UniTask.Delay((int)(this.cardDealDelay * _index * 1000));
        await _view.PlayDealAnim(_from, _mid, _dest, this.cardDealDuration);
    }
}
