using UnityEditor;
using UnityEngine;

/// <summary>
/// 관리자 로그인 탭.
///
/// <para>로그인 칸은 원래 데이터 업로드·콘텐츠 버전·전투 재생 세 곳이 각자 그려서, 릴리즈 탭 하나에
/// 같은 칸이 두 번, 창 전체로는 세 번 나왔다. 로그인 상태는 <see cref="SpecAdminAuth"/> 하나가
/// 들고 있으므로 그리는 자리도 하나여야 한다. 각 기능은 <see cref="DrawAdminAuthStatus"/> 로
/// "지금 쓸 수 있는지"만 알리고, 실제 로그인 조작은 전부 이 탭에서 한다.</para>
/// </summary>
public partial class ReleaseManagerWindow
{
    string adminEmail;
    string adminPassword = string.Empty;
    string adminAuthError;
    bool adminOAuthOpen;
    bool adminPasswordOpen;
    bool adminGoogleRetry;

    Vector2 authScroll;

    /// <summary>admin 권한이 필요한 조작을 지금 할 수 있는지. 각 섹션의 버튼 잠금 기준이다.</summary>
    static bool AdminReady => SpecAdminAuth.IsSignedIn && SpecAdminAuth.HasAdminClaim;

    void DrawAuthTab()
    {
        this.authScroll = EditorGUILayout.BeginScrollView(this.authScroll);
        Header("관리자 로그인");
        EditorGUILayout.HelpBox(
            "Firestore 운영 규칙이 스펙 쓰기와 전투 재생 설정을 admin 클레임에만 허용한다. " +
            "Firestore 업로드·토글은 여기서 로그인한 계정으로 나간다. Google Sheets 연결은 Google Sheet 탭에서 따로 한다. " +
            "비밀번호는 저장하지 않고 토큰은 유니티 세션 동안만 산다.",
            MessageType.Info);
        DrawAdminAuth();
        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// 기능 섹션에 붙이는 한 줄짜리 상태 표시. 쓸 수 있으면 아무것도 그리지 않는다 —
    /// 정상 상태에 안내를 깔면 세 섹션이 같은 문구를 세 번 반복한다.
    /// </summary>
    void DrawAdminAuthStatus()
    {
        if (AdminReady) return;

        EditorGUILayout.HelpBox(
            SpecAdminAuth.IsSignedIn
                ? $"{SpecAdminAuth.SignedInEmail} 로 로그인했지만 admin 클레임이 없어 이 조작을 할 수 없다."
                : "관리자 로그인이 필요하다.",
            MessageType.Warning);
        if (GUILayout.Button("로그인 탭 열기")) this.selectedTab = Tab.Auth;
    }

    void DrawAdminAuth()
    {
        EditorGUILayout.Space();

        if (SpecAdminAuth.IsSignedIn)
        {
            if (SpecAdminAuth.HasAdminClaim)
            {
                EditorGUILayout.HelpBox($"{SpecAdminAuth.SignedInEmail} (admin) 로 로그인됨.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"{SpecAdminAuth.SignedInEmail} 로 로그인했지만 admin 클레임이 없다. " +
                    "이 계정으로는 스펙을 쓸 수 없다 — functions/scripts/grant-admin.js 로 클레임을 부여한 뒤 " +
                    "다시 로그인할 것(토큰에 클레임이 박히므로 재로그인이 필요하다).",
                    MessageType.Error);
            }

            if (GUILayout.Button("로그아웃"))
            {
                SpecAdminAuth.SignOut();
                this.adminPassword = string.Empty;
                this.adminAuthError = null;
                this.adminGoogleRetry = false;
            }
            return;
        }

        EditorGUILayout.HelpBox(
            "Google에 연동된 계정은 아래 Google 로그인으로 들어간다. 이메일·비밀번호 입력 없이 " +
            "브라우저에서 기존 관리자 Google 계정을 선택하면 된다.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!GoogleOAuthSignIn.IsConfigured))
        {
            if (GUILayout.Button("Google 연동 계정으로 로그인", GUILayout.Height(28)))
                SignInAdminWithGoogle();
        }

        this.adminOAuthOpen = EditorGUILayout.Foldout(this.adminOAuthOpen, "구글 OAuth 클라이언트 설정", true);
        if (this.adminOAuthOpen)
        {
            EditorGUILayout.HelpBox(
                "Google Cloud 콘솔 > API 및 서비스 > 사용자 인증 정보에서 '데스크톱 앱' 유형 OAuth 클라이언트를 " +
                "만들고 그 값을 넣는다. 리다이렉트는 루프백을 자동으로 쓰므로 따로 등록할 필요가 없다. " +
                "이 값은 EditorPrefs에만 저장되고 저장소에는 들어가지 않는다.",
                MessageType.Info);

            string t_clientId = EditorGUILayout.TextField("클라이언트 ID", GoogleOAuthSignIn.ClientId);
            if (t_clientId != GoogleOAuthSignIn.ClientId) GoogleOAuthSignIn.ClientId = t_clientId;

            string t_clientSecret = EditorGUILayout.PasswordField("클라이언트 보안 비밀", GoogleOAuthSignIn.ClientSecret);
            if (t_clientSecret != GoogleOAuthSignIn.ClientSecret) GoogleOAuthSignIn.ClientSecret = t_clientSecret;
        }

        EditorGUILayout.Space();
        this.adminPasswordOpen = EditorGUILayout.Foldout(this.adminPasswordOpen, "이메일·비밀번호로 로그인", true);
        if (this.adminPasswordOpen)
        {
            this.adminEmail ??= SpecAdminAuth.LastEmail;
            this.adminEmail = EditorGUILayout.TextField("이메일", this.adminEmail);
            this.adminPassword = EditorGUILayout.PasswordField("비밀번호", this.adminPassword);

            if (GUILayout.Button("로그인"))
            {
                bool t_ok = SpecAdminAuth.TrySignIn(this.adminEmail, this.adminPassword, out string t_error);
                this.adminAuthError = t_ok ? null : t_error;
                this.adminGoogleRetry = !t_ok;
                // 성공하든 실패하든 비밀번호는 메모리에 남기지 않는다.
                this.adminPassword = string.Empty;
                if (t_ok && !SpecAdminAuth.HasAdminClaim)
                    this.adminAuthError = "로그인은 됐지만 admin 클레임이 없다.";
            }
        }

        if (!string.IsNullOrEmpty(this.adminAuthError))
        {
            EditorGUILayout.HelpBox(this.adminAuthError, MessageType.Error);
            if (this.adminGoogleRetry)
            {
                EditorGUILayout.HelpBox(
                    "Google 로그인만 연결된 계정은 이메일·비밀번호로 인증할 수 없다. " +
                    "Google 연동 계정이라면 Google 로그인으로 계속할 것.", MessageType.Info);
                using (new EditorGUI.DisabledScope(!GoogleOAuthSignIn.IsConfigured))
                    if (GUILayout.Button("Google 로그인으로 계속", GUILayout.Height(28)))
                        SignInAdminWithGoogle();
            }
        }
        else if (!GoogleOAuthSignIn.IsConfigured)
            EditorGUILayout.HelpBox("구글 OAuth 클라이언트 ID를 넣으면 구글 로그인을 쓸 수 있다.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("스펙 업로드에는 admin 클레임을 가진 계정 로그인이 필요하다.", MessageType.Warning);
    }

    void SignInAdminWithGoogle()
    {
        this.adminPassword = string.Empty;
        this.adminGoogleRetry = false;
        bool t_ok = SpecAdminAuth.TrySignInWithGoogle(out string t_error);
        this.adminAuthError = t_ok ? null : t_error;
        if (t_ok && !SpecAdminAuth.HasAdminClaim)
            this.adminAuthError = "로그인은 됐지만 admin 클레임이 없다.";
    }
}
