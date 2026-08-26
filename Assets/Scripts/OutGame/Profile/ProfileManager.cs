using System;
using UnityEngine;

// 내 프로필(닉네임·아바타·프레임)의 static 단일 창구
public static class ProfileManager
{
    public const int    NICKNAME_MAX_LENGTH = 12;
    public const string DEFAULT_NICKNAME    = "나";

    // 프로필 변경 통지 — UI 갱신용
    public static event Action OnChanged;

    static bool s_initialized;

    // 저장된 아바타·프레임 id를 Config가 몰라 기본값으로 떨어진 상태(그 원본 id를 덮지 않는다)
    static bool s_fellBackToDefault;

    public static ProfileConfig Config { get; private set; }

    public static string Nickname { get; private set; } = DEFAULT_NICKNAME;
    public static string AvatarId { get; private set; } = string.Empty;
    public static string FrameId  { get; private set; } = string.Empty;

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
            if (t_data.profile == null) t_data.profile = new ProfileSaveData();
            return t_data.profile;
        }
    }

    static string DefaultAvatarId => Config != null ? Config.DefaultAvatarId : string.Empty;
    static string DefaultFrameId  => Config != null ? Config.DefaultFrameId  : string.Empty;

    // 부트에서 1회 주입. 미배선(null)이면 그림이 전부 null로 떨어진다(화면은 저작값 유지).
    public static void SetConfig(ProfileConfig _config)
    {
        Config = _config;
    }

    // 부트에서 SetConfig 이후 1회 호출. DataSaveManager.LoadAsync() 뒤여야 세이브가 반영된다.
    // 세이브가 비었거나 Config에서 사라진 id면 기본값으로 떨어진다 — 폴백 결과를 슬롯에 되쓰지는
    // 않는다(부트마다 디스크 쓰기가 생긴다). 다음 Apply()가 정리한다.
    public static void Init()
    {
        ProfileSaveData t_slot = Slot;

        Nickname = string.IsNullOrEmpty(t_slot.nickname) ? DEFAULT_NICKNAME : SanitizeNickname(t_slot.nickname);
        AvatarId = IsKnownAvatar(t_slot.avatarId) ? t_slot.avatarId : DefaultAvatarId;
        FrameId  = IsKnownFrame(t_slot.frameId)   ? t_slot.frameId  : DefaultFrameId;

        s_fellBackToDefault = AvatarId != t_slot.avatarId || FrameId != t_slot.frameId;
        s_initialized = true;
    }

    // 프로필 3값 일괄 반영. 모르는 아바타·프레임 ID는 무시하고 기존값을 남긴다.
    public static void Apply(string _nickname, string _avatarId, string _frameId)
    {
        string t_nickname = SanitizeNickname(_nickname);
        string t_avatarId = IsKnownAvatar(_avatarId) ? _avatarId : AvatarId;
        string t_frameId  = IsKnownFrame(_frameId)   ? _frameId  : FrameId;

        if (t_nickname == Nickname && t_avatarId == AvatarId && t_frameId == FrameId) return;

        Nickname = t_nickname;
        AvatarId = t_avatarId;
        FrameId  = t_frameId;

        // 사용자가 직접 고른 값이라 이제 슬롯에 써도 된다(Config 미배선으로 인한 폴백이 아니다).
        s_fellBackToDefault = false;

        Persist();
        OnChanged?.Invoke();
    }

    // 해금 훅 자리 — 아바타 해금이 붙으면 여기서 소유 여부를 판정한다(지금은 전부 열려 있다).
    public static bool IsAvatarOwned(string _id) => true;

    // 해금 훅 자리 — 프레임 해금이 붙으면 여기서 소유 여부를 판정한다(지금은 전부 열려 있다).
    public static bool IsFrameOwned(string _id) => true;

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

    /// <summary>캐시를 세이브 슬롯에 반영만 한다(디스크 쓰기 없음).
    /// 미초기화면 기본값이 저장분을 덮고, 폴백 상태면 Config가 못 읽은 원본 id가 지워진다 — 둘 다 건너뛴다.</summary>
    internal static void FlushToData()
    {
        if (!s_initialized || s_fellBackToDefault) return;

        ProfileSaveData t_slot = Slot;
        t_slot.nickname = Nickname;
        t_slot.avatarId = AvatarId;
        t_slot.frameId  = FrameId;
    }

    // 통지는 호출부(Apply)가 한다 — 저장과 통지를 겹쳐 부르지 않게.
    static void Persist()
    {
        FlushToData();
        SaveTransaction.Request();
    }
}
