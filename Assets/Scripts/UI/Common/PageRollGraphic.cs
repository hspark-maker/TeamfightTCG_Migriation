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

    /// <summary>이 판이 그릴 면. UI Graphic 하나는 텍스처 한 장뿐이라, 종이 앞면(페이지 그림)과
    /// 뒷면(카드 뒤판)을 다른 그림으로 보이려면 판을 둘로 갈라야 한다.
    /// 뒷면은 통 꼭대기 = 화면에서 가장 앞이므로 앞면 판보다 뒤에(위에) 그린다.</summary>
    public enum RollFace
    {
        Both,    // 뒤판 그림이 없을 때 — 앞면 그림을 어둡게 접어 쓴다(예전 동작)
        Front,
        Back,
    }

    Texture  m_texture;
    RollFace m_face   = RollFace.Both;
    Vector2  m_tiling = Vector2.one;
    float   m_amount;              // 0 = 평평, 1 = 책등까지 다 말림
    int     m_dir = 1;             // +1이면 왼쪽이 책등
    float   m_radiusRatio = 0.13f;
    int     m_segments    = 28;
    float   m_bulge       = 0.06f;
    float   m_backShade   = 0.32f;
    float   m_gloss       = 0.35f;
    float   m_glossWidth  = 0.35f;
    float   m_sheenFloor  = 0.82f;
    float   m_diagonal;            // 0이면 세로축 말림(종전과 완전히 동일)
    float   m_edgeShade   = 0.45f;

    readonly UIVertex[] m_quad = new UIVertex[4];

    // 열 단위 값 — 조각을 깊이 순으로 다시 늘어놓아야 해서 한 번에 만들어 둔다
    float[] m_colX;
    float[] m_colUv;
    float[] m_colDepth;
    float[] m_colShade;
    float[] m_colBulge;
    float[] m_colFacing;   // 1 정면 → 0 옆 → -1 뒷면. 어느 판이 그릴 조각인지 이걸로 가른다
    float[] m_colSkew;     // 이 열에서 위·아래 정점이 서로 반대로 밀리는 양(px). 말림 경계를 비스듬히 눕힌다
    int[]   m_order;

    public override Texture mainTexture => m_texture != null ? m_texture : Texture2D.whiteTexture;

    public void SetTexture(Texture _texture)
    {
        if (m_texture == _texture) return;

        m_texture = _texture;
        this.SetMaterialDirty();
        this.SetVerticesDirty();
    }

    /// <summary>그림을 장 안에서 몇 번 반복할지. 뒤판을 칸마다 한 장씩(3x3) 붙일 때 쓴다 —
    /// 종이 한 면에 카드 뒷면 아홉 장이 있는 것이 실제 앨범의 그림이다.
    /// ⚠ 1을 넘기려면 텍스처 wrapMode가 Repeat여야 한다(Clamp면 가장자리 픽셀이 늘어붙는다).</summary>
    public void SetTiling(Vector2 _tiling)
    {
        var t_tiling = new Vector2(Mathf.Max(0.01f, _tiling.x), Mathf.Max(0.01f, _tiling.y));
        if (t_tiling == m_tiling) return;

        m_tiling = t_tiling;
        this.SetVerticesDirty();
    }

    /// <summary>이 판이 맡을 면. 앞뒤를 가르면 두 판이 한 통을 나눠 그린다.</summary>
    public void SetFace(RollFace _face)
    {
        if (m_face == _face) return;

        m_face = _face;
        this.SetVerticesDirty();
    }

    /// <summary>말림의 모양 상수. 진행도·기울기처럼 드래그 중 변하는 자세 값은 각각 전용 setter가 소유한다.</summary>
    public void Configure(float _radiusRatio, int _segments, float _bulge, float _backShade,
                          float _gloss, float _glossWidth, float _sheenFloor)
    {
        m_radiusRatio = Mathf.Clamp(_radiusRatio, 0.02f, 0.5f);
        m_segments    = Mathf.Clamp(_segments, 4, MaxSegments);
        m_bulge       = Mathf.Clamp(_bulge, 0f, 0.3f);
        m_backShade   = Mathf.Clamp01(_backShade);
        m_gloss       = Mathf.Clamp01(_gloss);
        m_glossWidth  = Mathf.Clamp(_glossWidth, 0.05f, 1f);
        m_sheenFloor  = Mathf.Clamp01(_sheenFloor);
        this.SetVerticesDirty();
    }

    /// <summary>말림 경계의 실시간 기울기(장 높이 대비). 0이면 종전의 곧은 세로축 말림.</summary>
    public void SetDiagonal(float _diagonal)
    {
        float t_diagonal = Mathf.Clamp(_diagonal, -0.5f, 0.5f);
        if (Mathf.Approximately(t_diagonal, m_diagonal)) return;

        m_diagonal = t_diagonal;
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
            if (m_face == RollFace.Back) return;   // 평평한 종이에는 뒷면이 없다

            // 평평 — 넘김 전과 픽셀 단위로 같은 그림이어야 한다(여기서 흔들리면 넘김 시작이 툭 튄다).
            // 기울기도 0을 넘긴다: 아직 아무것도 안 감겼으니 눕힐 경계 자체가 없다.
            this.AddStrip(_vh,
                t_hingeX, t_hingeX + t_u * t_w,
                m_dir >= 0 ? 0f : 1f, m_dir >= 0 ? 1f : 0f,
                1f, 1f, 1f, 1f, 0f, 0f, t_centerY, t_h, t_fade);
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

            // 조각이 어느 면인지는 두 열의 평균으로 정한다 — 경계에 걸친 한 조각은 한쪽이 통째로 가져간다
            float t_facing = (m_colFacing[t_c] + m_colFacing[t_c + 1]) * 0.5f;
            if (m_face == RollFace.Front && t_facing < 0f) continue;
            if (m_face == RollFace.Back  && t_facing >= 0f) continue;

            // 뒤판은 제 그림을 그대로 쓴다 — 앞면 그림을 어둡게 접던 값(m_backShade)을 물려받으면 뒤판까지 컴컴하다
            float t_shade0 = m_face == RollFace.Back ? this.BackFaceShade(m_colFacing[t_c])     : m_colShade[t_c];
            float t_shade1 = m_face == RollFace.Back ? this.BackFaceShade(m_colFacing[t_c + 1]) : m_colShade[t_c + 1];

            this.AddStrip(_vh,
                m_colX[t_c], m_colX[t_c + 1],
                m_colUv[t_c], m_colUv[t_c + 1],
                t_shade0, t_shade1,
                m_colBulge[t_c], m_colBulge[t_c + 1],
                m_colSkew[t_c], m_colSkew[t_c + 1],
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

        // LDR 정점 컬러는 1을 넘겨도 Color32에서 잘리므로 밝기 가산만으로는 흰 반사가 나오지 않는다.
        // 말림 중 바탕을 살짝 낮춰 헤드룸을 만들고, 넓은 sheen + 좁은 hot line의 대비로 비닐 코팅을 읽힌다.
        if (t_facing > 0f && m_gloss > 0f && m_face != RollFace.Back)
        {
            float t_strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, m_amount));
            t_shade *= Mathf.Lerp(1f, m_sheenFloor, t_strength);

            // 종이 위 좌표를 따라 자유단에서 책등 쪽으로 흐른다. 정방향·역방향은 같은 자세를
            // 역재생하므로 m_amount 하나만 따르면 빛도 자연스럽게 되감긴다.
            float t_progress  = Mathf.SmoothStep(0f, 1f, m_amount);
            float t_surface   = _a / _width;
            float t_center    = 1f - 0.72f * t_progress;
            float t_off       = t_surface - t_center;
            float t_softSigma = m_glossWidth * 0.45f;
            float t_hotSigma  = t_softSigma * 0.22f;
            float t_soft      = Mathf.Exp(-(t_off * t_off) / (2f * t_softSigma * t_softSigma));
            float t_hot       = Mathf.Exp(-(t_off * t_off) / (2f * t_hotSigma * t_hotSigma));
            float t_reflect   = Mathf.Clamp01(m_gloss * t_strength * (0.45f * t_soft + 2.7f * t_hot));
            t_shade = Mathf.Lerp(t_shade, 1f, t_reflect);
        }

        // 비스듬한 말림: 이 열이 **이미 감아 올린 종이 길이**(장 폭 대비)를 그대로 기울기 세기로 쓴다.
        // 0(아직 누워 있는 구간) → m_amount(자유단)까지 자연히 커지므로 별도 램프가 필요 없고,
        // 평평한 부분과 책등(a ≤ contact)은 0이라 움직이지 않는다 — 넘김 시작 프레임이 종전과 같다.
        m_colSkew[_index]   = Mathf.Clamp01((_a - _contact) / Mathf.Max(1f, _width));

        m_colX[_index]      = t_x;
        m_colUv[_index]     = m_dir >= 0 ? _a / _width : 1f - _a / _width;
        m_colDepth[_index]  = t_depth;
        m_colShade[_index]  = t_shade;
        m_colFacing[_index] = t_facing;
        m_colBulge[_index]  = 1f + m_bulge * (t_depth / (2f * Mathf.Max(1f, _radius)));
    }

    // 뒤판 판 전용 밝기 — 옆으로 누울수록 어둡고 정면으로 뒤집힌 데서 가장 밝다(앞면과 같은 축의 거울상).
    float BackFaceShade(float _facing)
        => Mathf.Lerp(m_edgeShade, 1f, Mathf.Clamp01(-_facing));

    void AddStrip(VertexHelper _vh,
                  float _x0, float _x1, float _uv0, float _uv1,
                  float _shade0, float _shade1, float _bulge0, float _bulge1,
                  float _skew0, float _skew1,
                  float _centerY, float _height, float _fade)
    {
        float t_half = _height * 0.5f;
        float t_top0 = _centerY + t_half * _bulge0;
        float t_bot0 = _centerY - t_half * _bulge0;
        float t_top1 = _centerY + t_half * _bulge1;
        float t_bot1 = _centerY - t_half * _bulge1;

        // 비스듬한 말림. 한 열의 위·아래를 **반대 방향으로** 밀어 말림 경계를 눕힌다 —
        // 아래는 책등 쪽으로(= 더 감긴 것처럼), 위는 자유단 쪽으로(= 덜 감긴 것처럼).
        // 열 단위 x를 흔드는 게 아니라 같은 열 안에서만 어긋내므로 이웃 조각과 틈이 생기지 않고,
        // 깊이 정렬(m_colDepth)·UV·밝기는 그대로다. 앞판·뒤판이 같은 값을 받아 벌어지지 않는다.
        float t_lean0 = m_diagonal * _height * _skew0 * m_dir;
        float t_lean1 = m_diagonal * _height * _skew1 * m_dir;

        float t_xTop0 = _x0 + t_lean0;
        float t_xBot0 = _x0 - t_lean0;
        float t_xTop1 = _x1 + t_lean1;
        float t_xBot1 = _x1 - t_lean1;

        Color t_c0 = this.Tint(_shade0, _fade);
        Color t_c1 = this.Tint(_shade1, _fade);

        // 법선·탄젠트까지 기본값으로 채운다 — 0 벡터가 섞이면 그 채널을 읽는 셰이더에서 판이 검게 나온다
        for (int t_i = 0; t_i < 4; t_i++) m_quad[t_i] = UIVertex.simpleVert;

        // UV는 종이 위 좌표에 반복 횟수만 곱한다 — 접힌 자리에서도 칸 경계가 종이를 따라 같이 휜다
        float t_u0 = _uv0 * m_tiling.x;
        float t_u1 = _uv1 * m_tiling.x;
        float t_vT = m_tiling.y;

        m_quad[0].position = new Vector3(t_xBot0, t_bot0);
        m_quad[0].uv0      = new Vector2(t_u0, 0f);
        m_quad[0].color    = t_c0;

        m_quad[1].position = new Vector3(t_xTop0, t_top0);
        m_quad[1].uv0      = new Vector2(t_u0, t_vT);
        m_quad[1].color    = t_c0;

        m_quad[2].position = new Vector3(t_xTop1, t_top1);
        m_quad[2].uv0      = new Vector2(t_u1, t_vT);
        m_quad[2].color    = t_c1;

        m_quad[3].position = new Vector3(t_xBot1, t_bot1);
        m_quad[3].uv0      = new Vector2(t_u1, 0f);
        m_quad[3].color    = t_c1;

        _vh.AddUIVertexQuad(m_quad);
    }

    Color Tint(float _shade, float _fade)
    {
        var t_base = this.color;
        // 기본 UI 셰이더의 Color32 정점색은 1 초과를 보존하지 않는다.
        float t_s  = Mathf.Clamp01(_shade);
        return new Color(t_base.r * t_s, t_base.g * t_s, t_base.b * t_s, t_base.a * _fade);
    }

    void EnsureBuffers(int _count)
    {
        if (m_colX != null && m_colX.Length >= _count) return;

        m_colX      = new float[_count];
        m_colUv     = new float[_count];
        m_colDepth  = new float[_count];
        m_colShade  = new float[_count];
        m_colFacing = new float[_count];
        m_colBulge  = new float[_count];
        m_colSkew   = new float[_count];
        m_order     = new int[_count];
    }
}
