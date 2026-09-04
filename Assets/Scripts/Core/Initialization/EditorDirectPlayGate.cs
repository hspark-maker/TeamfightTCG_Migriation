#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>로비 씬에서 바로 Play 해도 초기화가 끝까지 가도록 여는 <b>에디터 전용</b> 게이트.
///
/// <para>초기화 자체는 로비에서도 돈다 — <c>Initialize.prefab</c> 사본이 StartScene 과 LobbyScene 양쪽에 있고
/// <see cref="InitializationRunner.InitClaimed"/> 가 둘 중 하나만 선점하게 한다. 빠지는 것은 화면이다:
/// <c>LoadingCover</c> 는 StartScene 에만 저작돼 있어서, 로비에서 바로 시작하면 <see cref="LoadingCoverView"/>
/// 인스턴스가 없다. 그러면 로그인 관문(그 프리팹 안의 LoginEmailPanel)도, 초기화 실패 복구 화면도 뜰 자리가 없어
/// 진행이 조용히 멈춘다 — "접속이 안 된다"로 보이는 것이 이 상태다.</para>
///
/// <para>그래서 커버가 없을 때만 하나 세워 준다. StartScene 에서 시작하면 이미 있으므로 아무 일도 하지 않고,
/// 초기화 러너가 없는 테스트 씬(전투·연출 단독 씬)도 건드리지 않는다 — 그 씬들은 초기화 없이 도는 것이 사양이다.</para>
///
/// <para>빌드에는 통째로 들어가지 않는다(<c>#if UNITY_EDITOR</c>). 출시본의 진입점은 StartScene 하나다.</para></summary>
static class EditorDirectPlayGate
{
    const string LOADING_COVER_PATH = "Assets/Assets/Prefabs/UI/Common/LoadingCover.prefab";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Ensure()
    {
        // 이미 저작된 커버가 있다 — StartScene 에서 시작한 정상 경로다.
        if (Object.FindAnyObjectByType<LoadingCoverView>(FindObjectsInactive.Include) != null) return;

        // 초기화가 아예 돌지 않는 씬은 대상이 아니다. 커버만 띄우면 걷어 줄 주체가 없어 화면이 덮인 채 남는다.
        if (Object.FindAnyObjectByType<InitializationRunner>(FindObjectsInactive.Include) == null) return;

        var t_prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LOADING_COVER_PATH);
        if (t_prefab == null)
        {
            Debug.LogError($"[EditorDirectPlayGate] Loading cover prefab is missing at {LOADING_COVER_PATH} "
                         + "— direct play from this scene cannot show the sign-in or recovery screen.");
            return;
        }

        GameObject t_cover = Object.Instantiate(t_prefab);
        t_cover.name = t_prefab.name;   // (Clone) 접미사를 지운다 — 계층에서 저작본과 같아 보여야 헷갈리지 않는다

        Debug.Log("[EditorDirectPlayGate] Spawned the loading cover for direct play "
                + "— this scene has no authored one (editor only).");
    }
}
#endif
