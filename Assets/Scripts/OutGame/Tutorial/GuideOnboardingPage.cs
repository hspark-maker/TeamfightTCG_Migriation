using System;
using UnityEngine;

public enum EGuideOnboardingPage { Concept, Effect, Demo, Cards }

/// <summary>해금 안내의 편집 가능한 한 단계.</summary>
[Serializable]
public sealed class GuideOnboardingPage
{
    public EGuideOnboardingPage kind;
    public string title;
    [TextArea(2, 5)] public string body;
    [Tooltip("이미 돌보미 덱이 활성화된 계정의 대체 문구. 비우면 본문을 그대로 쓴다.")]
    [TextArea(2, 5)] public string activeBody;
}
