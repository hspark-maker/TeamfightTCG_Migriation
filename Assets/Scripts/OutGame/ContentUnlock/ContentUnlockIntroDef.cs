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
}

/// <summary>공통 해금 소개의 이름·설명·아이콘 저작.</summary>
[Serializable]
public sealed class ContentUnlockIntroDef
{
    public EContentUnlockIntro content;
    public string contentName;
    [TextArea] public string description;
    public Sprite icon;

    /// <summary>소개에 대응하는 실제 콘텐츠 해금 키. 랭크전 진입은 기존 온보딩이 담당한다.</summary>
    public static string KeyOf(EContentUnlockIntro _content) => _content switch
    {
        EContentUnlockIntro.Mission => ContentUnlockManager.MISSION,
        EContentUnlockIntro.Adventure => ContentUnlockManager.ADVENTURE,
        EContentUnlockIntro.Roulette => ContentUnlockManager.ROULETTE,
        _ => null,
    };
}
