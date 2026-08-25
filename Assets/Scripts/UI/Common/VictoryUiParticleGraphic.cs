using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Allocation-free UI renderer for a local-space ParticleSystem. The native
/// renderer stays disabled; particles are copied into a Canvas mesh so the
/// same prefab works in a Screen Space Overlay production canvas.
/// </summary>
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
public sealed class VictoryUiParticleGraphic : MaskableGraphic
{
    // Weighted 2x2 atlas lookup: dot, diamond, spark, shard. The masks stay
    // white-alpha; vivid color is selected independently from the same seed.
    private static readonly int[] WeightedTileLookup =
    {
        0, 0, 0,
        1, 1, 1, 1,
        2, 2, 2, 2,
        3, 3, 3, 3, 3
    };

    private static readonly Color32[] VividPalette =
    {
        new Color32(255, 61, 113, 255),
        new Color32(255, 201, 40, 255),
        new Color32(255, 122, 46, 255),
        new Color32(56, 232, 255, 255),
        new Color32(78, 125, 255, 255),
        new Color32(183, 92, 255, 255),
        new Color32(88, 230, 107, 255),
        new Color32(255, 243, 106, 255)
    };

    [SerializeField] private ParticleSystem source;
    [SerializeField] private Sprite atlasSprite;
    [SerializeField, Range(1, 16)] private int atlasColumns = 2;
    [SerializeField, Range(1, 16)] private int atlasRows = 2;
    [SerializeField, Range(0f, 4f)] private float tileInsetPixels = 2f;
    [SerializeField, Range(1, 192)] private int particleCapacity = 96;

    private ParticleSystem.Particle[] particleBuffer;
    private int lastRenderedCount;

    public override Texture mainTexture => atlasSprite != null ? atlasSprite.texture : Texture2D.whiteTexture;

    public void Configure(
        ParticleSystem particleSource,
        int capacity,
        Sprite colorAtlas,
        int columns,
        int rows,
        float insetPixels = 1.5f)
    {
        if (colorAtlas == null)
            throw new System.ArgumentNullException(nameof(colorAtlas));

        source = particleSource;
        atlasSprite = colorAtlas;
        particleCapacity = Mathf.Clamp(capacity, 1, 192);
        atlasColumns = Mathf.Clamp(columns, 1, 16);
        atlasRows = Mathf.Clamp(rows, 1, 16);
        tileInsetPixels = Mathf.Clamp(insetPixels, 0f, 4f);
        EnsureBuffer();
        lastRenderedCount = 0;
        raycastTarget = false;
        SetMaterialDirty();
        SetVerticesDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        EnsureBuffer();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        EnsureBuffer();
        SetMaterialDirty();
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        lastRenderedCount = 0;
        SetVerticesDirty();
        base.OnDisable();
    }

    private void LateUpdate()
    {
        if (source != null && (source.isPlaying || source.particleCount > 0 || lastRenderedCount > 0))
            SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (source == null || atlasSprite == null)
        {
            lastRenderedCount = 0;
            return;
        }

        EnsureBuffer();
        int count = source.GetParticles(particleBuffer);
        lastRenderedCount = count;
        UIVertex vertex = UIVertex.simpleVert;

        for (int index = 0; index < count; index++)
        {
            ParticleSystem.Particle particle = particleBuffer[index];
            float halfSize = particle.GetCurrentSize(source) * 0.5f;
            float radians = particle.rotation * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            Vector3 right = new Vector3(cosine, sine, 0f) * halfSize;
            Vector3 up = new Vector3(-sine, cosine, 0f) * halfSize;
            Vector3 center = particle.position;
            Color32 simulatedColor = particle.GetCurrentColor(source);
            Color32 graphicTint = color;
            uint mixedSeed = MixSeed(particle.randomSeed);
            int paletteIndex = (int)((mixedSeed >> 8) % (uint)VividPalette.Length);
            Color32 paletteColor = VividPalette[paletteIndex];
            Color32 particleColor = new Color32(
                MultiplyByte(paletteColor.r, graphicTint.r),
                MultiplyByte(paletteColor.g, graphicTint.g),
                MultiplyByte(paletteColor.b, graphicTint.b),
                MultiplyByte(simulatedColor.a, graphicTint.a));
            int firstVertex = vertexHelper.currentVertCount;
            GetTileUv(mixedSeed, out Vector2 uvMin, out Vector2 uvMax);

            vertex.color = particleColor;
            vertex.position = center - right - up;
            vertex.uv0 = uvMin;
            vertexHelper.AddVert(vertex);
            vertex.position = center - right + up;
            vertex.uv0 = new Vector2(uvMin.x, uvMax.y);
            vertexHelper.AddVert(vertex);
            vertex.position = center + right + up;
            vertex.uv0 = uvMax;
            vertexHelper.AddVert(vertex);
            vertex.position = center + right - up;
            vertex.uv0 = new Vector2(uvMax.x, uvMin.y);
            vertexHelper.AddVert(vertex);

            vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
        }
    }

    private void EnsureBuffer()
    {
        int capacity = Mathf.Clamp(particleCapacity, 1, 192);
        if (particleBuffer == null || particleBuffer.Length != capacity)
            particleBuffer = new ParticleSystem.Particle[capacity];
    }

    private void GetTileUv(uint randomSeed, out Vector2 uvMin, out Vector2 uvMax)
    {
        int columns = Mathf.Max(1, atlasColumns);
        int rows = Mathf.Max(1, atlasRows);
        int tileCount = columns * rows;
        int weightedSlot = (int)(randomSeed % (uint)WeightedTileLookup.Length);
        int weightedIndex = WeightedTileLookup[weightedSlot];
        int tileIndex = weightedIndex % tileCount;
        int column = tileIndex % columns;
        int row = tileIndex / columns;
        float cellWidth = 1f / columns;
        float cellHeight = 1f / rows;
        Texture2D texture = atlasSprite.texture;
        Rect atlasRect = atlasSprite.textureRect;
        float atlasMinU = atlasRect.xMin / texture.width;
        float atlasMinV = atlasRect.yMin / texture.height;
        float atlasWidthU = atlasRect.width / texture.width;
        float atlasHeightV = atlasRect.height / texture.height;
        float insetU = Mathf.Min(tileInsetPixels / texture.width, cellWidth * atlasWidthU * 0.45f);
        float insetV = Mathf.Min(tileInsetPixels / texture.height, cellHeight * atlasHeightV * 0.45f);

        uvMin = new Vector2(
            atlasMinU + column * cellWidth * atlasWidthU + insetU,
            atlasMinV + row * cellHeight * atlasHeightV + insetV);
        uvMax = new Vector2(
            atlasMinU + (column + 1) * cellWidth * atlasWidthU - insetU,
            atlasMinV + (row + 1) * cellHeight * atlasHeightV - insetV);
    }

    private static uint MixSeed(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }

    private static byte MultiplyByte(byte left, byte right)
    {
        return (byte)((left * right + 127) / 255);
    }
}
