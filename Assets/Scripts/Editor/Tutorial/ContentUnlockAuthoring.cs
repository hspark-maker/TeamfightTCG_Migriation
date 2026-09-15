using UnityEditor;

/// <summary>실제 런타임 카탈로그에 연결된 콘텐츠 해금 저작물을 조회한다.</summary>
public static class ContentUnlockAuthoring
{
    public static ContentUnlockData Data => AssetDatabase.LoadAssetAtPath<RuntimeContentCatalog>(
        "Assets/SO/RuntimeContentCatalog.asset")?.contentUnlockData;
}
