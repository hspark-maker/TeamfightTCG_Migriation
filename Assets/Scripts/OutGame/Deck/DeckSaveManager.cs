using System;
using System.Collections.Generic;
using System.IO;   // 레거시 decks.json 이관에만 쓴다(TryMigrateLegacyFile). 덱 영속화는 DataSaveManager 담당.
using System.Linq;
using UnityEngine;

// 덱 슬롯의 메모리 캐시. 영속화 진실원은 DataSaveManager.Data.deck 하나뿐이다.
//
// 압축 불변식 — 유효 덱은 항상 [0 .. DeckCount-1]을 연속 점유하고 [DeckCount .. SLOT_COUNT-1]은 전부 빈 칸이다.
// 인덱스가 작을수록 최근 덱(신규는 맨 앞 삽입, 삭제는 뒤를 앞으로 당김).
public static class DeckSaveManager
{
    public const int SLOT_COUNT = 6;
    public const int DECK_SIZE  = 6;

    const string LEGACY_FILE         = "decks.json";
    const string LEGACY_ARCHIVE_FILE = "decks_migrated.json";

    static readonly List<CardData>[] s_slots = new List<CardData>[SLOT_COUNT];
    static readonly string[] s_names = new string[SLOT_COUNT];
    // 덱 대표 이미지 키(DeckImageCatalog의 스프라이트 이름). 여기서는 스프라이트를 모르고 문자열만 다룬다.
    static readonly string[] s_imageKeys = new string[SLOT_COUNT];

    static IReadOnlyList<CardData> s_registry;

    // LoadFromSave를 거쳤는지. 전량 flush(SaveAll)가 미로드 상태로 세이브를 지우는 것을 막는 가드.
    static bool s_loaded;

    // 레거시 파일이 남아 있는데 이관을 끝내지 못한 상태. 이때 슬롯을 새로 쓰면 구 덱이 영영 묻힌다.
    public static bool LegacyMigrationPending { get; private set; }

    // 첫 무효 슬롯 앞까지가 곧 덱 개수. 전체를 세지 않는 건 불변식이 깨진 세이브에서 개수를 과대 보고해
    // 뒤쪽 덱을 덮어쓰는 사고를 막기 위해서다.
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

    // 저장값 그대로(빈 문자열 가능). 인덱스 파생 폴백은 표시 전용 GetDisplayName에만 둬야
    // 덱이 이동할 때 이름이 따라 변하지 않고 rename 판정이 순수 비교가 된다.
    public static string GetName(int _index) => s_names[_index] ?? "";
    public static void SetName(int _index, string _name) => s_names[_index] = _name;

    public static string GetDisplayName(int _index)
        => string.IsNullOrEmpty(s_names[_index]) ? $"덱 {_index + 1}" : s_names[_index];

    // 저장된 이름과 겹치지 않는 첫 후보(DeckImages.PickRandomKey의 미사용 우선 패턴과 같은 결).
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

    // 이미지 키는 표시 경로(DeckImages)가 슬롯 범위 밖까지 훑을 수 있어 이름과 달리 범위를 방어한다.
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

    // 임의의 카드 목록을 슬롯에 넣을 수 있는 덱으로 정규화한다(null·중복 제거 후 정확히 DECK_SIZE장).
    // 저작 원본(시나리오·팩 pool)을 슬롯에 넣는 쪽과 되찾는 쪽이 같은 규칙을 써야 좌표가 어긋나지 않는다.
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

    // 같은 카드 구성의 저장 슬롯을 찾는다(순서 무관 — 편성 순서만 바꾼 덱을 "다른 덱"으로 보면 중복 지급이 된다).
    // 원본을 그대로 받아 정규화까지 여기서 한다 — 호출측마다 정규화를 다시 짜면 지급과 조회가 갈린다.
    public static bool TryFindSlot(IEnumerable<CardData> _source, out int _index)
    {
        _index = -1;

        if (!TryBuildDeck(_source, out List<CardData> t_deck)) return false;

        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            // IsSlotValid가 "non-null 정확히 DECK_SIZE장"을 보장하고 t_deck도 중복이 없으므로,
            // 한 방향 포함 검사만으로 두 집합이 같다(장수가 같은데 부분집합이면 곧 동일 집합).
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

    // 신규 덱을 목록 맨 앞에 넣는다. name·imageKey를 인자로 받는 건 삽입이 끝나야 인덱스가 생기기 때문 —
    // 삽입 후 SetName/SetImageKey를 부르면 SaveAll이 이미 지나가 메모리에만 남는다.
    public static bool TryInsertFront(IEnumerable<CardData> _deck, string _name, string _imageKey, out int _index)
    {
        _index = -1;

        if (!CanReorder()) return false;
        if (IsFull) return false;

        // 미완성 덱이 맨 앞에 들어가면 DeckCount가 0으로 접혀 불변식이 즉시 깨진다(목록·개수 전멸).
        // 두 번 순회하지 않게 여기서 리스트로 확정해 검사와 저장에 함께 쓴다.
        var t_cards = _deck != null ? _deck.Where(c => c != null).ToList() : new List<CardData>();
        if (t_cards.Count != DECK_SIZE) return false;

        int t_count = DeckCount;   // 이동 중 DeckCount가 변하므로 시작 전에 캡처
        for (int t_i = t_count - 1; t_i >= 0; t_i--)
            CopySlot(t_i, t_i + 1);

        ClearSlot(0);
        s_slots[0]     = t_cards;
        s_names[0]     = _name ?? "";
        s_imageKeys[0] = _imageKey ?? "";

        SaveAll();   // 여러 칸이 함께 움직였다 — SaveSlot으로는 메모리와 세이브가 어긋난다
        _index = 0;
        return true;
    }

    // 삭제 후 뒤 덱을 앞으로 당겨 구멍을 없앤다(압축 불변식 유지).
    public static bool TryDeleteAt(int _index)
    {
        if (!CanReorder()) return false;

        if (_index < 0 || _index >= SLOT_COUNT || !IsSlotValid(_index)) return false;

        int t_count = DeckCount;
        // 불변식이 깨진 세이브(앞에 구멍)에서는 당기기가 엉뚱한 칸을 지운다 — 손대지 않는다.
        if (_index >= t_count) return false;

        for (int t_i = _index; t_i < t_count - 1; t_i++)
            CopySlot(t_i + 1, t_i);

        ClearSlot(t_count - 1);

        SaveAll();
        return true;
    }

    public static List<CardData> Load(int _index) => s_slots[_index] ?? new List<CardData>();

    // 카드 전체 목록 등록 (LoadFromSave 전에 반드시 호출)
    public static void SetCardRegistry(IEnumerable<CardData> _cards)
        => s_registry = _cards.ToList();

    // 세이브에 덱이 하나라도 들어 있는지(메모리 아님). 스타터덱 지급·레거시 이관의 공통 판정 기준이다 —
    // IsSlotValid(6장 완성)로 판정하면 불완전 덱만 있는 세이브를 "없음"으로 보고 덮어쓰게 된다.
    public static bool HasAnySavedDeck()
    {
        var t_slots = NormalizedSlots();
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
            if (t_slots[t_i].cardKeys.Length > 0) return true;

        return false;
    }

    // 아웃게임 세이브의 덱 노드를 메모리로 복원한다. DataSaveManager.Load 이후에 호출할 것.
    public static void LoadFromSave()
    {
        TryMigrateLegacyFile();
        s_loaded = true;

        var t_slots = NormalizedSlots();
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            var t_slot = t_slots[t_i];

            s_names[t_i]     = t_slot.name;
            s_imageKeys[t_i] = t_slot.imageKey;

            if (s_registry == null) continue;

            s_slots[t_i] = t_slot.cardKeys
                // 레지스트리에는 ID 보존용 빈 칸(null)이 섞일 수 있다 — 키 접근 전에 걸러야 NRE가 안 난다.
                .Select(k => s_registry.FirstOrDefault(c => c != null && CardCatalog.KeyOf(c) == k))
                .Where(c => c != null)
                .ToList();
        }

        // 구멍이 뚫린 구 세이브는 읽는 시점에 불변식을 세워야 안전하다(쓰기 경로만 지키면 첫 삽입에 뒤쪽 덱이 덮인다).
        // 레지스트리 미주입이면 전 슬롯이 무효로 보여 압축이 세이브의 덱을 통째로 지우므로 반드시 가드한다.
        if (s_registry != null && Compact()) SaveAll();
    }

    // 대상 슬롯만 세이브에 반영한다(나머지 슬롯은 저장된 값 보존).
    // 미로드 상태에서 부르면 그 슬롯의 이름·이미지키는 빈 값으로 덮이지만 다른 슬롯은 온전하다.
    public static void SaveSlot(int _index, IEnumerable<CardData> _deck)
    {
        if (_index < 0 || _index >= SLOT_COUNT) return;

        Save(_index, _deck);
        WriteSlot(NormalizedSlots(), _index);
        DataSaveManager.Save();
    }

    // 순서를 바꿔도 되는 상태인지. 미로드면 SaveAll이 거부돼 메모리만 재배열된 반쪽 상태가 되고,
    // 레지스트리 미주입이면 빈 메모리가 세이브의 덱 전체를 덮는다 — 메모리를 건드리기 전에 확인한다.
    static bool CanReorder()
    {
        if (s_loaded && s_registry != null) return true;

        Debug.LogWarning("[DeckSaveManager] LoadFromSave 미경유 또는 카드 레지스트리 미주입 — 순서 변경 거부(메모리·세이브 어긋남 방지). 부트 프리팹이 있는 씬에서 실행할 것.");
        return false;
    }

    // 메모리 슬롯만 갱신한다(영속화는 SaveSlot/SaveAll). 불변식을 우회한 임의 슬롯 쓰기를 막으려 외부에 열지 않는다.
    static void Save(int _index, IEnumerable<CardData> _deck)
        => s_slots[_index] = new List<CardData>(_deck.Where(d => d != null));

    // 메모리 6슬롯 전량 flush. 미로드 상태면 빈 메모리가 세이브의 덱 전체를 지우므로 거부한다.
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

    // 3배열을 개별로 만지면 하나를 빠뜨렸을 때 이름·이미지가 다른 덱에 붙은 채 세이브까지 굳는다.
    // 순수 복사다 — 원본을 지우면 연쇄 이동에서 다음 이동의 원본이 먼저 사라진다.
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

    // 유효 슬롯을 기존 오름차순 그대로 앞으로 당겨 구멍을 없앤다(이동이 있었을 때만 true → 불필요한 디스크 쓰기 방지).
    // 구 세이브엔 생성 시각이 없어 "최근 순"은 복원할 수 없다 — 유저가 보던 순서를 그대로 보존한다.
    static bool Compact()
    {
        var t_saved = NormalizedSlots();

        // 세이브엔 키가 온전한데 레지스트리에서 해석만 실패한 슬롯(카드 SO 개명·삭제)이 있으면 손을 뗀다 —
        // WriteSlot이 메모리 기준이라 이 상태로는 어떤 flush든 그 덱의 키를 영구히 잃는다.
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            if (t_saved[t_i].cardKeys.Length != DECK_SIZE || IsSlotValid(t_i)) continue;

            Debug.LogWarning($"[DeckSaveManager] 슬롯 {t_i}의 카드 키를 레지스트리에서 해석하지 못했다 — 압축·저장 보류(세이브 원본 보존). 카드 SO 이름 변경·삭제를 확인할 것.");
            return false;
        }

        int  t_write   = 0;
        int  t_dropped = 0;
        bool t_changed = false;

        for (int t_read = 0; t_read < SLOT_COUNT; t_read++)
        {
            if (!IsSlotValid(t_read))
            {
                // 위 가드를 지났으므로 여기 걸리는 건 키가 DECK_SIZE에 못 미치는 슬롯뿐 — 버리되 조용히 사라지진 않게 남긴다.
                if (t_saved[t_read].cardKeys.Length > 0) t_dropped++;
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
            if (t_saved[t_i].cardKeys.Length > 0 || !string.IsNullOrEmpty(s_names[t_i]) || !string.IsNullOrEmpty(s_imageKeys[t_i]))
                t_changed = true;

            ClearSlot(t_i);
        }

        if (t_dropped > 0)
            Debug.LogWarning($"[DeckSaveManager] 유효 덱 {DECK_SIZE}장을 이루지 못한 슬롯 {t_dropped}개를 압축에서 제외했다.");

        return t_changed;
    }

    // 세이브의 덱 노드를 SLOT_COUNT 길이·null 없는 상태로 정규화한다(누락·초과 방어).
    // 슬롯 객체뿐 아니라 내부 필드까지 채워, 이 배열을 받는 쪽은 null 검사를 하지 않아도 된다.
    // 세이브 인스턴스를 제자리에서 고쳐 반환하므로 읽기·쓰기가 같은 배열을 본다.
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
            if (t_slot.cardKeys == null) t_slot.cardKeys = new string[0];
            if (t_slot.imageKey == null) t_slot.imageKey = "";
        }

        return t_deck.slots;
    }

    // 메모리 슬롯 하나를 세이브 값 객체로 옮긴다(파일 기록은 호출자가).
    static void WriteSlot(DeckSlotSaveData[] _slots, int _index)
    {
        var t_dst = _slots[_index];

        t_dst.name     = s_names[_index] ?? "";
        t_dst.cardKeys = s_slots[_index]?.Where(c => c != null).Select(c => CardCatalog.KeyOf(c)).ToArray()
                         ?? new string[0];
        t_dst.imageKey = s_imageKeys[_index] ?? "";
    }

    // 레거시 decks.json → 아웃게임 세이브 1회 이관(한시 코드). 세이브에 덱이 하나도 없을 때만 시도하므로
    // 이관 뒤에는 조건이 다시 참이 되지 않는다 — 별도 완료 플래그를 두지 않는 이유.
    static void TryMigrateLegacyFile()
    {
        if (HasAnySavedDeck()) return;

        var t_path = Path.Combine(Application.persistentDataPath, LEGACY_FILE);

        try
        {
            if (!File.Exists(t_path)) return;

            // 여기부터 커밋 전까지는 "구 덱이 파일에만 있는" 구간 — 이 사이에 슬롯을 새로 쓰면 구 덱이 묻힌다.
            LegacyMigrationPending = true;

            var t_legacy = JsonUtility.FromJson<LegacyFile>(File.ReadAllText(t_path));
            if (t_legacy?.slots == null)
            {
                // 옮길 내용이 없다. 그대로 두면 Pending이 계속 켜져 신규 덱 지급을 영영 막는다.
                Debug.LogWarning("[DeckSaveManager] 레거시 덱 파일에 슬롯이 없음 — 이관 없이 보관 처리.");
                LegacyMigrationPending = false;
                ArchiveLegacyFile(t_path);
                return;
            }

            // 세이브를 제자리 수정하면 도중 예외 시 반쪽 상태가 남고 다른 도메인의 Save()가 그것을 영속화한다.
            // 임시 배열에 다 채운 뒤 마지막에 통째로 갈아끼워 커밋을 원자화한다.
            var t_built = new DeckSlotSaveData[SLOT_COUNT];
            for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
            {
                var t_src = t_i < t_legacy.slots.Length ? t_legacy.slots[t_i] : null;
                t_built[t_i] = new DeckSlotSaveData
                {
                    name     = t_src?.slotName ?? "",
                    // 구 SaveToFile은 null 카드를 ""로 기록했다 — 그대로 옮기면 조용히 줄어든 덱이 된다.
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
            // 이관 실패로 부트가 죽지 않게 — 기존 세이브는 건드리지 않고 계속 진행한다.
            // Pending이 켜진 채 남아 이번 부트의 신규 덱 지급을 막는다(구 덱을 덮어쓰지 않게).
            Debug.LogWarning($"[DeckSaveManager] 레거시 덱 파일 이관 실패: {t_e.Message}");
        }
    }

    // 이관 원본은 지우지 않고 옆에 남긴다(세이브가 어긋났을 때 손으로 복구할 수 있게).
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
            // rename 실패해도 이관된 덱이 세이브에 남아 위 가드가 재이관을 막는다.
            Debug.LogWarning($"[DeckSaveManager] 레거시 덱 파일 보관 실패: {t_e.Message}");
        }
    }

    // 레거시 decks.json 파싱 전용(한시). 이관이 끝나면 쓰이지 않는다.
    [Serializable]
    class LegacySlot
    {
        public string slotName;
        public string[] cards;
        public string imageKey;
    }

    [Serializable]
    class LegacyFile
    {
        public LegacySlot[] slots;
    }
}
