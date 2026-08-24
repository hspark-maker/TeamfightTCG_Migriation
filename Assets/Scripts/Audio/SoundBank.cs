using System.Collections.Generic;
using UnityEngine;

/// <summary>아웃게임 사건 하나에 배정된 소리 한 칸.</summary>
[System.Serializable]
public class SoundCueEntry
{
    [Tooltip("이 칸이 담당하는 사건.")]
    public EOutgameSound cue;

    [Tooltip("이 사건에 쓸 클립.\n\n" +
             "· 비워 두면 그 사건은 무음으로 넘어간다 — 오류가 아니다. 아직 소리를 정하지 않은 사건은 비워 두면 된다.\n" +
             "· 여러 개를 넣으면 재생할 때마다 그중 하나를 무작위로 고른다.")]
    public AudioClip[] clips;

    [Tooltip("이 사건만의 크기 보정.\n최종 볼륨 = 환경설정 SFX 볼륨 × SoundConfig의 sfxVolume × 이 값.")]
    [Range(0f, 1f)] public float volumeScale = 1f;
}

/// <summary>아웃게임 사건 → 소리 표. 사건을 추가할 때 코드가 아니라 이 에셋에 한 줄을 늘린다.</summary>
[CreateAssetMenu(fileName = "OutgameSoundBank", menuName = "Card Battle/Outgame Sound Bank")]
public class SoundBank : ScriptableObject
{
    [Tooltip("사건별 소리 칸.\n\n" +
             "· 같은 사건이 두 줄이면 위에 있는 줄이 이긴다.\n" +
             "· 표에 아예 없는 사건도 무음으로 넘어간다 — 모든 사건을 채워 넣어야 하는 것은 아니다.")]
    [SerializeField] List<SoundCueEntry> cues = new List<SoundCueEntry>();

    Dictionary<EOutgameSound, SoundCueEntry> lookup;

    /// <summary>_cue에 배정된 칸을 찾는다. 표에 없거나 클립이 비어 있으면 false.</summary>
    public bool TryGet(EOutgameSound _cue, out SoundCueEntry _entry)
    {
        BuildLookup();

        if (!this.lookup.TryGetValue(_cue, out _entry)) return false;

        return _entry != null && _entry.clips != null && _entry.clips.Length > 0;
    }

    void OnEnable()   => this.lookup = null;
    void OnValidate() => this.lookup = null;

    void BuildLookup()
    {
        if (this.lookup != null) return;

        this.lookup = new Dictionary<EOutgameSound, SoundCueEntry>(this.cues != null ? this.cues.Count : 0);
        if (this.cues == null) return;

        foreach (SoundCueEntry t_entry in this.cues)
        {
            if (t_entry == null || t_entry.cue == EOutgameSound.None) continue;
            if (this.lookup.ContainsKey(t_entry.cue)) continue;
            this.lookup[t_entry.cue] = t_entry;
        }
    }
}
