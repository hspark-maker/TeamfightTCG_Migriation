#if !DISABLE_SRDEBUGGER
using UnityEngine;
using UnityEngine.UIElements;

namespace HeroSiege.Debugging.CheatPanel
{
    // 치트 화면(UI Toolkit)을 띄우고 SRDebugger의 옵션 탭 자리에 겹쳐 놓는 진입점.
    // uGUI 캔버스가 아니라 별도 런타임 패널이라, 덮을 영역을 매 프레임 uGUI 좌표에서 받아 맞춘다
    public sealed class CheatPanelController : MonoBehaviour
    {
        #region Static

        // SRDebugger 폴더 안의 Resources다 — SRDebugger를 끄면 그 폴더가 통째로 _DISABLED~ 로 바뀌어
        // 화면 에셋도 함께 임포트에서 빠진다. _Project에 새 Resources 루트를 만들지 않으려는 배치이기도 하다
        private const string UxmlPath = "SRDebugger/UI/CheatPanel/CheatPanel";
        private const string PanelSettingsPath = "SRDebugger/UI/CheatPanel/CheatPanelSettings";

        // 화면에 보이는 값들을 실제 옵션 값에 맞추는 주기(초). 치트 화면이라 촘촘할 필요가 없다
        private const float RefreshInterval = 0.25f;

        // 덮을 uGUI 영역을 받아 치트 화면을 만든다. 필요한 에셋이 없으면 null을 돌려준다
        public static CheatPanelController Create(RectTransform area, Canvas canvas)
        {
            var tree = Resources.Load<VisualTreeAsset>(UxmlPath);
            var settings = Resources.Load<PanelSettings>(PanelSettingsPath);

            if (tree == null || settings == null)
            {
                Debug.LogError($"[CheatPanel] 화면 에셋을 찾지 못했다 (Resources/{UxmlPath}, Resources/{PanelSettingsPath})");
                return null;
            }

            var host = new GameObject("CheatPanel");
            DontDestroyOnLoad(host);

            var controller = host.AddComponent<CheatPanelController>();
            controller.Initialize(tree, settings, area, canvas);

            return controller;
        }

        #endregion

        // 매 프레임 영역을 재는 데 쓰는 버퍼. 프레임마다 새로 잡으면 GC가 생긴다
        private readonly Vector3[] cornerList = new Vector3[4];

        private UIDocument document;
        private PanelSettings settings;
        private CheatPanelView view;
        private VisualElement rootElement;

        private RectTransform area;
        private Canvas canvas;

        private float refreshTimer;
        private Rect lastRect;
        private bool lastVisible;

        private void Update()
        {
            bool visible = IsAreaVisible();

            if (visible != lastVisible)
            {
                lastVisible = visible;
                rootElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

                // 탭을 다시 열 때마다 옵션을 새로 스캔한다 — 플레이 중 늘어난 항목이 반영된다
                if (visible) view.Rebuild(SROptions.Current);
            }

            if (visible == false) return;

            FollowArea();

            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer < RefreshInterval) return;

            refreshTimer = 0f;
            view.RefreshValues();
        }

        private void OnDestroy()
        {
            if (settings != null) Destroy(settings);
        }

        // UI Toolkit 문서를 만들어 화면을 세우고, 덮을 영역을 기억한다
        private void Initialize(VisualTreeAsset tree, PanelSettings sourceSettings, RectTransform area, Canvas canvas)
        {
            this.area = area;
            this.canvas = canvas;

            // 원본 에셋을 그대로 쓰면 배율·정렬 순서를 건드릴 때 프로젝트 에셋이 더러워진다
            settings = Instantiate(sourceSettings);
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.sortingOrder = canvas == null ? 100f : canvas.sortingOrder + 1;

            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = tree;

            rootElement = document.rootVisualElement.Q<VisualElement>("cheat-root");
            rootElement.style.position = Position.Absolute;
            rootElement.style.display = DisplayStyle.None;

            view = new CheatPanelView(rootElement);
            view.Rebuild(SROptions.Current);
        }

        // 덮을 대상이 화면에 떠 있는지. 탭이 바뀌면 대상 오브젝트가 꺼지고, 패널 자체가 닫히면 서비스가 알려 준다
        private bool IsAreaVisible()
        {
            if (area == null) return false;
            if (area.gameObject.activeInHierarchy == false) return false;

            return CheatPanelBootstrap.IsDebugPanelVisible();
        }

        // uGUI 영역의 화면 좌표를 UI Toolkit 패널 좌표로 옮겨 루트 위치·크기를 맞춘다
        private void FollowArea()
        {
            float scale = canvas == null ? 1f : canvas.scaleFactor;
            if (scale <= 0f) scale = 1f;

            settings.scale = scale;

            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            area.GetWorldCorners(cornerList);

            var min = RectTransformUtility.WorldToScreenPoint(camera, cornerList[0]);
            var max = RectTransformUtility.WorldToScreenPoint(camera, cornerList[2]);

            // 탭 영역이 노치·홈 인디케이터 밑까지 뻗는 기기가 있다(실측: 1170x2532 화면에서 아래로 102px).
            // 세이프에어리어와의 교집합만 남긴다 — 교집합이라 이미 여백이 있는 화면에서 두 번 깎이지 않는다
            var safeArea = Screen.safeArea;

            float left = Mathf.Max(min.x, safeArea.xMin);
            float right = Mathf.Min(max.x, safeArea.xMax);
            float bottom = Mathf.Max(min.y, safeArea.yMin);
            float top = Mathf.Min(max.y, safeArea.yMax);

            // 겹치는 데가 없으면(탭이 화면 밖으로 밀린 순간 등) 지난 배치를 그대로 둔다
            if (right <= left || top <= bottom) return;

            // 패널 좌표는 왼쪽 위가 원점이고 아래로 자라므로 y를 뒤집는다
            var rect = new Rect(left / scale, (Screen.height - top) / scale, (right - left) / scale, (top - bottom) / scale);

            if (rect == lastRect) return;
            lastRect = rect;

            rootElement.style.left = rect.x;
            rootElement.style.top = rect.y;
            rootElement.style.width = rect.width;
            rootElement.style.height = rect.height;

            view.SetLayout(rect.width, rect.height);
        }
    }
}
#endif
