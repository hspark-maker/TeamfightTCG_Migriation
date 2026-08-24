#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 1회용 검증 하네스 — 랭크별·챕터별 토너먼트 맵을 PNG로 뜬다. 돌린 뒤 파일째 지운다.
// 세이브는 메모리에서만 흔들고 원상복구한다(Save를 부르는 디버그 API는 쓰지 않는다).
public static class _TmpTournamentCapture
{
    const string LOBBY_PATH = "Assets/Assets/Prefabs/UI/LobbyUI/LobbyCanvas.prefab";
    const string CFG_PATH   = "Assets/SO/Tournament/TournamentConfig.asset";
    const string OUT_DIR    = "Temp/TournamentShots";

    const int SHOT_W = 720;
    const int SHOT_H = 1520;

    [MenuItem("Tools/Tournament/[1회] 맵 캡처")]
    public static void Capture()
    {
        UserSaveData t_data = DataSaveManager.Data;
        long t_points0 = t_data.rank.points;
        var t_cleared0 = new List<string>(t_data.tournament.clearedNodeIds);
        string t_pending0 = t_data.tournament.pendingRewardNodeId;

        System.IO.Directory.CreateDirectory(OUT_DIR);

        try
        {
            // 1장 앞부분을 깨 둔 상태로 본다 — 클리어 / 도전가능 / 순차미해금 / 보스가 한 화면에 선다.
            t_data.tournament.clearedNodeIds.Clear();
            t_data.tournament.clearedNodeIds.Add("node_01");
            t_data.tournament.clearedNodeIds.Add("node_02");
            t_data.tournament.pendingRewardNodeId = "";

            Shoot("A_bronze_ch1_boss", ERankGrade.Bronze, -1350f);
            Shoot("B_bronze_ch2_locked", ERankGrade.Bronze, -2500f);
            Shoot("C_platinum_ch2_open", ERankGrade.Platinum, -2500f);
            Shoot("D_platinum_ch4_boss", ERankGrade.Platinum, -8100f);
        }
        finally
        {
            t_data.rank.points = t_points0;
            t_data.tournament.clearedNodeIds.Clear();
            t_data.tournament.clearedNodeIds.AddRange(t_cleared0);
            t_data.tournament.pendingRewardNodeId = t_pending0;
        }

        Debug.Log("[Capture] 완료 — " + System.IO.Path.GetFullPath(OUT_DIR));
    }

    static void Shoot(string _name, ERankGrade _grade, float _scroll)   // _scroll = Content anchoredPosition.y(px)
    {
        SetGrade(_grade);

        Scene t_scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject t_lobby = null;
        GameObject t_camGo = null;
        RenderTexture t_rt = null;

        try
        {
            var t_prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LOBBY_PATH);
            t_lobby = (GameObject)PrefabUtility.InstantiatePrefab(t_prefab, t_scene);

            TournamentProgress.SetConfig(AssetDatabase.LoadAssetAtPath<TournamentConfig>(CFG_PATH));

            var t_map = t_lobby.GetComponentInChildren<TournamentMapOverlayView>(true);
            if (t_map == null) { Debug.LogError("[Capture] 맵을 찾지 못했다."); return; }

            IsolateBranch(t_map.transform, t_lobby.transform);
            t_map.Open();

            // 트윈이 에디트 모드에서 안 돌아 패널이 투명·축소인 채로 남는다 — 손으로 세운다.
            var t_group = t_map.GetComponent<CanvasGroup>();
            if (t_group != null) { t_group.alpha = 1f; t_group.blocksRaycasts = true; }
            t_map.transform.localScale = Vector3.one;
            t_map.gameObject.SetActive(true);

            var t_scroll = t_map.GetComponentInChildren<ScrollRect>(true);
            Canvas.ForceUpdateCanvases();

            var t_canvas = t_lobby.GetComponent<Canvas>();
            if (t_canvas == null) t_canvas = t_lobby.GetComponentInChildren<Canvas>(true);

            t_camGo = new GameObject("_ShotCam", typeof(Camera));
            var t_cam = t_camGo.GetComponent<Camera>();
            t_cam.orthographic = true;
            t_cam.orthographicSize = 5f;
            t_cam.transform.position = new Vector3(0f, 0f, -100f);
            t_cam.transform.rotation = Quaternion.identity;
            t_cam.clearFlags = CameraClearFlags.SolidColor;
            t_cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
            t_cam.nearClipPlane = 0.01f;
            t_cam.farClipPlane = 5000f;
            t_cam.cullingMask = ~0;

            t_rt = new RenderTexture(SHOT_W, SHOT_H, 24);
            t_cam.targetTexture = t_rt;

            t_canvas.renderMode = RenderMode.ScreenSpaceCamera;
            t_canvas.worldCamera = t_cam;
            t_canvas.planeDistance = 50f;
            Canvas.ForceUpdateCanvases();

            var t_scaler = t_canvas.GetComponent<CanvasScaler>();
            if (t_scaler != null) t_scaler.enabled = false;
            if (t_scaler != null) t_scaler.enabled = true;
            Canvas.ForceUpdateCanvases();

            RectTransform t_content = t_scroll != null ? t_scroll.content : null;
            if (t_content != null) t_content.anchoredPosition = new Vector2(t_content.anchoredPosition.x, _scroll);
            Canvas.ForceUpdateCanvases();

            var t_states = new System.Text.StringBuilder();
            for (int t_i = 0; t_i < 8; t_i++)
                t_states.Append(t_i).Append(':').Append(TournamentProgress.StateOf(t_i))
                        .Append(TournamentProgress.IsRankLocked(t_i) ? "(R)" : "").Append(' ');
            Debug.Log($"[Capture:{_name}] 높이={t_content?.rect.height} 오프셋={_scroll} 상태 {t_states}");

            t_cam.Render();

            RenderTexture t_prev = RenderTexture.active;
            RenderTexture.active = t_rt;
            var t_tex = new Texture2D(SHOT_W, SHOT_H, TextureFormat.RGB24, false);
            t_tex.ReadPixels(new Rect(0, 0, SHOT_W, SHOT_H), 0, 0);
            t_tex.Apply();
            RenderTexture.active = t_prev;

            System.IO.File.WriteAllBytes($"{OUT_DIR}/{_name}.png", t_tex.EncodeToPNG());
            Object.DestroyImmediate(t_tex);
            Debug.Log($"[Capture] {_name} — 정점 {TournamentProgress.NodeCount}개 / 현재등급 {RankManager.CurrentGrade}");
        }
        finally
        {
            if (t_camGo != null) Object.DestroyImmediate(t_camGo);
            if (t_rt != null) { t_rt.Release(); Object.DestroyImmediate(t_rt); }
            EditorSceneManager.CloseScene(t_scene, true);
        }
    }

    // 조상 사슬 밖 형제를 전부 끈다 — 안 끄면 로비 UI가 앞에 겹쳐 로비만 찍힌다.
    static void IsolateBranch(Transform _target, Transform _root)
    {
        Transform t_node = _target;
        while (t_node != null && t_node != _root)
        {
            Transform t_parent = t_node.parent;
            if (t_parent == null) break;

            for (int t_i = 0; t_i < t_parent.childCount; t_i++)
            {
                Transform t_child = t_parent.GetChild(t_i);
                if (t_child != t_node) t_child.gameObject.SetActive(false);
            }

            t_node = t_parent;
        }
    }

    static void SetGrade(ERankGrade _grade)
    {
        int t_tierIndex = (int)_grade * 4;
        if (!RankManager.TryGetTier(t_tierIndex, out RankTier t_tier)) return;

        DataSaveManager.Data.rank.points = t_tier.RequiredPoints;
    }
}
#endif
