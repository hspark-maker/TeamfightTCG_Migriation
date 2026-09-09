#if UNITY_EDITOR && !DISABLE_SRDEBUGGER
using System;
using System.Collections.Generic;
using System.Reflection;
using SRDebugger;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SRF.Helpers;

namespace HeroSiege.Editor.SROptionsUI
{
    // 옵션 하나를 편집하는 컨트롤과, 외부에서 값이 바뀌었을 때 화면을 맞추는 갱신 함수
    internal sealed class SROptionField
    {
        public VisualElement Element;
        public Action Refresh;
    }

    // OptionDefinition의 타입을 보고 알맞은 UI Toolkit 컨트롤을 만든다.
    internal static class SROptionsFieldFactory
    {
        // 이 개수를 넘는 enum은 검색 가능한 AdvancedDropdown으로 띄운다
        private const int EnumSearchThreshold = 8;

        // 실행 버튼이 눌린 뒤 강조를 유지하는 시간(ms)
        private const int FiredFlashMs = 450;

        private static readonly Dictionary<Type, long> IntegerMinLookup = new()
        {
            { typeof(uint), uint.MinValue },
            { typeof(ushort), ushort.MinValue },
            { typeof(short), short.MinValue },
            { typeof(sbyte), sbyte.MinValue },
            { typeof(byte), byte.MinValue },
            { typeof(long), long.MinValue },
        };

        private static readonly Dictionary<Type, long> IntegerMaxLookup = new()
        {
            { typeof(uint), uint.MaxValue },
            { typeof(ushort), ushort.MaxValue },
            { typeof(short), short.MaxValue },
            { typeof(sbyte), sbyte.MaxValue },
            { typeof(byte), byte.MaxValue },
            { typeof(long), long.MaxValue },
        };

        // 프로퍼티 편집 컨트롤을 만든다. onEdited는 값이 실제로 바뀌었을 때만 불린다
        public static SROptionField CreateProperty(SROptionEntry entry, Action onEdited)
        {
            var property = entry.Definition.Property;
            var field = Build(property, onEdited);

            if (property.CanWrite == false)
            {
                field.Element.SetEnabled(false);
                field.Element.tooltip = "읽기 전용 옵션";
            }

            return field;
        }

        // 메서드 실행 버튼을 만든다
        public static Button CreateMethodButton(SROptionEntry entry, Action onInvoked)
        {
            Button button = null;

            button = new Button(() =>
            {
                // 실제 실행 여부와 무관하게 「눌렸다」는 것부터 눈에 보이게 한다
                FlashFired(button);

                try
                {
                    entry.Definition.Method.Invoke(null);
                    onInvoked?.Invoke();
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            })
            {
                // 눌러서 실행하는 것임을 알리는 ▶ — 이름만 있으면 라벨처럼 보인다
                text = "▶  " + entry.Name,
                tooltip = entry.Name,
            };

            button.AddToClassList("sro-action");
            return button;
        }

        // 눌린 버튼과 그 줄을 잠깐 강조해 클릭이 먹었음을 보여준다
        private static void FlashFired(Button button)
        {
            if (button == null) return;

            var line = button.parent;

            while (line != null && line.ClassListContains("sro-line") == false)
            {
                line = line.parent;
            }

            button.AddToClassList("sro-action--fired");
            line?.AddToClassList("sro-line--fired");

            button.schedule.Execute(() =>
            {
                button.RemoveFromClassList("sro-action--fired");
                line?.RemoveFromClassList("sro-line--fired");
            }).StartingIn(FiredFlashMs);
        }

        // 프로퍼티 타입을 보고 알맞은 편집 컨트롤 생성 함수로 분기한다
        private static SROptionField Build(PropertyReference property, Action onEdited)
        {
            var type = property.PropertyType;

            if (SRDynamicDropdown.IsDynamicDropdown(property)) return BuildDynamicDropdown(property, onEdited);
            if (type == typeof(bool)) return BuildToggle(property, onEdited);
            if (type == typeof(int)) return BuildInt(property, onEdited);
            if (type == typeof(float)) return BuildFloat(property, onEdited);
            if (type == typeof(double)) return BuildDouble(property, onEdited);
            if (type == typeof(string)) return BuildString(property, onEdited);
            if (type.IsEnum) return BuildEnum(property, onEdited);
            if (IntegerMinLookup.ContainsKey(type)) return BuildAnyInteger(property, onEdited);
            if (IsSerializableClassOrStruct(type)) return BuildNested(property, onEdited);

            return BuildUnsupported(type);
        }

        // bool 프로퍼티용 Toggle 컨트롤을 만든다
        private static SROptionField BuildToggle(PropertyReference property, Action onEdited)
        {
            var toggle = new Toggle { value = ReadBool(property) };
            toggle.AddToClassList("sro-field");

            toggle.RegisterValueChangedCallback(evt =>
            {
                Write(property, evt.newValue);
                onEdited?.Invoke();
            });

            return new SROptionField
            {
                Element = toggle,
                Refresh = () => SetIfChanged(toggle, ReadBool(property)),
            };
        }

        // int 프로퍼티용 컨트롤을 만든다. NumberRange가 있으면 슬라이더, 없으면 정수 입력창을 쓴다
        private static SROptionField BuildInt(PropertyReference property, Action onEdited)
        {
            var range = property.GetAttribute<NumberRangeAttribute>();

            if (range != null)
            {
                var slider = new SliderInt((int)range.Min, (int)range.Max)
                {
                    value = ReadInt(property),
                    showInputField = true,
                };
                slider.AddToClassList("sro-field");

                slider.RegisterValueChangedCallback(evt =>
                {
                    Write(property, Convert.ChangeType(evt.newValue, property.PropertyType));
                    onEdited?.Invoke();
                });

                return new SROptionField
                {
                    Element = slider,
                    Refresh = () => SetIfChanged(slider, ReadInt(property)),
                };
            }

            var field = new IntegerField { value = ReadInt(property), isDelayed = true };
            field.AddToClassList("sro-field");

            field.RegisterValueChangedCallback(evt =>
            {
                Write(property, Convert.ChangeType(evt.newValue, property.PropertyType));
                onEdited?.Invoke();
            });

            return new SROptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadInt(property)),
            };
        }

        // float 프로퍼티용 컨트롤을 만든다. NumberRange가 있으면 슬라이더, 없으면 실수 입력창을 쓴다
        private static SROptionField BuildFloat(PropertyReference property, Action onEdited)
        {
            var range = property.GetAttribute<NumberRangeAttribute>();

            if (range != null)
            {
                var slider = new Slider((float)range.Min, (float)range.Max)
                {
                    value = ReadFloat(property),
                    showInputField = true,
                };
                slider.AddToClassList("sro-field");

                slider.RegisterValueChangedCallback(evt =>
                {
                    Write(property, Convert.ChangeType(evt.newValue, property.PropertyType));
                    onEdited?.Invoke();
                });

                return new SROptionField
                {
                    Element = slider,
                    Refresh = () => SetIfChanged(slider, ReadFloat(property)),
                };
            }

            var field = new FloatField { value = ReadFloat(property), isDelayed = true };
            field.AddToClassList("sro-field");

            field.RegisterValueChangedCallback(evt =>
            {
                Write(property, Convert.ChangeType(evt.newValue, property.PropertyType));
                onEdited?.Invoke();
            });

            return new SROptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadFloat(property)),
            };
        }

        // double 프로퍼티용 입력창을 만든다. NumberRange가 있으면 입력값을 클램프한다
        private static SROptionField BuildDouble(PropertyReference property, Action onEdited)
        {
            var range = property.GetAttribute<NumberRangeAttribute>();
            var field = new DoubleField { value = ReadDouble(property), isDelayed = true };
            field.AddToClassList("sro-field");

            field.RegisterValueChangedCallback(evt =>
            {
                double value = evt.newValue;

                if (range != null) value = Math.Clamp(value, range.Min, range.Max);

                Write(property, value);
                SetIfChanged(field, value);
                onEdited?.Invoke();
            });

            return new SROptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadDouble(property)),
            };
        }

        // string 프로퍼티용 텍스트 입력창을 만든다
        private static SROptionField BuildString(PropertyReference property, Action onEdited)
        {
            var field = new TextField { value = ReadString(property), isDelayed = true };
            field.AddToClassList("sro-field");

            field.RegisterValueChangedCallback(evt =>
            {
                Write(property, evt.newValue);
                onEdited?.Invoke();
            });

            return new SROptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadString(property)),
            };
        }

        // enum 값이 적으면 기본 팝업, 많으면 검색창이 달린 AdvancedDropdown을 띄운다
        private static SROptionField BuildEnum(PropertyReference property, Action onEdited)
        {
            var type = property.PropertyType;
            var nameList = Enum.GetNames(type);

            if (nameList.Length <= EnumSearchThreshold)
            {
                var current = ReadEnum(property);
                var field = new EnumField(current);
                field.AddToClassList("sro-field");

                field.RegisterValueChangedCallback(evt =>
                {
                    Write(property, evt.newValue);
                    onEdited?.Invoke();
                });

                return new SROptionField
                {
                    Element = field,
                    Refresh = () =>
                    {
                        var value = ReadEnum(property);
                        if (Equals(field.value, value) == false) field.SetValueWithoutNotify(value);
                    },
                };
            }

            var button = MakePickerButton(ReadEnum(property).ToString());

            button.clicked += () =>
            {
                var dropdown = new EnumSearchDropdown(new AdvancedDropdownState(), type, picked =>
                {
                    Write(property, picked);
                    button.text = picked.ToString();
                    onEdited?.Invoke();
                });

                dropdown.Show(button.worldBound);
            };

            return new SROptionField
            {
                Element = button,
                Refresh = () =>
                {
                    var text = ReadEnum(property).ToString();
                    if (button.text != text) button.text = text;
                },
            };
        }

        // 드롭다운을 여는 버튼. 오른쪽 끝에 ▾ 를 달아 드롭다운임을 알린다 — text 는 값 표시에만 쓰므로 화살표는 별도 자식이다
        private static Button MakePickerButton(string text)
        {
            var button = new Button { text = text };
            button.AddToClassList("sro-field");
            button.AddToClassList("sro-enum-picker");

            var arrow = new Label("▾");
            arrow.AddToClassList("sro-enum-picker__arrow");
            arrow.pickingMode = PickingMode.Ignore;
            button.Add(arrow);

            return button;
        }

        // SRDynamicDropdownAttribute 가 달린 프로퍼티용 컨트롤. 제공 메서드가 준 목록을 검색창 달린
        // AdvancedDropdown 으로 띄운다 — 목록은 버튼을 누를 때마다 다시 받는다(플레이 중 스펙이 바뀌어도 맞는다)
        private static SROptionField BuildDynamicDropdown(PropertyReference property, Action onEdited)
        {
            string CurrentLabel() => SRDynamicDropdown.LabelOf(SRDynamicDropdown.GetItems(property), ReadObject(property));

            var button = MakePickerButton(CurrentLabel());

            button.clicked += () =>
            {
                var dropdown = new ItemSearchDropdown(new AdvancedDropdownState(), SRDynamicDropdown.GetItems(property), picked =>
                {
                    Write(property, picked.Value);
                    button.text = picked.Label;
                    onEdited?.Invoke();
                });

                dropdown.Show(button.worldBound);
            };

            return new SROptionField
            {
                Element = button,
                Refresh = () =>
                {
                    var text = CurrentLabel();
                    if (button.text != text) button.text = text;
                },
            };
        }

        // int를 제외한 정수 타입(uint, short 등) 프로퍼티용 입력창을 만들고, 타입 범위와 NumberRange로 값을 클램프한다
        private static SROptionField BuildAnyInteger(PropertyReference property, Action onEdited)
        {
            var type = property.PropertyType;
            var userRange = property.GetAttribute<NumberRangeAttribute>();

            var field = new LongField { value = ReadLong(property), isDelayed = true };
            field.AddToClassList("sro-field");

            field.RegisterValueChangedCallback(evt =>
            {
                long value = Math.Clamp(evt.newValue, IntegerMinLookup[type], IntegerMaxLookup[type]);

                if (userRange != null)
                {
                    value = Math.Clamp(value, (long)userRange.Min, (long)userRange.Max);
                }

                Write(property, Convert.ChangeType(value, type));
                SetIfChanged(field, value);
                onEdited?.Invoke();
            });

            return new SROptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadLong(property)),
            };
        }

        // [Serializable] 클래스·구조체는 공개/비공개 필드를 접이식으로 펼쳐 편집한다
        private static SROptionField BuildNested(PropertyReference property, Action onEdited)
        {
            var container = new VisualElement();
            container.AddToClassList("sro-nested");

            var target = ReadObject(property);

            if (target == null)
            {
                container.Add(new Label("null") { tooltip = "값이 비어 있어 편집할 수 없다" });
                return new SROptionField { Element = container, Refresh = () => { } };
            }

            var foldout = new Foldout { text = target.GetType().Name, value = false };
            container.Add(foldout);

            var refreshList = new List<Action>();
            BuildNestedFields(target, foldout.contentContainer, refreshList, () =>
            {
                // 구조체는 값 복사본이므로 편집 후 프로퍼티에 되돌려 써야 반영된다
                Write(property, target);
                onEdited?.Invoke();
            });

            return new SROptionField
            {
                Element = container,
                Refresh = () =>
                {
                    foreach (var refresh in refreshList) refresh();
                },
            };
        }

        // target의 필드를 리플렉션으로 순회하며 필드마다 편집 행을 만들어 parent에 추가하고, 갱신 함수를 refreshList에 모은다
        private static void BuildNestedFields(object target, VisualElement parent, List<Action> refreshList, Action onEdited)
        {
            var type = target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var fieldInfo in type.GetFields(flags))
            {
                if (Attribute.IsDefined(fieldInfo, typeof(NonSerializedAttribute))) continue;

                var reference = new PropertyReference(
                    fieldInfo.FieldType,
                    () => fieldInfo.GetValue(target),
                    value => fieldInfo.SetValue(target, value));

                var built = Build(reference, onEdited);

                var row = new VisualElement();
                row.AddToClassList("sro-row");

                var label = new Label(ObjectNames.NicifyVariableName(fieldInfo.Name));
                label.AddToClassList("sro-label");
                row.Add(label);

                built.Element.AddToClassList("sro-control");
                row.Add(built.Element);
                parent.Add(row);

                refreshList.Add(built.Refresh);
            }
        }

        // 지원하지 않는 타입임을 알리는 라벨만 만든다
        private static SROptionField BuildUnsupported(Type type)
        {
            var label = new Label($"지원하지 않는 타입: {type.Name}");
            label.AddToClassList("sro-unsupported");

            return new SROptionField { Element = label, Refresh = () => { } };
        }

        // 중첩 편집(BuildNested)으로 펼칠 수 있는 타입인지 판별한다 — 값 타입이거나 [Serializable] 클래스
        private static bool IsSerializableClassOrStruct(Type type)
        {
            if (type.IsPrimitive) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;

            return type.IsValueType || Attribute.IsDefined(type, typeof(SerializableAttribute));
        }

        // 편집 중인 필드를 건드리지 않도록, 값이 실제로 달라졌을 때만 통지 없이 덮어쓴다
        private static void SetIfChanged<T>(BaseField<T> field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field.value, value)) return;
            if (field.focusController != null && field.focusController.focusedElement == field) return;

            field.SetValueWithoutNotify(value);
        }

        // 프로퍼티에 값을 쓴다. 쓰기 불가능하면 무시하고, 세터가 던지는 예외는 로그만 남긴다
        private static void Write(PropertyReference property, object value)
        {
            if (property.CanWrite == false) return;

            try
            {
                property.SetValue(value);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static object ReadObject(PropertyReference property)
        {
            try
            {
                return property.CanRead ? property.GetValue() : null;
            }
            catch (Exception)
            {
                // 게임이 돌지 않을 때 게터가 던질 수 있다. 값 없음으로 취급한다
                return null;
            }
        }

        private static bool ReadBool(PropertyReference property)
        {
            return ReadObject(property) is bool value && value;
        }

        private static int ReadInt(PropertyReference property)
        {
            var raw = ReadObject(property);
            return raw == null ? 0 : Convert.ToInt32(raw);
        }

        private static long ReadLong(PropertyReference property)
        {
            var raw = ReadObject(property);
            return raw == null ? 0L : Convert.ToInt64(raw);
        }

        private static float ReadFloat(PropertyReference property)
        {
            var raw = ReadObject(property);
            return raw == null ? 0f : Convert.ToSingle(raw);
        }

        private static double ReadDouble(PropertyReference property)
        {
            var raw = ReadObject(property);
            return raw == null ? 0d : Convert.ToDouble(raw);
        }

        private static string ReadString(PropertyReference property)
        {
            return ReadObject(property) as string ?? string.Empty;
        }

        private static Enum ReadEnum(PropertyReference property)
        {
            var raw = ReadObject(property);
            if (raw is Enum value) return value;

            return (Enum)Enum.ToObject(property.PropertyType, 0);
        }

        // 값이 많은 enum을 검색해서 고르는 팝업
        // 동적 드롭다운 항목(SRDropdownItem) 목록을 검색창 달린 드롭다운으로 띄운다
        private sealed class ItemSearchDropdown : AdvancedDropdown
        {
            private readonly IList<SRDropdownItem> itemList;
            private readonly Action<SRDropdownItem> onPicked;

            public ItemSearchDropdown(AdvancedDropdownState state, IList<SRDropdownItem> itemList, Action<SRDropdownItem> onPicked) : base(state)
            {
                this.itemList = itemList;
                this.onPicked = onPicked;
                minimumSize = new Vector2(240f, 320f);
            }

            // 항목 라벨을 드롭다운 트리에 나열한다
            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("항목");

                foreach (var item in itemList)
                {
                    root.AddChild(new AdvancedDropdownItem(item.Label));
                }

                return root;
            }

            // id는 AdvancedDropdown 이 이름 해시로 덮어쓰므로 라벨로 항목을 되찾는다. 라벨이 겹치면 앞의 것이 이긴다
            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item == null) return;

                foreach (var candidate in itemList)
                {
                    if (candidate.Label != item.name) continue;

                    onPicked?.Invoke(candidate);
                    return;
                }
            }
        }

        private sealed class EnumSearchDropdown : AdvancedDropdown
        {
            private readonly Type enumType;
            private readonly string[] nameList;
            private readonly Action<Enum> onPicked;

            public EnumSearchDropdown(AdvancedDropdownState state, Type enumType, Action<Enum> onPicked) : base(state)
            {
                this.enumType = enumType;
                this.onPicked = onPicked;

                nameList = Enum.GetNames(enumType);
                minimumSize = new Vector2(240f, 320f);
            }

            // enum 값 이름들을 드롭다운 트리 항목으로 나열한다
            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem(enumType.Name);

                foreach (var name in nameList)
                {
                    root.AddChild(new AdvancedDropdownItem(name));
                }

                return root;
            }

            // 선택된 항목의 이름으로 enum 값을 찾아 콜백에 넘긴다.
            // id는 AdvancedDropdown 내부 데이터 소스가 이름 해시로 덮어쓰므로 여기 기대면 안 된다.
            // 루트 항목이나 검색 머리글처럼 enum 이름이 아닌 것이 오면 무시한다
            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item == null) return;
                if (Enum.IsDefined(enumType, item.name) == false) return;

                onPicked?.Invoke((Enum)Enum.Parse(enumType, item.name));
            }
        }
    }
}
#endif
