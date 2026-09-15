using UnityEngine;

/// <summary>Marker base used by DataLibrary to index non-pooled runtime prefabs resolved by
/// component type — 전면 오버레이뿐 아니라 화면 밖 무대처럼 타입으로 찾는 단일 인스턴스 프리팹도 포함한다.</summary>
public abstract class SingletonOverlayBase : ContentsUIBehaviour
{
    // 타입 색인만 사용하는 비표시 무대·상시 효과는 개폐 Contents를 요구하지 않는다.
    protected override bool RequiresContents => false;
}
