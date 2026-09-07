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
/// 멀티를 붙일 때 와이어에 실을 값은 **슬롯 번호가 아니라 감정표현 id**다. 장착은 사람마다 다르므로
/// 슬롯 번호를 보내면 받는 쪽에서 엉뚱한 그림이 뜬다(연출이라 게임 결과와는 무관하지만 거짓 정보다).</summary>
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

    /// <summary>내가 선택 표에서 고른 칸. 슬롯 번호를 장착 목록으로 풀어 id로 바꾼 뒤 재생한다 —
    /// 슬롯은 이 함수 밖으로 나가지 않는다.</summary>
    public void PlaySlot(int _slot)
    {
        if (_slot < 0 || _slot >= ProfileManager.EmoteIds.Count) return;

        int t_id = ProfileManager.EmoteIds[_slot];
        PlayId(t_id, _isEnemy: false);
        ScheduleAiReply(t_id);
    }

    /// <summary>배선·목록이 비면 조용히 무동작 — 감정표현은 어디까지나 곁들이라 없다고 전투가 멈추면 안 된다.</summary>
    public void PlayId(int _id, bool _isEnemy)
    {
        if (this.catalog == null || !this.catalog.TryGet(_id, out EmoteEntry t_entry)) return;

        EmoteStickerView t_view = _isEnemy ? this.enemySticker : this.playerSticker;
        if (t_view != null) t_view.Play(t_entry, this.catalog);
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
    /// 여기서 지어내면 두 클라가 서로 다른 것을 보게 된다.
    ///
    /// 무엇을 낼지는 <see cref="UnityEngine.Random"/>으로 뽑는다. 결정론 스트림(MatchRandom)을 쓰면 안 된다 —
    /// 감정표현은 게임상태가 아니라서 양 클라가 같은 횟수로 뽑는다는 보장이 없고,
    /// 스트림을 한 번이라도 어긋나게 소비하면 그 뒤의 모든 전투 난수가 갈라진다.</summary>
    void ScheduleAiReply(int _playerId)
    {
        CancelReply();

        if (this.catalog == null || !this.catalog.aiReply) return;
        if (this.enemySticker == null || IsMultiplayer()) return;

        this.m_replyCts = new CancellationTokenSource();
        ReplyAfterDelay(_playerId, this.m_replyCts.Token).Forget();
    }

    async UniTaskVoid ReplyAfterDelay(int _playerId, CancellationToken _ct)
    {
        float t_delay = this.catalog.aiReplyDelay;
        if (t_delay > 0f)
        {
            bool t_canceled = await UniTask.Delay((int)(t_delay * 1000), ignoreTimeScale: true,
                                                  cancellationToken: _ct)
                                           .SuppressCancellationThrow();
            if (t_canceled) return;
        }

        PlayId(PickReplyId(_playerId), _isEnemy: true);
    }

    /// <summary>되받을 감정표현 id 하나. 풀 전체에서 뽑되(장착 목록이 아니다 — AI는 장착이 없다)
    /// 내가 낸 것과 겹치지 않게 고른다. 같은 것이 되돌아오면 "반응"이 아니라 "따라 한 것"으로 읽힌다.</summary>
    int PickReplyId(int _playerId)
    {
        int t_count = this.catalog.Count;
        if (t_count <= 0) return 0;

        if (t_count == 1)
        {
            EmoteEntry t_only = this.catalog.PoolAt(0);
            return t_only != null ? t_only.id : 0;
        }

        int t_start = Random.Range(0, t_count);
        for (int t_offset = 0; t_offset < t_count; t_offset++)
        {
            EmoteEntry t_entry = this.catalog.PoolAt((t_start + t_offset) % t_count);
            if (t_entry != null && t_entry.id > 0 && t_entry.id != _playerId) return t_entry.id;
        }
        return 0;
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
