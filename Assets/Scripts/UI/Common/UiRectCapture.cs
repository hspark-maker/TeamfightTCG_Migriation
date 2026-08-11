using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// 화면에 떠 있는 UI 조각 하나를 <b>그 자리에서 들어내</b> RenderTexture 한 장으로 계속 떠 주는 임시 촬영대.
///
/// 왜 이런 짓이 필요한가: 로비 캔버스는 Screen Space - <b>Overlay</b>라 카메라가 찍을 수 없다.
/// Overlay 캔버스는 프레임버퍼에 직접 그려지므로 어떤 카메라의 targetTexture에도 담기지 않는다.
/// 그래서 촬영 동안만 대상 서브트리를 화면 밖 World Space 캔버스로 옮기고, 그 캔버스만 보는
/// 직교 카메라가 RenderTexture로 뱉는다. 옮겨간 동안에도 살아 있는 UI라 내용이 바뀌면 그대로 반영된다.
///
/// ⚠ 한 번 <see cref="Begin"/>했으면 반드시 <see cref="End"/>로 돌려놔야 한다 — 원래 부모·형제순서·레이어·
///   RectTransform 값을 전부 기억했다가 되돌리는 것이 이 클래스의 계약이다. 한 값이라도 빠지면 페이지가
///   어긋난 채 남는다.
/// ⚠ 반투명 픽셀은 알파가 한 번 더 곱해져(투명 배경에 SrcAlpha 블렌딩) 경계가 미세하게 어두워진다.
///   불투명한 판(카드 슬롯)에는 보이지 않는 수준이라 그대로 둔다.
/// </summary>
public class UiRectCapture
{
    const int MaxSize = 2048;

    GameObject    m_rig;
    Camera        m_cam;
    RectTransform m_canvasRect;
    RenderTexture m_rt;

    // 원래 자리 — 되돌리기용 전체 상태
    RectTransform m_source;
    Transform     m_parent;
    int           m_siblingIndex;
    readonly List<Transform> m_layerNodes = new List<Transform>();
    readonly List<int>       m_layers     = new List<int>();
    Vector2       m_anchorMin;
    Vector2       m_anchorMax;
    Vector2       m_pivot;
    Vector3       m_anchored;
    Vector2       m_sizeDelta;
    Vector3       m_scale;
    Quaternion    m_rotation;

    bool m_capturing;

    public Texture Texture => m_rt;

    public bool IsCapturing => m_capturing;

    /// <summary>_source를 촬영대로 옮기고 매 프레임 RenderTexture로 뜨기 시작한다. 실패하면 false —
    /// 호출부는 예전 연출로 물러나야 한다(레이아웃 전이라 rect가 0인 프레임이 실제로 있다).</summary>
    public bool Begin(RectTransform _source, int _layer)
    {
        if (m_capturing) return true;
        if (_source == null || _source.parent == null) return false;

        Vector2 t_size = _source.rect.size;
        if (t_size.x < 1f || t_size.y < 1f) return false;

        // 텍스처 해상도는 캔버스 배율까지 곱해야 화면과 같은 밀도가 된다(안 그러면 참조해상도 기준이라 흐려진다)
        var   t_canvas = _source.GetComponentInParent<Canvas>();
        float t_factor = t_canvas != null ? t_canvas.rootCanvas.scaleFactor : 1f;
        if (t_factor <= 0f) t_factor = 1f;

        int t_texW = Mathf.Clamp(Mathf.RoundToInt(t_size.x * t_factor), 8, MaxSize);
        int t_texH = Mathf.Clamp(Mathf.RoundToInt(t_size.y * t_factor), 8, MaxSize);

        this.EnsureRig();
        if (m_cam == null || m_canvasRect == null) return false;

        int t_layer = Mathf.Clamp(_layer, 0, 31);
        m_cam.cullingMask      = 1 << t_layer;
        m_cam.orthographicSize = t_size.y * 0.5f;
        m_cam.aspect           = t_size.x / t_size.y;

        m_canvasRect.sizeDelta       = t_size;
        m_canvasRect.gameObject.layer = t_layer;

        this.EnsureTexture(t_texW, t_texH);
        m_cam.targetTexture = m_rt;
        m_cam.enabled       = true;

        m_source       = _source;
        m_parent       = _source.parent;
        m_siblingIndex = _source.GetSiblingIndex();
        SaveLayers(_source);
        m_anchorMin    = _source.anchorMin;
        m_anchorMax    = _source.anchorMax;
        m_pivot        = _source.pivot;
        m_anchored     = _source.anchoredPosition3D;
        m_sizeDelta    = _source.sizeDelta;
        m_scale        = _source.localScale;
        m_rotation     = _source.localRotation;

        // 촬영대에서는 앵커를 가운데로 못 박고 크기를 원래 rect 그대로 준다 —
        // 늘림 앵커를 그대로 들고 오면 부모(촬영 캔버스)의 크기에 따라 rect가 달라진다.
        _source.SetParent(m_canvasRect, false);
        _source.anchorMin        = new Vector2(0.5f, 0.5f);
        _source.anchorMax        = new Vector2(0.5f, 0.5f);
        _source.pivot            = new Vector2(0.5f, 0.5f);
        _source.sizeDelta        = t_size;
        // z까지 0으로 못 박는다 — Vector2로만 밀면 원래 z가 남아 카메라의 near/far 밖으로 나갈 수 있다(그럼 빈 텍스처)
        _source.anchoredPosition3D = Vector3.zero;
        _source.localScale       = Vector3.one;
        _source.localRotation    = Quaternion.identity;
        SetLayerRecursive(_source, t_layer);

        // 같은 프레임에 찍히도록 강제로 한 번 정렬한다 — 안 하면 첫 프레임이 빈 텍스처다
        LayoutRebuilder.ForceRebuildLayoutImmediate(_source);
        Canvas.ForceUpdateCanvases();

        // 첫 그림을 지금 채운다. 카메라는 프레임 끝에 도는데, 그때까지 이 텍스처를 쓰는 쪽은
        // **한 번도 안 그려진 메모리**를 그대로 화면에 올린다 — 기기에 따라 빨강·분홍 쓰레기가 한 프레임 번쩍인다.
        this.ClearTexture();
        this.RenderNow();

        m_capturing = true;
        return true;
    }

    /// <summary>촬영을 끝내고 조각을 원래 자리로 되돌린다. 두 번 불러도 안전하다.</summary>
    public void End()
    {
        if (!m_capturing) return;
        m_capturing = false;

        if (m_source != null)
        {
            RestoreLayers();

            if (m_parent != null)
            {
                m_source.SetParent(m_parent, false);
                m_source.SetSiblingIndex(m_siblingIndex);
            }

            m_source.anchorMin        = m_anchorMin;
            m_source.anchorMax        = m_anchorMax;
            m_source.pivot            = m_pivot;
            m_source.sizeDelta        = m_sizeDelta;
            m_source.anchoredPosition3D = m_anchored;
            m_source.localScale       = m_scale;
            m_source.localRotation    = m_rotation;
        }

        m_source = null;
        m_parent = null;
        m_layerNodes.Clear();
        m_layers.Clear();

        if (m_cam != null)
        {
            m_cam.enabled       = false;
            m_cam.targetTexture = null;
        }

        this.ReleaseTexture();
    }

    /// <summary>촬영 상태를 복구하고 런타임에 만든 카메라·캔버스까지 폐기한다.</summary>
    public void Dispose()
    {
        this.End();

        if (m_rig != null) Object.Destroy(m_rig);
        m_rig        = null;
        m_cam        = null;
        m_canvasRect = null;
    }

    void EnsureRig()
    {
        if (m_rig != null) return;

        // 화면에 보이는 어떤 것과도 겹치지 않는 먼 자리. 촬영 카메라는 전용 레이어만 보므로
        // 여기 있는 것 말고는 아무것도 텍스처에 들어오지 않는다.
        m_rig = new GameObject("UiRectCapture_Rig");
        m_rig.transform.position = new Vector3(0f, 20000f, 0f);

        var t_camGo = new GameObject("Rig_Camera", typeof(Camera));
        t_camGo.transform.SetParent(m_rig.transform, false);
        t_camGo.transform.localPosition = new Vector3(0f, 0f, -100f);

        m_cam = t_camGo.GetComponent<Camera>();
        m_cam.orthographic        = true;
        m_cam.clearFlags          = CameraClearFlags.SolidColor;
        m_cam.backgroundColor     = new Color(0f, 0f, 0f, 0f);   // 배경은 투명 — 판 바깥이 검게 찍히면 안 된다
        m_cam.nearClipPlane       = 1f;
        m_cam.farClipPlane        = 1000f;
        m_cam.allowHDR            = false;
        m_cam.allowMSAA           = false;
        m_cam.useOcclusionCulling = false;
        m_cam.depth               = -100f;
        m_cam.enabled             = false;

        var t_canvasGo = new GameObject("Rig_Canvas", typeof(RectTransform), typeof(Canvas));
        var t_canvas   = t_canvasGo.GetComponent<Canvas>();
        t_canvas.renderMode  = RenderMode.WorldSpace;
        t_canvas.worldCamera = m_cam;
        // TMP·커스텀 UI 셰이더가 쓰는 채널까지 실어 보낸다 — 빠지면 글자 외곽선이 뭉갠다
        t_canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.Normal    | AdditionalCanvasShaderChannels.Tangent;

        m_canvasRect = (RectTransform)t_canvasGo.transform;
        m_canvasRect.SetParent(m_rig.transform, false);
        m_canvasRect.localPosition = Vector3.zero;
        m_canvasRect.localScale    = Vector3.one;
    }

    void EnsureTexture(int _w, int _h)
    {
        if (m_rt != null && m_rt.width == _w && m_rt.height == _h) return;

        this.ReleaseTexture();

        // 깊이 버퍼는 0이면 안 된다 — URP Render Graph는 Depth Stencil Format이 None인 타깃을 거부한다
        // ("Fake or uninitialized surface is not supported for attachment 0"). UI라 깊이를 쓰진 않지만 붙여 둔다.
        m_rt = new RenderTexture(_w, _h, 24, RenderTextureFormat.ARGB32)
        {
            name        = "UiRectCapture_RT",
            filterMode  = FilterMode.Bilinear,
            wrapMode    = TextureWrapMode.Clamp,
            antiAliasing = 1,
        };
        m_rt.Create();
    }

    // 새로 만든 텍스처의 내용은 정의되지 않는다(직전에 그 메모리를 쓰던 것이 그대로 남는다).
    // 크기가 같아 재사용할 때는 **직전 넘김의 마지막 그림**이 남아 있다 — 어느 쪽이든 한 프레임 번쩍인다.
    void ClearTexture()
    {
        if (m_rt == null) return;

        RenderTexture t_prev = RenderTexture.active;
        RenderTexture.active = m_rt;
        GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
        RenderTexture.active = t_prev;
    }

    // 프레임 끝을 기다리지 않고 지금 한 장 찍는다. SRP에서는 Camera.Render를 부르면 안 되므로 렌더 요청을 쓴다.
    void RenderNow()
    {
        if (m_cam == null || m_rt == null) return;

        var t_request = new RenderPipeline.StandardRequest { destination = m_rt };
        if (RenderPipeline.SupportsRenderRequest(m_cam, t_request))
            RenderPipeline.SubmitRenderRequest(m_cam, t_request);
    }

    void ReleaseTexture()
    {
        if (m_rt == null) return;

        m_rt.Release();
        Object.Destroy(m_rt);
        m_rt = null;
    }

    static void SetLayerRecursive(Transform _root, int _layer)
    {
        _root.gameObject.layer = _layer;
        for (int t_i = 0; t_i < _root.childCount; t_i++)
            SetLayerRecursive(_root.GetChild(t_i), _layer);
    }

    void SaveLayers(Transform _root)
    {
        m_layerNodes.Clear();
        m_layers.Clear();
        SaveLayersRecursive(_root);
    }

    void SaveLayersRecursive(Transform _root)
    {
        m_layerNodes.Add(_root);
        m_layers.Add(_root.gameObject.layer);
        for (int t_i = 0; t_i < _root.childCount; t_i++)
            SaveLayersRecursive(_root.GetChild(t_i));
    }

    void RestoreLayers()
    {
        // 촬영 중에 새로 생긴 자식(교체 지점의 RefreshPage가 Instantiate한 슬롯)은 기록에 없다.
        // 그대로 두면 촬영용 레이어를 영영 달고 산다 — 먼저 뿌리 레이어로 통일하고, 기록된 것만 제 값으로 되돌린다.
        if (m_source != null && m_layers.Count > 0) SetLayerRecursive(m_source, m_layers[0]);

        for (int t_i = 0; t_i < m_layerNodes.Count; t_i++)
        {
            Transform t_node = m_layerNodes[t_i];
            if (t_node != null) t_node.gameObject.layer = m_layers[t_i];
        }
    }
}
