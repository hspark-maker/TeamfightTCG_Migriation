using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>"누가 어떤 감정표현을 냈다"를 받아 **화면 어디에 띄울지**만 정하는 단일 창구.
///
/// 여기 하나로 모으는 이유: 낼 수 있는 쪽이 앞으로 셋이다 —
/// 플레이어(선택 표) · AI(자동 반응) · (나중에) 상대 클라. 각자 스티커 뷰를 직접 잡게 두면
/// "AI만 다른 자리에 뜬다" 같은 어긋남이 생기고, 나중에 멀티를 붙일 때 발화 지점을 다시 찾아야 한다.
///
/// 지금은 싱글 전용이다 — 여기서 네트워크로 아무것도 보내지 않는다.
/// 멀티를 붙일 때도 규칙은 그대로다: 감정표현은 게임상태·RNG를 건드리지 않으므로 결정론과 무관하고,
/// 와이어에 실을 때는 "누가·몇 번"만 보내면 된다(스티커 수명·자리는 받는 쪽이 정한다).</summary>
public class EmoteDirector : MonoBehaviour
{
    public static EmoteDirector Instance { get; private set; }

    [SerializeField] EmoteCatalog catalog;

    [Tooltip("내 감정표현이 뜨는 자리.")]
    [SerializeField] EmoteStickerView playerSticker;

    [Tooltip("상대(AI) 감정표현이 뜨는 자리.")]
    [SerializeField] EmoteStickerView enemySticker;

    public EmoteCatalog Catalog => this.catalog;

    // 예약된 AI 되받기. 연달아 내면 마지막 것만 남는다 — 쌓아 두면 손을 뗀 뒤에도
    // 상대가 몇 초 동안 혼자 감정표현을 쏟아낸다.
    CancellationTokenSource m_replyCts;

    void Awake()
    {
        // 씬에 하나만 둔다. 둘이 살아 있으면 나중에 깬 쪽이 창구를 가로채 스티커가 엉뚱한 자리에 뜬다.
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        CancelReply();
        if (Instance == this) Instance = null;
    }

    /// <summary>내가 낸 감정표현. 싱글(AI 대전)이면 잠시 뒤 AI가 하나 되받는다.</summary>
    public void PlayLocal(int _index)
    {
        Play(_index, _isEnemy: false);
        ScheduleAiReply(_index);
    }

    /// <summary>상대(AI)가 낸 감정표현.</summary>
    public void PlayEnemy(int _index) => Play(_index, _isEnemy: true);

    /// <summary>배선·목록이 비면 조용히 무동작 — 감정표현은 어디까지나 곁들이라 없다고 전투가 멈추면 안 된다.</summary>
    public void Play(int _index, bool _isEnemy)
    {
        if (this.catalog == null) return;

        EmoteEntry t_entry = this.catalog.Get(_index);
        if (t_entry == null) return;

        EmoteStickerView t_view = _isEnemy ? this.enemySticker : this.playerSticker;
        if (t_view == null) return;

        t_view.Play(t_entry, this.catalog);
    }

    /// <summary>양쪽 스티커를 즉시 거둔다(전투 종료·결과 연출 진입). 예약된 AI 반응도 함께 취소한다.</summary>
    public void HideAll()
    {
        CancelReply();
        this.playerSticker?.Hide();
        this.enemySticker?.Hide();
    }

    /// <summary>AI가 되받을 감정표현을 예약한다.
    ///
    /// **멀티에서는 아무것도 하지 않는다** — 상대는 사람이고, 그 사람의 감정표현은 와이어로 와야 한다.
    /// 여기서 지어내면 두 클라가 서로 다른 것을 보게 된다(연출이라 게임 결과와는 무관하지만 거짓 정보다).
    ///
    /// 무엇을 낼지는 <see cref="UnityEngine.Random"/>으로 뽑는다. 결정론 스트림(MatchRandom)을 쓰면 안 된다 —
    /// 감정표현은 게임상태가 아니라서 양 클라가 같은 횟수로 뽑는다는 보장이 없고,
    /// 스트림을 한 번이라도 어긋나게 소비하면 그 뒤의 모든 전투 난수가 갈라진다.</summary>
    void ScheduleAiReply(int _playerIndex)
    {
        CancelReply();

        if (this.catalog == null || !this.catalog.aiReply) return;
        if (this.enemySticker == null) return;
        if (IsMultiplayer()) return;

        this.m_replyCts = new CancellationTokenSource();
        ReplyAfterDelay(_playerIndex, this.m_replyCts.Token).Forget();
    }

    async UniTaskVoid ReplyAfterDelay(int _playerIndex, CancellationToken _ct)
    {
        float t_delay = this.catalog.aiReplyDelay;
        if (t_delay > 0f)
        {
            bool t_canceled = await UniTask.Delay((int)(t_delay * 1000), ignoreTimeScale: true,
                                                  cancellationToken: _ct)
                                           .SuppressCancellationThrow();
            if (t_canceled) return;
        }

        Play(PickReply(_playerIndex), _isEnemy: true);
    }

    /// <summary>되받을 감정표현 하나. 목록이 둘 이상이면 내가 낸 것과 겹치지 않게 고른다 —
    /// 같은 것이 되돌아오면 "반응"이 아니라 "따라 한 것"으로 읽힌다.</summary>
    int PickReply(int _playerIndex)
    {
        int t_count = this.catalog.Count;
        if (t_count <= 1) return 0;

        int t_pick = Random.Range(0, t_count - 1);
        return t_pick >= _playerIndex ? t_pick + 1 : t_pick;
    }

    void CancelReply()
    {
        this.m_replyCts?.Cancel();
        this.m_replyCts?.Dispose();
        this.m_replyCts = null;
    }

    /// <summary>러너가 살아 있으면 멀티다(NetworkSession이 없는 씬에서도 안전하게 false).</summary>
    static bool IsMultiplayer()
        => NetworkSession.Instance != null
        && NetworkSession.Instance.Runner != null
        && NetworkSession.Instance.Runner.IsRunning;
}
