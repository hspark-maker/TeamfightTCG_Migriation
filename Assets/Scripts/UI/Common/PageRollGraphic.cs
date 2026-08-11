using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 종이 한 장이 <b>실제로 말리는</b> 그림. 페이지를 통째로 뜬 RenderTexture(<see cref="UiRectCapture"/>)를
/// 세로축 원통에 감긴 격자 메시로 다시 그린다.
///
/// 왜 메시인가: 회전·스케일로는 자식이 딸린 UI를 휘게 할 수 없다. 그림을 텍스처 한 장으로 떠 오면
/// 그때부터는 "정점을 어디에 두느냐"의 문제라 진짜 말림(겹치는 통·안쪽 그늘·훑고 지나가는 광택)이 나온다.
///
/// 좌표 규약: 책등(hinge)에서 자유단까지 종이 위의 호 길이를 a라 하고, 자유단부터 <see cref="SetRoll"/>의
/// amount만큼이 이미 말려 올라간 상태로 본다. 종이는 늘어나지 않으므로 <b>UV는 x가 아니라 a</b>를 따른다.
/// Overlay 캔버스에는 원근이 없어 높이(z)는 미세한 세로 부풀림과 밝기로만 암시한다 —
/// <see cref="AlbumPageFlipView"/>의 전제와 같다.
/// </summary>
[AddComponentMenu("")]   // 손으로 붙이는 컴포넌트가 아니다 — 연출 코드가 런타임에 만든다
// ⚠ RequireComponent는 상속되지 않는다. Graphic이 달아 둔 것에 기대면 AddComponent 때 CanvasRenderer가
//   안 붙고, 그러면 Graphic.Rebuild가 통째로 건너뛰어(OnPopulateMesh조차 안 불린다) 아무것도 안 그려진다
//   — 화면엔 "연출이 사라진" 것으로 보인다. UnityEngine.UI.Image도 같은 이유로 자기 클래스에 다시 단다.
[RequireComponent(typeof(CanvasRenderer))]
public class PageRollGraphic : MaskableGraphic
{
    const int   MaxSegments  = 64;
    const float SpiralGrowth = 0.08f;   // 한 바퀴 감길 때마다 통이 굵어지는 비율(겹침이 정확히 포개지지 않게)

    Texture m_texture;
    float   m_amount;              // 0 = 평평, 1 = 책등까지 다 말림
    int     m_dir = 1;             // +1이면 왼쪽이 책등
    float   m_radiusRatio = 0.13f;
    int     m_segments    = 28;
    float   m_bulge       = 0.06f;
    float   m_backShade   = 0.32f;
    float   m_gloss       = 0.35f;
    float   m_edgeShade   = 0.45f;

    readonly UIVertex[] m_quad = new UIVertex[4];

    // 열 단위 값 — 조각을 깊이 순으로 다시 늘어놓아야 해서 한 번에 만들어 둔다
    float[] m_colX;
    float[] m_colUv;
    float[] m_colDepth;
    float[] m_colShade;
    float[] m_colBulge;
    int[]   m_order;

    public override Texture mainTexture => m_texture != null ? m_texture : Texture2D.whiteTexture;

    public void SetTexture(Texture _texture)
    {
        if (m_texture == _texture) return;

        m_texture = _texture;
        this.SetMaterialDirty();
        this.SetVerticesDirty();
    }

    /// <summary>말림의 모양 상수. 진행도(<see cref="SetRoll"/>)와 달리 넘김 한 번 동안 바뀌지 않는다.</summary>
    public void Configure(float _radiusRatio, int _segments, float _bulge, float _backShade, float _gloss)
    {
        m_radiusRatio = Mathf.Clamp(_radiusRatio, 0.02f, 0.5f);
        m_segments    = Mathf.Clamp(_segments, 4, MaxSegments);
        m_bulge       = Mathf.Clamp(_bulge, 0f, 0.3f);
        m_backShade   = Mathf.Clamp01(_backShade);
        m_gloss       = Mathf.Clamp01(_gloss);
        this.SetVerticesDirty();
    }

    /// <summary>말린 정도(0~1)와 책등 방향. 자세의 단일 진실원 — 바깥에서 정점을 따로 만지지 않는다.</summary>
    public void SetRoll(float _amount, int _dir)
    {
        float t_amount = Mathf.Clamp01(_amount);
        int   t_dir    = _dir >= 0 ? 1 : -1;
        if (Mathf.Approximately(t_amount, m_amount) && t_dir == m_dir) return;

        m_amount = t_amount;
        m_dir    = t_dir;
        this.SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper _vh)
    {
        _vh.Clear();

        var   t_rect = this.GetPixelAdjustedRect();
        float t_w    = t_rect.width;
        float t_h    = t_rect.height;
        if (t_w <= 0f || t_h <= 0f) return;

        float t_amount  = Mathf.Clamp01(m_amount);
        float t_rolled  = t_amount * t_w;                       // 이미 말려 올라간 종이 길이
        float t_hingeX  = m_dir >= 0 ? t_rect.xMin : t_rect.xMax;
        float t_u       = m_dir >= 0 ? 1f : -1f;                // 책등 → 자유단 방향
        float t_centerY = t_rect.center.y;

        // 다 말린 끝에서 한 박자만 지운다 — 통이 책등에서 툭 사라지는 것보다 낫고,
        // 교체 지점(진행도 0.5)에서 아래 깔린 새 장으로 매끄럽게 넘어간다
        float t_fade = 1f - Mathf.InverseLerp(0.88f, 1f, t_amount);

        if (t_rolled <= 0.5f)
        {
            // 평평 — 넘김 전과 픽셀 단위로 같은 그림이어야 한다(여기서 흔들리면 넘김 시작이 툭 튄다)
            this.AddStrip(_vh,
                t_hingeX, t_hingeX + t_u * t_w,
                m_dir >= 0 ? 0f : 1f, m_dir >= 0 ? 1f : 0f,
                1f, 1f, 1f, 1f, t_centerY, t_h, t_fade);
            return;
        }

        int   t_seg     = Mathf.Clamp(m_segments, 4, MaxSegments);
        float t_radius  = Mathf.Max(1f, t_w * m_radiusRatio);
        float t_contact = Mathf.Max(0f, t_w - t_rolled);        // 종이가 바닥을 떠나는 자리(책등에서 잰 거리)

        this.EnsureBuffers(t_seg + 2);

        int t_n = 0;
        if (t_contact > 0f)
            this.SetColumn(t_n++, 0f, t_contact, t_radius, t_hingeX, t_u, t_w);   // 아직 누워 있는 구간은 한 칸이면 충분하다

        for (int t_i = 0; t_i <= t_seg; t_i++)
        {
            float t_a = Mathf.Lerp(t_contact, t_w, (float)t_i / t_seg);
            this.SetColumn(t_n++, t_a, t_contact, t_radius, t_hingeX, t_u, t_w);
        }

        // 겹치는 통은 깊이 순으로 그려야 한다 — UI 메시에는 깊이 판정이 없어 나중에 그린 조각이 무조건 위다.
        int t_quads = t_n - 1;
        for (int t_i = 0; t_i < t_quads; t_i++) m_order[t_i] = t_i;
        for (int t_i = 1; t_i < t_quads; t_i++)
        {
            int   t_key   = m_order[t_i];
            float t_depth = m_colDepth[t_key] + m_colDepth[t_key + 1];
            int   t_j     = t_i - 1;
            while (t_j >= 0 && m_colDepth[m_order[t_j]] + m_colDepth[m_order[t_j] + 1] > t_depth)
            {
                m_order[t_j + 1] = m_order[t_j];
                t_j--;
            }
            m_order[t_j + 1] = t_key;
        }

        for (int t_i = 0; t_i < t_quads; t_i++)
        {
            int t_c = m_order[t_i];
            this.AddStrip(_vh,
                m_colX[t_c], m_colX[t_c + 1],
                m_colUv[t_c], m_colUv[t_c + 1],
                m_colShade[t_c], m_colShade[t_c + 1],
                m_colBulge[t_c], m_colBulge[t_c + 1],
                t_centerY, t_h, t_fade);
        }
    }

    /// <summary>종이 위 호 길이 a에 있는 열의 화면 자리·UV·밝기·깊이를 정한다.</summary>
    void SetColumn(int _index, float _a, float _contact, float _radius, float _hingeX, float _u, float _width)
    {
        float t_theta = _a <= _contact ? 0f : (_a - _contact) / _radius;

        float t_x;
        float t_depth;
        if (t_theta <= 0f)
        {
            t_x     = _hingeX + _u * _a;
            t_depth = 0f;
        }
        else
        {
            // 감길수록 통이 굵어진다 — 반지름이 고정이면 두 바퀴째가 첫 바퀴에 정확히 포개져 말림이 멈춘 것처럼 보인다
            float t_r = _radius * (1f + SpiralGrowth * t_theta / (2f * Mathf.PI));
            t_x     = _hingeX + _u * (_contact + t_r * Mathf.Sin(t_theta));
            t_depth = t_r * (1f - Mathf.Cos(t_theta));
        }

        // 정면 1 → 옆 0 → 뒷면 -1. 빛은 정면에서 오는 것으로 두어 평평할 때 밝기가 정확히 1이 되게 한다.
        float t_facing = Mathf.Cos(t_theta);
        float t_shade  = t_facing >= 0f
            ? Mathf.Lerp(m_edgeShade, 1f, t_facing)
            : m_backShade * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(-t_facing));   // 말린 안쪽(종이 뒷면)

        // 비스듬히 선 부분만 반짝인다. 통이 굴러가면 이 띠도 같이 훑고 지나간다.
        if (t_facing > 0f)
        {
            float t_off = t_theta - 0.75f;
            t_shade += m_gloss * Mathf.Exp(-(t_off * t_off) / 0.18f);
        }

        m_colX[_index]     = t_x;
        m_colUv[_index]    = m_dir >= 0 ? _a / _width : 1f - _a / _width;
        m_colDepth[_index] = t_depth;
        m_colShade[_index] = t_shade;
        m_colBulge[_index] = 1f + m_bulge * (t_depth / (2f * Mathf.Max(1f, _radius)));
    }

    void AddStrip(VertexHelper _vh,
                  float _x0, float _x1, float _uv0, float _uv1,
                  float _shade0, float _shade1, float _bulge0, float _bulge1,
                  float _centerY, float _height, float _fade)
    {
        float t_half = _height * 0.5f;
        float t_top0 = _centerY + t_half * _bulge0;
        float t_bot0 = _centerY - t_half * _bulge0;
        float t_top1 = _centerY + t_half * _bulge1;
        float t_bot1 = _centerY - t_half * _bulge1;

        Color t_c0 = this.Tint(_shade0, _fade);
        Color t_c1 = this.Tint(_shade1, _fade);

        // 법선·탄젠트까지 기본값으로 채운다 — 0 벡터가 섞이면 그 채널을 읽는 셰이더에서 판이 검게 나온다
        for (int t_i = 0; t_i < 4; t_i++) m_quad[t_i] = UIVertex.simpleVert;

        m_quad[0].position = new Vector3(_x0, t_bot0);
        m_quad[0].uv0      = new Vector2(_uv0, 0f);
        m_quad[0].color    = t_c0;

        m_quad[1].position = new Vector3(_x0, t_top0);
        m_quad[1].uv0      = new Vector2(_uv0, 1f);
        m_quad[1].color    = t_c0;

        m_quad[2].position = new Vector3(_x1, t_top1);
        m_quad[2].uv0      = new Vector2(_uv1, 1f);
        m_quad[2].color    = t_c1;

        m_quad[3].position = new Vector3(_x1, t_bot1);
        m_quad[3].uv0      = new Vector2(_uv1, 0f);
        m_quad[3].color    = t_c1;

        _vh.AddUIVertexQuad(m_quad);
    }

    Color Tint(float _shade, float _fade)
    {
        var t_base = this.color;
        float t_s  = Mathf.Clamp(_shade, 0f, 2f);
        return new Color(t_base.r * t_s, t_base.g * t_s, t_base.b * t_s, t_base.a * _fade);
    }

    void EnsureBuffers(int _count)
    {
        if (m_colX != null && m_colX.Length >= _count) return;

        m_colX     = new float[_count];
        m_colUv    = new float[_count];
        m_colDepth = new float[_count];
        m_colShade = new float[_count];
        m_colBulge = new float[_count];
        m_order    = new int[_count];
    }
}
