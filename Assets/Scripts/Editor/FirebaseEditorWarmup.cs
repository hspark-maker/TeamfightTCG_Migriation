using Firebase;
using UnityEditor;
using UnityEngine;

/// <summary>에디터가 열리거나 스크립트가 재컴파일되는 시점에 Firebase 네이티브 SDK 적재를 미리 태운다.
///
/// <para>이 적재는 프로세스당 한 번이고 첫 회에만 수 초가 걸린다. Play 진입 이후로 미루면 그 비용이
/// 부트 인증 예산 안으로 들어와 에디터 첫 Play만 인증 타임아웃으로 죽는다(두 번째 Play부터 멀쩡한 이유).
/// 미리 데워 두면 첫 Play도 데워진 뒤와 같은 조건에서 시작한다.</para></summary>
[InitializeOnLoad]
static class FirebaseEditorWarmup
{
    static FirebaseEditorWarmup()
    {
        // 컴파일 직후 도메인이 아직 흔들리는 구간에서 네이티브를 건드리지 않게 한 틱 미룬다.
        EditorApplication.delayCall += Warmup;
    }

    static void Warmup()
    {
        if (FirebaseAuthService.DependenciesReady) return;

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(_task =>
        {
            if (_task.IsFaulted)
            {
                Debug.LogWarning($"[FirebaseEditorWarmup] SDK 사전 적재 실패: {_task.Exception?.GetBaseException().Message}");
                return;
            }

            Debug.Log($"[FirebaseEditorWarmup] SDK 사전 적재 완료: {_task.Result}");
        });
    }
}
