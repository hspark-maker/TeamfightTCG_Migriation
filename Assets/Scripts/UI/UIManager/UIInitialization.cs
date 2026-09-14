using UnityEngine;

/// <summary>활성 여부와 관계없이 한 번 수행하는 참조·버튼 연결. 데이터 반영과 표시 구독은 포함하지 않는다.</summary>
public interface IUIInitializable
{
    void InitializeUI();
}

public static class UIInitialization
{
    /// <summary>프리팹 소유자가 비활성 자식까지 준비한다. 각 구현은 중복 호출에 안전해야 한다.</summary>
    public static void InitializeHierarchy(GameObject _root)
    {
        if (_root == null) return;
        InitializeBranch(_root.transform);
    }

    static void InitializeBranch(Transform _branch)
    {
        // 중첩 화면의 하위 초기화는 해당 화면이 맡는다. 부모가 다시 순회하지 않는다.
        if (_branch.TryGetComponent<ContentsPooledUI>(out var t_panel))
        {
            t_panel.InitializeUI();
            return;
        }

        foreach (var t_component in _branch.GetComponents<MonoBehaviour>())
            if (t_component is IUIInitializable t_initializable) t_initializable.InitializeUI();

        for (int t_i = 0; t_i < _branch.childCount; t_i++)
            InitializeBranch(_branch.GetChild(t_i));
    }
}
