using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

// 튜토리얼 무료 한 방 문서(envs/{env}/users/{uid}/grants/current)의 클라우드 창구. 클라는 읽기만 한다(소진은 서버가 찍는다).
//
// 세이브와 문서를 가른 이유는 지갑과 같다 — 소진 표식은 강화 트랜잭션 안에서 서버가 쓰므로
// 세이브 revision을 올리지 않고, 세이브의 "정확히 +1" 단언에 태우면 그 자리에서 세션이 끊긴다.
// 지갑과 다른 점은 rev 같은 단조 카운터가 없다는 것이다.
//
// 표식을 끄는 경로는 QA 되감기(devResetSave가 문서를 통째로 지운다) 하나뿐이고, 그 자리는
// 초기화 중이라 읽기가 이미 끝난 뒤다 — 그래서 늦은 응답이 꺼진 표식을 되켜는 경합은 생기지 않는다.
static class TutorialGrantsCloud
{
    const string DOCUMENT_SUFFIX = "/grants/current";
    const string FIELD_ENHANCE_CARD = "enhanceCard";
    const string FIELD_ENHANCE_KEYWORD = "enhanceKeyword";

    static FirebaseContext s_context;
    static string s_envId = string.Empty;
    static bool s_hasDocument;

    /// <summary>표식이 실제로 바뀐 경우에만 발화한다. 안내가 서 있는 스텝을 다시 판정시키는 자리다.</summary>
    internal static event Action OnChanged;

    /// <summary>카드 강화의 무료 한 방을 서버가 이미 소진했는가.</summary>
    internal static bool EnhanceCardSpent { get; private set; }

    /// <summary>키워드 강화의 무료 한 방을 서버가 이미 소진했는가.</summary>
    internal static bool EnhanceKeywordSpent { get; private set; }

    /// <summary>문서를 실제로 읽었는가. false면 아직 만들어지지 않았다(= 두 축 모두 미사용).</summary>
    internal static bool HasDocument => s_hasDocument;

    /// <summary>마지막 읽기 실패 사유. 성공했으면 빈 문자열이다.</summary>
    internal static string LastError { get; private set; } = string.Empty;

    /// <summary>세션을 연다. 세이브 창구와 같은 FirebaseContext를 쓴다.</summary>
    internal static void Initialize(in FirebaseContext _context)
    {
        Shutdown();

        s_context = _context;
        s_envId = _context.IsValid ? _context.EnvId : string.Empty;
    }

    /// <summary>복구 화면의 재시도. 문서를 다시 읽을 것이므로 채택분만 되돌린다.</summary>
    internal static void ResetForRetry()
    {
        ClearMarks();
    }

    /// <summary>되감기가 서버 문서를 지운 직후. 부팅 읽기가 물어 둔 소진 표식을 걷는다 —
    /// 남겨 두면 무료 한 방이 부활했는데도 안내 브리지의 통과 판정이 강화 스텝을 그냥 넘겨
    /// 그 챕터를 다시 볼 수 없다.</summary>
    // 통지하지 않는 것은 ClearMarks와 같은 이유이고, 이 자리는 초기화 중이라 아직 구독자가 없다.
    internal static void ResetForRewind()
    {
        ClearMarks();
    }

    internal static void Shutdown()
    {
        ClearMarks();

        s_context = default;
        s_envId = string.Empty;
    }

    /// <summary>초기화 읽기. 문서가 없으면 true를 주고 표식은 미사용으로 남긴다 —
    /// "못 읽었다"와 "아직 없다"를 가려야 호출부가 경고를 낼지 정상으로 볼지 정할 수 있다.</summary>
    // 던지지 않는다 — 세이브 읽기와 병렬로 띄워 두는 호출이라, 던지면 세이브 쪽이 먼저 실패했을 때 관측되지 않는다.
    internal static async UniTask<bool> TryReadAsync(string _userId)
    {
        LastError = string.Empty;

        if (!s_context.IsValid)
        {
            LastError = "Tutorial grants context is not initialized.";
            return false;
        }

        try
        {
            Task<DocumentSnapshot> t_readTask =
                s_context.GetFirestore().Document(DocumentPath(_userId)).GetSnapshotAsync(Source.Server);
            (bool t_hasResult, DocumentSnapshot t_snapshot) = await UniTask.WhenAny(
                t_readTask.AsUniTask(),
                UniTask.Delay(FirebaseTimeouts.AuthAndReadMilliseconds, DelayType.Realtime));

            if (!t_hasResult)
            {
                LastError = "Tutorial grants read timed out.";
                return false;
            }

            // 문서 부재는 두 축 모두 미사용이다. 되감기가 문서를 지울 수 있게 된 뒤로는 여기서
            // 표식을 걷어 줘야 세션 도중 재읽기가 꺼진 상태를 실제로 반영한다(안 걷으면 옛 값이 남는다).
            if (t_snapshot == null || !t_snapshot.Exists)
            {
                s_hasDocument = false;
                Adopt(false, false);
                return true;
            }

            // 깨진 필드는 미사용으로 선다 — 서버 readGrants와 같은 관대함이다.
            // 못 읽었다고 무료 한 방을 닫으면 낼 돈이 없는 신규 계정이 그 자리에서 멈춘다.
            s_hasDocument = true;
            Adopt(ReadFlag(t_snapshot, FIELD_ENHANCE_CARD), ReadFlag(t_snapshot, FIELD_ENHANCE_KEYWORD));
            return true;
        }
        catch (Exception t_exception)
        {
            LastError = $"Tutorial grants read failed ({t_exception.GetBaseException().Message}).";
            return false;
        }
    }

    /// <summary>세션 도중 표식을 다시 읽는다. 무료를 청구했는데 서버가 거절한 자리처럼
    /// "서버는 이미 소진으로 안다"가 드러난 순간에만 부른다(왕복이 그때만 늘어난다).</summary>
    internal static UniTask<bool> RefreshAsync(string _userId) => TryReadAsync(_userId);

    /// <summary>서명된 계정으로 표식을 다시 읽는다. 부르는 쪽(성장 매니저)이 uid를 알 필요가 없게 조달을 여기 가둔다 —
    /// 초기화 읽기와 같은 계정이어야 하므로 인증 창구가 유효한 계정을 쥐고 있을 때만 왕복한다.</summary>
    internal static UniTask<bool> RefreshAsync()
    {
        string t_userId = FirebaseAuthService.Instance.IsCurrentUserActive
            ? FirebaseAuthService.Instance.UserId
            : string.Empty;

        if (string.IsNullOrEmpty(t_userId))
        {
            LastError = "Tutorial grants refresh has no signed-in user.";
            return UniTask.FromResult(false);
        }

        return TryReadAsync(t_userId);
    }

    /// <summary>무료 한 방 문서 경로. 규칙 진단이 같은 문자열을 다시 조립하지 않게 여기서만 만든다.</summary>
    internal static string DocumentPath(string _userId)
    {
        return FirebaseRootPath.User(s_envId, _userId) + DOCUMENT_SUFFIX;
    }

    static void Adopt(bool _enhanceCard, bool _enhanceKeyword)
    {
        if (EnhanceCardSpent == _enhanceCard && EnhanceKeywordSpent == _enhanceKeyword) return;

        EnhanceCardSpent = _enhanceCard;
        EnhanceKeywordSpent = _enhanceKeyword;
        OnChanged?.Invoke();
    }

    static bool ReadFlag(DocumentSnapshot _snapshot, string _field)
    {
        try
        {
            return _snapshot.TryGetValue(_field, out bool t_spent) && t_spent;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning(
                $"[TutorialGrantsCloud] Field '{_field}' could not be read ({t_exception.GetBaseException().Message}).");
            return false;
        }
    }

    // 통지 없이 되돌린다 — 세션을 접는 자리라 구독자가 이 변화를 판정에 쓰면 안 된다.
    static void ClearMarks()
    {
        s_hasDocument = false;
        EnhanceCardSpent = false;
        EnhanceKeywordSpent = false;
        LastError = string.Empty;
    }
}
