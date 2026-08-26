using System;

public static class FirebaseRootPath
{
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
