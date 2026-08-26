using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

public static class FirebaseManager
{
    static readonly List<IFirebaseModule> s_modules = new();
    static FirebaseFirestore s_firestore;
    static bool s_initialized;

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

    internal static void Initialize(string _envId)
    {
        if (s_initialized) throw new InvalidOperationException("FirebaseManager is already initialized.");
        FirebaseRootPath.Environment(_envId);

        var t_context = new FirebaseContext(_envId, GetFirestore);
        int t_initializedCount = 0;
        try
        {
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

    internal static void FlushPending()
    {
        if (!s_initialized) return;
        foreach (IFirebaseModule t_module in s_modules) t_module.FlushPending();
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
        s_firestore = FirebaseFirestore.DefaultInstance;
        s_firestore.Settings.PersistenceEnabled = false;
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
