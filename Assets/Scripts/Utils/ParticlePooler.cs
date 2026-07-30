using System.Collections.Generic;
using UnityEngine;

public static class ParticlePooler
{
    static readonly Dictionary<string, GameObject> prefabs = new();
    static bool initialized = false;

    static void Init()
    {
        if (initialized) return;
        initialized = true;
        ObjectPooler.Register<GameObject>(
            (_id) =>
            {
                var t_obj = Object.Instantiate(prefabs[_id]);
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
        if (_parent != null) t_obj.transform.SetParent(_parent, worldPositionStays: false);
        t_obj.transform.SetPositionAndRotation(_pos, _rot);
        if (t_obj.TryGetComponent<PooledParticle>(out var t_pooled))
            t_pooled.id = _id;
        t_obj.SetActive(true);
        return t_obj;
    }

    /// <summary>풀에 반납. **부모를 반드시 끊는다** — 카드 자식으로 붙은 채 반납하면
    /// 그 카드가 파괴될 때 풀이 들고 있는 오브젝트까지 같이 죽어 다음 Get이 null을 준다.</summary>
    public static void Release(string _id, GameObject _obj)
    {
        if (_obj == null) return;
        if (_obj.transform.parent != null)
            _obj.transform.SetParent(null, worldPositionStays: false);
        ObjectPooler.Release<GameObject>(_id, _obj);
    }

    public static void Flush()
    {
        prefabs.Clear();
        initialized = false;
        ObjectPooler.Flush<GameObject>();
    }
}
