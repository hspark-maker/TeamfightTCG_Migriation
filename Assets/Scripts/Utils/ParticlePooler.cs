using System.Collections.Generic;
using UnityEngine;

public static class ParticlePooler
{
    static readonly Dictionary<string, GameObject> prefabs = new();
    static bool initialized = false;
    static Transform root;

    static Transform Root
    {
        get
        {
            if (root != null) return root;

            var t_root = new GameObject("[ParticlePool]");
            root = t_root.transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;
            return root;
        }
    }

    /// <summary>풀에서 빌려 나간 연출이 전부 매달리는 컨테이너(없으면 만든다).
    /// 히트스탑처럼 "지금 화면에 떠 있는 전투 파티클 전부"를 한 번에 다뤄야 하는 쪽이 여기로 들어온다 —
    /// 각 연출이 자기가 띄운 것만 들고 있으면 남이 띄운 파티클은 못 멈춘다.</summary>
    public static Transform Container => Root;

    static void Init()
    {
        if (initialized) return;
        initialized = true;
        ObjectPooler.Register<GameObject>(
            (_id) =>
            {
                var t_obj = Object.Instantiate(prefabs[_id], Root);
                t_obj.SetActive(false);
                return t_obj;
            },
            (_obj) => (_obj as GameObject)?.SetActive(false)
        );
    }

    public static void Register(string _id, GameObject _prefab)
    {
        if (!prefabs.ContainsKey(_id))
            prefabs[_id] = _prefab;
        Init();
    }

    /// <summary>풀에서 하나 꺼내 배치. _parent를 주면 그 아래 붙어 따라다닌다(공격 이펙트가 카드와 함께 이동).
    /// 부모를 먼저 걸고 월드 포즈를 잡으므로 시작 위치는 부모 유무와 무관하게 같다.</summary>
    public static GameObject Spawn(string _id, Vector3 _pos, Quaternion _rot, Transform _parent = null)
    {
        var t_obj = ObjectPooler.Get<GameObject>(_id);
        // 컨테이너가 씬 전환 등으로 먼저 파괴되면 object 기반 풀에는 fake-null이 남을 수 있다.
        if (t_obj == null)
            t_obj = Object.Instantiate(prefabs[_id], Root);

        t_obj.transform.SetParent(_parent != null ? _parent : Root, worldPositionStays: false);
        t_obj.transform.SetPositionAndRotation(_pos, _rot);
        if (t_obj.TryGetComponent<PooledParticle>(out var t_pooled))
            t_pooled.id = _id;
        t_obj.SetActive(true);
        return t_obj;
    }

    /// <summary>풀에 반납. **풀 컨테이너로 반드시 옮긴다** — 카드 자식으로 붙은 채 반납하면
    /// 그 카드가 파괴될 때 풀이 들고 있는 오브젝트까지 같이 죽어 다음 Get이 null을 준다.</summary>
    public static void Release(string _id, GameObject _obj)
    {
        if (_obj == null) return;
        if (_obj.transform.parent != Root)
            _obj.transform.SetParent(Root, worldPositionStays: false);
        _obj.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        ObjectPooler.Release<GameObject>(_id, _obj);
    }

    public static void Flush()
    {
        if (root != null)
            Object.Destroy(root.gameObject);
        root = null;
        prefabs.Clear();
        initialized = false;
        ObjectPooler.Flush<GameObject>();
    }
}
