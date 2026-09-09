#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UIElements;
#if !DISABLE_SRDEBUGGER
using System.Collections.Generic;
using UnityEditor.UIElements;
#endif

namespace HeroSiege.Editor.SROptionsUI
{
    // SROptions를 UXML/USS로 다시 짠 창. 기존 SROptionsWindow(IMGUI)는 그대로 두고 나란히 쓴다.
    public class SROptionsUIWindow : EditorWindow
    {
        #region Static

        private const string MenuPath = "Window/SRDebugger/SROptions Window (UXML)";
        private const string FolderPath = "Assets/_Project/1. Scripts/Editor/SROptions/";
        private const string UxmlPath = FolderPath + "SROptionsWindow.uxml";
        private const string UssPath = FolderPath + "SROptionsWindow.uss";

        private const string ThemePrefKey = "HeroSiege.SROptionsUI.Theme";
        private const string ScalePrefKey = "HeroSiege.SROptionsUI.Scale";

        // 사이드바를 접고 단일 컬럼으로 내려가는 폭
        private const float NarrowWidth = 340f;

        // 실행 문구를 상태바에 남겨 두는 시간(ms)
        private const int InvokedStatusMs = 2500;

        private const string FavoriteView = "★ 즐겨찾기";
        private const string RecentView = "◷ 최근 사용";

        [MenuItem(MenuPath, priority = 100)]
        private static void Open()
        {
            // 처음 만드는 창만 사이드바가 보이는 폭으로 띄운다. 이미 있던 창의 크기는 건드리지 않는다
            bool existed = Resources.FindObjectsOfTypeAll<SROptionsUIWindow>().Length > 0;

            var window = GetWindow<SROptionsUIWindow>(false, "SROptions+", true);
            window.minSize = new Vector2(260f, 300f);

            if (existed == false)
            {
                var rect = window.position;
                window.position = new Rect(rect.x, rect.y, 780f, 620f);
            }

            window.Show();
        }

        #endregion

#if DISABLE_SRDEBUGGER
        private void CreateGUI()
        {
            rootVisualElement.Add(new Label("SRDebugger가 비활성화되어 있다 (DISABLE_SRDEBUGGER)."));
        }
#else
        private readonly SROptionsTreeModel treeModel = new();

        // 지금 화면에 그려져 있는 필드들. 값이 밖에서 바뀌면 이걸로 맞춘다
        private readonly List<SROptionField> visibleFieldList = new();
        private readonly List<Foldout> visibleFoldoutList = new();

        // 즐겨찾기 화면의 블록들(즐겨찾기 id 하나 = 블록 하나). 화면에 보이는 순서 그대로다
        private readonly List<VisualElement> favoriteBlockList = new();

        // 드래그 중인 즐겨찾기 블록과 삽입 예정 자리(그 자리 블록의 앞). 드래그 중이 아니면 null · -1
        private VisualElement draggingBlock;
        private int dropIndex = -1;

        private object activeContainer;
        private bool isDark = true;
        private bool isNarrow;
        private float uiScale = SROptionsScale.Default;

        private string selectedCategory;
        private string previousCategory;
        private string searchQuery = string.Empty;

        private VisualElement rootElement;
        private VisualElement sideElement;
        private VisualElement bannerElement;
        private ScrollView contentElement;
        private ToolbarSearchField searchField;
        private Button favoriteButton;
        private Button themeButton;
        private Label playStateLabel;
        private Label crumbTitleLabel;
        private Label crumbSubLabel;
        private Label statusLeftLabel;
        private Label statusRightLabel;
        private Button categoryButton;

        private void CreateGUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);

            if (visualTree == null)
            {
                rootVisualElement.Add(new Label($"UXML을 찾지 못했다: {UxmlPath}"));
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet != null) rootVisualElement.styleSheets.Add(styleSheet);

            CacheElements();
            BindElements();

            isDark = EditorPrefs.GetBool(ThemePrefKey, true);
            uiScale = SROptionsScale.Clamp(EditorPrefs.GetFloat(ScalePrefKey, SROptionsScale.Default));
            selectedCategory = SROptionsPrefs.GetSelectedCategory(string.Empty);

            // 도메인 리로드(플레이 종료 등)는 private 필드도 복원하지만 검색 필드 UI는 새로 만들어져 비어 있다.
            // 빈 필드와 어긋난 채 지난 검색 결과가 남지 않도록 검색어를 비운다
            searchQuery = string.Empty;

            ApplyTheme();
            Rebuild();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        // 에디터가 주기적으로 부르는 지점. 게임이 바꾼 값을 화면에 반영한다
        private void OnInspectorUpdate()
        {
            if (rootElement == null) return;

            bool playing = EditorApplication.isPlaying;

            // 플레이에 들어가면 실제 옵션 인스턴스로 갈아탄다
            if (playing && ReferenceEquals(SROptions.Current, activeContainer) == false && SROptions.Current != null)
            {
                Rebuild();
                return;
            }

            if (playing == false) return;

            foreach (var field in visibleFieldList) field.Refresh?.Invoke();
        }

        // UXML에서 이름으로 요소들을 찾아 필드에 캐싱한다
        private void CacheElements()
        {
            rootElement = rootVisualElement.Q<VisualElement>("root");
            sideElement = rootVisualElement.Q<VisualElement>("side");
            bannerElement = rootVisualElement.Q<VisualElement>("banner");
            contentElement = rootVisualElement.Q<ScrollView>("content");
            searchField = rootVisualElement.Q<ToolbarSearchField>("search-field");
            favoriteButton = rootVisualElement.Q<Button>("favorite-button");
            themeButton = rootVisualElement.Q<Button>("theme-button");
            playStateLabel = rootVisualElement.Q<Label>("play-state");
            crumbTitleLabel = rootVisualElement.Q<Label>("crumb-title");
            crumbSubLabel = rootVisualElement.Q<Label>("crumb-sub");
            statusLeftLabel = rootVisualElement.Q<Label>("status-left");
            statusRightLabel = rootVisualElement.Q<Label>("status-right");
            categoryButton = rootVisualElement.Q<Button>("category-button");
        }

        // 검색·즐겨찾기·테마·접기/펼치기·단축키 등 각 UI 요소에 이벤트 콜백을 연결한다
        private void BindElements()
        {
            searchField?.RegisterValueChangedCallback(evt =>
            {
                searchQuery = evt.newValue ?? string.Empty;
                BuildContent();
            });

            // 즐겨찾기 화면과 직전 카테고리를 오간다
            favoriteButton.clicked += () =>
            {
                if (selectedCategory != FavoriteView)
                {
                    SelectCategory(FavoriteView);
                    return;
                }

                string fallback = treeModel.CategoryList.Count > 0 ? treeModel.CategoryList[0].Name : string.Empty;
                SelectCategory(string.IsNullOrEmpty(previousCategory) ? fallback : previousCategory);
            };

            themeButton.clicked += () =>
            {
                isDark = isDark == false;
                EditorPrefs.SetBool(ThemePrefKey, isDark);
                ApplyTheme();
            };

            rootVisualElement.Q<Button>("collapse-button").clicked += () => SetAllFoldouts(false);
            rootVisualElement.Q<Button>("expand-button").clicked += () => SetAllFoldouts(true);
            rootVisualElement.Q<Button>("play-button").clicked += () => EditorApplication.isPlaying = true;

            categoryButton.clicked += ShowCategoryMenu;
            ApplyNarrowLayout();

            // 창이 좁아지면 사이드바를 접고, 대신 카테고리 버튼으로 이동하게 한다
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                bool narrow = evt.newRect.width < NarrowWidth;
                if (narrow == isNarrow) return;

                isNarrow = narrow;
                ApplyNarrowLayout();
            });

            // Ctrl(⌘) + 휠로 글자·컨트롤 크기를 키우고 줄인다.
            // ScrollView가 휠을 먼저 먹어 스크롤되지 않도록 내려가는 단계에서 가로챈다
            rootVisualElement.RegisterCallback<WheelEvent>(evt =>
            {
                if (evt.ctrlKey == false && evt.commandKey == false) return;

                SetScale(uiScale + (evt.delta.y < 0f ? SROptionsScale.Step : -SROptionsScale.Step));
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.ctrlKey == false && evt.commandKey == false) return;

                if (evt.keyCode == KeyCode.F)
                {
                    searchField?.Q<TextField>()?.Focus();
                    evt.StopPropagation();
                    return;
                }

                // Ctrl+0으로 배율을 되돌린다
                if (evt.keyCode == KeyCode.Alpha0 || evt.keyCode == KeyCode.Keypad0)
                {
                    SetScale(SROptionsScale.Default);
                    evt.StopPropagation();
                }
            });
        }

        // 배율을 바꾸고 저장한 뒤 화면에 다시 입힌다. 범위 밖이면 아무것도 하지 않는다
        private void SetScale(float scale)
        {
            float clamped = SROptionsScale.Clamp(scale);
            if (Mathf.Approximately(clamped, uiScale)) return;

            uiScale = clamped;
            EditorPrefs.SetFloat(ScalePrefKey, uiScale);

            SROptionsScale.Apply(rootElement, uiScale);
            UpdateStatusHint();
        }

        // 하단 상태바 오른쪽의 조작 안내와 현재 배율을 갱신한다
        private void UpdateStatusHint()
        {
            if (statusRightLabel == null) return;

            statusRightLabel.text = $"Ctrl+휠 크기 {uiScale * 100f:F0}% · Ctrl+0 되돌리기 · Ctrl+F 검색";
        }

        // 플레이 모드 진입/종료 시 옵션 인스턴스가 바뀌므로 화면을 다시 그린다
        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode && change != PlayModeStateChange.EnteredEditMode) return;

            Rebuild();
        }

        // 다크/라이트 테마에 맞춰 루트 스타일 클래스와 토글 버튼 문구를 갱신한다
        private void ApplyTheme()
        {
            rootElement.EnableInClassList("sro-light", isDark == false);
            themeButton.text = isDark ? "Light" : "Dark";
        }

        // 좁을 때만 카테고리 버튼을 띄운다. 사이드바가 없어도 카테고리를 옮겨 다닐 수 있어야 한다
        private void ApplyNarrowLayout()
        {
            sideElement.EnableInClassList("sro-side--hidden", isNarrow);
            categoryButton.style.display = isNarrow ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // 좁은 화면에서 카테고리 버튼을 눌렀을 때 뜨는 드롭다운 메뉴를 만든다
        private void ShowCategoryMenu()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent(FavoriteView), selectedCategory == FavoriteView, () => SelectCategory(FavoriteView));
            menu.AddItem(new GUIContent(RecentView), selectedCategory == RecentView, () => SelectCategory(RecentView));
            menu.AddSeparator(string.Empty);

            foreach (var category in treeModel.CategoryList)
            {
                string name = category.Name;
                menu.AddItem(new GUIContent($"{name} ({category.OptionCount})"), selectedCategory == name, () => SelectCategory(name));
            }

            menu.DropDown(categoryButton.worldBound);
        }

        // 옵션을 다시 수집하고 화면 전체를 새로 그린다
        private void Rebuild()
        {
            if (rootElement == null) return;

            activeContainer = ResolveOptionContainer();

            treeModel.Build(activeContainer);

            if (IsKnownView(selectedCategory) == false)
            {
                selectedCategory = treeModel.CategoryList.Count > 0 ? treeModel.CategoryList[0].Name : string.Empty;
            }

            BuildSide();
            BuildContent();
            UpdatePlayState();
            UpdateStatusHint();
        }

        // 플레이 중이면 실제로 게임이 쓰는 인스턴스를, 아니면 목록을 보여주기 위한 임시 인스턴스를 쓴다
        private object ResolveOptionContainer()
        {
            if (SROptions.Current != null) return SROptions.Current;

            try
            {
                return new SROptions();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SROptions+] 옵션 목록을 만들지 못했다: {e.Message}");
                return null;
            }
        }

        // 선택된 이름이 즐겨찾기·최근이거나 실제 카테고리인지 확인한다. Rebuild 시 사라진 카테고리를 걸러내는 데 쓴다
        private bool IsKnownView(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name == FavoriteView || name == RecentView) return true;

            foreach (var category in treeModel.CategoryList)
            {
                if (category.Name == name) return true;
            }

            return false;
        }

        // 플레이 상태 라벨·안내 배너·하단 상태 텍스트를 현재 상태에 맞춰 갱신한다
        private void UpdatePlayState()
        {
            bool playing = EditorApplication.isPlaying;

            playStateLabel.text = playing ? "PLAY" : "EDIT";
            playStateLabel.EnableInClassList("sro-state--play", playing);
            playStateLabel.EnableInClassList("sro-state--stop", playing == false);

            bannerElement.style.display = playing ? DisplayStyle.None : DisplayStyle.Flex;
            statusLeftLabel.text = $"옵션 {treeModel.TotalCount}개 · 카테고리 {treeModel.CategoryList.Count}";
        }

        #region 사이드바

        // 사이드바를 비우고 빠른 접근(즐겨찾기·최근)과 카테고리·서브카테고리 목록으로 다시 채운다
        private void BuildSide()
        {
            sideElement.Clear();

            sideElement.Add(MakeSideGroupLabel("빠른 접근"));
            sideElement.Add(MakeNavItem(FavoriteView, SROptionsPrefs.GetFavorites().Count, false));
            sideElement.Add(MakeNavItem(RecentView, SROptionsPrefs.GetRecents().Count, false));

            var separator = new VisualElement();
            separator.AddToClassList("sro-sep");
            sideElement.Add(separator);

            sideElement.Add(MakeSideGroupLabel("카테고리"));

            foreach (var category in treeModel.CategoryList)
            {
                var subArea = new VisualElement();
                subArea.AddToClassList("sro-nav__subs");

                foreach (var subCategory in category.SubCategoryList)
                {
                    if (string.IsNullOrEmpty(subCategory.Name)) continue;

                    subArea.Add(MakeNavItem(subCategory.Name, -1, true, category.Name));
                }

                bool hasSub = subArea.childCount > 0;
                sideElement.Add(MakeNavItem(category.Name, category.OptionCount, false, null, hasSub ? subArea : null));

                if (hasSub == false) continue;

                subArea.style.display = SROptionsPrefs.IsFoldOpen(MakeSideFoldKey(category.Name), true)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

                sideElement.Add(subArea);
            }
        }

        // 사이드바 카테고리 폴드아웃 상태 저장 키. 본문 폴드아웃 키와 섞이지 않게 접두사를 붙인다
        private string MakeSideFoldKey(string category)
        {
            return $"side:{category}";
        }

        // 카테고리 줄의 화살표. 클릭하면 그 아래 서브카테고리 목록을 접었다 편다
        private VisualElement MakeNavArrow(string category, VisualElement subArea)
        {
            var arrow = new Label();
            arrow.AddToClassList("sro-nav__arrow");

            bool isOpen = SROptionsPrefs.IsFoldOpen(MakeSideFoldKey(category), true);
            arrow.text = isOpen ? "▼" : "▶";

            arrow.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                // 화살표는 접기 전용이라 카테고리 선택까지 함께 일어나면 안 된다
                evt.StopPropagation();

                bool nextOpen = subArea.style.display == DisplayStyle.None;
                subArea.style.display = nextOpen ? DisplayStyle.Flex : DisplayStyle.None;
                arrow.text = nextOpen ? "▼" : "▶";
                SROptionsPrefs.SetFoldOpen(MakeSideFoldKey(category), nextOpen);
            });

            return arrow;
        }

        // 사이드바 섹션 제목(빠른 접근/카테고리) 라벨을 만든다
        private Label MakeSideGroupLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("sro-side__group");
            return label;
        }

        // 사이드바 한 줄. 서브카테고리 줄은 부모 카테고리를 열고 그 위치로 스크롤한다
        private VisualElement MakeNavItem(string name, int count, bool isSub, string parentCategory = null, VisualElement subArea = null)
        {
            var item = new VisualElement();
            item.AddToClassList("sro-nav");
            if (isSub) item.AddToClassList("sro-nav--sub");

            string owner = isSub ? parentCategory : name;
            if (isSub == false && selectedCategory == name) item.AddToClassList("sro-nav--selected");

            if (isSub == false)
            {
                // 서브카테고리가 없는 줄에도 같은 폭의 빈 자리를 둬야 이름 시작점이 어긋나지 않는다
                if (subArea == null)
                {
                    var spacer = new Label();
                    spacer.AddToClassList("sro-nav__arrow");
                    item.Add(spacer);
                }
                else
                {
                    item.Add(MakeNavArrow(name, subArea));
                }
            }

            var label = new Label(name);
            label.AddToClassList("sro-nav__name");
            item.Add(label);

            if (count >= 0)
            {
                var countLabel = new Label(count.ToString());
                countLabel.AddToClassList("sro-nav__count");
                item.Add(countLabel);
            }

            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                SelectCategory(owner);
                if (isSub) ScrollToFoldout(name);
            });

            return item;
        }

        // 선택 카테고리를 바꾸고, 사이드바 선택 표시·저장값·본문까지 함께 갱신한다
        private void SelectCategory(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (selectedCategory == name) return;

            if (selectedCategory != FavoriteView && selectedCategory != RecentView)
            {
                previousCategory = selectedCategory;
            }

            selectedCategory = name;
            SROptionsPrefs.SetSelectedCategory(name);

            foreach (var child in sideElement.Children())
            {
                if (child.ClassListContains("sro-nav") == false) continue;
                if (child.ClassListContains("sro-nav--sub")) continue;

                var label = child.Q<Label>(className: "sro-nav__name");
                child.EnableInClassList("sro-nav--selected", label != null && label.text == name);
            }

            BuildContent();
        }

        // 제목이 일치하는 폴드아웃을 열고 그 위치까지 스크롤한다
        private void ScrollToFoldout(string title)
        {
            foreach (var foldout in visibleFoldoutList)
            {
                if (foldout.text != title) continue;

                foldout.value = true;

                // 레이아웃이 잡힌 다음 프레임에 스크롤해야 위치가 맞는다
                contentElement.schedule.Execute(() => contentElement.ScrollTo(foldout)).ExecuteLater(1);
                return;
            }
        }

        #endregion

        #region 본문

        // 본문을 다시 그리고, 새로 생긴 요소에 현재 배율을 입힌다
        private void BuildContent()
        {
            BuildContentCore();
            SROptionsScale.Apply(rootElement, uiScale);
        }

        // 현재 상태(빈 목록/검색어/즐겨찾기/최근/카테고리)에 맞는 본문 화면으로 분기해 그린다
        private void BuildContentCore()
        {
            contentElement.Clear();
            visibleFieldList.Clear();
            visibleFoldoutList.Clear();
            favoriteBlockList.Clear();

            if (treeModel.TotalCount == 0)
            {
                contentElement.Add(MakeEmptyLabel("표시할 옵션이 없다. SROptions에 [Category] 속성이 붙은 멤버가 있는지 확인한다."));
                UpdateCrumb("—", string.Empty);
                return;
            }

            if (string.IsNullOrWhiteSpace(searchQuery) == false)
            {
                BuildSearchResults();
                return;
            }

            if (selectedCategory == FavoriteView)
            {
                BuildFavoriteView();
                return;
            }

            if (selectedCategory == RecentView)
            {
                BuildGroupList(SROptionsPrefs.GetRecents(), RecentView, "값을 바꾸거나 실행한 옵션이 여기에 쌓인다.");
                return;
            }

            BuildCategoryView();
        }

        // 선택된 카테고리를 찾아 서브카테고리별로 폴드아웃(또는 직속 카드)을 그린다
        private void BuildCategoryView()
        {
            SROptionCategory target = null;

            foreach (var category in treeModel.CategoryList)
            {
                if (category.Name != selectedCategory) continue;

                target = category;
                break;
            }

            if (target == null)
            {
                contentElement.Add(MakeEmptyLabel("카테고리를 고른다."));
                return;
            }

            UpdateCrumb(target.Name, $"옵션 {target.OptionCount}개");

            foreach (var subCategory in target.SubCategoryList)
            {
                if (string.IsNullOrEmpty(subCategory.Name))
                {
                    contentElement.Add(MakeTable(subCategory.GroupList));
                    continue;
                }

                var foldout = MakeSubCategoryFoldout(subCategory, false);
                foldout.Add(MakeTable(subCategory.GroupList));

                contentElement.Add(foldout);
            }
        }

        // 검색어에 걸리는 옵션을 카테고리 구분 없이 평면 목록으로 그린다
        private void BuildSearchResults()
        {
            var resultList = treeModel.Search(searchQuery);

            UpdateCrumb("검색", $"“{searchQuery}” · {resultList.Count} / {treeModel.TotalCount}");

            if (resultList.Count == 0)
            {
                contentElement.Add(MakeEmptyLabel($"“{searchQuery}”에 걸리는 옵션이 없다."));
                return;
            }

            foreach (var entry in resultList) contentElement.Add(MakeResultRow(entry));
        }

        // 최근 사용처럼 카테고리 구분 없는 목록을 그린다. 항목이 아니라 그 항목이 속한 카드를 통째로
        // 올려서, 「장비 ID」만 최근에 올라도 짝인 「장비 획득」 버튼이 함께 온다
        private void BuildGroupList(IEnumerable<string> idList, string title, string emptyMessage)
        {
            var groupList = CollectGroupsByIds(idList);

            UpdateCrumb(title, $"{groupList.Count}개");

            if (groupList.Count == 0)
            {
                contentElement.Add(MakeEmptyLabel(emptyMessage));
                return;
            }

            contentElement.Add(MakeTable(groupList, true));
        }

        // id 목록을 그것이 속한 카드로 바꾼다. 서브카테고리 id는 그 안의 카드 전부로 풀리고,
        // 같은 카드를 가리키는 id가 여럿이어도 카드는 하나만 나온다
        private List<SROptionGroup> CollectGroupsByIds(IEnumerable<string> idList)
        {
            var groupList = new List<SROptionGroup>();

            foreach (var id in idList)
            {
                var subCategory = treeModel.FindSubCategory(id);

                if (subCategory != null)
                {
                    foreach (var group in subCategory.GroupList)
                    {
                        if (groupList.Contains(group)) continue;
                        groupList.Add(group);
                    }

                    continue;
                }

                var single = treeModel.FindGroup(id);

                // 트리에서 사라진 id이거나 이미 담은 카드면 건너뛴다
                if (single == null) continue;
                if (groupList.Contains(single)) continue;

                groupList.Add(single);
            }

            return groupList;
        }

        // 상단 경로 표시(제목·부제)를 갱신한다
        private void UpdateCrumb(string title, string sub)
        {
            crumbTitleLabel.text = title;
            crumbSubLabel.text = sub;
        }

        // 표시할 항목이 없을 때 보여줄 안내 라벨을 만든다
        private Label MakeEmptyLabel(string message)
        {
            var label = new Label(message);
            label.AddToClassList("sro-empty");
            return label;
        }

        #endregion

        #region 즐겨찾기 순서

        // 즐겨찾기 화면: id 하나가 블록 하나로 서고, 손잡이를 끌거나 우클릭 메뉴로 순서를 바꾼다
        private void BuildFavoriteView()
        {
            var table = new VisualElement();
            table.AddToClassList("sro-table");

            int groupCount = 0;
            using (ListPool<SROptionGroup>.Get(out var seenList))
            {
                foreach (var id in SROptionsPrefs.GetFavorites())
                {
                    using (ListPool<SROptionGroup>.Get(out var groupList))
                    {
                        var subCategory = CollectFavoriteGroups(id, seenList, groupList);
                        if (groupList.Count == 0) continue;

                        groupCount += groupList.Count;

                        var block = MakeFavoriteBlock(id, subCategory, groupList);
                        favoriteBlockList.Add(block);
                        table.Add(block);
                    }
                }
            }

            UpdateCrumb(FavoriteView, $"{groupCount}개");

            if (favoriteBlockList.Count == 0)
            {
                contentElement.Add(MakeEmptyLabel("★을 눌러 자주 쓰는 옵션을 여기에 모은다."));
                return;
            }

            favoriteBlockList[^1].AddToClassList("sro-fav-block--last");
            contentElement.Add(table);
        }

        // 즐겨찾기 id 하나를 그 id가 가리키는 카드들로 바꾼다. 서브카테고리 id는 안의 카드 전부로 풀리고,
        // 앞선 블록에 이미 나온 카드는 다시 담지 않는다. 서브카테고리 즐겨찾기면 그 서브카테고리를 돌려준다
        private SROptionSubCategory CollectFavoriteGroups(string id, List<SROptionGroup> seenList, List<SROptionGroup> groupList)
        {
            var subCategory = treeModel.FindSubCategory(id);

            if (subCategory != null)
            {
                foreach (var group in subCategory.GroupList)
                {
                    if (seenList.Contains(group)) continue;

                    seenList.Add(group);
                    groupList.Add(group);
                }

                return subCategory;
            }

            var single = treeModel.FindGroup(id);

            if (single != null && seenList.Contains(single) == false)
            {
                seenList.Add(single);
                groupList.Add(single);
            }

            return null;
        }

        // 즐겨찾기 블록 하나: [순서 손잡이] [카드 줄들]. userData에 즐겨찾기 id를 담는다.
        // subCategory가 있으면 카테고리 화면과 같은 폴드아웃(서브카테고리 이름 · 접기)째로 세운다
        private VisualElement MakeFavoriteBlock(string favoriteId, SROptionSubCategory subCategory, List<SROptionGroup> groupList)
        {
            var block = new VisualElement { userData = favoriteId };
            block.AddToClassList("sro-fav-block");

            var handle = new Label("≡") { tooltip = "끌어서 순서 이동 · 우클릭 메뉴" };
            handle.AddToClassList("sro-fav-handle");
            block.Add(handle);

            var rowArea = new VisualElement();
            rowArea.AddToClassList("sro-fav-block__rows");

            if (subCategory == null)
            {
                foreach (var group in groupList) rowArea.Add(MakeGroupRow(group, GroupPath(group)));
            }
            else
            {
                // 폴드아웃 머리에 출처와 이름이 있으므로 줄마다 경로를 되풀이하지 않는다
                var foldout = MakeSubCategoryFoldout(subCategory, true);
                foldout.AddToClassList("sro-fold--fav");
                foldout.Add(MakeRowArea(groupList, false));

                rowArea.Add(foldout);
            }

            block.Add(rowArea);

            AttachFavoriteDrag(handle, block);
            AttachFavoriteMenu(handle, block);
            return block;
        }

        // 손잡이를 끌면 삽입선으로 놓일 자리를 보여주고, 놓으면 그 자리로 즐겨찾기를 옮긴다
        private void AttachFavoriteDrag(VisualElement handle, VisualElement block)
        {
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                handle.CapturePointer(evt.pointerId);
                draggingBlock = block;
                dropIndex = -1;
                block.AddToClassList("sro-fav-block--dragging");
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (draggingBlock != block) return;
                if (handle.HasPointerCapture(evt.pointerId) == false) return;

                UpdateDropIndicator(evt.position.y);
            });

            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (draggingBlock != block) return;

                handle.ReleasePointer(evt.pointerId);
                CommitFavoriteDrag();
            });

            // 캡처가 끊기면(창 포커스 이탈 등) 마지막 삽입선 자리로 확정한다. 정상 드롭 뒤에는 no-op
            handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (draggingBlock == block) CommitFavoriteDrag();
            });
        }

        // 손잡이 우클릭 메뉴로도 순서를 옮길 수 있게 한다
        private void AttachFavoriteMenu(VisualElement handle, VisualElement block)
        {
            handle.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                int index = favoriteBlockList.IndexOf(block);
                if (index < 0) return;

                string id = (string)block.userData;

                var upStatus = index == 0 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
                var downStatus = index == favoriteBlockList.Count - 1
                    ? DropdownMenuAction.Status.Disabled
                    : DropdownMenuAction.Status.Normal;

                evt.menu.AppendAction("맨 위로", _ => MoveFavoriteBlock(id, 0), upStatus);
                evt.menu.AppendAction("위로", _ => MoveFavoriteBlock(id, index - 1), upStatus);
                evt.menu.AppendAction("아래로", _ => MoveFavoriteBlock(id, index + 2), downStatus);
                evt.menu.AppendAction("맨 아래로", _ => MoveFavoriteBlock(id, favoriteBlockList.Count), downStatus);
            }));
        }

        // 포인터 y좌표가 가리키는 삽입 자리를 계산해 그 경계에 강조선을 보여준다
        private void UpdateDropIndicator(float pointerY)
        {
            int index = favoriteBlockList.Count;

            for (int i = 0; i < favoriteBlockList.Count; i++)
            {
                if (pointerY < favoriteBlockList[i].worldBound.center.y) { index = i; break; }
            }

            if (index == dropIndex) return;

            dropIndex = index;
            ClearDropIndicator();

            if (index < favoriteBlockList.Count) favoriteBlockList[index].AddToClassList("sro-fav-block--drop-before");
            else favoriteBlockList[^1].AddToClassList("sro-fav-block--drop-after");
        }

        // 모든 블록에서 삽입 강조선을 지운다
        private void ClearDropIndicator()
        {
            foreach (var block in favoriteBlockList)
            {
                block.RemoveFromClassList("sro-fav-block--drop-before");
                block.RemoveFromClassList("sro-fav-block--drop-after");
            }
        }

        // 드래그를 끝낸다. 삽입선이 가리키던 자리로 즐겨찾기를 옮기고 화면을 다시 그린다
        private void CommitFavoriteDrag()
        {
            if (draggingBlock == null) return;

            var block = draggingBlock;
            int targetIndex = dropIndex;

            draggingBlock = null;
            dropIndex = -1;
            block.RemoveFromClassList("sro-fav-block--dragging");
            ClearDropIndicator();

            int currentIndex = favoriteBlockList.IndexOf(block);
            if (targetIndex < 0 || currentIndex < 0) return;

            // 제자리 앞뒤로의 드롭은 이동이 아니다
            if (targetIndex == currentIndex || targetIndex == currentIndex + 1) return;

            MoveFavoriteBlock((string)block.userData, targetIndex);
        }

        // 삽입 자리(그 자리 블록의 앞)로 즐겨찾기를 옮기고 화면을 다시 그린다
        private void MoveFavoriteBlock(string id, int insertIndex)
        {
            string beforeId = insertIndex < favoriteBlockList.Count
                ? (string)favoriteBlockList[insertIndex].userData
                : null;

            SROptionsPrefs.MoveFavorite(id, beforeId);
            BuildContent();
        }

        #endregion

        #region 표 · 줄

        // 그룹 목록을 표 하나로 그린다. 한 줄 = 메서드 하나(또는 메서드 없는 단독 프로퍼티).
        // showPath가 참이면 즐겨찾기·최근처럼 출처가 섞인 화면이라 줄마다 카테고리 경로를 적는다
        private VisualElement MakeTable(List<SROptionGroup> groupList, bool showPath = false)
        {
            var table = MakeRowArea(groupList, showPath);
            table.AddToClassList("sro-table");

            return table;
        }

        // 표 껍데기 없이 카드 줄만 담은 영역. 이미 표 안에 들어가는 자리(즐겨찾기 블록 등)에 쓴다
        private VisualElement MakeRowArea(List<SROptionGroup> groupList, bool showPath)
        {
            var area = new VisualElement();

            foreach (var group in groupList) area.Add(MakeGroupRow(group, showPath ? GroupPath(group) : null));

            return area;
        }

        // 표의 한 줄: [이름] [파라미터 필드들] [★]. 메서드 줄은 이름 자체가 실행 버튼이고,
        // 파라미터가 없으면 가운데는 빈 자리로 둔다
        private VisualElement MakeGroupRow(SROptionGroup group, string pathText)
        {
            var row = new VisualElement();
            row.AddToClassList("sro-line");

            bool hasMethod = group.MethodList.Count > 0;
            if (hasMethod) row.AddToClassList("sro-line--action");

            // 메서드 없는 단독 프로퍼티는 프로퍼티 자체가 이름 자리에 선다
            var nameEntry = hasMethod ? group.MethodList[0] : group.PropertyList[0];

            var head = new VisualElement();
            head.AddToClassList("sro-line__head");

            if (hasMethod)
            {
                var button = MakeActionButton(nameEntry);
                button.AddToClassList("sro-line__name");
                head.Add(button);
            }
            else
            {
                var nameLabel = new Label(nameEntry.Name) { tooltip = nameEntry.Name };
                nameLabel.AddToClassList("sro-line__name");
                head.Add(nameLabel);

                // 이름 라벨을 더블클릭해도 즐겨찾기가 토글된다
                nameLabel.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.clickCount != 2) return;

                    ToggleFavorite(nameEntry.Id, row.Q<Button>(className: "sro-star"));
                    evt.StopPropagation();
                });
            }

            if (string.IsNullOrEmpty(pathText) == false)
            {
                var path = new Label(pathText) { tooltip = pathText };
                path.AddToClassList("sro-line__path");
                head.Add(path);
            }

            row.Add(head);

            var paramArea = new VisualElement();
            paramArea.AddToClassList("sro-line__params");
            row.Add(paramArea);

            for (int i = 0; i < group.PropertyList.Count; i++)
                paramArea.Add(MakeParam(group.PropertyList[i], hasMethod ? group.ParamLabelList[i] : null));

            row.Add(MakeStarButton(nameEntry));
            return row;
        }

        // 파라미터 하나: [짧은 라벨] [편집 컨트롤]. label이 null이면(단독 프로퍼티) 컨트롤만 둔다
        private VisualElement MakeParam(SROptionEntry entry, string label)
        {
            var param = new VisualElement();
            param.AddToClassList("sro-param");

            if (label != null)
            {
                var paramLabel = new Label(label) { tooltip = entry.Name };
                paramLabel.AddToClassList("sro-param__label");
                param.Add(paramLabel);
            }

            var field = SROptionsFieldFactory.CreateProperty(entry, () => SROptionsPrefs.PushRecent(entry.Id));
            field.Element.AddToClassList("sro-param__control");
            visibleFieldList.Add(field);
            param.Add(field.Element);

            return param;
        }

        // 카드가 어느 카테고리에서 왔는지 나타내는 문구. 항목이 하나도 없으면 빈 문자열
        private string GroupPath(SROptionGroup group)
        {
            var entry = group.PropertyList.Count > 0
                ? group.PropertyList[0]
                : group.MethodList.Count > 0 ? group.MethodList[0] : null;

            if (entry == null) return string.Empty;

            return string.IsNullOrEmpty(entry.SubCategory)
                ? entry.Category
                : $"{entry.Category} › {entry.SubCategory}";
        }

        // 실행 메서드 버튼을 만든다. 실행되면 최근 사용 목록에 올리고 하단 상태바에 실행 사실을 남긴다
        private Button MakeActionButton(SROptionEntry entry)
        {
            return SROptionsFieldFactory.CreateMethodButton(entry, () =>
            {
                SROptionsPrefs.PushRecent(entry.Id);
                ShowInvokedStatus(entry);
            });
        }

        // 방금 실행한 옵션 이름을 상태바에 띄운다. 잠시 뒤 원래 문구로 돌아간다
        private void ShowInvokedStatus(SROptionEntry entry)
        {
            if (statusLeftLabel == null) return;

            statusLeftLabel.text = $"▶ 실행: {entry.Name}  ({DateTime.Now:HH:mm:ss})";
            statusLeftLabel.style.color = new StyleColor(new Color(0.44f, 0.75f, 0.45f));

            statusLeftLabel.schedule.Execute(() =>
            {
                statusLeftLabel.style.color = StyleKeyword.Null;
                UpdatePlayState();
            }).StartingIn(InvokedStatusMs);
        }

        // 검색·즐겨찾기·최근 화면에서 쓰는 한 줄. 카테고리 경로를 함께 보여준다
        private VisualElement MakeResultRow(SROptionEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("sro-result");

            var meta = new VisualElement();
            meta.AddToClassList("sro-result__meta");

            var nameLabel = new Label(entry.Name) { tooltip = entry.Name };
            nameLabel.AddToClassList("sro-result__name");
            meta.Add(nameLabel);

            string path = string.IsNullOrEmpty(entry.SubCategory)
                ? entry.Category
                : $"{entry.Category} › {entry.SubCategory}";

            var pathLabel = new Label(path);
            pathLabel.AddToClassList("sro-result__path");
            meta.Add(pathLabel);

            row.Add(meta);

            if (entry.IsMethod)
            {
                var spacer = new VisualElement();
                spacer.AddToClassList("sro-control");
                row.Add(spacer);
                row.Add(MakeActionButton(entry));
            }
            else
            {
                var field = SROptionsFieldFactory.CreateProperty(entry, () => SROptionsPrefs.PushRecent(entry.Id));
                field.Element.AddToClassList("sro-control");
                visibleFieldList.Add(field);
                row.Add(field.Element);
            }

            row.Add(MakeStarButton(entry));
            return row;
        }

        // 즐겨찾기 토글용 별 버튼을 만든다
        private Button MakeStarButton(SROptionEntry entry)
        {
            return MakeStarButton(entry.Id, "즐겨찾기");
        }

        private Button MakeStarButton(string favoriteId, string tooltip)
        {
            var star = new Button { text = "★", tooltip = tooltip };
            star.AddToClassList("sro-star");
            star.EnableInClassList("sro-star--on", SROptionsPrefs.IsFavorite(favoriteId));

            star.clicked += () => ToggleFavorite(favoriteId, star);
            return star;
        }

        // 서브카테고리 폴드아웃 하나. 카테고리 화면과 즐겨찾기 화면이 같은 모양으로 보이도록 둘 다 이것을 쓴다.
        // 내용(줄 영역)은 호출한 쪽이 붙인다. showCategory는 카테고리가 섞이는 화면에서 출처를 머리에 적는다
        private Foldout MakeSubCategoryFoldout(SROptionSubCategory subCategory, bool showCategory)
        {
            var foldout = new Foldout
            {
                text = subCategory.Name,
                value = SROptionsPrefs.IsFoldOpen(subCategory.Name, true),
            };
            foldout.AddToClassList("sro-fold");

            string foldKey = subCategory.Name;
            foldout.RegisterValueChangedCallback(evt => SROptionsPrefs.SetFoldOpen(foldKey, evt.newValue));

            if (showCategory)
            {
                var toggle = foldout.Q<Toggle>();

                if (toggle != null)
                {
                    var path = new Label(subCategory.Category) { tooltip = subCategory.Category };
                    path.AddToClassList("sro-fold__path");
                    toggle.Add(path);
                }
            }

            AttachSubCategoryStar(foldout, subCategory);

            visibleFoldoutList.Add(foldout);
            return foldout;
        }

        // 서브카테고리 폴드아웃 머리에 별을 붙인다. 서브카테고리째 즐겨찾기하면 그 안의 카드가 전부 따라온다
        private void AttachSubCategoryStar(Foldout foldout, SROptionSubCategory subCategory)
        {
            var toggle = foldout.Q<Toggle>();
            if (toggle == null) return;

            string favoriteId = SROptionsTreeModel.MakeSubCategoryId(subCategory.Category, subCategory.Name);
            var star = MakeStarButton(favoriteId, $"「{subCategory.Name}」 전체를 즐겨찾기");
            star.AddToClassList("sro-fold__star");

            // 별을 눌렀을 때 폴드아웃이 함께 접히지 않도록 막는다
            star.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());

            toggle.Add(star);
        }

        // 즐겨찾기 상태를 뒤집고 별 표시·즐겨찾기 화면·사이드바 개수를 함께 갱신한다
        private void ToggleFavorite(string favoriteId, Button star)
        {
            bool added = SROptionsPrefs.ToggleFavorite(favoriteId);
            star.EnableInClassList("sro-star--on", added);

            // 즐겨찾기 화면에서는 목록 자체가 바뀌므로 다시 그린다
            if (selectedCategory == FavoriteView) BuildContent();

            BuildSideCounts();
        }

        // 사이드바를 통째로 다시 만들지 않고 개수만 갱신한다
        private void BuildSideCounts()
        {
            foreach (var child in sideElement.Children())
            {
                var label = child.Q<Label>(className: "sro-nav__name");
                if (label == null) continue;

                var countLabel = child.Q<Label>(className: "sro-nav__count");
                if (countLabel == null) continue;

                if (label.text == FavoriteView) countLabel.text = SROptionsPrefs.GetFavorites().Count.ToString();
                else if (label.text == RecentView) countLabel.text = SROptionsPrefs.GetRecents().Count.ToString();
            }
        }

        // 화면에 보이는 모든 폴드아웃을 한 번에 열거나 접고, 상태를 저장한다
        private void SetAllFoldouts(bool isOpen)
        {
            foreach (var foldout in visibleFoldoutList)
            {
                foldout.value = isOpen;
                SROptionsPrefs.SetFoldOpen(foldout.text, isOpen);
            }
        }

        #endregion
#endif
    }
}
#endif
