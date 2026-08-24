using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural five-point UI mask used by the Victory medal shine. It replaces
/// the deleted star decoration sprite without introducing another raster asset.
/// </summary>
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
public sealed class VictoryStarMaskGraphic : MaskableGraphic
{
    [SerializeField, Range(0.2f, 0.8f)] private float innerRadiusRatio = 0.48f;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
        float innerRadius = outerRadius * innerRadiusRatio;
        Color32 vertexColor = color;

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = vertexColor;
        vertex.position = center;
        vertex.uv0 = new Vector2(0.5f, 0.5f);
        vertexHelper.AddVert(vertex);

        const int edgeVertexCount = 10;
        for (int index = 0; index < edgeVertexCount; index++)
        {
            float angle = Mathf.PI * 0.5f + index * Mathf.PI / 5f;
            float radius = (index & 1) == 0 ? outerRadius : innerRadius;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 position = center + direction * radius;
            vertex.position = position;
            vertex.uv0 = new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, position.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, position.y));
            vertexHelper.AddVert(vertex);
        }

        for (int index = 0; index < edgeVertexCount; index++)
        {
            int current = index + 1;
            int next = (index + 1) % edgeVertexCount + 1;
            vertexHelper.AddTriangle(0, current, next);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        innerRadiusRatio = Mathf.Clamp(innerRadiusRatio, 0.2f, 0.8f);
        SetVerticesDirty();
    }
#endif
}
