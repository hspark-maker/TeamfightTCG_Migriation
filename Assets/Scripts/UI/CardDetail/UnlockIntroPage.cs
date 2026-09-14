using System.Collections.Generic;

/// <summary>해금 설명 한 페이지의 표시 자료.</summary>
public readonly struct UnlockIntroPage
{
    public readonly string Title;
    public readonly string Body;
    public readonly IReadOnlyList<UnlockIntro> Intros;
    public readonly bool ShowCards;
    public readonly bool PlayDemo;

    public UnlockIntroPage(string title, string body, IReadOnlyList<UnlockIntro> intros = null,
        bool showCards = false, bool playDemo = false)
    {
        Title = title;
        Body = body;
        Intros = intros;
        ShowCards = showCards;
        PlayDemo = playDemo;
    }
}
