using UnityEngine;
using UnityEngine.SceneManagement;

// 로비 씬 상주. 첫실행 유저(소유 0)면 로비를 스치자마자 스타터팩 개봉 씬(PackTest)으로 전환한다.
// 별도 BootScene 없이, 앱은 기존대로 LobbyScene(index 0)으로 부팅하고 여기서 첫실행만 갈라낸다
//   — 상점→팩 씬 전환과 동일 경로를 첫실행이 자동으로 탄다.
//
// 순서: MainMenuInitializer.Awake([DefaultExecutionOrder(-100)])가 CardCatalog.SetSource·OwnershipManager.Init·
//   CardPackOpener.SetShop을 끝낸 뒤 이 Start(기본 순서)가 돌므로, HasAnyOwnedSaved·TryPurchase가 준비돼 있다.
//
// 경계: 구매·소유·차감은 CardPackOpener.TryPurchase가 원자 영속하고, 획득 후 목적지는 캐리어(PackHandoff)에
//   실어 확정한다 — PackTest 컨트롤러는 첫시작 재판정 없이 캐리어 값으로만 분기.
public class LobbyFirstRunRedirect : MonoBehaviour
{
    // 첫실행 개봉 씬(스타터팩을 사서 여기로 보낸다).
    [SerializeField] string packOpenScene = "PackTest";
    // 첫실행 스타터팩 packId(CardShop의 CardPackData.PackId와 일치해야 함).
    [SerializeField] string starterPackId = "starter";
    // 첫실행 개봉 후 최종 목적지(획득 → 튜토리얼 전투). 캐리어로 실어 넘긴다.
    [SerializeField] string battleSceneName = "BattleScene";

    void Start()
    {
        // 첫실행 판정은 세이브 소유 여부만으로(단일 창구 OwnershipManager, 세이브 직접 조회).
        if (OwnershipManager.HasAnyOwnedSaved()) return;   // 기존 유저 → 로비 그대로

        var t_opened = CardPackOpener.TryPurchase(this.starterPackId);
        if (t_opened == null || !t_opened.Success)
        {
            // 스타터팩 미배선/빈 풀 등 — 전환 안 하고 로비에 머문다 + 경고(진행도 훼손 없음).
            var t_result = t_opened != null ? t_opened.Result.ToString() : "null";
            Debug.LogWarning($"[LobbyFirstRunRedirect] 첫실행 스타터팩 구매 실패(packId={this.starterPackId}, result={t_result}) — 로비 유지.");
            return;
        }

        // 획득 후 튜토리얼 전투로 가라(+튜토리얼 시작)를 캐리어에 싣고 개봉 씬으로 전환.
        PackHandoff.Set(t_opened, this.battleSceneName, true);
        SceneManager.LoadScene(this.packOpenScene);
    }
}
