using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 생성된 SpecData 리소스(Assets/Resources/SpecData.bytes)를 에디터에서 읽어 표를 열거한다.
/// 로컬 표를 읽는 단일 창구다 — 업로더와 docs CSV 내보내기가 같은 경로를 쓴다.
/// </summary>
public static class SpecLocalTables
{
    /// <summary>생성된 리소스를 파싱해 SpecDataManager 인스턴스를 만든다.</summary>
    public static bool TryLoadManager(out object _manager, out string _error)
    {
        _manager = null;
        _error = null;

        string t_json = SpecDataResourceLoader.LoadSpecData();
        if (string.IsNullOrEmpty(t_json))
        {
            _error = "SpecData 리소스를 못 읽었다. CookApps > SpecData 창에서 '시트 적용 & CS 생성'을 먼저 실행할 것.";
            return false;
        }

        var t_manager = new SpecDataManager();
        if (!t_manager.Load(t_json))
        {
            _error = "SpecData 파싱 실패. 생성된 리소스가 손상됐을 수 있다(재생성 필요).";
            return false;
        }

        _manager = t_manager;
        return true;
    }

    /// <summary>표 하나. 행이 0개여도 컬럼을 알 수 있게 행 타입을 같이 나른다.</summary>
    public readonly struct SpecTable
    {
        public readonly string Name;
        public readonly IEnumerable Rows;
        /// <summary>컨테이너의 All 프로퍼티에서 뽑은 원소 타입. 못 뽑으면 null.</summary>
        public readonly Type RowType;

        public SpecTable(string _name, IEnumerable _rows, Type _rowType)
        {
            Name = _name;
            Rows = _rows;
            RowType = _rowType;
        }
    }

    /// <summary>manager의 공개 표 프로퍼티를 훑는다. 행이 없는 표도 그대로 나온다.</summary>
    public static IEnumerable<SpecTable> EnumerateTables(object _manager)
    {
        foreach (PropertyInfo t_property in _manager.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (t_property.GetIndexParameters().Length > 0) continue;

            object t_container;
            try { t_container = t_property.GetValue(_manager); }
            catch (Exception) { continue; }
            if (t_container == null) continue;

            PropertyInfo t_all = t_container.GetType().GetProperty("All", BindingFlags.Public | BindingFlags.Instance);
            if (t_all?.GetValue(t_container) is IEnumerable t_rows)
                yield return new SpecTable(t_property.Name, t_rows, ElementType(t_all.PropertyType));
        }
    }

    /// <summary>이름과 행만 필요한 호출부용 얇은 겉면.</summary>
    public static IEnumerable<KeyValuePair<string, IEnumerable>> Enumerate(object _manager)
    {
        foreach (SpecTable t_table in EnumerateTables(_manager))
            yield return new KeyValuePair<string, IEnumerable>(t_table.Name, t_table.Rows);
    }

    /// <summary>IReadOnlyList&lt;T&gt;·List&lt;T&gt;·T[] 어느 쪽이든 T를 꺼낸다.</summary>
    static Type ElementType(Type _collection)
    {
        if (_collection == null) return null;
        if (_collection.IsArray) return _collection.GetElementType();

        if (_collection.IsGenericType)
        {
            Type[] t_args = _collection.GetGenericArguments();
            if (t_args.Length == 1) return t_args[0];
        }

        foreach (Type t_interface in _collection.GetInterfaces())
            if (t_interface.IsGenericType && t_interface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return t_interface.GetGenericArguments()[0];

        return null;
    }
}
