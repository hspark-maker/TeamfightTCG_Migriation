namespace SRDebugger.UI.Controls.Data
{
    using System;
    using System.Collections.Generic;
    using SRF;
    using SRF.UI;
    using UnityEngine;
    using UnityEngine.UI;

    // SRDynamicDropdownAttribute 가 달린 프로퍼티용 컨트롤. EnumControl 과 같은 스피너 UI 로
    // 런타임 목록을 앞뒤로 넘긴다. 목록은 값을 바꿀 때마다 다시 받는다
    public class DynamicDropdownControl : DataBoundControl
    {
        private object _lastValue;
        private IList<SRDropdownItem> _items;

        [RequiredField] public LayoutElement ContentLayoutElement;

        public GameObject[] DisableOnReadOnly;

        [RequiredField] public SRSpinner Spinner;

        [RequiredField] public Text Title;

        [RequiredField] public Text Value;

        protected override void OnBind(string propertyName, Type t)
        {
            base.OnBind(propertyName, t);

            Title.text = propertyName;
            Spinner.interactable = !IsReadOnly;

            if (DisableOnReadOnly != null)
            {
                foreach (var child in DisableOnReadOnly)
                {
                    child.SetActive(!IsReadOnly);
                }
            }

            RefreshItems();
        }

        protected override void OnValueUpdated(object newValue)
        {
            _lastValue = newValue;
            if (_items == null) RefreshItems();
            Value.text = SRDynamicDropdown.LabelOf(_items, newValue);
            LayoutRebuilder.MarkLayoutForRebuild(GetComponent<RectTransform>());
        }

        // 팩토리는 어트리뷰트로 먼저 고르므로 타입만으로는 아무것에도 붙지 않는다
        public override bool CanBind(Type type, bool isReadOnly)
        {
            return false;
        }

        // 목록을 다시 받고 가장 긴 글자에 맞춰 값 영역 폭을 잡는다
        private void RefreshItems()
        {
            _items = SRDynamicDropdown.GetItems(Property);

            var longestLabel = "";
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Label != null && _items[i].Label.Length > longestLabel.Length) longestLabel = _items[i].Label;
            }

            if (_items.Count == 0) return;

            var width = Value.cachedTextGeneratorForLayout.GetPreferredWidth(longestLabel,
                Value.GetGenerationSettings(new Vector2(float.MaxValue, Value.preferredHeight)));

            ContentLayoutElement.preferredWidth = width;
        }

        private void Move(int delta)
        {
            RefreshItems();
            if (_items.Count == 0) return;

            var currentIndex = SRDynamicDropdown.IndexOf(_items, _lastValue);
            var nextIndex = currentIndex < 0 ? 0 : SRMath.Wrap(_items.Count, currentIndex + delta);

            UpdateValue(_items[nextIndex].Value);
            Refresh();
        }

        public void GoToNext()
        {
            Move(1);
        }

        public void GoToPrevious()
        {
            Move(-1);
        }
    }
}
