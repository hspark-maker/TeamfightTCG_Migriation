using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

// 지갑 문서(envs/{env}/users/{uid}/wallet/current)의 클라우드 창구. 클라는 읽기만 한다(룰이 쓰기를 막는다).
//
// 세이브와 문서를 가른 이유가 곧 이 파일이 PlayerSaveCloud와 갈라진 이유다 — 재화만 쓰는 명령은
// 세이브 revision을 올리지 않으므로, 세이브의 "정확히 +1" 단언에 지갑을 태우면 그 자리에서 세션이 끊긴다.
static class WalletCloud
{
    const string DOCUMENT_SUFFIX = "/wallet/current";
    const string FIELD_REV = "rev";
    const string FIELD_BALANCES = "balances";

    static FirebaseContext s_context;
    static string s_envId = string.Empty;
    static bool s_hasDocument;

    /// <summary>채택한 지갑의 rev. 단조 증가만 보장된다.</summary>
    internal static long Rev { get; private set; }

    /// <summary>지갑 문서를 실제로 읽었는가. false면 아직 만들어지지 않았다(ensureWallet 대상이다).</summary>
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

    /// <summary>복구 화면의 재시도. 문서를 다시 읽을 것이므로 채택분만 되돌린다(PlayerSaveCloud.ResetForRetry와 같은 축).</summary>
    internal static void ResetForRetry()
    {
        // 서버 잔액을 처음부터 다시 세우는 자리다 — 임자가 사라진 낙관 델타가 남아 있으면 재기동 뒤 잔액이 부풀어 보인다.
        CurrencyManager.ClearPending();

        s_hasDocument = false;
        Rev = 0;
        LastError = string.Empty;
    }

    internal static void Shutdown()
    {
        CurrencyManager.ClearPending();

        s_context = default;
        s_envId = string.Empty;
        s_hasDocument = false;
        Rev = 0;
        LastError = string.Empty;
    }

    /// <summary>초기화 읽기. 문서가 없으면 true를 주고 <see cref="HasDocument"/>만 false로 남긴다 —
    /// "못 읽었다"와 "아직 없다"를 가려야 초기화가 ensureWallet을 부를지 실패로 접을지 정할 수 있다.</summary>
    // 던지지 않는다 — 세이브 읽기와 병렬로 띄워 두는 호출이라, 던지면 세이브 쪽이 먼저 실패했을 때 관측되지 않는다.
    internal static async UniTask<bool> TryReadAsync(string _userId)
    {
        LastError = string.Empty;

        if (!s_context.IsValid)
        {
            LastError = "Wallet context is not initialized.";
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
                LastError = "Wallet read timed out.";
                return false;
            }

            if (t_snapshot == null || !t_snapshot.Exists) return true;

            if (!TryReadPatch(t_snapshot, out WalletPatch t_patch))
            {
                LastError = "Wallet document is missing fields or has a broken type.";
                return false;
            }

            Adopt(t_patch);
            return true;
        }
        catch (Exception t_exception)
        {
            LastError = $"Wallet read failed ({t_exception.GetBaseException().Message}).";
            return false;
        }
    }

    /// <summary>서버가 돌려준 지갑을 채택한다. null은 "이 명령은 지갑을 안 썼다"라 무시다.</summary>
    // 절대 세션을 접지 않는다 — 지갑은 두 codebase가 쓰고, 장차 결제 웹훅처럼 클라가 모르는 정당한 쓰기가 생긴다.
    internal static void Adopt(WalletPatch _patch)
    {
        if (_patch?.Balances == null) return;

        // 늦게 도착한 응답. 잔액을 뒤로 되돌리지 않는다.
        if (s_hasDocument && _patch.Rev < Rev) return;

        Rev = _patch.Rev;
        s_hasDocument = true;
        CurrencyManager.Adopt(_patch.Balances);
    }

    /// <summary>지갑 문서 경로. 규칙 진단이 같은 문자열을 다시 조립하지 않게 여기서만 만든다.</summary>
    internal static string DocumentPath(string _userId)
    {
        return FirebaseRootPath.User(s_envId, _userId) + DOCUMENT_SUFFIX;
    }

    // 콘솔에서 숫자를 number(double)로 저장하면 int64 변환이 터진다 — 깨진 문서로 취급한다.
    static bool TryReadPatch(DocumentSnapshot _snapshot, out WalletPatch _patch)
    {
        _patch = null;

        try
        {
            if (!_snapshot.TryGetValue(FIELD_REV, out long t_rev)) return false;
            if (!_snapshot.TryGetValue(FIELD_BALANCES, out Dictionary<string, object> t_raw)) return false;

            var t_balances = new Dictionary<string, long>(t_raw.Count);
            foreach (KeyValuePair<string, object> t_pair in t_raw)
            {
                if (t_pair.Value == null) continue;
                t_balances[t_pair.Key] = Convert.ToInt64(t_pair.Value);
            }

            _patch = new WalletPatch { Rev = t_rev, Balances = t_balances };
            return true;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[WalletCloud] Wallet document could not be read ({t_exception.GetBaseException().Message}).");
            return false;
        }
    }
}
