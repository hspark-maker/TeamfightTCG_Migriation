// 스텝이 "무엇으로 완료되는가" — 브리지는 스텝 타입을 모른 채 이 값으로 기다릴 신호를 고른다
public enum EOutgameTutorialCompletion
{
    Auto,       // 입력 없음(Enter가 실행·씬 전환·진행도 커밋까지 끝낸다)
    Click,
    PackOpen,
    Purchase,   // 클릭이 아니라 구매 "성공"이 완료
    Confirm,     // 화면 탭으로 넘기는 설명 스텝
    AlbumInsert, // 도감 삽입 세션의 종료가 완료
    Enhance,     // 클릭이 아니라 강화 "성공"이 완료(실패는 비용만 쓰고 그 자리에 남는다)
    RankEffect,  // 로비 랭크 연출이 끝나는 것이 완료(입력 없음 — 안내가 그 연출 위에 겹치지 않게 기다린다)
    LobbyReturn, // 열려 있던 오버레이가 모두 닫혀 로비 표면이 드러나는 것이 완료
    CardGain,    // 로비 획득 연출(카드 비행)이 끝나는 것이 완료 — RankEffect와 같은 이유로 기다린다
}
