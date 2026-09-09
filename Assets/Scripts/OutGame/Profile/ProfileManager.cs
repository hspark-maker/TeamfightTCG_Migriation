using System;
using System.Collections.Generic;
using UnityEngine;

// 내 프로필(닉네임·아바타·프레임·감정표현 장착)의 static 단일 창구
public static class ProfileManager
{
    // 새로 입력하는 이름의 상한이다. 이미 저장된 이름에는 적용하지 않는다(RestoreNickname).
    public const int    NICKNAME_MAX_LENGTH = 8;
    public const string DEFAULT_NICKNAME    = "나";

    // 프로필 변경 통지 — UI 갱신용
    public static event Action OnChanged;

    public static ProfileConfig Config { get; private set; }
    // 프로필 편집은 로비에서 열리는데 그 자리엔 EmoteDirector(전투 씬 전용)가 없어 여기도 표를 든다.
    public static EmoteCatalog EmoteCatalog { get; private set; }

    // 비속어 판정기. 미주입이면 아무것도 막지 않는다.
    static INicknameFilter s_nicknameFilter;

    public static string Nickname { get; private set; } = DEFAULT_NICKNAME;
    public static string AvatarId { get; private set; } = string.Empty;
    public static string FrameId  { get; private set; } = string.Empty;
    // 길이는 항상 SLOT_COUNT, index가 곧 선택 표의 칸이다. 0은 빈 칸이라 조회하면 실패한다.
    static List<int> s_emoteIds = new List<int>();
    public static IReadOnlyList<int> EmoteIds => s_emoteIds;

    // 그림은 null을 허용한다 — 뷰가 프리팹에 저작된 스프라이트를 그대로 유지한다.
    public static Sprite AvatarLarge => Config != null && Config.TryGetAvatar(AvatarId, out var t_entry) ? t_entry.large : null;
    public static Sprite AvatarSmall => Config != null && Config.TryGetAvatar(AvatarId, out var t_entry) ? t_entry.SmallOrLarge : null;
    public static Sprite Frame       => Config != null && Config.TryGetFrame(FrameId,   out var t_entry) ? t_entry.sprite : null;

    // 프레임 스프라이트는 흰 마스터라 이 색으로 틴트해야 저작한 색이 나온다. 미조회면 white(틴트 안 함).
    public static Color FrameColor  => Config != null && Config.TryGetFrame(FrameId,   out var t_entry) ? t_entry.color : Color.white;

    // 얼굴 뒤 판은 모든 아바타가 같은 흰 마스터를 쓰고 색만 아바타별로 갈린다.
    public static Sprite AvatarPlate => Config != null ? Config.AvatarPlate : null;
    public static Color  AvatarColor => Config != null && Config.TryGetAvatar(AvatarId, out var t_entry) ? t_entry.color : Color.white;

    // 판·얼굴·링 한 벌. 프로필을 그리는 화면은 값을 따로 집지 말고 이걸 받아라.
    public static ProfileLook CurrentLook => Config != null ? Config.LookOf(AvatarId, FrameId) : new ProfileLook(null, Color.white, null, null, Color.white);

    static ProfileSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.Profile == null) t_data.Profile = new ProfileSaveData();
            return t_data.Profile;
        }
    }

    static string DefaultAvatarId => Config != null ? Config.DefaultAvatarId : string.Empty;
    static string DefaultFrameId  => Config != null ? Config.DefaultFrameId  : string.Empty;

    // 초기화에서 1회 주입. 미배선(null)이면 그림이 전부 null로 떨어진다(화면은 저작값 유지).
    public static void SetConfig(ProfileConfig _config)
    {
        Config = _config;
    }

    // 초기화에서 1회 주입. 미배선(null)이면 장착 목록이 전부 빈 칸으로 떨어지고 감정표현이 뜨지 않는다.
    public static void SetEmoteCatalog(EmoteCatalog _catalog)
    {
        EmoteCatalog = _catalog;
    }

    // 초기화에서 1회 주입. 미주입(null)이면 어떤 이름도 막히지 않는다 — 판정기가 없는 씬에서도 편집이 서게.
    public static void SetNicknameFilter(INicknameFilter _filter)
    {
        s_nicknameFilter = _filter;
    }

    /// <summary>이 이름을 닉네임으로 쓸 수 없으면 true. 커밋 전 검사는 전부 여기를 지난다.</summary>
    public static bool IsNicknameBlocked(string _raw)
    {
        if (s_nicknameFilter == null || string.IsNullOrWhiteSpace(_raw)) return false;

        // 자르기 전 원문으로 묻는다 — 8자로 정제해서 물으면 상한 뒤로 밀린 금칙어를 놓친다.
        return s_nicknameFilter.IsBlocked(_raw.Trim());
    }

    // 초기화에서 SetConfig 이후 1회 호출. 클라우드 세이브 채택 뒤여야 세이브가 반영된다.
    // 세이브가 비었거나 Config에서 사라진 id면 기본값으로 떨어진다 — 폴백 결과를 슬롯에 되쓰지는
    // 않는다(초기화마다 디스크 쓰기가 생긴다). 다음 Apply()가 정리한다.
    public static void Init()
    {
        ProfileSaveData t_slot = Slot;

        // 닉네임은 서버가 계정 문서를 만들 때 낱말표에서 뽑아 굳힌다(functions/src/profile/generateNickname.ts).
        // 여기 DEFAULT_NICKNAME 폴백은 그 전에 만들어진 옛 계정(nickname=null)만 받는 안전망이다.
        Nickname = RestoreNickname(t_slot.Nickname);
        AvatarId = IsKnownAvatar(t_slot.AvatarId) ? t_slot.AvatarId : DefaultAvatarId;
        FrameId  = IsKnownFrame(t_slot.FrameId)   ? t_slot.FrameId  : DefaultFrameId;
        s_emoteIds = BuildLoadout(t_slot.EmoteIds);

        // 로비는 세이브 의존 설치보다 먼저 그려진다 — 통지가 없으면 프로필 버튼이 기본값으로 굳는다.
        // 초기화 한복판이라 구독자 예외를 여기서 흘리면 나머지 설치가 통째로 중단된다.
        try { OnChanged?.Invoke(); }
        catch (Exception t_exception) { Debug.LogException(t_exception); }
    }

    // 프로필 4값 일괄 반영(커밋이 한 번이라 저장·통지도 한 번). 모르는 아바타·프레임 ID는 기존값을 남기고,
    // 감정표현만은 칸 자리를 지켜야 해서 무시가 아니라 정규화한다(BuildLoadout).
    public static void Apply(string _nickname, string _avatarId, string _frameId, IReadOnlyList<int> _emoteIds)
    {
        string t_nickname = ResolveNickname(_nickname);
        string t_avatarId = IsKnownAvatar(_avatarId) ? _avatarId : AvatarId;
        string t_frameId  = IsKnownFrame(_frameId)   ? _frameId  : FrameId;
        List<int> t_emoteIds = BuildLoadout(_emoteIds);

        if (t_nickname == Nickname && t_avatarId == AvatarId && t_frameId == FrameId
            && LoadoutsEqual(t_emoteIds, s_emoteIds)) return;

        Nickname = t_nickname;
        AvatarId = t_avatarId;
        FrameId  = t_frameId;
        s_emoteIds = t_emoteIds;

        Persist();
        OnChanged?.Invoke();
    }

    // 해금 훅 자리 — 아바타 해금이 붙으면 여기서 소유 여부를 판정한다(지금은 전부 열려 있다).
    public static bool IsAvatarOwned(string _id) => true;

    // 해금 훅 자리 — 프레임 해금이 붙으면 여기서 소유 여부를 판정한다(지금은 전부 열려 있다).
    public static bool IsFrameOwned(string _id) => true;

    // 해금 훅 자리 — 감정표현 해금이 붙으면 여기서 소유 여부를 판정한다(지금은 전부 열려 있다).
    public static bool IsEmoteOwned(int _id) => true;

    // Apply가 받은 이름을 실제로 굳힐 값으로 바꾼다. 세 갈래다.
    //
    // 지금 이름을 그대로 되돌려준 경우를 먼저 거르는 것이 요점이다 — 프로필 편집은 이름을 안 고쳐도 네 축을
    // 통째로 넘기고, 요약 뷰의 입력칸도 지금 이름을 담은 채 열린다(TMP는 characterLimit으로 담긴 글자를
    // 자르지 않는다). 여기서 걸러내지 않으면 아바타만 바꿔 저장해도 상한을 낮추기 전에 굳은 긴 이름이
    // 8자로 잘려 영속되어, RestoreNickname이 지키려던 것이 저장 한 번에 무너진다.
    static string ResolveNickname(string _raw)
    {
        if (_raw == Nickname) return Nickname;

        // 막힌 이름은 닉네임 축만 지금 값으로 남기고 나머지 세 축은 정상 반영한다. 호출부가 IsNicknameBlocked로
        // 먼저 걸러 팝업까지 띄우므로 이 갈래는 검사를 잊은 호출부를 받는 안전망이다.
        if (IsNicknameBlocked(_raw)) return Nickname;

        return SanitizeNickname(_raw);
    }

    // 저장된 이름을 그대로 되살린다 — 길이를 재지 않는 것이 요점이다. 상한을 낮추기 전에 만들어진 계정이나
    // 서버가 옛 상한으로 발급한 이름을 여기서 자르면, 세이브에는 긴 이름이 남은 채 화면만 잘려 둘이 갈린다.
    static string RestoreNickname(string _stored)
    {
        if (string.IsNullOrWhiteSpace(_stored)) return DEFAULT_NICKNAME;

        return _stored.Trim();
    }

    // 앞뒤 공백 제거 → 길이 클램프 → 비면 기본 닉네임
    public static string SanitizeNickname(string _raw)
    {
        if (string.IsNullOrEmpty(_raw)) return DEFAULT_NICKNAME;

        string t_name = _raw.Trim();
        if (t_name.Length > NICKNAME_MAX_LENGTH) t_name = t_name.Substring(0, NICKNAME_MAX_LENGTH);

        return t_name.Length > 0 ? t_name : DEFAULT_NICKNAME;
    }

    // 세이브 복원(Init)과 사용자 선택(Apply)이 같은 판정을 쓰게 하는 자리 — 폴백만 호출부가 정한다.
    static bool IsKnownAvatar(string _id) => Config != null && Config.TryGetAvatar(_id, out _);

    static bool IsKnownFrame(string _id) => Config != null && Config.TryGetFrame(_id, out _);

    // 유효한 첫 등장과 슬롯 위치를 먼저 보존한다. 그 뒤 빈 칸만 풀의 미사용 ID로 채워야
    // 앞쪽의 누락 칸이 뒤쪽에 저장된 ID를 먼저 가져가 순서를 바꾸는 일이 없다.
    static List<int> BuildLoadout(IReadOnlyList<int> _source)
    {
        var t_result = new List<int>(global::EmoteCatalog.SLOT_COUNT);
        var t_used = new HashSet<int>();

        for (int t_i = 0; t_i < global::EmoteCatalog.SLOT_COUNT; t_i++)
        {
            int t_id = _source != null && t_i < _source.Count ? _source[t_i] : 0;
            if (EmoteCatalog == null || !EmoteCatalog.TryGet(t_id, out _) || !t_used.Add(t_id))
                t_id = 0;
            t_result.Add(t_id);
        }

        int t_poolIndex = 0;
        for (int t_i = 0; t_i < t_result.Count; t_i++)
        {
            if (t_result[t_i] != 0) continue;
            while (EmoteCatalog != null && t_poolIndex < EmoteCatalog.Count)
            {
                EmoteEntry t_entry = EmoteCatalog.PoolAt(t_poolIndex++);
                if (t_entry == null || t_entry.id <= 0 || !t_used.Add(t_entry.id)) continue;
                t_result[t_i] = t_entry.id;
                break;
            }
        }
        return t_result;
    }

    // 장착은 자리까지 같아야 같은 것이다 — 순서만 바꾼 편집도 저장할 것이 있다.
    static bool LoadoutsEqual(IReadOnlyList<int> _left, IReadOnlyList<int> _right)
    {
        if (_left == null || _right == null || _left.Count != _right.Count) return false;
        for (int t_i = 0; t_i < _left.Count; t_i++)
            if (_left[t_i] != _right[t_i]) return false;
        return true;
    }

    // 통지는 호출부(Apply)가 한다 — 저장과 통지를 겹쳐 부르지 않게.
    static void Persist()
    {
        ProfileSaveData t_slot = Slot;
        t_slot.Nickname = Nickname;
        t_slot.AvatarId = AvatarId;
        t_slot.FrameId  = FrameId;
        t_slot.EmoteIds = new List<int>(s_emoteIds);

        DataSaveManager.Save();
    }
}
