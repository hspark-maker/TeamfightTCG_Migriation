using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>카드 아트(Addressables)를 미리 받아 두고 **동기로** 꺼내 쓰게 하는 캐시.
///
/// 왜 캐시가 필요한가: 카드 아트를 읽는 지점(<see cref="CardVisualRules.PickCardArt"/> 계열)은 전부
/// `Sprite`를 그 프레임에 대입하는 동기 코드다. 아트를 AssetReference로 빼면서 그 시그니처를 async로 바꾸면
/// CardView·CardVisualView·팩 연출·비행 이펙트까지 호출 사슬 전체가 전염된다. 그래서 **로드 시점과 사용 시점을
/// 분리**한다 — 화면에 들어가기 전에 <see cref="Preload"/>로 채우고, 그리는 순간에는 사전에서 꺼내기만 한다.
///
/// 계약: 그릴 카드는 그리기 전에 반드시 Preload를 거친다. 안 거치면 그 프레임엔 null이 나가고(호출부가
/// 렌더러를 끈다) 뒤늦게 로드가 걸린다 — 조용히 빈 카드가 뜨므로 미스는 경고로 남긴다.
///
/// 여기 넣지 않는 것: 무엇을 프리로드할지 고르는 규칙(덱·도감·소유 목록은 호출부가 안다),
/// 진화 단계 폴백(그건 표시 규칙이라 <see cref="CardVisualRules"/> 소유).</summary>
public static class CardArtCache
{
    // 키 = AssetReference.RuntimeKey(=에셋 guid 문자열). 같은 스프라이트를 두 카드가 공유해도 1회만 로드된다.
    static readonly Dictionary<string, Sprite> s_loaded = new Dictionary<string, Sprite>();
    static readonly Dictionary<string, AsyncOperationHandle<Sprite>> s_handles =
        new Dictionary<string, AsyncOperationHandle<Sprite>>();
    // 로드 중인 키. 미스로 걸린 지연 로드가 같은 키를 중복 요청하지 않게 막는다.
    static readonly HashSet<string> s_pending = new HashSet<string>();

    /// <summary>프리로드가 끝나 새 아트가 들어왔을 때 발화. 미스로 뒤늦게 채워진 화면을 다시 그리고 싶은
    /// 호출부가 구독한다(현재 구독자는 없다 — 프리로드 계약을 지키면 미스가 안 난다).</summary>
    public static event System.Action OnArtLoaded;

    /// <summary>참조가 실제로 배선되어 있는가. **로드하지 않고** 판정한다 —
    /// 진화 단계 폴백(빈 슬롯이면 이전 단계로)은 로드 전에 결정해야 하므로 이 판정이 필수다.</summary>
    public static bool IsAssigned(AssetReferenceSprite _ref)
        => _ref != null && _ref.RuntimeKeyIsValid();

    static string KeyOf(AssetReferenceSprite _ref) => _ref.RuntimeKey.ToString();

    /// <summary>캐시에 있으면 스프라이트, 없으면 null. 미스면 지연 로드를 걸어 두지만
    /// **그 프레임엔 null이 나간다** — 호출부는 지금도 null을 렌더러 끄기로 처리하고 있다.</summary>
    public static Sprite Get(AssetReferenceSprite _ref)
    {
        if (!IsAssigned(_ref)) return null;

        string t_key = KeyOf(_ref);
        if (s_loaded.TryGetValue(t_key, out Sprite t_sprite)) return t_sprite;

        if (s_pending.Add(t_key))
        {
            Debug.LogWarning($"[CardArtCache] 프리로드 안 된 아트를 그리려 했다(이번 프레임은 빈 칸): {t_key}");
            LoadOne(t_key, _ref);
        }
        return null;
    }

    /// <summary>카드들의 아트를 전부 받아올 때까지 도는 코루틴. 이미 캐시에 있는 키는 건너뛴다.
    /// 진화 단계 아트까지 전부 받는다 — 전투 중 진화하면 그 자리에서 그림이 바뀌어야 하기 때문이다.</summary>
    public static IEnumerator Preload(IEnumerable<CardData> _cards)
    {
        if (_cards == null) yield break;

        var t_wanted = new Dictionary<string, AssetReferenceSprite>();
        foreach (CardData t_card in _cards)
            CollectRefs(t_card, t_wanted);

        foreach (KeyValuePair<string, AssetReferenceSprite> t_pair in t_wanted)
        {
            if (s_loaded.ContainsKey(t_pair.Key) || s_pending.Contains(t_pair.Key)) continue;
            s_pending.Add(t_pair.Key);
            LoadOne(t_pair.Key, t_pair.Value);
        }

        while (s_pending.Count > 0) yield return null;
        OnArtLoaded?.Invoke();
    }

    /// <summary>이 카드가 쓰는 아트 참조 전부(미진화 0단계 ~ 최종 단계).</summary>
    static void CollectRefs(CardData _card, Dictionary<string, AssetReferenceSprite> _into)
    {
        if (_card == null) return;

        for (int t_stage = 0; t_stage <= CardData.MaxEvolutionStage; t_stage++)
        {
            CardArtSet t_art = _card.GetArt(t_stage);
            if (t_art != null) Add(t_art.battleImageRef, _into);
        }
    }

    static void Add(AssetReferenceSprite _ref, Dictionary<string, AssetReferenceSprite> _into)
    {
        if (!IsAssigned(_ref)) return;
        _into[KeyOf(_ref)] = _ref;
    }

    static void LoadOne(string _key, AssetReferenceSprite _ref)
    {
        // AssetReference 인스턴스는 CardData 에셋이 소유하므로 같은 참조를 두 번 LoadAssetAsync 하면
        // 핸들이 겹친다. 키로 주소를 직접 로드해 참조 인스턴스 상태에 얽히지 않게 한다.
        AsyncOperationHandle<Sprite> t_handle = Addressables.LoadAssetAsync<Sprite>(_key);
        s_handles[_key] = t_handle;
        t_handle.Completed += _op =>
        {
            s_pending.Remove(_key);
            if (_op.Status == AsyncOperationStatus.Succeeded) s_loaded[_key] = _op.Result;
            else Debug.LogError($"[CardArtCache] 카드 아트 로드 실패: {_key}");
        };
    }

    /// <summary>받아 둔 아트를 전부 놓는다. 놓지 않으면 Addressables 참조 카운트가 남아
    /// 씬을 나가도 메모리가 안 내려간다 — 화면을 벗어날 때 호출부가 불러야 한다.</summary>
    public static void ReleaseAll()
    {
        foreach (KeyValuePair<string, AsyncOperationHandle<Sprite>> t_pair in s_handles)
            if (t_pair.Value.IsValid()) Addressables.Release(t_pair.Value);

        s_handles.Clear();
        s_loaded.Clear();
        s_pending.Clear();
    }

    /// <summary>프리로드가 아직 도는 중인가.</summary>
    public static bool IsBusy => s_pending.Count > 0;

    /// <summary>지금 캐시에 올라와 있는 아트 수(진단용).</summary>
    public static int LoadedCount => s_loaded.Count;
}
