using UnityEngine;

public sealed class InitializationContext
{
    public InitializationRunner Runner { get; }
    public GameObject Root { get; }

    /// <summary>이 루트는 초기화를 맡지 않는다는 표시. 실패가 아니라 정상 종료다 —
    /// 사본 두 벌 중 늦게 깬 쪽이 여기 걸린다(복구 요구를 띄우지 않는다).</summary>
    public bool IsAborted { get; private set; }

    public void Abort() => IsAborted = true;

    /// <summary>이번 부트가 쓰는 콘텐츠 프로필. ContentProfileStep이 세우고 뒤 스텝이 읽는다 —
    /// 각자 ContentProfileConfig.Active를 다시 뒤지면 스텝 순서가 코드에서 안 보인다.</summary>
    public ContentProfileConfig Profile { get; private set; }

    public void SetProfile(ContentProfileConfig _profile) => Profile = _profile;

    internal InitializationContext(InitializationRunner _runner)
    {
        Runner = _runner;
        Root = _runner.gameObject;
    }
}
