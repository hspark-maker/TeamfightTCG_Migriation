using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

[DefaultExecutionOrder(-210)]
public sealed class InitializationRunner : MonoBehaviour
{
    [Tooltip("후속 전환 단계에서만 켠다. 현재는 InitializationInstaller가 초기화를 계속 담당한다.")]
    [SerializeField] bool initializeOnAwake;
    [SerializeField] List<MainInitializer> initializers = new();

    public IReadOnlyList<MainInitializer> Initializers => initializers;

    // 부트를 선점한 사본이 카드 카탈로그 구성까지 마쳤는가. 프리팹 사본이 둘이라 하나만 참이 된다.
    // 실패한 사본은 루트째 걷히므로 false로 남고, 늦게 깬 사본이 처음부터 다시 시도한다.
    internal static bool BootClaimed { get; private set; }

    internal static void MarkBootClaimed() => BootClaimed = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => BootClaimed = false;

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

        var t_context = new InitializationContext(this);
        foreach (MainInitializer t_initializer in initializers)
        {
            try
            {
                await t_initializer.Initialize(t_context);
            }
            catch (Exception t_exception)
            {
                Debug.LogException(t_exception, t_initializer);
                if (!t_initializer.Required) continue;

                GameInitialization.MarkRecoveryRequired();
                return;
            }

            // 중단은 실패가 아니다 — 루트가 파괴됐을 수 있으므로 남은 스텝을 돌리지 않고 조용히 빠진다.
            if (t_context.IsAborted) return;
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
