#if !DISABLE_SRDEBUGGER
using SRDebugger.UI.Tabs;
using SRDebugger.Services;
using SRF.Service;
using UnityEngine;

namespace HeroSiege.Debugging.CheatPanel
{
    // SRDebugger의 옵션 탭 자리를 새 치트 화면으로 바꿔 놓는다.
    // SRDebugger 코드도 프리팹도 고치지 않는다 — 패널이 처음 열릴 때 만들어진 옵션 탭을 찾아
    // 그 안의 uGUI 내용만 꺼 두고, 같은 자리에 UI Toolkit 화면을 겹친다
    public static class CheatPanelBootstrap
    {
        private static CheatPanelController controller;

        internal static bool IsDebugPanelVisible()
        {
            // Instance 조회는 서비스가 없으면 경고와 자동 생성을 유발한다.
            // 재컴파일·종료 중에는 등록된 서비스만 확인하고 다음 프레임을 기다린다.
            if (!Application.isPlaying || !SRServiceManager.HasService<IDebugService>()) return false;
            var service = SRDebug.Instance;
            return service != null && service.IsDebugPanelVisible;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // 이 프로젝트는 SRDebugger의 활성 설정을 따른다.
            if (!SRDebugger.Settings.Instance.IsEnabled) return;
            SRDebug.Init();

            var host = new GameObject("CheatPanelBootstrap");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<CheatPanelInstallModule>();
        }

        // 옵션 탭을 넘겨받는다. 아직 탭이 없으면 false를 돌려주고 다음 기회를 기다린다
        internal static bool TryTakeOver()
        {
            if (controller != null) return true;

            var optionsTab = Object.FindFirstObjectByType<OptionsTabController>(FindObjectsInactive.Include);
            if (optionsTab == null) return false;

            // 스크롤·핀 버튼이 우리 화면 밑에서 계속 돌지 않게 컴포넌트부터 멈춘다
            optionsTab.enabled = false;

            var area = optionsTab.transform as RectTransform;

            for (int i = 0; i < area.childCount; i++)
                area.GetChild(i).gameObject.SetActive(false);

            controller = CheatPanelController.Create(area, optionsTab.GetComponentInParent<Canvas>());
            return controller != null;
        }
    }

    // 디버그 패널이 처음 열리는 순간을 잡아 탭을 넘겨받고 스스로 꺼진다.
    // 가시성 이벤트는 패널이 만들어지기 전의 첫 표시를 놓쳐서, 상태를 직접 본다
    internal sealed class CheatPanelInstallModule : MonoBehaviour
    {
        private void Update()
        {
            if (CheatPanelBootstrap.IsDebugPanelVisible() == false) return;
            if (CheatPanelBootstrap.TryTakeOver() == false) return;

            enabled = false;
        }
    }
}
#endif
