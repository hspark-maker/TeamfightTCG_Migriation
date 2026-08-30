using Cysharp.Threading.Tasks;
using UnityEngine;

public enum ESignInMethod
{
    None,
    Anonymous,
    Email,
}

/// <summary>부트가 Firebase 를 세우기 전에 "이 기기가 어떤 계정으로 들어가는가"를 확정하는 관문.
///
/// <para>판정은 기기에 저장한 키 하나다. 키가 있으면 화면을 띄우지 않고 그대로 부트한다 —
/// 익명 계정의 자격증명은 Firebase SDK 가 이미 기기에 들고 있어서
/// <c>FirebaseAuthService</c> 의 복원 경로가 같은 uid 를 되살린다.</para>
///
/// <para>키가 없을 때만(=첫 실행) 로그인 화면이 서고, 유저가 고를 때까지 부트가 멈춘다.
/// <b>Firebase 기동보다 앞</b>이어야 하는 이유: <c>PlayerSaveCloud.AuthenticateAsync</c> 가
/// 인증을 5초 상한으로 기다리므로, 그 뒤에서 사람을 기다리면 부트가 타임아웃으로 죽는다.</para></summary>
public static class SignInGate
{
    const string MethodKey = "auth.signIn.method";

    // 화면이 뜨기를 기다리는 상한. 이 안에 아무 화면도 등록하지 않으면 익명으로 진행한다 —
    // 로그인 화면이 없는 씬(테스트·단독 실행)에서 부트가 영원히 멈추지 않게 하는 것이 목적이다.
    const int PanelWaitMilliseconds = 2000;

    static UniTaskCompletionSource<ESignInMethod> s_choice;
    static bool s_panelReady;
    static bool s_completed;

    /// <summary>기기에 저장된 로그인 방식. None 이면 아직 한 번도 고르지 않았다.</summary>
    public static ESignInMethod StoredMethod
    {
        get
        {
            string t_stored = LocalPrefs.GetString(MethodKey, string.Empty);
            if (t_stored == ESignInMethod.Anonymous.ToString()) return ESignInMethod.Anonymous;
            if (t_stored == ESignInMethod.Email.ToString()) return ESignInMethod.Email;
            return ESignInMethod.None;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_choice = null;
        s_panelReady = false;
        s_completed = false;
    }

    /// <summary>계정이 정해졌는가. 로딩 화면이 "인증 대기" 단계를 끝낼 시점을 이 값으로 본다.</summary>
    public static bool IsResolved => s_completed || StoredMethod != ESignInMethod.None;

    /// <summary>로그인 화면이 자기 존재를 알린다. 이걸 부르지 않으면 관문은 상한 뒤 익명으로 넘어간다.</summary>
    public static void MarkPanelReady() => s_panelReady = true;

    /// <summary>화면이 유저의 선택을 확정한다. 키를 남겨 다음 실행부터는 화면이 서지 않는다.</summary>
    public static void Complete(ESignInMethod _method)
    {
        if (_method == ESignInMethod.None || s_completed) return;

        s_completed = true;
        LocalPrefs.SetString(MethodKey, _method.ToString());
        LocalPrefs.Save();
        s_choice?.TrySetResult(_method);
    }

    /// <summary>기기에 저장된 방식을 지운다 — 다음 실행에서 로그인 화면이 다시 선다.</summary>
    public static void ClearStoredMethod()
    {
        LocalPrefs.DeleteKey(MethodKey);
        LocalPrefs.Save();
    }

    /// <summary>계정이 정해질 때까지 부트를 멈춘다. 이미 정해져 있으면 그 자리에서 끝난다.</summary>
    public static async UniTask<ESignInMethod> WaitAsync()
    {
        ESignInMethod t_stored = StoredMethod;
        if (t_stored != ESignInMethod.None) return t_stored;

        // 대기 소스를 먼저 세운다 — 화면이 먼저 떠서 유저가 즉시 누르면 Complete 가 받을 곳이 없다.
        s_choice = new UniTaskCompletionSource<ESignInMethod>();

        // 화면이 뜰 시간을 준다. 씬에 로그인 화면이 없으면 여기서 결론이 난다.
        int t_waited = 0;
        while (!s_panelReady && !s_completed && t_waited < PanelWaitMilliseconds)
        {
            await UniTask.Delay(100, DelayType.Realtime);
            t_waited += 100;
        }

        if (!s_panelReady && !s_completed)
        {
            Debug.LogWarning("[SignInGate] 로그인 화면이 없어 익명으로 진행한다.");
            Complete(ESignInMethod.Anonymous);
        }

        return await s_choice.Task;
    }
}
