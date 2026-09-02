using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 초기화 실행 주체. 저작된 스텝 목록을 순서대로 돌린다 — 순서는 코드가 아니라 이 리스트가 정한다.
[DefaultExecutionOrder(-210)]
public sealed class InitializationRunner : MonoBehaviour
{
    [SerializeField] bool initializeOnAwake = true;
    [SerializeField] List<MainInitializer> initializers = new();

    static InitializationRunner s_instance;

    InitializationContext m_context;

    public IReadOnlyList<MainInitializer> Initializers => initializers;

    // 초기화를 선점한 사본이 카드 카탈로그 구성까지 마쳤는가. 프리팹 사본이 둘이라 하나만 참이 된다.
    // 실패한 사본은 루트째 걷히므로 false로 남고, 늦게 깬 사본이 처음부터 다시 시도한다.
    internal static bool InitClaimed { get; private set; }

    internal static void MarkInitClaimed() => InitClaimed = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        InitClaimed = false;
        s_instance = null;
    }

    void Awake()
    {
        if (!initializeOnAwake) return;
        InitializeAsync().Forget();
    }

    async UniTask InitializeAsync()
    {
        if (!TryValidateOrder(out string t_error))
        {
            Debug.LogError($"[InitializationRunner] {t_error}", this);
            GameInitialization.MarkRecoveryRequired();
            return;
        }

        // 초기화를 선점한 러너. 재시도가 다시 걸 대상을 찾는 유일한 통로다(씬 탐색 금지).
        s_instance = this;
        m_context = new InitializationContext(this);

        await RunFrom(0);
    }

    /// <summary>복구 화면의 재시도가 초기화를 대기 지점부터 다시 태운다(씬 재로드 없음).
    /// 어디서 다시 시작할지는 코드가 아니라 스텝의 retryEntry 저작값이 정한다.</summary>
    internal static void RestartGate()
    {
        if (s_instance == null)
        {
            Debug.LogError("[InitializationRunner] 초기화 러너가 없어 재시도를 걸 수 없습니다.");
            return;
        }

        s_instance.RestartFromRetryEntry().Forget();
    }

    async UniTask RestartFromRetryEntry()
    {
        int t_start = initializers.FindIndex(_step => _step != null && _step.RetryEntry);
        if (t_start < 0)
        {
            Debug.LogError("[InitializationRunner] retryEntry로 표시된 스텝이 없어 재시도할 자리를 못 찾았습니다.", this);
            return;
        }

        // 앞선 즉시 단계는 이미 섰고 멱등도 아니다 — 재시도는 대기 지점부터만 다시 돈다.
        m_context ??= new InitializationContext(this);
        await RunFrom(t_start);
    }

    async UniTask RunFrom(int _start)
    {
        for (int i = _start; i < initializers.Count; i++)
        {
            MainInitializer t_initializer = initializers[i];
            try
            {
                await t_initializer.Initialize(m_context);
            }
            catch (Exception t_exception)
            {
                Debug.LogException(t_exception, t_initializer);
                if (!t_initializer.Required) continue;

                GameInitialization.MarkRecoveryRequired();
                return;
            }

            // 중단은 실패가 아니다 — 루트가 파괴됐을 수 있으므로 남은 스텝을 돌리지 않고 조용히 빠진다.
            if (m_context.IsAborted) return;
        }
    }

    bool TryValidateOrder(out string _error)
    {
        var t_seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < initializers.Count; i++)
        {
            MainInitializer t_initializer = initializers[i];
            if (t_initializer == null)
            {
                _error = $"Initializers[{i}] is null.";
                return false;
            }

            if (t_initializer.transform.root != transform.root)
            {
                _error = $"'{t_initializer.InitializerId}' must belong to the same initialization root.";
                return false;
            }

            string t_id = t_initializer.InitializerId;
            if (!t_seenIds.Add(t_id))
            {
                _error = $"Initializer id '{t_id}' is duplicated.";
                return false;
            }

            foreach (string t_requiredId in t_initializer.RequiredIds)
            {
                if (string.IsNullOrWhiteSpace(t_requiredId)) continue;
                if (t_seenIds.Contains(t_requiredId)) continue;

                _error = $"'{t_id}' requires preceding initializer '{t_requiredId}'.";
                return false;
            }
        }

        _error = null;
        return true;
    }
}
