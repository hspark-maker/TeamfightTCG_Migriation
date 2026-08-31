using System;
using System.Collections.Generic;
using System.Threading;
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
    static CancellationTokenSource s_lifetime = new CancellationTokenSource();

    public static bool IsInitialized => s_initialized;

    /// <summary>이번 Firebase 세션의 수명 토큰. <see cref="Shutdown"/> 에서 취소된다.
    ///
    /// <para>Firestore·Functions 호출을 도는 폴링 루프는 반드시 여기에 묶어야 한다. 안 묶으면
    /// 에디터 정리(ShutdownForEditor)가 <c>TerminateAsync</c> 로 넘어갈 때 진행 중인 호출이 남아
    /// 종료가 끝나지 않고, gRPC 네이티브 스레드가 살아남아 Unity가 "Reloading Domain"에서 멈춘다.</para></summary>
    public static CancellationToken Lifetime => s_lifetime.Token;

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

        // 어느 백엔드에 붙었는지 초기화 로그 첫 줄에서 읽히지 않으면, 왕복 판정이 "어디를 상대로 성공했는지" 모른 채 내려진다.
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
        // 모듈보다 먼저 끊는다 — 진행 중인 콜러블·Firestore 왕복이 살아 있으면 종료가 그것들을 기다린다.
        CancelLifetime();

        for (int i = s_modules.Count - 1; i >= 0; i--) SafeShutdown(s_modules[i]);
        FirebaseAuthService.Instance.Shutdown();
        s_firestore = null;
        s_initialized = false;
    }

    static void CancelLifetime()
    {
        CancellationTokenSource t_previous = s_lifetime;
        s_lifetime = new CancellationTokenSource();
        try { t_previous.Cancel(); }
        catch (ObjectDisposedException) { }
        t_previous.Dispose();
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

#if UNITY_EDITOR
    // 에디터 전용 정리. Firestore 리스너·Auth 콜백이 살아 있는 채로 도메인 리로드에 들어가면
    // Unity가 "Reloading Domain"에서 네이티브 스레드를 기다리다 멈춘다 —
    // Play 종료와 어셈블리 리로드 직전에 먼저 내린다.
    [UnityEditor.InitializeOnLoadMethod]
    static void InstallEditorTeardown()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeChanged;

        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ShutdownForEditor;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ShutdownForEditor;
    }

    static void HandlePlayModeChanged(UnityEditor.PlayModeStateChange _state)
    {
        if (_state == UnityEditor.PlayModeStateChange.ExitingPlayMode) ShutdownForEditor();
    }

    static void ShutdownForEditor()
    {
        // Firestore는 gRPC 네이티브 채널·스레드를 들고 있다. 참조만 버리면(s_firestore = null)
        // 그 스레드가 남아 도메인 리로드가 "Reloading Domain"에서 멈춘다 — 명시적으로 종료시킨다.
        FirebaseFirestore t_firestore = s_firestore;

        try { Shutdown(); }
        catch (Exception t_exception) { Debug.LogWarning($"[Firebase] 에디터 정리 실패: {t_exception.Message}"); }

        // 여기서 TerminateAsync를 await하거나 .Wait()로 막으면 안 된다.
        // Firebase는 완료 콜백을 UnitySynchronizationContext로 메인 스레드에 넘기는데,
        // 리로드 콜백은 그 메인 스레드에서 돈다 — 막는 순간 콜백이 실행될 유일한 경로를 스스로 끊어
        // 종료가 영영 끝나지 않고, 남은 gRPC 스레드 때문에 "Reloading Domain"이 멈춘다.
        // 그래서 종료는 킥만 하고, 네이티브 자원은 아래 앱 Dispose가 동기로 내린다.
        if (t_firestore != null)
        {
            try { t_firestore.TerminateAsync(); }
            catch (Exception t_exception)
            {
                Debug.LogWarning($"[Firebase] Firestore 종료 요청 실패: {t_exception.Message}");
            }
        }

        // C++ SDK의 앱 소멸자가 Firestore·Auth·Functions의 네이티브 스레드를 동기로 내린다 —
        // 메인 스레드 펌핑에 의존하지 않는 유일한 경로다. 참조만 버리면 스레드가 남는다.
        // 다음 플레이에서는 FirebaseApp.DefaultInstance가 다시 만들어진다(정적 상태는 리로드가 비운다).
        try
        {
            FirebaseApp.DefaultInstance?.Dispose();
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[Firebase] 앱 정리 실패: {t_exception.Message}");
        }
    }
#endif
}
