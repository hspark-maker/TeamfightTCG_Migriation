using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// 덱 슬롯의 메모리 캐시(영속화 진실원은 DataSaveManager.Data.deck)
// 압축 불변식 — 유효 덱은 항상 [0 .. DeckCount-1]을 연속 점유하고 그 뒤는 전부 빈 칸이다
public static class DeckSaveManager
{
    public const int SLOT_COUNT = 6;
    public const int DECK_SIZE  = 6;

    const string LEGACY_FILE         = "decks.json";
    const string LEGACY_ARCHIVE_FILE = "decks_migrated.json";

    static readonly List<CardData>[] s_slots = new List<CardData>[SLOT_COUNT];
    static readonly string[] s_names = new string[SLOT_COUNT];
    static readonly string[] s_imageKeys = new string[SLOT_COUNT];

    static IReadOnlyList<CardData> s_registry;

    static bool s_loaded;

    // 레거시 덱 이관이 끝나지 않은 상태(이때 슬롯을 새로 쓰면 구 덱이 묻힌다)
    public static bool LegacyMigrationPending { get; private set; }

    // 첫 무효 슬롯 앞까지의 덱 개수
    public static int DeckCount
    {
        get
        {
            for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
                if (!IsSlotValid(t_i)) return t_i;

            return SLOT_COUNT;
        }
    }

    public static bool IsFull => DeckCount >= SLOT_COUNT;

    public static List<CardData> GetSlot(int _index) => s_slots[_index];

    // 저장된 덱 이름 원본(빈 문자열 가능)
    public static string GetName(int _index) => s_names[_index] ?? "";
    public static void SetName(int _index, string _name) => s_names[_index] = _name;

    // 표시용 덱 이름(비어 있으면 인덱스 폴백)
    public static string GetDisplayName(int _index)
        => string.IsNullOrEmpty(s_names[_index]) ? $"덱 {_index + 1}" : s_names[_index];

    // 저장된 이름과 겹치지 않는 첫 후보 이름
    public static string SuggestNewDeckName()
    {
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            var t_candidate = $"덱 {t_i + 1}";

            bool t_used = false;
            for (int t_j = 0; t_j < SLOT_COUNT; t_j++)
            {
                if (s_names[t_j] != t_candidate) continue;

                t_used = true;
                break;
            }

            if (!t_used) return t_candidate;
        }

        return "덱 1";
    }

    // 덱 대표 이미지 키(범위 밖 조회는 빈 문자열)
    public static string GetImageKey(int _index)
        => _index >= 0 && _index < SLOT_COUNT ? (s_imageKeys[_index] ?? "") : "";

    public static void SetImageKey(int _index, string _key)
    {
        if (_index < 0 || _index >= SLOT_COUNT) return;

        s_imageKeys[_index] = _key ?? "";
    }

    public static bool IsSlotValid(int _index)
        => s_slots[_index] != null && s_slots[_index].Count == DECK_SIZE && s_slots[_index].All(d => d != null);

    public static bool HasAnyValidSlot()
    {
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
            if (IsSlotValid(t_i)) return true;

        return false;
    }

    // 카드 목록을 슬롯용 덱으로 정규화(null·중복 제거 후 정확히 DECK_SIZE장)
    public static bool TryBuildDeck(IEnumerable<CardData> _source, out List<CardData> _deck)
    {
        _deck = new List<CardData>(DECK_SIZE);

        if (_source == null) return false;

        foreach (CardData t_card in _source)
        {
            if (_deck.Count >= DECK_SIZE) break;
            if (t_card == null || _deck.Contains(t_card)) continue;

            _deck.Add(t_card);
        }

        return _deck.Count == DECK_SIZE;
    }

    // 같은 카드 구성의 저장 슬롯 찾기(편성 순서 무관)
    public static bool TryFindSlot(IEnumerable<CardData> _source, out int _index)
    {
        _index = -1;

        if (!TryBuildDeck(_source, out List<CardData> t_deck)) return false;

        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            if (!IsSlotValid(t_i)) continue;

            var t_slot = s_slots[t_i];

            bool t_same = true;
            for (int t_j = 0; t_j < t_deck.Count && t_same; t_j++)
                t_same = t_slot.Contains(t_deck[t_j]);

            if (!t_same) continue;

            _index = t_i;
            return true;
        }

        return false;
    }

    // 신규 덱을 목록 맨 앞에 삽입(이름·이미지키까지 한 번에 저장)
    public static bool TryInsertFront(IEnumerable<CardData> _deck, string _name, string _imageKey, out int _index)
    {
        _index = -1;

        if (!CanReorder()) return false;
        if (IsFull) return false;

        var t_cards = _deck != null ? _deck.Where(c => c != null).ToList() : new List<CardData>();
        if (t_cards.Count != DECK_SIZE) return false;

        int t_count = DeckCount;
        for (int t_i = t_count - 1; t_i >= 0; t_i--)
            CopySlot(t_i, t_i + 1);

        ClearSlot(0);
        s_slots[0]     = t_cards;
        s_names[0]     = _name ?? "";
        s_imageKeys[0] = _imageKey ?? "";

        SaveAll();
        _index = 0;
        return true;
    }

    // 덱 삭제 후 뒤 덱을 앞으로 당겨 구멍 제거
    public static bool TryDeleteAt(int _index)
    {
        if (!CanReorder()) return false;

        if (_index < 0 || _index >= SLOT_COUNT || !IsSlotValid(_index)) return false;

        int t_count = DeckCount;
        if (_index >= t_count) return false;

        for (int t_i = _index; t_i < t_count - 1; t_i++)
            CopySlot(t_i + 1, t_i);

        ClearSlot(t_count - 1);

        SaveAll();
        return true;
    }

    public static List<CardData> Load(int _index) => s_slots[_index] ?? new List<CardData>();

    // 카드 전체 목록 등록(LoadFromSave 전에 반드시 호출)
    public static void SetCardRegistry(IEnumerable<CardData> _cards)
        => s_registry = _cards.ToList();

    // 세이브에 덱이 하나라도 들어 있는지(메모리 아님).
    // 이관 전 구 세이브도 "덱 있음"으로 읽어야 한다 — 아니면 기존 유저에게 스타터덱이 다시 지급된다.
    public static bool HasAnySavedDeck()
    {
        var t_slots = NormalizedSlots();
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
            if (t_slots[t_i].cardIds.Length > 0 || t_slots[t_i].cardKeys.Length > 0) return true;

        return false;
    }

    // 세이브의 덱 노드를 메모리로 복원(DataSaveManager.Load 이후 호출)
    public static void LoadFromSave(bool _allowLegacyMigration = true)
    {
        if (_allowLegacyMigration) TryMigrateLegacyFile();
        s_loaded = true;

        var t_slots = NormalizedSlots();
        bool t_migrated = MigrateLegacyCardKeys(t_slots);

        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            var t_slot = t_slots[t_i];

            s_names[t_i]     = t_slot.name;
            s_imageKeys[t_i] = t_slot.imageKey;

            if (s_registry == null) continue;

            s_slots[t_i] = t_slot.cardIds
                .Select(id => s_registry.FirstOrDefault(c => c != null && CardCatalog.IdOf(c) == id))
                .Where(c => c != null)
                .ToList();
        }

        if (s_registry == null) return;

        // 압축이 없어도 이관분은 반드시 내려써야 한다 — 안 그러면 다음 부트에 같은 이관을 또 한다.
        if (Compact() || t_migrated) SaveAll();
    }

    /// <summary>구 세이브의 카드 이름 배열을 번호 배열로 옮긴다(슬롯당 1회). 카탈로그 미준비면 미룬다 —
    /// 여기서 잘못 비우면 덱이 통째로 사라지므로 이관에 성공한 슬롯만 이름을 지운다.</summary>
    static bool MigrateLegacyCardKeys(DeckSlotSaveData[] _slots)
    {
        if (!CardCatalog.IsReady) return false;

        bool t_changed = false;
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            var t_slot = _slots[t_i];
            if (t_slot.cardKeys.Length == 0) continue;
            if (t_slot.cardIds.Length > 0) { t_slot.cardKeys = new string[0]; t_changed = true; continue; }

            t_slot.cardIds  = t_slot.cardKeys.Select(CardCatalog.LegacyIdOfName).Where(id => id > 0).ToArray();
            t_slot.cardKeys = new string[0];
            t_changed       = true;
        }
        return t_changed;
    }

    // 대상 슬롯만 세이브에 반영(나머지 슬롯은 저장된 값 보존)
    public static void SaveSlot(int _index, IEnumerable<CardData> _deck)
    {
        if (_index < 0 || _index >= SLOT_COUNT) return;

        Save(_index, _deck);
        WriteSlot(NormalizedSlots(), _index);
        DataSaveManager.Save();
    }

    static bool CanReorder()
    {
        if (s_loaded && s_registry != null) return true;

        Debug.LogWarning("[DeckSaveManager] LoadFromSave 미경유 또는 카드 레지스트리 미주입 — 순서 변경 거부(메모리·세이브 어긋남 방지). 부트 프리팹이 있는 씬에서 실행할 것.");
        return false;
    }

    static void Save(int _index, IEnumerable<CardData> _deck)
        => s_slots[_index] = new List<CardData>(_deck.Where(d => d != null));

    static void SaveAll()
    {
        if (!s_loaded)
        {
            Debug.LogWarning("[DeckSaveManager] LoadFromSave 미경유 — 전량 저장 거부(세이브 덱 전체 소실 방지). 부트 프리팹이 있는 씬에서 실행할 것.");
            return;
        }

        var t_slots = NormalizedSlots();
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
            WriteSlot(t_slots, t_i);

        DataSaveManager.Save();
    }

    // 원본을 지우지 않는 순수 복사(연쇄 이동에서 다음 이동의 원본이 살아 있어야 한다)
    static void CopySlot(int _from, int _to)
    {
        s_slots[_to]     = s_slots[_from];
        s_names[_to]     = s_names[_from];
        s_imageKeys[_to] = s_imageKeys[_from];
    }

    static void ClearSlot(int _index)
    {
        s_slots[_index]     = null;
        s_names[_index]     = "";
        s_imageKeys[_index] = "";
    }

    static bool Compact()
    {
        var t_saved = NormalizedSlots();

        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            if (t_saved[t_i].cardIds.Length != DECK_SIZE || IsSlotValid(t_i)) continue;

            Debug.LogWarning($"[DeckSaveManager] 슬롯 {t_i}의 카드 번호를 레지스트리에서 해석하지 못했다 — 압축·저장 보류(세이브 원본 보존). 카드 번호 변경·삭제를 확인할 것.");
            return false;
        }

        int  t_write   = 0;
        int  t_dropped = 0;
        bool t_changed = false;

        for (int t_read = 0; t_read < SLOT_COUNT; t_read++)
        {
            if (!IsSlotValid(t_read))
            {
                if (t_saved[t_read].cardIds.Length > 0) t_dropped++;
                continue;
            }

            if (t_read != t_write)
            {
                CopySlot(t_read, t_write);
                t_changed = true;
            }
            t_write++;
        }

        for (int t_i = t_write; t_i < SLOT_COUNT; t_i++)
        {
            if (t_saved[t_i].cardIds.Length > 0 || !string.IsNullOrEmpty(s_names[t_i]) || !string.IsNullOrEmpty(s_imageKeys[t_i]))
                t_changed = true;

            ClearSlot(t_i);
        }

        if (t_dropped > 0)
            Debug.LogWarning($"[DeckSaveManager] 유효 덱 {DECK_SIZE}장을 이루지 못한 슬롯 {t_dropped}개를 압축에서 제외했다.");

        return t_changed;
    }

    // 세이브 인스턴스를 제자리에서 정규화해 반환하므로 읽기·쓰기가 같은 배열을 본다
    static DeckSlotSaveData[] NormalizedSlots()
    {
        var t_deck = DataSaveManager.Data.deck;
        if (t_deck == null)
        {
            t_deck = new DeckSaveData();
            DataSaveManager.Data.deck = t_deck;
        }

        if (t_deck.slots == null || t_deck.slots.Length != SLOT_COUNT)
        {
            var t_resized = new DeckSlotSaveData[SLOT_COUNT];
            for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
                t_resized[t_i] = t_deck.slots != null && t_i < t_deck.slots.Length ? t_deck.slots[t_i] : null;

            t_deck.slots = t_resized;
        }

        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            var t_slot = t_deck.slots[t_i];
            if (t_slot == null) t_deck.slots[t_i] = t_slot = new DeckSlotSaveData();

            if (t_slot.name == null)     t_slot.name     = "";
            if (t_slot.cardIds == null)  t_slot.cardIds  = new int[0];
            if (t_slot.cardKeys == null) t_slot.cardKeys = new string[0];
            if (t_slot.imageKey == null) t_slot.imageKey = "";
        }

        return t_deck.slots;
    }

    static void WriteSlot(DeckSlotSaveData[] _slots, int _index)
    {
        var t_dst = _slots[_index];

        t_dst.name     = s_names[_index] ?? "";
        t_dst.cardIds  = s_slots[_index]?.Where(c => c != null).Select(c => CardCatalog.IdOf(c)).ToArray()
                         ?? new int[0];
        t_dst.cardKeys = new string[0];   // 이관 완료 슬롯은 구 필드를 비운 채로 유지한다
        t_dst.imageKey = s_imageKeys[_index] ?? "";
    }

    static void TryMigrateLegacyFile()
    {
        if (HasAnySavedDeck()) return;

        var t_path = Path.Combine(Application.persistentDataPath, LEGACY_FILE);

        try
        {
            if (!File.Exists(t_path)) return;

            LegacyMigrationPending = true;

            var t_legacy = JsonUtility.FromJson<LegacyFile>(File.ReadAllText(t_path));
            if (t_legacy?.slots == null)
            {
                Debug.LogWarning("[DeckSaveManager] 레거시 덱 파일에 슬롯이 없음 — 이관 없이 보관 처리.");
                LegacyMigrationPending = false;
                ArchiveLegacyFile(t_path);
                return;
            }

            var t_built = new DeckSlotSaveData[SLOT_COUNT];
            for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
            {
                var t_src = t_i < t_legacy.slots.Length ? t_legacy.slots[t_i] : null;
                t_built[t_i] = new DeckSlotSaveData
                {
                    name     = t_src?.slotName ?? "",
                    cardKeys = t_src?.cards?.Where(k => !string.IsNullOrEmpty(k)).ToArray() ?? new string[0],
                    imageKey = t_src?.imageKey ?? "",
                };
            }

            DataSaveManager.Data.deck.slots = t_built;
            DataSaveManager.Save();
            LegacyMigrationPending = false;

            ArchiveLegacyFile(t_path);
        }
        catch (Exception t_e)
        {
            Debug.LogWarning($"[DeckSaveManager] 레거시 덱 파일 이관 실패: {t_e.Message}");
        }
    }

    static void ArchiveLegacyFile(string _path)
    {
        try
        {
            var t_archive = Path.Combine(Application.persistentDataPath, LEGACY_ARCHIVE_FILE);
            if (File.Exists(t_archive)) File.Delete(t_archive);

            File.Move(_path, t_archive);
        }
        catch (Exception t_e)
        {
            Debug.LogWarning($"[DeckSaveManager] 레거시 덱 파일 보관 실패: {t_e.Message}");
        }
    }

    // 레거시 decks.json 파싱 전용
    [Serializable]
    class LegacySlot
    {
        public string slotName;
        public string[] cards;
        public string imageKey;
    }

    // 레거시 decks.json 루트
    [Serializable]
    class LegacyFile
    {
        public LegacySlot[] slots;
    }
}
