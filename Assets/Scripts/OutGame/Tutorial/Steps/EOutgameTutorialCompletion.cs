// 스텝이 "무엇으로 완료되는가" — 브리지는 스텝 타입을 모른 채 이 값으로 기다릴 신호를 고른다
public enum EOutgameTutorialCompletion
{
    Auto,       // 입력 없음(Enter가 실행·씬 전환·진행도 커밋까지 끝낸다)
    Click,
    PackOpen,
    Purchase,   // 클릭이 아니라 구매 "성공"이 완료
    Confirm,    // 화면 탭으로 넘기는 설명 스텝
}
