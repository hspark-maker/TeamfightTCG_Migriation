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

    public static GameObject Spawn(string _id, Vector3 _pos, Quaternion _rot)
    {
        var t_obj = ObjectPooler.Get<GameObject>(_id);
        t_obj.transform.SetPositionAndRotation(_pos, _rot);
        if (t_obj.TryGetComponent<PooledParticle>(out var t_pooled))
            t_pooled.id = _id;
        t_obj.SetActive(true);
        LogUtil.Log(_rot.eulerAngles.ToString());
        return t_obj;
    }

    public static void Release(string _id, GameObject _obj)
    {
        ObjectPooler.Release<GameObject>(_id, _obj);
    }

    public static void Flush()
    {
        prefabs.Clear();
        initialized = false;
        ObjectPooler.Flush<GameObject>();
    }
}
