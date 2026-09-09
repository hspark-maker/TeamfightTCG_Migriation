#if UNITY_EDITOR && !DISABLE_SRDEBUGGER
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using SRDebugger;
using SRDebugger.Internal;

namespace HeroSiege.Editor.SROptionsUI
{
    // 옵션 하나 — OptionDefinition에 화면용 식별자와 검색 캐시를 얹은 것
    internal sealed class SROptionEntry
    {
        public OptionDefinition Definition;
        public string Id;
        public string Name;
        public string Category;
        public string SubCategory;
        public string SearchText;
        public string[] TokenList;
        public int Order;

        public bool IsMethod => Definition.IsMethod;
    }

    // 실행 메서드 하나와 [SRParamOf]로 묶인 파라미터 프로퍼티들. 메서드가 없으면 단독 프로퍼티 한 줄이다
    internal sealed class SROptionGroup
    {
        public string Title;
        public int Order;
        public List<SROptionEntry> PropertyList = new();

        // PropertyList와 같은 순서. 메서드 이름과 겹치는 토큰을 뺀 짧은 라벨(「장비 ID」→「ID」)
        public List<string> ParamLabelList = new();
        public List<SROptionEntry> MethodList = new();
    }

    // 서브카테고리 한 덩어리. Name이 비어 있으면 카테고리 직속이다
    internal sealed class SROptionSubCategory
    {
        public string Name;
        public string Category;
        public List<SROptionGroup> GroupList = new();
    }

    internal sealed class SROptionCategory
    {
        public string Name;
        public int OptionCount;
        public List<SROptionSubCategory> SubCategoryList = new();
    }

    // SROptions를 스캔해 카테고리 → 서브카테고리 → 액션 그룹 트리로 만든다.
    // UI 의존이 없어 창 없이도 동작을 확인할 수 있다.
    internal sealed class SROptionsTreeModel
    {
        #region Static

        private static readonly char[] TokenSeparatorList = { ' ', '(', ')', '-', '/', ',', '.' };

        // 서브카테고리를 즐겨찾기 저장소에 담을 때 쓰는 키. 옵션 하나의 Id와 섞이지 않게 접두사를 붙인다
        public static string MakeSubCategoryId(string category, string subCategory)
        {
            return $"sub:{category}/{subCategory}";
        }

        // 표시명을 파라미터 라벨 계산용 토큰으로 쪼갠다. 「param_1」처럼 _로 이어진 것은 한 토큰이다.
        // 라벨에 그대로 쓰이므로 원문 대소문자를 유지하고, 비교할 때만 대소문자를 무시한다
        private static string[] Tokenize(string name)
        {
            return name.Split(TokenSeparatorList, System.StringSplitOptions.RemoveEmptyEntries);
        }

        // 토큰 목록에 해당 토큰이 대소문자 구분 없이 들어 있는지 검사한다
        private static bool ContainsToken(string[] tokenList, string token)
        {
            foreach (var other in tokenList)
            {
                if (string.Equals(other, token, System.StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        // 메서드 이름과 겹치는 토큰을 뺀 라벨(「장비 ID」→「ID」). 전부 겹치면 마지막 토큰, 하나도 안 겹치면 원래 이름
        private static string MakeParamLabel(SROptionEntry property, SROptionEntry method)
        {
            var restList = new List<string>();

            foreach (var token in property.TokenList)
            {
                if (ContainsToken(method.TokenList, token)) continue;
                restList.Add(token);
            }

            // 겹치는 게 없으면 괄호까지 살린 원래 이름이 낫다
            if (restList.Count == property.TokenList.Length) return property.Name;
            if (restList.Count > 0) return string.Join(" ", restList);

            return property.TokenList.Length > 0 ? property.TokenList[^1] : property.Name;
        }

        // 원래 등장 순서를 보존하려고 가장 앞선 항목의 순번을 대표값으로 쓴다
        private static int FirstOrder(SROptionCategory category)
        {
            int order = int.MaxValue;

            foreach (var subCategory in category.SubCategoryList)
            {
                int subOrder = FirstOrder(subCategory);
                if (subOrder < order) order = subOrder;
            }

            return order;
        }

        // 서브카테고리 안에서 가장 앞선 그룹의 순번을 대표값으로 쓴다
        private static int FirstOrder(SROptionSubCategory subCategory)
        {
            int order = int.MaxValue;

            foreach (var group in subCategory.GroupList)
            {
                if (group.Order < order) order = group.Order;
            }

            return order;
        }

        #endregion

        private readonly List<SROptionCategory> categoryList = new();
        private readonly Dictionary<string, SROptionEntry> entryLookup = new();

        // 항목 하나가 어느 액션 카드에 속하는지. 즐겨찾기·최근에서 카드를 통째로 되찾을 때 쓴다
        private readonly Dictionary<string, SROptionGroup> entryGroupLookup = new();

        // MakeSubCategoryId로 만든 키 → 서브카테고리. 서브카테고리째 즐겨찾기할 때 쓴다
        private readonly Dictionary<string, SROptionSubCategory> subCategoryLookup = new();

        // C# 메서드 이름 → 항목 Id. OptionDefinition은 메서드 이름을 안 주므로 컨테이너를 직접 훑어 만든다
        private readonly Dictionary<string, string> methodIdLookup = new();

        public IReadOnlyList<SROptionCategory> CategoryList => categoryList;
        public int TotalCount => entryLookup.Count;

        // 옵션 컨테이너를 스캔해 트리를 다시 만든다
        public void Build(object optionContainer)
        {
            categoryList.Clear();
            entryLookup.Clear();
            entryGroupLookup.Clear();
            subCategoryLookup.Clear();
            methodIdLookup.Clear();

            if (optionContainer == null) return;

            BuildMethodIdLookup(optionContainer);

            var categoryLookup = new Dictionary<string, List<SROptionEntry>>();
            int order = 0;

            foreach (var definition in SRDebuggerUtil.ScanForOptions(optionContainer))
            {
                if (definition.IsOpen == false) continue;

                var entry = CreateEntry(definition, order);
                order++;

                // 표시명이 같은 옵션이 여러 카테고리에 있을 수 있으므로 Id로 중복만 거른다
                if (entryLookup.ContainsKey(entry.Id)) continue;
                entryLookup.Add(entry.Id, entry);

                if (categoryLookup.TryGetValue(entry.Category, out var list) == false)
                {
                    list = new List<SROptionEntry>();
                    categoryLookup.Add(entry.Category, list);
                }

                list.Add(entry);
            }

            foreach (var pair in categoryLookup)
                categoryList.Add(BuildCategory(pair.Key, pair.Value));

            categoryList.Sort((a, b) => FirstOrder(a).CompareTo(FirstOrder(b)));
        }

        // id로 항목을 찾는다. 없으면 null
        public SROptionEntry Find(string id)
        {
            return entryLookup.GetValueOrDefault(id);
        }

        // 항목이 속한 액션 카드를 찾는다. 「장비 ID」를 즐겨찾기해도 짝인 「장비 획득」 버튼이 함께 와야 한다
        public SROptionGroup FindGroup(string id)
        {
            return entryGroupLookup.GetValueOrDefault(id);
        }

        // MakeSubCategoryId로 만든 키로 서브카테고리를 찾는다. 없으면 null
        public SROptionSubCategory FindSubCategory(string id)
        {
            return subCategoryLookup.GetValueOrDefault(id);
        }

        // 검색어에 걸리는 옵션을 원래 순서대로 모은다. 공백은 AND로 취급한다
        public List<SROptionEntry> Search(string query)
        {
            var resultList = new List<SROptionEntry>();
            if (string.IsNullOrWhiteSpace(query)) return resultList;

            var pieceList = query.ToLowerInvariant().Split(' ');

            foreach (var entry in entryLookup.Values)
            {
                bool matched = true;

                foreach (var piece in pieceList)
                {
                    if (piece.Length == 0) continue;
                    if (entry.SearchText.Contains(piece)) continue;

                    matched = false;
                    break;
                }

                if (matched) resultList.Add(entry);
            }

            resultList.Sort((a, b) => a.Order.CompareTo(b.Order));
            return resultList;
        }

        // OptionDefinition으로부터 화면 표시·검색·매칭에 쓰는 SROptionEntry를 만든다
        private SROptionEntry CreateEntry(OptionDefinition definition, int order)
        {
            var category = string.IsNullOrEmpty(definition.Category) ? "기본" : definition.Category;
            var subCategory = definition.SubCategory ?? string.Empty;

            return new SROptionEntry
            {
                Definition = definition,
                Id = $"{category}/{subCategory}/{definition.Name}",
                Name = definition.Name,
                Category = category,
                SubCategory = subCategory,
                SearchText = $"{definition.Name} {category} {subCategory}".ToLowerInvariant(),
                TokenList = Tokenize(definition.Name),
                Order = order,
            };
        }

        // 카테고리에 속한 항목들을 서브카테고리별로 나눠 그룹핑하고 정렬해 카테고리 트리를 만든다
        private SROptionCategory BuildCategory(string name, List<SROptionEntry> entryList)
        {
            var category = new SROptionCategory { Name = name, OptionCount = entryList.Count };

            var subLookup = new Dictionary<string, List<SROptionEntry>>();

            foreach (var entry in entryList)
            {
                if (subLookup.TryGetValue(entry.SubCategory, out var list) == false)
                {
                    list = new List<SROptionEntry>();
                    subLookup.Add(entry.SubCategory, list);
                }

                list.Add(entry);
            }

            foreach (var pair in subLookup)
            {
                var subCategory = new SROptionSubCategory
                {
                    Name = pair.Key,
                    Category = name,
                    GroupList = BuildGroups(pair.Value),
                };

                category.SubCategoryList.Add(subCategory);

                if (string.IsNullOrEmpty(subCategory.Name) == false)
                    subCategoryLookup[MakeSubCategoryId(name, subCategory.Name)] = subCategory;
            }

            // 카테고리 직속(서브카테고리 없음)을 맨 앞에, 나머지는 원래 등장 순서대로
            category.SubCategoryList.Sort((a, b) =>
            {
                bool aEmpty = string.IsNullOrEmpty(a.Name);
                bool bEmpty = string.IsNullOrEmpty(b.Name);
                if (aEmpty != bEmpty) return aEmpty ? -1 : 1;

                return FirstOrder(a).CompareTo(FirstOrder(b));
            });

            return category;
        }

        // 컨테이너의 public 메서드를 훑어 C# 이름 → 항목 Id를 만든다. Id 규칙은 CreateEntry와 같아야 한다
        private void BuildMethodIdLookup(object optionContainer)
        {
            foreach (var method in optionContainer.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                var categoryAttribute = method.GetCustomAttribute<CategoryAttribute>();
                var category = categoryAttribute == null ? "Default" : categoryAttribute.Category;
                if (string.IsNullOrEmpty(category)) category = "기본";

                var subCategory = method.GetCustomAttribute<SRSubCategoryAttribute>()?.SubCategory ?? string.Empty;
                var nameAttribute = method.GetCustomAttribute<DisplayNameAttribute>();
                var name = nameAttribute == null ? method.Name : nameAttribute.DisplayName;

                methodIdLookup[method.Name] = $"{category}/{subCategory}/{name}";
            }
        }

        // 메서드마다 그룹을 만들고, [SRParamOf]가 가리키는 메서드 그룹에 프로퍼티를 붙인다.
        // 가리키는 메서드가 이 서브카테고리에 없거나 어트리뷰트가 없으면 프로퍼티 혼자 한 그룹이다
        private List<SROptionGroup> BuildGroups(List<SROptionEntry> entryList)
        {
            var groupList = new List<SROptionGroup>();
            var groupLookup = new Dictionary<string, SROptionGroup>();

            foreach (var entry in entryList)
            {
                if (entry.IsMethod == false) continue;

                var group = new SROptionGroup { Title = entry.Name, Order = entry.Order };
                group.MethodList.Add(entry);

                groupLookup[entry.Id] = group;
                groupList.Add(group);
            }

            foreach (var entry in entryList)
            {
                if (entry.IsMethod) continue;

                bool attached = false;

                foreach (var owner in FindOwnerGroups(entry, groupLookup))
                {
                    owner.PropertyList.Add(entry);
                    owner.ParamLabelList.Add(MakeParamLabel(entry, owner.MethodList[0]));
                    attached = true;
                }

                if (attached) continue;

                var single = new SROptionGroup { Title = string.Empty, Order = entry.Order };
                single.PropertyList.Add(entry);
                single.ParamLabelList.Add(entry.Name);
                groupList.Add(single);
            }

            foreach (var group in groupList)
            {
                foreach (var entry in group.PropertyList) entryGroupLookup.TryAdd(entry.Id, group);
                foreach (var entry in group.MethodList) entryGroupLookup[entry.Id] = group;
            }

            groupList.Sort((a, b) => a.Order.CompareTo(b.Order));
            return groupList;
        }

        // 프로퍼티의 [SRParamOf]가 가리키는 메서드 그룹들. 이 서브카테고리에 있는 것만 돌려준다
        private IEnumerable<SROptionGroup> FindOwnerGroups(SROptionEntry property, Dictionary<string, SROptionGroup> groupLookup)
        {
            var attribute = property.Definition.Property?.GetAttribute<SRParamOfAttribute>();
            if (attribute == null) yield break;

            foreach (var methodName in attribute.MethodNameList)
            {
                if (methodIdLookup.TryGetValue(methodName, out var methodId) == false) continue;
                if (groupLookup.TryGetValue(methodId, out var group) == false) continue;

                yield return group;
            }
        }
    }
}
#endif
