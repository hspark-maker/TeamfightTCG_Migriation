using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class MainMenuInitializer : MonoBehaviour
{
    [SerializeField] AudioClip mainMenuBgm;
    [SerializeField] List<CardData> allCards;

    void Awake()
    {
        // 카드 레지스트리는 인스펙터에 담긴 씬 데이터라 여기서 설정한다.
        // 세이브 로드·재화 캐싱 등 씬 무관한 전역 부트는 GameManager가 앱 시작 시 처리한다.
        DeckSaveManager.SetCardRegistry(this.allCards);
        DeckSaveManager.LoadFromFile();
    }

    void Start() => SoundManager.Instance?.PlayBGM(mainMenuBgm);
}
