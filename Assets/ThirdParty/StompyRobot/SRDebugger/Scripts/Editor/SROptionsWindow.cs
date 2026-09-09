

using System.Text.RegularExpressions;

namespace SRDebugger.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using SRF;
    using UnityEngine;
    using UnityEditor;
    using System.ComponentModel;
    using SRF.Helpers;
#if !DISABLE_SRDEBUGGER
    using Internal;
    using SRDebugger.Services;
    using UI.Controls.Data;
#endif

    class SROptionsWindow : EditorWindow
    {
        [MenuItem(SRDebugEditorPaths.SROptionsMenuItemPath)]
        public static void Open()
        {
            var window = GetWindow<SROptionsWindow>(false, "SROptions", true);
            window.minSize = new Vector2(100, 100);
            window.Show();
        }

        private static string _searchKey;
        
#if DISABLE_SRDEBUGGER
        private bool _isWorking;

        void OnGUI()
        {
            SRDebugEditor.DrawDisabledWindowGui(ref _isWorking);
        }
#else
        [Serializable]
        private class CategoryState
        {
            public string Name;
            public bool IsOpen;
        }

        [SerializeField]
        private List<CategoryState> _categoryStates = new List<CategoryState>();
        private List<CategoryState> _subCategoryStates = new List<CategoryState>();

        private Dictionary<Type, Action<OptionDefinition>> _typeLookup;
        private Dictionary<string, List<OptionDefinition>> _options;

        private Vector2 _scrollPosition;
        private bool _queueRefresh;
        private bool _isDirty;

// 클래스 레벨에 캐시용 딕셔너리 추가
        [NonSerialized] private Dictionary<string, bool> _serializedFoldouts = new Dictionary<string, bool>();
        [NonSerialized] private Dictionary<string, SerializedObject> _serializedObjects = new Dictionary<string, SerializedObject>();
        [NonSerialized] private GUIStyle _divider;
        [NonSerialized] private GUIStyle _foldout;

        private IOptionsService _activeOptionsService;

        public void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying && (_options == null || _isDirty))
            {
                Populate();
                _queueRefresh = true;
                _isDirty = false;
            }
            else if (!EditorApplication.isPlaying && _options != null)
            {
                Clear();
                _queueRefresh = true;
            }

            if (_queueRefresh)
            {
                Repaint();
            }

            _queueRefresh = false;
        }

        private void OnDisable()
        {
            Clear();
        }

        void PopulateTypeLookup()
        {
            _typeLookup = new Dictionary<Type, Action<OptionDefinition>>()
            {
                {typeof(int), OnGUI_Int},
                {typeof(float), OnGUI_Float},
                {typeof(double), OnGUI_Double},
                {typeof(string), OnGUI_String},
                {typeof(bool), OnGUI_Boolean },
                {typeof(uint), OnGUI_AnyInteger},
                {typeof(ushort), OnGUI_AnyInteger},
                {typeof(short), OnGUI_AnyInteger},
                {typeof(sbyte), OnGUI_AnyInteger},
                {typeof(byte), OnGUI_AnyInteger},
                {typeof(long), OnGUI_AnyInteger},
            };
        }

        void Clear()
        {
            _options = null;
            _isDirty = false;

            if (_activeOptionsService != null)
            {
                _activeOptionsService.OptionsUpdated -= OnOptionsUpdated;
            }

            _activeOptionsService = null;
        }

        void Populate()
        {
            if (_typeLookup == null)
            {
                PopulateTypeLookup();
            }

            if (_activeOptionsService != null)
            {
                _activeOptionsService.OptionsUpdated -= OnOptionsUpdated;
            }

            if (_options != null)
            {
                foreach (KeyValuePair<string, List<OptionDefinition>> kv in _options)
                {
                    foreach (var option in kv.Value)
                    {
                        if (option.IsProperty)
                        {
                            option.Property.ValueChanged -= OnOptionPropertyValueChanged;
                        }
                    }
                }
            }

            _options = new Dictionary<string, List<OptionDefinition>>();
            
            foreach (var option in Service.Options.Options)
            {
                if(option.IsOpen == false) continue;
                
                List<OptionDefinition> list;

                if (!_options.TryGetValue(option.Category, out list))
                {
                    list = new List<OptionDefinition>();
                    _options[option.Category] = list;
                }

                list.Add(option);

                if (option.IsProperty)
                {
                    option.Property.ValueChanged += OnOptionPropertyValueChanged;
                }
            }

            foreach (var kv in _options)
            {
                kv.Value.Sort((d1, d2) =>
                {
                    var aEmpty = d1.SubCategory == string.Empty;
                    var bEmpty = d2.SubCategory == string.Empty;
                    if (aEmpty != bEmpty) return aEmpty ? -1 : 1;
                    
                    var sub = string.Compare(d1.SubCategory, d2.SubCategory, StringComparison.Ordinal);
                    return sub != 0 ? sub : d1.SortPriority.CompareTo(d2.SortPriority);
                });
            }

            _activeOptionsService = Service.Options;
            _activeOptionsService.OptionsUpdated += OnOptionsUpdated;
        }

        private void OnOptionPropertyValueChanged(PropertyReference property)
        {
            _queueRefresh = true;
        }

        private void OnOptionsUpdated(object sender, EventArgs e)
        {
            _isDirty = true;
            _queueRefresh = true;
        }

        void OnGUI()
        {
            EditorGUILayout.Space();

            _searchKey = EditorGUILayout.TextField("검색", _searchKey);
            
            if (!EditorApplication.isPlayingOrWillChangePlaymode || _options == null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("SROptions can only be edited in play-mode.");
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (_divider == null)
            {
                _divider = new GUIStyle(GUI.skin.box);
                _divider.stretchWidth = true;
                _divider.fixedHeight = 2;
            }

            if (_foldout == null)
            {
                _foldout = new GUIStyle(EditorStyles.foldout);
                _foldout.fontStyle = FontStyle.Bold;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var kv in _options)
            {
                var state = _categoryStates.FirstOrDefault(p => p.Name == kv.Key);

                if (state == null)
                {
                    state = new CategoryState()
                    {
                        Name = kv.Key,
                        IsOpen = true
                    };
                    _categoryStates.Add(state);
                }
                
                if (string.IsNullOrEmpty(_searchKey))
                {
                    state.IsOpen = EditorGUILayout.Foldout(state.IsOpen, kv.Key, _foldout);

                    if (!state.IsOpen)
                        continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                OnGUI_Category(kv.Value);
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        void OnGUI_Category(List<OptionDefinition> options)
        {
            if (options.FindIndex(o => o.IsOpen) == -1)
                return;
            
            var subCategoryOptionDictionary = options.GroupBy(g => g.SubCategory).ToDictionary(g => g.Key, g => g.ToList());
            
            foreach (var (subCategory, optionList) in subCategoryOptionDictionary)
            {
                var hasSubCategory = subCategory != string.Empty;
                // SubCategory용 Foldout 처리
                if (hasSubCategory)
                {
                    var state = _subCategoryStates.FirstOrDefault(p => p.Name == subCategory);

                    if (state == null)
                    {
                        state = new CategoryState()
                        {
                            Name = subCategory,
                            IsOpen = true
                        };
                        _subCategoryStates.Add(state);
                    }
                
                    if (string.IsNullOrEmpty(_searchKey))
                    {
                        state.IsOpen = EditorGUILayout.Foldout(state.IsOpen, subCategory, _foldout);

                        if (!state.IsOpen)
                            continue;
                    }
                }
                
                if(hasSubCategory) EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                
                foreach (var option in optionList)
                {
                    if(option.IsOpen == false) continue;
                    if (string.IsNullOrEmpty(_searchKey) == false && option.Name.Contains(_searchKey) == false)
                    {
                        continue;
                    }
                
                    if (option.Property != null)
                    {
                        OnGUI_Property(option);
                    } 
                    else if (option.Method != null)
                    {
                        OnGUI_Method(option);
                    }
                }

                if (hasSubCategory)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.EndHorizontal();
                }
            }
            
            // for (var i = 0; i < options.Count; i++)
            // {
            //     var op = options[i];
            //
            //     if(op.IsOpen == false) continue;
            //     if (string.IsNullOrEmpty(_searchKey) == false && op.Name.Contains(_searchKey) == false)
            //     {
            //         continue;
            //     }
            //     
            //     if (op.Property != null)
            //     {
            //         OnGUI_Property(op);
            //     } 
            //     else if (op.Method != null)
            //     {
            //         OnGUI_Method(op);
            //     }
            // }
        }

        void OnGUI_Method(OptionDefinition op)
        {
            if (GUILayout.Button(op.Name))
            {
                op.Method.Invoke(null);
            }
        }

        void OnGUI_Property(OptionDefinition op)
        {
            Action<OptionDefinition> method;

            if (SRDynamicDropdown.IsDynamicDropdown(op.Property))
            {
                method = OnGUI_DynamicDropdown;
            }
            else if (op.Property.PropertyType.IsEnum)
            {
                method = OnGUI_Enum;
            }
            else if (!_typeLookup.TryGetValue(op.Property.PropertyType, out method))
            {
                if (IsSerializableClassOrStruct(op.Property.PropertyType))
                {
                    method = OnGUI_Serialize;
                }
                else
                {
                    OnGUI_Unsupported(op);
                    return;
                }
            }


            if (!op.Property.CanWrite)
                EditorGUI.BeginDisabledGroup(true);

            method(op);

            if (!op.Property.CanWrite)
                EditorGUI.EndDisabledGroup();
        }

        void OnGUI_String(OptionDefinition op)
        {
            EditorGUI.BeginChangeCheck();
            var newValue = EditorGUILayout.TextField(op.Name, (string) op.Property.GetValue());

            if (EditorGUI.EndChangeCheck())
            {
                op.Property.SetValue(Convert.ChangeType(newValue, op.Property.PropertyType));
            }
        }

        void OnGUI_Boolean(OptionDefinition op)
        {
            EditorGUI.BeginChangeCheck();
            var newValue = EditorGUILayout.Toggle(op.Name, (bool) op.Property.GetValue());

            if (EditorGUI.EndChangeCheck())
            {
                op.Property.SetValue(Convert.ChangeType(newValue, op.Property.PropertyType));
            }
        }

        // 클래스 레벨에 필터 입력 값을 저장할 딕셔너리 선언
        [NonSerialized] private Dictionary<string, string> _enumFilters = new Dictionary<string, string>();

        void OnGUI_Enum(OptionDefinition op)
        {
            EditorGUI.BeginChangeCheck();

            // 현재 OptionDefinition에 대한 필터 입력 값을 검색하거나 기본값("")을 사용
            string enumFilter = _enumFilters.ContainsKey(op.Name) ? _enumFilters[op.Name] : "";

            // 필터 입력 필드를 GUI에 추가하고, 사용자 입력을 받습니다.
            enumFilter = EditorGUILayout.TextField($"{op.Name}_검색", enumFilter);

            // 입력된 필터 값을 딕셔너리에 저장 또는 업데이트
            _enumFilters[op.Name] = enumFilter;

            // 입력된 텍스트를 기반으로 Enum 값들을 필터링합니다.
            // 스페이스바를 와일드카드로 사용하여 regex 패턴을 생성합니다.
            var regexPattern = string.Join(".*", enumFilter.Split(' ').Select(Regex.Escape));
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

            var enumValues = Enum.GetValues(op.Property.PropertyType);
            var filteredEnumNames = Enum.GetNames(op.Property.PropertyType)
                .Where(name => regex.IsMatch(name))
                .ToArray();
            var filteredEnumValues = enumValues.Cast<Enum>()
                .Where(value => filteredEnumNames.Contains(value.ToString()))
                .ToArray();

            // 현재 선택된 Enum 값
            Enum currentEnumValue = (Enum)op.Property.GetValue();
            int currentIndex = Array.IndexOf(filteredEnumValues, currentEnumValue);
            string[] displayNames = filteredEnumNames;

            // 필터링된 Enum 값들만 EnumPopup에 표시합니다.
            int selectedIndex = EditorGUILayout.Popup(op.Name, currentIndex, displayNames);

            if (EditorGUI.EndChangeCheck() && selectedIndex >= 0 && selectedIndex < filteredEnumNames.Length)
            {
                // 선택된 Enum 값으로 Property 업데이트
                Enum newValue = (Enum)Enum.Parse(op.Property.PropertyType, filteredEnumNames[selectedIndex]);
                op.Property.SetValue(newValue);
            }
        }
        

        // SRDynamicDropdownAttribute 가 달린 프로퍼티 — 제공 메서드가 준 목록을 팝업으로 고른다
        void OnGUI_DynamicDropdown(OptionDefinition op)
        {
            var items = SRDynamicDropdown.GetItems(op.Property);
            var labels = new string[items.Count];
            for (int i = 0; i < items.Count; i++) labels[i] = items[i].Label;

            int currentIndex = SRDynamicDropdown.IndexOf(items, op.Property.GetValue());

            EditorGUI.BeginChangeCheck();
            int selectedIndex = EditorGUILayout.Popup(op.Name, currentIndex, labels);

            if (EditorGUI.EndChangeCheck() && selectedIndex >= 0 && selectedIndex < items.Count)
            {
                op.Property.SetValue(items[selectedIndex].Value);
            }
        }

        void OnGUI_Int(OptionDefinition op)
        {
            var range = op.Property.GetAttribute<NumberRangeAttribute>();

            int newValue;

            EditorGUI.BeginChangeCheck();

            if (range != null)
            {
                newValue = EditorGUILayout.IntSlider(op.Name, (int)op.Property.GetValue(), (int)range.Min, (int)range.Max);
            }
            else
            {
                newValue = EditorGUILayout.IntField(op.Name, (int) op.Property.GetValue());
            }

            if (EditorGUI.EndChangeCheck())
            {
                op.Property.SetValue(Convert.ChangeType(newValue, op.Property.PropertyType));
            }
        }

        void OnGUI_Float(OptionDefinition op)
        {
            var range = op.Property.GetAttribute<NumberRangeAttribute>();

            float newValue;

            EditorGUI.BeginChangeCheck();

            if (range != null)
            {
                newValue = EditorGUILayout.Slider(op.Name, (float)op.Property.GetValue(), (float)range.Min, (float)range.Max);
            }
            else
            {
                newValue = EditorGUILayout.FloatField(op.Name, (float) op.Property.GetValue());
            }

            if (EditorGUI.EndChangeCheck())
            {
                op.Property.SetValue(Convert.ChangeType(newValue, op.Property.PropertyType));
            }
        }

        void OnGUI_Double(OptionDefinition op)
        {
            var range = op.Property.GetAttribute<NumberRangeAttribute>();

            double newValue;

            EditorGUI.BeginChangeCheck();

            if (range != null && range.Min > float.MinValue && range.Max < float.MaxValue)
            {
                newValue = EditorGUILayout.Slider(op.Name, (float)op.Property.GetValue(), (float)range.Min, (float)range.Max);
            }
            else
            {
                newValue = EditorGUILayout.DoubleField(op.Name, (double) op.Property.GetValue());

                if (range != null)
                {
                    if (newValue > range.Max)
                    {
                        newValue = range.Max;
                    } else if (newValue < range.Min)
                    {
                        newValue = range.Min;
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                op.Property.SetValue(Convert.ChangeType(newValue, op.Property.PropertyType));
            }
        }


        void OnGUI_AnyInteger(OptionDefinition op)
        {
            NumberControl.ValueRange range;

            if (!NumberControl.ValueRanges.TryGetValue(op.Property.PropertyType, out range))
            {
                Debug.LogError("Unknown integer type: " + op.Property.PropertyType);
                return;
            }

            var userRange = op.Property.GetAttribute<NumberRangeAttribute>();

            EditorGUI.BeginChangeCheck();

            var oldValue = (long)Convert.ChangeType(op.Property.GetValue(), typeof(long));
            var newValue = EditorGUILayout.LongField(op.Name, oldValue);

            if (newValue > range.MaxValue)
            {
                newValue = (long)range.MaxValue;
            } else if (newValue < range.MinValue)
            {
                newValue = (long)range.MinValue;
            }

            if (userRange != null)
            {
                if (newValue > userRange.Max)
                {
                    newValue = (long)userRange.Max;
                } else if (newValue < userRange.Min)
                {
                    newValue = (long) userRange.Min;
                }
            }
            
            if (EditorGUI.EndChangeCheck())
            {
                op.Property.SetValue(Convert.ChangeType(newValue, op.Property.PropertyType));
            }
        }

        void OnGUI_Unsupported(OptionDefinition op)
        {
            EditorGUILayout.PrefixLabel(op.Name);
            EditorGUILayout.LabelField("Unsupported Type: {0}".Fmt(op.Property.PropertyType));
        }
        
        void OnGUI_Serialize(OptionDefinition op)
        {
            var target = op.Property.GetValue();
            if (target == null)
            {
                EditorGUILayout.LabelField(op.Name, "null (지원되는 직렬화 타입)");
                return;
            }

            var key = op.Name; // 필요하면 Category + Name 등으로 유니크하게
            if (!_serializedFoldouts.ContainsKey(key))
                _serializedFoldouts[key] = true;

            _serializedFoldouts[key] = EditorGUILayout.Foldout(_serializedFoldouts[key], op.Name, true);
            if (!_serializedFoldouts[key])
                return;

            // UnityEngine.Object가 아닌 순수 C# 객체를 SerializedObject로 감쌀 수 없으므로,
            // UnityEngine.Object가 아닌 경우에는 Reflection 기반 필드 렌더링으로 대체
            if (!typeof(UnityEngine.Object).IsAssignableFrom(target.GetType()))
            {
                DrawWithReflection(target, key);
                return;
            }

            // UnityEngine.Object 기반이면 SerializedObject 사용
            SerializedObject so;
            if (!_serializedObjects.TryGetValue(key, out so) || so.targetObject != (UnityEngine.Object)target)
            {
                so = new SerializedObject((UnityEngine.Object)target);
                _serializedObjects[key] = so;
            }

            so.Update();

            EditorGUI.indentLevel++;
            var prop = so.GetIterator();
            var first = true;
            while (prop.NextVisible(first))
            {
                // m_Script 숨김
                if (prop.propertyPath == "m_Script")
                {
                    first = false;
                    continue;
                }

                EditorGUILayout.PropertyField(prop, true);
                first = false;
            }
            EditorGUI.indentLevel--;

            if (so.ApplyModifiedProperties())
            {
                // SerializedObject 변경 내용을 실제 옵션 객체에 반영
                op.Property.SetValue(target);
            }
        }

        void DrawWithReflection(object target, string cacheKey)
        {
            if (target == null) return;

            var type = target.GetType();
            var flags = System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic;

            EditorGUI.indentLevel++;
            foreach (var field in type.GetFields(flags))
            {
                // \[NonSerialized\] 필드 등은 스킵
                if (Attribute.IsDefined(field, typeof(NonSerializedAttribute)))
                    continue;

                var fieldType = field.FieldType;
                var value = field.GetValue(target);
                var label = ObjectNames.NicifyVariableName(field.Name);

                EditorGUI.BeginChangeCheck();

                object newValue = value;

                if (fieldType == typeof(int))
                    newValue = EditorGUILayout.IntField(label, (int)(value ?? 0));
                else if (fieldType == typeof(float))
                    newValue = EditorGUILayout.FloatField(label, (float)(value ?? 0f));
                else if (fieldType == typeof(bool))
                    newValue = EditorGUILayout.Toggle(label, (bool)(value ?? false));
                else if (fieldType == typeof(string))
                    newValue = EditorGUILayout.TextField(label, (string)(value ?? string.Empty));
                else if (fieldType.IsEnum)
                    newValue = EditorGUILayout.EnumPopup(label, (Enum)(value ?? Activator.CreateInstance(fieldType)));
                else if (IsSerializableClassOrStruct(fieldType))
                {
                    // 중첩 클래스/구조체 재귀적으로 처리
                    if (value == null)
                        value = Activator.CreateInstance(fieldType);

                    var nestedKey = cacheKey + "." + field.Name;
                    if (!_serializedFoldouts.ContainsKey(nestedKey))
                        _serializedFoldouts[nestedKey] = true;

                    _serializedFoldouts[nestedKey] = EditorGUILayout.Foldout(_serializedFoldouts[nestedKey], label, true);
                    if (_serializedFoldouts[nestedKey])
                        DrawWithReflection(value, nestedKey);

                    newValue = value;
                }
                else
                {
                    EditorGUILayout.LabelField(label, $"Unsupported: {fieldType.Name}");
                }

                if (EditorGUI.EndChangeCheck())
                {
                    field.SetValue(target, newValue);
                }
            }

            EditorGUI.indentLevel--;
        }
        
        bool IsSerializableClassOrStruct(Type t)
        {
            if (t.IsPrimitive)
                return false;

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return false;

            // \[Serializable\] 또는 값 형식(struct)인 경우 허용
            return t.IsValueType || Attribute.IsDefined(t, typeof(SerializableAttribute));
        }
#endif
        }
    }
