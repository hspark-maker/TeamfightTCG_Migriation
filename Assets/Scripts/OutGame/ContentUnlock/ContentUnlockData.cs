using System.Collections.Generic;
using UnityEngine;

/// <summary>튜토리얼과 독립된 콘텐츠 이용 조건·소개 저작물.</summary>
[CreateAssetMenu(fileName = "ContentUnlockConfig", menuName = "Card Battle/Content Unlock Config")]
public sealed class ContentUnlockData : ScriptableObject
{
    [Header("콘텐츠 해금 조건")]
    [Tooltip("FTUE·랭크·계정 레벨·가이드 미션 조건을 AND로 평가한다. 한번 해금한 콘텐츠는 다시 잠기지 않는다.")]
    public List<ContentUnlockDef> contentUnlocks = new List<ContentUnlockDef>();

    [Header("콘텐츠 해금 소개")]
    [Tooltip("소개 화면의 이름·설명·아이콘. 실제 이용 자격과 해금 조건은 위 목록에서 정한다.")]
    public List<ContentUnlockIntroDef> contentIntros = new List<ContentUnlockIntroDef>();

    /// <summary>소개 스텝이 참조하는 콘텐츠 표현을 찾는다.</summary>
    public bool TryGetContentIntro(EContentUnlockIntro _content, out ContentUnlockIntroDef _intro)
    {
        if (contentIntros != null)
            foreach (ContentUnlockIntroDef t_intro in contentIntros)
                if (t_intro != null && t_intro.content == _content)
                {
                    _intro = t_intro;
                    return true;
                }
        _intro = null;
        return false;
    }

}
