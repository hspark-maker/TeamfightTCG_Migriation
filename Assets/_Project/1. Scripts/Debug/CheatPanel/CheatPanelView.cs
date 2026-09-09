#if !DISABLE_SRDEBUGGER
using System;
using System.Collections.Generic;
using UnityEngine.Pool;
using UnityEngine.UIElements;

namespace HeroSiege.Debugging.CheatPanel
{
    // UXML로 만든 치트 화면의 알맹이. 트리 모델을 받아 좌측 카테고리 목록과 우측 옵션 카드를 그린다.
    // MonoBehaviour가 아니어서 화면 없이도 만들 수 있고, 배치는 SetLayout에 넘긴 폭으로만 갈린다
    public sealed class CheatPanelView
    {
        #region Static

        private const string FavoriteView = "★ 즐겨찾기";
        private const string RecentView = "◷ 최근 사용";

        // 상태 문구를 남겨 두는 시간(ms)
        private const int StatusHoldMs = 2500;

        // 이 폭 미만이면 사이드바를 접고 한 컬럼으로 내려간다(패널 좌표 기준)
        private const float SingleColumnWidth = 420f;

        // 이 폭 미만이면 사이드바와 라벨 칸을 좁힌다
        private const float CompactWidth = 620f;

        #endregion

        private readonly VisualElement root;
        private readonly TextField searchField;
        private readonly Button clearSearchButton;
        private readonly ScrollView navView;
        private readonly ScrollView contentView;
        private readonly Label statusLabel;

        private readonly CheatOptionTreeModel model = new();
        private readonly CheatFieldFactory factory;
        private readonly CheatPickerOverlay picker;

        // 지금 화면에 그려진 컨트롤들의 값 갱신 함수. 다시 그릴 때마다 통째로 바뀐다
        private readonly List<Action> refreshList = new();

        private string selectedCategory;
        private bool isSingleColumn;
        private bool isCompact;

        public CheatPanelView(VisualElement root)
        {
            this.root = root;

            searchField = root.Q<TextField>("search");
            clearSearchButton = root.Q<Button>("clear-search");
            navView = root.Q<ScrollView>("nav");
            contentView = root.Q<ScrollView>("content");
            statusLabel = root.Q<Label>("status");

            navView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            navView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            contentView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            picker = new CheatPickerOverlay(root.Q<VisualElement>("picker"));
            factory = new CheatFieldFactory(picker);

            searchField.RegisterValueChangedCallback(_ => DrawContent());
            clearSearchButton.clicked += () =>
            {
                searchField.value = string.Empty;
                DrawContent();
            };

            selectedCategory = CheatPanelPrefs.GetSelectedCategory(FavoriteView);
        }

        // 옵션을 다시 스캔해 화면을 통째로 다시 그린다
        public void Rebuild(object optionContainer)
        {
            model.Build(optionContainer);

            // 저장된 카테고리가 사라졌으면 첫 카테고리로 되돌린다
            if (IsSpecialView(selectedCategory) == false && FindCategory(selectedCategory) == null)
            {
                selectedCategory = model.CategoryList.Count > 0 ? model.CategoryList[0].Name : FavoriteView;
            }

            DrawNav();
            DrawContent();
        }

        // 화면에 그려진 컨트롤들의 값을 실제 옵션 값에 맞춘다. 편집 중인 필드는 건드리지 않는다
        public void RefreshValues()
        {
            foreach (var refresh in refreshList) refresh();
        }

        // 쓸 수 있는 폭에 맞춰 배치를 바꾼다. 종횡비가 아니라 폭으로 가르는 이유는,
        // 저dpi 태블릿처럼 세로인데도 폭이 넉넉한 화면이 있고 반대로 작은 폰의 가로는
        // 2단으로 쪼개면 사이드바가 폭의 3분의 1을 먹기 때문이다
        public void SetLayout(float width, float height)
        {
            bool singleColumn = width < SingleColumnWidth;
            bool compact = width < CompactWidth;

            if (isSingleColumn != singleColumn)
            {
                isSingleColumn = singleColumn;

                root.EnableInClassList("cheat-root--single", singleColumn);
                navView.mode = singleColumn ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical;
            }

            if (isCompact == compact) return;

            isCompact = compact;
            root.EnableInClassList("cheat-root--compact", compact);
        }

        // 상태 줄에 잠깐 문구를 띄운다
        private void ShowStatus(string message)
        {
            statusLabel.text = message;
            statusLabel.RemoveFromClassList("cheat-status--faded");

            statusLabel.schedule.Execute(() => statusLabel.AddToClassList("cheat-status--faded")).StartingIn(StatusHoldMs);
        }

        // 즐겨찾기·최근처럼 카테고리 트리에 없는 가상 목록인지 판별한다
        private bool IsSpecialView(string name)
        {
            return name == FavoriteView || name == RecentView;
        }

        // 이름으로 카테고리를 찾는다. 없으면 null
        private CheatOptionCategory FindCategory(string name)
        {
            foreach (var category in model.CategoryList)
            {
                if (category.Name == name) return category;
            }

            return null;
        }

        // 좌측(세로에서는 상단) 카테고리 목록을 그린다
        private void DrawNav()
        {
            navView.Clear();

            navView.Add(MakeNavButton(FavoriteView, CheatPanelPrefs.GetFavorites().Count));
            navView.Add(MakeNavButton(RecentView, CheatPanelPrefs.GetRecents().Count));

            foreach (var category in model.CategoryList)
                navView.Add(MakeNavButton(category.Name, category.OptionCount));
        }

        // 카테고리 버튼 하나. 이름과 개수를 각각 라벨로 넣어 가로·세로 어느 쪽에서도 겹치지 않게 한다
        private Button MakeNavButton(string name, int count)
        {
            var button = new Button(() =>
            {
                selectedCategory = name;
                CheatPanelPrefs.SetSelectedCategory(name);

                DrawNav();
                DrawContent();
            });

            button.AddToClassList("cheat-nav");
            button.EnableInClassList("cheat-nav--on", selectedCategory == name);

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("cheat-nav__name");
            nameLabel.pickingMode = PickingMode.Ignore;
            button.Add(nameLabel);

            var countLabel = new Label(count.ToString());
            countLabel.AddToClassList("cheat-nav__count");
            countLabel.pickingMode = PickingMode.Ignore;
            button.Add(countLabel);

            return button;
        }

        // 검색어와 선택 카테고리에 맞춰 오른쪽 본문을 다시 그린다
        private void DrawContent()
        {
            contentView.Clear();
            refreshList.Clear();

            var query = searchField.value;

            if (string.IsNullOrWhiteSpace(query) == false)
            {
                DrawSearchResult(query);
                return;
            }

            if (selectedCategory == FavoriteView)
            {
                DrawIdList(CheatPanelPrefs.GetFavorites(), "즐겨찾기가 없다 — 카드의 ★를 눌러 담아라");
                return;
            }

            if (selectedCategory == RecentView)
            {
                DrawIdList(CheatPanelPrefs.GetRecents(), "최근 사용한 치트가 없다");
                return;
            }

            var category = FindCategory(selectedCategory);

            if (category == null)
            {
                contentView.Add(MakeEmptyLabel("표시할 옵션이 없다"));
                return;
            }

            foreach (var subCategory in category.SubCategoryList)
            {
                if (string.IsNullOrEmpty(subCategory.Name) == false)
                    contentView.Add(MakeSubHeader(subCategory.Name));

                foreach (var group in subCategory.GroupList)
                    contentView.Add(MakeGroupCard(group));
            }
        }

        // 검색 결과를 카드 단위로 그린다. 같은 카드에 걸린 항목이 여럿이어도 카드는 한 번만 나온다
        private void DrawSearchResult(string query)
        {
            using (HashSetPool<CheatOptionGroup>.Get(out var drawnSet))
            {
                int count = 0;

                foreach (var entry in model.Search(query))
                {
                    var group = model.FindGroup(entry.Id);
                    if (group == null || drawnSet.Add(group) == false) continue;

                    contentView.Add(MakeGroupCard(group));
                    count++;
                }

                if (count == 0) contentView.Add(MakeEmptyLabel($"「{query}」에 걸리는 옵션이 없다"));
            }
        }

        // 저장된 id 목록(즐겨찾기·최근)을 카드로 그린다. 사라진 id는 조용히 건너뛴다
        private void DrawIdList(IReadOnlyList<string> idList, string emptyMessage)
        {
            using (HashSetPool<CheatOptionGroup>.Get(out var drawnSet))
            {
                int count = 0;

                foreach (var id in idList)
                {
                    var subCategory = model.FindSubCategory(id);

                    if (subCategory != null)
                    {
                        contentView.Add(MakeSubHeader(subCategory.Name));

                        foreach (var group in subCategory.GroupList)
                        {
                            if (drawnSet.Add(group) == false) continue;

                            contentView.Add(MakeGroupCard(group));
                            count++;
                        }

                        continue;
                    }

                    var single = model.FindGroup(id);
                    if (single == null || drawnSet.Add(single) == false) continue;

                    contentView.Add(MakeGroupCard(single));
                    count++;
                }

                if (count == 0) contentView.Add(MakeEmptyLabel(emptyMessage));
            }
        }

        // 서브카테고리 구분 머리글
        private Label MakeSubHeader(string title)
        {
            var label = new Label(title);
            label.AddToClassList("cheat-subheader");

            return label;
        }

        // 내용이 없을 때 띄우는 안내 문구
        private Label MakeEmptyLabel(string message)
        {
            var label = new Label(message);
            label.AddToClassList("cheat-empty");

            return label;
        }

        // 액션 카드 하나 — 파라미터 줄들과 실행 버튼(들). 파라미터만 있는 카드는 값 편집 줄만 나온다
        private VisualElement MakeGroupCard(CheatOptionGroup group)
        {
            var card = new VisualElement();
            card.AddToClassList("cheat-group");

            var id = GroupId(group);

            var header = new VisualElement();
            header.AddToClassList("cheat-group__header");
            card.Add(header);

            var title = new Label(string.IsNullOrEmpty(group.Title) ? group.PropertyList[0].Name : group.Title);
            title.AddToClassList("cheat-group__title");
            header.Add(title);

            var favoriteButton = new Button { text = CheatPanelPrefs.IsFavorite(id) ? "★" : "☆" };
            favoriteButton.AddToClassList("cheat-favorite");
            favoriteButton.clicked += () =>
            {
                bool added = CheatPanelPrefs.ToggleFavorite(id);
                favoriteButton.text = added ? "★" : "☆";

                ShowStatus(added ? "즐겨찾기에 담았다" : "즐겨찾기에서 뺐다");
                DrawNav();
            };
            header.Add(favoriteButton);

            for (int i = 0; i < group.PropertyList.Count; i++)
            {
                var entry = group.PropertyList[i];
                var built = factory.CreateProperty(entry, () => CheatPanelPrefs.PushRecent(entry.Id));

                var row = new VisualElement();
                row.AddToClassList("cheat-row");

                // 카드 제목이 곧 항목 이름인 단독 프로퍼티는 라벨을 한 번 더 쓰지 않는다
                if (string.IsNullOrEmpty(group.Title) == false)
                {
                    var label = new Label(group.ParamLabelList[i]);
                    label.AddToClassList("cheat-row__label");
                    row.Add(label);
                }

                built.Element.AddToClassList("cheat-row__control");
                row.Add(built.Element);
                card.Add(row);

                refreshList.Add(built.Refresh);
            }

            foreach (var method in group.MethodList)
            {
                var captured = method;
                var button = factory.CreateMethodButton(captured, () =>
                {
                    CheatPanelPrefs.PushRecent(captured.Id);
                    ShowStatus($"실행: {captured.Name}");
                });

                card.Add(button);
            }

            return card;
        }

        // 카드를 즐겨찾기·최근에 담을 때 쓰는 id. 실행 메서드가 있으면 그것이 카드의 대표다
        private string GroupId(CheatOptionGroup group)
        {
            if (group.MethodList.Count > 0) return group.MethodList[0].Id;

            return group.PropertyList[0].Id;
        }
    }
}
#endif
