using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class MainMenuInitializer : MonoBehaviour
{
    [SerializeField] AudioClip mainMenuBgm;
    [SerializeField] List<CardData> allCards;

    void Awake()
    {
        DeckSaveManager.SetCardRegistry(this.allCards);
        DeckSaveManager.LoadFromFile();
        OutgameSaveManager.Load();
    }

    void Start() => SoundManager.Instance?.PlayBGM(mainMenuBgm);
}
