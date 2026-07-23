using UnityEngine;

[DefaultExecutionOrder(-100)]
public class MainMenuInitializer : MonoBehaviour
{
    [SerializeField] AudioClip mainMenuBgm;
    // 카드 목록은 CardRegistry(SO)가 단일 진실원. 씬에 사본을 두면 카드 추가 시 한쪽만 갱신된다.
    [SerializeField] CardRegistry cardRegistry;

    void Awake()
    {
        if (this.cardRegistry == null)
            Debug.LogError("[MainMenuInitializer] cardRegistry 미배선 — 저장된 덱을 복원할 수 없다.");
        else
            DeckSaveManager.SetCardRegistry(this.cardRegistry.All);
        DeckSaveManager.LoadFromFile();
    }

    void Start() => SoundManager.Instance?.PlayBGM(mainMenuBgm);
}
