using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ObjectPooler
{
    // 클래스 타입을 key로 하는 Dictionary
    static Dictionary<Type, ObjectPool> pools = new();

    // 내부 풀 클래스
    class ObjectPool
    {
        public Dictionary<string, Stack<object>> objectPools = new();
        public Func<string, object> createFunc;
        public Action<object> releaseFunc;

        public ObjectPool(Func<string, object> _createFunc, Action<object> _releaseFunc)
        {
            this.createFunc = _createFunc;
            this.releaseFunc = _releaseFunc;
        }

        public object Get(string _identifer)
        {
            if (!this.objectPools.ContainsKey(_identifer))
                this.objectPools.Add(_identifer, new Stack<object>());
            var t_obj = this.objectPools[_identifer].Count > 0 ? this.objectPools[_identifer].Pop() : this.createFunc(_identifer);
            if (t_obj == null)
                t_obj = createFunc(_identifer);
            return t_obj;
        }

        public void Release(string _identifer, object _obj)
        {
            if (!this.objectPools.ContainsKey(_identifer))
                return;
            this.releaseFunc?.Invoke(_obj);
            this.objectPools[_identifer].Push(_obj);
        }

    }

    // 제네릭 버전: T 타입용 풀 등록
    public static void Register<T>(Func<string, T> _createFunc, Action<object> _releaseFunc)
    {
        Type t_type = typeof(T);
        if (!pools.ContainsKey(t_type))
        {
            pools[t_type] = new ObjectPool((string _identifer) => _createFunc(_identifer), _releaseFunc);

        }
    }

    // 가져오기
    public static T Get<T>(string _identifer)
    {
        Type t_type = typeof(T);
        if (pools.TryGetValue(t_type, out var t_pool))
            return (T)t_pool.Get(_identifer);

        throw new InvalidOperationException($"ObjectPooler: {t_type.Name} is not registered.");
    }

    // 반납
    public static void Release<T>(string _identifer, T _obj)
    {
        Type t_type = typeof(T);
        if (pools.TryGetValue(t_type, out var t_pool))
            t_pool.Release(_identifer, _obj);
        else
        {
            if (_obj is GameObject)
            {
                GameObject.Destroy(_obj as GameObject);
            }
            return;
        }

    }

    public static void Flush<T>()
    {
        pools.Remove(typeof(T));
    }
}