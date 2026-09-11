/// <summary>
/// 서버 전용 RankAiEncounter 표의 발행 계약. 생성기의 RankAiEncounter 타입과 이름을 분리한다.
/// 클라이언트 동기화에는 포함하지 않으며 에디터는 docs CSV를 직접 읽는다.
/// </summary>
public sealed class RankAiEncounterRow
{
    public int id;
    public int tierIndex;
    public string battleKind;
    public string deckId;
    public int level1;
    public int level2;
    public int level3;
    public int level4;
    public int level5;
    public int level6;
    public int limitBreak1;
    public int limitBreak2;
    public int limitBreak3;
    public int limitBreak4;
    public int limitBreak5;
    public int limitBreak6;
    public int highlightSlot;
}
