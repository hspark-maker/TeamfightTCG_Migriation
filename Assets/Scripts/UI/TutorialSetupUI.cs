using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using ScriptedAttack = TutorialScenarioData.ScriptedAttack;

/// <summary>
/// 튜토리얼 셋업 씬 진입점. 인스펙터에서 양측 덱(순서=등장순서, 6장 이하)과
/// 스크립트 공격 순서를 직접 저작 → "배틀 시작" 버튼으로 <see cref="TutorialConfig"/> 세팅 후
/// 배틀 씬 로드. 별도 UI 프리팹 불필요 — 최소 캔버스+버튼을 코드로 만든다(DeckSelectPopup 선례).
/// </summary>
public class TutorialSetupUI : MonoBehaviour
{
    [Header("배틀 씬 이름")]
    [SerializeField] string battleSceneName = "BattleScene";

    [Header("고정 덱 (순서 = 등장 순서, 6장 이하)")]
    [SerializeField] List<CardData> playerDeck = new List<CardData>();
    [SerializeField] List<CardData> enemyDeck  = new List<CardData>();

    [Header("스크립트 공격 순서 (턴당 1건, 공격자 슬롯 → 타깃 슬롯)")]
    [SerializeField] List<ScriptedAttack> playerScript = new List<ScriptedAttack>();
    [SerializeField] List<ScriptedAttack> enemyScript  = new List<ScriptedAttack>();

    [Header("선택: SO 시나리오로 위 필드 대체")]
    [SerializeField] TutorialScenarioData scenario;

    void Start() => BuildUI();

    /// <summary>버튼 콜백. TutorialConfig 세팅 후 배틀 씬 로드.</summary>
    public void StartBattle()
    {
        if (this.scenario != null)
            TutorialConfig.Begin(this.scenario);
        else
            TutorialConfig.Begin(this.playerDeck, this.enemyDeck, this.playerScript, this.enemyScript);

        SceneManager.LoadScene(this.battleSceneName);
    }

    // ── 최소 캔버스 + 시작 버튼(코드 빌드) ──────────────────────────────────
    void BuildUI()
    {
        var t_canvasGo = new GameObject("TutorialSetupCanvas");
        t_canvasGo.transform.SetParent(transform, false);
        var t_canvas = t_canvasGo.AddComponent<Canvas>();
        t_canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        t_canvas.sortingOrder = 100;
        var t_scaler = t_canvasGo.AddComponent<CanvasScaler>();
        t_scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        t_scaler.referenceResolution = new Vector2(1080f, 1920f);
        t_canvasGo.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();

        // 안내 라벨
        var t_infoGo = new GameObject("Info");
        t_infoGo.transform.SetParent(t_canvasGo.transform, false);
        var t_info = t_infoGo.AddComponent<TextMeshProUGUI>();
        TutorialUIStyle.ApplyFont(t_info);
        t_info.text      = $"튜토리얼 셋업\n<size=60%>플레이어 {this.playerDeck.Count}장 · 적 {this.enemyDeck.Count}장</size>";
        t_info.fontSize  = 48f;
        t_info.color     = Color.white;
        t_info.alignment = TextAlignmentOptions.Center;
        var t_infoRect = t_info.GetComponent<RectTransform>();
        t_infoRect.anchorMin = t_infoRect.anchorMax = t_infoRect.pivot = new Vector2(0.5f, 0.5f);
        t_infoRect.anchoredPosition = new Vector2(0f, 200f);
        t_infoRect.sizeDelta = new Vector2(800f, 240f);

        // 시작 버튼
        var t_btnGo = new GameObject("StartButton");
        t_btnGo.transform.SetParent(t_canvasGo.transform, false);
        var t_img = t_btnGo.AddComponent<Image>();
        t_img.color = new Color(0.18f, 0.22f, 0.32f, 1f);
        var t_btn = t_btnGo.AddComponent<Button>();
        t_btn.onClick.AddListener(StartBattle);
        var t_btnRect = t_btnGo.GetComponent<RectTransform>();
        t_btnRect.anchorMin = t_btnRect.anchorMax = t_btnRect.pivot = new Vector2(0.5f, 0.5f);
        t_btnRect.anchoredPosition = new Vector2(0f, -120f);
        t_btnRect.sizeDelta = new Vector2(420f, 130f);

        var t_labelGo = new GameObject("Label");
        t_labelGo.transform.SetParent(t_btnGo.transform, false);
        var t_label = t_labelGo.AddComponent<TextMeshProUGUI>();
        TutorialUIStyle.ApplyFont(t_label);
        t_label.text      = "배틀 시작";
        t_label.fontSize  = 42f;
        t_label.color     = Color.white;
        t_label.alignment = TextAlignmentOptions.Center;
        var t_labelRect = t_label.GetComponent<RectTransform>();
        t_labelRect.anchorMin = Vector2.zero;
        t_labelRect.anchorMax = Vector2.one;
        t_labelRect.offsetMin = t_labelRect.offsetMax = Vector2.zero;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var t_es = new GameObject("EventSystem");
        t_es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        t_es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }
}
