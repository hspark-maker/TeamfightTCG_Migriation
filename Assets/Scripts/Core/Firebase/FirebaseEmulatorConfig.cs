using System;

/// <summary>로컬 Firebase 에뮬레이터 주소 묶음. Functions·Firestore·Auth 셋은 함께 켜지고 함께 꺼진다 —
/// 하나만 로컬이면 uid와 세이브 문서가 서로 다른 백엔드를 가리켜 왕복 검증 자체가 성립하지 않는다.</summary>
public readonly struct FirebaseEmulatorConfig
{
    /// <summary>Functions 에뮬레이터 origin(스킴 포함). 꺼져 있으면 빈 문자열이다.</summary>
    public string FunctionsOrigin { get; }

    /// <summary>Firestore 에뮬레이터 host:port. 꺼져 있으면 빈 문자열이다.</summary>
    public string FirestoreHost { get; }

    /// <summary>Auth 에뮬레이터 호스트. 꺼져 있으면 빈 문자열이다.</summary>
    public string AuthHost { get; }

    /// <summary>Auth 에뮬레이터 포트.</summary>
    public int AuthPort { get; }

    /// <summary>켜기로 저작했지만 주소가 틀려 못 켠 이유. 정상이면 빈 문자열이다.</summary>
    public string Error { get; }

    /// <summary>세 주소가 모두 유효해 로컬로 향하는 상태다.</summary>
    public bool IsEnabled => !string.IsNullOrEmpty(this.FunctionsOrigin);

    /// <summary>켜기로 저작했는데 주소가 틀렸다. 끈 것이 아니라 못 켠 것이므로 부트를 진행시키면 안 된다.</summary>
    public bool IsMisconfigured => !string.IsNullOrEmpty(this.Error);

    /// <summary>꺼진 설정. 배포된 함수와 실서버 Firestore·Auth로 간다.</summary>
    public static FirebaseEmulatorConfig Disabled => default;

    FirebaseEmulatorConfig(string _functionsOrigin, string _firestoreHost, string _authHost, int _authPort, string _error)
    {
        this.FunctionsOrigin = _functionsOrigin;
        this.FirestoreHost = _firestoreHost;
        this.AuthHost = _authHost;
        this.AuthPort = _authPort;
        this.Error = _error;
    }

    /// <summary>세 주소를 검증해 묶는다. 하나라도 비었거나 형식이 틀리면 이유를 담은 실패 설정을 돌려준다.</summary>
    public static FirebaseEmulatorConfig Create(string _functionsOrigin, string _firestoreHost, string _authHost)
    {
        string t_functions = Trim(_functionsOrigin);
        string t_firestore = Trim(_firestoreHost);
        string t_auth = Trim(_authHost);

        if (t_functions.Length == 0 || t_firestore.Length == 0 || t_auth.Length == 0)
            return Invalid("ContentProfile의 functions/firestore/auth 주소 세 칸이 모두 채워져야 합니다.");

        // 스킴이 빠지면 Functions만 조용히 배포된 함수로 가고 나머지 둘만 로컬이 된다 — 이 타입이 막으려는 바로 그 상태다.
        if (!Uri.TryCreate(t_functions, UriKind.Absolute, out Uri t_uri) ||
            (t_uri.Scheme != Uri.UriSchemeHttp && t_uri.Scheme != Uri.UriSchemeHttps))
            return Invalid($"functions 주소는 http:// 또는 https:// 로 시작하는 절대 주소여야 합니다: {t_functions}");

        if (!TrySplitHostPort(t_firestore, out _, out _))
            return Invalid($"firestore 주소는 스킴 없는 host:port 형식이어야 합니다: {t_firestore}");

        if (!TrySplitHostPort(t_auth, out string t_authHost, out int t_authPort))
            return Invalid($"auth 주소는 스킴 없는 host:port 형식이어야 합니다: {t_auth}");

        return new FirebaseEmulatorConfig(t_functions, t_firestore, t_authHost, t_authPort, string.Empty);
    }

    /// <summary>부트 로그 한 줄. 이번 실행이 어느 백엔드에 붙었는지 여기서 읽힌다.</summary>
    public override string ToString()
    {
        if (this.IsMisconfigured) return $"MISCONFIGURED({this.Error})";

        return this.IsEnabled
            ? $"EMULATOR(functions={this.FunctionsOrigin}, firestore={this.FirestoreHost}, " +
              $"auth={this.AuthHost}:{this.AuthPort})"
            : "LIVE";
    }

    static FirebaseEmulatorConfig Invalid(string _reason)
    {
        return new FirebaseEmulatorConfig(string.Empty, string.Empty, string.Empty, 0, _reason);
    }

    static string Trim(string _value)
    {
        return string.IsNullOrWhiteSpace(_value) ? string.Empty : _value.Trim();
    }

    static bool TrySplitHostPort(string _value, out string _host, out int _port)
    {
        _host = string.Empty;
        _port = 0;

        int t_separator = _value.LastIndexOf(':');
        if (t_separator <= 0 || t_separator == _value.Length - 1) return false;

        string t_host = _value.Substring(0, t_separator);
        if (t_host.IndexOf('/') >= 0) return false;
        if (!int.TryParse(_value.Substring(t_separator + 1), out int t_port)) return false;
        if (t_port <= 0 || t_port > 65535) return false;

        _host = t_host;
        _port = t_port;
        return true;
    }
}
