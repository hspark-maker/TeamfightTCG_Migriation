using UnityEditor;
using UnityEngine;

/// <summary>
/// 흐름(Flow) 시너지의 파도 프리팹을 코드로 굽는다. 메뉴: Tools/VFX/Generate Flow Wave Prefab.
///
/// 왜 코드로 굽나: ParticleSystem은 직렬화 덩치가 커서(시스템 하나가 1,000줄 이상) 손으로 YAML을
/// 고치면 어느 모듈이 어긋났는지 눈으로 확인이 안 된다. 여기서는 파라미터가 전부 한 화면에 보이고,
/// 값 바꿔 메뉴만 다시 실행하면 같은 경로에 덮어써진다.
///
/// 연출: 화면 오른쪽 밖에서 파도 리본(WaveMesh)이 여러 겹 밀려와 왼쪽으로 흘러 나간다.
/// 겹마다 속도·Y오프셋·크기를 살짝 달리해 한 덩어리로 보이지 않게 한다.
///
/// 진행 방향은 **월드 -X 고정**이다(velocityOverLifetime, World 시뮬레이션).
/// FlowWave 프리팹을 회전시켜 방향을 바꾸던 예전 방식은 버린다 — 스폰 회전과 파티클 시뮬레이션
/// 공간이 서로 어긋나 "돌렸는데 그대로 간다"가 나온다. 방향의 단일 진실원은 여기 하나.
/// </summary>
public static class FlowWavePrefabBuilder
{
    const string MeshPath     = "Assets/Assets/Particle/시너지/WaveMesh.asset";
    const string MaterialPath = "Assets/PurchasedAssets/Epic Toon FX/Materials/Misc/Unsorted/wave_soft_AB.mat";
    const string OutputPath   = "Assets/Assets/Particle/시너지/FlowWave.prefab";

    // ── 연출 파라미터 ──
    const int   Layers      = 3;      // 겹칠 파도 수
    const float SpawnX      = 4.2f;   // 스폰 지점 X(오른쪽 밖). 필드 폭보다 커야 화면 밖에서 들어온다
    const float SpreadY     = 0.55f;  // 겹 사이 Y 간격
    const float Speed       = 7.5f;   // 진행 속도(월드/초). -X 방향
    const float SpeedJitter = 1.4f;   // 겹별 속도 편차
    const float Lifetime    = 1.3f;   // 한 겹이 살아 있는 시간
    const float StaggerStep = 0.11f;  // 겹 사이 발사 간격
    const float BaseSize    = 1.0f;   // 메시 배율(WaveMesh 자체가 길이 4)
    const float SizeJitter  = 0.25f;

    [MenuItem("Tools/VFX/Generate Flow Wave Prefab")]
    public static void Generate()
    {
        var t_mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (t_mesh == null)
        {
            Debug.LogError($"[FlowWave] 메시 없음: {MeshPath} — 먼저 Tools/VFX/Generate Wave Mesh 실행");
            return;
        }
        var t_mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (t_mat == null) Debug.LogWarning($"[FlowWave] 머터리얼 없음: {MaterialPath} — 기본 머터리얼로 굽는다");

        var t_root = new GameObject("FlowWave");
        try
        {
            for (int i = 0; i < Layers; i++)
                BuildLayer(t_root.transform, i, t_mesh, t_mat);

            PrefabUtility.SaveAsPrefabAsset(t_root, OutputPath, out bool t_ok);
            if (!t_ok) { Debug.LogError("[FlowWave] 프리팹 저장 실패"); return; }
        }
        finally
        {
            Object.DestroyImmediate(t_root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var t_saved = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
        Debug.Log($"[FlowWave] 생성: {OutputPath} (겹 {Layers})");
        Selection.activeObject = t_saved;
    }

    static void BuildLayer(Transform _parent, int _index, Mesh _mesh, Material _mat)
    {
        var t_go = new GameObject($"Wave{_index}");
        t_go.transform.SetParent(_parent, false);

        // 겹마다 Y를 어긋내 앞뒤로 밀려오는 물결처럼. 가운데를 기준으로 위아래로 벌린다.
        float t_offsetY = (_index - (Layers - 1) * 0.5f) * SpreadY;
        t_go.transform.localPosition = new Vector3(SpawnX, t_offsetY, 0f);

        var t_ps   = t_go.AddComponent<ParticleSystem>();
        var t_main = t_ps.main;
        t_main.duration            = Lifetime + (Layers * StaggerStep);
        t_main.loop                = false;
        t_main.playOnAwake         = true;
        t_main.startLifetime       = Lifetime;
        t_main.startSpeed          = 0f;                       // 이동은 전부 velocityOverLifetime이 담당
        t_main.startSize           = BaseSize + Random01(_index) * SizeJitter;
        t_main.startRotation       = 0f;
        t_main.startColor          = LayerColor(_index);
        t_main.simulationSpace     = ParticleSystemSimulationSpace.World;   // 카드가 움직여도 파도는 필드에 남는다
        t_main.maxParticles        = 4;
        t_main.scalingMode         = ParticleSystemScalingMode.Hierarchy;

        // 겹당 딱 1개. 여러 개를 뿌리면 같은 메시가 겹쳐 뭉개진다 — 겹은 오브젝트로 나눈다.
        var t_emission = t_ps.emission;
        t_emission.enabled       = true;
        t_emission.rateOverTime  = 0f;
        t_emission.SetBursts(new[] { new ParticleSystem.Burst(_index * StaggerStep, 1) });

        var t_shape = t_ps.shape;
        t_shape.enabled = false;   // 스폰 지점은 이 오브젝트 위치 하나로 충분

        // 진행: 월드 -X. 겹마다 속도를 달리해 서로 어긋나며 흐른다.
        var t_vel = t_ps.velocityOverLifetime;
        t_vel.enabled = true;
        t_vel.space   = ParticleSystemSimulationSpace.World;
        float t_speed = Speed + ((Random01(_index + 7) - 0.5f) * 2f * SpeedJitter);
        t_vel.x = new ParticleSystem.MinMaxCurve(-t_speed);
        t_vel.y = new ParticleSystem.MinMaxCurve(0f);
        t_vel.z = new ParticleSystem.MinMaxCurve(0f);

        // 알파 페이드 인/아웃 — 화면 가장자리에서 툭 나타나고 툭 사라지지 않게.
        var t_col = t_ps.colorOverLifetime;
        t_col.enabled = true;
        var t_grad = new Gradient();
        t_grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f,    0f),
                new GradientAlphaKey(1f,    0.18f),
                new GradientAlphaKey(0.85f, 0.7f),
                new GradientAlphaKey(0f,    1f),
            });
        t_col.color = new ParticleSystem.MinMaxGradient(t_grad);

        // 진행하며 살짝 늘어났다 줄어든다(파도가 밀리는 느낌).
        var t_size = t_ps.sizeOverLifetime;
        t_size.enabled = true;
        var t_curve = new AnimationCurve(
            new Keyframe(0f, 0.82f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.9f));
        t_size.size = new ParticleSystem.MinMaxCurve(1f, t_curve);

        var t_r = t_go.GetComponent<ParticleSystemRenderer>();
        t_r.renderMode      = ParticleSystemRenderMode.Mesh;
        t_r.mesh            = _mesh;
        t_r.alignment       = ParticleSystemRenderSpace.World;   // 메시가 XY 평면 기준이라 뷰 정렬을 끈다
        t_r.sortingLayerID  = 0;                                 // 실제 정렬은 BattleVfx.ApplySorting이 스폰 때 덮어쓴다
        t_r.sortingOrder    = 0;
        if (_mat != null) t_r.sharedMaterial = _mat;
    }

    /// <summary>겹별 색. 뒤쪽 겹일수록 옅고 푸르게 — 깊이감. 알파는 colorOverLifetime이 다시 곱한다.</summary>
    static Color LayerColor(int _index)
    {
        float t_t = Layers <= 1 ? 0f : (float)_index / (Layers - 1);
        return Color.Lerp(new Color(0.72f, 0.92f, 1f, 0.95f),
                          new Color(0.35f, 0.62f, 0.95f, 0.55f), t_t);
    }

    /// <summary>인덱스 기반 고정 난수(0~1). Random을 쓰면 구울 때마다 프리팹이 달라져 diff가 지저분해진다.</summary>
    static float Random01(int _seed)
    {
        float t_v = Mathf.Sin(_seed * 127.1f + 311.7f) * 43758.5453f;
        return t_v - Mathf.Floor(t_v);
    }
}
