using System;
using System.Collections.Generic;
using UnityEngine;

// 프로필 아바타 한 칸.
[Serializable]
public class ProfileAvatarEntry
{
    [Tooltip("세이브에 그대로 들어가는 영구 키. 한 번 정하면 바꾸지 마라 — 그 아바타를 쓰던 유저가 기본값으로 돌아간다. " +
             "아트를 갈고 싶으면 id는 두고 아래 Sprite 슬롯만 교체하면 된다.")]
    public string id;

    [Tooltip("선택 화면에 보일 이름. 표시용이라 언제든 고쳐도 된다.")]
    public string displayName;

    [Tooltip("판 위에 얹을 얼굴. 프로필 화면·매칭 배너에 쓰는 큰 그림이다.")]
    public Sprite large;

    [Tooltip("로비 버튼 등 작은 자리에 쓰는 얼굴. 비워두면 large로 폴백된다(1사이즈만 있는 아바타 허용).")]
    public Sprite small;

    [Tooltip("얼굴 뒤 판의 색. 얼굴 스프라이트가 흰색인 것도 있어 판 색이 곧 그 아바타의 정체성이다.")]
    public Color color = Color.white;

    // 작은 자리용 얼굴. 미저작이면 큰 그림을 그대로 쓴다.
    public Sprite SmallOrLarge => small != null ? small : large;
}

// 프로필 프레임 한 칸.
[Serializable]
public class ProfileFrameEntry
{
    [Tooltip("세이브에 그대로 들어가는 영구 키. 한 번 정하면 바꾸지 마라 — 아트 교체는 아래 Sprite 슬롯으로 한다.")]
    public string id;

    [Tooltip("선택 화면에 보일 이름. 표시용이라 언제든 고쳐도 된다.")]
    public string displayName;

    [Tooltip("아바타 맨 뒤에 까는 프레임 원판. 앞의 얼굴 판이 가운데를 덮어 바깥 테두리만 링으로 드러난다 — " +
             "그래서 속이 뚫린 링이 아니라 얼굴 판보다 한 치수 큰 채워진 판을 넣어야 한다.")]
    public Sprite sprite;

    [Tooltip("스프라이트는 흰 마스터고 실제 색은 이 값이 정한다. 같은 마스터에 색만 달리해 프레임을 늘릴 수 있다. " +
             "흰색(기본)이면 스프라이트 원본 색 그대로.")]
    public Color color = Color.white;
}

// 아바타·프레임 마스터 표. 프로필 표시가 갈리는 화면은 전부 여기만 본다.
[CreateAssetMenu(fileName = "ProfileConfig", menuName = "Card Battle/Profile Config")]
public class ProfileConfig : ScriptableObject
{
    [Tooltip("모든 아바타가 공유하는 얼굴 뒤 판 마스터. 흰 마스터고 실제 색은 아바타별 color가 정한다. " +
             "이 스프라이트의 모양이 곧 얼굴이 잘리는 모양이다(뷰의 Mask가 이걸 쓴다) — 원형을 유지하려면 원판을 넣어라.")]
    [SerializeField] Sprite avatarPlate;

    [Tooltip("선택 화면 정렬 = 이 리스트 순서. 0번이 신규 유저의 기본 아바타다.")]
    [SerializeField] List<ProfileAvatarEntry> avatars = new List<ProfileAvatarEntry>();

    [Tooltip("선택 화면 정렬 = 이 리스트 순서. 0번이 신규 유저의 기본 프레임이다.")]
    [SerializeField] List<ProfileFrameEntry> frames = new List<ProfileFrameEntry>();

    Dictionary<string, ProfileAvatarEntry> m_avatarById;
    Dictionary<string, ProfileFrameEntry>  m_frameById;

    public Sprite AvatarPlate => avatarPlate;

    public IReadOnlyList<ProfileAvatarEntry> Avatars => avatars != null ? (IReadOnlyList<ProfileAvatarEntry>)avatars : Array.Empty<ProfileAvatarEntry>();
    public IReadOnlyList<ProfileFrameEntry>  Frames  => frames  != null ? (IReadOnlyList<ProfileFrameEntry>)frames  : Array.Empty<ProfileFrameEntry>();

    // 신규 유저·미저작 세이브가 떨어질 자리. 목록이 비면 빈 문자열(호출부가 null 그림으로 폴백).
    public string DefaultAvatarId => FirstIdOf(avatars != null && avatars.Count > 0 ? avatars[0]?.id : null);
    public string DefaultFrameId  => FirstIdOf(frames  != null && frames.Count  > 0 ? frames[0]?.id  : null);

    // 아바타·프레임을 한 벌로 조합하는 단일 지점. 조회 실패한 축만 null/white로 남고 나머지는 그대로 채운다.
    public ProfileLook LookOf(string _avatarId, string _frameId)
    {
        Sprite t_face       = null;
        Color  t_plateColor = Color.white;
        if (TryGetAvatar(_avatarId, out ProfileAvatarEntry t_avatar))
        {
            t_face       = t_avatar.large;
            t_plateColor = t_avatar.color;
        }

        Sprite t_ring      = null;
        Color  t_ringColor = Color.white;
        if (TryGetFrame(_frameId, out ProfileFrameEntry t_frame))
        {
            t_ring      = t_frame.sprite;
            t_ringColor = t_frame.color;
        }

        return new ProfileLook(avatarPlate, t_plateColor, t_face, t_ring, t_ringColor);
    }

    public bool TryGetAvatar(string _id, out ProfileAvatarEntry _entry)
    {
        _entry = null;
        if (string.IsNullOrEmpty(_id)) return false;

        if (m_avatarById == null) m_avatarById = BuildIndex(avatars, _e => _e.id);

        return m_avatarById.TryGetValue(_id, out _entry);
    }

    public bool TryGetFrame(string _id, out ProfileFrameEntry _entry)
    {
        _entry = null;
        if (string.IsNullOrEmpty(_id)) return false;

        if (m_frameById == null) m_frameById = BuildIndex(frames, _e => _e.id);

        return m_frameById.TryGetValue(_id, out _entry);
    }

    // 에디터에서 리스트를 고치면 캐시를 버린다 — 안 버리면 플레이 중 추가한 id가 조회되지 않는다.
    void OnValidate()
    {
        m_avatarById = null;
        m_frameById  = null;
    }

    static string FirstIdOf(string _id) => !string.IsNullOrEmpty(_id) ? _id : string.Empty;

    // 같은 id가 여러 줄이면 위쪽 줄이 이긴다(CurrencyLook과 같은 규약).
    static Dictionary<string, T> BuildIndex<T>(List<T> _entries, Func<T, string> _idOf) where T : class
    {
        var t_map = new Dictionary<string, T>();
        if (_entries == null) return t_map;

        for (int t_i = 0; t_i < _entries.Count; t_i++)
        {
            T t_entry = _entries[t_i];
            if (t_entry == null) continue;

            string t_id = _idOf(t_entry);
            if (string.IsNullOrEmpty(t_id)) continue;
            if (t_map.ContainsKey(t_id)) continue;

            t_map[t_id] = t_entry;
        }

        return t_map;
    }
}
