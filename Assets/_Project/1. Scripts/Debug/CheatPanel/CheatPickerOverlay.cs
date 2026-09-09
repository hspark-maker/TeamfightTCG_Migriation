#if !DISABLE_SRDEBUGGER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace HeroSiege.Debugging.CheatPanel
{
    // 목록에서 고르는 항목 하나 — 프로퍼티에 써 넣을 값과 화면에 보일 글자
    public readonly struct CheatPickerItem
    {
        public readonly object Value;
        public readonly string Label;

        public CheatPickerItem(object value, string label)
        {
            Value = value;
            Label = label;
        }
    }

    // 값이 많은 목록(enum · 동적 드롭다운)을 검색해서 고르는 전체 화면 오버레이.
    // 에디터의 AdvancedDropdown을 대신한다 — 런타임에는 그 API가 없고, 터치로는 작은 팝업이 쓰기 어렵다
    public sealed class CheatPickerOverlay
    {
        // 한 번에 그리는 최대 줄 수. 이보다 많으면 검색으로 좁히라고 안내한다
        private const int MaxRow = 300;

        private readonly VisualElement root;
        private readonly TextField searchField;
        private readonly Label titleLabel;
        private readonly ScrollView listView;
        private readonly Label noticeLabel;

        private readonly List<CheatPickerItem> itemList = new();
        private Action<CheatPickerItem> onPicked;

        public CheatPickerOverlay(VisualElement root)
        {
            this.root = root;
            root.AddToClassList("cheat-picker");
            root.style.display = DisplayStyle.None;

            // 카드 바깥을 누르면 닫힌다
            root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == root) Hide();
            });

            var card = new VisualElement();
            card.AddToClassList("cheat-picker__card");
            root.Add(card);

            var header = new VisualElement();
            header.AddToClassList("cheat-picker__header");
            card.Add(header);

            titleLabel = new Label();
            titleLabel.AddToClassList("cheat-picker__title");
            header.Add(titleLabel);

            var closeButton = new Button(Hide) { text = "✕" };
            closeButton.AddToClassList("cheat-picker__close");
            header.Add(closeButton);

            searchField = new TextField();
            searchField.AddToClassList("cheat-picker__search");
            searchField.RegisterValueChangedCallback(evt => Populate(evt.newValue));
            card.Add(searchField);

            noticeLabel = new Label();
            noticeLabel.AddToClassList("cheat-picker__notice");
            noticeLabel.style.display = DisplayStyle.None;
            card.Add(noticeLabel);

            listView = new ScrollView(ScrollViewMode.Vertical);
            listView.AddToClassList("cheat-picker__list");
            card.Add(listView);
        }

        // 목록을 띄운다. 목록은 열 때마다 새로 받으므로 플레이 중 내용이 바뀌어도 맞는다
        public void Show(string title, IList<CheatPickerItem> sourceList, Action<CheatPickerItem> onPicked)
        {
            this.onPicked = onPicked;

            itemList.Clear();
            itemList.AddRange(sourceList);

            titleLabel.text = title;
            searchField.SetValueWithoutNotify(string.Empty);

            Populate(string.Empty);

            root.style.display = DisplayStyle.Flex;
            root.BringToFront();

            // 키보드가 있는 환경에서는 바로 타이핑할 수 있게 한다
            searchField.schedule.Execute(() => searchField.Focus()).StartingIn(16);
        }

        // 오버레이를 닫는다
        public void Hide()
        {
            root.style.display = DisplayStyle.None;
            listView.Clear();
            itemList.Clear();
            onPicked = null;
        }

        // 검색어에 걸리는 항목만 버튼으로 다시 그린다. 빈 검색어는 전부 통과다
        private void Populate(string query)
        {
            listView.Clear();

            var lowered = string.IsNullOrWhiteSpace(query) ? null : query.ToLowerInvariant();
            int drawn = 0;
            int matched = 0;

            foreach (var item in itemList)
            {
                if (lowered != null && item.Label.ToLowerInvariant().Contains(lowered) == false) continue;

                matched++;
                if (drawn >= MaxRow) continue;

                var captured = item;
                var button = new Button(() =>
                {
                    var callback = onPicked;
                    Hide();
                    callback?.Invoke(captured);
                })
                {
                    text = item.Label,
                };

                button.AddToClassList("cheat-picker__row");
                listView.Add(button);
                drawn++;
            }

            if (matched == 0)
            {
                noticeLabel.text = "일치하는 항목이 없다";
                noticeLabel.style.display = DisplayStyle.Flex;
                return;
            }

            if (matched > drawn)
            {
                noticeLabel.text = $"{matched}개 중 {drawn}개만 표시 — 검색어로 좁혀라";
                noticeLabel.style.display = DisplayStyle.Flex;
                return;
            }

            noticeLabel.style.display = DisplayStyle.None;
        }
    }
}
#endif
