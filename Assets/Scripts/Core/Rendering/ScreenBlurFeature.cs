using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>화면 전체를 흐리게 만드는 URP 렌더러 기능. 롱프레스 카드 정보창이 차오를 때만 켜진다.
///
/// <para><b>강도는 <see cref="Strength"/> 정적 값 하나로만 조작한다.</b> 0이면 패스를 아예 큐에 넣지 않으므로
/// 평소 프레임에는 blit 비용조차 없다 — 렌더러 기능을 런타임에 켜고 끄려면 리플렉션이 필요해서,
/// "값이 0이면 스스로 빠지는" 쪽을 택했다.</para>
///
/// <para>흐려지는 대상은 <b>URP가 그리는 것까지</b>다. Screen Space - Overlay 캔버스(HUD·정보창)는
/// 이 패스 뒤에 그려지므로 선명하게 남는다 — 정보창 자체가 흐려지면 안 되니 의도한 순서다.</para></summary>
public class ScreenBlurFeature : ScriptableRendererFeature
{
    /// <summary>블러 강도(0~1). 0이면 패스를 걸지 않는다. 씬 전환·팝업 종료 시 반드시 0으로 되돌릴 것.</summary>
    public static float Strength;

    // 도메인 리로드를 끈 채 플레이를 반복하면 정적 값이 남아 시작부터 화면이 흐린 채로 뜬다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Strength = 0f;

    [SerializeField] Shader blurShader;

    [Tooltip("블러 버퍼 축소 배율. 클수록 싸고 더 뭉개진다")]
    [SerializeField, Range(1, 8)] int downsample = 4;

    [Tooltip("축소 버퍼 기준 탭 간격(픽셀). 클수록 더 넓게 번진다")]
    [SerializeField, Range(0.5f, 6f)] float blurRadius = 2f;

    [Tooltip("패스를 끼워 넣는 시점. 포스트프로세싱 결과까지 흐리려면 기본값 유지")]
    [SerializeField] RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

    Material  material;
    ScreenBlurPass pass;

    public override void Create()
    {
        // 화면 색을 **읽어서** 다시 쓰므로 백버퍼 직행 경로로는 동작하지 않는다 — 중간 텍스처를 요구한다.
        this.pass = new ScreenBlurPass
        {
            renderPassEvent            = this.injectionPoint,
            requiresIntermediateTexture = true,
        };
    }

    public override void AddRenderPasses(ScriptableRenderer _renderer, ref RenderingData _renderingData)
    {
        float t_strength = Mathf.Clamp01(Strength);
        if (t_strength <= 0.001f) return;

        if (this.blurShader == null) return;
        if (this.material == null) this.material = CoreUtils.CreateEngineMaterial(this.blurShader);

        this.pass.Setup(this.material, this.downsample, this.blurRadius, t_strength);
        _renderer.EnqueuePass(this.pass);
    }

    protected override void Dispose(bool _disposing)
    {
        CoreUtils.Destroy(this.material);
        this.material = null;
    }

    /// <summary>축소 → 가로 블러 → (원본 위에)세로 블러 합성. 분리형 가우시안이라 패스가 셋이다.</summary>
    class ScreenBlurPass : ScriptableRenderPass
    {
        static readonly int s_BlurStep      = Shader.PropertyToID("_BlurStep");
        static readonly int s_BlurStrength  = Shader.PropertyToID("_BlurStrength");
        static readonly int s_BlitTexture   = Shader.PropertyToID("_BlitTexture");
        static readonly int s_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");

        static readonly MaterialPropertyBlock s_Props = new MaterialPropertyBlock();

        Material material;
        int      downsample;
        float    radius;
        float    strength;

        public void Setup(Material _material, int _downsample, float _radius, float _strength)
        {
            this.material   = _material;
            this.downsample = Mathf.Max(1, _downsample);
            this.radius     = _radius;
            this.strength   = _strength;
        }

        class PassData
        {
            public Material      material;
            public TextureHandle source;
        }

        public override void RecordRenderGraph(RenderGraph _graph, ContextContainer _frameData)
        {
            var t_resources = _frameData.Get<UniversalResourceData>();
            var t_cameraData = _frameData.Get<UniversalCameraData>();

            // 게임 카메라만 — 씬 뷰/머티리얼 프리뷰까지 흐려지면 에디터 작업이 불가능해진다.
            if (t_cameraData.cameraType != CameraType.Game) return;
            if (!t_resources.activeColorTexture.IsValid()) return;

            TextureDesc t_desc = _graph.GetTextureDesc(t_resources.activeColorTexture);
            t_desc.width           = Mathf.Max(1, t_desc.width  / this.downsample);
            t_desc.height          = Mathf.Max(1, t_desc.height / this.downsample);
            t_desc.msaaSamples     = MSAASamples.None;
            t_desc.depthBufferBits = DepthBits.None;
            t_desc.clearBuffer     = false;
            t_desc.filterMode      = FilterMode.Bilinear;
            t_desc.wrapMode        = TextureWrapMode.Clamp;

            t_desc.name = "_ScreenBlurDown";
            TextureHandle t_down = _graph.CreateTexture(t_desc);
            t_desc.name = "_ScreenBlurH";
            TextureHandle t_horizontal = _graph.CreateTexture(t_desc);

            // 탭 간격은 **축소 버퍼** 크기 기준이다 — 화면 해상도로 계산하면 기기마다 번짐 폭이 달라진다.
            this.material.SetVector(s_BlurStep, new Vector4(this.radius / t_desc.width, this.radius / t_desc.height, 0f, 0f));
            this.material.SetFloat(s_BlurStrength, this.strength);

            _graph.AddBlitPass(t_resources.activeColorTexture, t_down, Vector2.one, Vector2.zero,
                passName: "ScreenBlur Downsample");

            _graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(t_down, t_horizontal, this.material, 0),
                passName: "ScreenBlur Horizontal");

            // 세로 패스는 화면 원본 위에 알파 블렌드로 얹는다(강도 램프). AddBlitPass는 대상을 Write로만 잡아
            // 기존 색을 버릴 수 있으므로 여기만 직접 래스터 패스를 만든다.
            using (var t_builder = _graph.AddRasterRenderPass<PassData>("ScreenBlur Vertical", out PassData t_data))
            {
                t_data.material = this.material;
                t_data.source   = t_horizontal;

                t_builder.UseTexture(t_horizontal, AccessFlags.Read);
                t_builder.SetRenderAttachment(t_resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                t_builder.SetRenderFunc((PassData _data, RasterGraphContext _context) =>
                {
                    s_Props.Clear();
                    s_Props.SetTexture(s_BlitTexture, _data.source);
                    s_Props.SetVector(s_BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
                    _context.cmd.DrawProcedural(Matrix4x4.identity, _data.material, 1, MeshTopology.Triangles, 3, 1, s_Props);
                });
            }
        }
    }
}
