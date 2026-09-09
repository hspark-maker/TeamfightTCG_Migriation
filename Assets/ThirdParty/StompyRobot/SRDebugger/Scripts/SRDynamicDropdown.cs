using System;
using System.Collections.Generic;
using System.Reflection;
using SRF.Helpers;
using UnityEngine;

namespace SRDebugger
{
    // SRDynamicDropdownAttribute 가 가리키는 제공 메서드를 찾아 항목 목록을 받아 온다.
    // 런타임 패널·에디터 창(IMGUI/UXML)이 같은 길로 목록을 얻는다
    public static class SRDynamicDropdown
    {
        private static readonly SRDropdownItem[] Empty = new SRDropdownItem[0];

        // 프로퍼티에 동적 드롭다운이 달려 있는가
        public static bool IsDynamicDropdown(PropertyReference property)
        {
            return property != null && property.GetAttribute<SRDynamicDropdownAttribute>() != null;
        }

        // 제공 메서드를 불러 항목 목록을 돌려준다. 못 찾거나 예외가 나면 빈 목록이다
        public static IList<SRDropdownItem> GetItems(PropertyReference property)
        {
            var attribute = property?.GetAttribute<SRDynamicDropdownAttribute>();
            if (attribute == null || property.Target == null) return Empty;

            var method = property.Target.GetType().GetMethod(attribute.ProviderMethodName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (method == null || method.GetParameters().Length != 0)
            {
                Debug.LogError($"[SRDynamicDropdown] 제공 메서드 '{attribute.ProviderMethodName}' 를 {property.Target.GetType().Name} 에서 찾지 못했습니다(인자 없는 메서드여야 합니다).");
                return Empty;
            }

            try
            {
                return method.Invoke(method.IsStatic ? null : property.Target, null) as IList<SRDropdownItem> ?? Empty;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Empty;
            }
        }

        // 현재 값이 목록의 몇 번째인지. 없으면 -1
        public static int IndexOf(IList<SRDropdownItem> items, object value)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (Equals(items[i].Value, value)) return i;
            }

            return -1;
        }

        // 현재 값을 보여 줄 글자. 목록에 없는 값이면 값 자체를 찍는다
        public static string LabelOf(IList<SRDropdownItem> items, object value)
        {
            int index = IndexOf(items, value);
            return index >= 0 ? items[index].Label : (value == null ? string.Empty : value.ToString());
        }
    }
}
