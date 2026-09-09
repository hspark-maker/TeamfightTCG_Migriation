using System;

namespace SRDebugger
{
    [AttributeUsage(AttributeTargets.Property)]
    public class NumberRangeAttribute : Attribute
    {
        public readonly double Max;
        public readonly double Min;

        public NumberRangeAttribute(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class IncrementAttribute : Attribute
    {
        public readonly double Increment;

        public IncrementAttribute(double increment)
        {
            Increment = increment;
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
    public class SortAttribute : Attribute
    {
        public readonly int SortPriority;

        public SortAttribute(int priority)
        {
            SortPriority = priority;
        }
    }
    
    [AttributeUsage(AttributeTargets.Field)]
    public class SROpenAttribute : Attribute
    {
        public readonly bool IsOpen;

        public SROpenAttribute(bool isOpen)
        {
            IsOpen = isOpen;
        }
    }
    
    [AttributeUsage(AttributeTargets.Field)]
    public class SRSubOpenAttribute : Attribute
    {
        public readonly bool IsOpen;

        public SRSubOpenAttribute(bool isOpen)
        {
            IsOpen = isOpen;
        }
    }
    
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
    public class SRSubCategoryAttribute : Attribute
    {
        public readonly string SubCategory;

        public SRSubCategoryAttribute(string subCategory)
        {
            SubCategory = subCategory;
        }
    }

    // 프로퍼티를 실행 메서드의 파라미터로 묶는다. 같은 카테고리·서브카테고리 안의 메서드 이름(nameof)을 넘기며,
    // 여러 메서드가 같은 값을 쓰면 전부 나열한다. 안 붙은 프로퍼티는 메서드 없는 단독 줄로 그려진다.
    [AttributeUsage(AttributeTargets.Property)]
    public class SRParamOfAttribute : Attribute
    {
        public readonly string[] MethodNameList;

        public SRParamOfAttribute(params string[] methodNameList)
        {
            MethodNameList = methodNameList;
        }
    }

    // 런타임에 만든 목록에서 고르는 드롭다운. 프로퍼티와 같은 클래스의 메서드 이름을 넘긴다 —
    // 그 메서드는 인자 없이 IList<SRDropdownItem> 을 돌려줘야 하며, 드롭다운을 그릴 때마다 다시 불린다.
    // 프로퍼티 타입은 항목의 Value 타입과 같아야 한다(int id 에 int 값 등).
    [AttributeUsage(AttributeTargets.Property)]
    public class SRDynamicDropdownAttribute : Attribute
    {
        public readonly string ProviderMethodName;

        public SRDynamicDropdownAttribute(string providerMethodName)
        {
            ProviderMethodName = providerMethodName;
        }
    }

    // 동적 드롭다운의 항목 하나 — 프로퍼티에 써 넣을 값과 화면에 보일 글자
    public readonly struct SRDropdownItem
    {
        public readonly object Value;
        public readonly string Label;

        public SRDropdownItem(object value, string label)
        {
            Value = value;
            Label = label;
        }
    }
}
