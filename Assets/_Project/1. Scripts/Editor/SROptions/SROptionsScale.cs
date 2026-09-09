#if UNITY_EDITOR
using UnityEngine.UIElements;

namespace HeroSiege.Editor.SROptionsUI
{
    // 창 전체의 글자·컨트롤 크기를 배율로 다시 칠한다.
    // USS에 적힌 치수는 배율 1.0 기준이고, 여기 표의 기준값이 그것과 어긋나면 1.0에서 모습이 달라진다.
    internal static class SROptionsScale
    {
        public const float Min = 0.7f;
        public const float Max = 2.5f;
        public const float Step = 0.1f;
        public const float Default = 1f;

        private const float RootFontSize = 12f;

        private static readonly (string ClassName, float Value)[] FontSizeList =
        {
            ("sro-label", 11f),
            ("sro-nav--sub", 11f),
            ("sro-nav__count", 10f),
            ("sro-side__group", 9f),
            ("sro-line__name", 11f),
            ("sro-line__path", 9f),
            ("sro-param__label", 10f),
            ("sro-crumb__title", 13f),
            ("sro-crumb__sub", 11f),
            ("sro-result__name", 11f),
            ("sro-result__path", 9f),
            ("sro-status", 10f),
            ("sro-state", 10f),
            ("sro-banner", 11f),
        };

        private static readonly (string ClassName, float Value)[] HeightList =
        {
            ("sro-toolbar", 30f),
            ("sro-tbtn", 19f),
            ("sro-state", 18f),
            ("sro-nav", 22f),
            ("sro-action", 20f),
            ("sro-status", 19f),
            ("sro-enum-picker", 18f),
        };

        private static readonly (string ClassName, float Value)[] MinHeightList =
        {
            ("sro-row", 21f),
            ("sro-line", 24f),
            ("sro-field", 18f),
            ("sro-result", 22f),
        };

        // 이름 칸은 내용에 맞춰 늘어나므로 최소 폭만 배율을 먹인다 — width를 박으면 긴 이름이 잘린다
        private static readonly (string ClassName, float Value)[] MinWidthList =
        {
            ("sro-line__head", 120f),
        };

        private static readonly (string ClassName, float Value)[] WidthList =
        {
            ("sro-side", 186f),
            ("sro-label", 150f),
            ("sro-result__meta", 150f),
            ("sro-star", 18f),
        };

        // 배율을 허용 범위로 자르고 Step 단위로 맞춘다
        public static float Clamp(float scale)
        {
            float stepped = UnityEngine.Mathf.Round(scale / Step) * Step;
            return UnityEngine.Mathf.Clamp(stepped, Min, Max);
        }

        // root와 그 아래 모든 요소에 배율을 인라인 스타일로 입힌다.
        // 화면을 다시 그릴 때마다 요소가 새로 생기므로 그때마다 다시 불러야 한다
        public static void Apply(VisualElement root, float scale)
        {
            if (root == null) return;

            root.style.fontSize = RootFontSize * scale;

            foreach (var (className, value) in FontSizeList)
                root.Query<VisualElement>(className: className).ForEach(e => e.style.fontSize = value * scale);

            foreach (var (className, value) in HeightList)
                root.Query<VisualElement>(className: className).ForEach(e => e.style.height = value * scale);

            foreach (var (className, value) in MinHeightList)
                root.Query<VisualElement>(className: className).ForEach(e => e.style.minHeight = value * scale);

            foreach (var (className, value) in WidthList)
                root.Query<VisualElement>(className: className).ForEach(e => e.style.width = value * scale);

            foreach (var (className, value) in MinWidthList)
                root.Query<VisualElement>(className: className).ForEach(e => e.style.minWidth = value * scale);

            // 폴드아웃 제목은 Unity 기본 클래스라 위 표로 잡히지 않는다
            root.Query<Label>(className: "unity-foldout__text").ForEach(e => e.style.fontSize = RootFontSize * scale);
        }
    }
}
#endif
