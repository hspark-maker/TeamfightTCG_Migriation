using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Firestore;
using UnityEngine;

public static class FirebaseManager
{
    static readonly List<IFirebaseModule> s_modules = new();
    static FirebaseFirestore s_firestore;
    static FirebaseEmulatorConfig s_emulators = FirebaseEmulatorConfig.Disabled;
    static bool s_initialized;
    static bool s_settingsApplied;

    public static bool IsInitialized => s_initialized;

    internal static void Register(IFirebaseModule _module)
    {
        if (_module == null) throw new ArgumentNullException(nameof(_module));
        if (s_initialized) throw new InvalidOperationException("Firebase modules must be registered before initialization.");
        foreach (IFirebaseModule t_registered in s_modules)
            if (t_registered.GetType() == _module.GetType())
                throw new InvalidOperationException($"Firebase module is already registered: {_module.GetType().Name}");
        s_modules.Add(_module);
    }

    internal static void Initialize(string _envId, in FirebaseEmulatorConfig _emulators)
    {
        if (s_initialized) throw new InvalidOperationException("FirebaseManager is already initialized.");
        FirebaseRootPath.Environment(_envId);

        // 어느 백엔드에 붙었는지 부트 로그 첫 줄에서 읽히지 않으면, 왕복 판정이 "어디를 상대로 성공했는지" 모른 채 내려진다.
        s_emulators = _emulators;
        Debug.Log($"[FirebaseManager] backend={_emulators} env={_envId} database={FirebaseRootPath.DatabaseId}");

        var t_context = new FirebaseContext(_envId, GetFirestore);
        int t_initializedCount = 0;
        try
        {
            FirebaseAuthService.Instance.UseEmulator(_emulators.AuthHost, _emulators.AuthPort);
            FirebaseAuthService.Instance.InitializeAsync().Forget();
            for (; t_initializedCount < s_modules.Count; t_initializedCount++)
            {
                try { s_modules[t_initializedCount].Initialize(in t_context); }
                catch
                {
                    SafeShutdown(s_modules[t_initializedCount]);
                    throw;
                }
            }
            s_initialized = true;
        }
        catch
        {
            for (int i = t_initializedCount - 1; i >= 0; i--)
                SafeShutdown(s_modules[i]);
            FirebaseAuthService.Instance.Shutdown();
            s_firestore = null;
            s_initialized = false;
            throw;
        }
    }

    internal static void RetryPending()
    {
        if (!s_initialized) return;
        foreach (IFirebaseModule t_module in s_modules) t_module.RetryPending();
    }

    internal static UniTask FlushPendingAsync()
    {
        if (!s_initialized) return UniTask.CompletedTask;

        // 모듈 하나가 동기적으로 터지면 WhenAll에 닿기 전에 팬아웃 전체가 죽는다 —
        // 실제로 스펙 동기 모듈이 먼저 등록돼 있어 세이브 flush가 한 번도 실행되지 않았다.
        var t_flushes = new UniTask[s_modules.Count];
        for (int t_i = 0; t_i < s_modules.Count; t_i++)
        {
            try
            {
                t_flushes[t_i] = s_modules[t_i].FlushPendingAsync();
            }
            catch (System.Exception t_exception)
            {
                t_flushes[t_i] = UniTask.CompletedTask;
                Debug.LogException(t_exception);
            }
        }

        return UniTask.WhenAll(t_flushes);
    }

    internal static void Shutdown()
    {
        for (int i = s_modules.Count - 1; i >= 0; i--) SafeShutdown(s_modules[i]);
        FirebaseAuthService.Instance.Shutdown();
        s_firestore = null;
        s_initialized = false;
    }

    static FirebaseFirestore GetFirestore()
    {
        if (s_firestore != null) return s_firestore;
        // 기본 DB가 아니라 이름 있는 DB(FirebaseRootPath.DatabaseId)를 잡는다.
        s_firestore = FirebaseFirestore.GetInstance(FirebaseApp.DefaultInstance, FirebaseRootPath.DatabaseId);

        // 설정 대입은 프로세스당 1회다 — 인스턴스는 프로세스 전역 싱글턴이라 한 번 적용하면 그대로인데,
        // 이미 네트워크 작업을 시작한 클라이언트에 Settings를 다시 대입하면 SDK가 던져 재시도가 원리상 못 고친다.
        // (그래서 Shutdown에서도 이 플래그는 리셋하지 않는다.)
        if (!s_settingsApplied)
        {
            s_firestore.Settings.PersistenceEnabled = false;

            // Host/SslEnabled도 같은 1회 창이라 여기서만 바꿀 수 있다 — 첫 읽기·쓰기가 나간 뒤에는 SDK가 던진다.
            if (s_emulators.IsEnabled)
            {
                s_firestore.Settings.Host = s_emulators.FirestoreHost;
                s_firestore.Settings.SslEnabled = false;
                Debug.LogWarning($"[FirebaseManager] Firestore is pointed at the emulator: {s_emulators.FirestoreHost}");
            }

            s_settingsApplied = true;
        }

        return s_firestore;
    }

    static void SafeShutdown(IFirebaseModule _module)
    {
        try { _module.Shutdown(); }
        catch (Exception t_exception) { Debug.LogException(t_exception); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        Shutdown();
        s_modules.Clear();
    }
}
