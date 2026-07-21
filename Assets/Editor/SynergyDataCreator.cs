using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class SynergyDataCreator
{
    struct SynergyDef
    {
        public string name;
        public int    required;
        public string effect;
        public Color  color;
    }

    struct CardSynergyDef
    {
        public string card;
        public string main;
        public string sub;
    }

    static readonly SynergyDef[] SYNERGIES = new SynergyDef[]
    {
        // ── 메인 시너지 (3장 활성화) ─────────────────────────────────────
        new SynergyDef { name = "N.O.V.A",    required = 3, color = new Color(0.40f, 0.70f, 1.00f),
            effect = "적을 처치하면 모든 N.O.V.A 카드가 추가 생명력 3 획득." },
        new SynergyDef { name = "정령족",      required = 3, color = new Color(0.30f, 0.85f, 0.55f),
            effect = "매 턴 종료 시 정령족 카드 생명력 1 회복 + 추가 생명력 2 획득. 아군 사망 시 생명력 2인 정령 토큰 소환." },
        new SynergyDef { name = "우주 그루브", required = 3, color = new Color(0.85f, 0.45f, 1.00f),
            effect = "카드가 전장에 나올 때 그루브 중첩 +1. 중첩 1당 적이 받는 피해 +1." },

        // ── 서브 시너지 (2장 활성화) ─────────────────────────────────────
        new SynergyDef { name = "싸움꾼", required = 2, color = new Color(0.90f, 0.40f, 0.30f),
            effect = "추가 생명력 +3." },
        new SynergyDef { name = "요새",   required = 2, color = new Color(0.60f, 0.65f, 0.75f),
            effect = "받는 피해 1 감소." },
        new SynergyDef { name = "습격자", required = 2, color = new Color(0.95f, 0.70f, 0.20f),
            effect = "공격 후 생존 시 준 피해의 절반만큼 생명력 회복." },
        new SynergyDef { name = "불한당", required = 2, color = new Color(0.55f, 0.30f, 0.70f),
            effect = "피격 후 생존 시 아직 출격하지 않은 카드와 교체." },
    };

    static readonly CardSynergyDef[] CARD_MAP = new CardSynergyDef[]
    {
        new CardSynergyDef { card = "아트록스", main = "N.O.V.A",    sub = "습격자" },
        new CardSynergyDef { card = "마오카이", main = "N.O.V.A",    sub = "싸움꾼" },
        new CardSynergyDef { card = "킨드레드", main = "N.O.V.A",    sub = "불한당" },
        new CardSynergyDef { card = "람머스",   main = "정령족",      sub = "요새"   },
        new CardSynergyDef { card = "뽀삐",     main = "정령족",      sub = "싸움꾼" },
        new CardSynergyDef { card = "피즈",     main = "정령족",      sub = "불한당" },
        new CardSynergyDef { card = "오른",     main = "우주 그루브", sub = "요새"   },
        new CardSynergyDef { card = "티모",     main = "우주 그루브", sub = "불한당" },
        new CardSynergyDef { card = "그웬",     main = "우주 그루브", sub = "습격자" },
    };

    [MenuItem("Tools/Create Synergy Data Assets")]
    public static void CreateSynergies()
    {
        const string FOLDER = "Assets/Synergies";
        if (!AssetDatabase.IsValidFolder(FOLDER))
            AssetDatabase.CreateFolder("Assets", "Synergies");

        // 시너지 SO 생성
        var t_map = new Dictionary<string, SynergyData>();
        foreach (var t_def in SYNERGIES)
        {
            string t_path = $"{FOLDER}/{t_def.name}.asset";
            SynergyData t_data = AssetDatabase.LoadAssetAtPath<SynergyData>(t_path);
            if (t_data == null)
            {
                t_data = ScriptableObject.CreateInstance<SynergyData>();
                AssetDatabase.CreateAsset(t_data, t_path);
            }

            SerializedObject t_so = new SerializedObject(t_data);
            t_so.FindProperty("displayName").stringValue    = t_def.name;
            t_so.FindProperty("requiredCount").intValue     = t_def.required;
            t_so.FindProperty("effectDescription").stringValue = t_def.effect;
            t_so.FindProperty("color").colorValue           = t_def.color;
            t_so.ApplyModifiedProperties();

            t_map[t_def.name] = t_data;
        }

        AssetDatabase.SaveAssets();

        // 카드에 시너지 wire
        int t_wired = 0;
        foreach (var t_cm in CARD_MAP)
        {
            string t_cardPath = $"Assets/Cards/{t_cm.card}.asset";
            CardData t_card = AssetDatabase.LoadAssetAtPath<CardData>(t_cardPath);
            if (t_card == null)
            {
                Debug.LogWarning($"[SynergyDataCreator] 카드 없음: {t_cm.card} — Create Card Data Assets 먼저 실행.");
                continue;
            }

            SerializedObject t_cSO = new SerializedObject(t_card);
            t_cSO.FindProperty("mainSynergy").objectReferenceValue = t_map[t_cm.main];
            t_cSO.FindProperty("subClass").objectReferenceValue    = t_map[t_cm.sub];
            t_cSO.ApplyModifiedProperties();
            t_wired++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SynergyDataCreator] 시너지 {SYNERGIES.Length}개 생성, 카드 {t_wired}개 wire 완료.");
    }
}
