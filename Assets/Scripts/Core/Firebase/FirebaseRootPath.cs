using System;

public static class FirebaseRootPath
{
    /// <summary>Firestore 데이터베이스 ID 단일 진실원. 기본 DB "(default)"가 아니라 명명 DB를 쓴다.</summary>
    public const string DatabaseId = "cardbattle";

    public const string EnvironmentCollection = "envs";

    public static string Environment(string _envId)
        => EnvironmentCollection + "/" + Segment(_envId, nameof(_envId));

    public static string User(string _envId, string _userId)
        => Environment(_envId) + "/users/" + Segment(_userId, nameof(_userId));

    static string Segment(string _value, string _parameterName)
    {
        if (string.IsNullOrWhiteSpace(_value))
            throw new ArgumentException("Firebase path segment is empty.", _parameterName);
        if (_value.IndexOf('/') >= 0)
            throw new ArgumentException("Firebase path segment cannot contain '/'.", _parameterName);
        return _value;
    }
}
