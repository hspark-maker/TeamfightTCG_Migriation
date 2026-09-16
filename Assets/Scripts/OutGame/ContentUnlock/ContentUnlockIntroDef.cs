using System;
using UnityEngine;

/// <summary>이용 자격과 별개로 해금 소개에 표시하는 콘텐츠.</summary>
public enum EContentUnlockIntro
{
    None = 0,
    Ranked = 1,
    Mission = 2,
    Adventure = 3,
    Roulette = 4,
    CardEnhance = 5,
}

public enum EContentUnlockDestination
{
    Ranked = 0,
    DailyMission = 1,
    GuideMission = 2,
    Attendance = 3,
    Adventure = 4,
    Roulette = 5,
    Collection = 6,
}

[Serializable]
public sealed class ContentUnlockIntroItem
{
    public string name;
    public Sprite icon;
    public EContentUnlockDestination destination;
}

/// <summary>해금 소개 한 화면의 설명과 표시 항목.</summary>
[Serializable]
public sealed class ContentUnlockIntroDef
{
    public EContentUnlockIntro content;
    [TextArea] public string description;
    [Tooltip("표시 순서대로 등록한다. 현재 소개 화면은 1~3개를 지원한다. 이름은 제목에도 사용하며 도착 버튼은 중복할 수 없다.")]
    public ContentUnlockIntroItem[] items = Array.Empty<ContentUnlockIntroItem>();

    public bool TryValidate(out string _error)
    {
        _error = null;
        if (items == null || items.Length == 0 || items.Length > 3)
            _error = "해금 소개 항목은 1~3개여야 합니다.";
        else
            for (int t_i = 0; t_i < items.Length; t_i++)
            {
                var t_item = items[t_i];
                if (t_item == null || string.IsNullOrWhiteSpace(t_item.name) || t_item.icon == null
                    || !Enum.IsDefined(typeof(EContentUnlockDestination), t_item.destination))
                {
                    _error = "해금 소개 항목의 이름·아이콘·도착 버튼을 확인하세요.";
                    break;
                }
                for (int t_j = 0; t_j < t_i; t_j++)
                    if (items[t_j].destination == t_item.destination)
                        _error = "해금 소개 항목의 도착 버튼이 중복입니다.";
                if (_error != null) break;
            }
        return _error == null;
    }

    /// <summary>소개에 대응하는 실제 콘텐츠 해금 키. 랭크전 진입은 기존 온보딩이 담당한다.</summary>
    public static string KeyOf(EContentUnlockIntro _content) => _content switch
    {
        EContentUnlockIntro.Mission => ContentUnlockManager.MISSION,
        EContentUnlockIntro.Adventure => ContentUnlockManager.ADVENTURE,
        EContentUnlockIntro.Roulette => ContentUnlockManager.ROULETTE,
        EContentUnlockIntro.CardEnhance => ContentUnlockManager.CARD_ENHANCE,
        _ => null,
    };
}
