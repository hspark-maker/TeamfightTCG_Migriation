using UnityEditor;
using UnityEngine;

/// <summary>
/// 파도 리본 메시를 절차 생성해 .asset으로 굽는다. 메뉴: Tools/VFX/Generate Wave Mesh.
///
/// 형태: X축(+)을 따라 흐르는 띠. 중심선이 사인으로 출렁이고, 양 끝으로 갈수록 두께가 0으로 수렴한다
/// (엔벨로프). 그래서 잘린 사각형이 아니라 "밀려왔다 사라지는 파도"로 읽힌다.
///
/// XY 평면에 눕혀 굽는 이유: 이 프로젝트 전투는 카메라가 +Z를 보는 2D 평면이라,
/// 파티클 Mesh 렌더러에 그대로 물리면 별도 회전 없이 정면으로 보인다.
/// 진행 방향은 메시가 아니라 **스폰 회전**이 정한다(FlowWave 프리팹이 Y축 -90°로 우→좌).
///
/// UV는 u=진행방향 0~1, v=두께방향 0~1. 텍스처를 u로 흘리면(머터리얼 offset 애니) 물결이 흐른다.
/// </summary>
public static class WaveMeshBuilder
{
    const string OutputPath = "Assets/Assets/Particle/시너지/WaveMesh.asset";

    // ── 형태 파라미터 (값 바꾸고 메뉴 다시 실행하면 같은 에셋을 덮어쓴다) ──
    const int   Segments   = 96;     // 진행방향 분할 수. 낮으면 각지고 높으면 정점만 늘어난다
    const float Length     = 4.0f;   // 전체 길이(월드 단위, 스폰 스케일로 다시 조절 가능)
    const float Thickness  = 0.42f;  // 가장 두꺼운 지점의 반두께
    const float Amplitude  = 0.30f;  // 중심선 출렁임 폭
    const float Waves      = 1.6f;   // 길이당 물결 수(정수 아님 = 양 끝이 대칭이 아니라 더 자연스럽다)
    const float Phase      = 0.35f;  // 물결 위상(0~1). 시작점이 마루/골 중 어디냐
    const float TaperPower = 0.7f;   // 엔벨로프 날카로움. 1=사인 그대로, 낮을수록 가운데가 넓고 끝이 급하게 죈다
    const float CrestSkew  = 0.25f;  // 마루 쏠림. >0이면 윗변이 진행 방향으로 밀려 파도가 앞으로 넘어간다

    [MenuItem("Tools/VFX/Generate Wave Mesh")]
    public static void Generate()
    {
        Mesh t_mesh = Build();

        // 기존 에셋이 있으면 **내용만 갈아끼운다** — 새로 만들면 guid가 바뀌어
        // 이미 물려 있는 머터리얼/파티클 참조가 전부 끊긴다.
        var t_existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutputPath);
        if (t_existing != null)
        {
            t_existing.Clear();
            CopyInto(t_mesh, t_existing);
            EditorUtility.SetDirty(t_existing);
            AssetDatabase.SaveAssets();
            Object.DestroyImmediate(t_mesh);
            Debug.Log($"[WaveMesh] 갱신: {OutputPath} (verts {t_existing.vertexCount})");
            Selection.activeObject = t_existing;
            return;
        }

        string t_dir = System.IO.Path.GetDirectoryName(OutputPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(t_dir))
            System.IO.Directory.CreateDirectory(t_dir);

        AssetDatabase.CreateAsset(t_mesh, OutputPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[WaveMesh] 생성: {OutputPath} (verts {t_mesh.vertexCount})");
        Selection.activeObject = t_mesh;
    }

    static Mesh Build()
    {
        int t_cols = Mathf.Max(2, Segments) + 1;

        var t_verts  = new Vector3[t_cols * 2];
        var t_uvs    = new Vector2[t_cols * 2];
        var t_norms  = new Vector3[t_cols * 2];
        var t_colors = new Color[t_cols * 2];
        var t_tris   = new int[(t_cols - 1) * 6];

        for (int i = 0; i < t_cols; i++)
        {
            float t = (float)i / (t_cols - 1);          // 진행방향 0~1
            float t_x = (t - 0.5f) * Length;            // 중앙 정렬 — 스폰 지점이 파도 한가운데가 된다

            // 엔벨로프: 양 끝 0, 가운데 1. 두께와 진폭에 함께 곱해 끝이 뾰족하게 사라진다.
            // Max(0)이 필수 — Sin(π·1)은 부동소수 오차로 -8.7e-8이 나오고, 음수를 소수 지수로 Pow하면
            // NaN이 되어 메시 bounds가 통째로 깨진다(정점은 만들어지는데 화면에 아무것도 안 나온다).
            float t_env = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), TaperPower);

            float t_ang    = ((t * Waves) + Phase) * Mathf.PI * 2f;
            float t_center = Mathf.Sin(t_ang) * Amplitude * t_env;
            float t_half   = Thickness * t_env;

            // 마루 쏠림: 윗변만 진행 방향 기울기를 더해 파도가 앞으로 말린 실루엣이 된다.
            float t_skew = Mathf.Cos(t_ang) * CrestSkew * t_env;

            int t_b = i * 2;       // 아랫변
            int t_t = i * 2 + 1;   // 윗변

            t_verts[t_b] = new Vector3(t_x,          t_center - t_half, 0f);
            t_verts[t_t] = new Vector3(t_x + t_skew, t_center + t_half, 0f);

            t_uvs[t_b] = new Vector2(t, 0f);
            t_uvs[t_t] = new Vector2(t, 1f);

            // 카메라가 +Z를 보므로 정면 법선은 -Z.
            t_norms[t_b] = t_norms[t_t] = new Vector3(0f, 0f, -1f);

            // 정점 컬러 알파로 끝을 페이드. 파티클/애드 머터리얼이 버텍스 컬러를 쓰면
            // 텍스처 없이도 끝단이 잘리지 않는다(안 쓰면 무해).
            float t_a = Mathf.Clamp01(t_env);
            t_colors[t_b] = t_colors[t_t] = new Color(1f, 1f, 1f, t_a);
        }

        for (int i = 0; i < t_cols - 1; i++)
        {
            int t_o = i * 6;
            int t_b0 = i * 2, t_t0 = i * 2 + 1, t_b1 = (i + 1) * 2, t_t1 = (i + 1) * 2 + 1;

            // -Z를 향한 면이 앞면이 되도록 감는다(시계방향 in XY, -Z 노멀 기준 CCW).
            t_tris[t_o + 0] = t_b0; t_tris[t_o + 1] = t_b1; t_tris[t_o + 2] = t_t0;
            t_tris[t_o + 3] = t_t0; t_tris[t_o + 4] = t_b1; t_tris[t_o + 5] = t_t1;
        }

        var t_mesh = new Mesh { name = "WaveMesh" };
        t_mesh.SetVertices(t_verts);
        t_mesh.SetUVs(0, t_uvs);
        t_mesh.SetNormals(t_norms);
        t_mesh.SetColors(t_colors);
        t_mesh.SetTriangles(t_tris, 0);
        t_mesh.RecalculateTangents();
        t_mesh.RecalculateBounds();
        return t_mesh;
    }

    static void CopyInto(Mesh _src, Mesh _dst)
    {
        _dst.name = "WaveMesh";
        _dst.SetVertices(_src.vertices);
        _dst.SetUVs(0, new System.Collections.Generic.List<Vector2>(_src.uv));
        _dst.SetNormals(_src.normals);
        _dst.SetColors(_src.colors);
        _dst.SetTriangles(_src.triangles, 0);
        _dst.RecalculateTangents();
        _dst.RecalculateBounds();
    }
}
