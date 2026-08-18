// 한국어 조사 선택. 데이터로 저작된 이름(재화·카드 등)을 문장에 끼울 때 조사가 어긋나지 않게 한다.
public static class KoreanText
{
    const int HANGUL_BASE = 0xAC00;   // '가'
    const int HANGUL_LAST = 0xD7A3;   // '힣'
    const int JONGSEONG_COUNT = 28;   // 받침 종류 수(없음 포함)

    /// <summary>주격 조사 — 받침이 있으면 "이", 없으면 "가".</summary>
    public static string Subject(string _word) => HasFinalConsonant(_word) ? "이" : "가";

    // 마지막 글자에 받침이 있는지. 한글이 아니면(숫자·영문) 받침 없음으로 본다.
    static bool HasFinalConsonant(string _word)
    {
        if (string.IsNullOrEmpty(_word)) return false;

        int t_code = _word[_word.Length - 1];
        if (t_code < HANGUL_BASE || t_code > HANGUL_LAST) return false;

        return (t_code - HANGUL_BASE) % JONGSEONG_COUNT != 0;
    }
}
