// 스텝이 "무엇으로 완료되는가". 브리지는 스텝 타입을 모른 채 이 값 하나로 기다릴 신호를 고른다.
public enum EOutgameTutorialCompletion
{
    Auto,       // 입력 없음. Enter가 실행·씬 전환·진행도 커밋까지 스스로 끝낸다
    Click,      // 앵커 클릭이 곧 완료
    PackOpen,   // 3D 팩 개봉 신호로 완료(딤을 못 뚫어 앵커 없이 배너만)
    Purchase,   // 구매 "성공" 신호로 완료(클릭은 골드 부족으로 실패할 수 있어 완료가 아니다)
}
