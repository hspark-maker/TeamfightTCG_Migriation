using UnityEditor;
using UnityEngine;

/// <summary>기기에 저장된 로그인 방식을 지우는 에디터 메뉴. 다음 Play 에서 로그인 화면이 다시 선다 —
/// 익명으로 들어와 버린 인스턴스를 기존 계정(이메일)으로 되돌릴 때 쓴다.
///
/// <para><see cref="SignInGate.ClearStoredMethod"/> 는 있었지만 부르는 자리가 없었다. 저장된 방식이 남아 있는 한
/// 로그인 화면은 서지 않으므로, 손으로 지울 창구가 없으면 그 인스턴스는 그 계정에 묶인 채로 남는다.</para></summary>
static class SignInGateMenu
{
    const string MENU = "Tools/Account/Clear stored sign-in method";

    [MenuItem(MENU)]
    static void ClearStoredMethod()
    {
        ESignInMethod t_before = SignInGate.StoredMethod;
        SignInGate.ClearStoredMethod();

        Debug.Log($"[SignInGate] Stored sign-in method cleared (was {t_before}). "
                + "The sign-in screen appears on the next Play.");
    }

    // 지울 것이 없으면 눌러도 할 일이 없다 — 메뉴를 흐려 상태를 그대로 읽히게 한다.
    [MenuItem(MENU, true)]
    static bool ValidateClearStoredMethod() => SignInGate.StoredMethod != ESignInMethod.None;
}
