using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>버전 대조 결과. 셋은 유저가 할 수 있는 일이 서로 달라 한 값으로 뭉치지 않는다.</summary>
public enum EAppVersionAction
{
    /// <summary>이 버전으로 계속 논다(문서를 못 읽은 경우도 여기다).</summary>
    Allowed,

    /// <summary>새 버전이 있지만 막지는 않는다. 유저가 미룰 수 있어야 한다.</summary>
    Recommended,

    /// <summary>서버가 이 버전을 더 받지 않는다. 초기화를 끊고 업데이트 화면으로 보낸다.</summary>
    Blocked,
}

/// <summary>앱 버전 통지의 단일 창구. 서버 문서 <c>envs/{envId}/config/app</c> 하나를 읽고
/// "차단 / 권장 / 공지" 를 정한다.
///
/// <para>Firebase Remote Config 를 쓰지 않는 이유: 그 패키지가 프로젝트에 없다. 이미 초기화에서 도는
/// Firestore 읽기에 문서 하나를 얹으면 SDK 추가도 초기화 순서 변경도 없다.</para>
///
/// <para><b>읽기 실패는 통과다.</b> 통지 기능이 가용성을 깎으면 손해다 — 서버가 죽거나 오프라인이면
/// 안내를 포기하고 게임을 연다. 그래서 이 게이트는 <b>안내용이지 보안 경계가 아니다</b>(비행기 모드로
/// 넘길 수 있다). 진짜 거절이 필요하면 callable 쪽에서 appVersion 을 보고 막아야 한다.</para></summary>
public static class AppVersionGate
{
    /// <summary>정책 문서 경로의 마지막 두 마디. <c>specs/_index</c> 와 같은 env 축에 둔다.</summary>
    const string ConfigCollection = "config";
    const string ConfigDocumentId = "app";

    /// <summary>이미 보여 준 공지 id. 세이브 문서가 아니라 기기에 남긴다 —
    /// 세이브는 스키마 검증과 revision 충돌 축이라, 안내 한 번 보여 준 사실 때문에 저장이 막히면 안 된다.</summary>
    const string SeenNoticeKey = "appVersion.notice.seenId";

    static FirebaseContext s_context;
    static bool s_initialized;

    static AppSemVer s_current;
    static bool s_currentParsed;

    static string s_storeUrl = string.Empty;
    static string s_latestText = string.Empty;
    static string s_noticeId = string.Empty;
    static string s_noticeTitle = string.Empty;
    static string s_noticeBody = string.Empty;

    static bool s_recommendationConsumed;
    static bool s_noticeConsumed;

    /// <summary>이번 실행의 판정. 평가 전에는 Allowed다.</summary>
    public static EAppVersionAction Action { get; private set; } = EAppVersionAction.Allowed;

    /// <summary>업데이트 안내가 보낼 스토어 주소. 서버 저작이라 스토어를 옮겨도 구버전이 죽은 링크에 갇히지 않는다.</summary>
    public static string StoreUrl => s_storeUrl;

    public static bool HasStoreUrl => !string.IsNullOrEmpty(s_storeUrl);

    /// <summary>서버가 알린 최신 버전 표기(안내 문구용). 없으면 빈 문자열.</summary>
    public static string LatestText => s_latestText;

    /// <summary>권장 업데이트 안내를 아직 안 띄웠는가.</summary>
    public static bool HasPendingRecommendation =>
        !s_recommendationConsumed && Action == EAppVersionAction.Recommended;

    /// <summary>이 기기에서 처음 보는 공지가 남아 있는가.</summary>
    public static bool HasPendingNotice =>
        !s_noticeConsumed &&
        !string.IsNullOrEmpty(s_noticeId) &&
        !string.IsNullOrEmpty(s_noticeBody) &&
        !string.Equals(LocalPrefs.GetString(SeenNoticeKey), s_noticeId, StringComparison.Ordinal);

    public static string NoticeTitle => s_noticeTitle;
    public static string NoticeBody => s_noticeBody;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_context = default;
        s_initialized = false;
        s_currentParsed = false;
        s_storeUrl = string.Empty;
        s_latestText = string.Empty;
        s_noticeId = string.Empty;
        s_noticeTitle = string.Empty;
        s_noticeBody = string.Empty;
        s_recommendationConsumed = false;
        s_noticeConsumed = false;
        Action = EAppVersionAction.Allowed;
    }

    internal static void Initialize(in FirebaseContext _context)
    {
        s_context = _context;
        s_initialized = _context.IsValid;
    }

    internal static void Shutdown()
    {
        s_initialized = false;
        s_context = default;
    }

    /// <summary>권장 안내를 띄웠다고 표시한다. 세션당 한 번만 뜨게 하는 유일한 자리다.</summary>
    public static void MarkRecommendationHandled() => s_recommendationConsumed = true;

    /// <summary>공지를 봤다고 기기에 남긴다. 같은 noticeId 는 다시 뜨지 않는다.</summary>
    public static void MarkNoticeSeen()
    {
        s_noticeConsumed = true;
        if (string.IsNullOrEmpty(s_noticeId)) return;

        LocalPrefs.SetString(SeenNoticeKey, s_noticeId);
        LocalPrefs.Save();
    }

    /// <summary>서버 정책을 읽고 판정한다. 예외를 던지지 않는다 — 무슨 일이 나든 최악이 Allowed다.</summary>
    public static async UniTask EvaluateAsync(CancellationToken _ct)
    {
        ClearPolicyResult();

        s_currentParsed = AppSemVer.TryGetCurrent(out s_current);
        if (!s_currentParsed)
        {
            // 여기서 막으면 bundleVersion 오저작 빌드가 통째로 못 켜진다. 안내를 포기하는 쪽을 고른다.
            Debug.LogWarning($"[AppVersionGate] Could not read the app version '{Application.version}', so the version notice is skipped.");
            return;
        }

        try
        {
            IDictionary<string, object> t_fields = await ReadPolicyAsync(_ct);
            if (t_fields == null) return;

            Apply(t_fields);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception t_exception)
        {
            // 통지를 못 읽은 것은 초기화 실패가 아니다. 로그만 남기고 게임을 연다.
            Debug.LogWarning($"[AppVersionGate] Failed to read the version policy: {t_exception.GetBaseException().Message}");
        }
    }

    // 재시도에서 서버 읽기가 실패했을 때 앞선 시도의 URL·공지까지 살아남으면 새 정책처럼 보인다.
    // 평가는 매번 빈 정책에서 시작하고, 이번 서버 응답으로만 결과를 채운다.
    static void ClearPolicyResult()
    {
        Action = EAppVersionAction.Allowed;
        s_storeUrl = string.Empty;
        s_latestText = string.Empty;
        s_noticeId = string.Empty;
        s_noticeTitle = string.Empty;
        s_noticeBody = string.Empty;
    }

    static async Task<IDictionary<string, object>> ReadPolicyAsync(CancellationToken _ct)
    {
        var t_wait = System.Diagnostics.Stopwatch.StartNew();
        while (!s_initialized)
        {
            _ct.ThrowIfCancellationRequested();
            if (t_wait.ElapsedMilliseconds >= FirebaseTimeouts.AuthAndReadMilliseconds)
            {
                Debug.LogWarning("[AppVersionGate] The Firebase module is not up, so the version notice is skipped.");
                return null;
            }
            await Task.Delay(50, _ct);
        }

        FirebaseFirestore t_store = s_context.GetFirestore();
        string t_path = $"{FirebaseRootPath.Environment(s_context.EnvId)}/{ConfigCollection}/{ConfigDocumentId}";

        // Source.Server 다 — 캐시를 읽으면 방금 올린 차단 정책이 구버전 기기에 며칠씩 안 닿는다.
        DocumentSnapshot t_snapshot = await t_store.Document(t_path).GetSnapshotAsync(Source.Server);
        _ct.ThrowIfCancellationRequested();

        if (!t_snapshot.Exists)
        {
            // 문서가 없는 것은 "정책 미저작"이지 오류가 아니다. 첫 배포 전 상태가 이렇다.
            Debug.Log($"[AppVersionGate] There is no version policy document ({t_path}). Proceeding without a notice.");
            return null;
        }

        return t_snapshot.ToDictionary();
    }

    static void Apply(IDictionary<string, object> _fields)
    {
        s_storeUrl = ReadStoreUrl(_fields);
        s_noticeId = ReadString(_fields, "noticeId");
        s_noticeTitle = ReadString(_fields, "noticeTitle");
        s_noticeBody = ReadString(_fields, "noticeBody");

        // 차단이 먼저다 — minSupported 와 latest 를 둘 다 넘겼으면 유저에게는 차단만 말한다.
        if (TryReadVersion(_fields, "minSupported", out AppSemVer t_min) && s_current < t_min)
        {
            s_latestText = TryReadVersion(_fields, "latest", out AppSemVer t_blockedLatest)
                ? t_blockedLatest.ToString()
                : string.Empty;
            Action = EAppVersionAction.Blocked;
            Debug.LogWarning($"[AppVersionGate] This version is no longer supported: current={s_current} min={t_min}");
            return;
        }

        if (TryReadVersion(_fields, "latest", out AppSemVer t_latest) && s_current < t_latest)
        {
            s_latestText = t_latest.ToString();
            Action = EAppVersionAction.Recommended;
            Debug.Log($"[AppVersionGate] A new version is available: current={s_current} latest={t_latest}");
            return;
        }

        s_latestText = string.Empty;
        Action = EAppVersionAction.Allowed;
    }

    // 플랫폼 분기를 전처리기가 아니라 런타임 값으로 하는 이유: 에디터에서 iOS 로 저작한 주소를 확인할 수 없으면
    // 오저작이 스토어 빌드에서만 드러난다.
    static string ReadStoreUrl(IDictionary<string, object> _fields)
    {
        bool t_isIos = Application.platform == RuntimePlatform.IPhonePlayer;
        string t_primary = ReadString(_fields, t_isIos ? "storeUrlIOS" : "storeUrlAndroid");
        if (!string.IsNullOrEmpty(t_primary)) return t_primary;

        // 한쪽만 저작한 초기 상태를 위한 보조 키. 그것도 없으면 버튼을 띄우지 않는다.
        return ReadString(_fields, "storeUrl");
    }

    static bool TryReadVersion(IDictionary<string, object> _fields, string _key, out AppSemVer _version)
    {
        _version = default;
        string t_text = ReadString(_fields, _key);
        if (string.IsNullOrEmpty(t_text)) return false;

        if (AppSemVer.TryParse(t_text, out _version)) return true;

        // 저작 오류로 차단이 걸리는 것보다, 걸리지 않는 쪽이 안전하다.
        Debug.LogWarning($"[AppVersionGate] Could not read the version policy value '{_key}' = '{t_text}'. This axis is ignored.");
        return false;
    }

    static string ReadString(IDictionary<string, object> _fields, string _key)
        => _fields.TryGetValue(_key, out object t_value) && t_value is string t_text
            ? t_text.Trim()
            : string.Empty;
}

/// <summary>버전 게이트를 Firebase 수명주기에 얹는다. 등록 자리는 <c>GameManager</c> 의 모듈 목록이다.</summary>
public sealed class AppVersionFirebaseModule : IFirebaseModule
{
    public void Initialize(in FirebaseContext _context) => AppVersionGate.Initialize(in _context);

    // 미룬 쓰기가 없다 — 이 모듈은 읽기 전용이다.
    public void RetryPending() { }

    public UniTask FlushPendingAsync() => UniTask.CompletedTask;

    public void Shutdown() => AppVersionGate.Shutdown();
}
