using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>Marker base used by DataLibrary to index non-pooled runtime prefabs resolved by
/// component type — 전면 오버레이뿐 아니라 화면 밖 무대처럼 타입으로 찾는 단일 인스턴스 프리팹도 포함한다.</summary>
public abstract class SingletonOverlayBase : MonoBehaviour
{
}

/// <summary>단일 인스턴스 회수와 개폐 계약을 함께 쥔다 — 여는 표식·닫는 표식·닫힘 통지·닫힘 대기·정렬 층이
/// 전부 여기 있고, 파생은 자기 화면을 어떻게 세우고 걷는지만 정한다.</summary>
public abstract class SingletonOverlay<T> : SingletonOverlayBase
    where T : SingletonOverlay<T>
{
    static T s_instance;

    /// <summary>이 타입의 화면이 떠 있는가. 제네릭 타입 인자마다 갈라진 static이라 타입별로 독립이다.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>닫힘 통지. 쏘는 문은 <see cref="NotifyClosed"/> 하나뿐이다.</summary>
    public static event Action OnAnyClosed;

    /// <summary>이 화면이 서는 층(<see cref="UiSortingOrder"/> 표의 값). 프리팹 저작값이 아니라 이 값이 이긴다 —
    /// 인스턴스를 받아 드는 자리에서 한 번 찍는다. 루트 캔버스가 없는 파생은 어떤 값을 줘도 찍기가 그냥 지나간다.</summary>
    protected abstract int SortingOrder { get; }

    protected static bool TryGetExisting(out T _overlay)
    {
        if (s_instance == null) Adopt(FindFirstObjectByType<T>(FindObjectsInactive.Include));

        _overlay = s_instance;
        return _overlay != null;
    }

    protected static bool TryGetOrCreate(Func<GameObject> _loadPrefab, out T _overlay)
    {
        if (TryGetExisting(out _overlay)) return true;

        GameObject t_prefab = _loadPrefab?.Invoke();
        if (t_prefab == null) return false;

        GameObject t_instance = Instantiate(t_prefab);

        T t_found = t_instance.GetComponent<T>();
        if (t_found == null)
        {
            Debug.LogError(
                $"[SingletonOverlay] The root of {t_prefab.name} has no {typeof(T).Name}.",
                t_prefab);
            Destroy(t_instance);
        }

        Adopt(t_found);

        _overlay = s_instance;
        return _overlay != null;
    }

    /// <summary>화면을 열린 것으로 표시한다(Show가 부른다).
    ///
    /// <b>여기에 중복 진입 가드(IsOpen이면 return)를 두지 않는다.</b> 파생의 Show는 전부 "걷고 다시 세운다"라
    /// 조용히 돌아가면 호출자가 넘긴 콜백이 통째로 버려진다 — 튜토리얼은 그 콜백으로만 다음 걸음을 잇는다
    /// (TutorialStepExecutor). 화면이 겹치는 것보다 진행이 영영 멈추는 쪽이 나쁘다. 대신 에디터에서만 소리를 낸다.</summary>
    protected static void MarkOpen()
    {
#if UNITY_EDITOR
        if (IsOpen)
            Debug.LogWarning($"[SingletonOverlay] {typeof(T).Name}이 열린 채로 다시 열립니다 — 앞 호출자의 콜백이 버려집니다.");
#endif
        IsOpen = true;
    }

    /// <summary>열림 표식을 내리고 <b>내가 내렸는지</b>를 돌려준다. 연타·중복 닫기는 이 값이 거짓인 것으로 갈린다.
    /// 통지와 갈라 둔 이유: 상태 소거와 닫힘 통지의 시점이 다른 판이 있다(CardRewardOverlay의 넘겨주기).</summary>
    protected static bool ConsumeOpen()
    {
        bool t_wasOpen = IsOpen;
        IsOpen = false;
        return t_wasOpen;
    }

    /// <summary>닫힘을 알린다. <paramref name="_wasOpen"/>이 거짓이면 아무 일도 하지 않는다 —
    /// <see cref="ConsumeOpen"/>이 돌려준 값을 그대로 넘겨 두 번 울리는 길을 막는다.</summary>
    protected static void NotifyClosed(bool _wasOpen)
    {
        if (_wasOpen) OnAnyClosed?.Invoke();
    }

    /// <summary>표식만 내린다(통지 없음). Show를 거치지 않고 꺼지는 길(부모 비활성·씬 언로드)의 자리다 —
    /// 그 길에도 기다리는 쪽이 있지만 <see cref="WaitUntilClosedAsync"/>가 이 표식을 보고 있어 함께 풀린다.</summary>
    protected static void ClearOpen() => IsOpen = false;

    /// <summary>이 타입의 화면이 닫힐 때까지 기다린다(떠 있지 않으면 곧바로 돌아온다).
    /// 통지가 아니라 <see cref="IsOpen"/>을 본다 — 콜백도 통지도 거치지 않고 꺼지는 길이 실제로 있어서,
    /// 통지에만 걸면 대기가 영영 안 풀린다. 기다리는 쪽이 먼저 죽을 수 있으면 GetCancellationTokenOnDestroy()를 넘길 것.</summary>
    public static UniTask WaitUntilClosedAsync(CancellationToken _token = default)
        => UniTask.WaitUntil(() => !IsOpen, cancellationToken: _token);

    protected virtual void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
        ClearOpen();
    }

    // 인스턴스를 받아 드는 단 한 곳. 층을 여기서 찍는다 — 베이스에 protected virtual Awake를 두면
    // 파생이 base.Awake()를 빠뜨리는 함정이 생긴다(PooledUIBase에서 이미 겪었고 ServerWaitOverlay.cs:88에 경고가 남아 있다).
    static void Adopt(T _overlay)
    {
        s_instance = _overlay;
        if (_overlay == null) return;

        UiSortingOrder.Stamp(_overlay.GetComponent<Canvas>(), _overlay.SortingOrder);
    }
}
