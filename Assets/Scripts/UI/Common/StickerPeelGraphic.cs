using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>
/// 스티커가 왼쪽 아래 모서리로 말려 들어가는 대각선 메시.
/// 0은 평평하게 붙은 상태, 1은 오른쪽 위부터 왼쪽 아래까지 완전히 떼어진 상태다.
///
/// 페이지 말림과 달리 축이 왼쪽 아래→오른쪽 위 대각선으로 고정되어 있다. 원본 좌표로 UV를
/// 계산하므로 종이가 말려도 그림이 늘어나지 않으며, DataUtility의 outer UV를 써서 아틀라스도
/// 같은 그림 영역을 읽는다. Overlay Canvas에서는 실제 z 깊이가 보이지 않으므로 굽힘은 폭·명암으로 표현한다.
/// </summary>
[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public class StickerPeelGraphic : Image
{
    const int MaxSegments = 48;

    float m_amount;
    float m_radiusRatio = 0.16f;
    int   m_segments = 20;
    float m_bulge = 0.12f;
    float m_backShade = 0.38f;
    float m_edgeShade = 0.55f;

    Vector2[] m_originalLow;
    Vector2[] m_originalHigh;
    Vector2[] m_drawLow;
    Vector2[] m_drawHigh;
    float[]   m_depth;
    float[]   m_facing;
    int[]     m_order;

    readonly UIVertex[] m_quad = new UIVertex[4];

    public float Amount => this.m_amount;

    public void Configure(float _radiusRatio, int _segments)
    {
        this.m_radiusRatio = Mathf.Clamp(_radiusRatio, 0.03f, 0.5f);
        this.m_segments = Mathf.Clamp(_segments, 6, MaxSegments);
        this.SetVerticesDirty();
    }

    public void SetPeel(float _amount)
    {
        float t_amount = Mathf.Clamp01(_amount);
        if (Mathf.Approximately(t_amount, this.m_amount)) return;

        this.m_amount = t_amount;
        this.SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper _vh)
    {
        _vh.Clear();
        if (this.sprite == null) return;

        Rect t_rect = this.DrawingRect();
        if (t_rect.width <= 0.01f || t_rect.height <= 0.01f) return;

        Vector4 t_uv = DataUtility.GetOuterUV(this.sprite);
        float t_fade = 1f - Mathf.InverseLerp(0.9f, 1f, this.m_amount);
        if (t_fade <= 0f) return;

        if (this.m_amount <= 0.001f)
        {
            this.AddFlatQuad(_vh, t_rect, t_uv);
            return;
        }

        int t_segments = Mathf.Clamp(this.m_segments, 6, MaxSegments);
        this.EnsureBuffers(t_segments + 1);

        Vector2 t_hinge = new Vector2(t_rect.xMin, t_rect.yMin);
        Vector2 t_axis = new Vector2(t_rect.width, t_rect.height);
        float t_length = t_axis.magnitude;
        if (t_length <= 0.01f) return;

        Vector2 t_dir = t_axis / t_length;
        Vector2 t_normal = new Vector2(-t_dir.y, t_dir.x);
        float t_rolled = this.m_amount * t_length;
        float t_contact = Mathf.Max(0f, t_length - t_rolled);
        float t_radius = Mathf.Max(1f, t_length * this.m_radiusRatio);

        for (int t_i = 0; t_i <= t_segments; t_i++)
        {
            float t_s = t_length * t_i / t_segments;
            this.BuildColumn(t_i, t_s, t_contact, t_radius, t_hinge, t_dir, t_normal, t_rect);
        }

        for (int t_i = 0; t_i < t_segments; t_i++) this.m_order[t_i] = t_i;
        for (int t_i = 1; t_i < t_segments; t_i++)
        {
            int t_key = this.m_order[t_i];
            float t_keyDepth = this.m_depth[t_key] + this.m_depth[t_key + 1];
            int t_j = t_i - 1;
            while (t_j >= 0 && this.m_depth[this.m_order[t_j]] + this.m_depth[this.m_order[t_j] + 1] > t_keyDepth)
            {
                this.m_order[t_j + 1] = this.m_order[t_j];
                t_j--;
            }
            this.m_order[t_j + 1] = t_key;
        }

        for (int t_i = 0; t_i < t_segments; t_i++)
        {
            int t_column = this.m_order[t_i];
            this.AddStrip(_vh, t_column, t_column + 1, t_rect, t_uv, t_fade);
        }
    }

    Rect DrawingRect()
    {
        Rect t_rect = this.GetPixelAdjustedRect();
        if (!this.preserveAspect || this.sprite == null) return t_rect;

        Rect t_spriteRect = this.sprite.rect;
        if (t_spriteRect.width <= 0f || t_spriteRect.height <= 0f) return t_rect;

        float t_spriteRatio = t_spriteRect.width / t_spriteRect.height;
        float t_rectRatio = t_rect.width / t_rect.height;
        if (t_spriteRatio > t_rectRatio)
        {
            float t_height = t_rect.width / t_spriteRatio;
            t_rect.y += (t_rect.height - t_height) * this.rectTransform.pivot.y;
            t_rect.height = t_height;
        }
        else
        {
            float t_width = t_rect.height * t_spriteRatio;
            t_rect.x += (t_rect.width - t_width) * this.rectTransform.pivot.x;
            t_rect.width = t_width;
        }

        return t_rect;
    }

    void BuildColumn(int _index, float _surfaceDistance, float _contact, float _radius,
                     Vector2 _hinge, Vector2 _dir, Vector2 _normal, Rect _rect)
    {
        Vector2 t_base = _hinge + _dir * _surfaceDistance;

        float t_qx0 = (_rect.xMin - t_base.x) / _normal.x;
        float t_qx1 = (_rect.xMax - t_base.x) / _normal.x;
        float t_qy0 = (_rect.yMin - t_base.y) / _normal.y;
        float t_qy1 = (_rect.yMax - t_base.y) / _normal.y;
        float t_qMin = Mathf.Max(Mathf.Min(t_qx0, t_qx1), Mathf.Min(t_qy0, t_qy1));
        float t_qMax = Mathf.Min(Mathf.Max(t_qx0, t_qx1), Mathf.Max(t_qy0, t_qy1));

        this.m_originalLow[_index] = t_base + _normal * t_qMin;
        this.m_originalHigh[_index] = t_base + _normal * t_qMax;

        float t_theta = _surfaceDistance <= _contact ? 0f : (_surfaceDistance - _contact) / _radius;
        float t_axisDistance;
        float t_depth;
        if (t_theta <= 0f)
        {
            t_axisDistance = _surfaceDistance;
            t_depth = 0f;
        }
        else
        {
            t_axisDistance = _contact + _radius * Mathf.Sin(t_theta);
            t_depth = _radius * (1f - Mathf.Cos(t_theta));
        }

        float t_lateralScale = 1f + this.m_bulge * t_depth / Mathf.Max(1f, _radius * 2f);
        Vector2 t_drawBase = _hinge + _dir * t_axisDistance;
        this.m_drawLow[_index] = t_drawBase + _normal * (t_qMin * t_lateralScale);
        this.m_drawHigh[_index] = t_drawBase + _normal * (t_qMax * t_lateralScale);
        this.m_depth[_index] = t_depth;
        this.m_facing[_index] = Mathf.Cos(t_theta);
    }

    void AddStrip(VertexHelper _vh, int _a, int _b, Rect _rect, Vector4 _uv, float _fade)
    {
        for (int t_i = 0; t_i < 4; t_i++) this.m_quad[t_i] = UIVertex.simpleVert;

        float t_shadeA = this.Shade(this.m_facing[_a]);
        float t_shadeB = this.Shade(this.m_facing[_b]);
        this.SetVertex(0, this.m_drawLow[_a],  this.m_originalLow[_a],  t_shadeA, _fade, _rect, _uv);
        this.SetVertex(1, this.m_drawHigh[_a], this.m_originalHigh[_a], t_shadeA, _fade, _rect, _uv);
        this.SetVertex(2, this.m_drawHigh[_b], this.m_originalHigh[_b], t_shadeB, _fade, _rect, _uv);
        this.SetVertex(3, this.m_drawLow[_b],  this.m_originalLow[_b],  t_shadeB, _fade, _rect, _uv);
        _vh.AddUIVertexQuad(this.m_quad);
    }

    void AddFlatQuad(VertexHelper _vh, Rect _rect, Vector4 _uv)
    {
        for (int t_i = 0; t_i < 4; t_i++) this.m_quad[t_i] = UIVertex.simpleVert;

        this.SetVertex(0, new Vector2(_rect.xMin, _rect.yMin), new Vector2(_rect.xMin, _rect.yMin), 1f, 1f, _rect, _uv);
        this.SetVertex(1, new Vector2(_rect.xMin, _rect.yMax), new Vector2(_rect.xMin, _rect.yMax), 1f, 1f, _rect, _uv);
        this.SetVertex(2, new Vector2(_rect.xMax, _rect.yMax), new Vector2(_rect.xMax, _rect.yMax), 1f, 1f, _rect, _uv);
        this.SetVertex(3, new Vector2(_rect.xMax, _rect.yMin), new Vector2(_rect.xMax, _rect.yMin), 1f, 1f, _rect, _uv);
        _vh.AddUIVertexQuad(this.m_quad);
    }

    void SetVertex(int _index, Vector2 _position, Vector2 _source, float _shade, float _fade,
                   Rect _rect, Vector4 _uv)
    {
        float t_x = Mathf.InverseLerp(_rect.xMin, _rect.xMax, _source.x);
        float t_y = Mathf.InverseLerp(_rect.yMin, _rect.yMax, _source.y);
        Color t_base = this.color;

        this.m_quad[_index].position = _position;
        this.m_quad[_index].uv0 = new Vector2(Mathf.Lerp(_uv.x, _uv.z, t_x), Mathf.Lerp(_uv.y, _uv.w, t_y));
        this.m_quad[_index].color = new Color(t_base.r * _shade, t_base.g * _shade,
                                              t_base.b * _shade, t_base.a * _fade);
    }

    float Shade(float _facing)
        => _facing >= 0f
         ? Mathf.Lerp(this.m_edgeShade, 1f, _facing)
         : this.m_backShade * Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(-_facing));

    void EnsureBuffers(int _count)
    {
        if (this.m_originalLow != null && this.m_originalLow.Length >= _count) return;

        this.m_originalLow = new Vector2[_count];
        this.m_originalHigh = new Vector2[_count];
        this.m_drawLow = new Vector2[_count];
        this.m_drawHigh = new Vector2[_count];
        this.m_depth = new float[_count];
        this.m_facing = new float[_count];
        this.m_order = new int[_count];
    }
}
