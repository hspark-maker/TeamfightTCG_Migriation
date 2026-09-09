#if !DISABLE_SRDEBUGGER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SRDebugger;
using SRF.Helpers;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UIElements;

namespace HeroSiege.Debugging.CheatPanel
{
    // 옵션 하나를 편집하는 컨트롤과, 외부에서 값이 바뀌었을 때 화면을 맞추는 갱신 함수
    public sealed class CheatOptionField
    {
        public VisualElement Element;
        public Action Refresh;
    }

    // OptionDefinition의 타입을 보고 알맞은 런타임 UI Toolkit 컨트롤을 만든다.
    // 에디터 전용 API(AdvancedDropdown · ObjectNames)를 안 쓰고, 값 목록은 CheatPickerOverlay로 고른다
    public sealed class CheatFieldFactory
    {
        #region Static

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

        // 눌린 버튼과 그 줄을 잠깐 강조해 터치가 먹었음을 보여준다
        private static void FlashFired(Button button)
        {
            if (button == null) return;

            var line = button.parent;

            while (line != null && line.ClassListContains("cheat-group") == false)
            {
                line = line.parent;
            }

            button.AddToClassList("cheat-action--fired");
            line?.AddToClassList("cheat-group--fired");

            button.schedule.Execute(() =>
            {
                button.RemoveFromClassList("cheat-action--fired");
                line?.RemoveFromClassList("cheat-group--fired");
            }).StartingIn(FiredFlashMs);
        }

        // 필드 이름을 읽기 좋게 띄운다. 에디터의 ObjectNames.NicifyVariableName을 대신하는 최소 구현이다
        private static string NicifyName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            int start = 0;
            if (name.Length > 1 && name[0] == 'm' && name[1] == '_') start = 2;
            else if (name[0] == '_') start = 1;

            var builder = new StringBuilder(name.Length + 4);

            for (int i = start; i < name.Length; i++)
            {
                char c = name[i];

                if (i > start && char.IsUpper(c) && char.IsUpper(name[i - 1]) == false) builder.Append(' ');
                if (i == start) c = char.ToUpperInvariant(c);

                builder.Append(c);
            }

            return builder.ToString();
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

        // 프로퍼티 값을 읽는다. 아직 생성되지 않은 시스템의 게터 예외는 값 없음으로 처리한다
        private static object ReadObject(PropertyReference property)
        {
            try
            {
                return property.CanRead ? property.GetValue() : null;
            }
            catch (Exception)
            {
                // 해당 시스템이 아직 없을 때 게터가 던질 수 있다. 값 없음으로 취급한다
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

        // 목록을 여는 버튼. 오른쪽 끝의 ▾ 는 값 표시(text)와 섞이면 안 되므로 별도 자식이다
        private static Button MakePickerButton(string text)
        {
            var button = new Button { text = text };
            button.AddToClassList("cheat-field");
            button.AddToClassList("cheat-picker-button");

            var arrow = new Label("▾");
            arrow.AddToClassList("cheat-picker-button__arrow");
            arrow.pickingMode = PickingMode.Ignore;
            button.Add(arrow);

            return button;
        }

        // 지원하지 않는 타입임을 알리는 라벨만 만든다
        private static CheatOptionField BuildUnsupported(Type type)
        {
            var label = new Label($"지원하지 않는 타입: {type.Name}");
            label.AddToClassList("cheat-unsupported");

            return new CheatOptionField { Element = label, Refresh = () => { } };
        }

        // 중첩 편집(BuildNested)으로 펼칠 수 있는 타입인지 판별한다 — 값 타입이거나 [Serializable] 클래스
        private static bool IsSerializableClassOrStruct(Type type)
        {
            if (type.IsPrimitive) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;

            return type.IsValueType || Attribute.IsDefined(type, typeof(SerializableAttribute));
        }

        #endregion

        private readonly CheatPickerOverlay picker;

        public CheatFieldFactory(CheatPickerOverlay picker)
        {
            this.picker = picker;
        }

        // 프로퍼티 편집 컨트롤을 만든다. onEdited는 값이 실제로 바뀌었을 때만 불린다
        public CheatOptionField CreateProperty(CheatOptionEntry entry, Action onEdited)
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
        public Button CreateMethodButton(CheatOptionEntry entry, Action onInvoked)
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
            };

            button.AddToClassList("cheat-action");
            return button;
        }

        // 프로퍼티 타입을 보고 알맞은 편집 컨트롤 생성 함수로 분기한다
        private CheatOptionField Build(PropertyReference property, Action onEdited)
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
        private CheatOptionField BuildToggle(PropertyReference property, Action onEdited)
        {
            var toggle = new Toggle { value = ReadBool(property) };
            toggle.AddToClassList("cheat-field");

            toggle.RegisterValueChangedCallback(evt =>
            {
                Write(property, evt.newValue);
                onEdited?.Invoke();
            });

            return new CheatOptionField
            {
                Element = toggle,
                Refresh = () => SetIfChanged(toggle, ReadBool(property)),
            };
        }

        // int 프로퍼티용 컨트롤을 만든다. NumberRange가 있으면 슬라이더, 없으면 정수 입력창을 쓴다
        private CheatOptionField BuildInt(PropertyReference property, Action onEdited)
        {
            var range = property.GetAttribute<NumberRangeAttribute>();

            if (range != null)
            {
                var slider = new SliderInt((int)range.Min, (int)range.Max)
                {
                    value = ReadInt(property),
                    showInputField = true,
                };
                slider.AddToClassList("cheat-field");

                slider.RegisterValueChangedCallback(evt =>
                {
                    Write(property, Convert.ChangeType(evt.newValue, property.PropertyType));
                    onEdited?.Invoke();
                });

                return new CheatOptionField
                {
                    Element = slider,
                    Refresh = () => SetIfChanged(slider, ReadInt(property)),
                };
            }

            var field = new IntegerField { value = ReadInt(property), isDelayed = true };
            field.AddToClassList("cheat-field");

            field.RegisterValueChangedCallback(evt =>
            {
                Write(property, Convert.ChangeType(evt.newValue, property.PropertyType));
                onEdited?.Invoke();
            });

            return new CheatOptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadInt(property)),
            };
        }

        // float 프로퍼티용 컨트롤을 만든다. NumberRange가 있으면 슬라이더, 없으면 실수 입력창을 쓴다
        private CheatOptionField BuildFloat(PropertyReference property, Action onEdited)
        {
            var range = property.GetAttribute<NumberRangeAttribute>();

            if (range != null)
            {
                var slider = new Slider((float)range.Min, (float)range.Max)
                {
                    value = ReadFloat(property),
                    showInputField = true,
                };
                slider.AddToClassList("cheat-field");

                slider.RegisterValueChangedCallback(evt =>
                {
                    Write(property, Convert.ChangeType(evt.newValue, property.PropertyType));
                    onEdited?.Invoke();
                });

                return new CheatOptionField
                {
                    Element = slider,
                    Refresh = () => SetIfChanged(slider, ReadFloat(property)),
                };
            }

            var field = new FloatField { value = ReadFloat(property), isDelayed = true };
            field.AddToClassList("cheat-field");

            field.RegisterValueChangedCallback(evt =>
            {
                Write(property, Convert.ChangeType(evt.newValue, property.PropertyType));
                onEdited?.Invoke();
            });

            return new CheatOptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadFloat(property)),
            };
        }

        // double 프로퍼티용 입력창을 만든다. NumberRange가 있으면 입력값을 클램프한다
        private CheatOptionField BuildDouble(PropertyReference property, Action onEdited)
        {
            var range = property.GetAttribute<NumberRangeAttribute>();
            var field = new DoubleField { value = ReadDouble(property), isDelayed = true };
            field.AddToClassList("cheat-field");

            field.RegisterValueChangedCallback(evt =>
            {
                double value = evt.newValue;

                if (range != null) value = Math.Clamp(value, range.Min, range.Max);

                Write(property, value);
                SetIfChanged(field, value);
                onEdited?.Invoke();
            });

            return new CheatOptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadDouble(property)),
            };
        }

        // string 프로퍼티용 텍스트 입력창을 만든다
        private CheatOptionField BuildString(PropertyReference property, Action onEdited)
        {
            var field = new TextField { value = ReadString(property), isDelayed = true };
            field.AddToClassList("cheat-field");

            field.RegisterValueChangedCallback(evt =>
            {
                Write(property, evt.newValue);
                onEdited?.Invoke();
            });

            return new CheatOptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadString(property)),
            };
        }

        // enum 프로퍼티는 값 개수와 무관하게 검색 오버레이로 고른다 — 터치에서는 작은 팝업이 쓰기 어렵다
        private CheatOptionField BuildEnum(PropertyReference property, Action onEdited)
        {
            var type = property.PropertyType;
            var button = MakePickerButton(ReadEnum(property).ToString());

            button.clicked += () =>
            {
                using (ListPool<CheatPickerItem>.Get(out var itemList))
                {
                    foreach (var name in Enum.GetNames(type))
                        itemList.Add(new CheatPickerItem(Enum.Parse(type, name), name));

                    picker.Show(type.Name, itemList, item =>
                    {
                        Write(property, item.Value);
                        button.text = item.Label;
                        onEdited?.Invoke();
                    });
                }
            };

            return new CheatOptionField
            {
                Element = button,
                Refresh = () =>
                {
                    var text = ReadEnum(property).ToString();
                    if (button.text != text) button.text = text;
                },
            };
        }

        // SRDynamicDropdownAttribute가 달린 프로퍼티용 컨트롤. 제공 메서드가 준 목록을 검색 오버레이로 띄운다 —
        // 목록은 버튼을 누를 때마다 다시 받는다(플레이 중 스펙이 바뀌어도 맞는다)
        private CheatOptionField BuildDynamicDropdown(PropertyReference property, Action onEdited)
        {
            string CurrentLabel() => SRDynamicDropdown.LabelOf(SRDynamicDropdown.GetItems(property), ReadObject(property));

            var button = MakePickerButton(CurrentLabel());

            button.clicked += () =>
            {
                using (ListPool<CheatPickerItem>.Get(out var itemList))
                {
                    foreach (var source in SRDynamicDropdown.GetItems(property))
                        itemList.Add(new CheatPickerItem(source.Value, source.Label));

                    picker.Show("항목 선택", itemList, item =>
                    {
                        Write(property, item.Value);
                        button.text = item.Label;
                        onEdited?.Invoke();
                    });
                }
            };

            return new CheatOptionField
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
        private CheatOptionField BuildAnyInteger(PropertyReference property, Action onEdited)
        {
            var type = property.PropertyType;
            var userRange = property.GetAttribute<NumberRangeAttribute>();

            var field = new LongField { value = ReadLong(property), isDelayed = true };
            field.AddToClassList("cheat-field");

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

            return new CheatOptionField
            {
                Element = field,
                Refresh = () => SetIfChanged(field, ReadLong(property)),
            };
        }

        // [Serializable] 클래스·구조체는 공개/비공개 필드를 접이식으로 펼쳐 편집한다
        private CheatOptionField BuildNested(PropertyReference property, Action onEdited)
        {
            var container = new VisualElement();
            container.AddToClassList("cheat-nested");

            var target = ReadObject(property);

            if (target == null)
            {
                container.Add(new Label("null") { tooltip = "값이 비어 있어 편집할 수 없다" });
                return new CheatOptionField { Element = container, Refresh = () => { } };
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

            return new CheatOptionField
            {
                Element = container,
                Refresh = () =>
                {
                    foreach (var refresh in refreshList) refresh();
                },
            };
        }

        // target의 필드를 리플렉션으로 순회하며 필드마다 편집 행을 만들어 parent에 추가하고, 갱신 함수를 refreshList에 모은다
        private void BuildNestedFields(object target, VisualElement parent, List<Action> refreshList, Action onEdited)
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
                row.AddToClassList("cheat-row");

                var label = new Label(NicifyName(fieldInfo.Name));
                label.AddToClassList("cheat-row__label");
                row.Add(label);

                built.Element.AddToClassList("cheat-row__control");
                row.Add(built.Element);
                parent.Add(row);

                refreshList.Add(built.Refresh);
            }
        }
    }
}
#endif
