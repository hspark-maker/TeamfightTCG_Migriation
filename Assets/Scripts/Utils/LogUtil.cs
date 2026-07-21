using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class LogUtil
{
    public static void Log(string _message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log(_message);
#endif
    }

    public static void LogWarning(string _message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning(_message);
#endif
    }

    public static void LogError(string _message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogError(_message);
#endif
    }
}
